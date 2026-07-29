# SharedKernel.Caching.Redis.PubSub

Ephemeral Redis Pub/Sub signaling and cache invalidation: `IRedisChannelService` (raw
publish/subscribe), `ICacheInvalidationBus` (key/tag/broadcast invalidation publishing), and
`CacheInvalidationReceiver` (a `BackgroundService` subscriber). Depends only on
`SharedKernel.Caching.Abstractions` + `SharedKernel.Caching.Redis.Core` — does not transitively
reference `SharedKernel.Caching.Redis` (L2), distributed locking, or the hash store package.

> **At-most-once, no durability.** Messages are not persisted — an offline subscriber misses them
> permanently. This package is scoped to cache-adjacent ephemeral signaling only and is **not** a
> substitute for `SharedKernel.Messaging.*` (`07.Messaging`), which provides outbox-backed,
> retryable, ordered delivery.

## Install

```
dotnet add package SharedKernel.Caching.Redis.PubSub
```

```xml
<PackageReference Include="SharedKernel.Caching.Redis.PubSub" Version="1.0.0" />
```

## Usage

### Pub/Sub only (no FusionCache, no cache invalidation receiver)

```csharp
services.AddRedisConnection("localhost:6379"); // IConnectionMultiplexer via .Redis.Core
services.AddCachingCoreOptions(o => o.ServiceName = "my-service");

var builder = new MyCachingBuilder(services); // any ICachingBuilder wrapping `services`
builder.AddRedisChannelService()
       .AddRedisCacheInvalidationBus();
// Inject: IRedisChannelService, ICacheInvalidationBus
```

### Full cross-service cache invalidation (with the background receiver)

`AddCacheInvalidationReceiver()` additionally requires an `ICacheService` to already be registered
(it invalidates the local cache when a message arrives):

```csharp
builder.AddRedisChannelService()
       .AddRedisCacheInvalidationBus()
       .AddCacheInvalidationReceiver();
// CacheInvalidationReceiver runs as a BackgroundService, subscribed to this service's own
// invalidation channel and the broadcast channel.
```

```csharp
public sealed class ProductInvalidator(ICacheInvalidationBus invalidationBus)
{
    public ValueTask InvalidateAsync(Guid productId, CancellationToken ct) =>
        invalidationBus.PublishKeyInvalidationAsync([$"products:{productId}"], ct);
}
```

## Layering

```
SharedKernel.Caching.Redis.PubSub  →  SharedKernel.Caching.Redis.Core  →  SharedKernel.Caching.Abstractions
```

Target framework: `net10.0`. AOT-compatible.

For full documentation see the [repository README](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/02.Caching/README.md).
