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

| Phase key | Phase | Status |
| --- | --- | --- |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

Open (WO-087):

- **01.Core (inbound, SK.01.ObservabilityHelpers / P-580):** both phases consume `ActivityFailureExtensions` (`RecordFailure`/`RecordException`/`RecordResult`), `WellKnownOutcomes`, `WellKnownTagKeys.Outcome`, `TelemetryVersion.Of`. `SK.19.SchedulingConformance` also needs `LoggingEventIdRanges.PackageSubBlockWidth`; P-580's D-03 uses it but no C-task there adds it, and `Primitives` is outside this domain: confirm on disk that it ships, else the core planner adds it.
- **16.Testing (inbound, SK.16.TelemetryCapture / P-581):** `ActivityRecorder`, `MetricRecorder`, the `Should*` assertions and `LoggerAssertions` additions. Suggestion, not a blocker: `InMemoryLogger` could record the ambient `IRequestContext` per record; until then `SK.19.SchedulerLogContext` T-01 carries a test-local provider. `SharedKernel.Scheduling.Testing` needs no change in either phase (`InMemoryScheduledJobRegistry` emits no telemetry and opens no caller scope; verified by search).
- **02.Caching (inbound, SK.02.LockUnavailableError / P-585):** gives the lock-outage span its `caching.lock_unavailable` `error.code`; T-02 of `SK.19.SchedulerLogContext` asserts the code only once that phase ships.
- **05.Application (inbound, SK.05.PipelineTelemetryGaps / P-586):** idempotent commands return an Unavailable `Result`; `ScheduledCommandJob` logs it as a failed fire with its code (covered by T-03, no code change here).
- **13.ServiceDefaults (outbound, SK.13.WildcardTelemetry / P-582):** the renamed source stays `SharedKernel.Scheduling`, so the wildcard covers it; `WithSchedulingTelemetry()` and its test are deleted there. Call sites outside that domain (for example `samples/Shop` hosts) and the ServiceDefaults README are theirs; the caller should have them removed with the breaking rename.
- **00.Governance (outbound, SK.00.ObservabilityReportOnly / P-583):** `SchedulingTelemetry.Name` is the public constant its source/meter-name check reads; its report must show zero findings for `SharedKernel.Scheduling` at the end of `SK.19.SchedulingConformance`.

Downstream consumers already shipped: `13.ServiceDefaults`' `WithSchedulingTelemetry` (P-465), the `scheduler` readiness probe mapped by `AddSharedKernelReadiness()` (P-466, superseded by the `IReadinessProbe` model), `16.Testing`'s registry fake (P-467).
