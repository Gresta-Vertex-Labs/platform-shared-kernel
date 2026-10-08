# 02.Caching — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

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

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete. The first public release ships with root P-577 (release train).

## Blocked

None.

## Cross-Domain Dependencies

None open.
