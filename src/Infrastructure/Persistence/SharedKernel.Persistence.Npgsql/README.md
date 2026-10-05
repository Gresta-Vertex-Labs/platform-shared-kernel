# SharedKernel.Persistence.Npgsql

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-6f42c1)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
![PostgreSQL 15+](https://img.shields.io/badge/PostgreSQL-15%2B-4169E1?logo=postgresql&logoColor=white)

> **The PostgreSQL connection layer under every SharedKernel persistence package: one configuration shape, TLS
> `VerifyFull` by default, the canonical database roles, row-level security binding with a startup privilege check,
> advisory locks and SQLSTATE → `Error` classification. No EF Core dependency.**

| You get | So that |
| --- | --- |
| One `NpgsqlDataSource` per connection name from `ConnectionStrings:{name}` + `SharedKernel:Persistence:{name}` | EF Core and Dapper share one pool, one TLS policy and one database identity |
| Keyed data sources for migrations, a read replica and cross-tenant work | Each kind of work runs as the right role on the right server |
| TLS `VerifyFull` for remote hosts; insecure modes need an acknowledgement | A plain-text production connection cannot happen by accident |
| Transaction-local tenant binding and a startup privilege check | Row-level security holds behind PgBouncer and refuses a role that could bypass it |
| The one canonical role script | Migrations, RLS, the audit ledger and field encryption agree on who may do what |
| `PostgresErrorMapping.TryAsync` / `PostgresExceptionClassifier` | Constraint, RLS and serialization errors become platform `Error` values |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Configuration](#configuration)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.Persistence.Npgsql" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Adapter — reference it from your **Infrastructure** project |
| Database | PostgreSQL 15 or later |
| Depends on | Npgsql 10, `Pgvector`, `SharedKernel.Persistence.Abstractions`, `SharedKernel.Configuration` |
| Namespaces | `SharedKernel.Persistence` (registration), `SharedKernel.Persistence.Npgsql.Options`, `.Errors`, `.Connections`, `.Coordination` |

A service using EF Core does not reference or call this package directly: `AddSharedKernelPostgres<TContext>("orders")`
from [`SharedKernel.Persistence.EfCore`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Persistence/SharedKernel.Persistence.EfCore/README.md)
brings and registers it. Only a Dapper-only or plain ADO.NET service calls `AddSharedKernelNpgsql` itself.

## Quick start

```csharp
using SharedKernel.Persistence;

// ConnectionStrings:orders + optional settings in SharedKernel:Persistence:orders
builder.Services.AddSharedKernelNpgsql(builder.Configuration, "orders");
```

```json
{
  "ConnectionStrings": { "orders": "Host=db;Database=orders;Username=app_runtime;Password=..." },
  "SharedKernel": {
    "Persistence": {
      "orders": {
        "StatementTimeoutMilliseconds": 30000,
        "MigrationConnectionString": "Host=db-direct;Database=orders;Username=app_migrator;Password=...",
        "ReadOnlyConnectionString": "Host=db-replica;Database=orders;Username=app_runtime;Password=...",
        "RowLevelSecurity": {
          "Enabled": true,
          "CrossTenantConnectionString": "Host=db;Database=orders;Username=app_cross_tenant;Password=..."
        }
      }
    }
  }
}
```

This registers `NpgsqlDataSource`, `IDbConnectionFactory`, `IMigrationLock`, `IAdvisoryTransactionLock`,
`ITenantSessionBinder`, the keyed data sources of `NpgsqlDataSourceKeys` and the row-level security startup check.
Options are validated when the host starts. Inject `IDbConnectionFactory` for plain ADO.NET, or add
[`SharedKernel.Persistence.Dapper`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Persistence/SharedKernel.Persistence.Dapper/README.md).

## How it works

**One shape.** Every database has a connection name; its connection string is `ConnectionStrings:{name}` and every
other setting is in `SharedKernel:Persistence:{name}` — whichever package registers it. A `ConnectionString` key in the
settings section overrides `ConnectionStrings:{name}`. The package sections sit next to the connection sections, so
`Encryption`, `Auditing`, `Dapper` and `Npgsql` cannot be connection names.

| Registration | Connection string | Settings |
| --- | --- | --- |
| `AddSharedKernelPostgres<OrderDbContext>("orders")` | `ConnectionStrings:orders` | `SharedKernel:Persistence:orders` |
| `AddSharedKernelNpgsql(configuration, "orders")` | `ConnectionStrings:orders` | `SharedKernel:Persistence:orders` |
| `AddSharedKernelNpgsql(section, "reporting")` (keyed `"reporting"`) | `section:ConnectionString`, else `ConnectionStrings:{ConnectionStringName}` | `section` |
| `AddSharedKernelNpgsql(configuration)` (unnamed) | `SharedKernel:Persistence:Npgsql:ConnectionString` | `SharedKernel:Persistence:Npgsql` |

**TLS.** The effective mode is `SslMode` when set, else the connection string's `SSL Mode`, else `Disable` for a
loopback host (`localhost`, `127.0.0.1`, `::1`, a Unix socket) and `VerifyFull` for any other. Below `VerifyFull` is
accepted for loopback, accepted with a warning (6300) in `Development`, and otherwise fails startup unless
`AcknowledgeInsecureSslMode` is `true`. `Persist Security Info` is always off. GSSAPI transport encryption is off unless
the connection string sets `GSS Encryption Mode` (Npgsql's default probe costs a round trip and logs a missing
Kerberos library in standard ASP.NET images). For managed databases keep `VerifyFull` and trust the provider's root CA
(`Root Certificate=/certs/global-bundle.pem` for AWS RDS; Azure's roots are in the standard CA bundle).

**Row-level security.** When enabled (`UseMultiTenancy(rowLevelSecurity: true)` on the EF Core builder, or
`RowLevelSecurity:Enabled` for a Dapper-only service) the caller's `IRequestContext.TenantId` is bound to every
transaction with `set_config('app.tenant_id', …, true)` — transaction-local, so safe behind transaction-mode PgBouncer.
The policy predicate is `tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid` for `USING` and
`WITH CHECK`: no tenant bound, no rows. RLS protects against application bugs (a missing filter, a hand-written query);
it does not stop SQL injection, which runs as the application role — keep SQL parameterized.

At startup the application role (default and read-only data sources) is checked. A problem is any of: a superuser or
`BYPASSRLS` role; a role that owns, or is a member of the owner of, an RLS table; a permissive policy that does not read
the tenant applying to the role, to `PUBLIC` or to a role it is a member of (typically `app_runtime` made a member of
the cross-tenant role). `PrivilegeCheck` decides: `Fail` (default, the host does not start), `Warn` (6304), `Disabled`.

**Poolers and replicas.** Migrations and the migration lock need a real server session: point
`MigrationConnectionString` past the pooler. `Multiplexing` and `No Reset On Close` are refused with RLS on. A
multi-host connection string (`Host=primary,standby`) gives read-only sessions `PreferStandby`; or set
`ReadOnlyConnectionString`. Replicas lag — read your own writes from the primary.

**Telemetry.** Npgsql's own `ActivitySource`/`Meter` named `"Npgsql"` are added by `WithPersistenceTelemetry()`. The
data sources built here log Npgsql's per-command `Information` event at `Debug`, so an `Information` minimum level does
not log every statement.

## Recipes

### 1. Create the roles (the canonical script)

The whole platform — EF Core migrations, row-level security, the audit ledger, field encryption — expects these roles.
Run once, as an administrator:

```sql
-- Owns every table, runs migrations (MigrationConnectionString). Never used by the application at runtime.
CREATE ROLE app_migrator LOGIN PASSWORD '...' NOSUPERUSER;
-- The application (ConnectionStrings:{name}): no superuser, no BYPASSRLS, owns nothing, member of nothing below.
CREATE ROLE app_runtime LOGIN PASSWORD '...' NOSUPERUSER NOBYPASSRLS;
-- Optional: cross-tenant work under RLS (reports, back office, encryption maintenance). Its own login.
CREATE ROLE app_cross_tenant LOGIN PASSWORD '...' NOSUPERUSER BYPASSRLS;
-- Optional, recommended with the audit trail: the audit sealer (Auditing: Sealer:DataSourceName).
CREATE ROLE app_audit_sealer LOGIN PASSWORD '...' NOSUPERUSER NOBYPASSRLS;

CREATE DATABASE orders OWNER app_migrator;
\c orders
GRANT CONNECT ON DATABASE orders TO app_runtime, app_cross_tenant, app_audit_sealer;
GRANT USAGE ON SCHEMA public TO app_runtime, app_cross_tenant, app_audit_sealer;

-- Every table and sequence app_migrator creates from now on:
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_runtime, app_cross_tenant;
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO app_runtime, app_cross_tenant;
```

| Role | Connection | Can | Cannot |
| --- | --- | --- | --- |
| `app_migrator` | `MigrationConnectionString` | Own and change the schema | — (never used at runtime) |
| `app_runtime` | `ConnectionStrings:{name}` | Read and write the caller's tenant | See another tenant, bypass RLS, change the schema, rewrite the audit ledger |
| `app_cross_tenant` | `RowLevelSecurity:CrossTenantConnectionString` | Read and write every tenant, inside an `ICrossTenantScope` | Rewrite the audit ledger |
| `app_audit_sealer` | The sealer's data source | Read audit records, append chain links and checkpoints | Anything else |

Then, in the EF Core migrations (run as `app_migrator`):

```csharp
migrationBuilder.EnableTenantRowLevelSecurityForModel(TargetModel!);             // every tenant table of the model
// Audit ledger: revokes what the default privileges above granted on its tables (UPDATE, DELETE) and grants exactly
// what the request path, the sealer and the startup self-check expect.
migrationBuilder.CreateAuditLedgerTable(runtimeRole: "app_runtime", sealerRole: "app_audit_sealer");
migrationBuilder.Sql("REVOKE UPDATE, DELETE, TRUNCATE ON audit_records, audit_record_payloads, audit_chain_links, audit_checkpoints FROM app_cross_tenant;");
// Field encryption with tenant data keys: a tombstone must not be deletable.
migrationBuilder.CreateTenantEncryptionKeyTable();
migrationBuilder.Sql("REVOKE DELETE, TRUNCATE ON sk_tenant_encryption_keys FROM app_runtime, app_cross_tenant;");
```

Without a separate sealer, call `CreateAuditLedgerTable(runtimeRole: "app_runtime")`: the runtime role then also gets
`INSERT` on the link and checkpoint tables. Instead of `BYPASSRLS`, the cross-tenant role can be `NOBYPASSRLS` with a
role-specific policy: `EnableTenantRowLevelSecurity("orders", crossTenantRole: "app_cross_tenant")` — and `app_runtime`
must never be a member of it (the startup check reports it). Inside an active `ICrossTenantScope`, Dapper sessions open
on the cross-tenant data source automatically; an EF Core context calls `context.Database.UseCrossTenantConnection()`.

### 2. Create a database for local development

Nothing in the platform creates the database: `MigrateOnStartup()` migrates one that exists.

```bash
docker run -d --name orders-db -p 5432:5432 -e POSTGRES_PASSWORD=dev -e POSTGRES_DB=orders postgres:17
```

`ConnectionStrings:orders` = `Host=localhost;Database=orders;Username=postgres;Password=dev` (a loopback host needs no
TLS settings). The [BillingApi sample](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/samples/BillingApi/README.md)
ships a Docker Compose file that runs the role script on first start.

### 3. Map enums or use short-lived tokens

The last parameter configures every data source the registration builds, with DI available:

```csharp
builder.Services.AddSharedKernelNpgsql(builder.Configuration, "orders", (sp, dataSource) =>
{
    dataSource.MapEnum<OrderStatus>("order_status");                  // ADO-level (Dapper, raw SQL)
    dataSource.UsePeriodicPasswordProvider(
        (_, ct) => sp.GetRequiredService<TokenSource>().GetAsync(ct), // Entra ID / RDS IAM token
        TimeSpan.FromMinutes(45), TimeSpan.FromSeconds(5));
});
```

EF Core needs the same enum on its provider options too (`ConfigureDataSource` on the `AddSharedKernelPostgres` builder).

### 4. A second database

```csharp
builder.Services.AddSharedKernelNpgsql(builder.Configuration.GetSection("SharedKernel:Persistence:reporting"), "reporting");
// resolve with [FromKeyedServices("reporting")] NpgsqlDataSource
```

### 5. Map database errors in non-EF code

```csharp
Result<int> result = await PostgresErrorMapping.TryAsync(() => connection.ExecuteAsync(sql, args));
if (result.IsFailure && result.Error.Code == PostgresClassifiedErrorCodes.UniqueViolation) { /* … */ }
```

## Configuration

Settings of a named database, `SharedKernel:Persistence:{name}` (`NpgsqlPersistenceOptions`), validated at startup:

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `ConnectionStrings:{name}` | `string` | — (required unless `ConnectionString` is set) | The runtime role's connection string |
| `SharedKernel:Persistence:{name}:ConnectionString` | `string` | `""` | Overrides `ConnectionStrings:{name}` |
| `SharedKernel:Persistence:{name}:ConnectionStringName` | `string?` | the connection name | Name under `ConnectionStrings` to read |
| `SharedKernel:Persistence:{name}:SslMode` | `SslMode?` | `null` (see TLS) | Overrides the connection string's `SSL Mode` |
| `SharedKernel:Persistence:{name}:AcknowledgeInsecureSslMode` | `bool` | `false` | Required below `VerifyFull` for a remote host outside Development |
| `SharedKernel:Persistence:{name}:StatementTimeoutMilliseconds` | `int?` | server default | `statement_timeout` of every connection |
| `SharedKernel:Persistence:{name}:LockTimeoutMilliseconds` | `int?` | server default | `lock_timeout` |
| `SharedKernel:Persistence:{name}:IdleInTransactionSessionTimeoutMilliseconds` | `int?` | server default | `idle_in_transaction_session_timeout` |
| `SharedKernel:Persistence:{name}:UseVector` | `bool` | `false` | pgvector type mapping |
| `SharedKernel:Persistence:{name}:EnableDynamicJson` | `bool` | `false` | Npgsql's reflection-based dynamic JSON |
| `SharedKernel:Persistence:{name}:MigrationConnectionString` | `string?` | `null` | Owner role, direct to the server; migrations and the migration lock |
| `SharedKernel:Persistence:{name}:ReadOnlyConnectionString` | `string?` | `null` | Replica for read-only sessions |
| `SharedKernel:Persistence:{name}:RowLevelSecurity:Enabled` | `bool` | `false` | Bind the tenant in every transaction (set in code by `UseMultiTenancy(rowLevelSecurity: true)`) |
| `SharedKernel:Persistence:{name}:RowLevelSecurity:CrossTenantConnectionString` | `string?` | `null` | Cross-tenant role; without it cross-tenant work under RLS is refused |
| `SharedKernel:Persistence:{name}:RowLevelSecurity:PrivilegeCheck` | `Fail` \| `Warn` \| `Disabled` | `Fail` | Outcome of a failed startup privilege check |

## Reference

| Method / type | Does |
| --- | --- |
| `AddSharedKernelNpgsql(IConfiguration, string name, Action<IServiceProvider, NpgsqlDataSourceBuilder>?)` | Named database: `ConnectionStrings:{name}` + `SharedKernel:Persistence:{name}` |
| `AddSharedKernelNpgsql(IConfigurationSection, string name, …)` | Keyed database from an explicit section |
| `AddSharedKernelNpgsql(IConfiguration, …)` | Unnamed database from `SharedKernel:Persistence:Npgsql` |
| `NpgsqlDataSourceKeys` | `Migration`, `ReadOnly`, `CrossTenant` — keys of the secondary data sources |
| `AdvisoryLockKeys` | `Migration(name)` → `sk:migration:{name}`, `Audit(name)` → `sk:audit:{name}`, `ToKey(name)` → stable 64-bit FNV-1a key |
| `IAdvisoryTransactionLock.AcquireAsync(connection, transaction, lockKey, timeout)` | Transaction-scoped advisory lock; the timeout bounds acquisition only and the previous `lock_timeout` is restored |
| `PostgresErrorMapping.TryAsync(...)` | Runs an operation and returns `Result`/`Result<T>`, mapping PostgreSQL errors |
| `PostgresExceptionClassifier.Classify(exception)` | The classification behind the mapping |

### Errors

| PostgreSQL | Type | Code (`PostgresClassifiedErrorCodes`) |
| --- | --- | --- |
| `23505` unique violation | Conflict | `persistence.postgresql.unique_violation` |
| `23503` foreign key violation | Validation or Conflict | `…foreign_key_reference_missing`, `…foreign_key_dependent_exists`, `…foreign_key_violation` |
| `23502`, `23514`, `23P01`, `22001` | Validation | `…not_null_violation`, `…check_violation`, `…exclusion_violation`, `…value_too_long` |
| `40001`, `40P01` / `55P03` / `57014` | Conflict (transient) | `…transient_conflict` / `…lock_timeout` / `…statement_timeout` |
| `42501` (incl. RLS `WITH CHECK`) | Forbidden | `…insufficient_privilege` |

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 6300 | Warning | A data source uses an SSL mode below `VerifyFull` |
| 6301 | Information | Advisory migration lock acquired |
| 6302 | Warning | Advisory migration lock not acquired within the timeout |
| 6303 | Information | Advisory migration lock released |
| 6304 | Warning | Row-level security privilege check failed |
| 6305 | Warning | Row-level security privilege check could not run |
| 6306 | Information | Row-level security privilege check passed |

## Testing

[`SharedKernel.Persistence.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Persistence.Testing/README.md)'s
`PostgresTestServer.StartAsync()` starts PostgreSQL in a container and `PostgresTestDatabase` creates a database with
the canonical roles, so RLS claims are tested through an unprivileged role (a superuser bypasses RLS even under
`FORCE`). `FakeDbConnectionFactory` stands in for `IDbConnectionFactory` in unit tests.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Connect the application as a superuser or the table owner | Use `app_runtime` from the role script | The startup check fails: such a role bypasses or disables RLS |
| Make `app_runtime` a member of `app_cross_tenant` | Give the cross-tenant role its own login | Membership lets any SQL `SET ROLE` past tenant isolation |
| Run migrations through a transaction-mode pooler | Set `MigrationConnectionString` to the server directly | Migrations and the migration lock need one server session |
| Use `Multiplexing` or `No Reset On Close` with RLS | Leave them off | Refused at startup; they break transaction-local binding |
| Silence TLS errors with `SSL Mode=Require` in production | Keep `VerifyFull` and install the provider's root CA | Below `VerifyFull` needs an explicit acknowledgement for a reason |
| Name a database `Encryption`, `Auditing`, `Dapper` or `Npgsql` | Pick another connection name | Those sections belong to the packages |
| Put a connection string literal in code | `ConnectionStrings:{name}` from configuration or a secret store | The password never belongs in source |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Persistence domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Persistence/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
