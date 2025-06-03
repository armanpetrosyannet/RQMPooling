using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RabbitMqPooling.Application.Messaging
{
    /// <summary>
    /// Options for configuring the RabbitMQ connection/channel pool.
    /// </summary>
    public class RabbitMqConnectionPoolOptions
    {
        public const string Position = "SOARealTimeClientConfigurations";
        public string HostName { get; set; } = "localhost";
        public int Port { get; set; } = 5672;
        public string UserName { get; set; } = "guest";
        public string Password { get; set; } = "guest";
        public int MinConnections { get; set; } = 1;        // Minimum connection count in pool
        public int MaxConnections { get; set; } = 4;        // Maximum connection count in pool
        public int MinChannelsPerConnection { get; set; } = 1;
        public int MaxChannelsPerConnection { get; set; } = 16;
        public int IdleTimeoutSeconds { get; set; } = 300;  // When to remove idle connections
    }
}
