using RMQPooling.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RMQPooling.Extensions
{
    public class NamedRabbitMqPool
    {
        public string Name { get; }
        public IRabbitMqConnectionPool Pool { get; }
        public NamedRabbitMqPool(string name, IRabbitMqConnectionPool pool)
        {
            Name = name;
            Pool = pool;
        }
    }
}
