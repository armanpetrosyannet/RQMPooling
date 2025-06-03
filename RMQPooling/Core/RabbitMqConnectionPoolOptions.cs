using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RMQPooling.Core
{
    public class RabbitMqConnectionPoolOptions
    {
        public string HostName { get; set; } = "localhost";
        public int Port { get; set; } = 5672;
        public string UserName { get; set; } = "guest";
        public string Password { get; set; } = "guest";
        public int MinConnections { get; set; } = 1;
        public int MaxConnections { get; set; } = 4;
        public int MaxChannelsPerConnection { get; set; } = 16;
        public int IdleTimeoutSeconds { get; set; } = 300;
    }
}
