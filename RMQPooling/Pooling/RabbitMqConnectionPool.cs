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
            _logger.LogDebug("Pool contains {Count} connection buckets before renting.", _connectionBuckets.Count);
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
                            _logger.LogInformation("Reusing connection for channel. Connection: {ConnectionHash}, OpenChannels: {OpenChannelCount}",
                                bucket.Connection.GetHashCode(), bucket.OpenChannelCount);

                            return new Decorators.ChannelTrackingDecorator(channel, () =>
                            {
                                Interlocked.Decrement(ref bucket.OpenChannelCount);
                                bucket.LastUsedUtc = DateTime.UtcNow;
                                _logger.LogDebug("Channel disposed. Remaining: {OpenChannelCount} on Connection {ConnectionHash}",
                                    bucket.OpenChannelCount, bucket.Connection.GetHashCode());
                            });
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Decrement(ref bucket.OpenChannelCount);
                            _logger.LogError(ex, "Failed to create channel on connection {ConnectionHash}", bucket.Connection.GetHashCode());
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

                    _logger.LogInformation("Created new connection for pool. Current bucket count: {Count}", _connectionBuckets.Count);
                    _logger.LogDebug("New connection bucket added. Pool now has {Count} buckets.", _connectionBuckets.Count);

                    try
                    {
                        var channel = await connection.CreateChannelAsync();
                        return new Decorators.ChannelTrackingDecorator(channel, () =>
                        {
                            Interlocked.Decrement(ref newBucket.OpenChannelCount);
                            newBucket.LastUsedUtc = DateTime.UtcNow;
                            _logger.LogDebug("Channel disposed. Remaining: {OpenChannelCount} on New Connection {ConnectionHash}",
                                newBucket.OpenChannelCount, newBucket.Connection.GetHashCode());
                        });
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Decrement(ref newBucket.OpenChannelCount);
                        _logger.LogError(ex, "Failed to create channel on new connection {ConnectionHash}", connection.GetHashCode());
                        throw;
                    }
                }

                _logger.LogWarning("Pool exhausted. Max pool size {MaxConnections} reached.", _options.MaxConnections);
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
                {
                    _logger.LogDebug("No idle connections to remove. Pool at or below minimum.");
                    _logger.LogDebug("Pool contains {Count} connection buckets after cleanup.", _connectionBuckets.Count);
                    return;
                }

                var toRemove = new List<ConnectionBucket>();

                foreach (var bucket in _connectionBuckets)
                {
                    if (bucket.OpenChannelCount == 0 &&
                        (now - bucket.LastUsedUtc).TotalSeconds > _options.IdleTimeoutSeconds)
                    {
                        toRemove.Add(bucket);
                        removable--;

                        _logger.LogInformation("Cleaning up idle connection {ConnectionHash}", bucket.Connection.GetHashCode());
                        if (removable == 0)
                            break;
                    }
                }

                foreach (var bucket in toRemove)
                {
                    try
                    {
                        bucket.Connection.Dispose();
                        _logger.LogInformation("Removed idle connection {ConnectionHash}", bucket.Connection.GetHashCode());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to dispose idle connection {ConnectionHash}", bucket.Connection.GetHashCode());
                    }
                    _connectionBuckets.Remove(bucket);
                }

                _logger.LogDebug("Pool contains {Count} connection buckets after cleanup.", _connectionBuckets.Count);
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

            _logger.LogInformation("Disposing RabbitMqConnectionPool and all connections.");
            _cleanupTimer.Dispose();

            await _poolSemaphore.WaitAsync();
            try
            {
                foreach (var bucket in _connectionBuckets)
                {
                    try
                    {
                        bucket.Connection.Dispose();
                        _logger.LogDebug("Disposed connection {ConnectionHash}", bucket.Connection.GetHashCode());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error while disposing connection {ConnectionHash}", bucket.Connection.GetHashCode());
                    }
                }
                _connectionBuckets.Clear();
                _logger.LogDebug("Pool completely disposed, bucket count is now {Count}.", _connectionBuckets.Count);
            }
            finally
            {
                _poolSemaphore.Release();
            }
        }
    }
}
