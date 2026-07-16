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
| `AddStorageReadinessCheck` (WO-043/P-270, design-locked, blocked on 08.Storage as of 2026-07-16) | `"ready"`, `"storage"` | `Unhealthy` | Yes |

**Key distinction to remember:** `AddRedisHealthCheck` (raw connectivity probe) and `AddCacheReadinessCheck` (functional probe through `ICacheService`) are two different checks with two different calibrations, both opt-in, both tagged `"cache"`. The Redis one reports a hard `Unhealthy` because it's checking the transport directly. The cache-readiness one reports `Degraded` because it goes through `ICacheService.GetAsync`, where FusionCache's L1 fail-safe may legitimately still be serving stale-but-correct data even when L2/Redis is down — pulling the pod from rotation in that case would be the wrong response to a outage FusionCache is specifically designed to absorb.

**Why this matters for future phase design:** any new health check task must be classified into this table before being added to state-map.md. The rule of thumb: does this check have a fail-safe/graceful-degradation layer sitting in front of the failure (FusionCache L1, retry-then-serve-cached, etc.)? If yes → `Degraded`. If it's checking raw transport/connection liveness with no absorbing layer → `Unhealthy`. Never `"live"` for anything that touches an external system, full stop.

See [[phase_sequencing]] for build-order dependencies these checks sit behind.
