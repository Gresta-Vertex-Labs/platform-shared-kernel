# SharedKernel.Caching.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**In-memory doubles for `SharedKernel.Caching.Abstractions`: the cache, the tenant cache, distributed locks and
leases with fencing tokens, key providers and a recording warmup strategy.** They follow the production contract —
a cached `null` is a hit, tags evict across keys, a tenant's keys never collide with another tenant's — so code
tested against them behaves the same on FusionCache and Redis.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.Caching.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespace: `SharedKernel.Testing.Caching`.

## Contents

| Type | Stands in for | Notes |
| --- | --- | --- |
| `FakeCacheService` | `ICacheService` | `TryGetAsync`, stampede-shaped `GetOrSetAsync` (`FactoryInvocationCount`), `SetAsync`/`SetManyAsync`, tag removal, `ExpireAsync`, `ClearAsync`; `Count`, `GetTags(key)`, `Clear()` |
| `FakeTenantCacheService` | `ITenantCacheService` | Tenant-scoped keys and tags built with the real `CacheKeyFormat` rules, `RemoveTenantAsync`; `Cache` exposes the underlying `FakeCacheService` |
| `FakeDistributedLockService` / `FakeDistributedLock` | `IDistributedLockService` / `IDistributedLock` | Locks and leases with increasing fencing tokens; `SimulateContention` (acquire returns `null`), `SimulateUnavailable` (throws `DistributedLockUnavailableException`), `FakeDistributedLock.SimulateLoss()` trips `LostToken`; `AcquiredLocks`, `AcquiredLeases`. Lease expiry follows a registered `TimeProvider` |
| `FakeTenantCacheKeyProvider` | `ICacheKeyProvider`, `ITenantCacheKeyProvider` | Real key format with service name `"test-svc"` by default |
| `FakeCacheWarmupStrategy` | `ICacheWarmupStrategy` | Fixed `Name`/`Order`, `CallCount`, `OnWarmup` callback, optional shared execution log, `SimulateFailure` |

## Registration

```csharp
services.AddFakeCachingServices();                  // FakeCacheService, FakeDistributedLockService, FakeTenantCacheKeyProvider
services.AddFakeTenantCacheService();               // ITenantCacheService (not part of the bundle)
services.AddFakeCacheWarmupStrategy("products", order: 1, executionLog);   // once per strategy
```

All singletons. Resolve the interface and cast to the fake type to reach its assertion members, or register your
own instance (`services.AddSingleton<ICacheService>(cache)`) to keep a typed reference. The Redis-specific hash
store and pub/sub fakes are in [`SharedKernel.Caching.Redis.Testing`](../SharedKernel.Caching.Redis.Testing/README.md)
(`AddFakeRedisServices()`); `AddFakeCachingServices()` does not register them.

## Example

```csharp
var cache = new FakeCacheService();
var catalog = new CachedProductCatalog(cache, repository);

await catalog.GetAsync(productId, ct);
await catalog.GetAsync(productId, ct);

cache.FactoryInvocationCount.Should().Be(1);        // the second read was a cache hit
```

```csharp
var locks = new FakeDistributedLockService { SimulateContention = true };
var job = new NightlyExport(locks);

(await job.RunAsync(ct)).Should().BeFalse();        // another replica holds the lock
```

## Related packages

- References `SharedKernel.Caching.Abstractions` only — no FusionCache, no Redis.
- [`SharedKernel.Testing`](../SharedKernel.Testing/README.md) supplies the shared basics (`FakeClock`,
  `InMemoryLogger`, `TestRequestContext`, fakers and assertions).
