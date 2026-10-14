---
proven-by:
  - samples/Shop/Ordering/Shop.Ordering.Application/OrderCommands.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/Fulfilment/OrderFulfilment.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/OrderingInfrastructure.cs
  - samples/Shop/Ordering/Shop.Ordering.Api/Program.cs
  - samples/Shop/Ordering/Shop.Ordering.Api/appsettings.json
  - samples/Shop/Ordering/Shop.Ordering.Tests/OrderingTests.cs
  - samples/Shop/Shop.E2E/OrderingFlowTests.cs
unproven:
  - Signals, queries and awaiting a result through IWorkflowHandle: the reference workflow is started and left to finish; see the SharedKernel.Workflows.Temporal README
  - A separate Worker process with AsClientOnly() on the Api: the reference platform runs the worker inside the Api host
  - Payload encryption with WithPayloadEncryption(): not used by the reference platform
---
# Add a long-running workflow

**Use when:** a business process has several steps across services, must survive crashes and restarts, may wait
(timers, days), and must undo earlier steps when a later one fails (compensation). This is the only place for that
coordination: do not build sagas or routing slips on the message bus.
**Not when:** one unit of work on a time trigger (add-background-job.md; a job may start a workflow); reacting to one
event with one action (add-messaging.md); a single call to another service (add-outbound-call.md).

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Application | `SharedKernel.Application` | Each step is an ordinary command and handler, sent through `ISender` |
| Infrastructure | `SharedKernel.Workflows.Temporal` | `AddSharedKernelTemporalWorkflows`, `WorkflowBase`, `ActivityBase`, `CommandActivity<T>`, `IWorkflowDispatcher` |
| Api/Worker | `SharedKernel.Application.Pipeline`, `SharedKernel.Application.Mediator.MediatR` | Activities send commands through the full pipeline |
| Api/Worker | `SharedKernel.ServiceDefaults` | `WithWorkflowTelemetry()`, `AddSharedKernelReadiness()` (Temporal probe) |
| Tests | `SharedKernel.Workflows.Testing` | `InMemoryWorkflowDispatcher` for the code that starts workflows |

`SharedKernel.Workflows.Temporal` is an Adapter package: the workflow, its activities and every `IWorkflowDispatcher`
call live in Infrastructure. When Application code must start a workflow, give it a port and implement the port in
Infrastructure. The reference platform starts its workflow from a message consumer.

## Steps

1. **Application: one command per step, including the compensations.** Nothing here knows about Temporal.

   ```csharp
   using SharedKernel.Application.Messaging;

   public sealed record ChargeOrderCommand(Guid OrderId, string PaymentToken) : ICommand;
   public sealed record ReleaseOrderStockCommand(Guid OrderId) : ICommand;
   public sealed record RejectOrderCommand(Guid OrderId, string Reason) : ICommand;
   ```

   Make each handler idempotent per business key (the order id): Temporal retries activities.

2. **Infrastructure: activities.** `CommandActivity<TCommand>` sends its command through `ISender` in the
   workflow's caller context and turns a failed `Result` into a Temporal failure (business refusals are
   non-retryable). An activity that calls a port directly derives `ActivityBase` and throws `Fail(error)`.

   ```csharp
   using Microsoft.Extensions.Logging;
   using SharedKernel.Application.Messaging;
   using SharedKernel.Primitives.Clocks;
   using SharedKernel.Workflows.Temporal.Authoring;
   using Temporalio.Activities;

   public sealed class ChargeOrderActivity(
       ISender sender, ILogger<ChargeOrderActivity> logger, IClock clock)
       : CommandActivity<ChargeOrderCommand>(sender, logger, clock)
   {
       [Activity(nameof(ChargeOrderActivity))]
       public override Task ExecuteAsync(ChargeOrderCommand command, CancellationToken ct = default) =>
           base.ExecuteAsync(command, ct);
   }
   ```

3. **Infrastructure: the workflow.** Derive `WorkflowBase`, run steps with `ExecuteAsync<TActivity, TArgs, TResult>`,
   and compensate in a `catch` on a non-retryable failure. Workflow code must be deterministic: no clock, no
   `Guid.NewGuid`, no I/O (use `UtcNow` and `NewId()` from the base).

   ```csharp
   using SharedKernel.Workflows.Temporal.Authoring;
   using Temporalio.Exceptions;
   using Temporalio.Workflows;

   [Workflow]
   public sealed class OrderFulfilmentWorkflow : WorkflowBase
   {
       [WorkflowRun]
       public async Task<string> RunAsync(FulfilmentRequest request)
       {
           Guid reservation = await ExecuteAsync<ReserveStockActivity, FulfilmentRequest, Guid>(request);
           try
           {
               await ExecuteAsync<ChargeOrderActivity, ChargeOrderCommand, object?>(
                   new ChargeOrderCommand(request.OrderId, request.PaymentToken));
           }
           catch (ActivityFailureException failure)
               when (failure.InnerException is ApplicationFailureException { NonRetryable: true } declined)
           {
               // Compensation: release what the earlier step held, then reject.
               await ExecuteAsync<ReleaseOrderStockActivity, ReleaseOrderStockCommand, object?>(
                   new ReleaseOrderStockCommand(request.OrderId));
               await ExecuteAsync<RejectOrderActivity, RejectOrderCommand, object?>(
                   new RejectOrderCommand(request.OrderId, declined.ErrorType ?? "billing.refused"));
               return "payment-declined";
           }

           await ExecuteAsync<ConfirmOrderActivity, ConfirmOrderCommand, object?>(
               new ConfirmOrderCommand(request.OrderId, reservation));
           return "confirmed";
       }
   }
   ```

4. **Infrastructure: start it.** `IWorkflowDispatcher.StartAsync` takes the arguments, the start options and a
   `TenantScope`; the workflow id is built from the tenant and the `BusinessKey`, so a duplicate start for the same
   order fails with `WorkflowErrorCodes.AlreadyStarted` instead of running twice.

   ```csharp
   using SharedKernel.Execution.Tenancy;
   using SharedKernel.Workflows.Temporal.Dispatch;
   using SharedKernel.Workflows.Temporal.Errors;
   using Temporalio.Api.Enums.V1;

   var started = await workflows.StartAsync<OrderFulfilmentWorkflow, FulfilmentRequest>(
       request,
       new WorkflowStartOptions
       {
           TaskQueue = FulfilmentQueues.TaskQueue,
           BusinessKey = placed.OrderId.ToString("D"),
           IdReusePolicy = WorkflowIdReusePolicy.RejectDuplicate,
           IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
       },
       TenantScope.FromNullable(caller.TenantId),
       ct);

   if (started.IsFailure && started.Error.Code != WorkflowErrorCodes.AlreadyStarted)
   {
       // Let the caller retry (a consumer rethrows so the bus redelivers).
   }
   ```

5. **Infrastructure: register the workflow, its activities and the worker.** `WithWorker(taskQueue)` runs a worker
   in this host; a process that only starts workflows uses `AsClientOnly()` instead.

   ```csharp
   using SharedKernel.Workflows.Temporal.Hosting;

   services
       .AddSharedKernelTemporalWorkflows(configuration)
       .AddWorkflow<OrderFulfilmentWorkflow>()
       .AddActivities<ReserveStockActivity>()
       .AddActivities<ChargeOrderActivity>()
       .AddActivities<ReleaseOrderStockActivity>()
       .AddActivities<ConfirmOrderActivity>()
       .AddActivities<RejectOrderActivity>()
       .WithWorker(FulfilmentQueues.TaskQueue)
       .WithOpenTelemetry()
       .Build();
   ```

6. **Api/Worker: the host.** The host that runs the worker needs the application pipeline (the activities send
   commands), the telemetry hook and readiness.

   ```csharp
   using SharedKernel.Application.Mediator.MediatR;
   using SharedKernel.Application.Pipeline;
   using SharedKernel.ServiceDefaults.HealthChecks;
   using SharedKernel.ServiceDefaults.Telemetry;

   builder.AddServiceDefaults();
   builder.WithWorkflowTelemetry();
   builder.AddOrderingInfrastructure();
   builder.Services.AddSharedKernelApplication(
       typeof(PlaceOrderCommand).Assembly,
       app => app.UseMediatR().WithTransactions());
   builder.Services.AddHealthChecks().AddSharedKernelReadiness();
   ```

7. **Configuration:** `Workflows:Temporal` (address, namespace). The keys are in the `SharedKernel.Workflows.Temporal`
   README.

## Test it

- The code that starts a workflow: inject `new InMemoryWorkflowDispatcher()` (namespace `SharedKernel.Testing.Workflows`),
  run it twice for the same business key, then `workflows.ShouldHaveStartedOnce<OrderFulfilmentWorkflow>()` and assert
  its `TenantScope` and `Args`.
- The step handlers: ordinary handler tests, no Temporal involved.
- The workflow itself (ordering, compensation, retries): run it against a real Temporal server end to end; the
  README names the options.

## Check

- `SK0028` flags a clock, `Guid.NewGuid`, `Random`, I/O or `Task.Run` inside a workflow.
- `SK0029` flags a raw Temporal client, worker or handle injected outside the package; inject `IWorkflowDispatcher`.
- `SK0030` flags a dropped `Result` from `StartAsync`.
- `/health/ready` stays unhealthy while Temporal cannot be reached.

## Read next

`SharedKernel.Workflows.Temporal`, `SharedKernel.Workflows.Testing`, `SharedKernel.Application.Pipeline`.
