# 02.Caching

Hybrid caching, distributed locking and Redis data structures for SharedKernel services.

Application code depends on `SharedKernel.Caching.Abstractions` for the cache and lock contracts, and on the Redis
packages' own contracts (`IRedisHashService`, `IRedisChannelService`) only where it needs those Redis features. The
composition root chooses the providers. A service that needs only an in-process cache references no Redis package.

## Packages

| Package | Purpose |
| --- | --- |
| [`SharedKernel.Caching.Abstractions`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/02.Caching/SharedKernel.Caching.Abstractions/README.md) | `ICacheService`, `ITenantCacheService`, `CachePolicy`, `CacheKeyFormat`, `IDistributedLockService`. No provider dependency |
| [`SharedKernel.Caching.FusionCache`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/02.Caching/SharedKernel.Caching.FusionCache/README.md) | The FusionCache implementation of the cache contracts: memory layer, stampede protection, fail-safe, compression, encryption, warmup |
| [`SharedKernel.Caching.Redis.Core`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/02.Caching/SharedKernel.Caching.Redis.Core/README.md) | The one shared Redis connection (`AddRedisConnection`): configuration, timeouts, fail-fast, TLS and mutual TLS, connection logs, `IRedisConnectionProbe` |
| [`SharedKernel.Caching.Redis`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/02.Caching/SharedKernel.Caching.Redis/README.md) | Redis distributed layer and backplane for the cache (`AddRedisL2`), with FusionCache circuit breakers |
| [`SharedKernel.Caching.Redis.DistributedLocking`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/02.Caching/SharedKernel.Caching.Redis.DistributedLocking/README.md) | Redis locks and leases with atomic fencing tokens (`AddRedisDistributedLocking`) |
| [`SharedKernel.Caching.Redis.HashStore`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/02.Caching/SharedKernel.Caching.Redis.HashStore/README.md) | Redis hashes for sessions, snapshots and counters (`IRedisHashService`, `ITypedHashStore<T>`) |
| [`SharedKernel.Caching.Redis.PubSub`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/02.Caching/SharedKernel.Caching.Redis.PubSub/README.md) | Loss-tolerant Redis Pub/Sub signals with disposable subscriptions (`IRedisChannelService`) |

## Typical composition

```json
{
  "SharedKernel": {
    "Caching": {
      "ServiceName": "orders",
      "Redis": {
        "ConnectionString": "redis.internal:6380",
        "Ssl": true
      }
    }
  }
}
```

```csharp
builder.Services.AddRedisConnection(builder.Configuration);      // the shared connection, once

builder.Services
    .AddSharedKernelCaching(builder.Configuration)               // ICacheService, key providers
    .AddTenantCacheService()                                      // ITenantCacheService
    .AddRedisL2()                                                 // shared across instances + backplane
    .AddRedisDistributedLocking();                                // IDistributedLockService

builder.Services.AddRedisHashService();                          // IRedisHashService (only if needed)
builder.Services.AddRedisChannelService();                       // IRedisChannelService (only if needed)
```

- **One connection string.** Only `AddRedisConnection` reads Redis connection settings. Every other Redis
  registration takes no connection string and throws `InvalidOperationException` when `AddRedisConnection` has not
  been called first.
- **Any subset.** A lock-only worker calls `AddRedisConnection(...).AddRedisDistributedLocking()` and needs no cache.
- **No invalidation messaging.** With the distributed layer configured, removals, expirations, tag evictions and clears
  reach every instance through the backplane.

## Layering

```text
Redis (L2) ────────────────┐
Redis.DistributedLocking ──┼─→ Abstractions + Redis.Core (connection, probe)
Redis.HashStore ───────────┤
Redis.PubSub ──────────────┴─→ Redis.Core
FusionCache ──────────────→ Abstractions
Redis.Core ───────────────→ SharedKernel.Configuration, SharedKernel.Primitives, StackExchange.Redis
```

Sibling provider packages never reference each other, and `Redis.Core` references no caching package. `07.Messaging`
never references any caching package, and no caching package references messaging.
