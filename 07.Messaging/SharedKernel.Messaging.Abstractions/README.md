# SharedKernel.Messaging.Abstractions

Transport-agnostic messaging abstractions for Platform.SharedKernel microservices: `IMessageBus`,
`IEventPublisher`, `PublishContext`, `IMessagingBuilder`, `MessagingOptions`, `IIdempotencyStore`,
`IMessageHeaderPropagator`, `ITenantContextAccessor`, `IMessageScheduler`, `IMessageBusProbe`, and
`IRoutingSlipBuilder` — the contracts every transport package
([`SharedKernel.Messaging.MassTransit`](https://www.nuget.org/packages/SharedKernel.Messaging.MassTransit))
implements. **Zero transport NuGet dependency** — references only
`Microsoft.Extensions.DependencyInjection.Abstractions` and `SharedKernel.Contracts` (for
`IIntegrationEvent`) — so `05.Application` and other upstream layers can depend on messaging
contracts without pulling in MassTransit, RabbitMQ, or Azure Service Bus client libraries.

## Install

```
dotnet add package SharedKernel.Messaging.Abstractions
```

```xml
<PackageReference Include="SharedKernel.Messaging.Abstractions" Version="1.0.0" />
```

## Usage

Application code depends on `IMessageBus`/`IEventPublisher` only — never a concrete MassTransit type
(`IBus`, `IPublishEndpoint`, `ISendEndpointProvider` must never appear in application code):

```csharp
public sealed class PlaceOrderHandler(IMessageBus bus, IEventPublisher publisher)
{
    public async Task Handle(PlaceOrderCommand command, CancellationToken ct)
    {
        // ... domain logic ...

        await bus.SendAsync(new ProcessPaymentCommand(command.OrderId), ct);

        // Optional per-publish metadata; Subject becomes the CloudEvents "subject" attribute.
        await publisher.PublishAsync(
            new OrderPlaced(Guid.NewGuid(), DateTimeOffset.UtcNow, command.OrderId),
            ctx => ctx.WithSubject($"order/{command.OrderId}"),
            ct);
    }
}

// An integration event: a sealed record implementing IIntegrationEvent (SharedKernel.Contracts),
// with a stable wire name. The name — never the class name — becomes the envelope's CloudEvents "type".
[IntegrationEvent("orders.order-placed", Version = 1)]
public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IIntegrationEvent;
```

`IEventPublisher.PublishAsync<TEvent>` is constrained to `class, IIntegrationEvent`: a domain event or
a plain message type does not compile. An integration event type without a valid `[IntegrationEvent]`
attribute compiles but throws `InvalidOperationException` at publish time. Plain messages go through
`IMessageBus`.

This package ships **no DI registration or transport wiring of its own** — a microservice's
composition root wires a concrete transport that satisfies these contracts, e.g.
[`SharedKernel.Messaging.MassTransit`](https://www.nuget.org/packages/SharedKernel.Messaging.MassTransit)'s
`AddSharedKernelMessaging()` + `MessagingBusBuilder`.

## Recipe: a working `IIdempotencyStore` implementation

`IIdempotencyStore` is deliberately **not** provided by SharedKernel — consumer-side deduplication
storage is a service-specific infrastructure decision (Redis? EF Core? a table with a unique index?).
The consuming service registers its own implementation before calling
`MessagingBusBuilder.WithIdempotency()`; `Build()` throws `InvalidOperationException` if none is
registered. Below is a complete, working reference implementation backed by
`IDistributedCache` (e.g. `Microsoft.Extensions.Caching.StackExchangeRedis`), sourcing its retention
window from `IdempotencyOptions.ExpiryWindow`:

```csharp
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using SharedKernel.Messaging.Abstractions.Idempotency;

/// <summary>
/// Reference IIdempotencyStore implementation backed by IDistributedCache. A processed
/// messageId is recorded as a cache entry whose presence alone signals "already handled" —
/// the stored value carries no meaning beyond existence.
/// </summary>
public sealed class RedisIdempotencyStore(
    IDistributedCache cache,
    IOptions<IdempotencyOptions> options) : IIdempotencyStore
{
    private const string KeyPrefix = "idempotency:";

    public async Task<bool> HasProcessedAsync(Guid messageId, CancellationToken ct)
    {
        var value = await cache.GetAsync(BuildKey(messageId), ct);
        return value is not null;
    }

    public Task MarkProcessedAsync(Guid messageId, CancellationToken ct) =>
        cache.SetAsync(
            BuildKey(messageId),
            value: [],
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = options.Value.ExpiryWindow,
            },
            ct);

    private static string BuildKey(Guid messageId) => $"{KeyPrefix}{messageId:N}";
}
```

```csharp
// Composition root
services.AddStackExchangeRedisCache(o => o.Configuration = configuration["Redis:ConnectionString"]);
services.AddScoped<IIdempotencyStore, RedisIdempotencyStore>();

services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .WithRetry()
    .WithIdempotency(o => o.ExpiryWindow = TimeSpan.FromHours(48))
    .AddConsumer<OrderPlacedConsumer>()
    .Build();
```

The `ExpiryWindow` should be set to at least as long as the transport's dead-letter retry budget —
a shorter window risks a late redelivery being treated as novel and reprocessed. Never implement
deduplication logic inline inside a `ConsumeAsync` body (e.g. a local `HashSet<Guid>` or an ad hoc
database query) — `WithIdempotency()` + a registered `IIdempotencyStore` is the only approved
mechanism.

## Layering

```
SharedKernel.Messaging.Abstractions  →  Microsoft.Extensions.DependencyInjection.Abstractions,
                                         SharedKernel.Contracts (04.Contracts)
```

Target framework: `net10.0`. No MassTransit, RabbitMQ, or Azure Service Bus
dependency — any transport NuGet reference leaking into this package is a hard architectural
violation.

For full documentation see
[`07.Messaging/CLAUDE.md`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/CLAUDE.md).
