# SharedKernel.Workflows.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**`InMemoryWorkflowDispatcher` implements `SharedKernel.Workflows.Temporal`'s `IWorkflowDispatcher` without a
Temporal server.** Starts, signals, queries, cancellations and terminations are recorded per tenant, and the test
completes or fails a workflow to drive the code that awaits it.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.Workflows.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespace: `SharedKernel.Testing.Workflows`.

## Contents

| Type | Stands in for | Notes |
| --- | --- | --- |
| `InMemoryWorkflowDispatcher` | `IWorkflowDispatcher` | `StartAsync` (all three overloads), `GetHandle`, `DescribeAsync`, all taking the mandatory `TenantScope` (`SharedKernel.Execution.Tenancy`). Workflow ids use the production format `{tenant}:{workflowType}:{businessKey}` (or the `IWorkflowIdFactory` you pass), so the tenant is part of the id as in production. `StartedWorkflows`, `ShouldHaveStarted<TWorkflow>()`, `ShouldHaveStartedOnce<TWorkflow>()`, `ShouldNotHaveStarted<TWorkflow>()`; `CompleteWorkflow(id, result)`, `FailWorkflow(id, error)`, `ConfigureQueryHandler(id, name, handler)`; `SimulateFailure`; `Reset()` |
| `InMemoryWorkflowHandle` / `InMemoryWorkflowHandle<TResult>` | `IWorkflowHandle` / `IWorkflowHandle<TResult>` | `SignalAsync`, `QueryAsync`, `CancelAsync`, `TerminateAsync`, `GetResultAsync`; `Status` (`WorkflowLifecycleStatus`), `SignalsReceived`, `QueriesReceived`, `ShouldHaveSignalled(name)`, `ShouldHaveBeenCancelled()`, `ShouldHaveBeenTerminated(reason?)`, `ShouldHaveBeenQueried(name)` |
| `InMemoryWorkflowStartRecord` | — | `WorkflowTypeName`, `WorkflowId`, `TenantScope`, `Args` of one start |

Nothing is replayed and no workflow code runs: this double tests the **caller** of the dispatcher. Test workflow and
activity code with Temporal's own test environment.

## Registration

```csharp
services.AddInMemoryWorkflowDispatcher();
```

Registers one singleton `InMemoryWorkflowDispatcher` as `IWorkflowDispatcher` (production registers it scoped); the
recorded history has to outlive the system under test's scope so the test can assert afterwards.

## Example

```csharp
var dispatcher = new InMemoryWorkflowDispatcher();
var onboarding = new TenantOnboarding(dispatcher);          // the code under test

await onboarding.StartAsync(tenantId, ct);

var start = dispatcher.ShouldHaveStartedOnce<OnboardingWorkflow>();
start.TenantScope.Should().Be(TenantScope.For(tenantId));

dispatcher.CompleteWorkflow(start.WorkflowId, new OnboardingResult(Succeeded: true));
```

## Related packages

- References `SharedKernel.Workflows.Temporal` (the dispatch contracts ship there; there is no separate abstractions
  package).
- [`SharedKernel.Testing`](../SharedKernel.Testing/README.md) — `FakeClock`, `InMemoryLogger`, `TestRequestContext`.
