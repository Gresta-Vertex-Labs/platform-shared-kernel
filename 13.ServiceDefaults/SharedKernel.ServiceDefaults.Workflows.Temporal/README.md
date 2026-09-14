# SharedKernel.ServiceDefaults.Workflows.Temporal

Temporal workflow-service readiness check. One of the `SharedKernel.ServiceDefaults.*` integration packages: add it only if your
service has this dependency.

## Usage

```xml
<PackageReference Include="SharedKernel.ServiceDefaults" />
<PackageReference Include="SharedKernel.ServiceDefaults.Workflows.Temporal" />
```

```csharp
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;

builder.AddServiceDefaults();

builder.Services.AddHealthChecks()
    .AddWorkflowReadinessCheck();

var app = builder.Build();
app.MapDefaultHealthCheckEndpoints();
```

Chain onto `AddHealthChecks()`, **not** `AddSharedKernelHealthChecks()`. `AddServiceDefaults()`
already calls `AddSharedKernelHealthChecks()`, which registers the `"startup"` check; calling it a
second time registers `"startup"` twice, and the application throws `ArgumentException: Duplicate
health checks were registered with the name(s): startup` when it starts.

## Behaviour

| | |
| --- | --- |
| Method | `AddWorkflowReadinessCheck()` — no arguments |
| Default name | `HealthCheckNames.Workflows` (`"workflows"`) |
| Tags | `ready`, `workflows` — never `live` |
| On failure | `Unhealthy` unless the service is reachable, the namespace addressable, and worker pollers active. A task-queue backlog is reported as data only |
| Resolves | `IWorkflowServiceProbe` |

The check is tagged `ready` and never `live`, so it gates load-balancer rotation through
`/health/ready` without ever causing Kubernetes to restart the pod through `/health/live`.

## Why a separate package

It brings `SharedKernel.Workflows.Temporal` and with it `Temporalio` — the dependency a service without workflows must never restore. This package holds the root layering grant that lets `13.ServiceDefaults` reach `17.Workflows`, and that grant permits `IWorkflowServiceProbe` and `WorkflowServiceHealth` only; `00.Governance` enforces it against this assembly.

The types keep their `SharedKernel.ServiceDefaults.HealthChecks` namespace from before the WO-084
split, so moving to this package changes a `PackageReference` and no source.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the
[SharedKernel.ServiceDefaults README](../SharedKernel.ServiceDefaults/README.md) for the composition
base and the full list of integration packages.
