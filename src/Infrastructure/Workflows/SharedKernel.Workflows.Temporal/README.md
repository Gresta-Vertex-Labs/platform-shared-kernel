# SharedKernel.Workflows.Temporal

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-6f42c1)
![Temporalio 1.17](https://img.shields.io/badge/Temporalio-1.17-informational)

> **Durable, crash-proof workflows on [Temporal](https://github.com/temporalio/sdk-dotnet): start, signal, query and
> await them from application code, author them on platform base types, and host the worker in the generic host —
> with tenant-scoped workflow ids, caller propagation into activities, `Result`-to-failure mapping and payload
> encryption already wired.**

| You get | So that |
| --- | --- |
| `IWorkflowDispatcher` returning `Result<IWorkflowHandle>` | Application code starts and drives workflows without touching a `Temporalio.*` type |
| A mandatory `TenantScope` on every dispatch call, and ids from `IWorkflowIdFactory` | Tenant B can never address tenant A's workflow by guessing an id |
| `WorkflowBase`, `ActivityBase`, `CommandActivity<TCommand>` | Workflows stay deterministic; activities are ordinary DI code; a kernel command is one activity |
| `Result` → Temporal failure mapping by `ErrorType` | Expected errors fail fast (non-retryable); transient ones retry |
| Caller propagation into activities | `IRequestContext` inside an activity carries the dispatcher's tenant and correlation id |
| `.WithPayloadEncryption()` | Workflow inputs and outputs are not stored in plaintext in server history |
| `.AsClientOnly()` and eager `.Build()` validation | API pods dispatch without hosting a worker; a mis-wired worker fails at startup, not silently |
| The `workflows` readiness probe | A worker whose pollers died stops receiving traffic |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Configuration](#configuration)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)

## Install

```xml
<PackageReference Include="SharedKernel.Workflows.Temporal" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Adapter — reference it from your **Infrastructure** project (workflows and activities) and compose it in the **Api** / **Worker** |
| Depends on | `SharedKernel.Primitives`, `.Execution`, `.Configuration`, `.Cryptography`, `SharedKernel.Application` (`ISender` only); `Temporalio` 1.17 (+ `.Extensions.Hosting`, `.Extensions.OpenTelemetry`, `.Extensions.DiagnosticSource`) |
| Namespaces | `SharedKernel.Workflows.Temporal.Dispatch`, `.Authoring`, `.Hosting`, `.Configuration`, `.Errors`, `.Health`, `.Constants` |
| Needs in the host | `IClock` and logging; a mediator adapter (`app.UseMediatR()`) when you use `CommandActivity<>`; `ISymmetricEncryptionService` when you use `.WithPayloadEncryption()` |
| Temporal server | Any Temporal server or Temporal Cloud namespace |

There is deliberately no `SharedKernel.Workflows.Abstractions`: the determinism and replay programming model is the
abstraction (see [Design decisions](#design-decisions)).

## Quick start

A service that only **starts** workflows — the common case for an API — registers a client and no worker:

```csharp
using SharedKernel.Workflows.Temporal.Hosting;

builder.Services.AddSharedKernelTemporalWorkflows(builder.Configuration)   // Workflows:Temporal
    .AsClientOnly()
    .WithPayloadEncryption()
    .WithOpenTelemetry()
    .Build();
```

```json
{
  "Workflows": {
    "Temporal": { "TargetHost": "temporal-frontend:7233", "Namespace": "orders" }
  }
}
```

```csharp
using SharedKernel.Application.Messaging;
using SharedKernel.Core.Extensions;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Dispatch;
using Temporalio.Api.Enums.V1;

public sealed class StartFulfilmentHandler(IWorkflowDispatcher workflows, IRequestContext caller)
    : ICommandHandler<StartFulfilment, string>
{
    public async Task<Result<string>> Handle(StartFulfilment command, CancellationToken ct)
    {
        var options = new WorkflowStartOptions
        {
            TaskQueue = "orders-fulfilment",
            BusinessKey = command.OrderId,
            IdReusePolicy = WorkflowIdReusePolicy.RejectDuplicate,
            IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
        };

        var started = await workflows.StartAsync<OrderFulfilmentWorkflow, string>(
            command.OrderId, options, TenantScope.FromNullable(caller.TenantId), ct);

        return started.Map(handle => handle.WorkflowId);
    }
}
```

The service that **runs** the workflow registers it with a worker:

```csharp
builder.Services.AddSharedKernelTemporalWorkflows(builder.Configuration)
    .AddWorkflow<OrderFulfilmentWorkflow>()
    .AddActivities<ChargeCardActivity>()
    .AddActivities<ApproveOrderActivity>()
    .WithWorker("orders-fulfilment", t => t with { MaxConcurrentActivities = 50 })
    .WithPayloadEncryption()
    .WithOpenTelemetry()
    .WithMetrics()
    .Build();

builder.Services.AddHealthChecks().AddSharedKernelReadiness();   // exposes the "workflows" probe
```

## How it works

```mermaid
sequenceDiagram
    participant A as Application code
    participant D as IWorkflowDispatcher
    participant T as Temporal server
    participant W as Worker (WorkflowBase)
    participant X as Activity (ActivityBase / CommandActivity)
    A->>D: StartAsync(args, options, TenantScope)
    D->>D: refuse Global scope; id = {tenant}:{workflowType}:{businessKey}
    D->>T: start (tenant + correlation headers, encrypted payload)
    T->>W: workflow task (replayable)
    W->>X: ExecuteAsync<TActivity, TArgs, TResult>
    X->>X: RequestContextScope(PropagatedRequestContext) → ISender / DI code
    X-->>W: result, or ApplicationFailure (retryable by ErrorType)
    D-->>A: Result<IWorkflowHandle>
```

### The determinism rule

**Workflow code is replay code.** Every statement in a `[Workflow]` type is re-executed from history — possibly on
another process, possibly months later — and must issue the same commands every time. Anything that reads a clock, a
random source, the network, a database, configuration or DI belongs in an **activity**. Many violations compile and
only fail on replay, in production; review and replay tests are the backstop.

| Inside a workflow (`WorkflowBase`) | Inside an activity (`ActivityBase`) |
| --- | --- |
| `UtcNow` / `Workflow.UtcNow` — never `DateTimeOffset.UtcNow`, never `IClock` | `Clock.UtcNow` (`IClock`) — never `DateTimeOffset.UtcNow` |
| `NewId()` / `Workflow.NewGuid()`, `Workflow.Random` | Ordinary code |
| `Workflow.DelayAsync`, `Workflow.WaitConditionAsync` | `Task.Delay` is fine |
| No `Task.Run`, `ContinueWith`, `ConfigureAwait(false)`, `lock`, `Parallel.*` | All fine |
| No constructor parameters — the worker, not DI, constructs workflows | Ordinary constructor DI |
| `[LoggerMessage]` methods called on `Logger` (= `Workflow.Logger`, replay-aware) | `[LoggerMessage]` methods on the injected logger |
| No `System.Diagnostics.Activity` API — `.WithOpenTelemetry()` traces | Fine |

Inside a workflow this **inverts** the platform rule that mandates `IClock` (SK0001): an injected clock cannot exist on
replay. Analyzer SK0028 flags the common violations.

### Failure mapping

`WorkflowFailureMapper` turns an `Error` into an `ApplicationFailureException` whose `errorType` is `Error.Code`:

| `ErrorType` | Temporal failure |
| --- | --- |
| Validation, NotFound, Conflict, Unauthorized, Forbidden, BusinessRule | **non-retryable** — the workflow can compensate at once |
| Unexpected, Unavailable, Timeout | retryable |

An exception that escapes an activity unmapped is retryable, as in Temporal. The one bug this domain cannot catch for
you: an activity that receives a failed `Result` and **returns normally** reports success, and the workflow continues
down the happy path. Every activity path ends in `throw Fail(error)` / `throw FailFrom(result)` or uses
`CommandActivity<>`, which maps for you.

### Tenancy, ids and propagation

- `TenantScope` is a required, separate parameter on every dispatch member. `TenantScope.Global` returns
  `workflow.tenant_scope_missing` before any I/O (log 17001).
- No member accepts a raw workflow id: `IWorkflowIdFactory.Create(workflowTypeName, businessKey, tenantScope)` builds
  `{tenant}:{workflowType}:{businessKey}`. The id is the durable idempotency key of the workflow.
- The tenant, correlation id and caller travel as Temporal headers (`RequestContextPropagation`, `WellKnownHeaders`).
  `WorkflowBase.TenantScope` and `.CorrelationId` read them; each activity runs inside a `RequestContextScope`
  carrying a `PropagatedRequestContext`, so tenant filters, idempotency keys and outbound calls use the dispatcher's
  tenant and correlation id. It is attribution only: it grants no permission.

### What `CommandActivity<TCommand>` means for the application pipeline

- Each activity execution has its own DI scope, so the command is **outermost**: `TransactionBehavior` commits once
  and `ICommandScope.OnCompleted` callbacks run before the activity returns.
- A failed `Result` (validation, authorization, business rule) becomes a non-retryable failure.
- **Retries re-send the command.** A command with side effects outside its unit of work should implement
  `IIdempotentRequest` with a key derived from the workflow id.

## Recipes

### 1. Write a workflow and its activities

```csharp
using Microsoft.Extensions.Logging;
using SharedKernel.Workflows.Temporal.Authoring;
using Temporalio.Activities;
using Temporalio.Workflows;

public sealed class ChargeCardActivity(IPaymentGateway gateway, ILogger<ChargeCardActivity> logger, IClock clock)
    : ActivityBase(logger, clock)
{
    // Name every [Activity] explicitly: an unnamed method is registered under the METHOD name.
    [Activity(nameof(ChargeCardActivity))]
    public async Task<string> ChargeAsync(ChargeRequest request, CancellationToken ct)
    {
        Heartbeat("charging");                      // long activities must heartbeat or they are retried while running
        var result = await gateway.ChargeAsync(request.CardToken, request.Amount, ct);
        if (result.IsFailure)
            throw FailFrom(result);                 // never let a failed Result reach the end of the body
        return result.Value.ReceiptId;
    }
}

public sealed class ApproveOrderActivity(ISender sender, ILogger<ApproveOrderActivity> logger, IClock clock)
    : CommandActivity<ApproveOrderCommand>(sender, logger, clock)
{
    [Activity(nameof(ApproveOrderActivity))]
    public override Task ExecuteAsync(ApproveOrderCommand command, CancellationToken ct = default)
        => base.ExecuteAsync(command, ct);
}

[Workflow]
public sealed class OrderFulfilmentWorkflow : WorkflowBase
{
    private bool _cancelledByCustomer;

    [WorkflowRun]
    public async Task<string> RunAsync(string orderId)
    {
        await ExecuteAsync<ApproveOrderActivity, ApproveOrderCommand, object?>(new ApproveOrderCommand(orderId));

        var receiptId = await ExecuteAsync<ChargeCardActivity, ChargeRequest, string>(
            new ChargeRequest(orderId, Amount: 4999),
            new ActivityDispatchOptions { StartToCloseTimeout = TimeSpan.FromSeconds(15) });

        OrderLog.OrderFulfilled(Logger, orderId, UtcNow);   // [LoggerMessage] on Workflow.Logger
        return receiptId;
    }

    [WorkflowSignal]
    public Task CancelByCustomer() { _cancelledByCustomer = true; return Task.CompletedTask; }

    [WorkflowQuery]
    public bool WasCancelledByCustomer() => _cancelledByCustomer;
}
```

Without `ActivityDispatchOptions`, `ExecuteAsync` applies the `WorkflowWellKnown` defaults: 30 s start-to-close,
30 s heartbeat timeout, 5 attempts.

### 2. Signal, query and await a running workflow

```csharp
var handle = workflows.GetHandle<string>(workflowId, runId: null, TenantScope.For(tenantId));
await handle.SignalAsync("CancelByCustomer", args: (object?)null, ct);
Result<bool> cancelled = await handle.QueryAsync<bool>("WasCancelledByCustomer", ct);
Result<string> receipt = await handle.GetResultAsync(ct);
```

### 3. Send a permission-guarded command from an activity

A propagated context grants no permission, so a `[RequirePermission]` command fails with `Forbidden`. Open a scope with
exactly the permissions the activity needs:

```csharp
[Activity(nameof(ApproveOrderActivity))]
public override async Task ExecuteAsync(ApproveOrderCommand command, CancellationToken ct = default)
{
    using (RequestContextScope.Begin(new SystemRequestContext(["orders.approve"], "orders-worker", TenantScope.Tenant)))
    {
        await base.ExecuteAsync(command, ct);
    }
}
```

### 4. Change a workflow that has running executions

Reordering activities, inserting a step, changing a timer or renaming an activity in a deployed workflow makes
**every** in-flight execution fail on replay the next time it is replayed. Use `Workflow.Patched`, across separate
deploys:

1. **Introduce** the patch and keep the old path, exactly, in the `else` branch:

   ```csharp
   if (Workflow.Patched("charge-before-approve"))
   {
       await ExecuteAsync<ChargeCardActivity, ChargeRequest, string>(request);
       await ExecuteAsync<ApproveOrderActivity, ApproveOrderCommand, object?>(command);
   }
   else
   {
       await ExecuteAsync<ApproveOrderActivity, ApproveOrderCommand, object?>(command);
       await ExecuteAsync<ChargeCardActivity, ChargeRequest, string>(request);
   }
   ```

2. **Deploy.** Old executions replay down `else`; new ones record the patch marker.
3. **Drain.** Wait until no execution started before the patch can still be running.
4. **Deprecate.** Replace `Patched` with `Workflow.DeprecatePatch("charge-before-approve")` and delete the `else`.
5. **Remove** the `DeprecatePatch` call once that generation has drained too.

Replay tests (`WorkflowReplayer` over recorded histories) are the only thing that catches a missed patch before
production.

### 5. Encrypt workflow payloads

Temporal stores every workflow input, output, signal and activity argument in server history, readable by anyone with
namespace access. `.WithPayloadEncryption()` wraps `ISymmetricEncryptionService` (AES-256-GCM) as an `IPayloadCodec`:

```csharp
builder.Services.AddSingleton<IEncryptionKeyProvider, YourEncryptionKeyProvider>();
builder.Services.AddSharedKernelCryptography(builder.Configuration).AddSymmetricEncryption();

builder.Services.AddSharedKernelTemporalWorkflows(builder.Configuration)
    .AsClientOnly()
    .WithPayloadEncryption()
    .Build();
```

- The associated data is the UTF-8 **workflow id** (never the run id, which changes across continue-as-new and
  retries), so a ciphertext cannot be replayed into another workflow. Outside an execution context (CLI, Web UI) the
  binding is empty.
- An encoded payload carries `encoding: binary/encrypted-sk-v2` and `EncryptedPayload.ToBytes()` data. A payload
  without the marker passes through unchanged; one with the marker that fails to parse or decrypt throws
  (`workflow.payload_codec_failure`) — ciphertext is never returned as plaintext.
- **Costs:** payloads are opaque in the Web UI and CLI unless you deploy a codec server, and a key must stay resolvable
  for as long as any history encrypted under it may replay — months for a long workflow.

## Configuration

Section `Workflows:Temporal` (`TemporalOptions.SectionName` — note: not under `SharedKernel:`), validated when the
host starts.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `Workflows:Temporal:TargetHost` | `string` | — (required) | Temporal frontend, e.g. `localhost:7233` |
| `Workflows:Temporal:Namespace` | `string` | — (required) | Temporal namespace |
| `Workflows:Temporal:TaskQueue` | `string?` | `null` | Default task queue when `.WithWorker(...)` names none |
| `Workflows:Temporal:Tls` | `bool` | `false` | Connect with TLS |
| `Workflows:Temporal:ApiKey` | `string?` | `null` | Temporal Cloud API key |
| `Workflows:Temporal:IdentityPrefix` | `string?` | `null` | Prefix of the reported worker identity (`{prefix}-{MachineName}`) |
| `Workflows:Temporal:DefaultActivityStartToCloseTimeoutSeconds` | `int` | `30` | Bound and validated, **not yet applied** — `ExecuteAsync` uses the `WorkflowWellKnown` constants |
| `Workflows:Temporal:DefaultWorkflowExecutionTimeoutSeconds` | `int?` | `null` | Bound and validated, not yet applied; set `WorkflowStartOptions.ExecutionTimeout` |
| `Workflows:Temporal:DefaultRetryMaximumAttempts` | `int` | `5` | Bound and validated, not yet applied |
| `Workflows:Temporal:ValidateNamespaceOnStart` | `bool` | `true` | Bound and validated, not yet applied |

Worker tuning (`.WithWorker(taskQueue, t => t with { … })`, `WorkerTuningOptions`): `MaxConcurrentWorkflowTasks`,
`MaxConcurrentActivities`, `MaxConcurrentLocalActivities` (SDK defaults when `null`), `MaxCachedWorkflows` (1000),
`GracefulShutdownTimeout` (30 s).

## Reference

### Registration

`services.AddSharedKernelTemporalWorkflows(IConfiguration)` returns `ITemporalWorkflowsBuilder`:

| Method | Does |
| --- | --- |
| `AddWorkflow<TWorkflow>()` | Registers a `WorkflowBase` workflow on the worker |
| `AddActivities<TActivities>()` | Registers an activity class (scoped — one DI scope per activity task) |
| `WithWorker(taskQueue, tune?)` | Hosts a worker on the task queue (one per builder) |
| `AsClientOnly()` | Client and dispatcher only; no `IHostedService` |
| `WithPayloadEncryption()` | Adds the AES-GCM payload codec (log 17010) |
| `WithOpenTelemetry()` / `WithMetrics()` | Temporal's tracing interceptor / SDK metrics |
| `AllowRawClientAccess()` | Registers `ITemporalRawClientAccessor` (log 17012, Warning) — bypasses tenant scoping and id composition |
| `Build()` | Validates and registers everything; returns `IServiceCollection` |

`Build()` throws for: client-only combined with a workflow, activity or worker; no worker and not client-only; a worker
with no workflows and no activities; a second `WithWorker`; a second `Build()`; a missing `TargetHost`/`Namespace`.

Lifetimes: `IWorkflowDispatcher` scoped; `IWorkflowIdFactory`, the Temporal client, the codec and the probe
singletons. The package registers no `IClock` or `ILogger<T>`.

### Dispatch surface

| Member | Returns |
| --- | --- |
| `StartAsync<TWorkflow>(options, tenantScope, ct)` | `Result<IWorkflowHandle>` |
| `StartAsync<TWorkflow, TArgs>(args, options, tenantScope, ct)` | `Result<IWorkflowHandle>` |
| `StartAsync<TWorkflow, TArgs, TResult>(args, options, tenantScope, ct)` | `Result<IWorkflowHandle<TResult>>` |
| `GetHandle(workflowId, runId, tenantScope)` / `GetHandle<TResult>(…)` | a handle |
| `DescribeAsync(workflowId, tenantScope, ct)` | `Result<WorkflowExecutionDescription>` |
| `IWorkflowHandle.SignalAsync` / `QueryAsync<T>` / `CancelAsync` / `TerminateAsync(reason)` | `Result` / `Result<T>` |
| `IWorkflowHandle<TResult>.GetResultAsync` | `Result<TResult>` |

`WorkflowStartOptions`: required `TaskQueue`, `BusinessKey`, `IdReusePolicy`, `IdConflictPolicy`; optional
`ExecutionTimeout`, `RunTimeout`, `TaskTimeout`, `RetryPolicy`.

### Errors

`WorkflowErrors`, prefix `workflow.`:

| Code | Type | When |
| --- | --- | --- |
| `workflow.tenant_scope_missing` | Unauthorized | A dispatch call passed `TenantScope.Global` |
| `workflow.not_found` | NotFound | No execution for the id |
| `workflow.already_started` | Conflict | An execution with the id is running |
| `workflow.invalid_workflow_id` | Validation | The id could not be composed |
| `workflow.invalid_workflow_registration` | Validation | A registration is not a valid workflow/activity |
| `workflow.cancelled` / `.terminated` / `.timed_out` | Unexpected | The awaited execution ended that way |
| `workflow.query_failed` / `.signal_failed` | Unexpected | The query or signal failed |
| `workflow.service_unavailable` / `.namespace_not_found` | Unexpected | Temporal unreachable / namespace not addressable |
| `workflow.worker_not_configured` | Unexpected | No worker for the task queue |
| `workflow.determinism_violation` | Unexpected | A replay detected non-determinism |
| `workflow.payload_codec_failure` | Unexpected | An encrypted payload could not be decoded |

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 17000 | Information | Workflow started |
| 17001 | Warning | Dispatch rejected for `TenantScope.Global` |
| 17002 | Debug | Signal dispatched |
| 17003 | Debug | Query dispatched |
| 17004 | Warning | Workflow terminated (no compensation runs) |
| 17005 | Warning | A workflow observed no tenant header |
| 17006 | Error | Payload codec failed |
| 17007 | Information | Worker composition built |
| 17008 | Information | Client-only composition built |
| 17009 | Warning | Readiness probe degraded |
| 17010 | Information | Payload encryption configured |
| 17011 | Debug | Activity heartbeat recorded |
| 17012 | Warning | Raw client access enabled |

### Health and telemetry

Registers the `workflows` readiness probe (`WorkflowReadiness.ProbeName`) on every composition;
`AddSharedKernelReadiness()` exposes it on `/health/ready`. It is ready when Temporal answers, the namespace can be
described and — on a worker — every worker is still polling; `ReadinessReport.Data` carries `Reachable`,
`NamespaceAddressable` and `WorkerPollersActive`. A task-queue backlog never fails readiness. Traces and metrics use the
name `SharedKernel.Workflows`; subscribe with ServiceDefaults' `WithWorkflowTelemetry()`.

## Testing

Code that **dispatches** workflows: reference
[`SharedKernel.Workflows.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Workflows.Testing/README.md)
and call `services.AddInMemoryWorkflowDispatcher()`. `InMemoryWorkflowDispatcher` applies the same tenant and id rules,
records starts (`ShouldHaveStarted<TWorkflow>()`, `ShouldHaveStartedOnce<TWorkflow>()`), lets you complete or fail a
workflow (`CompleteWorkflow`, `FailWorkflow`, `ConfigureQueryHandler`), and its handles assert signals, queries,
cancellation and termination (`ShouldHaveSignalled`, `ShouldHaveBeenCancelled`, …).

Workflow and activity **code**: use `Temporalio.Testing` — `WorkflowEnvironment.StartTimeSkippingAsync()` for timers
(start and await through `environment.Client`; a separately connected client never skips time), `ActivityEnvironment`
for one activity, and `WorkflowReplayer` over recorded histories for every workflow you change.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Read `DateTimeOffset.UtcNow`, `Guid.NewGuid()` or DI in a workflow | `UtcNow`, `NewId()`, `Workflow.*`; move I/O to an activity | Replay diverges and every in-flight execution fails |
| Inject into a workflow's constructor | Keep workflows parameterless | The worker constructs them; the dependency is absent on replay |
| Return normally from an activity after a failed `Result` | `throw FailFrom(result)` or use `CommandActivity<>` | The workflow proceeds as if it succeeded |
| Change a deployed workflow's code path directly | Follow the `Workflow.Patched` lifecycle (recipe 4) | Running executions fail on replay |
| Pass `TenantScope.Global` to dispatch | `TenantScope.For(tenantId)` | Refused with `workflow.tenant_scope_missing` |
| Host a worker in an API pod | `.AsClientOnly()` there; a separate worker deployment | An API rolling deploy becomes a workflow outage |
| Leave long activities without heartbeats | `Heartbeat(...)` and a `HeartbeatTimeout` | A slow activity is presumed dead and retried while still running |
| Inject `ITemporalClient` | `IWorkflowDispatcher` (SK0029) | The raw client bypasses tenant scoping and id composition |
| Rely on the `Default*` options above | Set timeouts per call (`ActivityDispatchOptions`, `WorkflowStartOptions`) | They are bound but not applied yet |
| Retire an encryption key after a few days | Keep it for the longest workflow's lifetime | Old history must still decrypt on replay |

## Design decisions

**Why Temporal?** A durable call stack — "do A, wait 30 days for B, then C or compensate" — is what Temporal models.
Message-shaped reactions stay in messaging, which deliberately has no sagas.

**Why no `.Abstractions` split?** Determinism, replay and patching *are* the programming model; a neutral engine
interface would need no-op members for every other engine. Only the dispatch surface carries no Temporal type, and it
would be extracted only if a second backend were adopted for dispatch.

**Why are expected errors non-retryable?** Temporal retries forever by default. A validation or business-rule failure
will fail identically every time, so it must fail fast and let the workflow compensate.

**Why no `StartAndWaitAsync`?** It hides an unbounded wait behind a start-shaped name. Start, keep the handle, and
await `GetResultAsync` where waiting is intended.

**Not modelled:** workflow Update, list/search (Visibility API), schedules and worker deployment versioning. Reach
them through `AllowRawClientAccess()` only when unavoidable. `Temporalio` ships a native core per runtime identifier
and uses reflection-based JSON, so it is not AOT-safe.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Workflows domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Workflows/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
