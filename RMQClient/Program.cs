using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Events;
using RMQPooling.Abstractions;
using RMQPooling.Extensions;
using System.IO.Compression;
using System.Text;

namespace RMQClient
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
                .Build();

            var services = new ServiceCollection();

            services.AddLogging(builder =>
            {
                builder.AddSimpleConsole(o => o.TimestampFormat = "[HH:mm:ss] ");
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            services.AddRabbitMqPoolAccessor();
            services.AddRabbitMqPool("RQMPoolingConnection:Publisher", configuration);

            var serviceProvider = services.BuildServiceProvider();

            var poolAccessor = serviceProvider.GetRequiredService<IRabbitMqConnectionPoolAccessor>();
            var pool = poolAccessor.GetPool("RQMPoolingConnection:Publisher");

            // Run either message subscription or stress test
             await SubscribeAsync(pool);
          //  await RunStressTestAsync(pool);
        }

        private static async Task SubscribeAsync(IRabbitMqConnectionPool pool)
        {
            await using var channel = await pool.RentChannelAsync();

            await channel.QueueDeclareAsync("TEST", durable: false, exclusive: false, autoDelete: true);
            await channel.QueueBindAsync("TEST", "AGP", "#");

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                Console.WriteLine($"[x] Received: {Decompress(body)}");
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

            await channel.BasicCancelAsync(consumerTag);
        }

        private static async Task RunStressTestAsync(IRabbitMqConnectionPool pool, int threadCount = 40, int iterationsPerThread = 5)
        {
            var random = new Random();
            var tasks = new Task[threadCount];

            for (int i = 0; i < threadCount; i++)
            {
                int threadId = i;
                tasks[i] = Task.Run(async () =>
                {
                    for (int j = 0; j < iterationsPerThread; j++)
                    {
                        var sleepMs = random.Next(1000, 5000); // 1–5 seconds
                        await using var channel = await pool.RentChannelAsync();
                        Console.WriteLine($"[Thread {threadId}] Channel rented (iteration {j}), sleeping {sleepMs}ms...");
                        await Task.Delay(sleepMs);
                    }
                });
            }

            await Task.WhenAll(tasks);
            Console.WriteLine("✅ Stress test completed. Press any key to exit.");
            Console.ReadKey();
        }

        private static string Decompress(byte[] data)
        {
            using var msi = new MemoryStream(data);
            using var mso = new MemoryStream();
            using (var zipStream = new DeflateStream(msi, CompressionMode.Decompress))
            {
                zipStream.CopyTo(mso);
            }
            return Encoding.UTF8.GetString(mso.ToArray());
        }
    }
}
