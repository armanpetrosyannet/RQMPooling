# RMQPooling – RabbitMQ Connection & Channel Pooling for .NET

RMQPooling is a .NET library that provides efficient RabbitMQ connection and channel pooling. It helps developers follow RabbitMQ best practices by reusing connections and channels, improving performance, preventing socket exhaustion, and simplifying multi-tenant or high-load scenarios. Modern .NET applications (microservices, web APIs, background workers, etc.) can use RMQPooling to manage RabbitMQ connections with confidence and ease.

---

## Why RabbitMQ Connection Pooling?

RabbitMQ’s own documentation and industry experts **strongly recommend long-lived connections and channels** instead of frequent open/close cycles. Each RabbitMQ connection is a TCP connection with a heavy handshake – establishing an AMQP connection requires at least 7 TCP packets (plus more for TLS).  
Opening or closing connections for each message or request adds significant latency and CPU overhead on both client and server. In fact, a CloudAMQP benchmark found that publishers opening a new connection per message had orders of magnitude lower throughput and high CPU usage on the RabbitMQ broker.

Connections are meant to be long-lived. The RabbitMQ .NET client guide notes that opening a new connection per operation (e.g. per message) is “unnecessary and strongly discouraged” due to the overhead introduced. Instead, applications should open a connection once and reuse it.  
Similarly, channels (RabbitMQ IModel) are intended to be reused for many operations rather than opened and closed repeatedly. Opening a channel involves a network round-trip, so creating a channel for every message is very inefficient.  
The best practice is to reuse connections and multiplex a connection across threads using channels.

Resource constraints also make pooling essential. Each connection uses ~100KB of RAM on the broker (more if TLS) and consumes a file descriptor. Thousands of connections can burden or even crash a RabbitMQ node due to memory exhaustion. By using a single connection (or a few) with multiple channels, you drastically reduce the load on the broker.  
The OS also limits how many sockets a process can open; an application that leaks or churns connections can hit these limits, causing new connections to fail. In one real example, a health check endpoint that opened a new RabbitMQ connection on every call quickly led to TCP port exhaustion, resulting in errors connecting to RabbitMQ. Pooling prevents such scenarios by keeping connections open and reusing them.

Finally, RabbitMQ imposes a channel limit (up to 65,535 channels per connection by protocol). While this is high, very high concurrency apps might prefer using a pool of connections each with a more modest number of channels, rather than one huge connection with thousands of channels.  
The RabbitMQ team suggests that most applications can use a single-digit number of channels per connection, and only extremely high throughput scenarios might justify channel pooling or additional connections for scalability. RMQPooling gives you the flexibility to use multiple connections (a connection pool) when needed, while still adhering to best practices of reuse.

### In summary, connection/channel pooling is necessary to:

- **Boost Performance:** Avoid repeated connection handshakes and teardown, reducing latency and CPU overhead. Long-lived connections and channels allow higher throughput.
- **Prevent Resource Exhaustion:** Reuse a few connections instead of creating many. This prevents running out of OS sockets or broker file handles/memory.
- **Respect RabbitMQ Limits:** Use channels (lightweight virtual connections) over a single TCP connection to handle parallel work, up to safe limits. Without pooling, naive usage (e.g. one connection per message or thread) could hit socket or channel limits and degrade the broker.
- **Simplify Code:** A pool abstracts the management of connections/channels so developers don’t need to handle reconnections, thread-safety around shared connections, or connection lifecycle in each part of the app.

---

## When Should You Use Connection Pooling?

Use RabbitMQ connection pooling in any application that interacts with RabbitMQ frequently or from multiple threads/requests. Here are common scenarios and problems that pooling solves:

- **High-Throughput Publishing (e.g. logging, event streaming):** If your app publishes many messages per second (or in bursts), opening a fresh connection for each publish will crush performance. Pooling keeps a connection ready so publishing a message is fast and doesn’t involve a new TCP handshake. Without pooling, throughput plummets and broker CPU soars due to connection churn.
- **Web APIs and Microservices:** In an ASP.NET Core API or microservice, you may produce or consume messages per HTTP request (for example, to trigger background work or emit events). Pool connections via DI so that each request handler can get a RabbitMQ channel without creating its own connection. This avoids socket proliferation and latency spikes for user requests. If pooling is not used, a busy API could exhaust ephemeral ports or spend significant time establishing connections, hurting response times.
- **Background Workers & Scheduled Jobs:** Long-running workers or scheduled tasks that periodically publish/consume should reuse connections. For example, a Hangfire job or IHostedService running every minute should not create a new connection each run. A pooled singleton connection reduces load and avoids subtle bugs (like failing to close connections properly on exceptions, which can leak resources).
- **Message Consumers (Event Listeners):** Consumers are typically long-lived by nature. A consumer service can use a pooled connection and open a channel per consumer thread. This ensures each consumer thread has its own channel (since channels are not thread-safe) while all share a few TCP connections. Without pooling, if each consumer opened a separate connection, you’d quickly accumulate many open sockets.
- **Multi-Tenant Applications:** If you have an application serving multiple tenants or customers and segregating their messaging, you might use named connection pools (see Named Pools below). For instance, each tenant could use a different RabbitMQ vhost or cluster for isolation. RMQPooling lets you configure a pool for “TenantA” and “TenantB” separately. Without pooling, handling tenant isolation might require complex connection management and risk either too many connections or cross-tenant interference.
- **Microservice Clusters and Horizontal Scaling:** In containerized environments (Kubernetes, etc.), you often scale out instances of services. Each instance should ideally use minimal connections (maybe one for pub, one for sub). With pooling, each instance manages a fixed small number of connections no matter how many messages it processes. If you don’t pool, a scaled-out deployment could flood the broker with connections (e.g. 100 pods × 10 connections each = 1000 connections).

> In short, use connection pooling whenever your .NET app interacts with RabbitMQ in a concurrent, frequent, or scalable manner.  
> The only cases where you might not need pooling are trivial apps that send very few messages infrequently. Even then, following the best practice of a long-lived connection is recommended – RMQPooling can still help by managing that single connection’s lifecycle for you.

---

## What Happens Without Pooling?

Not using pooling (or not reusing connections) can lead to multiple issues:

- **Performance Degradation:** Each new connection or channel creation adds latency. If you open/close on every operation, your throughput will suffer dramatically. For example, establishing a TLS connection can take dozens of milliseconds or more – multiplied by each message it becomes a bottleneck.
- **Socket Exhaustion:** Opening many short-lived connections in quick succession can exhaust ephemeral ports on the client or file descriptors on the server. This results in errors like “Too many open sockets” or “Cannot allocate port”. Connections in TIME_WAIT state can accumulate if you’re constantly opening/closing.
- **Broker Overload:** RabbitMQ handles thousands of connections, but at a cost. High connection churn forces the broker to constantly set up/tear down resources, and many open connections consume RAM and CPU. In worst cases, the broker can run OOM or hit FD limits and crash.
- **Inconsistent Reliability:** Frequent reconnects mean more points of failure. If a connection attempt fails (network hiccup, broker busy), an app not using a persistent connection must handle these errors on each operation. With a long-lived connection, you benefit from RabbitMQ’s heartbeat and automatic recovery (if enabled) to maintain the link. Without pooling, an app might experience intermittent message loss or errors under load.
- **Thread-Safety Pitfalls:** A naive approach might share a single connection or channel across threads without proper locking. This can cause `AlreadyClosedException` or undefined behavior, as channels are not thread-safe. Pooling (especially with RMQPooling) ensures each thread gets its own channel or safe access to connections, avoiding such race conditions by design.

**By using RMQPooling, you address these problems:**  
The library keeps a controlled number of long-lived connections and provides channels on-demand, so your app runs efficiently and reliably.

---

## Real-World Usage Scenarios

RMQPooling is useful in a variety of application types:

- **Microservices & Distributed Systems:** In an event-driven microservice architecture, services often publish events and consume commands via RabbitMQ. RMQPooling helps each service maintain optimal RabbitMQ connections.
- **ASP.NET Core Web APIs:** For web APIs that enqueue tasks for background processing or emit domain events on each request. RMQPooling allows quick acquisition of channels, keeping request latency low and avoiding excessive sockets.
- **Background Worker Services:** .NET IHostedService or Windows Services that perform background jobs can use RMQPooling to manage RabbitMQ connections.
- **Multi-Tenant SaaS Applications:** Route messages for different tenants to different RabbitMQ exchanges or brokers. RMQPooling’s named pools maintain separate pools per tenant or broker.
- **Enterprise Integration & Messaging Hubs:** Manage connections to multiple messaging systems or RabbitMQ clusters, using named pools for each cluster.
- **High-Availability or Failover Strategies:** Configure multiple connection pools to different nodes or clusters for redundancy.

---

## RMQPooling vs. Manual RabbitMQ Management

Why not just use RabbitMQ.Client directly? You certainly can, but you’ll need to implement pooling and lifetime management yourself.  
Most teams end up writing boilerplate: create a singleton ConnectionFactory, hold a singleton IConnection, create channels per thread or operation, handle reconnections, etc. RMQPooling encapsulates these best practices and provides a cleaner API.

**Comparison Table:**

| Aspect                   | Manual RabbitMQ.Client                           | Using RMQPooling                                                      |
|--------------------------|--------------------------------------------------|-----------------------------------------------------------------------|
| Connection setup         | Manually create ConnectionFactory/Connection.    | Provided via options/DI. Connections opened and disposed automatically.|
| Connection reuse         | Store IConnection singleton, manually manage.    | Pool automatically reuses connections.                                |
| Channels                 | Manually create/close channels per thread/use.   | Pool manages and hands out channels on demand.                        |
| Thread safety            | Developer must ensure channels are not shared.   | Each requested channel is single-thread safe by design.               |
| Error handling           | Manual try-catch, handle reconnections, etc.     | Pool encapsulates reconnection logic and error recovery.              |
| Multi-tenancy            | Manually manage multiple connections/factories.  | Pool supports named pools per tenant/cluster.                         |

---

## Named Pools: Multi-Tenant and Advanced Usage

One standout feature of RMQPooling is **named connection pools**.  
Each named pool is a separate grouping of RabbitMQ connections (with its own settings).  
Use cases include:

- **Tenant Isolation:** Route each tenant’s traffic through its own pool.
- **Publisher/Consumer Separation:** Use separate pools to avoid publisher backpressure from slow consumers.
- **Priority/Dedicated Channels:** Dedicate a pool for high-priority or special message flows.
- **Multiple RabbitMQ Clusters:** Publish to different clusters using named pools.

**Example:**

```csharp
// During configuration:
services.AddRabbitMqPool("TenantA", options => { /* ... */ });
services.AddRabbitMqPool("TenantB", options => { /* ... */ });
services.AddRabbitMqPool("Default", options => { /* ... */ });

// Usage:
var pool = accessor.GetPool("TenantA"); // or "Default"
await using var channel = await pool.RentChannelAsync();
// Use the channel as needed
```

## Performance and Reliability Benefits

- **Reduced Latency:** No need for handshake per publish – fast channel access.
- **Higher Throughput:** Orders of magnitude more throughput when reusing connections ([CloudAMQP Benchmark](https://www.cloudamqp.com/blog/part1-rabbitmq-best-practices.html)).
- **Controlled Resource Usage:** Limit memory, sockets, and RabbitMQ broker load.
- **Stability and Auto-Recovery:** Automatic recovery via RabbitMQ client features.
- **Avoiding Port Exhaustion:** Pooling prevents running out of ephemeral ports or file handles.

---

## Integration with Dependency Injection (DI)

RMQPooling is built to integrate with ASP.NET Core and .NET DI.

### Install

```shell
dotnet add package RMQPooling
## Basic DI Setup
```
```csharp
using RMQPooling.Extensions;

services.AddRabbitMqPoolAccessor();
services.AddRabbitMqPool("Publisher", options =>
{
    options.HostName = "localhost";
    options.Port = 5672;
    options.UserName = "guest";
    options.Password = "guest";
    options.MinConnections = 2;
    options.MaxConnections = 10;
    options.MaxChannelsPerConnection = 5;
    options.IdleTimeoutSeconds = 60;
});
```

## Usage

```csharp
var accessor = serviceProvider.GetRequiredService<IRabbitMqConnectionPoolAccessor>();
var pool = accessor.GetPool("Publisher");
await using var channel = await pool.RentChannelAsync();
// Use channel as normal (publishing/consuming)
```
## Configuration Options

| Option                   | Description                                | Default     |
|--------------------------|--------------------------------------------|-------------|
| HostName                 | RabbitMQ server hostname                   | "localhost" |
| Port                     | RabbitMQ port                              | 5672        |
| UserName                 | Username                                   | "guest"     |
| Password                 | Password                                   | "guest"     |
| MinConnections           | Minimum open connections                   | 1           |
| MaxConnections           | Maximum open connections                   | 4           |
| MaxChannelsPerConnection | Max channels per connection                | 16          |
| IdleTimeoutSeconds       | Idle connection cleanup interval (seconds) | 300         |

---

### Example `appsettings.json`

```json
{
  "RabbitMQ": {
    "HostName": "rabbitmq.mycompany.local",
    "UserName": "guest",
    "Password": "guest",
    "VirtualHost": "/",
    "PoolSize": 2,
    "AutomaticRecoveryEnabled": true,
    "Name": "Default"
  }
}
```
## References

- [RabbitMQ Official Documentation: Connections](https://www.rabbitmq.com/connections.html)
- [RabbitMQ Official Documentation: Channels](https://www.rabbitmq.com/channels.html)
- [RabbitMQ .NET Client API Guide](https://www.rabbitmq.com/client-libraries/dotnet-api-guide)
- [CloudAMQP: RabbitMQ Best Practices](https://www.cloudamqp.com/blog/part1-rabbitmq-best-practice.html)
- [CloudAMQP: 13 Common RabbitMQ Mistakes](https://www.cloudamqp.com/blog/part4-rabbitmq-13-common-errors.html)

## Contributing and Feedback

This project was created to address real-world needs for efficient, production-grade RabbitMQ connection and channel pooling in modern .NET applications.  
However, this approach was developed from a specific set of use cases and requirements, and there may be scenarios or considerations that were not fully covered.

**Motivation:**  
The primary use case for this library originated from a high-throughput application acting as a *republisher*—a system with advanced caching, filtering, and re-routing logic, designed for high-rate messaging scenarios. In these environments, neither a single long-lived connection nor a naive multiple open/close connection approach could provide the necessary reliability or performance. RMQPooling was built to solve these issues with a flexible, robust pooling strategy.

I am open to feedback, reviews, and requests for changes or improvements.  
If you have suggestions, use cases, or feature requests, please open an issue or submit a pull request on GitHub.

Your input is welcome and appreciated to help make this library even better and more broadly useful for the .NET/RabbitMQ community!

