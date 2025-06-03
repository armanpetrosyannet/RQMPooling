using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RMQPooling.Abstractions
{
    // Accessor interface for resolving by name
    public interface IRabbitMqConnectionPoolAccessor
    {
        IRabbitMqConnectionPool GetPool(string name);
    }
}
