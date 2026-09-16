# 19.Scheduling — Domain Brain

## What This Domain Is

Lightweight, time-triggered job dispatch: **cron/recurring** and **one-shot deferred** execution, with cross-replica single-execution guarantees. One package, `SharedKernel.Scheduling`.

It is the answer to *"run reconciliation nightly at 02:00"* and *"retry this in 15 minutes"* — nothing more ambitious than that, by design.

---

## The Boundary Against 17.Workflows — read this first

This domain exists in a space `17.Workflows` deliberately does not occupy, and the line between them is the single most important thing to get right here. It is recorded in root `CLAUDE.md`'s "What Goes Where" table and restated here because a future session will be tempted to blur it.

| Use `19.Scheduling` when… | Use `17.Workflows` when… |
|---|---|
| Firing a **single unit of work** on a time-based trigger | The process has **multiple steps** |
| *"Did it fire, exactly once, across replicas"* is the only durability question | The process uses signals/queries, or must survive partial completion across a deploy or a crash mid-step |
| Cron or one-shot deferred | Durable, replay-safe, deterministic execution |

**Composition is legitimate and expected:** a recurring trigger that *starts* a multi-step Temporal workflow uses both — `19.Scheduling` fires the trigger, `17.Workflows` owns everything after that. That is the intended shape, not a layering smell.

**Why `17.Workflows` was not simply extended:** its own brain records that Hangfire, Elsa, Dapr Workflow and MassTransit sagas were all evaluated and rejected — but that evaluation concerned *durable, multi-step, replay-safe business processes*. It says nothing about cron. Requiring a full Temporal worker for a single nightly tick is real overkill that teams will route around by hand-rolling a `BackgroundService` with a timer — which is silently wrong across N replicas (duplicate execution, no misfire handling). This domain closes that gap with primitives the platform already owns.

---

## Packages

```
SharedKernel.Scheduling   → the whole domain, single package
```

**No `.Abstractions` split, and none may be added without a ratified second backend.** Exactly one provider ships, matching the documented single-provider convention already used by `SharedKernel.Cryptography`, `SharedKernel.Compression`, and `SharedKernel.Guards`. If a second scheduling backend is ever genuinely proposed, the split is reconsidered then — not pre-emptively.

Note this is a *different* justification from `17.Workflows`' single-package decision. That domain argues the programming model **is** the abstraction (determinism/replay/`Workflow.Patched` versioning are not swappable). Here the reasoning is simply that one provider exists. Do not conflate the two rationales.

---

## Layering

```
19.Scheduling → may reference 01.Core, 02.Caching.Abstractions, 05.Application
```

**`02.Caching.Abstractions` only — never `.Redis.DistributedLocking`.** `IFencedLock`/`IDistributedLockService` live in the zero-infrastructure `.Abstractions` package (no RedLock.net/StackExchange.Redis transitively). This package never references the concrete Redis implementation — the consuming service composes `AddRedisDistributedLocking()` at its own composition root, alongside `AddSharedKernelScheduling()`. (The `.Tests` project is the one exception, by design: it references `.Redis.DistributedLocking` directly for the real multi-replica proof — never mock `IDistributedLockService` for that assertion.)

**No `04.Contracts` reference.** Nothing shipped in this package needs a `04.Contracts` type on its own signature — dropped from the original plan's allowed-reference list once implementation confirmed it was never actually used.

**Inbound grant — narrow and separately named.** `13.ServiceDefaults` may take a `ProjectReference` to `SharedKernel.Scheduling` **solely** to resolve `ISchedulerServiceProbe`/`SchedulerServiceHealth` for `AddSchedulerReadinessCheck` (P-466/WO-073).

No other type in this package — `IScheduledJobRegistry`, `ScheduledCommandJob<TCommand>`, `MisfirePolicy`, `OverlapPolicy`, the options types — may be reached through that grant.

This grant is **independent of, and never a widening of**, the `17.Workflows` grant (WO-047). Root `CLAUDE.md` is emphatic that a domain above `13` wanting the same pattern requires its own named grant, never reasoning by analogy. That rule applied to this domain and was honoured; it applies equally to the next one.

---

## Domain Invariants

**1 — Never hand-roll cron.** Cron-expression parsing and next-fire-time computation go through Quartz's standalone `CronExpression` class. A hand-written parser is a defect: cron's edge cases (DST transitions, `L`/`W`/`#` specifiers, day-of-week vs day-of-month interaction) are exactly where naive implementations break, silently and at 02:00.

**2 — Quartz's scheduler machinery is deliberately not adopted.** `CronExpression` only. Quartz's `IScheduler`, `ITrigger`, `IJobDetail`, and clustered `JobStore` would stand up a second, competing persistence story alongside `06.Persistence`. No raw Quartz type may ever be exposed to application code — the same no-raw-client rule `10.Intelligence` applies to `QdrantClient` and `17.Workflows` applies to `ITemporalClient`.

**3 — Single execution across replicas is opt-in but loudly defaulted.** Cross-replica single execution comes from acquiring an `IDistributedLockService` (`02.Caching.Abstractions`, real implementation from `.Redis.DistributedLocking`, P-434/WO-065) **per occurrence** — never per tick alone; see the corrected lock-composition note under Technology and Job Execution Model point 3. Omitting a lock provider is allowed for single-replica and dev use, but **must log a startup `Warning`** naming the single-replica-only caveat — mirroring `11.Communication.Internal`'s static-resolver precedent, which warns for exactly the same class of "fine locally, wrong in production" configuration.

**4 — Misfire and overlap policy are mandatory, never defaulted silently.** `MisfirePolicy` (`FireOnce` / `Skip` / `RunImmediatelyThenReschedule`) and `OverlapPolicy` (`Skip` / `Queue` / `Allow`) are non-defaulted parameters at registration. A team must state what happens when a scheduled run is missed because the service was down, and what happens when a run is still executing at the next tick. Guessing on their behalf is how duplicate reconciliation runs happen.

**5 — `TenantScope` is nullable here — a deliberate, documented deviation.** Every other tenant-aware domain (`09.Search`, `10.Intelligence`, `17.Workflows`, `18.Idempotency`) makes tenant scope a mandatory, non-defaulted parameter. This domain does not, because a scheduled job is registered **once, at startup, as a system-level actor**. A genuinely per-tenant recurring job ("send each active tenant's weekly digest") iterates its own tenant directory inside the job body; the scheduler does not spawn N tenant-scoped executions on its behalf. The XML docs must state this rationale — otherwise it reads as an oversight and someone will "fix" it.

**6 — MediatR is the command bridge, with zero reflection.** `ScheduledCommandJob<TCommand>` is the scheduling-side counterpart to `17.Workflows`' `CommandActivity<TCommand>` — a closed generic per command dispatching via `ISender`. Never `Type.GetMethod` + `MakeGenericMethod` + `Invoke`; that shape is forbidden platform-wide. Because every execution gets its own DI scope, the dispatched command is the **outermost** command for `05.Application.Behaviors`' `ICommandScope`: `TransactionBehavior` commits once on success, `ICommandScope.OnCompleted` callbacks run before the fire is reported, and validation/authorization failures come back as a failed `Result` (logged as a failed fire), never as an exception. A service that opts into `AuthorizationBehavior` must register an `IRequestContext` representing its system identity for job scopes, or guarded commands fail closed.

**7 — The probe is zero-I/O.** `ISchedulerServiceProbe` reports whether the hosted loop is running and how many jobs are registered, from in-process state only. This domain ships the probe **primitive** and no `IHealthCheck` — the wiring is `13.ServiceDefaults`' concern, matching the `06`/`07`/`08`/`09`/`10`/`17` split exactly.

---

## Technology

| Concern | Choice | Notes |
|---|---|---|
| Cron parsing | Quartz `CronExpression` (standalone class only) | **Shipped.** `Quartz/3.18.1` pinned directly in root `Directory.Packages.props` (matches what `MassTransit.Quartz` 9.1.2 resolves transitively, so no behavior change for `07.Messaging`). `ScheduledJobRegistry.ParseCron` always sets `CronExpression.TimeZone = TimeZoneInfo.Utc` explicitly — the class defaults to `TimeZoneInfo.Local`, which would make this domain's scheduling non-deterministic across environments given every other decision here is UTC-anchored (`IClock.UtcNow`, `ScheduledJobExecutionContext` timestamps). A consequence: this package's own runtime behavior is deliberately DST-invariant, by design — DST correctness is proven directly against `CronExpression` with a real DST-observing zone instead (see `.Tests/Cron/CronExpressionCorrectnessTests.cs`) |
| Scheduler loop | An `IHostedService` (`SchedulingHostedService : BackgroundService`) owned by this package | Not Quartz's `IScheduler`. Computes next-fire time via `CronExpression.GetTimeAfter` only (not `GetNextValidTimeAfter` — both are equivalent for this domain's usage; `GetTimeAfter` was chosen as primary) |
| Cross-replica lock | `02.Caching.Abstractions`'s `IDistributedLockService`/`IFencedLock` | **Corrected during implementation — this is a genuine correctness fix, not a style choice.** The lock is a **per-OCCURRENCE claim** (`SchedulingLockKeys.ForOccurrence(jobName, scheduledFireTimeUtc)` — job name **and** scheduled fire time), acquired with `wait: TimeSpan.Zero` (single non-blocking attempt) and **deliberately never released early** — it expires naturally via its configured TTL (`ScheduledJobOptions.LockExpiry` ?? `SchedulingOptions.DefaultLockExpiry`, default 5 minutes). A job-name-only key that releases immediately after execution reopens a real duplicate-execution window: a second replica evaluating the *same* due occurrence slightly later (bounded only by inter-replica clock/loop skew, never tightly guaranteed) would find the lock already released and re-acquire it, firing the same occurrence twice. Per-occurrence-keyed claim-and-hold is the standard pattern production distributed schedulers use (Quartz clustered `JobStore`, Hangfire, db-scheduler) for exactly this reason — do not "simplify" this back to acquire/release. The acquired `FencingToken` is exposed on `ScheduledJobExecutionContext` for propagation only, never re-checked internally (this domain owns no protected resource of its own) |
| Command dispatch | MediatR `ISender` via `ScheduledCommandJob<TCommand>` | Closed generic, zero reflection, `sealed` (no Temporal-style attribute-collision problem forcing a consumer subclass, unlike `17.Workflows`' `CommandActivity<TCommand>`). The command instance comes from a caller-supplied `Func<ScheduledJobExecutionContext, TCommand>` factory at registration time — not a bare `TCommand`, since a fired job has no external caller to supply one |
| Misfire detection | In-process only, no persistent job store | Comparing `IClock.UtcNow` against the hosted loop's own last-known next-fire time at each tick/startup — never a durable "last fired at" record (that would mean adopting Quartz's `JobStore`, which Invariant 2 forbids). `OverlapPolicy` is a separate, in-process-only guard against a job whose previous run is still executing at the next tick, independent of and additional to the per-occurrence lock's cross-replica exclusivity. **Exact semantics locked and tested** (see Job Execution Model point 5 below) |
| Clock | `01.Core`'s `IClock` | Never `DateTime.UtcNow` |
| Telemetry | `ActivitySource("SharedKernel.Scheduling")` + companion `Meter("SharedKernel.Scheduling")` | Covers every fire / skip / misfire / overlap / lock-acquisition-failed event; tag keys live in a local `SchedulingTagKeys` constants class, never a raw string literal at a call site |
| Options validation | `OptionsBuilder<SchedulingOptions>.BindConfiguration(SchedulingOptions.SectionName)` + `.ValidateDataAnnotations().ValidateOnStart()` | **Not** `01.Core/SharedKernel.Configuration`'s `AddValidatedOptions<T>(IConfigurationSection)` — that helper needs an eagerly-resolved section, but `AddSharedKernelScheduling(this IServiceCollection, Action<SchedulingOptions>? configure = null)`'s signature (matching every other `AddSharedKernelX` entry point on the platform) takes no `IConfiguration` parameter. `BindConfiguration` resolves `IConfiguration` lazily from the container at options-materialization time instead — same externally observable contract (bound from the `SectionName` const, fails fast at `IHost.StartAsync()`, SK0022-compliant), different mechanism. Reusable pattern for any other single-package domain with an `Action<TOptions>?`-only DI entry point |
| Logging | `[LoggerMessage]`, EventIds `19000`–`19999` | **Shipped.** `LoggingEventIdRanges.Scheduling = 19000` (`01.Core`) confirmed on disk; 16 `[LoggerMessage]` methods in `Diagnostics/SchedulingLogs.cs` |

---

## What Goes Where (within this domain)

| I need to add… | It belongs in… |
|---|---|
| A recurring or deferred job registration | `IScheduledJobRegistry` |
| A job whose work is a MediatR command | `ScheduledCommandJob<TCommand>` — never a hand-rolled `ISender` call in a `BackgroundService` |
| A multi-step, signal-driven, or crash-resumable process | **Not here.** `17.Workflows` |
| A scheduled *message* send | **Not here.** `07.Messaging`'s MassTransit scheduling |
| A per-tenant recurring job | One system-level registration whose body iterates the tenant directory — not N registrations |
| Cleanup/expiry for another domain's table (e.g. `18.Idempotency`) | A job registered here by the consuming service — this domain never reaches into another domain's storage |
| An `IHealthCheck` | **Not here.** The probe primitive lives here; wiring lives in `13.ServiceDefaults` |
| An in-memory test double | `16.Testing/SharedKernel.Testing` (P-467) |

---

## Job Execution Model (locked at Design 2026-08-26, refined at Implementation 2026-09-04)

These decisions are load-bearing for anyone implementing or planning against this domain — including the downstream `13.ServiceDefaults` (P-465/P-466) and `16.Testing` (P-467) planners, who take this section as their contract source of truth.

1. **Registration takes a command factory, not a bare command.** `IScheduledJobRegistry.AddRecurring<TCommand>(jobName, cronExpression, commandFactory, configure)` / `.AddDeferred<TCommand>(jobName, fireAtUtc, commandFactory, configure)`, where `commandFactory` is `Func<ScheduledJobExecutionContext, TCommand>`. A fired job has no external caller to supply a command instance the way an HTTP request or a Temporal activity input does — the factory closes over whatever the job needs at registration time.
2. **`MisfirePolicy`/`OverlapPolicy` are separate concerns enforced at different scopes.** `OverlapPolicy` is a per-process, in-memory guard against a job whose previous run is still executing when the next tick arrives. `MisfirePolicy` is about a tick that was missed entirely (service was down) — detected by comparing `IClock.UtcNow` against the hosted loop's own last-known next-fire time, never against a persisted record (this domain ships no job store, Invariant 2). Neither policy substitutes for the per-occurrence lock's cross-replica exclusivity; all three protections are independent and additive.
3. **The distributed lock is a per-occurrence claim, never a per-tick mutex — see the corrected Technology table row above.** `SchedulingLockKeys.ForOccurrence(jobName, scheduledFireTimeUtc)`, acquired non-blocking (`wait: TimeSpan.Zero`), never disposed on the success path. `ScheduledJobExecutionContext.FencingToken` (nullable) carries the token for propagation to a downstream fencing-token-aware resource — this domain owns no protected resource of its own to re-check it against, so this remains a propagation seam only, not a second enforcement point. This is the one point where the original Design-phase plan ("per tick," "released after the job body completes") was wrong and had to be corrected during implementation — see the Technology table entry for the concrete duplicate-execution scenario it would have allowed.
4. **Quartz dependency is a direct pin, not a transitive ride.** Shipped: `Directory.Packages.props` has its own `Quartz` `PackageVersion` entry (`3.18.1`, matching what `MassTransit.Quartz` 9.1.2 already resolves) — landed by `scheduling-phase-implementer` itself under this phase's explicit dispatch authorization, not devops-lead.
5. **`MisfirePolicy`'s three members have distinct, tested, non-arbitrary semantics — the original Design pass left "each behaves as specified" unspecified as to *how*.** `Skip`: zero catch-up executions, ever; next fire jumps straight to the next occurrence after "now". `FireOnce`: exactly one catch-up execution regardless of how many occurrences were missed, then also jumps to the next occurrence after "now" — collapses an arbitrarily long downtime into a single run. `RunImmediatelyThenReschedule`: replays **every** missed occurrence, one per subsequent tick (bounded by `TickInterval`, never a tight inner loop), by computing each next-fire from the *previous missed occurrence's own timestamp* rather than from "now" — this is what preserves the original cron cadence's alignment grid instead of drifting. Pinned by `.Tests/Policies/MisfirePolicyTests.cs` (a 12-occurrence downtime window asserts exactly 0/1/12 invocations for Skip/FireOnce/RunImmediatelyThenReschedule respectively).

## Testing Notes (Implementation, 2026-09-04)

**`BackgroundService.StartAsync` is fire-and-forget — verified by decompiling `Microsoft.Extensions.Hosting.Abstractions` 10.0.0 (`ilspycmd`), not assumed.** Its real implementation is:

```csharp
public virtual Task StartAsync(CancellationToken cancellationToken)
{
    _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    _executeTask = Task.Run(() => ExecuteAsync(_stoppingCts.Token), _stoppingCts.Token);
    return Task.CompletedTask;
}
```

It schedules `ExecuteAsync` via `Task.Run` and returns `Task.CompletedTask` **immediately** — it does not wait for even `ExecuteAsync`'s synchronous-looking prefix (computing each job's initial `NextFireTimeUtc` from `IClock.UtcNow`, setting `IsRunning = true`) to have actually run. A test harness that `await hostedService.StartAsync()` and then immediately advances a `FakeClock` or asserts state races that prefix: if the clock moves before the prefix reads `IClock.UtcNow`, the very first `NextFireTimeUtc` is computed from the wrong instant and the job silently never fires on the occurrence the test expects. This cost significant debugging time in this domain's own `.Tests` project (9 tests failed identically, all traced to this one root cause) before the fix: `SchedulingTestHarness.StartAsync` now polls for `SchedulingHostedService.IsRunning` (set at the very end of that prefix) before returning control to the caller — never assume a `BackgroundService`'s "synchronous" startup code has run just because its `StartAsync` `Task` completed. **This generalizes to any future `BackgroundService`-based hosted-loop testing anywhere in this repo**, not just this domain.

## Open Items

None outstanding for `SharedKernel.Scheduling`/`SharedKernel.Scheduling.Tests`/`consumer-verify` themselves — P-464 is implemented, tested (34/35 green; the 35th, the real multi-replica proof, is written correctly but not executable in a Docker-less sandbox), packed clean, and consumer-verified against a real `IHost`. The one remaining item is **outside this domain's own jurisdiction**: `Platform.SharedKernel.slnx` registration for the three new projects (`SharedKernel.Scheduling`, `SharedKernel.Scheduling.Tests`, `consumer-verify`) was deliberately left to the dispatcher under a shared-file protocol for this session — see `state-map.md`'s Scaffold phase (S-04, `⚑`).

---

## Changelog

- [2026-08-26] Domain founded ahead of WO-073 dispatch (P-464); Design pass populated all six phase sections, resolved the Quartz-pin question (D-06)
- [2026-09-04] `scheduling-phase-implementer` shipped P-464 end to end (34/35 tests green, Docker-blocked multi-replica test written and correct); corrected the lock composition from per-tick acquire/release to per-occurrence claim-and-hold (a real duplicate-execution bug found while designing the multi-replica test); narrowed the `02.Caching` reference to `.Abstractions` only and dropped the unused `04.Contracts` reference; documented the `BindConfiguration`-over-`AddValidatedOptions` options pattern and the `BackgroundService.StartAsync`/`Task.Run` test-harness gotcha; pinned exact `MisfirePolicy` semantics
