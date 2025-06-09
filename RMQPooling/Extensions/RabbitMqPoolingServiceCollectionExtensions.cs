using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RMQPooling.Abstractions;
using RMQPooling.Core;
using RMQPooling.Pooling;

namespace RMQPooling.Extensions
{
    public static class RabbitMqPoolingServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a named RabbitMQ connection pool.
        /// </summary>
        public static IServiceCollection AddRabbitMqPool(
            this IServiceCollection services,
            string name,
            Action<RabbitMqConnectionPoolOptions> configure)
        {
            // Register options for the given name
            services.Configure(name, configure);

            // Register a singleton for this named pool
            services.AddSingleton(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<RabbitMqConnectionPool>>();
                var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<RabbitMqConnectionPoolOptions>>();
                var pool = new RabbitMqConnectionPool(Options.Create(optionsMonitor.Get(name)), logger);
                return new NamedRabbitMqPool(name, pool);
            });

            return services;
        }

        /// <summary>
        /// Registers a named RabbitMQ connection pool with options bound from configuration.
        /// </summary>
        public static IServiceCollection AddRabbitMqPool(
            this IServiceCollection services,
            string name,
            IConfiguration configuration)
        {
            // Bind configuration section to named options
            services.Configure<RabbitMqConnectionPoolOptions>(name, configuration.GetSection(name));

            // Register a singleton for this named pool
            services.AddSingleton(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<RabbitMqConnectionPool>>();
                var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<RabbitMqConnectionPoolOptions>>();
                var pool = new RabbitMqConnectionPool(Options.Create(optionsMonitor.Get(name)), logger);
                return new NamedRabbitMqPool(name, pool);
            });

            return services;
        }

        /// <summary>
        /// Registers an accessor to retrieve pools by name.
        /// </summary>
        public static IServiceCollection AddRabbitMqPoolAccessor(this IServiceCollection services)
        {
            services.AddSingleton<IRabbitMqConnectionPoolAccessor, RabbitMqConnectionPoolAccessor>();
            return services;
        }
    }
}
