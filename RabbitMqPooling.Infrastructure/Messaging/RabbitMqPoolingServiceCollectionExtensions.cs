using System;
using Microsoft.Extensions.DependencyInjection;
using RabbitMqPooling.Application.Messaging;

namespace RabbitMqPooling.Infrastructure.Messaging
{
    /// <summary>
    /// Extension methods for registering RabbitMQ pooling in DI.
    /// </summary>
    public static class RabbitMqPoolingServiceCollectionExtensions
    {
        // For config via appsettings/IConfiguration
        public static IServiceCollection AddRabbitMqPooling(
            this IServiceCollection services,
            Action<RabbitMqConnectionPoolOptions> configureOptions)
        {
            services.Configure(configureOptions);
            services.AddSingleton<IRabbitMqConnectionPool, RabbitMqConnectionPool>();
            return services;
        }

        // Optional: For config via IConfiguration (for consumers who want config file binding)
        public static IServiceCollection AddRabbitMqPooling(
            this IServiceCollection services,
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            string sectionName = "RabbitMqConnectionPool")
        {
            services.Configure<RabbitMqConnectionPoolOptions>(configuration.GetSection(sectionName));
            services.AddSingleton<IRabbitMqConnectionPool, RabbitMqConnectionPool>();
            return services;
        }
    }
}
