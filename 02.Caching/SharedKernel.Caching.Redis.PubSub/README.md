# SharedKernel.Caching.Redis.PubSub

Ephemeral Redis Pub/Sub signaling through `IRedisChannelService`: publish, subscribe, unsubscribe, and a `ConnectionHealth` state for health checks. It depends only on `SharedKernel.Caching.Abstractions` and `SharedKernel.Caching.Redis.Core`.

> **At-most-once, no durability.** Messages are not persisted, so an offline subscriber misses them permanently. This package is for cache-adjacent, loss-tolerant signaling only. It is **not** a substitute for `SharedKernel.Messaging.*` (`07.Messaging`), which provides outbox-backed, retryable, ordered delivery.
>
> **Not needed for cache invalidation.** Removing, expiring or tag-evicting an `ICacheService` entry already reaches every instance of the service through the FusionCache backplane registered by `SharedKernel.Caching.Redis` (`AddRedisL2`).

## Install

```text
dotnet add package SharedKernel.Caching.Redis.PubSub
```

## Usage

```csharp
services.AddSharedKernelCaching(o => o.ServiceName = "pricing")
        .AddRedisL2("localhost:6379")      // or any registration that adds the shared IConnectionMultiplexer
        .AddRedisChannelService();
```

`AddRedisChannelService` requires `IConnectionMultiplexer` to be registered first, through `AddRedisConnection`, `AddRedisL2` or `AddRedisDistributedLocking`.

```csharp
public sealed class PriceTicker(IRedisChannelService channels)
{
    public ValueTask StartAsync(CancellationToken ct) =>
        channels.SubscribeAsync("pricing:ticks", message => HandleAsync(message), ct);

    public ValueTask PublishAsync(string tick, CancellationToken ct) =>
        channels.PublishAsync("pricing:ticks", tick, ct);
}
```

Handler exceptions are caught and logged, and never reach the Redis subscriber.

## Layering

```text
SharedKernel.Caching.Redis.PubSub  →  SharedKernel.Caching.Abstractions, SharedKernel.Caching.Redis.Core
```

Target framework: `net10.0`.
