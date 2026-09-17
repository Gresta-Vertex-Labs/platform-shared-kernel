# 02.Caching

Hybrid caching and distributed locking for SharedKernel services.

Application code depends only on `SharedKernel.Caching.Abstractions`. The composition root chooses the providers. A service that needs only an in-process cache references no Redis package at all.

## Packages

| Package | Purpose |
| --- | --- |
| [`SharedKernel.Caching.Abstractions`](SharedKernel.Caching.Abstractions/README.md) | `ICacheService`, `ITenantCacheService`, `CachePolicy`, `CacheKeyFormat`, `IDistributedLockService`. No provider dependency |
| [`SharedKernel.Caching.FusionCache`](SharedKernel.Caching.FusionCache/README.md) | The FusionCache implementation of the cache contracts (memory layer, compression, encryption, warmup) |
| [`SharedKernel.Caching.Redis`](SharedKernel.Caching.Redis/README.md) | Redis distributed layer and backplane for the FusionCache implementation |
| [`SharedKernel.Caching.Redis.DistributedLocking`](SharedKernel.Caching.Redis.DistributedLocking/README.md) | Redis locks and leases with atomic fencing tokens |
| [`SharedKernel.Caching.Redis.HashStore`](SharedKernel.Caching.Redis.HashStore/README.md) | Redis hash storage (`IRedisHashService`, `ITypedHashStore<T>`) |
| [`SharedKernel.Caching.Redis.PubSub`](SharedKernel.Caching.Redis.PubSub/README.md) | Ephemeral Redis Pub/Sub signaling (`IRedisChannelService`) |
| [`SharedKernel.Caching.Redis.Core`](SharedKernel.Caching.Redis.Core/README.md) | The shared Redis connection, health tracking and resilience used by every Redis package |

## Typical composition

```csharp
builder.Services
    .AddSharedKernelCaching(o => o.ServiceName = "orders")   // ICacheService, key providers
    .AddTenantCacheService()                                  // ITenantCacheService
    .AddRedisL2(redisConnectionString)                        // shared across instances + backplane
    .AddRedisDistributedLocking(redisConnectionString);       // IDistributedLockService
```

With the distributed layer configured:

- entries are shared by every instance of the service;
- removals, expirations, tag evictions and clears reach every instance through the backplane.

No separate invalidation messaging is needed.

## Layering

```text
Redis.DistributedLocking ─┐
Redis.HashStore ──────────┼─→ Abstractions + Redis.Core (connection, health, resilience)
Redis.PubSub ─────────────┤
Redis (L2) ───────────────┘
FusionCache ─────────────→ Abstractions
```

Sibling provider packages never reference each other. `07.Messaging` never references any caching package, and no caching package references messaging.
