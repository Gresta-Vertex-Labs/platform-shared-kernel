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

**Confirmed NuGet versions:**
- ZiggyCreatures.FusionCache: 2.6.0
- ZiggyCreatures.FusionCache.Serialization.SystemTextJson: 2.6.0
- ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis: 2.6.0
- StackExchange.Redis: 2.13.1
- RedLock.net: 2.3.2
- Microsoft.Extensions.DependencyInjection.Abstractions: 10.0.1
- Microsoft.Extensions.Caching.StackExchangeRedis: 10.0.0

**SharedKernel.Caching.Redis.csproj** explicitly includes FusionCache packages (ZiggyCreatures.FusionCache + Serialization.SystemTextJson + Backplane) because it no longer references SharedKernel.Caching transitively.

**Why:** Redis package must be independently deployable without pulling FusionCache via SharedKernel.Caching. FusionCache references are needed for AddFusionCache() and WithSystemTextJsonSerializer() in AddRedisL2.

**How to apply:** When adding features to SharedKernel.Caching.Redis that need FusionCache APIs, confirm the package has its own explicit FusionCache references.
