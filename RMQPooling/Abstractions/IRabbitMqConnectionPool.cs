using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RMQPooling.Abstractions
{
    public interface IRabbitMqConnectionPool : IAsyncDisposable
    {
        Task<IChannel> RentChannelAsync();
    }

}
