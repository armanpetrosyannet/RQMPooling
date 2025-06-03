using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMqPooling.Application.Messaging;

namespace RabbitMqPooling.Infrastructure.Messaging
{
    /// <summary>
    /// Thread-safe, auto-scaling connection and channel pool for RabbitMQ.
    /// </summary>
    public class RabbitMqConnectionPool : IRabbitMqConnectionPool, IDisposable
    {
        private class ConnectionPool
        {
            public IConnection Connection { get; }
            public ConcurrentBag<IChannel> Channels { get; }
            public SemaphoreSlim Semaphore { get; }
            public int MaxChannels { get; }
            public DateTime LastUsedUtc { get; set; }
            public int ChannelsInUse => MaxChannels - Semaphore.CurrentCount;

            public ConnectionPool(IConnection conn, int maxChannels)
            {
                Connection = conn;
                Channels = new ConcurrentBag<IChannel>();
                Semaphore = new SemaphoreSlim(maxChannels, maxChannels);
                MaxChannels = maxChannels;
                LastUsedUtc = DateTime.UtcNow;
            }
        }

        private readonly ILogger<RabbitMqConnectionPool> _logger;
        private readonly RabbitMqConnectionPoolOptions _options;
        private readonly ConnectionFactory _factory;
        private readonly int _minConnections;
        private readonly int _maxConnections;
        private readonly int _minChannelsPerConnection;
        private readonly int _maxChannelsPerConnection;
        private readonly TimeSpan _idleTimeout;
        private readonly object _syncRoot = new();
        private readonly List<ConnectionPool> _connectionPools = new();
        private readonly ConcurrentDictionary<IChannel, ConnectionPool> _channelToPool = new();
        // private readonly Timer _cleanupTimer;
        private bool _disposed = false;

        public RabbitMqConnectionPool(
            IOptions<RabbitMqConnectionPoolOptions> options,
            ILogger<RabbitMqConnectionPool> logger)
        {
            _options = options.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = "/",
                AutomaticRecoveryEnabled = false,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(30),
            };

            _minConnections = Math.Max(_options.MinConnections, 1);
            _maxConnections = Math.Max(_options.MaxConnections, _minConnections);
            _minChannelsPerConnection = Math.Max(_options.MinChannelsPerConnection, 1);
            _maxChannelsPerConnection = Math.Max(_options.MaxChannelsPerConnection, _minChannelsPerConnection);
            _idleTimeout = TimeSpan.FromSeconds(_options.IdleTimeoutSeconds > 0 ? _options.IdleTimeoutSeconds : 300);

            // Create the minimum number of connections on startup
            for (int i = 0; i < _minConnections; i++)
            {
                AddConnectionPoolAsync().GetAwaiter().GetResult();
            }

            // Timer for cleaning up idle connections (optional, left commented)
            //_cleanupTimer = new Timer(CleanupIdleConnections, null, _idleTimeout, _idleTimeout);
        }

        // Rent a channel from the pool. Blocks if all are busy.
        public async Task<IChannel> RentChannelAsync()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RabbitMqConnectionPool));

            while (true)
            {
                lock (_syncRoot)
                {
                    foreach (var pool in _connectionPools)
                    {
                        if (pool.Semaphore.Wait(0))
                        {
                            pool.LastUsedUtc = DateTime.UtcNow;
                            if (pool.Channels.TryTake(out var channel) && channel?.IsOpen == true)
                            {
                                _channelToPool[channel] = pool;
                                return channel;
                            }
                            else
                            {
                                // We must create a new channel
                                try
                                {
                                    // We'll create it outside the lock (below)
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Failed to create new channel");
                                    pool.Semaphore.Release();
                                }
                            }
                        }
                    }

                    // Not enough pools? Add more (up to max)
                    if (_connectionPools.Count < _maxConnections)
                    {
                        // We'll add it outside lock
                        break;
                    }
                }

                _logger.LogInformation("All channels in use, waiting for a free one...");
                await Task.Delay(15);
            }

            // If we got here, we need to either create a channel, or create a new pool
            // 1. Try to create a channel in an existing pool
            lock (_syncRoot)
            {
                foreach (var pool in _connectionPools)
                {
                    if (pool.ChannelsInUse < pool.MaxChannels)
                    {
                        try
                        {
                            var ch = pool.Connection.CreateChannelAsync().GetAwaiter().GetResult();
                            _channelToPool[ch] = pool;
                            return ch;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to create new channel");
                            pool.Semaphore.Release();
                        }
                    }
                }

                // 2. Or add new pool if possible
                if (_connectionPools.Count < _maxConnections)
                {
                    var newPool = AddConnectionPoolAsync().GetAwaiter().GetResult();
                    if (newPool.Semaphore.Wait(0))
                    {
                        newPool.LastUsedUtc = DateTime.UtcNow;
                        var ch = newPool.Connection.CreateChannelAsync().GetAwaiter().GetResult();
                        _channelToPool[ch] = newPool;
                        return ch;
                    }
                }
            }

            // Wait and retry
            await Task.Delay(15);
            return await RentChannelAsync();
        }

        // Return a channel to the pool.
        public void ReturnChannel(IChannel channel)
        {
            if (channel == null) return;
            if (_disposed)
            {
                try { channel.Dispose(); } catch { }
                return;
            }
            if (!_channelToPool.TryRemove(channel, out var pool))
            {
                _logger.LogWarning("Returned channel was not found in pool mapping. Disposing it.");
                try { channel.Dispose(); } catch { }
                return;
            }
            pool.LastUsedUtc = DateTime.UtcNow;
            if (channel.IsOpen)
            {
                pool.Channels.Add(channel);
            }
            else
            {
                try { channel.Dispose(); } catch { }
            }
            pool.Semaphore.Release();
        }

        // Internal: Add a new connection+pool to the list.
        private async Task<ConnectionPool> AddConnectionPoolAsync()
        {
            IConnection conn = null!;
            try
            {
                conn = await _factory.CreateConnectionAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create RabbitMQ connection");
                throw;
            }

            var pool = new ConnectionPool(conn, _maxChannelsPerConnection);
            conn.ConnectionShutdownAsync += async(s, e) =>
                _logger.LogWarning("RabbitMQ connection shutdown: {Reason}", e.ReplyText);
            conn.CallbackExceptionAsync += async (s, e) =>
                _logger.LogError(e.Exception, "RabbitMQ connection callback exception");
            lock (_syncRoot)
            {
                _connectionPools.Add(pool);

                // Pre-warm minimum number of channels
                for (int i = 0; i < _minChannelsPerConnection; i++)
                {
                    var ch = conn.CreateChannelAsync().GetAwaiter().GetResult();
                    pool.Channels.Add(ch);
                }
            }
            return pool;
        }

        // (CleanupIdleConnections not implemented for brevity)

        // Dispose: cleans everything up.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            //_cleanupTimer?.Dispose();
            lock (_syncRoot)
            {
                foreach (var pool in _connectionPools)
                {
                    while (pool.Channels.TryTake(out var ch))
                    {
                        try { ch.Dispose(); } catch { }
                    }
                    try { pool.Connection.Dispose(); } catch { }
                    try { pool.Semaphore.Dispose(); } catch { }
                }
                _connectionPools.Clear();
                _channelToPool.Clear();
            }
        }
    }
}
