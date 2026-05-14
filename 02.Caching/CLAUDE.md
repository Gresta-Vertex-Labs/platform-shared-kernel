# 02.Caching — Domain Brain

## What This Domain Is

The caching capability domain. Provides a hybrid L1 (in-process memory) / L2 (Redis distributed) cache abstraction powered by **ZiggyCreatures.FusionCache** with built-in stampede protection, background refresh, and fail-safe. The Redis provider lives in a separate package so microservices that only need L1 can stay Redis-free.

Philosophy: **Fail-silent by default. Stampede-proof. AOT-compatible.**

---

## Packages

| Package | Role | References |
|---------|------|-----------|
| `SharedKernel.Caching` | Core abstraction + FusionCache L1 wiring — `ICacheService`, `CachePolicy` | `01.Core` only |
| `SharedKernel.Caching.Redis` | Redis L2 distributed provider, RedLock distributed locking — `IDistributedLockService` | `SharedKernel.Caching`, `01.Core` |

Both target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`).

---

## Technology Stack

| Concern | Technology |
|---------|-----------|
| L1 cache (in-process) | `ZiggyCreatures.FusionCache` |
| L2 cache (distributed) | `ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis` |
| Distributed locking | `RedLock.net` (over Redis) |
| Redis client | `StackExchange.Redis` |
| Serialization | `System.Text.Json` source-generated contexts (AOT-safe) |

---

## Interface Contracts

### `SharedKernel.Caching` — public surface

```
ICacheService
    GetAsync<T>(string key, CancellationToken ct)                                        → T?
    SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct)          → void
    GetOrSetAsync<T>(string key, Func<CancellationToken,Task<T>> factory, CachePolicy)  → T
    RemoveAsync(string key, CancellationToken ct)                                        → void
    RemoveByTagAsync(string tag, CancellationToken ct)                                   → void

CachePolicy  (sealed immutable record)
    .Default                              → 5 min L1, 30 min L2, fail-safe on
    .For(TimeSpan l1, TimeSpan l2)
    .WithTags(params string[] tags)
    .WithEagerRefresh(double threshold)   → default 0.9 (90 % of TTL)
```

### `SharedKernel.Caching.Redis` — public surface

```
IDistributedLockService
    AcquireAsync(string resource, TimeSpan expiry, TimeSpan wait, TimeSpan retry, CancellationToken ct)
    → IAsyncDisposable?   (null = lock not acquired within wait; callers decide fallback)
```

---

## Implementation Rules

- `ICacheService` is **always** backed by FusionCache — never raw `IMemoryCache` or `IDistributedCache`.
- `CachePolicy` is a sealed, immutable record — no subclassing, no mutation after construction.
- Cache keys are prefix-namespaced by the **consuming service**, not by this package.
- Callers **must** use `GetOrSetAsync` to get stampede protection. `GetAsync` + `SetAsync` in sequence is a bug.
- The Redis L2 backplane is opt-in. When `AddRedisL2` is not called, `ICacheService` silently operates L1-only.
- `IDistributedLockService.AcquireAsync` returns `null` on timeout — it must never throw for a contended lock.
- STJ serialization contexts must be registered by the consuming project. This package ships only the base `JsonSerializerContext` helper class.
- No static mutable state anywhere in this domain.

---

## DI Registration (expected shape)

```csharp
// L1-only
services.AddSharedKernelCaching(options => { });

// L1 + L2 (Redis backplane)
services.AddSharedKernelCaching(options => { })
        .AddRedisL2(connectionString, options => { });

// Distributed locking (requires Redis, independent of L2 cache)
services.AddRedisDistributedLocking(connectionString);
```

---

## AOT Compatibility

- All serialization uses STJ source-generated contexts — no reflection-based serializer.
- FusionCache is AOT-compatible as of v1.x — verify release notes on every upgrade.
- StackExchange.Redis is AOT-compatible — avoid dynamic configuration patterns.
- RedLock.net — verify AOT status on each major upgrade and wrap if needed.

---

## Test Rules

- Unit tests → `SharedKernel.Caching/SharedKernel.Caching.Tests/`
- Integration tests → `SharedKernel.Caching.Redis/SharedKernel.Caching.Redis.Tests/`
- Redis integration tests **must** use Testcontainers (`16.Testing/SharedKernel.Testing`) — no external Redis dependency.
- Stampede protection must be covered: parallel `GetOrSetAsync` calls verifying factory is invoked exactly once.
- RedLock tests must cover: acquire success, acquire timeout (returns `null`), release on dispose, lock expiry.
- L1-only fallback must be covered: stop the Redis container and assert cache degrades gracefully.

---

## Changelog

> Maintained by the caching domain agent. One line per significant change.

- [2026-05-14] Domain brain initialized — packages, interfaces, rules, AOT notes
