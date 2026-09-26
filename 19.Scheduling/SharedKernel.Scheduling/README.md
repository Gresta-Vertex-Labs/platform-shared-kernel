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

```xml
<PackageReference Include="SharedKernel.Scheduling" />
```

Versions come from the consumer's single `SharedKernelVersion`. **Tier: Adapter** — references
`SharedKernel.Primitives`, `.Execution`, `.Configuration`, `SharedKernel.Caching.Abstractions`
(`IDistributedLockService`), `SharedKernel.Application` (the kernel `ISender`, no MediatR) and `Quartz` (for its
`CronExpression` parser only; Quartz's scheduler is never used).

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
    fireAtUtc: clock.UtcNow.AddMinutes(15),                 // IClock, never DateTimeOffset.UtcNow
    commandFactory: ctx => new SendWelcomeEmailCommand(userId),
    configure: options =>
    {
        options.MisfirePolicy = MisfirePolicy.FireOnce;
        options.OverlapPolicy = OverlapPolicy.Skip;
        options.TenantScope = TenantScope.For(tenantId);    // optional; SharedKernel.Execution.Tenancy
    });
```

`MisfirePolicy` and `OverlapPolicy` are **mandatory** — `AddRecurring`/`AddDeferred` throws
`ArgumentException` immediately if either is left unset. Guessing on a team's behalf here is how
duplicate reconciliation runs happen.

## The `ScheduledCommandJob<TCommand>` bridge

Every registered job's unit of work is a command sent through the kernel `ISender`
(`SharedKernel.Application`) — the same application pipeline an HTTP request gets, behind whichever mediator
adapter the host registered (e.g. `SharedKernel.Application.Mediator.MediatR`). `AddRecurring`/`AddDeferred`
take a `Func<ScheduledJobExecutionContext, TCommand>` factory — not a bare command instance — because a fired
job has no external caller able to supply one. Internally, `ScheduledCommandJob<TCommand>` (a closed generic,
zero reflection — the scheduling-side counterpart to `17.Workflows`' `CommandActivity<TCommand>`) resolves
`ISender` from a fresh DI scope created per execution.

What that means for the application pipeline (`SharedKernel.Application.Pipeline`):

- **The command is an outermost command.** A fresh DI scope means a fresh `ICommandScope`, so
  `TransactionBehavior` commits the job's unit of work when the command succeeds, and
  `ICommandScope.OnCompleted` callbacks run before the fire is reported. Commands the handler sends
  from inside itself share that one commit.
- **Failures come back as a `Result`.** Validation (`ErrorType.Validation`) and authorization
  (`ErrorType.Unauthorized`/`Forbidden`) failures are logged as a failed fire, never thrown.

## Who the job runs as

Before each execution the runner opens a `RequestContextScope` carrying
`new SystemRequestContext([], identity: jobName, tenantId: options.TenantScope.Tenant, correlationId: CorrelationIds.New())`
(`SharedKernel.Execution`). Inside the job:

- `IRequestContext` answers with `ActorKind.System`, the job name, the job's tenant (or none for
  `TenantScope.Global`) and a new correlation id — so tenant-filtered persistence and idempotency keys use the
  job's tenant.
- Every outbound REST or gRPC call, message and workflow the job starts carries the same tenant and correlation
  id, so one run can be followed across services.
- The context holds **no permissions**, and authorization is always on: a job whose command carries
  `[RequirePermission]` (`SharedKernel.Application.Authorization`) opens its own
  scope with exactly the permissions it needs, or the command fails closed:

  ```csharp
  using (RequestContextScope.Begin(new SystemRequestContext(["reports.generate"], "nightly-reports", tenantId)))
  {
      return await sender.Send(command, ct);
  }
  ```

  When the service's commands carry `[RequirePermission]`, the host start also demands a registered
  `IRequestContext` (`AddSharedKernelApplication`'s start check names the request types).

## Cross-replica single execution — and the single-replica caveat

Register an `IDistributedLockService` (e.g. `SharedKernel.Caching.Redis.DistributedLocking`'s Redis-backed
implementation) **before** calling `AddSharedKernelScheduling` to get exactly-once-per-occurrence execution
across every replica of your service:

```csharp
services.AddRedisConnection(configuration)   // SharedKernel:Caching:Redis — the shared Redis connection
    .AddRedisDistributedLocking();

services.AddSharedKernelScheduling(...);
```

Each occurrence is claimed with a self-expiring lease keyed by job name **and** scheduled fire time; its fencing
token reaches the job as `ScheduledJobExecutionContext.FencingToken`. When the lock store is unreachable, no
replica runs the occurrence and an error is logged — an outage is never mistaken for another replica's claim.

**If you do not register one, this package still starts — but every registered job will fire once PER
REPLICA, per tick, the moment you scale beyond a single instance.** This is a supported mode for
single-replica/dev use, and it is never silent: omitting the lock logs a startup `Warning` naming this
exact caveat. Do not scale a service using this package beyond one replica without registering a
distributed lock first.

## Readiness

`AddSharedKernelScheduling()` registers one `IReadinessProbe` named `scheduler`
(`SchedulerReadiness.ProbeName`). It is healthy while the scheduling loop runs and reads only in-process state
(zero I/O); `ReadinessReport.Data` carries `IsRunning`, `RegisteredJobCount` and `LastTickUtc`. Map it in the
host:

```csharp
builder.Services.AddHealthChecks().AddSharedKernelReadiness();   // SharedKernel.ServiceDefaults
```

## Why `ScheduledJobOptions.TenantScope` is optional

Every other tenant-aware capability domain on this platform makes a tenant-scope parameter mandatory.
This package does not — `TenantScope` defaults to `TenantScope.Global` — because a scheduled job is registered
**once, at startup, as a system-level actor**, not as a per-request or per-tenant operation. A genuinely
per-tenant recurring job ("send each active tenant's weekly digest") is one system-level registration whose
command handler iterates its own tenant directory; the scheduler itself never fans out N tenant-scoped
executions on the job's behalf. Set a tenant only for the rare job owned by a single tenant: it becomes the job's
`IRequestContext.TenantId`.

## Testing

`SharedKernel.Scheduling.Testing` provides `InMemoryScheduledJobRegistry`: registrations are recorded and a test
fires a tick explicitly with `TriggerAsync`, so misfire and overlap policies are asserted without a clock, a
hosted loop or a lock store.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see
[19.Scheduling/CLAUDE.md](../CLAUDE.md) for the invariants, the job execution model and the misfire semantics.
