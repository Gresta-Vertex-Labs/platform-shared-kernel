---
name: project-caching-domain
description: Three-package split architecture, confirmed NuGet versions, and key implementation decisions for 02.Caching
metadata:
  type: project
---

# SharedKernel Caching Domain — Architecture & Versions

**Three-package split (established Phase 5–7, renamed Phase 14):**
- `SharedKernel.Caching.Abstractions` — zero-infra contracts only; refs `Microsoft.Extensions.DependencyInjection.Abstractions 10.0.1`
- `SharedKernel.Caching.FusionCache` — FusionCache L1 provider; refs Abstractions + FusionCache packages + 01.Core
- `SharedKernel.Caching.Redis` — Redis L2, RedLock, RedisChannelService, RedisHashService, RedLockRenewableLock; refs Abstractions directly (NOT SharedKernel.Caching.FusionCache)

**WO-023 (Phases 32-36, in progress) — Redis package split into 5:** see
[[project_redis_package_split]] for details. Phase 32 created
`SharedKernel.Caching.Redis.Core` (dependency root: `AddRedisConnection`,
`AddRedisCircuitBreaker`, `RedisConnectionHealthTracker`). Phase 33 (complete,
2026-06-11) refactored `SharedKernel.Caching.Redis`'s `AddRedisL2` to consume Core
instead of self-registering `IConnectionMultiplexer`/`ResiliencePipeline`. Test
baseline after Phase 33: **154 Redis + 33 Redis.Core tests passing** (FusionCache
tests unaffected — still 209 from Phase 31).

**Phase 29 (TenantCacheKey) — key decisions:**

- `ITenantCacheKeyProvider` extends `ICacheKeyProvider` — lives in `SharedKernel.Caching.Abstractions`. Zero dependency on `12.Security` or `IHttpContextAccessor`.
- `TenantCacheKeyProvider` is `internal sealed` in `SharedKernel.Caching.FusionCache`. Constructor takes `IOptions<CachingCoreOptions>` (from Abstractions, not `CachingOptions` from FusionCache).
- `AddTenantCacheKeyProvider(this ICachingBuilder)` uses `TryAddSingleton<ITenantCacheKeyProvider, TenantCacheKeyProvider>()` — does NOT touch the existing `ICacheKeyProvider → CacheKeyProvider` registration. Both coexist.
- `FakeTenantCacheKeyProvider` lives in `16.Testing/SharedKernel.Testing/Caching/` — default service name `"test-svc"`, accepts custom name via constructor.
- Test baseline after Phase 29: **196 FusionCache + 142 Redis tests passing**.
- `InternalsVisibleTo` was already set in FusionCache.csproj for the test project — no csproj changes needed to access `internal sealed TenantCacheKeyProvider` in tests.

**Phase 25 (KeyVersioning) — key decisions:**
- `CachePolicy.KeyVersion` defaults to `0`. `WithVersion(int version)` uses `this with { KeyVersion = version }` pattern (immutable record).
- `ArgumentOutOfRangeException.ThrowIfNegative(version)` guards against negative values in both `WithVersion` and `CacheKeyProvider.BuildKey`.
- `CacheKeyProvider.BuildKey(entity, id, version, params extraSegments)`: when version == 0, delegates to the parameterless overload for identical output. When version > 0, appends `:v{version}` as the last segment using pre-allocated `string[]` (same performance pattern as existing overload).
- `ICacheService` signatures are NOT changed — key versioning is caller's responsibility via `ICacheKeyProvider`.
- Test baseline after Phase 25: **160 FusionCache + 125 Redis tests passing**.
- Any class implementing `ICacheKeyProvider` (e.g., test stubs) must implement BOTH `BuildKey` overloads — check for all `ICacheKeyProvider` implementors when adding interface members.

**Phase 23 (RenewableLock) — key decision:**
- RedLock.net 2.3.2 has NO public `ExtendAsync` on `IRedLock`. The internal timer-based auto-extension exists but is inaccessible.
- `RedLockRenewableLock` uses re-acquisition: dispose old lock first, then `CreateLockAsync` on same resource. Brief unprotected window is unavoidable on single-node Redis.
- `SemaphoreSlim` must NOT be disposed in `DisposeAsync` — `RenewAsync` may be called concurrently/after disposal. Use `volatile bool _disposed` as fast-path check before touching the semaphore.
- `FakeDistributedLockService` and `FakeRenewableLock` live in `16.Testing/SharedKernel.Testing/Caching/`.

**Phase 30 (RedisCircuitBreaker) — key decisions:**

- `Polly.Core 8.5.2` added to `SharedKernel.Caching.Redis` only. AOT-compatible at this version.
- Polly v8 is **ratio-based**, not count-based. To emulate count-based semantics: `FailureRatio = 1.0` + `MinimumThroughput = FailureThreshold`. This means "all calls must fail AND minimum count must be reached."
- **`BreakDuration` minimum is 500ms** (Polly v8 enforces validation). Tests using short durations must use `TimeSpan.FromMilliseconds(500)` as the floor, not `100ms`.
- `ResiliencePipeline` registered as singleton **only when `Enabled = true`**. Services use `sp.GetService<ResiliencePipeline>()` (nullable, returns null when not registered).
- Changed `TryAddSingleton<TService, TImplementation>()` to factory lambdas in `AddRedisHashService` and `AddRedisChannelService` to support optional `ResiliencePipeline` from DI.
- FusionCache backplane does NOT use the circuit breaker — FusionCache's fail-safe handles L2 unavailability at that level.
- Test baseline after Phase 30: **196 FusionCache + 154 Redis tests passing**.

**Confirmed NuGet versions:**
- ZiggyCreatures.FusionCache: 2.6.0
- ZiggyCreatures.FusionCache.Serialization.SystemTextJson: 2.6.0
- ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis: 2.6.0
- StackExchange.Redis: 2.13.1
- RedLock.net: 2.3.2
- Microsoft.Extensions.DependencyInjection.Abstractions: 10.0.1
- Microsoft.Extensions.Caching.StackExchangeRedis: 10.0.0
- Polly.Core: 8.5.2

**SharedKernel.Caching.Redis.csproj** explicitly includes FusionCache packages (ZiggyCreatures.FusionCache + Serialization.SystemTextJson + Backplane) because it no longer references SharedKernel.Caching transitively.

**Why:** Redis package must be independently deployable without pulling FusionCache via SharedKernel.Caching. FusionCache references are needed for AddFusionCache() and WithSystemTextJsonSerializer() in AddRedisL2.

**How to apply:** When adding features to SharedKernel.Caching.Redis that need FusionCache APIs, confirm the package has its own explicit FusionCache references.
