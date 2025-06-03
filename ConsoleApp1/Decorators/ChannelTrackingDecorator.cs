using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RMQPooling.Decorators
{
    public class ChannelTrackingDecorator : IChannel
    {
        private readonly IChannel _inner;
        private readonly Action _onDispose;
        private bool _disposed;

        public ChannelTrackingDecorator(IChannel inner, Action onDispose)
        {
            _inner = inner;
            _onDispose = onDispose;
        }

        public event AsyncEventHandler<BasicAckEventArgs> BasicAcksAsync
        {
            add => _inner.BasicAcksAsync += value;
            remove => _inner.BasicAcksAsync -= value;
        }

        public event AsyncEventHandler<BasicNackEventArgs> BasicNacksAsync
        {
            add => _inner.BasicNacksAsync += value;
            remove => _inner.BasicNacksAsync -= value;
        }

        public event AsyncEventHandler<BasicReturnEventArgs> BasicReturnAsync
        {
            add => _inner.BasicReturnAsync += value;
            remove => _inner.BasicReturnAsync -= value;
        }

        public event AsyncEventHandler<CallbackExceptionEventArgs> CallbackExceptionAsync
        {
            add => _inner.CallbackExceptionAsync += value;
            remove => _inner.CallbackExceptionAsync -= value;
        }

        public event AsyncEventHandler<FlowControlEventArgs> FlowControlAsync
        {
            add => _inner.FlowControlAsync += value;
            remove => _inner.FlowControlAsync -= value;
        }

        public event AsyncEventHandler<ShutdownEventArgs> ChannelShutdownAsync
        {
            add => _inner.ChannelShutdownAsync += value;
            remove => _inner.ChannelShutdownAsync -= value;
        }

        public ValueTask BasicAckAsync(ulong deliveryTag, bool multiple, CancellationToken cancellationToken = default)
            => _inner.BasicAckAsync(deliveryTag, multiple, cancellationToken);

        public ValueTask BasicNackAsync(ulong deliveryTag, bool multiple, bool requeue, CancellationToken cancellationToken = default)
            => _inner.BasicNackAsync(deliveryTag, multiple, requeue, cancellationToken);

        public ValueTask BasicRejectAsync(ulong deliveryTag, bool requeue, CancellationToken cancellationToken = default)
            => _inner.BasicRejectAsync(deliveryTag, requeue, cancellationToken);

        public Task CloseAsync(ushort replyCode, string replyText, bool abort, CancellationToken cancellationToken = default)
            => _inner.CloseAsync(replyCode, replyText, abort, cancellationToken);

        public Task AbortAsync() => _inner.AbortAsync();

        public bool IsClosed => _inner.IsClosed;

        public bool IsOpen => _inner.IsOpen;

        public int ChannelNumber => _inner.ChannelNumber;

        public ShutdownEventArgs? CloseReason => _inner.CloseReason;

        public IAsyncBasicConsumer? DefaultConsumer
        {
            get => _inner.DefaultConsumer;
            set => _inner.DefaultConsumer = value;
        }

        public string? CurrentQueue => _inner.CurrentQueue;

        public TimeSpan ContinuationTimeout
        {
            get => _inner.ContinuationTimeout;
            set => _inner.ContinuationTimeout = value;
        }

        public Task QueueDeclareAsync(string queue, bool durable, bool exclusive, bool autoDelete, IDictionary<string, object>? arguments = null, bool passive = false, bool noWait = false, CancellationToken cancellationToken = default)
            => _inner.QueueDeclareAsync(queue, durable, exclusive, autoDelete, arguments, passive, noWait, cancellationToken);

        public Task QueueDeleteAsync(string queue, bool ifUnused, bool ifEmpty, bool noWait = false, CancellationToken cancellationToken = default)
            => _inner.QueueDeleteAsync(queue, ifUnused, ifEmpty, noWait, cancellationToken);

        public Task QueueBindAsync(string queue, string exchange, string routingKey, IDictionary<string, object>? arguments = null, bool noWait = false, CancellationToken cancellationToken = default)
            => _inner.QueueBindAsync(queue, exchange, routingKey, arguments, noWait, cancellationToken);

        public Task QueueUnbindAsync(string queue, string exchange, string routingKey, IDictionary<string, object>? arguments = null, CancellationToken cancellationToken = default)
            => _inner.QueueUnbindAsync(queue, exchange, routingKey, arguments, cancellationToken);

        public Task ExchangeDeclareAsync(string exchange, string type, bool durable, bool autoDelete, IDictionary<string, object>? arguments = null, bool passive = false, bool noWait = false, CancellationToken cancellationToken = default)
            => _inner.ExchangeDeclareAsync(exchange, type, durable, autoDelete, arguments, passive, noWait, cancellationToken);

        public Task ExchangeDeleteAsync(string exchange, bool ifUnused = false, bool noWait = false, CancellationToken cancellationToken = default)
            => _inner.ExchangeDeleteAsync(exchange, ifUnused, noWait, cancellationToken);

        public Task ExchangeBindAsync(string destination, string source, string routingKey, IDictionary<string, object>? arguments = null, bool noWait = false, CancellationToken cancellationToken = default)
            => _inner.ExchangeBindAsync(destination, source, routingKey, arguments, noWait, cancellationToken);

        public Task ExchangeUnbindAsync(string destination, string source, string routingKey, IDictionary<string, object>? arguments = null, bool noWait = false, CancellationToken cancellationToken = default)
            => _inner.ExchangeUnbindAsync(destination, source, routingKey, arguments, noWait, cancellationToken);

        public ValueTask BasicPublishAsync<T>(string exchange, string routingKey, bool mandatory, T basicProperties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default) where T : IReadOnlyBasicProperties, IAmqpHeader
            => _inner.BasicPublishAsync(exchange, routingKey, mandatory, basicProperties, body, cancellationToken);

        public Task<QueueDeclareOk> QueueDeclarePassiveAsync(string queue, CancellationToken cancellationToken = default)
            => _inner.QueueDeclarePassiveAsync(queue, cancellationToken);

        public Task ExchangeDeclarePassiveAsync(string exchange, CancellationToken cancellationToken = default)
            => _inner.ExchangeDeclarePassiveAsync(exchange, cancellationToken);

        public ValueTask<ulong> GetNextPublishSequenceNumberAsync(CancellationToken cancellationToken = default)
        {
            return _inner.GetNextPublishSequenceNumberAsync(cancellationToken);
        }

        ValueTask IChannel.BasicAckAsync(ulong deliveryTag, bool multiple, CancellationToken cancellationToken)
        {
            return _inner.BasicAckAsync(deliveryTag, multiple, cancellationToken);
        }

        ValueTask IChannel.BasicNackAsync(ulong deliveryTag, bool multiple, bool requeue, CancellationToken cancellationToken)
        {
            return _inner.BasicNackAsync(deliveryTag, multiple, requeue, cancellationToken);
        }

        public Task BasicCancelAsync(string consumerTag, bool noWait = false, CancellationToken cancellationToken = default)
        {
            return _inner.BasicCancelAsync(consumerTag, noWait, cancellationToken);
        }

        public Task<string> BasicConsumeAsync(string queue, bool autoAck, string consumerTag, bool noLocal, bool exclusive, IDictionary<string, object?>? arguments, IAsyncBasicConsumer consumer, CancellationToken cancellationToken = default)
        {
            return _inner.BasicConsumeAsync(queue, autoAck, consumerTag, noLocal, exclusive, arguments, consumer, cancellationToken);
        }

        public Task<BasicGetResult?> BasicGetAsync(string queue, bool autoAck, CancellationToken cancellationToken = default)
        {
            return _inner.BasicGetAsync(queue, autoAck, cancellationToken);
        }



        public ValueTask BasicPublishAsync<TProperties>(CachedString exchange, CachedString routingKey, bool mandatory, TProperties basicProperties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default) where TProperties : IReadOnlyBasicProperties, IAmqpHeader
        {
            return _inner.BasicPublishAsync(exchange, routingKey, mandatory, basicProperties, body, cancellationToken);
        }

        public Task BasicQosAsync(uint prefetchSize, ushort prefetchCount, bool global, CancellationToken cancellationToken = default)
        {
            return _inner.BasicQosAsync(prefetchSize, prefetchCount, global, cancellationToken);
        }

        ValueTask IChannel.BasicRejectAsync(ulong deliveryTag, bool requeue, CancellationToken cancellationToken)
        {
            return _inner.BasicRejectAsync(deliveryTag, requeue, cancellationToken);
        }

        public Task CloseAsync(ShutdownEventArgs reason, bool abort, CancellationToken cancellationToken = default)
        {
            return _inner.CloseAsync(reason, abort, cancellationToken);
        }

        public Task<uint> QueuePurgeAsync(string queue, CancellationToken cancellationToken = default)
        {
            return _inner.QueuePurgeAsync(queue, cancellationToken);
        }

        public Task<uint> MessageCountAsync(string queue, CancellationToken cancellationToken = default)
        {
            return _inner.MessageCountAsync(queue, cancellationToken);
        }

        public Task<uint> ConsumerCountAsync(string queue, CancellationToken cancellationToken = default)
        {
            return _inner.ConsumerCountAsync(queue, cancellationToken);
        }

        public Task TxCommitAsync(CancellationToken cancellationToken = default)
        {
            return _inner.TxCommitAsync(cancellationToken);
        }

        public Task TxRollbackAsync(CancellationToken cancellationToken = default)
        {
            return _inner.TxRollbackAsync(cancellationToken);
        }

        public Task TxSelectAsync(CancellationToken cancellationToken = default)
        {
            return _inner.TxRollbackAsync(cancellationToken);
        }

        Task<QueueDeclareOk> IChannel.QueueDeclareAsync(string queue, bool durable, bool exclusive, bool autoDelete, IDictionary<string, object?>? arguments, bool passive, bool noWait, CancellationToken cancellationToken)
        {
            return _inner.QueueDeclareAsync(queue, durable, exclusive, autoDelete, arguments, passive, noWait, cancellationToken);
        }

        Task<uint> IChannel.QueueDeleteAsync(string queue, bool ifUnused, bool ifEmpty, bool noWait, CancellationToken cancellationToken)
        {
            return _inner.QueueDeleteAsync(queue, ifUnused, ifEmpty, noWait, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await _inner.DisposeAsync();
            _onDispose();
        }

        // Explicit interface implementation — redirects to the above method
        ValueTask IAsyncDisposable.DisposeAsync() => DisposeAsync();

        // Optional sync dispose — safe fallback for `using` patterns, not required if not used
        public void Dispose()
        {
            _inner.Dispose();
            _onDispose();
            _disposed = true;
        }
    }
}
