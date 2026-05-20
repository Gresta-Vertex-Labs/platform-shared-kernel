---
name: project-caching-state
description: Three-package split for 02.Caching, phase history through WO-006, interface locations, layering rules, AOT decisions
metadata:
  type: project
---

# Caching Domain State

## Three-Package Split (enforced after WO-006 / Phase 17)

| Package | Role |
| ------- | ---- |
| `SharedKernel.Caching.Abstractions` | Zero-infra contracts only — no FusionCache, no StackExchange.Redis |
| `SharedKernel.Caching.FusionCache` | FusionCache L1 provider + CacheKeyProvider implementation. Renamed from `SharedKernel.Caching` in Phase 14 (WO-004). |
| `SharedKernel.Caching.Redis` | Redis L2, RedLock, RedisChannelService, RedisHashService, TypedHashStore, RedisCacheInvalidationBus, CacheInvalidationReceiver |

**Layering rule (Phase 17):** Redis and FusionCache packages are siblings — they must never reference each other. Both reference only Abstractions.

**Why:** `05.Application` needs `ICacheService` without pulling in FusionCache. A service needing only Redis (channel, hash, locking) must not be forced to take a transitive FusionCache dependency.

**How to apply:** Any new interface goes in `SharedKernel.Caching.Abstractions`. Shared options that both providers need (e.g. `ServiceName`) go in `CachingCoreOptions` (Abstractions), not `CachingOptions` (FusionCache).

## Interface Canonical Locations

- `ICacheService` — `SharedKernel.Caching.Abstractions`
- `CachePolicy` — `SharedKernel.Caching.Abstractions`
- `ICacheKeyProvider` — `SharedKernel.Caching.Abstractions`
- `IDistributedLockService` — `SharedKernel.Caching.Abstractions`
- `IRedisChannelService` — `SharedKernel.Caching.Abstractions`
- `IRedisHashService` — `SharedKernel.Caching.Abstractions`
- `ITypedHashStore<T>` — `SharedKernel.Caching.Abstractions`
- `ICacheInvalidationBus` — `SharedKernel.Caching.Abstractions`
- `CacheInvalidationMessage` + `CacheInvalidationType` — `SharedKernel.Caching.Abstractions`
- `ICachingBuilder` — `SharedKernel.Caching.Abstractions`
- `CachingCoreOptions` — `SharedKernel.Caching.Abstractions` (added Phase 17; holds `ServiceName`)
- `CacheKeyProvider` — `SharedKernel.Caching.FusionCache`
- `FusionCacheService` — `SharedKernel.Caching.FusionCache`
- `CachingOptions` — `SharedKernel.Caching.FusionCache` (retains `ServiceName` for validation + FusionCache-specific fields)
- `BrotliCacheSerializer` — `SharedKernel.Caching.FusionCache`
- `RedLockDistributedLockService` — `SharedKernel.Caching.Redis`
- `RedisChannelService` — `SharedKernel.Caching.Redis`
- `RedisHashService` — `SharedKernel.Caching.Redis`
- `TypedHashStore<T>` — `SharedKernel.Caching.Redis` (internal sealed)
- `RedisCacheInvalidationBus` — `SharedKernel.Caching.Redis`
- `CacheInvalidationReceiver` — `SharedKernel.Caching.Redis`

## Phase History Summary

- Phases 1–4 (Design/Scaffold/Core/Tests/Docs/Published): complete
- Phase 5 (Abstractions package): complete
- Phase 6 (CachingRefactor + CacheKeyProvider): complete
- Phase 7 (RedisRefactor + Channel + Hash): complete
- Phase 12 (InvalidationBus): complete
- Phase 14 (FusionCacheRename + NeverExpire): complete
- Phase 15 (AotHardening + ITypedHashStore): complete — 91 FC + 74 Redis tests passing
- Phase 16 (BrotliCompression): complete — BrotliCacheSerializer, magic bytes 0x42 0x52
- Phase 17 (LayeringFix): pending — CachingCoreOptions in Abstractions; remove Redis→FusionCache project ref
- Phase 18 (AotSerializerFix): pending — remove WithSystemTextJsonSerializer from AddRedisL2
- Phase 19 (DiErgonomics): pending — ICachingBuilder overload for AddRedisDistributedLocking; startup guards; depends on Phase 17
- Phase 20 (L1SizeLimit): pending — wire L1SizeLimit to MemoryCacheOptions.SizeLimit; L2 KeyPrefix integration test
- Phase 21 (ValueTaskFactory): pending — breaking change: GetOrSetAsync factory Func(CT,Task) → Func(CT,ValueTask); nullable overload

## Key Design Decisions (current state)

- `ICacheService.GetOrSetAsync` factory is `Func<CancellationToken, ValueTask<T>>` (after Phase 21 — breaking change at v1.0.0).
- Nullable overload `GetOrSetAsync<T?>` caches null as a valid result (Phase 21). Uses FusionCache `MaybeValue<T>`.
- `AddRedisL2` must NOT call `.WithSystemTextJsonSerializer()` — use `.WithRegisteredSerializer()` (Phase 18 fix).
- `CachingCoreOptions.ServiceName` (Abstractions) is the source for channel naming in Redis. `CachingOptions.ServiceName` (FusionCache) retains validation; `AddSharedKernelCaching` syncs both at startup (Phase 17).
- `L1SizeLimit` is an entry COUNT limit (not bytes). Default 10,000. Each entry has `Size = 1` (Phase 20).
- `AddRedisDistributedLocking` canonical overload on `ICachingBuilder`; `IServiceCollection` overload is `[Obsolete]` (Phase 19).
- `AddRedisChannelService` guards for `IConnectionMultiplexer` at registration time (Phase 19).
- `AddCacheInvalidationReceiver` guards for `IRedisChannelService` and `ICacheService` at registration time (Phase 19).
- `IRedisHashService` typed methods take `JsonTypeInfo<T>` parameter — no reflection, AOT-safe.
- `IRedisChannelService` uses `RedisChannel.Literal` only — never pattern matching.
- All three Redis services share the single `IConnectionMultiplexer` singleton — no connection proliferation.
- `BrotliCacheSerializer` magic bytes: `0x42 0x52` ("BR"). ArrayPool, no MemoryStream on hot path.
- Broadcast invalidation (`All`) logs a structured warning and relies on TTL — no enumerate-all-keys.

## Invalidation Channel Naming Convention

- Targeted: `sharedkernel:cache:invalidation:{service-name}` (lowercase, spaces → hyphens)
- Broadcast: `sharedkernel:cache:invalidation:broadcast` (literal constant)
- `ServiceName` sourced from `IOptions<CachingCoreOptions>` in Redis (post Phase 17)

## NuGet Version Pins

- ZiggyCreatures.FusionCache: 2.6.0
- ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis: 2.6.0
- ZiggyCreatures.FusionCache.Serialization.SystemTextJson: 2.6.0
- StackExchange.Redis: 2.13.1
- RedLock.net: 2.3.2
- Microsoft.Extensions.DependencyInjection.Abstractions: 10.0.1
