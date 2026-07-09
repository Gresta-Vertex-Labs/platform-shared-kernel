---
name: project-caching-state
description: Package split for 02.Caching (7-package split COMPLETE as of Phase 36), phase history through WO-007/WO-023/WO-041 P-252, interface locations, layering rules, AOT decisions, EventId sub-block allocation
metadata:
  type: project
---

## CORRECTION (2026-07-09): WO-023 is COMPLETE, not planned

Earlier revisions of this memory said the 7-package split (Phases 32-36) was "planned, not yet implemented." That was stale — verified against the actual `02.Caching/state-map.md` and `CLAUDE.md` on 2026-07-09: **all of Phases 32-36 are complete (`●`)**. The 7-package topology (Abstractions, FusionCache, Redis.Core, Redis (L2), Redis.DistributedLocking, Redis.HashStore, Redis.PubSub) is shipped. Always re-verify phase completion state against the live files before trusting this memory's "planned" language below — it describes the design intent at authoring time, not necessarily current reality.

# Caching Domain State

## Three-Package Split (enforced after WO-006 / Phase 17, complete)

| Package | Role |
| ------- | ---- |
| `SharedKernel.Caching.Abstractions` | Zero-infra contracts only — no FusionCache, no StackExchange.Redis |
| `SharedKernel.Caching.FusionCache` | FusionCache L1 provider + CacheKeyProvider implementation. Renamed from `SharedKernel.Caching` in Phase 14 (WO-004). |
| `SharedKernel.Caching.Redis` | Redis L2, RedLock, RedisChannelService, RedisHashService, TypedHashStore, RedisCacheInvalidationBus, CacheInvalidationReceiver |

**Layering rule (Phase 17):** Redis and FusionCache packages are siblings — they must never reference each other. Both reference only Abstractions.

**Why:** `05.Application` needs `ICacheService` without pulling in FusionCache. A service needing only Redis (channel, hash, locking) must not be forced to take a transitive FusionCache dependency.

**How to apply:** Any new interface goes in `SharedKernel.Caching.Abstractions`. Shared options that both providers need (e.g. `ServiceName`) go in `CachingCoreOptions` (Abstractions), not `CachingOptions` (FusionCache).

## Seven-Package Split (WO-023, Phases 32–36 — PLANNED, not yet implemented)

`SharedKernel.Caching.Redis` is being split into 5 packages, all sharing a new dependency-root package:

| Package | Role | Status |
| ------- | ---- | ------ |
| `SharedKernel.Caching.Abstractions` | Unchanged — all contracts stay here | complete |
| `SharedKernel.Caching.FusionCache` | Unchanged — L1 provider | complete |
| `SharedKernel.Caching.Redis.Core` | NEW (Phase 32) — `AddRedisConnection` (`IConnectionMultiplexer` `TryAddSingleton`, first-caller-wins), `RedisConnectionHealthTracker` (passive `ConnectionHealthState` observer), `RedisCircuitBreakerOptions` + `AddRedisCircuitBreaker` (generalized Ph.30 circuit breaker). Zero refs to FusionCache/RedLock/capability types. **Dependency root for the other 4.** | planned |
| `SharedKernel.Caching.Redis` | SLIMMED (Phase 33) — only `AddRedisL2`, `RedisL2Options`, `IRedisL2BatchService` (internal), FusionCache Redis backplane wiring. Sources multiplexer/pipeline from `.Redis.Core` via new `ProjectReference`. | planned |
| `SharedKernel.Caching.Redis.DistributedLocking` | NEW (Phase 34) — `RedLockDistributedLockService`, `RedLockRenewableLock`, `KeepAliveAsync`, `RedisLockOptions`, `AddRedisDistributedLocking`. Refs Abstractions + `.Redis.Core` + RedLock.net. | planned |
| `SharedKernel.Caching.Redis.HashStore` | NEW (Phase 35) — `RedisHashService`, `TypedHashStore<T>` (internal sealed), `AddRedisHashService`, `AddTypedHashStore<T>`. Refs Abstractions + `.Redis.Core`. | planned |
| `SharedKernel.Caching.Redis.PubSub` | NEW (Phase 36) — `RedisChannelService` (with Ph.26 reconnect replay intact), `RedisCacheInvalidationBus`, `CacheInvalidationReceiver`. Refs Abstractions + `.Redis.Core` + Hosting.Abstractions. **Stays in 02.Caching, not 07.Messaging.** | planned |

**Key rules:**

- `.Redis`, `.DistributedLocking`, `.HashStore`, `.PubSub` are siblings depending only on `.Redis.Core` — never on each other.
- All `Add*` extension signatures preserved exactly (no breaking DI changes). `RedisL2Options.CircuitBreaker` retypes from a nested class to top-level `RedisCircuitBreakerOptions` (Phase 32) — type relocation, not rename, source-compatible.
- `[Obsolete]` `IServiceCollection` shims: each package that needs one defines its own tiny `internal ...CachingBuilder : ICachingBuilder` — not shared across packages.
- `RedisConnectionHealthTracker` (Core, Phase 32) and `RedisChannelService`'s own reconnect/resubscribe replay (Ph.26, relocates intact to `.Redis.PubSub` Phase 36) are SEPARATE, coexisting mechanisms — not unified in WO-023.
- See `02.Caching/state-map.md` Phases 32–36 for full file-level plans.

**Why pub/sub stays in 02.Caching:** `IRedisChannelService`/`ICacheInvalidationBus` are at-most-once/no-durability — the architectural opposite of `07.Messaging`'s outbox-backed guarantees. `07.Messaging` cannot reference `02.Caching` types (`CacheInvalidationMessage`, `CachingCoreOptions`) per layering rules, and moving these types to `07.Messaging` would falsely imply they inherit its durability contract.

## Interface Canonical Locations

- `ICacheService` — `SharedKernel.Caching.Abstractions`
- `CachePolicy` — `SharedKernel.Caching.Abstractions`
- `ICacheKeyProvider` — `SharedKernel.Caching.Abstractions`
- `IDistributedLockService` — `SharedKernel.Caching.Abstractions`
- `IRenewableLock` — `SharedKernel.Caching.Abstractions` (Phase 23, complete)
- `IRedisChannelService` — `SharedKernel.Caching.Abstractions`
- `IRedisHashService` — `SharedKernel.Caching.Abstractions`
- `ITypedHashStore<T>` — `SharedKernel.Caching.Abstractions`
- `ICacheInvalidationBus` — `SharedKernel.Caching.Abstractions`
- `CacheInvalidationMessage` + `CacheInvalidationType` — `SharedKernel.Caching.Abstractions`
- `ICachingBuilder` — `SharedKernel.Caching.Abstractions`
- `CachingCoreOptions` — `SharedKernel.Caching.Abstractions` (added Phase 17; holds `ServiceName`)
- `ConnectionHealthState` — `SharedKernel.Caching.Abstractions` (Phase 26, complete; enum: Connected/Reconnecting/Disconnected)
- `ICacheWarmupStrategy` — `SharedKernel.Caching.Abstractions` (Phase 28, complete)
- `ITenantCacheKeyProvider` — `SharedKernel.Caching.Abstractions` (Phase 29, complete; extends ICacheKeyProvider)
- `CacheKeyProvider` — `SharedKernel.Caching.FusionCache`
- `TenantCacheKeyProvider` — `SharedKernel.Caching.FusionCache` (Phase 29, complete)
- `CacheWarmupHostedService` — `SharedKernel.Caching.FusionCache` (Phase 28, complete)
- `FusionCacheService` — `SharedKernel.Caching.FusionCache`
- `CachingOptions` — `SharedKernel.Caching.FusionCache` (retains `ServiceName` for validation + FusionCache-specific fields)
- `BrotliCacheSerializer` — `SharedKernel.Caching.FusionCache`
- `RedLockDistributedLockService` — `SharedKernel.Caching.Redis` (relocates to `.Redis.DistributedLocking` in planned Phase 34)
- `RedLockRenewableLock` — `SharedKernel.Caching.Redis` (Phase 23, complete; relocates to `.Redis.DistributedLocking` in planned Phase 34)
- `RedisChannelService` — `SharedKernel.Caching.Redis` (relocates to `.Redis.PubSub` in planned Phase 36)
- `RedisHashService` — `SharedKernel.Caching.Redis` (relocates to `.Redis.HashStore` in planned Phase 35)
- `TypedHashStore<T>` — `SharedKernel.Caching.Redis` (internal sealed; relocates to `.Redis.HashStore` in planned Phase 35)
- `IRedisL2BatchService` — `SharedKernel.Caching.Redis` (internal, Phase 22 complete; stays in slimmed `.Redis` per Phase 33)
- `RedisCacheInvalidationBus` — `SharedKernel.Caching.Redis` (relocates to `.Redis.PubSub` in planned Phase 36)
- `CacheInvalidationReceiver` — `SharedKernel.Caching.Redis` (relocates to `.Redis.PubSub` in planned Phase 36)
- **(Phase 32, planned)** `RedisConnectionOptions`, `RedisCircuitBreakerOptions`, `RedisConnectionHealthTracker`, `AddRedisConnection`, `AddRedisCircuitBreaker` — new `SharedKernel.Caching.Redis.Core` (not Abstractions — concrete connection/resilience primitives, dependency root for the 4 sibling Redis packages)

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
- Phase 22 (BatchOperations): complete — GetManyAsync/SetManyAsync; Redis pipeline helper (internal IRedisL2BatchService); 114 FC + 99 Redis tests
- Phase 23 (RenewableLock): complete — IRenewableLock + AcquireRenewableAsync + KeepAliveAsync + RedLockRenewableLock; 114 FC + 125 Redis tests
- Phase 24 (SlidingExpiration): complete — CachePolicy.Sliding + SlidingWindow; L1-only sliding; NeverExpire guard; 134 FC tests
- Phase 25 (KeyVersioning): complete — CachePolicy.KeyVersion + WithVersion + ICacheKeyProvider version overload; 160 FC + 125 Redis tests
- Phase 26 (ChannelReconnect): complete — RedisChannelService reconnect + ConnectionHealthState enum; 160 FC + 132 Redis tests
- Phase 27 (CachingCoreOptionsDi): complete — AddCachingCoreOptions standalone extension in Abstractions; 160 FC + 142 Redis tests
- Phase 28 (CacheWarmup): complete — ICacheWarmupStrategy + CacheWarmupHostedService + AddCacheWarmup<T>; 172 FC + 142 Redis tests
- Phase 29 (TenantCacheKey): complete — ITenantCacheKeyProvider; format {service}:{tenant}:{entity}:{id}; 196 FC tests
- Phase 30 (RedisCircuitBreaker): complete — Polly v8 opt-in circuit breaker for RedisL2; new dep Polly.Core 8.5.2; 196 FC + 154 Redis tests
- Phase 31 (OtelMeters): complete — BCL System.Diagnostics.Metrics on FusionCacheService; no new NuGet deps; 209 FC tests
- **WO-023 (Phases 32–36, planned, not yet implemented)** — split SharedKernel.Caching.Redis into 5 packages:
  - Phase 32 (RedisConnectionCore): new `.Redis.Core` package — `AddRedisConnection`, `RedisConnectionHealthTracker`, `RedisCircuitBreakerOptions` + `AddRedisCircuitBreaker`. Dependency root.
  - Phase 33 (RedisL2Refactor): slim `.Redis` to L2-only; `AddRedisL2` sources multiplexer/pipeline from `.Redis.Core`.
  - Phase 34 (RedisLockingExtraction): new `.Redis.DistributedLocking` — RedLock.net types + `AddRedisDistributedLocking`.
  - Phase 35 (RedisHashExtraction): new `.Redis.HashStore` — `IRedisHashService`/`ITypedHashStore<T>` impls + `AddRedisHashService`/`AddTypedHashStore<T>`.
  - Phase 36 (RedisPubSubExtraction): new `.Redis.PubSub` — `RedisChannelService`/`RedisCacheInvalidationBus`/`CacheInvalidationReceiver`. Stays in 02.Caching, not 07.Messaging.

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
- **(Phase 32, planned)** `IConnectionMultiplexer` registration centralizes into `.Redis.Core`'s `AddRedisConnection` (`TryAddSingleton`, first-caller-wins) — `AddRedisL2`/`AddRedisDistributedLocking` call it internally instead of self-registering. No new multiplexer registrations permitted outside `.Redis.Core`.
- **(Phase 32, planned)** `RedisCircuitBreakerOptions` generalizes Ph.30's `RedisL2Options.CircuitBreakerOptions` (same 5 properties/defaults) as a top-level class in `.Redis.Core`. `RedisL2Options.CircuitBreaker` retypes to it — source-compatible (`o.CircuitBreaker.Enabled = true` still compiles).
- **(Phase 32, planned)** Two independent connection-health mechanisms coexist by design: `RedisConnectionHealthTracker` (`.Redis.Core`, passive observer, no replay logic) vs. `RedisChannelService`'s own `ConnectionRestored`/`ConnectionFailed` + registry-lock resubscription replay (Ph.26, relocates intact to `.Redis.PubSub` Phase 36). Do NOT unify these in WO-023 — explicitly deferred.
- **(Phases 34–36, planned)** `.Redis`, `.DistributedLocking`, `.HashStore`, `.PubSub` are siblings — each refs only Abstractions + `.Redis.Core`, never each other. Each retains its own tiny `internal ...CachingBuilder : ICachingBuilder` for `[Obsolete]` `IServiceCollection` shims (no shared adapter type).

## Phase 37 (WO-041, P-252) — Logging Retrofit to [LoggerMessage] Standard (PLANNED, execution-blocked)

Designed 2026-07-09. Converts every production log statement in this domain to the mandatory `[LoggerMessage]` pattern and renumbers every `EventId` into `LoggingEventIdRanges.Caching` (2000-2999, from `SharedKernel.Primitives`, `01.Core` P-249).

**Confirmed defects motivating this phase (found via direct grep of production .cs files, not assumed):**
- `RedisConnectionHealthTracker` (Redis.Core) and `CacheInvalidationReceiver` (Redis.PubSub) both used EventId 4001/4002 — live collision, same process.
- `FusionCacheService` (FusionCache) used EventId 1001-1005 — squatting on `01.Core`'s reserved 1000-1999 block.
- `RedisChannelService` (Redis.PubSub) used EventId 3001-3005 — squatting on `03.Domain`'s reserved 3000-3999 block.
- `CacheWarmupHostedService` (FusionCache) uses 8 direct `ILogger.LogInformation/LogWarning/LogError` calls — not `[LoggerMessage]` at all.
- `RedisCacheInvalidationBus` (Redis.PubSub) uses a hand-written `LoggerMessage.Define<string>` static delegate with `new EventId(1, nameof(...))` — not the attribute pattern.
- `SharedKernel.Caching.Redis` (L2) and `SharedKernel.Caching.Redis.HashStore` carry **zero** logging call sites — confirmed via grep, unaffected by this phase.

**Authoritative EventId sub-block allocation (100-wide, package declaration order — this supersedes `01.Core/CLAUDE.md`'s illustrative worked example, which omitted FusionCache):**

| Package | Sub-block |
|---|---|
| FusionCache | +0..+99 (CacheWarmupHostedService +0..+7, FusionCacheService +10..+14) |
| Redis.Core | +100..+199 (RedisConnectionHealthTracker +100/+101) |
| Redis (L2) | +200..+299 (reserved, unused) |
| Redis.DistributedLocking | +300..+399 (RedLockDistributedLockService +300..+306, RedLockRenewableLock +307..+312) |
| Redis.HashStore | +400..+499 (reserved, unused) |
| Redis.PubSub | +500..+599 (RedisChannelService +500..+504, CacheInvalidationReceiver +505..+509, RedisCacheInvalidationBus +510) |

EventId values must be written as `LoggingEventIdRanges.Caching + {offset}` (compile-time const expression), never a bare literal.

**Hard blocker:** none of the four affected packages (FusionCache, Redis.Core, Redis.DistributedLocking, Redis.PubSub) currently have a `ProjectReference` to `SharedKernel.Primitives` — despite this domain's CLAUDE.md package table aspirationally listing "01.Core" as a FusionCache reference, the actual csproj has no such reference. This phase must add one to each of the four. Execution is blocked until `01.Core` P-249 (`LoggingEventIdRanges`, task C-42) actually ships — as of 2026-07-09 it is still `○` Pending in `01.Core/state-map.md`.

Full task breakdown: `02.Caching/state-map.md` Phase 37 (`SK.02.LoggingRetrofit`, 12 tasks LR-01→LR-12).

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
- Polly.Core: 8.5.2 (confirmed Phase 30, complete; AOT-safe `ResiliencePipeline` usage validated)
- Microsoft.Extensions.Caching.StackExchangeRedis: 10.0.0
- Microsoft.Extensions.Hosting.Abstractions: 10.0.0

### Post-WO-023 owning package per pin (Phases 32–36, planned)

- `.Redis.Core`: StackExchange.Redis 2.13.1, Polly.Core 8.5.2, Microsoft.Extensions.DependencyInjection.Abstractions 10.0.1
- `.Redis` (slimmed): ZiggyCreatures.FusionCache 2.6.0, ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis 2.6.0, Microsoft.Extensions.Caching.StackExchangeRedis 10.0.0, + ProjectReference to `.Redis.Core`
- `.Redis.DistributedLocking`: RedLock.net 2.3.2, + ProjectReference to `.Redis.Core`
- `.Redis.HashStore`: + ProjectReference to `.Redis.Core` only (no new third-party packages)
- `.Redis.PubSub`: Microsoft.Extensions.Hosting.Abstractions 10.0.0 (for `CacheInvalidationReceiver` `BackgroundService`), + ProjectReference to `.Redis.Core`
