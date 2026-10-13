# 17.Workflows — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Workflows.Temporal` | Adapter | ● | One package by design (no `.Abstractions` split: durable execution's programming model is the abstraction). `IWorkflowDispatcher`/`IWorkflowHandle<TResult>`/`IWorkflowIdFactory` with a mandatory `TenantScope` (from `SharedKernel.Execution`), `WorkflowBase` (deterministic; SK0028) / `ActivityBase`, `CommandActivity<TCommand>` over the kernel `ISender`, worker hosting, `Result<T>` ↔ failure mapping, `EncryptionPayloadCodec` (async, associated data bound), context propagation into activities (each runs in the dispatcher's `RequestContextScope`), `workflows` probe. Raw `ITemporalClient` prohibited (SK0029; gated escape hatch `ITemporalRawClientAccessor`). Verified by `consumer-verify`; real-engine tests use Temporalio's in-box `WorkflowEnvironment` (no container fixture needed). |

Test double: `InMemoryWorkflowDispatcher` in `src/Infrastructure/Workflows/SharedKernel.Workflows.Testing`.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

- **01.Core (SK.01.ObservabilityHelpers, P-580):** consumes `TelemetryVersion.Of`, `LoggingEventIdRanges`, `IHasError`/`SharedKernelException`; nothing new is asked of Core and no `ErrorType` member is added (rule 8 rows unchanged).
- **13.ServiceDefaults (SK.13.WildcardTelemetry, P-582):** the wildcard covers meter `SharedKernel.Workflows`; `WithWorkflowTelemetry()` must list the three `TracingInterceptor` source names exactly as `SK.17.WorkflowObservabilityFixes` T-03 reads them from the SDK, and its XML docs still cite `WorkflowWellKnown.ActivitySourceName` (removed here).
- **16.Testing (SK.16.TelemetryCapture, P-581):** this domain's tests use its recorders and assertions; the SDK-named instruments on meter `SharedKernel.Workflows` need an allow-list entry in `ShouldFollowMetricConvention` (T6 naming and `s` unit cannot hold for them). `SharedKernel.Workflows.Testing` is this domain's own double and follows in both phases.
- **00.Governance (SK.00.ObservabilityReportOnly, P-583):** the same exemption for the telemetry-name/instrument checks; the error-code format check reads `WorkflowErrorCodes`; SK0044 sees no bare EventId after `SK.17.WorkflowConformance`. A note for a future rule: an exception that exposes an `Error` must derive `SharedKernelException` (audit G4).
- **14.Presentation (note only):** edge assertions for the changed types: `workflow.timed_out` → `Timeout`, `workflow.request_timed_out` → `Timeout`, `workflow.service_unavailable` → `Unavailable`, `workflow.not_found` → `NotFound`.
- **19.Scheduling / 07.Messaging:** no change; a scheduled trigger starting a workflow sees `GetHandle` as `Result` only after `SK.17.WorkflowConformance`.
