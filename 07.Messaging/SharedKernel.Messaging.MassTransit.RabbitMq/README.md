# SharedKernel.Messaging.MassTransit.RabbitMq

The RabbitMQ transport for `SharedKernel.Messaging.MassTransit`. Reference it only in a service that runs its bus on RabbitMQ; the core package references no broker client.

```csharp
builder.Services
    .AddSharedKernelMessaging(builder.Configuration)
    .UseRabbitMq(builder.Configuration.GetConnectionString("rabbitmq")!)
    .WithDelayedDelivery()        // RabbitMQ delayed-message exchange (plugin required)
    .WithDeadLetterPolicy(o => o.MessageTimeToLive = TimeSpan.FromDays(7))
    .AddConsumer<OrderPlacedConsumer>()
    .Build();
```

`UseRabbitMq(Action<RabbitMqBusOptions>)` sets host, virtual host, credentials, heartbeat, prefetch and a bus-level `ConcurrentMessageLimit`. A publish-time partition key becomes the routing key.

MassTransit is pinned to 8.5.x, the last Apache-2.0 release.
