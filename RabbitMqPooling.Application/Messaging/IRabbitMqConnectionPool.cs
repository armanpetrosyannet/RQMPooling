using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RabbitMqPooling.Application.Messaging
{
    /// <summary>
    /// Abstraction for a thread-safe, pooled RabbitMQ connection/channel provider.
    /// </summary>
    public interface IRabbitMqConnectionPool : IDisposable
    {
        /// <summary>
        /// Rent a pooled, ready-to-use channel for publishing or consuming.
        /// </summary>
        Task<IChannel> RentChannelAsync();

        /// <summary>
        /// Return the channel to the pool when finished.
        /// </summary>
        void ReturnChannel(IChannel channel);
    }
}
