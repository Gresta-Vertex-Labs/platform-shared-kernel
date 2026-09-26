---
name: health-check-tag-calibration
description: Canonical tag taxonomy and HealthStatus calibration for 13.ServiceDefaults health checks after WO-086 (IReadinessProbe + AddSharedKernelReadiness)
metadata:
  type: project
---

> WO-086 (2026-09): every per-provider readiness extension (`AddRedisHealthCheck`, `AddCacheReadinessCheck`, `Add{Messaging,Storage,Search,VectorStore,Workflow,Scheduler,EncryptionKeyProvider,FieldEncryption,AuditSealing}ReadinessCheck`) and the nine `SharedKernel.ServiceDefaults.{Provider}` packages hosting them were deleted. Providers now register an `IReadinessProbe` (`SharedKernel.Primitives.Health`) and `healthChecks.AddSharedKernelReadiness()` maps each to a `ready` check. The table below is the current state; verify against `13.ServiceDefaults/CLAUDE.md` before relying on it.

| Check | Registered by | Tags | HealthStatus on failure |
|---|---|---|---|
| `startup` (`StartupGateHealthCheck`) | `AddSharedKernelHealthChecks()` (only always-on check — in-process state only) | `"ready"` | `Unhealthy` before `MarkReady()` |
| `AddDatabaseReadinessCheck<TContext>` / `AddDapperDatabaseReadinessCheck` (`SharedKernel.ServiceDefaults.Persistence`) | opt-in | `"ready"`, `"db"` | `Unhealthy`; the EF check also waits for startup migrations |
| `AddPersistenceStartupReadinessCheck` (`.Persistence`) | opt-in | `"ready"` | `Unhealthy` until `IPersistenceStartup` completes |
| every `IReadinessProbe` (`redis`, `cache`, `messaging`, `storage-{store}`, `search-{provider}-{index}`, `vector-store-{provider}-{collection}`, `workflows`, `scheduler`, `encryption-key-provider`, `field-encryption`, `audit-sealing`) | opt-in, one call: `AddSharedKernelReadiness()` | `"ready"` | whatever the probe reports: `ReadinessStatus.Degraded` → `Degraded`, `Unhealthy` → `Unhealthy` |

**Calibration now lives in the probe, not here.** The cache probe (`CacheReadinessProbe`, `02.Caching.FusionCache`) reports `Degraded`, never `Unhealthy` — FusionCache's fail-safe may still serve stale-but-correct data. The raw Redis connection probe reports `Unhealthy`. This domain's job is to map the probe's status faithfully and never re-calibrate it.

**Rule of thumb for reviewing a new probe or check:** is there a fail-safe/graceful-degradation layer in front of the failure (FusionCache fail-safe, retry-then-serve-cached)? If yes → `Degraded`. Raw transport/connection liveness with no absorbing layer → `Unhealthy`. Never `"live"` for anything that touches an external system.

**Sub-pattern (WO-044, still valid):** a probe result can carry a field that must be excluded from the verdict entirely — a backlog/lag/staleness figure (search `PendingWriteCount`) measures staleness, not availability. It belongs in the report's `Data`, never in the status. Check the owning domain's own brain for which category each field belongs to.

See [[phase_sequencing]] for build-order dependencies.
