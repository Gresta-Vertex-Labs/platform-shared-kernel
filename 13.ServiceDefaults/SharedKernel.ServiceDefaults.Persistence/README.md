# SharedKernel.ServiceDefaults.Persistence

Database readiness checks for EF Core and for connection-factory access such as Dapper. One of the `SharedKernel.ServiceDefaults.*` integration packages: add it only if your
service has this dependency.

## Usage

```xml
<PackageReference Include="SharedKernel.ServiceDefaults" />
<PackageReference Include="SharedKernel.ServiceDefaults.Persistence" />
```

```csharp
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;

builder.AddServiceDefaults();

builder.Services.AddHealthChecks()
    .AddDatabaseReadinessCheck<AppDbContext>()      // EF Core
    // or
    .AddDapperDatabaseReadinessCheck()                // IDbConnectionFactory;

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
| Methods | `AddDatabaseReadinessCheck<TContext>()`, `AddDapperDatabaseReadinessCheck()` |
| Default name | `HealthCheckNames.Database` (`"database"`) |
| Tags | `ready`, `db` — never `live` |
| On failure | `Unhealthy` |
| Resolves | `TContext` (a `SharedKernelDbContext`), or `IDbConnectionFactory` — register your persistence first |

The check is tagged `ready` and never `live`, so it gates load-balancer rotation through
`/health/ready` without ever causing Kubernetes to restart the pod through `/health/live`.

## Why a separate package

Brings `SharedKernel.Persistence.EfCore` and, with it, EF Core. It is one package rather than separate EF Core and Dapper packages because a PostgreSQL service restores EF Core regardless — `SharedKernel.Persistence.PostgreSQL` depends on `SharedKernel.Persistence.EfCore` — so a split would save no one anything.

The types keep their `SharedKernel.ServiceDefaults.HealthChecks` namespace from before the WO-084
split, so moving to this package changes a `PackageReference` and no source.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the
[SharedKernel.ServiceDefaults README](../SharedKernel.ServiceDefaults/README.md) for the composition
base and the full list of integration packages.
