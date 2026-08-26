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
19.Scheduling → may reference 01.Core, 02.Caching (.Redis.DistributedLocking),
                04.Contracts, 05.Application
```

**Inbound grant — narrow and separately named.** `13.ServiceDefaults` may take a `ProjectReference` to `SharedKernel.Scheduling` **solely** to resolve `ISchedulerServiceProbe`/`SchedulerServiceHealth` for `AddSchedulerReadinessCheck` (P-466/WO-073).

No other type in this package — `IScheduledJobRegistry`, `ScheduledCommandJob<TCommand>`, `MisfirePolicy`, `OverlapPolicy`, the options types — may be reached through that grant.

This grant is **independent of, and never a widening of**, the `17.Workflows` grant (WO-047). Root `CLAUDE.md` is emphatic that a domain above `13` wanting the same pattern requires its own named grant, never reasoning by analogy. That rule applied to this domain and was honoured; it applies equally to the next one.

---

## Domain Invariants

**1 — Never hand-roll cron.** Cron-expression parsing and next-fire-time computation go through Quartz's standalone `CronExpression` class. A hand-written parser is a defect: cron's edge cases (DST transitions, `L`/`W`/`#` specifiers, day-of-week vs day-of-month interaction) are exactly where naive implementations break, silently and at 02:00.

**2 — Quartz's scheduler machinery is deliberately not adopted.** `CronExpression` only. Quartz's `IScheduler`, `ITrigger`, `IJobDetail`, and clustered `JobStore` would stand up a second, competing persistence story alongside `06.Persistence`. No raw Quartz type may ever be exposed to application code — the same no-raw-client rule `10.Intelligence` applies to `QdrantClient` and `17.Workflows` applies to `ITemporalClient`.

**3 — Single execution across replicas is opt-in but loudly defaulted.** Cross-replica single execution comes from acquiring an `IFencedLock` (`02.Caching.Redis.DistributedLocking`, P-434/WO-065) at each tick. Omitting it is allowed for single-replica and dev use, but **must log a startup `Warning`** naming the single-replica-only caveat — mirroring `11.Communication.Internal`'s static-resolver precedent, which warns for exactly the same class of "fine locally, wrong in production" configuration.

**4 — Misfire and overlap policy are mandatory, never defaulted silently.** `MisfirePolicy` (`FireOnce` / `Skip` / `RunImmediatelyThenReschedule`) and `OverlapPolicy` (`Skip` / `Queue` / `Allow`) are non-defaulted parameters at registration. A team must state what happens when a scheduled run is missed because the service was down, and what happens when a run is still executing at the next tick. Guessing on their behalf is how duplicate reconciliation runs happen.

**5 — `TenantScope` is nullable here — a deliberate, documented deviation.** Every other tenant-aware domain (`09.Search`, `10.Intelligence`, `17.Workflows`, `18.Idempotency`) makes tenant scope a mandatory, non-defaulted parameter. This domain does not, because a scheduled job is registered **once, at startup, as a system-level actor**. A genuinely per-tenant recurring job ("send each active tenant's weekly digest") iterates its own tenant directory inside the job body; the scheduler does not spawn N tenant-scoped executions on its behalf. The XML docs must state this rationale — otherwise it reads as an oversight and someone will "fix" it.

**6 — MediatR is the command bridge, with zero reflection.** `ScheduledCommandJob<TCommand>` is the scheduling-side counterpart to `17.Workflows`' `CommandActivity<TCommand>` — a closed generic per command dispatching via `ISender`. Never `Type.GetMethod` + `MakeGenericMethod` + `Invoke`; that shape is forbidden platform-wide.

**7 — The probe is zero-I/O.** `ISchedulerServiceProbe` reports whether the hosted loop is running and how many jobs are registered, from in-process state only. This domain ships the probe **primitive** and no `IHealthCheck` — the wiring is `13.ServiceDefaults`' concern, matching the `06`/`07`/`08`/`09`/`10`/`17` split exactly.

---

## Technology

| Concern | Choice | Notes |
|---|---|---|
| Cron parsing | Quartz `CronExpression` (standalone class only) | **Resolved (Design, 2026-08-26): direct pin required.** Verified on disk that `Quartz/3.18.1` is what `MassTransit.Quartz` 9.1.2 currently resolves transitively (`07.Messaging/SharedKernel.Messaging.MassTransit/obj/project.assets.json`). Relying on that transitive edge is fragile — `07.Messaging` may bump/drop `MassTransit.Quartz` independently — so root `Directory.Packages.props` needs its own `<PackageVersion Include="Quartz" Version="3.18.1" />`, and `SharedKernel.Scheduling.csproj` takes an unversioned `<PackageReference Include="Quartz" />` per this repo's CPM convention. The `.props` edit itself is outside this domain's jurisdiction — tracked as a Scaffold-phase cross-domain dependency in `state-map.md` |
| Scheduler loop | An `IHostedService` (`SchedulingHostedService : BackgroundService`) owned by this package | Not Quartz's `IScheduler`. Computes next-fire time via `CronExpression.GetTimeAfter`/`GetNextValidTimeAfter` |
| Cross-replica lock | `02.Caching.Redis.DistributedLocking`'s `IFencedLock` | Optional; absence warns at startup. Acquired per job per tick, lock key derived from job name. The acquired `FencingToken` is exposed on `ScheduledJobExecutionContext` for a job body to propagate to a downstream fenced resource — this domain has no protected resource of its own to re-check the token against, so there is no internal re-check, only propagation |
| Command dispatch | MediatR `ISender` via `ScheduledCommandJob<TCommand>` | Closed generic, zero reflection. The command instance comes from a caller-supplied `Func<ScheduledJobExecutionContext, TCommand>` factory at registration time — not a bare `TCommand`, since a fired job has no external caller to supply one, unlike `17.Workflows`' `CommandActivity<TCommand>` which receives its command as activity input |
| Misfire detection | In-process only, no persistent job store | Comparing `IClock.UtcNow` against the hosted loop's own last-known next-fire time at each tick/startup — never a durable "last fired at" record (that would mean adopting Quartz's `JobStore`, which Invariant 2 forbids). `OverlapPolicy` is a separate, in-process-only guard against a job whose previous run is still executing at the next tick, independent of and additional to `IFencedLock` cross-replica exclusivity |
| Clock | `01.Core`'s `IClock` | Never `DateTime.UtcNow` |
| Telemetry | `ActivitySource("SharedKernel.Scheduling")` + companion `Meter("SharedKernel.Scheduling")` | Covers every fire / skip / misfire / overlap / lock-acquisition-failed event; tag keys live in a local `SchedulingTagKeys` constants class, never a raw string literal at a call site |
| Options validation | `01.Core/SharedKernel.Configuration`'s `AddValidatedOptions` | Fail fast at `IHost.StartAsync()`; `SchedulingOptions.SectionName` is a `public const string`, never a bare `GetSection(...)` literal (SK0022) |
| Logging | `[LoggerMessage]`, EventIds `19000`–`19999` | **Design-locked, not yet implemented.** `core-arch-planner` locked `Scheduling = 19000` in `01.Core`'s `LoggingEventIdRanges` (phase key `SK.01.LoggingRangesNewDomains`, design `●`). Do not author a `[LoggerMessage]` method until `01.Core` ships that entry — blocks Core tasks C-07/C-09 (see `state-map.md`) |

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

## Job Execution Model (locked at Design, 2026-08-26)

These four decisions came out of P-464's Design pass and are load-bearing for anyone implementing or planning against this domain — including the downstream `13.ServiceDefaults` (P-465/P-466) and `16.Testing` (P-467) planners, who take this section as their contract source of truth.

1. **Registration takes a command factory, not a bare command.** `IScheduledJobRegistry.AddRecurring<TCommand>(jobName, cronExpression, commandFactory, configure)` / `.AddDeferred<TCommand>(jobName, fireAtUtc, commandFactory, configure)`, where `commandFactory` is `Func<ScheduledJobExecutionContext, TCommand>`. A fired job has no external caller to supply a command instance the way an HTTP request or a Temporal activity input does — the factory closes over whatever the job needs at registration time.
2. **`MisfirePolicy`/`OverlapPolicy` are separate concerns enforced at different scopes.** `OverlapPolicy` is a per-process, in-memory guard against a job whose previous run is still executing when the next tick arrives. `MisfirePolicy` is about a tick that was missed entirely (service was down) — detected by comparing `IClock.UtcNow` against the hosted loop's own last-known next-fire time, never against a persisted record (this domain ships no job store, Invariant 2). Neither policy substitutes for `IFencedLock` cross-replica exclusivity; all three protections are independent and additive.
3. **The fencing token is exposed for propagation, never re-checked internally.** `ScheduledJobExecutionContext.FencingToken` (nullable) carries the token from the tick's `IFencedLock` acquisition so a job body that itself calls into a fencing-token-aware downstream resource can pass it along. This domain owns no protected resource of its own — there is nothing here to check the token *against* — so this is a propagation seam only, not a second enforcement point.
4. **Quartz dependency is a direct pin, not a transitive ride.** See the Technology table above — `Directory.Packages.props` needs its own `Quartz` `PackageVersion` entry (version `3.18.1`, matching what `MassTransit.Quartz` 9.1.2 already resolves), added outside this domain's jurisdiction.

## Open Items

- `01.Core`'s `LoggingEventIdRanges` has a `19 = 19000` base **design-locked** (phase key `SK.01.LoggingRangesNewDomains`, design `●`) but not yet implemented — still required before the first `[LoggerMessage]` method ships in this domain.
- The root `Directory.Packages.props` Quartz direct pin (Job Execution Model, point 4 above) has not landed on disk yet — blocks Scaffold task S-03.
- Root Phase Backlog P-464 (WO-073) is `○` Pending. Design tasks D-01–D-08 are now defined in `state-map.md` (D-06 resolved); Scaffold/Core/Tests/Docs/Published tasks are defined but unimplemented.
