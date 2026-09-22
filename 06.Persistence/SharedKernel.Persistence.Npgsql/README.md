# SharedKernel.Persistence.Npgsql

The PostgreSQL connection layer shared by `SharedKernel.Persistence.EfCore` and `SharedKernel.Persistence.Dapper`: one options-bound `NpgsqlDataSource` per database, secondary data sources (read replica, migrations, cross-tenant role), advisory locks, the tenant binding used by row-level security, and the SQLSTATE classifier. No EF Core dependency.

A service using EF Core does not call this package directly: `AddSharedKernelPostgres<TContext>("orders")` registers it. A Dapper-only service calls `AddSharedKernelNpgsql` itself.

## Register

```csharp
// ConnectionStrings:orders (Aspire / Testcontainers shape) + optional settings in SharedKernel:Persistence:orders
services.AddSharedKernelNpgsql(builder.Configuration, "orders");
```

Registers `NpgsqlDataSource`, `IDbConnectionFactory`, `IMigrationLock`, `IAdvisoryTransactionLock`, `ITenantSessionBinder`, the keyed data sources of `NpgsqlDataSourceKeys`, and the row-level security startup check. Options are validated when the host starts, not at the first query.

## Configuration: one shape

Every database has a **connection name**. Its connection string is `ConnectionStrings:{name}`; every other setting is in `SharedKernel:Persistence:{name}`. The same rule applies whichever package registers the database:

| Registration | Connection string | Settings (`NpgsqlPersistenceOptions`) |
|---|---|---|
| `AddSharedKernelPostgres<OrderDbContext>("orders")` (EF Core) | `ConnectionStrings:orders` | `SharedKernel:Persistence:orders` |
| a second context, `AddSharedKernelPostgres<ReportDbContext>("reporting")` | `ConnectionStrings:reporting` | `SharedKernel:Persistence:reporting` (keyed data source `"reporting"`) |
| `AddSharedKernelNpgsql(configuration, "orders")` (Dapper-only) | `ConnectionStrings:orders` | `SharedKernel:Persistence:orders` |
| `AddSharedKernelNpgsql(section, "reporting")` (keyed, explicit section) | `section:ConnectionString`, else `ConnectionStrings:{ConnectionStringName}` | `section` |
| `AddSharedKernelNpgsql(configuration)` (no name) | `SharedKernel:Persistence:Npgsql:ConnectionString` | `SharedKernel:Persistence:Npgsql` |

A `ConnectionString` key in the settings section overrides `ConnectionStrings:{name}`. The package-level sections sit next to the connection sections — `SharedKernel:Persistence:Encryption`, `SharedKernel:Persistence:Auditing`, `SharedKernel:Persistence:Dapper`, and `SharedKernel:Persistence:ServiceName` — so `Encryption`, `Auditing`, `Dapper` and `Npgsql` are not usable as connection names.

```json
"ConnectionStrings": { "orders": "Host=db;Database=orders;Username=app_runtime;Password=..." },
"SharedKernel": { "Persistence": { "orders": {
  "StatementTimeoutMilliseconds": 30000,
  "MigrationConnectionString": "Host=db-direct;Database=orders;Username=app_migrator;Password=...",
  "ReadOnlyConnectionString": "Host=db-replica;Database=orders;Username=app_runtime;Password=...",
  "UseVector": true,
  "RowLevelSecurity": { "Enabled": true, "CrossTenantConnectionString": "Host=db;Database=orders;Username=app_cross_tenant;Password=...", "PrivilegeCheck": "Fail" }
} } }
```

A second Dapper database: `services.AddSharedKernelNpgsql(configuration.GetSection("SharedKernel:Persistence:reporting"), "reporting")`, resolved with `[FromKeyedServices("reporting")]`.

### Enums, composites, tokens

The last parameter configures every data source this registration builds, with DI available:

```csharp
services.AddSharedKernelNpgsql(builder.Configuration, "orders", (sp, dataSource) =>
{
    dataSource.MapEnum<OrderStatus>("order_status");                  // ADO-level (Dapper, raw SQL)
    dataSource.UsePeriodicPasswordProvider(
        (_, ct) => sp.GetRequiredService<TokenSource>().GetAsync(ct), // Entra ID / RDS IAM token
        TimeSpan.FromMinutes(45), TimeSpan.FromSeconds(5));
});
```

EF Core needs the same enum mapped on its provider options as well (`ConfigureDataSource` on the `AddSharedKernelPostgres` builder).

## Creating the database

Nothing in the platform creates the database itself: `MigrateOnStartup()` applies migrations to a database that exists, and every registration assumes the database is there. In production an administrator (or your infrastructure code) creates it once, owned by the migration role (see [Roles](#roles-the-one-canonical-script)). For local development:

```bash
# a throwaway server whose database already exists
docker run -d --name orders-db -p 5432:5432 -e POSTGRES_PASSWORD=dev -e POSTGRES_DB=orders postgres:17

# or, against an existing server
psql -h localhost -U postgres -c "CREATE DATABASE orders"
```

`ConnectionStrings:orders` = `Host=localhost;Database=orders;Username=postgres;Password=dev` (a loopback host needs no TLS settings). .NET Aspire's `AddPostgres("pg").AddDatabase("orders")` creates it for you.

## TLS

| Connection string / options | Result |
|---|---|
| nothing set, loopback host (`localhost`, `127.0.0.1`, `::1`, Unix socket) | `Disable` |
| nothing set, any other host | `VerifyFull` (server certificate and host name verified) |
| `SSL Mode=...` in the connection string | honoured |
| `SslMode` option | overrides the connection string |
| below `VerifyFull`, loopback host | accepted |
| below `VerifyFull`, `Development` environment | accepted, warning logged |
| below `VerifyFull`, anything else | startup fails unless `AcknowledgeInsecureSslMode: true` (warning logged) |

GSSAPI (Kerberos) transport encryption is off unless the connection string sets `GSS Encryption Mode`: Npgsql's own
default probes for it on every new connection, which costs a round trip and, in images without the Kerberos library
(the standard ASP.NET images), prints `libgssapi_krb5.so.2: cannot open shared object file`.

Managed databases: keep `VerifyFull` and trust the provider's root CA — AWS RDS: download `global-bundle.pem` and add `Root Certificate=/certs/global-bundle.pem`; Azure Database for PostgreSQL: its roots (DigiCert Global Root G2, Microsoft RSA Root CA 2017) are public CAs from the standard `ca-certificates` bundle; if your image lacks them, point `Root Certificate` at the downloaded PEM.

## PgBouncer and poolers

Transaction-mode PgBouncer (also Azure Flexible Server's built-in pooler, Supabase) is supported: every tenant binding is transaction-local (`set_config(..., true)`), so nothing survives a transaction. Two things need a real server session and must bypass the pooler:

- migrations and the startup migration lock — set `MigrationConnectionString` to the database directly;
- `Multiplexing` and `No Reset On Close` are refused when row-level security is on.

Multi-host / read replicas: a connection string with several hosts (`Host=primary,standby`) gives a multi-host data source; read-only Dapper sessions use it with `TargetSessionAttributes=PreferStandby`. Or set `ReadOnlyConnectionString`. Replicas lag — read your own writes from the primary.

## Row-level security

`UseMultiTenancy(rowLevelSecurity: true)` on the EF Core builder (or `RowLevelSecurity:Enabled` for a Dapper-only service) binds the caller's tenant to every transaction; the policy from `migrationBuilder.EnableTenantRowLevelSecurity("orders")` (EF Core migrations) is a single predicate, `tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid`, for `USING` and `WITH CHECK`, and uses the tenant index. No tenant bound means no rows.

RLS protects against application bugs (a missing filter, `IgnoreQueryFilters()`, a hand-written query). It does not stop SQL injection: injected SQL runs as the application role and can bind any tenant. Keep SQL parameterized.

At startup the application role (default and read-only data sources) is checked — `PrivilegeCheck`: `Fail` (default), `Warn`, `Disabled`. A problem is any of:

- the role is a superuser or has `BYPASSRLS` (every policy skipped);
- the role owns, or is a member of the owner of, a table with RLS (it can switch RLS off);
- a **permissive policy that does not read the tenant** applies to the role on a protected table — to `PUBLIC`, to the role itself, or to a role it is a member of, inherited or not (it can `SET ROLE`). Typical cause: `app_runtime` was made a member of the cross-tenant role, whose `USING (true)` policy is combined with the tenant policy by `OR`, so the application sees every tenant.

### Roles: the one canonical script

The whole platform — EF Core migrations, row-level security, the audit ledger, field encryption — expects these roles. Run once, as an administrator:

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

Then, in the EF Core migrations (run as `app_migrator`):

```csharp
migrationBuilder.EnableTenantRowLevelSecurity("orders");                       // per tenant table, with RLS
// Audit ledger: revokes what the default privileges above granted on its tables (UPDATE, DELETE) and grants exactly
// what the request path, the sealer and the startup self-check expect.
migrationBuilder.CreateAuditLedgerTable(runtimeRole: "app_runtime", sealerRole: "app_audit_sealer");
migrationBuilder.Sql("REVOKE UPDATE, DELETE, TRUNCATE ON audit_records, audit_record_payloads, audit_chain_links, audit_checkpoints FROM app_cross_tenant;");
// Field encryption with tenant data keys: a tombstone must not be deletable.
migrationBuilder.CreateTenantEncryptionKeyTable();
migrationBuilder.Sql("REVOKE DELETE, TRUNCATE ON sk_tenant_encryption_keys FROM app_runtime, app_cross_tenant;");
```

Without a separate sealer, call `CreateAuditLedgerTable(runtimeRole: "app_runtime")`: the runtime role then also gets `INSERT` on the link and checkpoint tables. Instead of `BYPASSRLS`, the cross-tenant role can be `NOBYPASSRLS` with a role-specific policy: `EnableTenantRowLevelSecurity("orders", crossTenantRole: "app_cross_tenant")` — and `app_runtime` must never be a member of it (the startup check reports it).

`ConnectionStrings:{name}` connects as `app_runtime`, `MigrationConnectionString` as `app_migrator`, `RowLevelSecurity:CrossTenantConnectionString` as `app_cross_tenant`, the audit sealer's data source as `app_audit_sealer`. Inside an active `ICrossTenantScope`, Dapper sessions open on the cross-tenant data source automatically; an EF Core context calls `context.Database.UseCrossTenantConnection()`; encryption maintenance uses it by itself.

## Advisory locks

Lock names are namespaced so SharedKernel locks never collide with each other or with yours:

```csharp
AdvisoryLockKeys.Migration("OrderDbContext")  // "sk:migration:OrderDbContext" (IMigrationLock adds it itself)
AdvisoryLockKeys.Audit("sealer")              // "sk:audit:sealer"
AdvisoryLockKeys.ToKey(name)                  // the bigint for pg_advisory_* (FNV-1a 64, stable)
```

`IAdvisoryTransactionLock.AcquireAsync(connection, transaction, name, timeout)` hashes the name as given (pass a namespaced one); a timeout applies to the acquisition only — the transaction's previous `lock_timeout` is restored — and never rounds down to "wait forever".

## Errors

`PostgresExceptionClassifier.Classify(exception)` maps SQLSTATEs to the platform `Error` (unique → Conflict, foreign key → Validation/Conflict, RLS/privilege → Forbidden, serialization/deadlock/lock or statement timeout → transient Conflict, ...), with the codes in `PostgresClassifiedErrorCodes` (`persistence.postgresql.unique_violation`, ...). For non-EF code:

```csharp
Result<int> result = await PostgresErrorMapping.TryAsync(() => connection.ExecuteAsync(sql, args));
if (result.IsFailure && result.Error.Code == PostgresClassifiedErrorCodes.UniqueViolation) { ... }
```

`PostgresClassifiedErrorCodes` holds platform error codes; the SQLSTATEs themselves are Npgsql's `PostgresErrorCodes`.

## Telemetry and logging

Npgsql emits its own `ActivitySource`/`Meter` named `"Npgsql"`; `WithPersistenceTelemetry` adds them. Npgsql logs every executed command ("Command execution completed ...: {CommandText}") at `Information`; the data sources this package builds write that one log at `Debug` instead, so an `Information` minimum level does not log every statement. To see them, enable `Debug` for the `Npgsql.Command` category.
