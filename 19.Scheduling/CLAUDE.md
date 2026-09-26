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

**No `.Abstractions` split, and none may be added without a ratified second backend.** Exactly one provider ships, matching the documented single-provider convention already used by `SharedKernel.Cryptography` and `SharedKernel.Compression`. If a second scheduling backend is ever genuinely proposed, the split is reconsidered then — not pre-emptively.

Note this is a *different* justification from `17.Workflows`' single-package decision. That domain argues the programming model **is** the abstraction (determinism/replay/`Workflow.Patched` versioning are not swappable). Here the reasoning is simply that one provider exists. Do not conflate the two rationales.

---

## Tier and references

`SharedKernel.Scheduling` is **Adapter** tier. It references Foundation packages (`SharedKernel.Primitives`, `.Execution`, `.Configuration`), two Abstractions packages — `SharedKernel.Caching.Abstractions` (`IDistributedLockService`) and `SharedKernel.Application` (the kernel `ISender`, no MediatR) — plus `Quartz` (for `CronExpression` only) and `Microsoft.Extensions.*` abstractions. The build enforces the tiers (SKTIER001–006).

**`SharedKernel.Caching.Abstractions` only — never `.Redis.DistributedLocking`.** `IDistributedLockService`, `DistributedLease` and `DistributedLockUnavailableException` live in the zero-infrastructure abstractions package. The consuming service composes `AddRedisDistributedLocking()` at its own composition root, alongside `AddSharedKernelScheduling()`. (The `.Tests` project is the one exception, by design: it references `.Redis.DistributedLocking` directly for the real multi-replica proof — never mock `IDistributedLockService` for that assertion.)

No package references this one to reach its readiness probe: the probe is an `IReadinessProbe` the host maps with `AddSharedKernelReadiness()`, so there is no ServiceDefaults integration package and no layering grant.

---

## Domain Invariants

**1 — Never hand-roll cron.** Cron-expression parsing and next-fire-time computation go through Quartz's standalone `CronExpression` class. A hand-written parser is a defect: cron's edge cases (DST transitions, `L`/`W`/`#` specifiers, day-of-week vs day-of-month interaction) are exactly where naive implementations break, silently and at 02:00.

**2 — Quartz's scheduler machinery is deliberately not adopted.** `CronExpression` only. Quartz's `IScheduler`, `ITrigger`, `IJobDetail`, and clustered `JobStore` would stand up a second, competing persistence story alongside `06.Persistence`. No raw Quartz type may ever be exposed to application code — the same no-raw-client rule `10.Intelligence` applies to `QdrantClient` and `17.Workflows` applies to `ITemporalClient`.

**3 — Single execution across replicas is opt-in but loudly defaulted.** Cross-replica single execution comes from an `IDistributedLockService` lease (`02.Caching.Abstractions`, real implementation from `.Redis.DistributedLocking`) taken **per occurrence** — never per tick alone; see the corrected lock-composition note under Technology and Job Execution Model point 3. Omitting a lock provider is allowed for single-replica and dev use, but **must log a startup `Warning`** naming the single-replica-only caveat — mirroring `11.Communication.Internal`'s static-resolver precedent, which warns for exactly the same class of "fine locally, wrong in production" configuration.

**4 — Misfire and overlap policy are mandatory, never defaulted silently.** `MisfirePolicy` (`FireOnce` / `Skip` / `RunImmediatelyThenReschedule`) and `OverlapPolicy` (`Skip` / `Queue` / `Allow`) are non-defaulted parameters at registration. A team must state what happens when a scheduled run is missed because the service was down, and what happens when a run is still executing at the next tick. Guessing on their behalf is how duplicate reconciliation runs happen.

**5 — `TenantScope` is optional here — a deliberate, documented deviation.** Every other tenant-aware domain (`09.Search`, `10.Intelligence`, `17.Workflows`, `18.Idempotency`) makes tenant scope a mandatory, non-defaulted parameter. Here `ScheduledJobOptions.TenantScope` (`SharedKernel.Execution.Tenancy.TenantScope`) defaults to `TenantScope.Global`, because a scheduled job is registered **once, at startup, as a system-level actor**. A genuinely per-tenant recurring job ("send each active tenant's weekly digest") iterates its own tenant directory inside the job body; the scheduler does not spawn N tenant-scoped executions on its behalf. The XML docs must state this rationale — otherwise it reads as an oversight and someone will "fix" it.

**6 — Every execution runs as a system caller in its own request-context scope.** The runner opens `RequestContextScope.Begin(new SystemRequestContext([], identity: jobName, tenantId: options.TenantScope.Tenant, correlationId: CorrelationIds.New()))` before creating the job's DI scope. `IRequestContext` inside the job therefore answers with the job's tenant (persistence tenant filters and idempotency keys use it) and a fresh correlation id, and every outbound REST/gRPC call, message or workflow the job starts carries both. The context holds **no permissions**: a job that sends authorization-gated commands opens its own `RequestContextScope` with a `SystemRequestContext` listing exactly the permissions it needs, or the guarded command fails closed.

**7 — The kernel `ISender` is the command bridge, with zero reflection.** `ScheduledCommandJob<TCommand>` is the scheduling-side counterpart to `17.Workflows`' `CommandActivity<TCommand>` — a closed generic per command dispatching via `SharedKernel.Application`'s `ISender` (the host picks the mediator adapter). Never `Type.GetMethod` + `MakeGenericMethod` + `Invoke`; that shape is forbidden platform-wide. Because every execution gets its own DI scope, the dispatched command is the **outermost** command for `ICommandScope`: `TransactionBehavior` commits once on success, `ICommandScope.OnCompleted` callbacks run before the fire is reported, and validation/authorization failures come back as a failed `Result` (logged as a failed fire), never as an exception.

**8 — Readiness is a zero-I/O `IReadinessProbe`.** `AddSharedKernelScheduling()` registers the internal `SchedulerServiceProbe`, named `scheduler` (`SchedulerReadiness.ProbeName`): healthy while the hosted loop runs, with `IsRunning`, `RegisteredJobCount` and `LastTickUtc` in `ReadinessReport.Data`, read from in-process state only. This domain ships no `IHealthCheck`; the host maps the probe with `services.AddHealthChecks().AddSharedKernelReadiness()`.

---

## Technology

| Concern | Choice | Notes |
|---|---|---|
| Cron parsing | Quartz `CronExpression` (standalone class only) | `Quartz` `3.18.1`, pinned directly in root `Directory.Packages.props` — this package is its only consumer. `ScheduledJobRegistry.ParseCron` always sets `CronExpression.TimeZone = TimeZoneInfo.Utc` explicitly — the class defaults to `TimeZoneInfo.Local`, which would make this domain's scheduling non-deterministic across environments given every other decision here is UTC-anchored (`IClock.UtcNow`, `ScheduledJobExecutionContext` timestamps). A consequence: this package's own runtime behavior is deliberately DST-invariant, by design — DST correctness is proven directly against `CronExpression` with a real DST-observing zone instead (see `.Tests/Cron/CronExpressionCorrectnessTests.cs`) |
| Scheduler loop | An `IHostedService` (`SchedulingHostedService : BackgroundService`) owned by this package | Not Quartz's `IScheduler`. Computes next-fire time via `CronExpression.GetTimeAfter` only (not `GetNextValidTimeAfter` — both are equivalent for this domain's usage; `GetTimeAfter` was chosen as primary) |
| Cross-replica lock | `02.Caching.Abstractions`'s `IDistributedLockService.TryAcquireLeaseAsync` (P-547) | **A per-occurrence lease, never a lock that is released.** The resource is `SchedulingLockKeys.ForOccurrence(jobName, scheduledFireTimeUtc)` — job name **and** scheduled fire time. `TryAcquireLeaseAsync(resource, lockExpiry, ct)` makes one attempt and returns a `DistributedLease` that is never released or extended; it expires after `ScheduledJobOptions.LockExpiry` ?? `SchedulingOptions.DefaultLockExpiry` (default 5 minutes). A job-name-only key released after execution reopens a duplicate-execution window: a second replica evaluating the *same* due occurrence slightly later (bounded only by inter-replica clock/loop skew) would find the key free and fire the occurrence twice. Per-occurrence claim-and-expire is what Quartz's clustered `JobStore`, Hangfire and db-scheduler do — do not "simplify" it to acquire/release. **Three outcomes:** a lease → the occurrence runs; `null` → another replica holds it (`scheduling.lock.acquisition_failed`, activity outcome `"lock-not-acquired"`); `DistributedLockUnavailableException` → the lock store is unreachable, so no replica can prove ownership and none runs it: `Log.LockStoreUnavailable` (`Diagnostics/SchedulingLogs.cs`, `Error`, EventId `Scheduling + 16`), `scheduling.lock.store_unavailable` counter, activity outcome `"lock-store-unavailable"` with error status, and the tick returns without running the job. An outage is never treated as contention. `lease.FencingToken` is exposed on `ScheduledJobExecutionContext.FencingToken` for propagation only, never re-checked internally (this domain owns no protected resource) |
| Command dispatch | The kernel `ISender` (`SharedKernel.Application`) via `ScheduledCommandJob<TCommand>` | Closed generic, zero reflection, `sealed` (no Temporal-style attribute-collision problem forcing a consumer subclass, unlike `17.Workflows`' `CommandActivity<TCommand>`). The command instance comes from a caller-supplied `Func<ScheduledJobExecutionContext, TCommand>` factory at registration time — not a bare `TCommand`, since a fired job has no external caller to supply one |
| Misfire detection | In-process only, no persistent job store | Comparing `IClock.UtcNow` against the hosted loop's own last-known next-fire time at each tick/startup — never a durable "last fired at" record (that would mean adopting Quartz's `JobStore`, which Invariant 2 forbids). `OverlapPolicy` is a separate, in-process-only guard against a job whose previous run is still executing at the next tick, independent of and additional to the per-occurrence lock's cross-replica exclusivity. **Exact semantics locked and tested** (see Job Execution Model point 5 below) |
| Clock | `SharedKernel.Primitives`' `IClock` | Never `DateTime.UtcNow` |
| Telemetry | `ActivitySource("SharedKernel.Scheduling")` + companion `Meter("SharedKernel.Scheduling")` | Covers every fire / skip / misfire / overlap / lock-acquisition-failed / lock-store-unavailable event; tag keys live in a local `SchedulingTagKeys` constants class, never a raw string literal at a call site |
| Options validation | `OptionsBuilder<SchedulingOptions>.BindConfiguration(SchedulingOptions.SectionName)` + `.ValidateDataAnnotations().ValidateOnStart()` | **Not** `01.Core/SharedKernel.Configuration`'s `AddValidatedOptions<T>(IConfigurationSection)` — that helper needs an eagerly-resolved section, but `AddSharedKernelScheduling(this IServiceCollection, Action<SchedulingOptions>? configure = null)`'s signature (matching every other `AddSharedKernelX` entry point on the platform) takes no `IConfiguration` parameter. `BindConfiguration` resolves `IConfiguration` lazily from the container at options-materialization time instead — same externally observable contract (bound from the `SectionName` const, fails fast at `IHost.StartAsync()`, SK0022-compliant), different mechanism. Reusable pattern for any other single-package domain with an `Action<TOptions>?`-only DI entry point |
| Logging | `[LoggerMessage]`, EventIds `19000`–`19999` | **Shipped.** `LoggingEventIdRanges.Scheduling = 19000` (`SharedKernel.Primitives`); 17 `[LoggerMessage]` methods in `Diagnostics/SchedulingLogs.cs` (`Scheduling + 0` .. `+ 16`; `+16` is `LockStoreUnavailable`) |

---

## What Goes Where (within this domain)

| I need to add… | It belongs in… |
|---|---|
| A recurring or deferred job registration | `IScheduledJobRegistry` |
| A job whose work is a command | `ScheduledCommandJob<TCommand>` — never a hand-rolled `ISender` call in a `BackgroundService` |
| A multi-step, signal-driven, or crash-resumable process | **Not here.** `17.Workflows` |
| A scheduled *message* send | **Not here.** `07.Messaging`'s `IMessageScheduler` (transport-native delayed delivery) |
| A per-tenant recurring job | One system-level registration whose body iterates the tenant directory — not N registrations |
| Cleanup/expiry for another domain's table (e.g. `18.Idempotency`) | A job registered here by the consuming service — this domain never reaches into another domain's storage |
| An `IHealthCheck` | **Not here.** The `scheduler` `IReadinessProbe` lives here; the host maps it with `AddSharedKernelReadiness()` |
| An in-memory test double | `16.Testing/SharedKernel.Scheduling.Testing` (`InMemoryScheduledJobRegistry`) |

---

## Job Execution Model (locked at Design 2026-08-26, refined at Implementation 2026-09-04)

These decisions are load-bearing for anyone implementing or planning against this domain — including `16.Testing`'s `SharedKernel.Scheduling.Testing`, which takes this section as its contract source of truth.

1. **Registration takes a command factory, not a bare command.** `IScheduledJobRegistry.AddRecurring<TCommand>(jobName, cronExpression, commandFactory, configure)` / `.AddDeferred<TCommand>(jobName, fireAtUtc, commandFactory, configure)`, where `commandFactory` is `Func<ScheduledJobExecutionContext, TCommand>`. A fired job has no external caller to supply a command instance the way an HTTP request or a Temporal activity input does — the factory closes over whatever the job needs at registration time.
2. **`MisfirePolicy`/`OverlapPolicy` are separate concerns enforced at different scopes.** `OverlapPolicy` is a per-process, in-memory guard against a job whose previous run is still executing when the next tick arrives. `MisfirePolicy` is about a tick that was missed entirely (service was down) — detected by comparing `IClock.UtcNow` against the hosted loop's own last-known next-fire time, never against a persisted record (this domain ships no job store, Invariant 2). Neither policy substitutes for the per-occurrence lock's cross-replica exclusivity; all three protections are independent and additive.
3. **The distributed claim is a per-occurrence lease, never a per-tick mutex — see the Technology table row above.** `TryAcquireLeaseAsync(SchedulingLockKeys.ForOccurrence(jobName, scheduledFireTimeUtc), lockExpiry, ct)`: one attempt, never released, expires on its own. A lock store outage (`DistributedLockUnavailableException`) skips the occurrence on every replica and is logged as an error, distinct from contention. `ScheduledJobExecutionContext.FencingToken` (nullable; `null` only in single-replica mode) carries `lease.FencingToken` for propagation to a downstream fencing-token-aware resource — this domain owns no protected resource of its own, so it is a propagation seam only, not a second enforcement point.
4. **Quartz dependency is a direct pin, not a transitive ride.** `Directory.Packages.props` has its own `Quartz` `PackageVersion` entry (`3.18.1`).
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

- `ScheduledJobOptions.TenantScope`'s XML doc still calls the tenant "only a label" that "enforces no tenant isolation". Since WO-086 the runner makes it the job's `IRequestContext.TenantId`, so tenant-filtered persistence inside the job is scoped to it. Correct the doc comment in a code change.

---

## Changelog

History — the Design pass (WO-073, P-464), the per-occurrence lease correction, the P-547 lease migration and the WO-086 refactor — is in `state-map.md` ("Domain-Brain Changelog").
