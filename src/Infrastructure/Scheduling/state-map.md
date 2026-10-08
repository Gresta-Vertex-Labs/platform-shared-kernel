# 19.Scheduling — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Scheduling` | Adapter | ● | P-464/WO-073, one package by design (single provider). `IScheduledJobRegistry`, `ScheduledCommandJob<TCommand>` (kernel `ISender`), mandatory `MisfirePolicy`/`OverlapPolicy`, cross-replica single execution through an optional `IDistributedLockService` per-occurrence lease (fencing token in `ScheduledJobExecutionContext.FencingToken`), each run inside a `SystemRequestContext` scope with the job's `TenantScope` (default `TenantScope.Global`) and a new correlation id, `scheduler` probe. Quartz's `CronExpression` for parsing only (Quartz pinned directly in `Directory.Packages.props`). Verified by `consumer-verify`. |

Test double: `InMemoryScheduledJobRegistry` in `src/Infrastructure/Scheduling/SharedKernel.Scheduling.Testing`.

## Phase Key Registry

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

None open. Downstream consumers already shipped: `13.ServiceDefaults`' `WithSchedulingTelemetry` (P-465), the `scheduler` readiness probe mapped by `AddSharedKernelReadiness()` (P-466, superseded by the `IReadinessProbe` model), `16.Testing`'s registry fake (P-467).
