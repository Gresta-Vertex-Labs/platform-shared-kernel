---
name: health-check-tag-calibration
description: Canonical tag taxonomy and HealthStatus calibration decisions for every health check in 13.ServiceDefaults
metadata:
  type: project
---

Tag taxonomy established across P-010/P-122/P-170 task design (2026-06-19), all consistent with the domain's central liveness/readiness invariant:

| Check | Tags | HealthStatus on failure | Opt-in? |
|---|---|---|---|
| `StartupGateHealthCheck` | `"ready"` | `Unhealthy` (before `MarkReady()`) | No — auto-registered by `AddServiceDefaults()`, the only exception, because it has zero dependency-specific coupling (depends only on in-process `StartupGate`) |
| `AddDatabaseReadinessCheck<TContext>` / `AddDapperDatabaseReadinessCheck` | `"ready"`, `"db"` | `Unhealthy` | Yes |
| `AddRedisHealthCheck` | `"ready"`, `"redis"`, `"cache"` | `Unhealthy` (it's a raw connectivity check, not a fail-safe-aware probe) | Yes |
| `AddCacheReadinessCheck` | `"ready"`, `"cache"` | `Degraded` — **never `Unhealthy`** | Yes |
| `AddRabbitMqMessagingHealthCheck` | `"ready"`, `"messaging"` | `Unhealthy` | Yes |
| `AddAzureServiceBusMessagingHealthCheck` | `"ready"`, `"messaging"` | `Unhealthy` | Yes |
| `AddStorageReadinessCheck` (WO-043/P-270 — implemented and shipped 2026-07-18) | `"ready"`, `"storage"` | `Unhealthy` | Yes |
| `AddSearchReadinessCheck` (WO-044/P-277, design-locked, blocked on 09.Search as of 2026-07-19) | `"ready"`, `"search"` | `Unhealthy` (never `Degraded` — no fail-safe layer in front of raw search-index connectivity) iff any of `Reachable`/`IndexAddressable`/`Searchable` is `false`. **`PendingWriteCount` is deliberately excluded from the health calculation entirely** — surfaced only as informational `HealthCheckResult.Data`, since a deep write backlog means results are stale, not unavailable | Yes |
| `AddVectorStoreReadinessCheck` (WO-045/P-285, design-locked, blocked on 10.Intelligence as of 2026-07-21) | `"ready"`, `"vector-store"` | `Unhealthy` (never `Degraded`) iff any of `Reachable`/`CollectionAddressable`/`Queryable` is `false`. `PendingWriteCount` excluded from the calc, same rationale as Search's identical field | Yes |
| `AddOrchestrationReadinessCheck` (WO-045/P-285, outer shape only design-locked — see [[upstream_contract_definition_gap]]) | `"ready"`, `"orchestration"` | Not yet determinable — the `ProbeAsync`-shaped member it should wrap does not exist in `10.Intelligence`'s own ratified `ICompletionProviderDescriptor` contract as of this pass | Yes, once implemented |

**Key distinction to remember:** `AddRedisHealthCheck` (raw connectivity probe) and `AddCacheReadinessCheck` (functional probe through `ICacheService`) are two different checks with two different calibrations, both opt-in, both tagged `"cache"`. The Redis one reports a hard `Unhealthy` because it's checking the transport directly. The cache-readiness one reports `Degraded` because it goes through `ICacheService.GetAsync`, where FusionCache's L1 fail-safe may legitimately still be serving stale-but-correct data even when L2/Redis is down — pulling the pod from rotation in that case would be the wrong response to a outage FusionCache is specifically designed to absorb.

**Why this matters for future phase design:** any new health check task must be classified into this table before being added to state-map.md. The rule of thumb: does this check have a fail-safe/graceful-degradation layer sitting in front of the failure (FusionCache L1, retry-then-serve-cached, etc.)? If yes → `Degraded`. If it's checking raw transport/connection liveness with no absorbing layer → `Unhealthy`. Never `"live"` for anything that touches an external system, full stop.

**New sub-pattern from `AddSearchReadinessCheck` (WO-044): a probe result can carry a field that must be EXCLUDED from the health calculation entirely, not merely calibrated to `Degraded`.** `SearchIndexHealth.PendingWriteCount` is nullable-and-permanently-so (per `09.Search/CLAUDE.md`) because it measures staleness (results lag behind writes), not availability — feeding it into Healthy/Unhealthy/Degraded at all would conflate two orthogonal signals. The correct treatment is: surface it as `HealthCheckResult.Data` (a metric/gauge an operator can alert on separately) while the readiness verdict itself is computed from a disjoint set of booleans (`Reachable`/`IndexAddressable`/`Searchable`). When a new health check's probe result has a "backlog/lag/staleness" field alongside "connectivity" fields, check whether the owning domain's own brain already states which category each field belongs to before wiring — do not assume every non-`true` field should push toward `Unhealthy` or `Degraded`.

See [[phase_sequencing]] for build-order dependencies these checks sit behind.
