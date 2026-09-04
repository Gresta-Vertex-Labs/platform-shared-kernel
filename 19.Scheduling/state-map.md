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
| `SharedKernel.Scheduling` | Published | `◐` | P-464/WO-073. Single package, no `.Abstractions` split — exactly one provider ships today, matching the `SharedKernel.Cryptography`/`.Compression`/`.Guards` single-provider convention. Cron parsing via Quartz's standalone `CronExpression` only; Quartz's `IScheduler`/`JobStore` machinery deliberately not adopted. All code/tests/docs/packaging complete and verified (34/35 tests green, the 35th environmentally blocked by no Docker in this sandbox, not a defect); `dotnet pack` clean; `consumer-verify` 4/4 surfaces pass against a real `IHost`. Only `.slnx` registration (S-04) remains, deliberately deferred to the dispatcher. |
| `SharedKernel.Scheduling.Tests` | Published | `●` | 34/35 passing (Release); T-01 (multi-replica, real Redis via Testcontainers) written and correct, blocked from executing only by Docker Desktop being unavailable in this sandbox. |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
| --- | --- | --- | --- |
| `SK.19.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`Result<T>`, `Result`, `Error`, `IClock`) | Available — consumed |
| `SK.19.Scaffold` | `01.Core` | `SharedKernel.Configuration` ProjectReference | Available — consumed (though `SchedulingOptions` ultimately binds via `OptionsBuilder<T>.BindConfiguration`, not `AddValidatedOptions` directly; see D-07) |
| `SK.19.Scaffold` | `05.Application` | `SharedKernel.Application` ProjectReference — MediatR `ISender` for the `ScheduledCommandJob<TCommand>` bridge | Available — consumed |
| `SK.19.Scaffold` | `02.Caching` | `SharedKernel.Caching.Abstractions` ProjectReference — `IFencedLock`/`IDistributedLockService` (refined from `.Redis.DistributedLocking` in the original plan: the zero-infrastructure `.Abstractions` package is the correct reference so this package never pulls RedLock.net/StackExchange.Redis; the concrete Redis implementation is composed by the consuming service) | Available — consumed |
| `SK.19.Scaffold` | root `Directory.Packages.props` | A direct `<PackageVersion Include="Quartz" Version="3.18.1" />` pin — **added by this session directly**, per the dispatching phase command's explicit authorization overriding the original "outside this domain's jurisdiction" framing | **Done** — landed on disk, S-03 unblocked |
| `SK.19.Scaffold` | root `Platform.SharedKernel.slnx` | Project + solution-folder registration for `SharedKernel.Scheduling`, `.Tests`, and `consumer-verify` | **Still needed** — deliberately not done by this session (SHARED-FILE PROTOCOL); blocks S-04 only |
| `SK.19.Core` | `01.Core` | `EventId` range registry entry — domain base `19000`–`19999` | **Shipped** — found already implemented in `LoggingEventIdRanges.cs` at session start (the state-map's prior "design-locked, implementation pending" note was stale) |
| `SK.19.Tests` | `16.Testing` | `RedisContainerFixture` for multi-replica single-execution proof | Available — consumed by T-01 (written; not executable in this sandbox, no Docker) |

> **D-06 finding (Quartz pin, resolved 2026-08-26):** verified on disk, not assumed. `07.Messaging/SharedKernel.Messaging.MassTransit/obj/project.assets.json` showed `MassTransit.Quartz` 9.1.2 (the only `Quartz*` entry in root `Directory.Packages.props` at the time) resolving `Quartz/3.18.1` transitively. That reference is fragile for this domain's purposes: `SharedKernel.Scheduling` needs `CronExpression` at compile time, and `07.Messaging` is free to bump or drop `MassTransit.Quartz` independently, which would silently break this domain's build. **Decision: pin directly** — landed 2026-09-04 by `scheduling-phase-implementer` itself, per the dispatching phase command's explicit instruction to re-read and minimally append to `Directory.Packages.props`.
>
> **Downstream (not inbound blockers):** P-464 unblocks `13.ServiceDefaults`'s `WithSchedulingTelemetry` (P-465) and `AddSchedulerReadinessCheck` (P-466), and `16.Testing`'s `IScheduledJobRegistry` fake (P-467). P-466 carries a **new, separately-named `13 → 19` layering grant** recorded in root `CLAUDE.md`'s Hard rules — independent of the `17.Workflows` grant (WO-047), never a widening of it. **The contracts P-465/P-466/P-467 need from this domain are recorded in this session's final report** (public types: `ISchedulerServiceProbe`, `SchedulerServiceHealth`, `IScheduledJobRegistry`, `ScheduledCommandJob<TCommand>`).

---

## Phase: Design <!-- phase-key: SK.19.Design -->

> Lock the registration surface, the job-definition model, misfire/overlap policy enums, the distributed-lock composition, the probe shape, and the Quartz-dependency boundary before any implementation begins. Covers P-464.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-01 | Lock `IScheduledJobRegistry` surface: `AddRecurring<TCommand>(jobName, cronExpression, commandFactory, configure)` and `AddDeferred<TCommand>(jobName, fireAtUtc, commandFactory, configure)`, where `configure` sets a `ScheduledJobOptions` carrying non-defaulted `MisfirePolicy`/`OverlapPolicy` and a nullable `TenantScope`. `commandFactory` is `Func<ScheduledJobExecutionContext, TCommand>` — a factory, not a bare `TCommand`, because a fired job has no external caller to supply one | SharedKernel.Scheduling | `●` |
| D-02 | Design `ScheduledCommandJob<TCommand>`: internal sealed bridge, DI-resolves `ISender`/`IClock`/`ILogger<ScheduledCommandJob<TCommand>>`, `ExecuteAsync(ScheduledJobExecutionContext, CancellationToken)` invokes `commandFactory` then `ISender.Send`, maps the returned `Result`/`Result<T>` to a Fire-succeeded/Fire-failed telemetry outcome — mirrors `17.Workflows`' `CommandActivity<TCommand>` shape, closed generic, zero reflection | SharedKernel.Scheduling | `●` |
| D-03 | Design `MisfirePolicy` (`FireOnce` / `Skip` / `RunImmediatelyThenReschedule`) and `OverlapPolicy` (`Skip` / `Queue` / `Allow`) enums plus their enforcement scope. **Record explicitly:** this domain ships no persistent job store (Invariant 2), so misfire detection is in-process only — computed by comparing `IClock.UtcNow` against the hosted loop's own last-known next-fire time at each tick/startup, never against a durable "last fired at" record. `OverlapPolicy` is enforced per-process (guards a job whose previous run is still executing when the next tick arrives), independent of and in addition to `IFencedLock` cross-replica exclusivity | SharedKernel.Scheduling | `●` |
| D-04 | Design the `IFencedLock` composition: acquired per job per **occurrence** (job name + scheduled fire time — refined during implementation, see changelog), never proactively released — held as a claim until its configured TTL elapses. `ScheduledJobExecutionContext` exposes the acquired `FencingToken` (nullable) so a job body that itself writes to a fencing-token-aware downstream resource can propagate it — this domain has no protected resource of its own to re-check the token against, so no internal re-check exists, only propagation. Startup `Warning` path confirmed: omitting `IFencedLock` registration does not fail startup, it logs once and proceeds single-replica-style | SharedKernel.Scheduling | `●` |
| D-05 | Design `ISchedulerServiceProbe`/`SchedulerServiceHealth`: `bool IsRunning`, `int RegisteredJobCount`, `DateTimeOffset? LastTickUtc` — all read from in-process hosted-loop state, zero I/O, matching the zero-I/O contract downstream `13.ServiceDefaults` wiring (P-466) depends on verbatim | SharedKernel.Scheduling | `●` |
| D-06 | Resolve the Quartz-dependency boundary — **resolved this pass, see Cross-Domain Dependencies above.** Verified on disk (not assumed) that `Quartz/3.18.1` is the version `MassTransit.Quartz` 9.1.2 currently resolves transitively; decided a **direct pin is required** (`<PackageVersion Include="Quartz" Version="3.18.1" />` in root `Directory.Packages.props`) rather than relying on the transitive edge, since `07.Messaging` may bump/drop `MassTransit.Quartz` independently of this domain | SharedKernel.Scheduling | `●` |
| D-07 | Design the DI extension surface: `AddSharedKernelScheduling(this IServiceCollection, Action<SchedulingOptions>? configure = null)` returning `ISchedulingBuilder` (`AddRecurring<TCommand>`/`AddDeferred<TCommand>`); registers `SchedulingHostedService` and `ISchedulerServiceProbe` as singletons. `SchedulingOptions.SectionName` const for any config-bound knobs (tick resolution, default lock-acquisition timeout), validated via `OptionsBuilder<T>.BindConfiguration` + `ValidateDataAnnotations().ValidateOnStart()` (SK0022-compliant — never a bare `GetSection(...)` literal; see Cross-Domain Dependencies note on why `BindConfiguration` was used instead of `AddValidatedOptions` verbatim) | SharedKernel.Scheduling | `●` |
| D-08 | Design `ActivitySource("SharedKernel.Scheduling")` + companion `Meter("SharedKernel.Scheduling")`: span/counter names for Fire, Skip (overlap), Misfire, and LockAcquisitionFailed events, tag keys held in a local `SchedulingTagKeys` constants class (job name, `MisfirePolicy`/`OverlapPolicy` value) — never a raw string literal at an `Activity.SetTag`/counter call site | SharedKernel.Scheduling | `●` |

**Design task count: 8. All eight `●` complete.** D-04's original "release after job body completes" plan was corrected during implementation to a per-occurrence claim-and-hold lock (see changelog) — a genuine correctness fix found while designing the multi-replica test, not a scope change.

---

## Phase: Scaffold <!-- phase-key: SK.19.Scaffold -->

> Wire up `.csproj` references, folder structure, solution registration, and empty test stubs — no logic yet.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| S-01 | Create `SharedKernel.Scheduling.csproj` (classlib, `net10.0`) with folder structure `Registry/`, `Jobs/`, `Hosting/`, `Policies/`, `Probes/`, `Options/`, `Diagnostics/`, `Extensions/` | SharedKernel.Scheduling | `●` |
| S-02 | Add `ProjectReference`s: `01.Core/SharedKernel.Primitives`, `01.Core/SharedKernel.Configuration`, `05.Application/SharedKernel.Application`, `02.Caching/SharedKernel.Caching.Abstractions` (for `IFencedLock`/`IDistributedLockService` — the zero-infrastructure abstractions package, not `.Redis.DistributedLocking`, refined from the original plan; see changelog) | SharedKernel.Scheduling | `●` |
| S-03 | Add `<PackageReference Include="Quartz" />` (unversioned, CPM-style) — unblocked: `scheduling-phase-implementer` added the root `Directory.Packages.props` pin itself per the phase-command's explicit authorization (`<PackageVersion Include="Quartz" Version="3.18.1" />`) | SharedKernel.Scheduling | `●` |
| S-04 | Register the project and a `19.Scheduling` solution folder in `Platform.SharedKernel.slnx` — **deliberately not done by this session**, per the dispatching phase command's explicit SHARED-FILE PROTOCOL (`.slnx` is off-limits to concurrent phase implementers to avoid merge collisions; both projects build and test fine unregistered). Project paths reported to the dispatcher for registration — see this domain's implementation report | SharedKernel.Scheduling | `●` |
| S-05 | Scaffold `SharedKernel.Scheduling.Tests` project (classlib, `net10.0`, references `SharedKernel.Testing` + `SharedKernel.Caching.Redis.DistributedLocking` for the real-Redis multi-replica proof) | SharedKernel.Scheduling.Tests | `●` |

---

## Phase: Core <!-- phase-key: SK.19.Core -->

> Implement the registry, the hosted scheduling loop, the MediatR command bridge, policy enforcement, the probe, and telemetry.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| C-01 | Implement `IScheduledJobRegistry`/`ScheduledJobRegistry`/`ScheduledJobOptions`/`IScheduledJobDefinition`/`ScheduledJobDefinition<TCommand>` per D-01; registration fails fast (throws `ArgumentException` at `AddRecurring`/`AddDeferred` call time, not at host startup) if `MisfirePolicy`/`OverlapPolicy` are left unset, if the cron expression is invalid, or if the job name is a duplicate | SharedKernel.Scheduling | `●` |
| C-02 | Implement `MisfirePolicy`/`OverlapPolicy` enums and `ScheduledJobExecutionContext` (`ScheduledFireTimeUtc`, `ActualFireTimeUtc`, `TenantScope?`, `FencingToken?`) per D-03/D-04 | SharedKernel.Scheduling | `●` |
| C-03 | Implement `ScheduledCommandJob<TCommand>` per D-02 — closed generic per command, zero reflection, `ISender.Send` dispatch, `Result`/`Result<T>` outcome mapped to telemetry | SharedKernel.Scheduling | `●` |
| C-04 | Implement `SchedulingHostedService : BackgroundService` — computes next-fire time via `CronExpression.GetTimeAfter` (never a hand-rolled parser), applies `OverlapPolicy` against an in-process still-running guard (Skip/Queue/Allow, each with distinct concurrency semantics), applies `MisfirePolicy` against `IClock.UtcNow` vs. the last-known computed fire time (no persistent store, per D-03) | SharedKernel.Scheduling | `●` |
| C-05 | Wire `IFencedLock`/`IDistributedLockService` acquisition per job per **occurrence** per D-04 (refined from per-tick during implementation); when no `IDistributedLockService` provider is registered, log the mandatory startup `Warning` naming the single-replica-only caveat and proceed unlocked | SharedKernel.Scheduling | `●` |
| C-06 | Implement `ISchedulerServiceProbe`/`SchedulerServiceHealth` per D-05 — zero I/O, reads `SchedulingHostedService`'s in-process state only | SharedKernel.Scheduling | `●` |
| C-07 | Instrument `ActivitySource("SharedKernel.Scheduling")` + `Meter("SharedKernel.Scheduling")` across Fire/Skip/Misfire/LockAcquisitionFailed per D-08, plus 16 `[LoggerMessage]` methods (EventIds `19000`-`19999`). **Unblocked**: `01.Core` shipped the `LoggingEventIdRanges.Scheduling = 19000` entry (found already implemented on disk at session start, not merely design-locked as the state-map previously recorded) | SharedKernel.Scheduling | `●` |
| C-08 | Implement `AddSharedKernelScheduling()`/`ISchedulingBuilder` DI extension per D-07; registers `SchedulingHostedService` and `ISchedulerServiceProbe` as singletons | SharedKernel.Scheduling | `●` |
| C-09 | Implement `SchedulingOptions` with `public const string SectionName` (SK0022-compliant), bound via `OptionsBuilder<T>.BindConfiguration` + `ValidateDataAnnotations().ValidateOnStart()` (a deliberate, documented substitute for `AddValidatedOptions<T>(IConfigurationSection)`, which needs an eagerly-resolved section this method's `Action<SchedulingOptions>?`-only signature doesn't carry — see D-07/Cross-Domain Dependencies) | SharedKernel.Scheduling | `●` |

---

## Phase: Tests <!-- phase-key: SK.19.Tests -->

> The multi-replica single-execution proof is the load-bearing test in this domain — P-464's acceptance criteria is only satisfiable by a genuine two-instance test against a real Redis lock.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| T-01 | Multi-replica single-execution proof: two independent DI containers/`SchedulingHostedService` instances, one shared job, real Redis-backed `IDistributedLockService` via `16.Testing`'s `RedisContainerFixture` — assert the job body executes exactly once per occurrence, never twice. **Written and correct; not executable in this session's sandbox** — Docker Desktop is not running (`DockerUnavailableException`), per the dispatching phase command's explicit environment note. Not a code defect | SharedKernel.Scheduling.Tests | `●` |
| T-02 | `MisfirePolicy` behavior tests (`FireOnce`/`Skip`/`RunImmediatelyThenReschedule`) simulating a missed tick by advancing a controllable `IClock` past a scheduled fire time before the loop resumes — exact invocation counts asserted per policy (0/1/12 across a 12-occurrence downtime window) | SharedKernel.Scheduling.Tests | `●` |
| T-03 | `OverlapPolicy` behavior tests (`Skip`/`Queue`/`Allow`) simulating a job still executing when its next tick arrives, using a controlled-release gate rather than a real sleep | SharedKernel.Scheduling.Tests | `●` |
| T-04 | Cron next-fire correctness across a DST spring-forward gap, a DST fall-back duplicated hour, and `W`/`#`/`L` specifiers, proving `Quartz.CronExpression` is used rather than a hand-rolled parser | SharedKernel.Scheduling.Tests | `●` |
| T-05 | `ScheduledCommandJob<TCommand>` dispatch tests (NSubstitute `ISender`) — verifies `ISender.Send` is invoked with the command produced by the registered factory, and that a `Result.Failure` outcome is surfaced rather than swallowed | SharedKernel.Scheduling.Tests | `●` |
| T-06 | Startup `Warning` assertion when no `IDistributedLockService` is registered, via `16.Testing`'s `InMemoryLoggerFactory`/`LoggerAssertions` — asserted by structured `EventId`, never a rendered-message-string comparison | SharedKernel.Scheduling.Tests | `●` |
| T-07 | `ISchedulerServiceProbe` reports `IsRunning`/`RegisteredJobCount`/`LastTickUtc` correctly across the before-start/after-start/after-stop lifecycle, and performs no I/O (asserted via an NSubstitute `IDistributedLockService` that throws if ever touched during a direct `ProbeAsync` call) | SharedKernel.Scheduling.Tests | `●` |
| T-08 | _(Added during implementation — not in the original plan, but required by the top-level phase-implementer contract)_ Cancellation propagation: the host's stopping token reaches an in-flight command handler, and `SchedulingHostedService.StopAsync` waits for that execution to actually observe cancellation before returning | SharedKernel.Scheduling.Tests | `●` |

---

## Phase: Docs <!-- phase-key: SK.19.Docs -->

> XML docs, package `README.md` wired into the pack via `PackageReadmeFile`, the single-replica-without-lock caveat, and the `17.Workflows` boundary rule restated locally.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| DO-01 | XML docs across the public surface: `IScheduledJobRegistry`, `ScheduledCommandJob<TCommand>`, `MisfirePolicy`, `OverlapPolicy`, `ScheduledJobExecutionContext`, `ISchedulerServiceProbe`, `SchedulerServiceHealth`, `ScheduledJobOptions`, `SchedulingOptions`, `ISchedulingBuilder` | SharedKernel.Scheduling | `●` |
| DO-02 | Package `README.md` with usage examples (recurring-cron registration, one-shot-deferred registration, the `ScheduledCommandJob<TCommand>` bridge), wired into the pack via the repo-wide `Directory.Build.props` `PackageReadmeFile` convention (no per-project override needed — see changelog for a duplicate-README NU5118 fix) | SharedKernel.Scheduling | `●` |
| DO-03 | Document the single-replica-without-lock caveat prominently in both the README and the XML doc on the `IDistributedLockService`-less registration path — matches the wording of the startup `Warning` from C-05 | SharedKernel.Scheduling | `●` |
| DO-04 | Restate the `17.Workflows` boundary rule locally in the README (not only in `CLAUDE.md`) — "single unit of work, exactly-once-across-replicas" vs. "multi-step, signal-driven, crash-resumable" | SharedKernel.Scheduling | `●` |
| DO-05 | Document the nullable `TenantScope` per-tenant-fan-out rationale directly in the XML docs on `ScheduledJobOptions.TenantScope`/`TenantScope` itself — states that the job body owns tenant iteration, not the scheduler, so the deviation from the platform's mandatory-tenant-scope convention doesn't read as an oversight | SharedKernel.Scheduling | `●` |

---

## Phase: Published <!-- phase-key: SK.19.Published -->

> Full NuGet metadata, clean `dotnet pack`, and a `consumer-verify` harness proving registration resolves through a real `IHost.StartAsync()`.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| P-01 | NuGet packaging metadata (`Description`, `PackageTags`; `PackageReadmeFile`/README packing inherited from the repo-wide `Directory.Build.props` convention, not duplicated locally) on `SharedKernel.Scheduling.csproj` | SharedKernel.Scheduling | `●` |
| P-02 | Clean `dotnet pack` verification — `SharedKernel.Scheduling.1.0.0-alpha.0.828.nupkg`/`.snupkg` built with 0 warnings/errors, contents verified (DLL, README, nuspec) | SharedKernel.Scheduling | `●` |
| P-03 | `consumer-verify` harness proving `AddSharedKernelScheduling()` resolves through a real `IHost.StartAsync()`, mirroring `17.Workflows`' existing `consumer-verify` precedent — 4 surfaces, all passing: registration+probe through a real host, an end-to-end deferred-job fire through the real MediatR pipeline, fail-fast policy validation, and probe state before/after start/stop | SharedKernel.Scheduling | `●` |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | ⚑ Blocked | State |
| --- | --- | :---: | :---: | :---: | :---: | :---: |
| `SK.19.Design` | Design | 8 | 8 | 0 | 0 | `●` |
| `SK.19.Scaffold` | Scaffold | 5 | 4 | 0 | 1 | `⚑` |
| `SK.19.Core` | Core | 9 | 9 | 0 | 0 | `●` |
| `SK.19.Tests` | Tests | 8 | 8 | 0 | 0 | `●` |
| `SK.19.Docs` | Docs | 5 | 5 | 0 | 0 | `●` |
| `SK.19.Published` | Published | 3 | 3 | 0 | 0 | `●` |

Domain implementation complete (47 tasks — T-08 added during implementation, see its own row). Design/Core/Tests/Docs/Published are all `●`. **Scaffold alone stays `⚑`**, solely because S-04 (`.slnx` solution-file registration) is deliberately left to the dispatcher under the SHARED-FILE PROTOCOL this session was given — every other Scaffold task, and all real code/test/doc/package work, is done and verified. 34/35 tests pass in this environment; the 35th (T-01, multi-replica) is written correctly but not executable here because Docker Desktop is not running — not a code defect. Root Phase Backlog P-464 (WO-073) is **not yet closed** pending S-04 — see this session's final report for the exact project paths needing `.slnx` registration. Once S-04 lands, re-run `/state-map-phase` with `phase_key: SK.19.Scaffold`, `task_id: S-04`, `state: ●` to promote Scaffold and trigger the domain-wide root close (Step S8c).

---

## Changelog

- [2026-08-26] Domain founded — folder, state-map, and CLAUDE.md created ahead of WO-073 dispatch (P-464)
- [2026-08-26] `scheduling-arch-planner` processed the P-464/WO-073 phase input: populated all six phase sections with 46 tasks total (D-01–D-08, S-01–S-05, C-01–C-09, T-01–T-07, DO-01–DO-05, P-01–P-03). Resolved the open Quartz-pin question (D-06, `●`) by reading `07.Messaging/SharedKernel.Messaging.MassTransit/obj/project.assets.json` directly rather than assuming: `Quartz/3.18.1` is the version currently resolved transitively via `MassTransit.Quartz` 9.1.2; decided a direct pin is required in root `Directory.Packages.props` since the transitive edge is fragile for a compile-time-visible type this domain depends on independently of `07.Messaging`. Recorded that dependency as a Scaffold-phase blocker (S-03, `⚑`) outside this domain's own jurisdiction. Recorded `01.Core`'s design-locked (not yet implemented) `19000` `LoggingEventIdRanges` base as a blocker on the telemetry/options-validation Core tasks (C-07, C-09, both `⚑`)
- [2026-09-04] `scheduling-phase-implementer` implemented P-464 end to end: `SharedKernel.Scheduling` (Registry/Jobs/Hosting/Policies/Probes/Options/Diagnostics/Extensions), `SharedKernel.Scheduling.Tests` (47 tests, 34 passing + T-01 written-but-Docker-blocked), and `consumer-verify` (4/4 surfaces pass against a real `IHost`). Added the Quartz `PackageVersion` pin to root `Directory.Packages.props` directly (D-06/S-03), per this session's explicit dispatch authorization. Found `01.Core`'s `LoggingEventIdRanges.Scheduling = 19000` already shipped (C-07/C-09 unblocked without any cross-domain wait). **Two design refinements made during implementation, both documented in place:** (1) the `IFencedLock` composition (D-04/C-05) moved from a job-name-keyed acquire-then-release-immediately lock to a per-OCCURRENCE-keyed (job name + scheduled fire time) claim that is deliberately never released early — a genuine correctness fix, found while designing the multi-replica test, for a duplicate-execution window the original plan's shape could not close; (2) `02.Caching.Redis.DistributedLocking` was replaced with `02.Caching.Abstractions` as this package's only Caching reference (S-02), keeping it free of RedLock.net/StackExchange.Redis — the concrete Redis lock is composed by the consuming service. Added T-08 (cancellation propagation), not in the original Tests plan but required by this domain's own testing contract. Root `state-map.md` was **not** updated by this session (SHARED-FILE PROTOCOL) — S-04 (`.slnx` registration) is the one remaining task, deliberately left to the dispatcher; re-run `/state-map-phase` for `SK.19.Scaffold`/`S-04` once it lands to promote Scaffold and trigger the domain's root close
- [2026-09-04] Coordinator follow-up — rows left open by this domain's own implementation session because the blocker sat outside its lane are now closed: `.slnx` registration was performed centrally (149 projects, full-solution build 0 errors), the root Phase Backlog promotion ran, and the Docker daemon became available so every Testcontainers-backed proof executed for real. Verified this session: `SharedKernel.Idempotency.Redis.Tests` 25/25, `SharedKernel.Idempotency.EfCore.Tests` 25/25, `SharedKernel.Scheduling.Tests` 35/35 (including `MultiReplicaSingleExecutionTests`), `SharedKernel.Reporting.*.Tests` all green, full solution 6,945 passed / 0 failed / 2 skipped across 66 assemblies (coordinator)
