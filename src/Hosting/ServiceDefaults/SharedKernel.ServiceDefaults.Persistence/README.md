# SharedKernel.ServiceDefaults.Persistence

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Host](https://img.shields.io/badge/tier-Host-d73a49)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **Database readiness for the `06.Persistence` stack: `/health/ready` stays unhealthy until startup migrations and
> seeders have finished, and while the database cannot be reached — for EF Core, Dapper, or both.**

| You get | So that |
| --- | --- |
| `AddDatabaseReadinessCheck<TContext>()` | A pod takes traffic only after its migrations ran and its `DbContext` reaches PostgreSQL |
| `AddDapperDatabaseReadinessCheck()` | A Dapper-only service gets the same check through `IDbConnectionFactory` |
| `AddPersistenceStartupReadinessCheck()` | The startup gate alone, for a service with no EF Core check |
| Checks tagged `ready` and `db`, never `live` | A database outage takes the pod out of rotation without Kubernetes restarting it |

## Install

```xml
<PackageReference Include="SharedKernel.ServiceDefaults.Persistence" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Host — reference it from your **Api** / **Worker** project |
| Depends on | `SharedKernel.ServiceDefaults`, `SharedKernel.Persistence.Abstractions`, `SharedKernel.Persistence.EfCore` |
| Namespaces | `SharedKernel.ServiceDefaults.HealthChecks` |

Add it only if your service has a database: it brings EF Core, which is why it is not part of the composition base.

## Quick start

```csharp
using SharedKernel.Persistence;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;

builder.AddServiceDefaults();
builder.AddSharedKernelPostgres<OrdersDbContext>("orders", p => p.MigrateOnStartup());

builder.Services.AddHealthChecks()
    .AddDatabaseReadinessCheck<OrdersDbContext>()   // Unhealthy until migrations and seeders finished
    .AddSharedKernelReadiness();                    // field-encryption, audit-sealing and every other probe

var app = builder.Build();
app.MapDefaultHealthCheckEndpoints();
```

Variants: `.AddDapperDatabaseReadinessCheck()` for a Dapper-only service (`IDbConnectionFactory`), and
`.AddPersistenceStartupReadinessCheck()` for `IPersistenceStartup` alone.

## How it works

| Method | Default name (`HealthCheckNames`) | Tags | Reports Unhealthy when |
| --- | --- | --- | --- |
| `AddDatabaseReadinessCheck<TContext>()` | `database` | `ready`, `db` | `IPersistenceStartup` (if registered) has not completed, or `TContext` cannot reach the database |
| `AddDapperDatabaseReadinessCheck()` | `database-dapper` | `ready`, `db` | `IDbConnectionFactory` cannot connect |
| `AddPersistenceStartupReadinessCheck()` | `persistence-startup` | `ready`, `db` | Startup migrations and seeders have not completed |

- Each check resolves what `AddSharedKernelPostgres` (or `AddSharedKernelNpgsql`) registers, when the check first runs.
- The names differ, so the EF Core and Dapper checks can both be added; pass `name` to add one per context.
- Registration is logged through `HealthCheckRegistrationLogging` (EventId 13002).
- Field encryption and the audit sealer need nothing from this package: `UseFieldEncryption()` and `UseAuditTrail()`
  register the `field-encryption` and `audit-sealing` readiness probes, which `AddSharedKernelReadiness()` maps.

## Recipes

### 1. Two contexts on one service

```csharp
builder.Services.AddHealthChecks()
    .AddDatabaseReadinessCheck<OrdersDbContext>("database-orders")
    .AddDatabaseReadinessCheck<ReportingDbContext>("database-reporting");
```

## Reference

| Method | Registers |
| --- | --- |
| `IHealthChecksBuilder.AddDatabaseReadinessCheck<TContext>(string name = "database")` | The EF Core check; `TContext : SharedKernelDbContext` |
| `IHealthChecksBuilder.AddDapperDatabaseReadinessCheck(string name = "database-dapper")` | The `IDbConnectionFactory` check |
| `IHealthChecksBuilder.AddPersistenceStartupReadinessCheck(string name = "persistence-startup")` | The startup-completion check |

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 13002 | Information | Health check registered (name, tags) — from the base's `HealthCheckRegistrationLogging` |

## Testing

Test against a real PostgreSQL with [`SharedKernel.Persistence.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Persistence.Testing/README.md)'s
`PostgresTestServer`/`PostgresTestDatabase`, host the service with `WebApplicationFactory<Program>`, and request
`/health/ready`. [`SharedKernel.ServiceDefaults.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.ServiceDefaults.Testing/README.md)'s
`ShouldBeTaggedReady()` / `ShouldNotBeTaggedLive()` assert a registration's tags without a database.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Chain these onto `AddSharedKernelHealthChecks()` | Chain onto `builder.Services.AddHealthChecks()` | `AddServiceDefaults()` already called it; a second call registers `startup` twice and the host throws |
| Tag a database check `live` | Keep the defaults (`ready`, `db`) | A database outage would make Kubernetes restart healthy pods |
| Add the check before persistence is registered and forget it | Register `AddSharedKernelPostgres` / `AddSharedKernelNpgsql` too | The check resolves `TContext` / `IDbConnectionFactory` when it runs |
| Add two checks with the same name | Pass a distinct `name` per context | Duplicate names fail health-check resolution |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[ServiceDefaults domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/ServiceDefaults/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
