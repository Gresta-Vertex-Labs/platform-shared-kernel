<div align="center">

# SharedKernel Workflows

**Durable, crash-proof business processes for multi-tenant .NET services on Temporal — start a process that may
run for thirty days, survive every deploy and restart on the way, and never run the same process twice for the
same business key.**

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Temporal](https://img.shields.io/badge/Temporalio-1.17-000000)](SharedKernel.Workflows.Temporal/README.md)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](../LICENSE)
![Packages: 1](https://img.shields.io/badge/packages-1-informational)

[Package](#the-package) · [Quick start](#quick-start) · [Guarantees](#what-you-can-rely-on) · [Readiness](#readiness) · [Testing](#testing)

</div>

---

## The package

| Package | Tier | What it is |
| --- | --- | --- |
| [`SharedKernel.Workflows.Temporal`](SharedKernel.Workflows.Temporal/README.md) | Adapter | The dispatch surface (`IWorkflowDispatcher`, `IWorkflowHandle<TResult>`, `IWorkflowIdFactory`), the authoring bases (`WorkflowBase`, `ActivityBase`, `CommandActivity<TCommand>`), worker hosting, context propagation, `Result<T>`↔Temporal-failure mapping, AES-256-GCM payload encryption and the `workflows` readiness probe |

```xml
<PackageReference Include="SharedKernel.Workflows.Temporal" />
```

It is deliberately **one** package: durable execution's programming model — determinism, replay,
`Workflow.Patched` versioning — *is* the abstraction, and no other engine is swap-compatible. Use it when a process
is "do A, then wait up to 30 days for B, then do C or compensate". For "run this job every night, exactly once
across replicas", use [`19.Scheduling`](../19.Scheduling/SharedKernel.Scheduling/README.md); a scheduled job that
*starts* a workflow is a fine combination of both.

---

## Quick start

```csharp
// An API service that only dispatches — the most common registration:
builder.Services
    .AddSharedKernelTemporalWorkflows(builder.Configuration)   // "Workflows:Temporal"
    .AsClientOnly()
    .WithPayloadEncryption()
    .Build();

builder.Services.AddHealthChecks().AddSharedKernelReadiness();   // maps the "workflows" probe
```

```csharp
public sealed class OrderService(IWorkflowDispatcher workflows)
{
    public Task<Result<IWorkflowHandle>> StartFulfilmentAsync(string orderId, TenantId tenantId, CancellationToken ct) =>
        workflows.StartAsync<OrderFulfilmentWorkflow, string>(
            orderId,
            new WorkflowStartOptions
            {
                TaskQueue = "orders-fulfilment",
                BusinessKey = orderId,                           // the workflow id is {tenant}:{type}:{key}
                IdReusePolicy = WorkflowIdReusePolicy.RejectDuplicate,
                IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
            },
            TenantScope.For(tenantId),                           // SharedKernel.Execution.Tenancy — mandatory
            ct);
}
```

The worker-hosting service registers `.AddWorkflow<T>()`, `.AddActivities<T>()` and `.WithWorker(taskQueue)`
instead of `.AsClientOnly()`. The package README has the full walkthrough, the determinism rules and the
`Workflow.Patched` versioning procedure.

---

## What you can rely on

- **Workflow code is replay code.** Clocks, randomness, I/O and DI belong in activities; `WorkflowBase` exposes
  `Workflow.UtcNow`, `Workflow.NewGuid()` and the replay-aware logger instead. Analyzer `SK0028` flags
  non-deterministic API use inside a `[Workflow]` type.
- **A workflow id is an idempotency key.** Every start goes through `IWorkflowIdFactory`, which puts the tenant in
  the id; the same business key cannot start a second running execution.
- **Tenant scope is mandatory.** Every dispatch member takes `TenantScope`; `TenantScope.Global` returns
  `WorkflowErrors.TenantScopeMissing` before any I/O.
- **The caller travels with the work.** The dispatching call's correlation id, actor and client, and the
  workflow's tenant, ride in Temporal headers to every activity and child workflow. Each activity runs inside a
  `RequestContextScope`, so `IRequestContext` and the activity's own REST, gRPC and bus calls carry the same
  tenant and correlation id. The propagated caller is attribution only — it grants no permission.
- **Expected failures never retry.** Validation, not-found, conflict and authorization errors map to
  non-retryable Temporal failures; only `Unexpected` is retried. An activity that swallows a failed `Result` is a
  bug the test suite checks for.
- **Commands run through your pipeline.** `CommandActivity<TCommand>` sends a command through the kernel `ISender`
  — the same pipeline behaviours an HTTP request gets — with one closed generic class per command, no reflection.
- **Payloads can be encrypted at rest in Temporal's history**, bound to the workflow id so ciphertext cannot be
  replayed into another execution.

---

## Readiness

`AddSharedKernelTemporalWorkflows` registers one `IReadinessProbe` named `workflows`: ready when the Temporal
service answers, the namespace is addressable and, on a worker host, every worker is still polling. A task-queue
backlog never fails it.

---

## Testing

Code that dispatches workflows is unit-tested with
[`SharedKernel.Workflows.Testing`](../16.Testing/SharedKernel.Workflows.Testing/README.md)'s
`InMemoryWorkflowDispatcher`. Workflow and activity code is tested with Temporal's own `WorkflowEnvironment`
(time-skipping) and `WorkflowReplayer` (history-replay determinism); no container is needed.

---

## Further reading

- [`CLAUDE.md`](CLAUDE.md) — interface contracts, the full determinism prohibition list, the failure-mapping table.
- [`state-map.md`](state-map.md) — phase history (WO-046, WO-081, WO-086).
