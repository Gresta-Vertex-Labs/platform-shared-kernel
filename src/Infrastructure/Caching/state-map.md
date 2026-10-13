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

| Phase key | Phase | Status |
| --- | --- | --- |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

- **01.Core** (inbound, open): `SK.02.LockUnavailableError` needs `IHasError` / the recorder from `SK.01.ObservabilityHelpers`; `SK.02.ObservabilityConformance` needs `TelemetryVersion`, `ActivityFailureExtensions`, `WellKnownOutcomes`, `WellKnownTagKeys.Outcome` and the `LoggingEventIdRanges` helpers. Also depends on `SK.01.CoreConformance` renaming `unexpected.exception` to `unexpected.default` only if a test here asserts that literal (none planned).
- **16.Testing** (inbound, open): `SK.02.ObservabilityConformance` T-tasks use `ActivityRecorder`, `MetricRecorder` and the logger assertions from `SK.16.TelemetryCapture`.
- **00.Governance** (inbound, open): the report-only SK0043/SK0044 and EventId/telemetry/template/error-code assertions from `SK.00.ObservabilityReportOnly` must exist before this phase's zero-findings acceptance is checkable.
- **13.ServiceDefaults** (outbound note): the instrument, tag and span renames break any dashboard built on `cache.*`. `SK.13.WildcardTelemetry` deletes `WithCachingTelemetry()`; its Shop call sites (Ordering, Inventory, Catalog) are theirs. The `SharedKernel.Caching` source and meter names do not change.
- **14.Presentation** (outbound note): `DistributedLockUnavailableException` becomes a `SharedKernelException`, so the existing edge mapping answers Unavailable with no edge change; if `SK.02.LockUnavailableError` T-02 finds no public seam, `14.Presentation` is asked for a test-visible one.
- **19.Scheduling** (outbound note): catches `DistributedLockUnavailableException` by type, unaffected; its `OccurrenceLeaseTests` use `ForResource`, which keeps its signature.
- **05.Application** (outbound note): `Application.Pipeline.Caching`'s own metrics are owned by 05 and are not renamed here.
- **Root (arch-lead)**: `SharedKernel.Caching.Abstractions` gains a reference to `SharedKernel.Core` (Foundation, tier-legal, not a declared adapter edge); the root `CLAUDE.md` Abstractions description needs no change unless arch-lead wants it recorded.
