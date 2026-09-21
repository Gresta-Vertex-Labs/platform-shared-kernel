# SharedKernel.ServiceDefaults.Persistence

Readiness checks for the `06.Persistence` stack: the database (EF Core or a connection factory such as Dapper), startup migrations, the field-encryption key ring and the audit sealer. One of the `SharedKernel.ServiceDefaults.*` integration packages: add it only if your
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
    .AddDatabaseReadinessCheck<AppDbContext>()      // EF Core; Unhealthy until startup migrations and seeders finished
    .AddPersistenceStartupReadinessCheck()          // IPersistenceStartup alone (e.g. a Dapper-only service with a migrating context elsewhere)
    .AddFieldEncryptionReadinessCheck()             // with UseFieldEncryption(): the key ring is loaded and fresh
    .AddAuditSealingReadinessCheck();               // with UseAuditTrail(): Degraded when the sealer lags more than 5 minutes
// a Dapper-only service: .AddDapperDatabaseReadinessCheck()   (IDbConnectionFactory)

var app = builder.Build();
app.MapDefaultHealthCheckEndpoints();
```

Chain onto `AddHealthChecks()`, **not** `AddSharedKernelHealthChecks()`. `AddServiceDefaults()`
already calls `AddSharedKernelHealthChecks()`, which registers the `"startup"` check; calling it a
second time registers `"startup"` twice, and the application throws `ArgumentException: Duplicate
health checks were registered with the name(s): startup` when it starts.

## Behaviour

| Method | Default name (`HealthCheckNames`) | Tags | Reports |
| --- | --- | --- | --- |
| `AddDatabaseReadinessCheck<TContext>()` | `"database"` | `ready`, `db` | Unhealthy until `IPersistenceStartup` completed, then when `TContext` cannot reach the database |
| `AddDapperDatabaseReadinessCheck()` | `"database-dapper"` | `ready`, `db` | Unhealthy when `IDbConnectionFactory` cannot connect |
| `AddPersistenceStartupReadinessCheck()` | `"persistence-startup"` | `ready`, `db` | Unhealthy until startup migrations and seeders finished |
| `AddFieldEncryptionReadinessCheck()` | `"field-encryption"` | `ready`, `encryption-key-provider` | Unhealthy when the field-encryption key ring is stale or its provider is unreachable |
| `AddAuditSealingReadinessCheck(maxLag)` | `"audit-sealing"` | `ready`, `db` | Degraded when the oldest unsealed audit record is older than `maxLag` (default 5 minutes); Unhealthy only when the probe fails |

Each resolves what its package registers (`AddSharedKernelPostgres`, `UseFieldEncryption`, `UseAuditTrail`, `AddSharedKernelNpgsql`) — register persistence first. The names differ, so the EF Core and Dapper checks can both be added.

Every check is tagged `ready` and never `live`, so it gates load-balancer rotation through
`/health/ready` without ever causing Kubernetes to restart the pod through `/health/live`.

## Why a separate package

Brings `SharedKernel.Persistence.EfCore`, `.EfCore.Encryption` and `.EfCore.Auditing` (for their probes) and, with them, EF Core. It is one package rather than one per capability because the probes are small and a PostgreSQL service on this stack restores EF Core regardless.

The types keep their `SharedKernel.ServiceDefaults.HealthChecks` namespace from before the WO-084
split, so moving to this package changes a `PackageReference` and no source.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the
[SharedKernel.ServiceDefaults README](../SharedKernel.ServiceDefaults/README.md) for the composition
base and the full list of integration packages.
