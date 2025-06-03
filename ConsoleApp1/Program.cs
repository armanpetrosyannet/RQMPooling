using System;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RMQPooling.Abstractions;
using RMQPooling.Extensions;


class Program
{
    static async Task Main(string[] args)
    {
        // SETUP DI
        var services = new ServiceCollection();


        services.AddLogging(builder =>
        {
            builder.AddConsole();
        });

        services.AddRabbitMqPoolAccessor();
        // Register the pool using code-based options (no IConfiguration required)
        services.AddRabbitMqPool("Publisher",options =>
        {
            options.HostName = "172.16.200.83";
            options.Port = 5672;
            options.UserName = "etl";
            options.Password = "qgQzmX9c9WVmi9A";
            options.MinConnections = 5;
            options.MaxConnections = 11;
            options.MaxChannelsPerConnection = 2;
            options.IdleTimeoutSeconds = 10;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Resolve the pool
        var poolAccessor = serviceProvider.GetRequiredService<IRabbitMqConnectionPoolAccessor>();
        var pool=poolAccessor.GetPool("Publisher");
        for (int i = 0; i < 20; i++)
        {
            var th = new Thread(p =>
            {
                using var channel = pool.RentChannelAsync().GetAwaiter().GetResult();
                {
                    Console.WriteLine("Channel created ");
                    Thread.Sleep(2000);
                }
            });
           th.Start();
          
        }
        Console.ReadKey();
    }



    static async void SubscribeTest(IRabbitMqConnectionPool pool)
    {
        var channel = await pool.RentChannelAsync();
        try
        {
            await channel.QueueDeclareAsync("test-queue1", durable: false, exclusive: false, autoDelete: true);
            await channel.QueueBindAsync("test-queue", "AGP", "#");

            var consumer = new RabbitMQ.Client.Events.AsyncEventingBasicConsumer(channel);

            consumer.ReceivedAsync +=async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                //  Console.WriteLine($" [x] Received: {message}");
                // You can manually ack if needed: channel.BasicAck(ea.DeliveryTag, false);
            };

            string consumerTag = await channel.BasicConsumeAsync(
                queue: "test-queue",
                autoAck: true,            // Set to false if you want manual ack
                consumer: consumer);

            Console.WriteLine(" [*] Subscribed. Press [Enter] to stop listening...");



            Console.ReadLine();

            await channel.BasicCancelAsync(consumerTag);
        }
        catch (Exception ex)
        {

        }

    }


}
