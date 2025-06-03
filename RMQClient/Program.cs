using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Events;
using RMQPooling.Abstractions;
using RMQPooling.Extensions;
using System;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RMQClient
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // SETUP DI
            var services = new ServiceCollection();

            services.AddLogging(builder =>
            {
                builder.AddSimpleConsole(o => o.TimestampFormat = "[HH:mm:ss] ");
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            services.AddRabbitMqPoolAccessor();
            // Register the pool using code-based options (no IConfiguration required)
            services.AddRabbitMqPool("Publisher", options =>
            {
                options.HostName = "172.16.200.83";
                options.Port = 5672;
                options.UserName = "etl";
                options.Password = "qgQzmX9c9WVmi9A";
                options.MinConnections = 5;
                options.MaxConnections = 50;
                options.MaxChannelsPerConnection = 5;
                options.IdleTimeoutSeconds = 10;
            });

            var serviceProvider = services.BuildServiceProvider();

            // Resolve the pool
            var poolAccessor = serviceProvider.GetRequiredService<IRabbitMqConnectionPoolAccessor>();
            var pool = poolAccessor.GetPool("Publisher");

            SubscribeAsync(pool).GetAwaiter().GetResult();
            //var random = new Random();
            //var tasks = new Task[40]; // Number of parallel threads/tasks

            //for (int i = 0; i < tasks.Length; i++)
            //{
            //    int threadId = i;
            //    tasks[i] = Task.Run(async () =>
            //    {
            //        for (int j = 0; j < 5; j++) // Each thread rents/releases multiple times
            //        {
            //            var sleepMs = random.Next(1000, 20000); // 1-5 seconds
            //            await using var channel = await pool.RentChannelAsync();
            //            Console.WriteLine($"[Thread {threadId}] Channel rented (iteration {j}), sleeping {sleepMs}ms...");
            //            await Task.Delay(sleepMs);
            //            // Channel disposed here (async using)
            //        }
            //    });
            //}

            //Task.WaitAll(tasks);
            //Console.WriteLine("Stress test completed. Press any key to exit.");
            //Console.ReadKey();

        }

        static async Task SubscribeAsync(IRabbitMqConnectionPool pool)
        {

            await using var channel = await pool.RentChannelAsync();

            await channel.QueueDeclareAsync("TEST", durable: false, exclusive: false, autoDelete: true);
            await channel.QueueBindAsync("TEST", "AGP", "#");

            // Example async consumer (using an async lambda, adjust as needed for your app)
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                Console.WriteLine($"[x] Received: {Decompress(body)}");
                Thread.Sleep(1000); 
                await Task.Yield(); // Simulate async work
            };

            var consumerTag = await channel.BasicConsumeAsync(
                queue: "TEST",
                autoAck: true,
                consumerTag: "",
                noLocal: false,
                exclusive: false,
                arguments: null,
                consumer: consumer
            );

            Console.WriteLine(" [*] Waiting for messages. Press Enter to exit.");
            Console.ReadLine();

            // Optionally, cancel the consumer when done
            await channel.BasicCancelAsync(consumerTag);
        }
        private static string Decompress(byte[] data)
        {
            using (var msi = new MemoryStream(data))
            using (var mso = new MemoryStream())
            {
                using (var zipStream = new DeflateStream(msi, CompressionMode.Decompress))
                {
                    zipStream.CopyTo(mso);
                }
                return Encoding.UTF8.GetString(mso.ToArray());
            }
        }
    }
}
