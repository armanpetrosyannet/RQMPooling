using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RMQPooling.Abstractions;
using RMQPooling.Core;

namespace RMQPooling.Pooling
{
    public class RabbitMqConnectionPool : IRabbitMqConnectionPool
    {
        private class ConnectionBucket
        {
            public IConnection Connection { get; }
            public int MaxChannelsPerConnection { get; }
            public int OpenChannelCount;
            public DateTime LastUsedUtc { get; set; }

            public ConnectionBucket(IConnection connection, int maxChannels)
            {
                Connection = connection;
                MaxChannelsPerConnection = maxChannels;
                OpenChannelCount = 0;
                LastUsedUtc = DateTime.UtcNow;
            }
        }

        private readonly ILogger<RabbitMqConnectionPool> _logger;
        private readonly RabbitMqConnectionPoolOptions _options;
        private readonly ConnectionFactory _factory;
        private readonly List<ConnectionBucket> _connectionBuckets = new();
        private readonly SemaphoreSlim _poolSemaphore = new(1, 1);
        private readonly Timer _cleanupTimer;
        private bool _disposed;

        public RabbitMqConnectionPool(IOptions<RabbitMqConnectionPoolOptions> options, ILogger<RabbitMqConnectionPool> logger)
        {
            _options = options.Value;
            _logger = logger;

            _factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                AutomaticRecoveryEnabled = true,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(30)
            };

            _cleanupTimer = new Timer(CleanupIdleConnections, null,
                TimeSpan.FromSeconds(_options.IdleTimeoutSeconds),
                TimeSpan.FromSeconds(_options.IdleTimeoutSeconds));
        }

        public async Task<IChannel> RentChannelAsync()
        {
            await _poolSemaphore.WaitAsync();
            Console.WriteLine(_connectionBuckets.Count);
            try
            {
                foreach (var bucket in _connectionBuckets)
                {
                    if (bucket.OpenChannelCount < bucket.MaxChannelsPerConnection && bucket.Connection.IsOpen)
                    {
                        Interlocked.Increment(ref bucket.OpenChannelCount);
                        try
                        {
                            var channel = await bucket.Connection.CreateChannelAsync();
                            return new Decorators.ChannelTrackingDecorator(channel, () =>
                            {
                                Interlocked.Decrement(ref bucket.OpenChannelCount);
                                bucket.LastUsedUtc = DateTime.UtcNow;
                            });
                        }
                        catch
                        {
                            Interlocked.Decrement(ref bucket.OpenChannelCount);
                            throw;
                        }
                    }
                }

                if (_connectionBuckets.Count < _options.MaxConnections)
                {
                    var connection = await _factory.CreateConnectionAsync();
                    var newBucket = new ConnectionBucket(connection, _options.MaxChannelsPerConnection);
                    Interlocked.Increment(ref newBucket.OpenChannelCount);
                    _connectionBuckets.Add(newBucket);

                    try
                    {
                        var channel = await connection.CreateChannelAsync();
                        return new Decorators.ChannelTrackingDecorator(channel, () =>
                        {
                            Interlocked.Decrement(ref newBucket.OpenChannelCount);
                            newBucket.LastUsedUtc = DateTime.UtcNow;
                        });
                    }
                    catch
                    {
                        Interlocked.Decrement(ref newBucket.OpenChannelCount);
                        throw;
                    }
                }

                throw new InvalidOperationException("Maximum pool size reached");
            }
            finally
            {
                _poolSemaphore.Release();
            }
        }

        private void CleanupIdleConnections(object state)
        {
            _poolSemaphore.Wait();
            try
            {
                var now = DateTime.UtcNow;
                int removable = _connectionBuckets.Count - _options.MinConnections;

                if (removable <= 0)
                    return;

                var toRemove = new List<ConnectionBucket>();

                foreach (var bucket in _connectionBuckets)
                {
                    if (bucket.OpenChannelCount == 0 &&
                        (now - bucket.LastUsedUtc).TotalSeconds > _options.IdleTimeoutSeconds)
                    {
                        toRemove.Add(bucket);
                        removable--;

                        if (removable == 0)
                            break;
                    }
                }

                foreach (var bucket in toRemove)
                {
                    bucket.Connection.Dispose();
                    _connectionBuckets.Remove(bucket);
                }
            }
            finally
            {
                _poolSemaphore.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            _cleanupTimer.Dispose();

            await _poolSemaphore.WaitAsync();
            try
            {
                foreach (var bucket in _connectionBuckets)
                {
                    bucket.Connection.Dispose();
                }
                _connectionBuckets.Clear();
            }
            finally
            {
                _poolSemaphore.Release();
            }
        }
    }
}
