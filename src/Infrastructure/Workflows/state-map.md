# 17.Workflows — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Workflows.Temporal` | Adapter | ● | One package by design (no `.Abstractions` split: durable execution's programming model is the abstraction). `IWorkflowDispatcher`/`IWorkflowHandle<TResult>`/`IWorkflowIdFactory` with a mandatory `TenantScope` (from `SharedKernel.Execution`), `WorkflowBase` (deterministic; SK0028) / `ActivityBase`, `CommandActivity<TCommand>` over the kernel `ISender`, worker hosting, `Result<T>` ↔ failure mapping, `EncryptionPayloadCodec` (async, associated data bound), context propagation into activities (each runs in the dispatcher's `RequestContextScope`), `workflows` probe. Raw `ITemporalClient` prohibited (SK0029; gated escape hatch `ITemporalRawClientAccessor`). Verified by `consumer-verify`; real-engine tests use Temporalio's in-box `WorkflowEnvironment` (no container fixture needed). |

Test double: `InMemoryWorkflowDispatcher` in `src/Testing/SharedKernel.Workflows.Testing`.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | :---: |
| `SK.17.Design` | Design (D-01–D-19; WO-046 P-287, WO-081 P-501) | ● |
| `SK.17.Scaffold` | Scaffold (S-01–S-10) | ● |
| `SK.17.Core` | Core (C-01–C-30) | ● |
| `SK.17.Tests` | Tests (T-01–T-20) | ● |
| `SK.17.Docs` | Docs (DO-01–DO-07) | ● |
| `SK.17.Published` | Published (P-01–P-08) | ● |

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

None open. (`01.Core`'s P-491/P-492 shipped; downstream `13.ServiceDefaults` telemetry and readiness and the `16.Testing` dispatcher double have shipped.)

## Completed Phases

- WO-086 ● `TenantScope` from `SharedKernel.Execution`; activities run in the caller's `RequestContextScope`; kernel `ISender`; `workflows` `IReadinessProbe`; fake moved to `SharedKernel.Workflows.Testing` (P-565, P-566, P-567, P-569, P-571, P-574, P-575) (2026-09-26)
- Contracts redesign follow-up ● `WorkflowErrors` note and package table corrected (2026-09-15)
- P-501 ● `EncryptionPayloadCodec` async migration + associated-data binding (WO-081) (2026-09-08)
- SK.17.Published ● Pack, metadata, `consumer-verify` (2026-07-27)
- SK.17.Docs ● README, XML docs, NuGet metadata (2026-07-27)
- SK.17.Tests ● 158 tests (134 unit + 24 real `WorkflowEnvironment`); three production defects found and fixed (2026-07-23)
- SK.17.Design / Scaffold / Core ● Verified against the compiled Temporalio 1.17.0 assemblies (WO-046, P-287) (2026-07-23)

## Changelog

- [2026-09-28] State map rewritten as a living board; completed phase detail archived outside the repository.
- [2026-09-26] WO-086 foundation refactor recorded (P-565, P-566, P-567, P-569, P-571, P-574, P-575).
- [2026-09-15] Contracts redesign: `WorkflowErrors` note and package table corrected (coordinator).
- [2026-09-08] P-501 (WO-081) shipped end to end — 94/94 tasks ●.
- [2026-09-08] P-501 (WO-081) designed.
