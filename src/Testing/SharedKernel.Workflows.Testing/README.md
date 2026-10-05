# SharedKernel.Workflows.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **An in-memory `IWorkflowDispatcher` that records starts, signals, queries, cancellations and terminations per
> tenant, so code that dispatches Temporal workflows is unit-tested without a Temporal server — and the test decides
> how each workflow ends.**

| You get | So that |
| --- | --- |
| `InMemoryWorkflowDispatcher` for `IWorkflowDispatcher` | Dispatching code runs unchanged against a recorder |
| Production workflow ids (`{tenant}:{workflowType}:{businessKey}`) | Id-based idempotency and tenant separation are visible in the test |
| `TenantScope.Global` rejected, duplicate running ids rejected | The fail-closed and already-started paths return the real `workflow.*` errors |
| `CompleteWorkflow` / `FailWorkflow` / `ConfigureQueryHandler` | The test scripts the outcome that `GetResultAsync` and `QueryAsync` return |
| `ShouldHaveStarted…` on the dispatcher, `ShouldHave…` on handles | Starts, signals, queries, cancellations and terminations are asserted in one line |
| `SimulateFailure` on the dispatcher and handles | Temporal-outage handling is testable |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.Workflows.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

Add it to a **test project** only. A production project that references it fails the architecture rule
`TestingNeverReferencedByProduction`.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.Workflows.Temporal` (the dispatch contracts live there; it brings the Temporal .NET SDK), `Microsoft.Extensions.DependencyInjection.Abstractions` |
| Namespaces | `SharedKernel.Testing.Workflows` |

## Quick start

```csharp
using SharedKernel.Execution.Tenancy;
using SharedKernel.Testing.Workflows;
using Xunit;

public sealed class TenantOnboardingTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("0f6b1c2e-5d4a-4f7b-9a01-0000000000a1"));

    [Fact]
    public async Task Onboarding_starts_one_workflow_for_the_tenant()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var onboarding = new TenantOnboarding(dispatcher);   // your class, taking IWorkflowDispatcher

        var result = await onboarding.StartAsync(Tenant, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var start = dispatcher.ShouldHaveStartedOnce<OnboardingWorkflow>();
        Assert.Equal(TenantScope.For(Tenant), start.TenantScope);
    }
}
```

`OnboardingWorkflow` is your `WorkflowBase` subclass; the dispatcher only reads its type name and never runs it.

## How it works

- **Nothing executes.** A start records an execution (status `Running`, run id `in-memory-run-{n}`) and returns a
  handle. Workflow and activity code never runs and history is never replayed; the test ends an execution with
  `CompleteWorkflow` or `FailWorkflow`.
- **Workflow ids.** `{TenantScope}:{typeof(TWorkflow).Name}:{BusinessKey}` joined with
  `WorkflowWellKnown.IdSeparator`, or the result of the `IWorkflowIdFactory` passed to the constructor. A blank
  `BusinessKey` fails with `workflow.invalid_workflow_id`.
- **Fail-closed tenancy.** `StartAsync` and `DescribeAsync` answer `TenantScope.Global` with
  `workflow.tenant_scope_missing`; `GetHandle` throws `ArgumentException` for it.
- **One running execution per id.** Starting an id that is still `Running` fails with `workflow.already_started`; an id
  whose execution has ended can be started again.
- **Shared state.** Every handle for the same workflow id — from `StartAsync` or `GetHandle` — is a view over the same
  execution. `GetHandle` never checks existence; operations on a missing execution return `workflow.not_found`.
- **Results.** `GetResultAsync` returns the value from `CompleteWorkflow` or the error from `FailWorkflow` at once, and
  throws `InvalidOperationException` when neither was called. It never waits.
- **Simplified:** `IdReusePolicy`, `IdConflictPolicy`, timeouts and retry policies are ignored; signals are accepted
  whatever the execution's status; cancel and terminate change only a `Running` execution and are otherwise no-ops that
  succeed; `DescribeAsync` reports a fixed start time (2024-01-01T00:00:00Z), the same close time for an ended
  execution, and `HistoryLength` 0; handles do not re-check the tenant.
- **Lifetime.** `AddInMemoryWorkflowDispatcher()` registers a **singleton** (production registers the dispatcher
  scoped), so the recorded history outlives the scope the system under test ran in. The fakes are thread-safe.

## Recipes

### 1. Drive the result the caller awaits

```csharp
var dispatcher = new InMemoryWorkflowDispatcher();
var sut = new ReportRequester(dispatcher);   // starts ReportWorkflow, then awaits its handle

var start = await sut.StartAsync(Tenant, CancellationToken.None);
dispatcher.CompleteWorkflow(dispatcher.ShouldHaveStarted<ReportWorkflow>().WorkflowId, new ReportReady("r-1"));

var report = await sut.WaitAsync(start.Value, CancellationToken.None);
Assert.Equal("r-1", report.Value.ReportId);
```

`FailWorkflow(workflowId, error)` makes `GetResultAsync` return that `Error` instead.

### 2. Assert a signal and its payload

```csharp
var handle = (InMemoryWorkflowHandle)dispatcher.GetHandle(workflowId, runId: null, TenantScope.For(Tenant));

await new ApprovalService(dispatcher).ApproveAsync(workflowId, Tenant, CancellationToken.None);

var approval = handle.ShouldHaveSignalled<ApprovalGiven>("approve");
Assert.Equal("alice", approval.ApprovedBy);
```

### 3. Answer a query

```csharp
dispatcher.ConfigureQueryHandler(workflowId, "progress", () => 75);

var progress = await handle.QueryAsync<int>("progress", CancellationToken.None);   // Success(75)
handle.ShouldHaveBeenQueried("progress");
```

An unconfigured query name returns `workflow.query_failed`.

### 4. Assert cancellation or termination

```csharp
await sut.AbortAsync(workflowId, CancellationToken.None);

handle.ShouldHaveBeenTerminated(expectedReason: "operator abort");
Assert.Equal(WorkflowLifecycleStatus.Terminated, handle.Status);
```

### 5. Test the Temporal-outage path

```csharp
dispatcher.SimulateFailure = true;   // StartAsync and DescribeAsync → workflow.service_unavailable

var result = await onboarding.StartAsync(Tenant, CancellationToken.None);

Assert.Equal("workflow.service_unavailable", result.Error.Code);
dispatcher.ShouldNotHaveStarted<OnboardingWorkflow>();
```

Set `SimulateFailure` on a handle to fail its signal, cancel and terminate calls the same way; queries are unaffected.

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddInMemoryWorkflowDispatcher(this IServiceCollection)` | `IWorkflowDispatcher` → `InMemoryWorkflowDispatcher` (singleton) |

Only the interface is registered: resolve `IWorkflowDispatcher` and cast it to `InMemoryWorkflowDispatcher` to assert.

### Types

| Type | Implements / purpose |
| --- | --- |
| `InMemoryWorkflowDispatcher(IWorkflowIdFactory? workflowIdFactory = null)` | `IWorkflowDispatcher` — all three `StartAsync` overloads, both `GetHandle` overloads, `DescribeAsync` |
| `InMemoryWorkflowHandle` / `InMemoryWorkflowHandle<TResult>` | `IWorkflowHandle` / `IWorkflowHandle<TResult>` |
| `InMemoryWorkflowStartRecord(WorkflowTypeName, WorkflowId, TenantScope, Args)` | One recorded start; `Args` is `null` for a no-argument start |
| `WorkflowLifecycleStatus` | `Running`, `Completed`, `Cancelled`, `Terminated`, `Failed` |

### Dispatcher test helpers

| Member | Purpose |
| --- | --- |
| `StartedWorkflows` | Every successful start, in order |
| `ShouldHaveStarted<TWorkflow>()` / `ShouldHaveStartedOnce<TWorkflow>()` | The first / the only start of that type; throws otherwise |
| `ShouldNotHaveStarted<TWorkflow>()` | Throws if one was started |
| `CompleteWorkflow<TResult>(workflowId, result)` / `FailWorkflow(workflowId, error)` | Ends the execution with a result or an `Error` |
| `ConfigureQueryHandler<TQueryResult>(workflowId, queryName, Func<TQueryResult>)` | Answers a query |
| `SimulateFailure`, `Reset()` | Outage simulation; clears executions and starts |

`CompleteWorkflow`, `FailWorkflow` and `ConfigureQueryHandler` throw `InvalidOperationException` for an id that was
never started.

### Handle test helpers

| Member | Purpose |
| --- | --- |
| `Status` | The execution's `WorkflowLifecycleStatus`, or `null` when none exists |
| `SignalsReceived` / `QueriesReceived` | Every signal (name, args) and query name, in order |
| `ShouldHaveSignalled(name)` / `ShouldHaveSignalled<TSignalArgs>(name)` | Returns the first matching signal's args; throws if none |
| `ShouldNotHaveSignalled(name)`, `ShouldHaveBeenQueried(name)` | Signal / query assertions |
| `ShouldHaveBeenCancelled()`, `ShouldHaveBeenTerminated(string? expectedReason = null)` | Status assertions |
| `SimulateFailure` | Fails signal, cancel and terminate |

### Errors

| Code | Returned when |
| --- | --- |
| `workflow.tenant_scope_missing` | `TenantScope.Global` on `StartAsync` or `DescribeAsync` |
| `workflow.invalid_workflow_id` | A blank `BusinessKey` |
| `workflow.already_started` | The composed id is still running |
| `workflow.not_found` | An operation on an id with no execution |
| `workflow.query_failed` | No handler configured for the query name |
| `workflow.service_unavailable` | `SimulateFailure` is set |

## Testing

This package is the test double; its own self-tests live in
[`SharedKernel.Workflows.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Workflows.Testing/SharedKernel.Workflows.Testing.Tests),
which prove the dispatcher and handles against the `IWorkflowDispatcher`/`IWorkflowHandle` contract of
[`SharedKernel.Workflows.Temporal`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Workflows/SharedKernel.Workflows.Temporal/README.md).
Pair it with [`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Testing/README.md)
for `TestRequestContext` and `FakeClock`. Workflow and activity code itself is tested with Temporal's own test
environment, which runs the real worker and replays history.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference this package from a production project | Reference it from test projects only | `TestingNeverReferencedByProduction` fails the build's architecture tests |
| Expect the workflow's code to run | Script the outcome with `CompleteWorkflow` / `FailWorkflow` | The double records executions; it never executes them |
| Call `GetResultAsync` before scripting the outcome | Call `CompleteWorkflow` or `FailWorkflow` first | It throws `InvalidOperationException` instead of waiting |
| Rely on `IdReusePolicy` or `IdConflictPolicy` behaviour | Test those policies against a Temporal test server | The double only rejects a start while the same id is running |
| Resolve `InMemoryWorkflowDispatcher` from DI | Resolve `IWorkflowDispatcher` and cast | Only the interface is registered |
| Register a scoped `IWorkflowIdFactory` next to `AddInMemoryWorkflowDispatcher()` | Register it as a singleton, or pass it to the constructor | The singleton dispatcher would capture a scoped dependency |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · [16.Testing domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/README.md) · [MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
