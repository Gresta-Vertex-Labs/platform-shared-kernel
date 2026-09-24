# SharedKernel.Scheduling

Lightweight cron/recurring and one-shot deferred background job dispatch for Platform.SharedKernel
microservices, with optional cross-replica single-execution guarantees.

## When to use this package — and when not to

| Use `SharedKernel.Scheduling` when… | Use `SharedKernel.Workflows.Temporal` (`17.Workflows`) when… |
|---|---|
| Firing a **single unit of work** on a time-based trigger | The process has **multiple steps** |
| *"Did it fire, exactly once, across replicas"* is the only durability question | The process uses signals/queries, or must survive partial completion across a deploy or a crash mid-step |
| Cron or one-shot deferred | Durable, replay-safe, deterministic execution |

Composition is legitimate and expected: a recurring trigger that *starts* a multi-step Temporal
workflow uses both — this package fires the trigger, `17.Workflows` owns everything after that.

## Installation

```
dotnet add package SharedKernel.Scheduling
```

## Quick start

```csharp
ISchedulingBuilder scheduling = services.AddSharedKernelScheduling(options =>
{
    options.TickInterval = TimeSpan.FromSeconds(1);
});

scheduling.AddRecurring<RunNightlyReconciliationCommand>(
    jobName: "nightly-reconciliation",
    cronExpression: "0 0 2 * * ?", // every day at 02:00 UTC
    commandFactory: ctx => new RunNightlyReconciliationCommand(ctx.ScheduledFireTimeUtc),
    configure: options =>
    {
        options.MisfirePolicy = MisfirePolicy.RunImmediatelyThenReschedule;
        options.OverlapPolicy = OverlapPolicy.Skip;
    });

scheduling.AddDeferred<SendWelcomeEmailCommand>(
    jobName: $"welcome-email:{userId}",
    fireAtUtc: DateTimeOffset.UtcNow.AddMinutes(15),
    commandFactory: ctx => new SendWelcomeEmailCommand(userId),
    configure: options =>
    {
        options.MisfirePolicy = MisfirePolicy.FireOnce;
        options.OverlapPolicy = OverlapPolicy.Skip;
    });
```

`MisfirePolicy` and `OverlapPolicy` are **mandatory** — `AddRecurring`/`AddDeferred` throws
`ArgumentException` immediately if either is left unset. Guessing on a team's behalf here is how
duplicate reconciliation runs happen.

## The `ScheduledCommandJob<TCommand>` bridge

Every registered job's unit of work is a MediatR command. `IScheduledJobRegistry.AddRecurring`/
`.AddDeferred` take a `Func<ScheduledJobExecutionContext, TCommand>` factory — not a bare command
instance — because a fired job has no external caller able to supply one the way an HTTP request or a
Temporal activity input does. Internally, `ScheduledCommandJob<TCommand>` (a closed generic, zero
reflection — the scheduling-side counterpart to `17.Workflows`' `CommandActivity<TCommand>`) resolves
`ISender` from a fresh DI scope created per execution and dispatches through the full MediatR pipeline.

What that means for the `SharedKernel.Application` pipeline:

- **The command is an outermost command.** A fresh DI scope means a fresh `ICommandScope`, so
  `TransactionBehavior` commits the job's unit of work when the command succeeds, and
  `ICommandScope.OnCompleted` callbacks run before the fire is reported. Commands the handler sends
  from inside itself share that one commit.
- **Failures come back as a `Result`.** Validation (`ErrorType.Validation`) and authorization
  (`ErrorType.Unauthorized`/`Forbidden`) failures are logged as a failed fire, never thrown.
- **Authorization needs a system identity.** The authorization behavior (`.WithAuthorization()`) reads `IRequestContext`, and a
  scheduled job has no caller. If the service opts into authorization, register
  `SharedKernel.Application.Context.SystemRequestContext` — naming the scheduler and listing exactly the
  permissions its jobs need — or every guarded command fails closed with `Error.Unauthorized`.

## Cross-replica single execution — and the single-replica caveat

Register an `IDistributedLockService` (e.g. `02.Caching.Redis.DistributedLocking`'s Redis-backed
implementation) **before** calling `AddSharedKernelScheduling` to get exactly-once-per-tick execution
across every replica of your service:

```csharp
services.AddRedisConnection(configuration)   // SharedKernel:Caching:Redis — the shared Redis connection
    .AddRedisDistributedLocking();

services.AddSharedKernelScheduling(...);
```

**If you do not register one, this package still starts — but every registered job will fire once PER
REPLICA, per tick, the moment you scale beyond a single instance.** This is a supported mode for
single-replica/dev use, and it is never silent: omitting the lock logs a startup `Warning` naming this
exact caveat. Do not scale a service using this package beyond one replica without registering a
distributed lock first.

## Readiness probing

`ISchedulerServiceProbe`/`SchedulerServiceHealth` report whether the hosted loop is running and how
many jobs are registered — zero I/O, in-process state only. This package ships the probe primitive and
no `IHealthCheck`; wiring it into `AddHealthChecks()` is `13.ServiceDefaults`'s
`AddSchedulerReadinessCheck`.

## Why `ScheduledJobOptions.TenantScope` is nullable

Every other tenant-aware capability domain on this platform makes a tenant-scope parameter mandatory.
This package does not, because a scheduled job is registered **once, at startup, as a system-level
actor** — not as a per-request or per-tenant operation. A genuinely per-tenant recurring job ("send
each active tenant's weekly digest") is one system-level registration whose command handler iterates
its own tenant directory; the scheduler itself never fans out N tenant-scoped executions on the job's
behalf. `TenantScope` exists only as an optional, purely informational label for logging/telemetry
correlation — it carries no isolation enforcement.
