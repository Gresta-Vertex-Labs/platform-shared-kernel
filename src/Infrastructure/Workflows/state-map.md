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

- **16.Testing / 00.Governance (note):** the SDK-named instruments on meter `SharedKernel.Workflows` cannot follow T6 naming or the `s` unit; `ShouldFollowMetricConvention` has no exemption for them, so no test calls it on that meter, and the governance IL check does not see them. An allow-list is needed before either is pointed at it.
