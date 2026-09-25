# SharedKernel.Messaging.MassTransit.AzureServiceBus

The Azure Service Bus transport for `SharedKernel.Messaging.MassTransit`. Reference it only in a service that runs its bus on Azure Service Bus; the core package references neither the Azure SDK nor Azure.Identity.

```csharp
builder.Services
    .AddSharedKernelMessaging(builder.Configuration)
    .UseAzureServiceBus(o => o.FullyQualifiedNamespace = "my-namespace.servicebus.windows.net")
    .WithDelayedDelivery()        // native scheduled enqueue
    .AddConsumer<OrderPlacedConsumer>()
    .Build();
```

Set exactly one of `ConnectionString` (local development and CI) or `FullyQualifiedNamespace` (managed identity through `DefaultAzureCredential`). A publish-time partition key becomes the session id; enable sessions on the receiving entity for ordered delivery. `WithDeadLetterPolicy()` is RabbitMQ-only and logs a warning at startup here, because Azure Service Bus dead-lettering is configured on the entity.

MassTransit is pinned to 8.5.x, the last Apache-2.0 release.
