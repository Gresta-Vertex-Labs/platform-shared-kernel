# 19.Scheduling — Domain Brain

> Lightweight, time-triggered job dispatch — **cron/recurring** and **one-shot deferred** — with cross-replica single execution, in one package, `SharedKernel.Scheduling`. Each fire sends one kernel command through `ISender`. It deliberately does **not** own multi-step, signal-driven or crash-resumable processes (that is `17.Workflows`), scheduled message sends (`07.Messaging`'s `IMessageScheduler`), a persistent job store, or any other domain's storage cleanup (a consuming service registers such a job here itself).

| Use `19.Scheduling` when… | Use `17.Workflows` when… |
|---|---|
| Firing a **single unit of work** on a time trigger | The process has **multiple steps** |
| "Did it fire exactly once across replicas" is the only durability question | It uses signals/queries or must survive a crash mid-step |
| Cron or one-shot deferred | Durable, replay-safe, deterministic execution |

Composition is expected: a recurring trigger here may *start* a Temporal workflow; `17.Workflows` owns everything after that.

## Packages

| Package | Tier | Purpose |
|---|---|---|
| `SharedKernel.Scheduling` | Adapter | Job registry, hosted scheduling loop, `ScheduledCommandJob<TCommand>` bridge to `ISender`, misfire/overlap policies, per-occurrence distributed lease, `scheduler` readiness probe, telemetry. |

Also in the folder (not packages): `SharedKernel.Scheduling/SharedKernel.Scheduling.Tests` and `consumer-verify/` (a package-reference consumer harness).

References: `SharedKernel.Primitives`, `.Execution`, `.Configuration` (Foundation), `SharedKernel.Caching.Abstractions` (`IDistributedLockService`) and `SharedKernel.Application` (kernel `ISender`, `ICommand`) (Abstractions), `Quartz` (for `CronExpression` only, pinned directly in `Directory.Packages.props`), `Microsoft.Extensions.*` hosting/options/logging.

## Public Entry Points

- **Registration:** `services.AddSharedKernelScheduling(Action<SchedulingOptions>? configure = null)` → `ISchedulingBuilder` (an `IScheduledJobRegistry` with `Services`). Registers the `IScheduledJobRegistry` singleton, the `SchedulingHostedService` (`IHostedService`) and the `scheduler` readiness probe.
- **Configuration:** `SchedulingOptions` bound from `SharedKernel:Scheduling` (`SchedulingOptions.SectionName`) — `TickInterval` (default 1 s, range 100 ms–10 min), `DefaultLockExpiry` (default 5 min, range 1 s–1 day). Data Annotations, validated on start; the `configure` delegate runs as a post-configure.
- **Jobs:** `IScheduledJobRegistry.AddRecurring<TCommand>(jobName, cronExpression, commandFactory, configure)` and `.AddDeferred<TCommand>(jobName, fireAtUtc, commandFactory, configure)`, `TCommand : class, ICommand`; `commandFactory` is `Func<ScheduledJobExecutionContext, TCommand>`.
- **`ScheduledJobOptions`:** `MisfirePolicy` (`FireOnce`/`Skip`/`RunImmediatelyThenReschedule`), `OverlapPolicy` (`Skip`/`Queue`/`Allow`) — both mandatory; `TenantScope` (default `TenantScope.Global`); `LockExpiry` (overrides `DefaultLockExpiry`).
- **`ScheduledJobExecutionContext`:** `JobName`, `ScheduledFireTimeUtc`, `ActualFireTimeUtc`, `TenantScope`, `FencingToken` (`long?`, `null` in single-replica mode).
- **`ScheduledCommandJob<TCommand>`:** the sealed closed-generic bridge that sends the command through `ISender` in a fresh DI scope.
- **Readiness:** `SchedulerReadiness.ProbeName` = `"scheduler"`; mapped by the host's `services.AddHealthChecks().AddSharedKernelReadiness()`.
- **Telemetry:** `ActivitySource` and `Meter` named `SharedKernel.Scheduling` (`scheduling.job.*` and `scheduling.lock.*` counters); the host subscribes with ServiceDefaults' `WithSchedulingTelemetry()`.
- **Cross-replica lock:** register an `IDistributedLockService` (e.g. `AddRedisConnection(configuration).AddRedisDistributedLocking()`) in the host.

## Rules & Invariants

1. **Never hand-roll cron.** Parse and compute next-fire times with Quartz's standalone `CronExpression` only, always with `TimeZone = TimeZoneInfo.Utc` (the class defaults to local time). All scheduling is UTC-anchored.
2. **No Quartz scheduler machinery.** No `IScheduler`, `ITrigger`, `IJobDetail` or clustered `JobStore`, and no Quartz type in the public API.
3. **The cross-replica claim is a per-occurrence lease, never an acquire/release lock.** `IDistributedLockService.TryAcquireLeaseAsync(SchedulingLockKeys.ForOccurrence(jobName, scheduledFireTimeUtc), lockExpiry, ct)`: one attempt, never released or extended, expires on its own. A job-name-only key released after the run lets a slightly later replica fire the same occurrence twice.
4. **An outage is not contention.** `null` lease → another replica owns the occurrence (skip, `lock-not-acquired`). `DistributedLockUnavailableException` → no replica can prove ownership, so none runs it: log `LockStoreUnavailable` (Error), count `scheduling.lock.store_unavailable`, return.
5. **No lock service is allowed but never silent.** Without an `IDistributedLockService` the loop logs the `SingleReplicaWarning` at start; every job then fires once per replica.
6. **`MisfirePolicy` and `OverlapPolicy` are mandatory.** `AddRecurring`/`AddDeferred` throw `ArgumentException` when either is unset. Never add a default.
7. **Misfire semantics are fixed and tested:** `Skip` → zero catch-up runs; `FireOnce` → exactly one catch-up run, then the next occurrence after now; `RunImmediatelyThenReschedule` → replays every missed occurrence, one per tick, computing each next fire from the previous missed occurrence (keeps the cron grid).
8. **Misfire detection is in-process.** Compare `IClock.UtcNow` against the loop's own last-known next-fire time; never persist a "last fired" record. Overlap is a separate in-process guard. Lease, misfire and overlap protections are independent and additive.
9. **Every execution runs as a system caller in its own scope:** `RequestContextScope.Begin(new SystemRequestContext([], identity: jobName, tenantId: options.TenantScope.Tenant, correlationId: CorrelationIds.New()))`, then a fresh DI scope. The context carries **no permissions** — a job whose command has `[RequirePermission]` must open its own scope with exactly the permissions it needs, or the command fails with `Error.Forbidden`.
10. **The command is an outermost command.** Fresh scope → fresh `ICommandScope`: `TransactionBehavior` commits once, `OnCompleted` callbacks run before the fire is reported. Validation/authorization failures come back as a failed `Result`, logged as a failed fire, never thrown.
11. **Zero reflection in dispatch.** `ScheduledCommandJob<TCommand>` is a closed generic calling `ISender`; never `MakeGenericMethod` + `Invoke`, never MediatR directly.
12. **`TenantScope` is optional here on purpose** (default `Global`): a job is registered once at startup as a system actor. A per-tenant recurring job is one registration whose handler iterates the tenant directory; the scheduler never fans out per tenant. A set tenant becomes the job's `IRequestContext.TenantId`. Keep the XML-doc rationale so nobody "fixes" it.
13. **Time comes from `IClock`**, never `DateTime.UtcNow`/`DateTimeOffset.UtcNow`.
14. **The readiness probe is zero-I/O**: in-process state only (`IsRunning`, `RegisteredJobCount`, `LastTickUtc` in `ReadinessReport.Data`). This package ships no `IHealthCheck`.
15. **Reference `SharedKernel.Caching.Abstractions` only**, never `Caching.Redis.DistributedLocking` (an undeclared Adapter→Adapter edge). Only the `.Tests` project references the Redis package, for the real multi-replica proof.
16. `FencingToken` is a propagation seam only; this package owns no protected resource and never re-checks it.

## Decisions

| Decision | Why |
|---|---|
| One package, no `.Abstractions` split | One provider exists; split only when a second backend is ratified. (A different rationale from `17.Workflows`, where the programming model is the abstraction.) |
| Quartz `CronExpression` only, pinned directly | Cron edge cases (DST, `L`/`W`/`#`) are where hand-written parsers break; a direct pin means `07.Messaging`'s MassTransit.Quartz can never change it silently. |
| Declined Quartz `IScheduler`/`JobStore`, Hangfire | A second persistence story next to `06.Persistence`; the per-occurrence lease gives the cross-replica guarantee without one. |
| Per-occurrence claim-and-expire lease | The shape Quartz clustering, Hangfire and db-scheduler use; acquire/release reopens a duplicate window bounded only by replica skew. |
| Registration takes a command factory | A fired job has no external caller to supply a command instance. |
| Options via `AddOptions().BindConfiguration(SectionName).ValidateDataAnnotations().ValidateOnStart()` | `AddSharedKernelScheduling` takes no `IConfiguration`; `BindConfiguration` resolves it lazily with the same fail-fast contract. |
| Declined mandatory `TenantScope` / per-tenant fan-out | A scheduled job is a startup-registered system actor; fan-out belongs in the handler. |
| Declined multi-step job chains | Crash-resumable multi-step execution is `17.Workflows`. |

## Logging

EventId block **19000–19999** (`LoggingEventIdRanges.Scheduling`). Single package, so no sub-blocks: `Diagnostics/SchedulingLogs.cs` uses `Scheduling + 0` … `+ 16` (start/stop, `SingleReplicaWarning` (+2), fire, success, failure, unhandled exception, overlap skip/queue, misfire, misfire discard, claim not acquired / acquired, loop faulted (+13, Critical), shutdown cancellation, drain timeout, `LockStoreUnavailable` (+16)). Next free: `+17`. Telemetry tag keys live in `SchedulingTagKeys`.

## Cross-Domain Couplings

- **01.Core:** `IClock`, `LoggingEventIdRanges`, `IReadinessProbe`/`AddReadinessProbe` (Primitives); `SystemRequestContext`, `RequestContextScope`, `CorrelationIds`, `TenantScope` (Execution).
- **02.Caching:** consumes `IDistributedLockService.TryAcquireLeaseAsync`, `DistributedLease.FencingToken`, `DistributedLockUnavailableException` from Abstractions; the host supplies the Redis implementation.
- **05.Application:** `ISender`, `ICommand`; the host must register a mediator adapter (`UseMediatR()`); `[RequirePermission]` commands need their own permissioned scope.
- **13.ServiceDefaults:** `AddSharedKernelReadiness()` maps the `scheduler` probe; `WithSchedulingTelemetry()` subscribes to the source/meter.
- **16.Testing:** `SharedKernel.Scheduling.Testing` (`InMemoryScheduledJobRegistry`) takes this domain's job execution model as its contract.
- **17.Workflows:** composition only (a trigger that starts a workflow); no reference either way.

## Testing

- `SharedKernel.Scheduling.Tests` — **Integration lane** (`Platform.SharedKernel.Integration.slnf`; Testcontainers Redis for `MultiReplica/MultiReplicaSingleExecutionTests` and the lease tests). Uses `SharedKernel.Testing` (`FakeClock`), `SharedKernel.Caching.Testing`, `SharedKernel.Testing.Internal`, and MediatR via `Application.Mediator.MediatR`. Never mock `IDistributedLockService` for the multi-replica assertion.
- Pinned behaviours: `Policies/MisfirePolicyTests` (a 12-occurrence downtime asserts 0/1/12 runs), `OverlapPolicyTests`, `Locking/OccurrenceLeaseTests`, `Cron/CronExpressionCorrectnessTests` (DST correctness against a real DST zone).
- `TestSupport/SchedulingTestHarness.StartAsync` polls `SchedulingHostedService.IsRunning` before returning: `BackgroundService.StartAsync` returns before `ExecuteAsync`'s prefix runs, so advancing a `FakeClock` immediately after it races the initial next-fire computation. Apply the same wait to any hosted-loop test.
- `consumer-verify/` is in the Unit lane.
- Consumers use `SharedKernel.Scheduling.Testing`'s `InMemoryScheduledJobRegistry` and fire ticks with `TriggerAsync`.

## Known Limitations

- No persistent job store: jobs are registered at composition time and the loop computes their first fire time when it starts; next-fire and misfire state live in process memory, so a deferred job survives a restart only if the composition root registers it again.
- Without an `IDistributedLockService` every replica fires every occurrence.
- Cron is evaluated in UTC only; there is no per-job time zone.
- Size `LockExpiry` above the occurrence's worst-case claim-to-start skew between replicas; a lease that expires before a lagging replica evaluates the same occurrence no longer excludes it.
