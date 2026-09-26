# SharedKernel.ServiceDefaults.Persistence

Database readiness checks for the `06.Persistence` stack: the database itself (EF Core or a connection factory such
as Dapper) and startup migrations. **Tier: Host.** Add it only if your service has a database.

Field encryption and the audit sealer need nothing from this package: `UseFieldEncryption()` and `UseAuditTrail()`
register the `field-encryption` and `audit-sealing` readiness probes, and the base's `AddSharedKernelReadiness()` maps
them.

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
    .AddDatabaseReadinessCheck<AppDbContext>()      // EF Core; Unhealthy until startup migrations and seeders finished
    .AddSharedKernelReadiness();                    // field-encryption, audit-sealing and every other provider probe
// a Dapper-only service:            .AddDapperDatabaseReadinessCheck()        (IDbConnectionFactory)
// a service with no EF Core check:  .AddPersistenceStartupReadinessCheck()    (IPersistenceStartup alone)

var app = builder.Build();
app.MapDefaultHealthCheckEndpoints();
```

Chain onto `AddHealthChecks()`, **not** `AddSharedKernelHealthChecks()`. `AddServiceDefaults()` already calls
`AddSharedKernelHealthChecks()`, which registers the `"startup"` check; a second call registers it twice and the host
throws `ArgumentException: Duplicate health checks were registered with the name(s): startup` when it starts.

## Behaviour

| Method | Default name (`HealthCheckNames`) | Tags | Reports |
| --- | --- | --- | --- |
| `AddDatabaseReadinessCheck<TContext>()` | `"database"` | `ready`, `db` | Unhealthy until `IPersistenceStartup` completed, then when `TContext` cannot reach the database |
| `AddDapperDatabaseReadinessCheck()` | `"database-dapper"` | `ready`, `db` | Unhealthy when `IDbConnectionFactory` cannot connect |
| `AddPersistenceStartupReadinessCheck()` | `"persistence-startup"` | `ready`, `db` | Unhealthy until startup migrations and seeders finished |

Each resolves what `AddSharedKernelPostgres` (or `AddSharedKernelNpgsql`) registers — register persistence first. The
names differ, so the EF Core and Dapper checks can both be added.

Every check is tagged `ready` and never `live`, so it gates load-balancer rotation through `/health/ready` without
ever causing Kubernetes to restart the pod through `/health/live`.

| Probe (via `AddSharedKernelReadiness()`) | Registered by | Reports |
| --- | --- | --- |
| `field-encryption` | `UseFieldEncryption()` | Unhealthy when the key ring is stale or its provider is unreachable |
| `audit-sealing` | `UseAuditTrail()` | Degraded when the oldest unsealed audit record is older than `AuditSealerOptions.MaxReadyLag` |

## Why a separate package

It brings `SharedKernel.Persistence.EfCore` and, with it, EF Core. A service without a database must not restore
either, so this cannot live in the composition base.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the
[SharedKernel.ServiceDefaults README](../SharedKernel.ServiceDefaults/README.md) for the composition base.
