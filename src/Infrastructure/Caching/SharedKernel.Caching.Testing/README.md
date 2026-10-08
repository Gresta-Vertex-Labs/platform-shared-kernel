# SharedKernel.Caching.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **In-memory doubles for `SharedKernel.Caching.Abstractions` — the cache, the tenant cache, distributed locks and
> leases with fencing tokens, key providers and a recording warmup strategy — so caching code is unit-tested
> without FusionCache or Redis.**

Use it in unit tests of code that injects `ICacheService`, `ITenantCacheService` or `IDistributedLockService`. For
`IRedisHashService`, `ITypedHashStore<T>` or `IRedisChannelService`, use
[`SharedKernel.Caching.Redis.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Caching/SharedKernel.Caching.Redis.Testing/README.md).
Expiry, fail-safe and Redis behaviour need an integration test against the real packages.

| You get | So that |
| --- | --- |
| `FakeCacheService` with real hit/miss, tags and stampede protection | A test proves "the second read was a hit" with `FactoryInvocationCount` |
| `FakeTenantCacheService` built on the real `CacheKeyFormat` keys and tenant tags | Tenant isolation and `RemoveTenantAsync` behave as in production |
| `FakeDistributedLockService` with exclusivity and increasing fencing tokens | Single-execution and fencing logic is testable in-process |
| `SimulateContention`, `SimulateUnavailable`, `SimulateLoss()` | The "someone else holds it", "store is down" and "lock lost mid-work" paths are one flag away |
| `FakeCacheWarmupStrategy` with a shared execution log | Warmup ordering and failure isolation can be asserted |
| `AddFakeCachingServices()` | One call swaps the caching abstractions in a real `IServiceCollection` |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.Caching.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

Reference it from a **test project only**; production code must never reference a Testing package.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.Caching.Abstractions` (no FusionCache, no Redis) |
| Namespaces | `SharedKernel.Testing.Caching` |

## Quick start

```csharp
using SharedKernel.Caching.Abstractions;
using SharedKernel.Testing.Caching;
using Xunit;

public sealed class ProductCatalogTests
{
    [Fact]
    public async Task Second_read_is_served_from_the_cache()
    {
        var cache = new FakeCacheService();
        async Task<string> GetNameAsync() =>
            await cache.GetOrSetAsync(
                "catalog:product:42",
                _ => ValueTask.FromResult("Coffee"),
                CachePolicy.Default.WithTags("products"));

        Assert.Equal("Coffee", await GetNameAsync());
        Assert.Equal("Coffee", await GetNameAsync());

        Assert.Equal(1, cache.FactoryInvocationCount);
        Assert.Equal(new[] { "products" }, cache.GetTags("catalog:product:42"));
    }
}
```

In a real host or `WebApplicationFactory`, register the doubles instead of the production caching packages:

```csharp
services.AddFakeCachingServices();       // ICacheService, IDistributedLockService, key providers
services.AddFakeTenantCacheService();    // ITenantCacheService (separate call)
```

## How it works

- **Faithful:** a hit versus a miss (a cached `null` is a hit), tags and tag removal, the factory's
  `CacheFactoryContext.SkipCaching()` decision, and stampede protection — concurrent misses for one key serialise on a
  per-key gate and re-check the entry, so a waiter is served the stored value, or runs the factory itself when the
  first run skipped caching.
- **Simplified:** every call completes synchronously. Durations, fail-safe, eager refresh, jitter and factory timeouts
  from `CachePolicy` are ignored — entries never expire on their own, and `ExpireAsync` removes the entry exactly like
  `RemoveAsync`. A lookup with a type that does not match the stored value is a miss.
- **Tenant cache:** `FakeTenantCacheService` builds keys with `ITenantCacheKeyProvider.BuildTenantKey` and scopes
  tags with `CachePolicy.ForTenant`, so tenant A never sees tenant B's entry and `RemoveTenantAsync` drops only one
  tenant. The parameterless constructor — the one DI uses — owns its own `FakeCacheService` and a
  `FakeTenantCacheKeyProvider` (`"test-svc"`); reach the entries through `Cache`.
- **Locks:** a held lock or an unexpired lease makes further acquisitions of that resource return `null`; fencing
  tokens increase per resource across locks and leases. Every acquisition is one attempt — `DistributedLockOptions`
  (wait time) is not simulated. A lease is never released early; it frees the resource when the `TimeProvider`
  passes `ExpiresAt`.
- **Lifetimes and threading:** every registration is a singleton, so recorded state outlives the SUT's scope. All
  doubles are thread-safe.

## Recipes

### 1. Prove a tenant cannot read another tenant's entry

```csharp
using SharedKernel.Caching.Abstractions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Testing.Caching;

var cache = new FakeTenantCacheService();
var tenantA = new TenantId(Guid.NewGuid());
var tenantB = new TenantId(Guid.NewGuid());

await cache.SetAsync(tenantA, "invoice", "42", 100, CachePolicy.Default);

Assert.True((await cache.TryGetAsync<int>(tenantA, "invoice", "42")).IsHit);
Assert.False((await cache.TryGetAsync<int>(tenantB, "invoice", "42")).IsHit);
```

### 2. Test the "another replica holds the lock" path

```csharp
var locks = new FakeDistributedLockService { SimulateContention = true };

Assert.Null(await locks.TryAcquireAsync("nightly-export"));
Assert.Empty(locks.AcquiredLocks);
```

`SimulateUnavailable = true` makes every acquisition throw `DistributedLockUnavailableException` instead.

### 3. Cancel work when the lock is lost

```csharp
var locks = new FakeDistributedLockService();
var handle = (FakeDistributedLock)(await locks.TryAcquireAsync("rebuild-index"))!;

handle.SimulateLoss();

Assert.False(handle.IsHeld);
Assert.True(handle.LostToken.IsCancellationRequested);
```

### 4. Expire a lease with a controllable clock

Pass any `TimeProvider` you can advance (for example `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`):

```csharp
using Microsoft.Extensions.Time.Testing;
using SharedKernel.Testing.Caching;

var time = new FakeTimeProvider();
var locks = new FakeDistributedLockService(time);

var lease = await locks.TryAcquireLeaseAsync("job:daily", TimeSpan.FromMinutes(5));
Assert.Null(await locks.TryAcquireLeaseAsync("job:daily", TimeSpan.FromMinutes(5)));

time.Advance(TimeSpan.FromMinutes(6));
var next = await locks.TryAcquireLeaseAsync("job:daily", TimeSpan.FromMinutes(5));
Assert.True(next!.FencingToken > lease!.FencingToken);
```

Under `AddFakeCachingServices()`, the lock service uses a `TimeProvider` registered in the container, else
`TimeProvider.System`.

### 5. Assert warmup order and failure isolation

```csharp
using System.Collections.Concurrent;
using SharedKernel.Testing.Caching;

var log = new ConcurrentQueue<string>();
services.AddFakeCacheWarmupStrategy("products", order: 1, log);
services.AddFakeCacheWarmupStrategy("prices", order: 2, log);
// ... run the host's warmup ...
Assert.Equal(new[] { "products", "prices" }, log);
```

Set `SimulateFailure = true` on a `FakeCacheWarmupStrategy` to make it throw `InvalidOperationException` after it
has recorded its run.

## Reference

### Registration

| Method | Registers (all singletons) |
| --- | --- |
| `AddFakeCachingServices(this IServiceCollection)` | `ICacheService` → `FakeCacheService`, `IDistributedLockService` → `FakeDistributedLockService`, `FakeTenantCacheKeyProvider` as itself, `ICacheKeyProvider` and `ITenantCacheKeyProvider` (one instance) |
| `AddFakeTenantCacheService(this IServiceCollection)` | `ITenantCacheService` → `FakeTenantCacheService` |
| `AddFakeCacheWarmupStrategy(this IServiceCollection, string name, int order = 0, ConcurrentQueue<string>? executionLog = null)` | One `ICacheWarmupStrategy` per call |

The registrations are added, not `TryAdd`ed: added after the production ones, they win a single-service resolution.
Resolve the interface and cast to the fake type to reach its inspection members.

### Types

| Type | Implements | Inspection and control |
| --- | --- | --- |
| `FakeCacheService` | `ICacheService` | `Count`, `FactoryInvocationCount`, `GetTags(key)`, `Clear()` (also resets the factory count) |
| `FakeTenantCacheService` | `ITenantCacheService` | `Cache` (the underlying `FakeCacheService`), `Count` (all tenants), `Reset()`; constructors `()` and `(FakeCacheService, ITenantCacheKeyProvider)` |
| `FakeDistributedLockService` | `IDistributedLockService` | `AcquiredLocks`, `AcquiredLeases` (acquisition order), `SimulateContention`, `SimulateUnavailable`; constructors `()` and `(TimeProvider)` |
| `FakeDistributedLock` | `IDistributedLock` | `IsHeld`, `IsReleased` (released by disposal), `SimulateLoss()` |
| `FakeTenantCacheKeyProvider` | `ITenantCacheKeyProvider` (and `ICacheKeyProvider`) | `ServiceName`; `DefaultServiceName = "test-svc"`; an invalid service name throws `ArgumentException` |
| `FakeCacheWarmupStrategy` | `ICacheWarmupStrategy` | `Name`, `Order`, `CallCount`, `OnWarmup` callback, `SimulateFailure` |

## Testing

This package is the test double; its own self-tests live in
[`SharedKernel.Caching.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Caching/SharedKernel.Caching.Testing/SharedKernel.Caching.Testing.Tests)
and prove each fake against the `SharedKernel.Caching.Abstractions` contract (stampede split, tag eviction, tenant
isolation, fencing order, lease expiry). Pair it with
[`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Testing/README.md)
for `FakeClock`, `TestRequestContext` and the in-memory logger. The Redis-specific hash store and Pub/Sub fakes are in
[`SharedKernel.Caching.Redis.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Caching/SharedKernel.Caching.Redis.Testing/README.md);
`AddFakeCachingServices()` does not register them.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference this package from a production project | Reference it from test projects only | The architecture tests fail a production reference, and the fakes do no real caching |
| Test expiry, fail-safe or eager refresh against `FakeCacheService` | Test them against FusionCache (and Redis) in an integration test | The fake ignores every duration in `CachePolicy` |
| Expect `AddFakeTenantCacheService()` to share the `ICacheService` fake | Assert through `((FakeTenantCacheService)tenantCache).Cache` | The DI-built tenant fake owns its own `FakeCacheService` |
| Expect a lease to free the resource before it expires | Advance the `TimeProvider` past `ExpiresAt` | Leases have no release; only expiry frees them |
| Expect `DistributedLockOptions` wait time to retry | Model retries in the SUT, or use `SimulateContention` | Every acquisition is a single attempt |
| Share one fake across parallel tests that assert counts | Create a fresh fake per test, or `Clear()`/`Reset()` it | Counts and entries are global to the instance |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Caching packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Caching/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
