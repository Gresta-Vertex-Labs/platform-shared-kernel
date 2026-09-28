# 19.Scheduling — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Scheduling` | Adapter | ● | P-464/WO-073, one package by design (single provider). `IScheduledJobRegistry`, `ScheduledCommandJob<TCommand>` (kernel `ISender`), mandatory `MisfirePolicy`/`OverlapPolicy`, cross-replica single execution through an optional `IDistributedLockService` per-occurrence lease (fencing token in `ScheduledJobExecutionContext.FencingToken`), each run inside a `SystemRequestContext` scope with the job's `TenantScope` (default `TenantScope.Global`) and a new correlation id, `scheduler` probe. Quartz's `CronExpression` for parsing only (Quartz pinned directly in `Directory.Packages.props`). Verified by `consumer-verify`. |

Test double: `InMemoryScheduledJobRegistry` in `16.Testing/SharedKernel.Scheduling.Testing`.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | :---: |
| `SK.19.Design` | Design (D-01–D-08) | ● |
| `SK.19.Scaffold` | Scaffold (S-01–S-05) | ● |
| `SK.19.Core` | Core (C-01–C-09) | ● |
| `SK.19.Tests` | Tests (T-01–T-08; multi-replica proof against real Redis) | ● |
| `SK.19.Docs` | Docs (DO-01–DO-05) | ● |
| `SK.19.Published` | Published (P-01–P-03) | ● |

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

None open. Downstream consumers already shipped: `13.ServiceDefaults`' `WithSchedulingTelemetry` (P-465), the `scheduler` readiness probe mapped by `AddSharedKernelReadiness()` (P-466, superseded by the `IReadinessProbe` model), `16.Testing`'s registry fake (P-467).

## Completed Phases

- WO-086 ● Foundation refactor — `TenantScope` from `SharedKernel.Execution`; runner opens a `RequestContextScope` per run; kernel `ISender`; `scheduler` `IReadinessProbe`; fake moved to `SharedKernel.Scheduling.Testing` (P-565, P-566, P-567, P-569, P-571, P-574, P-575) (2026-09-26)
- SK.19.Published ● Pack, metadata, `consumer-verify` 4/4 (2026-09-04)
- SK.19.Docs ● README and CLAUDE.md (2026-09-04)
- SK.19.Tests ● 47 tests; multi-replica proof executed once Docker was available (2026-09-04)
- SK.19.Core ● Registry, hosting loop, policies, lease composition (per-occurrence claim-and-hold) (2026-09-04)
- SK.19.Scaffold ● Project, Quartz pin, `.slnx` registration (2026-09-04)
- SK.19.Design ● D-01–D-08; Quartz pin question resolved (D-06) (2026-08-26)

## Changelog

- [2026-09-28] State map rewritten as a living board; completed phase detail archived outside the repository.
- [2026-09-26] WO-086 foundation refactor recorded (P-565, P-566, P-567, P-569, P-571, P-574, P-575).
- [2026-09-04] Coordinator follow-up: `.slnx` registration done centrally; Testcontainers proofs executed; every phase ●.
- [2026-09-04] `scheduling-phase-implementer` implemented P-464 end to end; lock composition corrected to per-occurrence claim-and-hold; `02.Caching` reference narrowed to `.Abstractions`.
- [2026-08-26] Design pass populated all six phases (46 tasks); Quartz 3.18.1 pin resolved.
