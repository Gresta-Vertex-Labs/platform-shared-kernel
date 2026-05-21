---
name: project-caching-state
description: Three-package split for 02.Caching, phase history through WO-007, interface locations, layering rules, AOT decisions
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
- `IRenewableLock` — `SharedKernel.Caching.Abstractions` (planned Phase 23)
- `IRedisChannelService` — `SharedKernel.Caching.Abstractions`
- `IRedisHashService` — `SharedKernel.Caching.Abstractions`
- `ITypedHashStore<T>` — `SharedKernel.Caching.Abstractions`
- `ICacheInvalidationBus` — `SharedKernel.Caching.Abstractions`
- `CacheInvalidationMessage` + `CacheInvalidationType` — `SharedKernel.Caching.Abstractions`
- `ICachingBuilder` — `SharedKernel.Caching.Abstractions`
- `CachingCoreOptions` — `SharedKernel.Caching.Abstractions` (added Phase 17; holds `ServiceName`)
- `ConnectionHealthState` — `SharedKernel.Caching.Abstractions` (planned Phase 26; enum: Connected/Reconnecting/Disconnected)
- `ICacheWarmupStrategy` — `SharedKernel.Caching.Abstractions` (planned Phase 28)
- `ITenantCacheKeyProvider` — `SharedKernel.Caching.Abstractions` (planned Phase 29; extends ICacheKeyProvider)
- `CacheKeyProvider` — `SharedKernel.Caching.FusionCache`
- `TenantCacheKeyProvider` — `SharedKernel.Caching.FusionCache` (planned Phase 29)
- `CacheWarmupHostedService` — `SharedKernel.Caching.FusionCache` (planned Phase 28)
- `FusionCacheService` — `SharedKernel.Caching.FusionCache`
- `CachingOptions` — `SharedKernel.Caching.FusionCache` (retains `ServiceName` for validation + FusionCache-specific fields)
- `BrotliCacheSerializer` — `SharedKernel.Caching.FusionCache`
- `RedLockDistributedLockService` — `SharedKernel.Caching.Redis`
- `RedLockRenewableLock` — `SharedKernel.Caching.Redis` (planned Phase 23)
- `RedisChannelService` — `SharedKernel.Caching.Redis`
- `RedisHashService` — `SharedKernel.Caching.Redis`
- `TypedHashStore<T>` — `SharedKernel.Caching.Redis` (internal sealed)
- `IRedisL2BatchService` — `SharedKernel.Caching.Redis` (internal — planned Phase 22)
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
- Phase 17 (LayeringFix): complete — CachingCoreOptions in Abstractions; removed Redis→FusionCache project ref
- Phase 18 (AotSerializerFix): complete — removed WithSystemTextJsonSerializer from AddRedisL2
- Phase 19 (DiErgonomics): complete — ICachingBuilder overload for AddRedisDistributedLocking; startup guards
- Phase 20 (L1SizeLimit): complete — L1SizeLimit wired; L2 key format verified as {KeyPrefix}v2:{user-key}
- Phase 21 (ValueTaskFactory): complete — GetOrSetAsync factory to ValueTask{T}; nullable overload; 102 FC + 93 Redis tests
- Phase 22 (BatchOperations): PENDING — GetManyAsync/SetManyAsync; Redis pipeline helper (internal IRedisL2BatchService)
- Phase 23 (RenewableLock): PENDING — IRenewableLock + AcquireRenewableAsync + KeepAliveAsync
- Phase 24 (SlidingExpiration): PENDING — CachePolicy.Sliding + SlidingWindow; L1-only sliding; NeverExpire guard
- Phase 25 (KeyVersioning): PENDING — CachePolicy.KeyVersion + WithVersion + ICacheKeyProvider version overload
- Phase 26 (ChannelReconnect): PENDING — RedisChannelService reconnect + ConnectionHealthState enum
- Phase 27 (CachingCoreOptionsDi): PENDING — AddCachingCoreOptions standalone extension in Abstractions
- Phase 28 (CacheWarmup): PENDING — ICacheWarmupStrategy + CacheWarmupHostedService + AddCacheWarmup<T>
- Phase 29 (TenantCacheKey): PENDING — ITenantCacheKeyProvider; format {service}:{tenant}:{entity}:{id}
- Phase 30 (RedisCircuitBreaker): PENDING — Polly v8 opt-in circuit breaker for RedisL2; new dep Polly.Core
- Phase 31 (OtelMeters): PENDING — BCL System.Diagnostics.Metrics on FusionCacheService; no new NuGet deps

## Key Design Decisions (current state)

- `ICacheService.GetOrSetAsync` factory is `Func<CancellationToken, ValueTask<T>>` (Phase 21 — breaking change at v1.0.0).
- Nullable overload `GetOrSetAsync<T?>` caches null as a valid result (Phase 21). Uses FusionCache `MaybeValue<T>`.
- `GetManyAsync<T>` returns a dictionary with an entry per input key (null on miss). `SetManyAsync<T>` applies one `CachePolicy` to all (Phase 22).
- `IRenewableLock.RenewAsync` returns `bool` — never throws for lost lock. `KeepAliveAsync` is a static extension for background renewal (Phase 23).
- `CachePolicy.SlidingWindow` maps to L1 `SlidingExpiration` only — L2 does not slide (Phase 24). `NeverExpire + SlidingWindow` guard in `BuildEntryOptions`.
- `CachePolicy.KeyVersion` default `0` (no suffix). Version > 0 appends `:v{version}`. ICacheService signatures unchanged (Phase 25).
- `IRedisChannelService.ConnectionHealth` (ConnectionHealthState enum in Abstractions). Reconnect resubscription is atomic (registry lock) (Phase 26).
- `AddCachingCoreOptions` on `IServiceCollection` in Abstractions — zero provider dependencies (Phase 27).
- `ICacheWarmupStrategy` in Abstractions; `CacheWarmupHostedService` + `WaitForWarmup` option in FusionCache (Phase 28).
- `ITenantCacheKeyProvider.BuildTenantKey` format: `{service}:{tenant}:{entity}:{id}`. tenantId always explicit — no IHttpContextAccessor dependency (Phase 29).
- Polly circuit breaker is opt-in (`Enabled = false` default). New dep `Polly.Core` v8 in Redis package only (Phase 30).
- OTel `Meter` name `"SharedKernel.Caching"` (version `"1.0"`). `cache.key_prefix` tag uses `{service}:{entity}` (not full key — high-cardinality guard) (Phase 31).
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
- Polly.Core: >= 8.0.0 (new — planned Phase 30; verify AOT compatibility at implementation time)
