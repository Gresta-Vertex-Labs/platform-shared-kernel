---
proven-by:
  - samples/Shop/Inventory/Shop.Inventory.Api/Reconciliation/ReconcileStock.cs
  - samples/Shop/Inventory/Shop.Inventory.Api/Program.cs
  - samples/Shop/Inventory/Shop.Inventory.Api/appsettings.json
  - samples/Shop/Inventory/Shop.Inventory.Tests/InventoryTests.cs
unproven:
  - One-shot jobs with AddDeferred: the reference platform registers recurring jobs only; the call has the same shape with a fire time instead of a cron expression
  - A job that runs for one tenant through ScheduledJobOptions.TenantScope: the reference job is cross-tenant; see the SharedKernel.Scheduling README
---
# Add a background job

**Use when:** one unit of work must run on a time trigger (a cron schedule, or once at a given time) and run once per
occurrence across every replica of the service. That guarantee comes from the per-occurrence lease in
`IDistributedLockService`: register the Redis lock (`AddRedisConnection(configuration).AddRedisDistributedLocking()`)
or every replica runs every occurrence.
**Not when:** the work has several steps, waits for signals or must resume after a crash (add-long-running-workflow.md;
a job may start a workflow); something should happen because an event occurred (add-messaging.md); you only need a
distributed lock in a request (add-caching.md).

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Application | `SharedKernel.Application` | The job is an ordinary `ICommand` and `ICommandHandler<>` |
| Infrastructure | `SharedKernel.Scheduling` | `AddSharedKernelScheduling()`, `AddRecurring<TCommand>`, `AddDeferred<TCommand>`, the policies |
| Infrastructure | `SharedKernel.Caching.Redis.Core`, `SharedKernel.Caching.Redis.DistributedLocking` | The cross-replica lease: `AddRedisConnection`, `AddRedisDistributedLocking()` |
| Api/Worker | `SharedKernel.Application.Pipeline`, `SharedKernel.Application.Mediator.MediatR` | Every run goes through `ISender` and the full pipeline |
| Api/Worker | `SharedKernel.ServiceDefaults` | `AddSharedKernelReadiness()` maps the `scheduler` probe |
| Tests | `SharedKernel.Scheduling.Testing` | `InMemoryScheduledJobRegistry` and `TriggerAsync` |

## Steps

1. **Infrastructure: the lock that makes the job run once.** Register the Redis lock before the scheduler. Running on
   one replica only is this lock, not the scheduler: without an `IDistributedLockService` the scheduler still starts
   and every replica fires every occurrence.

   ```csharp
   using SharedKernel.Caching.Redis.Core.Extensions;
   using SharedKernel.Caching.Redis.DistributedLocking.Extensions;
   using SharedKernel.Scheduling.Extensions;

   services.AddRedisConnection(configuration).AddRedisDistributedLocking();
   ISchedulingBuilder scheduling = services.AddSharedKernelScheduling();
   ```

2. **Application: the command the job sends.** A job never contains logic; each fire builds a command and sends it
   through `ISender`, so validation, transactions and auditing apply as they do for an HTTP request. Each run has a
   system caller and a new correlation id. A job that reads every tenant enters an explicit cross-tenant scope.

   ```csharp
   using SharedKernel.Application.Messaging;
   using SharedKernel.Persistence.Abstractions.Context;
   using SharedKernel.Primitives.Results;

   public sealed record ReconcileStockCommand(DateTimeOffset FireTime) : ICommand;

   public sealed class ReconcileStockHandler(ICrossTenantScope crossTenant /*, your ports */)
       : ICommandHandler<ReconcileStockCommand>
   {
       public const string JobName = "inventory-reconciliation";

       public async Task<Result> Handle(ReconcileStockCommand command, CancellationToken ct)
       {
           using (crossTenant.Enter("inventory reconciliation"))
           {
               // Read the source of truth, rebuild what the job owns.
           }

           return Result.Success();
       }
   }
   ```

3. **Infrastructure: register the job.** Keep the registration in one method that takes `IScheduledJobRegistry`,
   so the host and the tests call the same code. `MisfirePolicy` and `OverlapPolicy` have no default: decide what
   happens after downtime and when a run is still going at the next tick. Cron is Quartz syntax, seconds first, in UTC.

   ```csharp
   using SharedKernel.Scheduling.Policies;
   using SharedKernel.Scheduling.Registry;

   public static class InventoryJobs
   {
       public const string DefaultReconciliationCron = "0/2 * * * * ?";

       public static IScheduledJobRegistry Register(
           IScheduledJobRegistry registry,
           string? reconciliationCron) =>
           registry.AddRecurring(
               ReconcileStockHandler.JobName,
               reconciliationCron ?? DefaultReconciliationCron,
               context => new ReconcileStockCommand(context.ScheduledFireTimeUtc),
               options =>
               {
                   options.MisfirePolicy = MisfirePolicy.Skip;
                   options.OverlapPolicy = OverlapPolicy.Skip;
               });
   }
   ```

   A one-shot job is `registry.AddDeferred<TCommand>(jobName, fireAtUtc, factory, configure)` with the same
   mandatory policies. A job for a single tenant sets `options.TenantScope`. Call `Register` with the builder from
   step 1:

   ```csharp
   InventoryJobs.Register(scheduling, configuration["Inventory:Reconciliation:Cron"]);
   ```

4. **Api/Worker: the pipeline and readiness.** The scheduler needs `ISender` (a mediator adapter) and an `IClock`
   (`services.AddClock()` in Infrastructure).

   ```csharp
   using SharedKernel.Application.Mediator.MediatR;
   using SharedKernel.Application.Pipeline;
   using SharedKernel.ServiceDefaults.HealthChecks;

   builder.Services.AddSharedKernelApplication(typeof(ReconcileStockCommand).Assembly, app => app.UseMediatR());
   builder.Services.AddHealthChecks().AddSharedKernelReadiness(); // includes the "scheduler" probe
   ```

   Traces and metrics (`SharedKernel.Scheduling`) are exported by `AddServiceDefaults()` with no extra call.

5. **Configuration:** `SharedKernel:Scheduling` for the scheduler loop, plus your own key for each cron expression
   you want to change per environment (the reference platform uses `Inventory:Reconciliation:Cron`). The scheduler
   keys are in the `SharedKernel.Scheduling` README.

## Test it

Register the jobs against an `InMemoryScheduledJobRegistry` with the same `Register` method, fire an occurrence with
`TriggerAsync`, and assert the command that was sent. Test the handler on its own like any other handler.

```csharp
using SharedKernel.Testing.Scheduling;

var sender = new RecordingSender(); // your ISender that records what it is sent
var registry = new InMemoryScheduledJobRegistry(sender);
InventoryJobs.Register(registry, reconciliationCron: null);
var at = new DateTimeOffset(2026, 10, 6, 12, 0, 2, TimeSpan.Zero);

var outcome = await registry.TriggerAsync(ReconcileStockHandler.JobName, at);

outcome!.Value.IsSuccess.Should().BeTrue();
sender.Sent.Should().ContainSingle().Which.Should().Be(new ReconcileStockCommand(at));
```

Proving once-per-occurrence across replicas needs two real hosts and a real Redis; do it end to end.

## Check

- Registration throws when `MisfirePolicy` or `OverlapPolicy` is not set, when two jobs share a name, or when the
  cron expression is not valid Quartz syntax.
- The host fails to start without an `IClock` (`services.AddClock()`, namespace `SharedKernel.Primitives.Clocks`);
  every run needs the `ISender` that `UseMediatR()` provides.
- `SK0001` flags `DateTime.UtcNow` in the handler; use the fire time from the command or an injected `IClock`.
- `SK0030` flags a `Result` the handler drops.
- `/health/ready` stays unhealthy while the scheduler loop is not running.
- With jobs registered and no `IDistributedLockService`, the host logs a Warning at start (EventId 19002) that every
  job runs once per replica; register the Redis lock to get one run per occurrence.

## Read next

`SharedKernel.Scheduling`, `SharedKernel.Caching.Redis.DistributedLocking`, `SharedKernel.Scheduling.Testing`,
`SharedKernel.Persistence.Abstractions` (cross-tenant scope).
