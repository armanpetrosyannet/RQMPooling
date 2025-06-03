using RMQPooling.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RMQPooling.Extensions
{
    public class RabbitMqConnectionPoolAccessor : IRabbitMqConnectionPoolAccessor
    {
        private readonly Dictionary<string, IRabbitMqConnectionPool> _pools;
        public RabbitMqConnectionPoolAccessor(IEnumerable<NamedRabbitMqPool> namedPools)
        {
            _pools = namedPools.ToDictionary(x => x.Name, x => x.Pool);
        }

        public IRabbitMqConnectionPool GetPool(string name)
        {
            if (_pools.TryGetValue(name, out var pool))
                return pool;
            throw new KeyNotFoundException($"No RabbitMQ pool registered with name '{name}'.");
        }
    }
}
