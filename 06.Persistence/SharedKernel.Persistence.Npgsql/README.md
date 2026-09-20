# SharedKernel.Persistence.Npgsql

Shared Npgsql infrastructure for Platform.SharedKernel microservices: one `NpgsqlDataSource`, `NpgsqlConnectionFactory` (the `IDbConnectionFactory` both EF Core and Dapper consume), PostgreSQL advisory-lock primitives (`IMigrationLock`/`IAdvisoryTransactionLock`), and `ITenantSessionBinder` for row-level security. This is the one place `Npgsql`/`Npgsql.EntityFrameworkCore.PostgreSQL` connection concerns live — `SharedKernel.Persistence.EfCore` never references Npgsql directly.

## Included types

- `AddSharedKernelNpgsql()` — registers one `NpgsqlDataSource`, `IDbConnectionFactory`, `IMigrationLock`, `ITenantSessionBinder`, and `IAdvisoryTransactionLock`; a second overload registers a keyed, independently-configured second database for a service that talks to more than one PostgreSQL instance
- `NpgsqlConnectionFactory` — sealed `IDbConnectionFactory`; returns an open `NpgsqlConnection`, caller disposes
- `NpgsqlAdvisoryMigrationLock` — session-level `pg_advisory_lock`, guards startup migrations/seeders across replicas
- `NpgsqlAdvisoryTransactionLock` — transaction-level `pg_advisory_xact_lock`, auto-releases on commit/rollback
- `NpgsqlTenantSessionBinder` — implements `ITenantSessionBinder`; sets/resets the `app.tenant_id`/`app.cross_tenant` session variables row-level-security policies read
- `NpgsqlPersistenceOptions` / `NpgsqlPersistenceOptionsValidator` — connection string, pool sizing, periodic-password-provider options, validated at startup
- `NpgsqlTelemetryNames` — the well-known `"Npgsql"` `ActivitySource`/`Meter` name, for wiring `Npgsql.OpenTelemetry` if a consumer opts in

## Install

```xml
<ProjectReference Include="..\SharedKernel.Persistence.Npgsql\SharedKernel.Persistence.Npgsql.csproj" />
```

## Quick start

```csharp
services.AddSharedKernelNpgsql(configuration);   // section "SharedKernel:Persistence:Npgsql"

// A second, independently-configured database:
services.AddSharedKernelNpgsql(
    configuration.GetSection("SharedKernel:Persistence:Npgsql:Reporting"),
    name: "reporting");
```

`SharedKernel.Persistence.PostgreSQL`'s `UsePostgreSQL()` (EF Core) and `SharedKernel.Persistence.Dapper`'s `AddSharedKernelDapper()` both build on the `IDbConnectionFactory`/`NpgsqlDataSource` this package registers — call `AddSharedKernelNpgsql()` once at startup before either.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [06.Persistence/CLAUDE.md](../CLAUDE.md) for the full interface contracts and implementation rules.
