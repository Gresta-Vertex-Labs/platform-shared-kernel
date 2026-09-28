# 02.Caching — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Caching.Abstractions` | Abstractions | ● | `ICacheService`, `ITenantCacheService` (explicit `TenantId` on every call), `CachePolicy`, `CacheKeyFormat`, `IDistributedLockService` (locks, leases, fencing tokens), `ICacheWarmupStrategy`. The only caching package application code references. |
| `SharedKernel.Caching.FusionCache` | Adapter | ● | The cache: required `ServiceName`, Brotli compression, `AddCacheEncryption()` (cache key as associated data), warmup, `cache` readiness probe. |
| `SharedKernel.Caching.Redis.Core` | Adapter | ● | `AddRedisConnection(configuration)` — the one multiplexer (TLS/mTLS, fail-fast), `redis` readiness probe. |
| `SharedKernel.Caching.Redis` | Adapter | ● | `AddRedisL2()` — distributed L2 and backplane (removals, expirations, tag evictions, clears). Edge → Redis.Core. |
| `SharedKernel.Caching.Redis.DistributedLocking` | Adapter | ● | `IDistributedLockService` over Redis — kept-alive locks, leases, atomic fencing tokens. Edge → Redis.Core. |
| `SharedKernel.Caching.Redis.HashStore` | Adapter | ● | `IRedisHashService`, `ITypedHashStore<T>`. Edge → Redis.Core. |
| `SharedKernel.Caching.Redis.PubSub` | Adapter | ● | `IRedisChannelService` — loss-tolerant Pub/Sub, never messaging. Edge → Redis.Core. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |
| `SK.02.Design` | Design | ● |
| `SK.02.Scaffold` | Scaffold | ● |
| `SK.02.Core` | Core | ● |
| `SK.02.Tests` | Tests | ● |
| `SK.02.Docs` | Docs | ● |
| `SK.02.Published` | Published | ● |
| `SK.02.Abstractions` | Phase 5 — abstractions package | ● |
| `SK.02.CachingRefactor` | Phase 6 — caching refactor | ● |
| `SK.02.RedisRefactor` | Phase 7 — Redis refactor | ● |
| `SK.02.InvalidationBus` | Phase 12 — invalidation bus (replaced by the L2 backplane, P-550) | ⊘ |
| `SK.02.FusionCacheRename` | Phase 14 — FusionCache rename + `NeverExpire` (P-014) | ● |
| `SK.02.AotHardening` | Phase 15 — AOT hardening + `ITypedHashStore` | ● |
| `SK.02.BrotliCompression` | Phase 16 — Brotli L2 compression | ● |
| `SK.02.LayeringFix` | Phase 17 (P-016) | ● |
| `SK.02.AotSerializerFix` | Phase 18 (P-017) | ● |
| `SK.02.DiErgonomics` | Phase 19 (P-018) | ● |
| `SK.02.L1SizeLimit` | Phase 20 (P-019) | ● |
| `SK.02.ValueTaskFactory` | Phase 21 (P-020) | ● |
| `SK.02.BatchOperations` | Phase 22 (P-021) | ● |
| `SK.02.RenewableLock` | Phase 23 (P-022) — replaced by the lock/lease redesign (P-547) | ⊘ |
| `SK.02.SlidingExpiration` | Phase 24 (P-023) | ● |
| `SK.02.KeyVersioning` | Phase 25 (P-024) | ● |
| `SK.02.ChannelReconnect` | Phase 26 (P-025) | ● |
| `SK.02.CachingCoreOptionsDi` | Phase 27 (P-026) | ● |
| `SK.02.CacheWarmup` | Phase 28 (P-027) | ● |
| `SK.02.TenantCacheKey` | Phase 29 (P-028) | ● |
| `SK.02.RedisCircuitBreaker` | Phase 30 (P-029) | ● |
| `SK.02.OtelMeters` | Phase 31 (P-030) | ● |
| `SK.02.RedisConnectionCore` | Phase 32 (P-140) | ● |
| `SK.02.RedisL2Refactor` | Phase 33 (P-141) | ● |
| `SK.02.RedisLockingExtraction` | Phase 34 (P-142) | ● |
| `SK.02.RedisHashExtraction` | Phase 35 (P-143) | ● |
| `SK.02.RedisPubSubExtraction` | Phase 36 (P-144) | ● |
| `SK.02.LoggingRetrofit` | Phase 37 (P-252) | ● |
| `SK.02.NuGetPackagingParity` | Phase 38 (P-301) | ● |
| `SK.02.CrossInstanceTagInvalidation` | Phase 39 (P-302) | ● |
| `SK.02.BatchOperationsParallelization` | Phase 40 (P-303) | ● |
| `SK.02.OtelTracingSpans` | Phase 41 (P-304) | ● |
| `SK.02.CacheEncryptionAtRest` | Phase 42 (P-433) | ● |
| `SK.02.LockFencingTokens` | Phase 43 (P-434) | ● |
| `SK.02.TenantCacheService` | Phase 44 (P-435) | ● |
| `SK.02.RedisTransportHardening` | Phase 45 (P-436) | ● |
| `SK.02.CacheEncryptionAadBinding` | Phase 46 (P-497) | ● |

## Open Work

None — every phase in this domain is complete. The first public release ships with root P-577 (release train).

## Blocked

None.

## Cross-Domain Dependencies

None open.

## Completed Phases

- WO-086 ● Foundation refactor — `TenantId` tenant parameters, `redis`/`cache` readiness probes (replacing `IRedisConnectionProbe`), `ServiceDefaults.Caching(.Redis)` deleted, fakes in `SharedKernel.Caching.Testing`/`.Caching.Redis.Testing`, tiers and Redis.Core edges declared (P-563, P-565, P-569, P-571, P-575) (2026-09-26)
- P-547–P-550 ● Pre-first-publish passes — `Caching.Abstractions` redesign (locks, leases, fencing tokens, collision-free tenant keys), FusionCache pass, Redis section names and value caching, Redis packages pass; all published (2026-09-17)
- P-497 ● Cache encryption on async cryptography contracts with AAD — `EncryptedCacheService` (WO-081) (2026-09-08)
- P-433–P-436 ● WO-065 — cache encryption at rest, lock fencing tokens, fail-closed `ITenantCacheService`, Redis secure-transport hardening (2026-08-24)
- P-301–P-304 ● WO-050 — packaging parity, cross-instance tag invalidation, batch parallelization, tracing spans
- P-252 ● Logging retrofit to `[LoggerMessage]` (WO-041)
- P-140–P-144 ● WO-023 — Redis role split: Redis.Core, L2, DistributedLocking, HashStore, PubSub
- P-021–P-030 ● WO-007 — batch operations, renewable locks, sliding expiration, key versioning, reconnect, options DI, warmup, tenant keys, circuit breaker, meters
- P-016–P-020 ● WO-006 — layering fix, AOT serializer fix, DI ergonomics, L1 size limit, `ValueTask` factory
- Earlier phases (P-005–P-007, P-012, P-014) — archived.

## Changelog

- [2026-09-28] State map slimmed to a living board; completed phase detail archived outside the repository — public-release cleanup
- [2026-09-26] WO-086 note added — `TenantId` tenant parameters, `redis`/`cache` readiness probes, ServiceDefaults.Caching(.Redis) deleted, Testing split (P-575)
- [2026-09-17] P-547–P-550 pre-first-publish passes shipped and published (recorded on the root board)
- [2026-09-08] AA-01→AA-13 ● in SK.02.CacheEncryptionAadBinding — `CacheEncryptionSerializer` replaced by `EncryptedCacheService` (P-497)
- [2026-08-24] TH-01→TH-10 ● in SK.02.RedisTransportHardening — `AddRedisConnection` validates options on start (P-436)
