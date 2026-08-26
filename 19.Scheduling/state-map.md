# 19.Scheduling — State Map

> **What this file is:** Phase and task tracker for all work within `19.Scheduling`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.19.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
| --- | --- |
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition | Root Backlog ID |
| --- | --- | --- | --- |
| `SK.19.Design` | Design | All tasks in Phase: Design are `●` | — |
| `SK.19.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` | — |
| `SK.19.Core` | Core | All tasks in Phase: Core are `●` | — |
| `SK.19.Tests` | Tests | All tasks in Phase: Tests are `●` | — |
| `SK.19.Docs` | Docs | All tasks in Phase: Docs are `●` | — |
| `SK.19.Published` | Published | All tasks in Phase: Published are `●` | — |

> **Root Backlog ID column:** left `—` on every lifecycle row deliberately. P-464 spans Design→Published, so attaching it to a single lifecycle key would close it prematurely (see `/state-map-phase` Step S8a, Case 3). It closes via Step S8c when every phase key is `●`.

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement IScheduledJobRegistry | SK.19.Core | SharedKernel.Scheduling | ◐ |
-->

---

## Blocked

_Nothing blocked._

---

## Package Board

| Package | Current Phase | State | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Scheduling` | Design | `◐` | P-464/WO-073. Single package, no `.Abstractions` split — exactly one provider ships today, matching the `SharedKernel.Cryptography`/`.Compression`/`.Guards` single-provider convention. Cron parsing via Quartz's standalone `CronExpression` only; Quartz's `IScheduler`/`JobStore` machinery deliberately not adopted. Design tasks D-01–D-08 now defined; D-06 (Quartz direct-pin decision) resolved. Not yet scaffolded — S-03 blocked on the root `Directory.Packages.props` Quartz pin landing first. |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
| --- | --- | --- | --- |
| `SK.19.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`Result<T>`, `Result`, `Error`, `IClock`) | Available |
| `SK.19.Scaffold` | `01.Core` | `SharedKernel.Configuration` ProjectReference (`AddValidatedOptions`) | Available |
| `SK.19.Scaffold` | `05.Application` | `SharedKernel.Application` ProjectReference — MediatR `ISender` for the `ScheduledCommandJob<TCommand>` bridge | Available |
| `SK.19.Scaffold` | `02.Caching` | `SharedKernel.Caching.Redis.DistributedLocking` ProjectReference — `IFencedLock` for cross-replica single execution (P-434/WO-065) | Available |
| `SK.19.Scaffold` | root `Directory.Packages.props` (devops-lead territory, outside this domain's jurisdiction) | A direct `<PackageVersion Include="Quartz" Version="3.18.1" />` pin — **resolved during Design (D-06), see below**; `SharedKernel.Scheduling.csproj` cannot add `<PackageReference Include="Quartz" />` under Central Package Management until this lands | **Needed** — not yet present on disk; blocks S-03 |
| `SK.19.Core` | `01.Core` | `EventId` range registry entry — domain base `19000`–`19999` | **Design-locked, implementation pending.** `core-arch-planner` locked `Scheduling = 19000` this session (phase key `SK.01.LoggingRangesNewDomains`, design `●`). Do not author a `[LoggerMessage]` method against `19000`–`19999` until `01.Core` ships the entry — track `SK.01.LoggingRangesNewDomains` before starting C-09/C-07 |
| `SK.19.Tests` | `16.Testing` | `RedisContainerFixture` for multi-replica single-execution proof | Available |

> **D-06 finding (Quartz pin, resolved 2026-08-26):** verified on disk, not assumed. `07.Messaging/SharedKernel.Messaging.MassTransit/obj/project.assets.json` shows `MassTransit.Quartz` 9.1.2 (the only `Quartz*` entry in root `Directory.Packages.props`) currently resolves `Quartz/3.18.1` transitively. That reference is fragile for this domain's purposes: `SharedKernel.Scheduling` needs `CronExpression` at compile time, and `07.Messaging` is free to bump or drop `MassTransit.Quartz` independently, which would silently break this domain's build. **Decision: pin directly.** Root `Directory.Packages.props` needs `<PackageVersion Include="Quartz" Version="3.18.1" />` added (matching the version already in use, so no behavior changes for `07.Messaging`); `SharedKernel.Scheduling.csproj` then takes an unversioned `<PackageReference Include="Quartz" />` per this repo's CPM convention. Recorded as a Scaffold-phase cross-domain need above — the `.props` edit itself is outside `19.Scheduling/`'s jurisdiction.
>
> **Downstream (not inbound blockers):** P-464 unblocks `13.ServiceDefaults`'s `WithSchedulingTelemetry` (P-465) and `AddSchedulerReadinessCheck` (P-466), and `16.Testing`'s `IScheduledJobRegistry` fake (P-467). P-466 carries a **new, separately-named `13 → 19` layering grant** recorded in root `CLAUDE.md`'s Hard rules — independent of the `17.Workflows` grant (WO-047), never a widening of it.

---

## Phase: Design <!-- phase-key: SK.19.Design -->

> Lock the registration surface, the job-definition model, misfire/overlap policy enums, the distributed-lock composition, the probe shape, and the Quartz-dependency boundary before any implementation begins. Covers P-464.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-01 | Lock `IScheduledJobRegistry` surface: `AddRecurring<TCommand>(jobName, cronExpression, commandFactory, configure)` and `AddDeferred<TCommand>(jobName, fireAtUtc, commandFactory, configure)`, where `configure` sets a `ScheduledJobOptions` carrying non-defaulted `MisfirePolicy`/`OverlapPolicy` and a nullable `TenantScope`. `commandFactory` is `Func<ScheduledJobExecutionContext, TCommand>` — a factory, not a bare `TCommand`, because a fired job has no external caller to supply one | SharedKernel.Scheduling | `○` |
| D-02 | Design `ScheduledCommandJob<TCommand>`: internal sealed bridge, DI-resolves `ISender`/`IClock`/`ILogger<ScheduledCommandJob<TCommand>>`, `ExecuteAsync(ScheduledJobExecutionContext, CancellationToken)` invokes `commandFactory` then `ISender.Send`, maps the returned `Result`/`Result<T>` to a Fire-succeeded/Fire-failed telemetry outcome — mirrors `17.Workflows`' `CommandActivity<TCommand>` shape, closed generic, zero reflection | SharedKernel.Scheduling | `○` |
| D-03 | Design `MisfirePolicy` (`FireOnce` / `Skip` / `RunImmediatelyThenReschedule`) and `OverlapPolicy` (`Skip` / `Queue` / `Allow`) enums plus their enforcement scope. **Record explicitly:** this domain ships no persistent job store (Invariant 2), so misfire detection is in-process only — computed by comparing `IClock.UtcNow` against the hosted loop's own last-known next-fire time at each tick/startup, never against a durable "last fired at" record. `OverlapPolicy` is enforced per-process (guards a job whose previous run is still executing when the next tick arrives), independent of and in addition to `IFencedLock` cross-replica exclusivity | SharedKernel.Scheduling | `○` |
| D-04 | Design the `IFencedLock` composition: acquired per job per tick, lock key derived from job name via a local key-building helper (never string-concatenated ad hoc at each call site — SK0022-shaped), released after the job body completes. `ScheduledJobExecutionContext` exposes the acquired `FencingToken` (nullable) so a job body that itself writes to a fencing-token-aware downstream resource can propagate it — this domain has no protected resource of its own to re-check the token against, so no internal re-check exists, only propagation. Confirm the startup `Warning` path: omitting `IFencedLock` registration does not fail startup, it logs once and proceeds single-replica-style | SharedKernel.Scheduling | `○` |
| D-05 | Design `ISchedulerServiceProbe`/`SchedulerServiceHealth`: `bool IsRunning`, `int RegisteredJobCount`, `DateTimeOffset? LastTickUtc` — all read from in-process hosted-loop state, zero I/O, matching the zero-I/O contract downstream `13.ServiceDefaults` wiring (P-466) depends on verbatim | SharedKernel.Scheduling | `○` |
| D-06 | Resolve the Quartz-dependency boundary — **resolved this pass, see Cross-Domain Dependencies above.** Verified on disk (not assumed) that `Quartz/3.18.1` is the version `MassTransit.Quartz` 9.1.2 currently resolves transitively; decided a **direct pin is required** (`<PackageVersion Include="Quartz" Version="3.18.1" />` in root `Directory.Packages.props`) rather than relying on the transitive edge, since `07.Messaging` may bump/drop `MassTransit.Quartz` independently of this domain | SharedKernel.Scheduling | `●` |
| D-07 | Design the DI extension surface: `AddSharedKernelScheduling(this IServiceCollection, Action<SchedulingOptions>? configure = null)` returning `ISchedulingBuilder` (`AddRecurring<TCommand>`/`AddDeferred<TCommand>`); registers `SchedulingHostedService` and `ISchedulerServiceProbe` as singletons. `SchedulingOptions.SectionName` const for any config-bound knobs (tick resolution, default lock-acquisition timeout), validated via `AddValidatedOptions` (SK0022-compliant — never a bare `GetSection(...)` literal) | SharedKernel.Scheduling | `○` |
| D-08 | Design `ActivitySource("SharedKernel.Scheduling")` + companion `Meter("SharedKernel.Scheduling")`: span/counter names for Fire, Skip (overlap), Misfire, and LockAcquisitionFailed events, tag keys held in a local `SchedulingTagKeys` constants class (job name, `MisfirePolicy`/`OverlapPolicy` value) — never a raw string literal at an `Activity.SetTag`/counter call site | SharedKernel.Scheduling | `○` |

**Design task count: 8. D-06 ships complete in this pass — the other seven remain planning artifacts for `scheduling-phase-implementer` to execute.**

---

## Phase: Scaffold <!-- phase-key: SK.19.Scaffold -->

> Wire up `.csproj` references, folder structure, solution registration, and empty test stubs — no logic yet.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| S-01 | Create `SharedKernel.Scheduling.csproj` (classlib, `net10.0`) with folder structure `Registry/`, `Jobs/`, `Hosting/`, `Policies/`, `Probes/`, `Options/`, `Diagnostics/` | SharedKernel.Scheduling | `○` |
| S-02 | Add `ProjectReference`s: `01.Core/SharedKernel.Primitives`, `01.Core/SharedKernel.Configuration`, `05.Application/SharedKernel.Application`, `02.Caching/SharedKernel.Caching.Redis.DistributedLocking` (for `IFencedLock`) | SharedKernel.Scheduling | `○` |
| S-03 | Add `<PackageReference Include="Quartz" />` (unversioned, CPM-style) — **blocked** until the root `Directory.Packages.props` pin from D-06 lands (see Cross-Domain Dependencies) | SharedKernel.Scheduling | `⚑` |
| S-04 | Register the project and a `19.Scheduling` solution folder in `Platform.SharedKernel.slnx` | SharedKernel.Scheduling | `○` |
| S-05 | Scaffold an empty `SharedKernel.Scheduling.Tests` project stub (classlib, `net10.0`, references `SharedKernel.Testing`) — no test logic yet | SharedKernel.Scheduling.Tests | `○` |

---

## Phase: Core <!-- phase-key: SK.19.Core -->

> Implement the registry, the hosted scheduling loop, the MediatR command bridge, policy enforcement, the probe, and telemetry.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| C-01 | Implement `IScheduledJobRegistry` + `ScheduledJobOptions`/`RecurringJobRegistration`/`DeferredJobRegistration` records per D-01; registration fails fast (throws at `AddRecurring`/`AddDeferred` call time, not at host startup) if `MisfirePolicy`/`OverlapPolicy` are left unset | SharedKernel.Scheduling | `○` |
| C-02 | Implement `MisfirePolicy`/`OverlapPolicy` enums and `ScheduledJobExecutionContext` (`ScheduledFireTimeUtc`, `ActualFireTimeUtc`, `TenantScope?`, `FencingToken?`) per D-03/D-04 | SharedKernel.Scheduling | `○` |
| C-03 | Implement `ScheduledCommandJob<TCommand>` per D-02 — closed generic per command, zero reflection, `ISender.Send` dispatch, `Result`/`Result<T>` outcome mapped to telemetry | SharedKernel.Scheduling | `○` |
| C-04 | Implement `SchedulingHostedService : BackgroundService` — computes next-fire time via `CronExpression.GetTimeAfter`/`GetNextValidTimeAfter` (never a hand-rolled parser), applies `OverlapPolicy` against an in-process still-running guard, applies `MisfirePolicy` against `IClock.UtcNow` vs. the last-known computed fire time (no persistent store, per D-03) | SharedKernel.Scheduling | `○` |
| C-05 | Wire `IFencedLock` acquisition per job per tick per D-04; when no `IFencedLock` provider is registered, log the mandatory startup `Warning` naming the single-replica-only caveat and proceed unlocked | SharedKernel.Scheduling | `○` |
| C-06 | Implement `ISchedulerServiceProbe`/`SchedulerServiceHealth` per D-05 — zero I/O, reads `SchedulingHostedService`'s in-process state only | SharedKernel.Scheduling | `○` |
| C-07 | Instrument `ActivitySource("SharedKernel.Scheduling")` + `Meter("SharedKernel.Scheduling")` across Fire/Skip/Misfire/LockAcquisitionFailed per D-08. **Blocked on `01.Core` shipping the `19000`–`19999` `LoggingEventIdRanges` entry** (design-locked, not yet implemented — see Cross-Domain Dependencies) before any `[LoggerMessage]` call sites are authored alongside the telemetry | SharedKernel.Scheduling | `⚑` |
| C-08 | Implement `AddSharedKernelScheduling()`/`ISchedulingBuilder` DI extension per D-07; registers `SchedulingHostedService` and `ISchedulerServiceProbe` as singletons | SharedKernel.Scheduling | `○` |
| C-09 | Implement `SchedulingOptions` with `public const string SectionName` (SK0022-compliant) and wire through `AddValidatedOptions`; requires the same `01.Core` EventId entry as C-07 for its own validation-failure logging | SharedKernel.Scheduling | `⚑` |

---

## Phase: Tests <!-- phase-key: SK.19.Tests -->

> The multi-replica single-execution proof is the load-bearing test in this domain — P-464's acceptance criteria is only satisfiable by a genuine two-instance test against a real Redis lock.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| T-01 | Multi-replica single-execution proof: two `SchedulingHostedService` instances, one job, real Redis `IFencedLock` via `16.Testing`'s `RedisContainerFixture` — assert the job body executes exactly once per tick, not once-per-instance | SharedKernel.Scheduling.Tests | `○` |
| T-02 | `MisfirePolicy` behavior tests (`FireOnce`/`Skip`/`RunImmediatelyThenReschedule`) simulating a missed tick by advancing a controllable `IClock` past a scheduled fire time before the loop resumes | SharedKernel.Scheduling.Tests | `○` |
| T-03 | `OverlapPolicy` behavior tests (`Skip`/`Queue`/`Allow`) simulating a job still executing when its next tick arrives | SharedKernel.Scheduling.Tests | `○` |
| T-04 | Cron next-fire correctness across a DST spring-forward and fall-back transition, proving `CronExpression` is used rather than a hand-rolled parser | SharedKernel.Scheduling.Tests | `○` |
| T-05 | `ScheduledCommandJob<TCommand>` dispatch test — verifies `ISender.Send` is invoked with the command produced by the registered factory, and that a `Result.Failure` outcome is distinguishable from a thrown exception in telemetry | SharedKernel.Scheduling.Tests | `○` |
| T-06 | Startup `Warning` assertion when `IFencedLock` is not registered, via `16.Testing`'s structured-log assertion double — never a rendered-message-string comparison | SharedKernel.Scheduling.Tests | `○` |
| T-07 | `ISchedulerServiceProbe` reports `IsRunning`/`RegisteredJobCount`/`LastTickUtc` correctly and performs no I/O (assert via a fake `IFencedLock`/`ISender` that would throw if touched during the probe call) | SharedKernel.Scheduling.Tests | `○` |

---

## Phase: Docs <!-- phase-key: SK.19.Docs -->

> XML docs, package `README.md` wired into the pack via `PackageReadmeFile`, the single-replica-without-lock caveat, and the `17.Workflows` boundary rule restated locally.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| DO-01 | XML docs across the public surface: `IScheduledJobRegistry`, `ScheduledCommandJob<TCommand>`, `MisfirePolicy`, `OverlapPolicy`, `ScheduledJobExecutionContext`, `ISchedulerServiceProbe` | SharedKernel.Scheduling | `○` |
| DO-02 | Package `README.md` with usage examples (recurring-cron registration, one-shot-deferred registration, the `ScheduledCommandJob<TCommand>` bridge), wired into the pack via `PackageReadmeFile` | SharedKernel.Scheduling | `○` |
| DO-03 | Document the single-replica-without-lock caveat prominently in both the README and the XML doc on the `IFencedLock`-less registration path — must match the wording of the startup `Warning` from C-05 | SharedKernel.Scheduling | `○` |
| DO-04 | Restate the `17.Workflows` boundary rule locally in the README (not only in `CLAUDE.md`) — "single unit of work, exactly-once-across-replicas" vs. "multi-step, signal-driven, crash-resumable" | SharedKernel.Scheduling | `○` |
| DO-05 | Document the nullable `TenantScope` per-tenant-fan-out rationale directly in the XML docs on `ScheduledJobOptions.TenantScope` — must state that the job body owns tenant iteration, not the scheduler, so the deviation from the platform's mandatory-tenant-scope convention doesn't read as an oversight | SharedKernel.Scheduling | `○` |

---

## Phase: Published <!-- phase-key: SK.19.Published -->

> Full NuGet metadata, clean `dotnet pack`, and a `consumer-verify` harness proving registration resolves through a real `IHost.StartAsync()`.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| P-01 | NuGet packaging metadata (`PackageId`, description, tags, `PackageReadmeFile`) on `SharedKernel.Scheduling.csproj` | SharedKernel.Scheduling | `○` |
| P-02 | Clean `dotnet pack` verification | SharedKernel.Scheduling | `○` |
| P-03 | `consumer-verify` harness proving `AddSharedKernelScheduling()` resolves through a real `IHost.StartAsync()`, mirroring `11.Communication`'s existing `consumer-verify` precedent | SharedKernel.Scheduling | `○` |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | ⚑ Blocked | State |
| --- | --- | :---: | :---: | :---: | :---: | :---: |
| `SK.19.Design` | Design | 8 | 1 | 7 | 0 | `◐` |
| `SK.19.Scaffold` | Scaffold | 5 | 0 | 4 | 1 | `◐` |
| `SK.19.Core` | Core | 9 | 0 | 7 | 2 | `○` |
| `SK.19.Tests` | Tests | 7 | 0 | 7 | 0 | `○` |
| `SK.19.Docs` | Docs | 5 | 0 | 5 | 0 | `○` |
| `SK.19.Published` | Published | 3 | 0 | 3 | 0 | `○` |

Domain tasks now defined end to end (46 tasks across six phases). Root Phase Backlog carries P-464 (WO-073) for this domain, `○` Pending; P-465/P-466 (`13.ServiceDefaults`) and P-467 (`16.Testing`) depend on it. D-06 (the Quartz-pin question) is the only task resolved in this planning pass — everything else awaits `scheduling-phase-implementer`. S-03, C-07, and C-09 are `⚑` Blocked on cross-domain actions outside this domain's jurisdiction (the `Directory.Packages.props` Quartz pin, and `01.Core`'s `19000`–`19999` `LoggingEventIdRanges` implementation respectively) — see Cross-Domain Dependencies.

---

## Changelog

- [2026-08-26] Domain founded — folder, state-map, and CLAUDE.md created ahead of WO-073 dispatch (P-464)
- [2026-08-26] `scheduling-arch-planner` processed the P-464/WO-073 phase input: populated all six phase sections with 46 tasks total (D-01–D-08, S-01–S-05, C-01–C-09, T-01–T-07, DO-01–DO-05, P-01–P-03). Resolved the open Quartz-pin question (D-06, `●`) by reading `07.Messaging/SharedKernel.Messaging.MassTransit/obj/project.assets.json` directly rather than assuming: `Quartz/3.18.1` is the version currently resolved transitively via `MassTransit.Quartz` 9.1.2; decided a direct pin is required in root `Directory.Packages.props` since the transitive edge is fragile for a compile-time-visible type this domain depends on independently of `07.Messaging`. Recorded that dependency as a Scaffold-phase blocker (S-03, `⚑`) outside this domain's own jurisdiction. Recorded `01.Core`'s design-locked (not yet implemented) `19000` `LoggingEventIdRanges` base as a blocker on the telemetry/options-validation Core tasks (C-07, C-09, both `⚑`)
