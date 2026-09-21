# SharedKernel.Persistence.Npgsql

The PostgreSQL connection layer shared by `SharedKernel.Persistence.EfCore` and `SharedKernel.Persistence.Dapper`: one options-bound `NpgsqlDataSource` per database, secondary data sources (read replica, migrations, cross-tenant role), advisory locks, the tenant binding used by row-level security, and the SQLSTATE classifier. No EF Core dependency.

## Register

```csharp
// ConnectionStrings:orders (Aspire / Testcontainers shape) + optional SharedKernel:Persistence:Npgsql settings
services.AddSharedKernelNpgsql(builder.Configuration, "orders");

// or everything in SharedKernel:Persistence:Npgsql (ConnectionString, or ConnectionStringName)
services.AddSharedKernelNpgsql(builder.Configuration);
```

Registers `NpgsqlDataSource`, `IDbConnectionFactory`, `IMigrationLock`, `IAdvisoryTransactionLock`, `ITenantSessionBinder`, the keyed data sources of `NpgsqlDataSourceKeys`, and the row-level security startup check. Options are validated when the host starts, not at the first query.

```json
"SharedKernel": { "Persistence": { "Npgsql": {
  "ConnectionStringName": "orders",
  "StatementTimeoutMilliseconds": 30000,
  "MigrationConnectionString": "Host=db-direct;...",
  "ReadOnlyConnectionString": "Host=db-replica;...",
  "UseVector": true,
  "RowLevelSecurity": { "Enabled": true, "CrossTenantConnectionString": "Host=db;Username=app_cross_tenant;...", "PrivilegeCheck": "Fail" }
} } }
```

A second database: `services.AddSharedKernelNpgsql(configuration.GetSection("SharedKernel:Persistence:Reporting"), name: "reporting")`, resolved with `[FromKeyedServices("reporting")]`.

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

EF Core needs the same enum mapped on its provider options as well.

## TLS

| Connection string / options | Result |
|---|---|
| nothing set | `VerifyFull` (server certificate and host name verified) |
| `SSL Mode=...` in the connection string | honoured |
| `SslMode` option | overrides the connection string |
| below `VerifyFull`, host is `localhost`/`127.0.0.1`/`::1`/Unix socket | accepted |
| below `VerifyFull`, `Development` environment | accepted, warning logged |
| below `VerifyFull`, anything else | startup fails unless `AcknowledgeInsecureSslMode: true` (warning logged) |

Managed databases: keep `VerifyFull` and trust the provider's root CA — AWS RDS: download `global-bundle.pem` and add `Root Certificate=/certs/global-bundle.pem`; Azure Database for PostgreSQL: its roots (DigiCert Global Root G2, Microsoft RSA Root CA 2017) are public CAs from the standard `ca-certificates` bundle; if your image lacks them, point `Root Certificate` at the downloaded PEM.

## PgBouncer and poolers

Transaction-mode PgBouncer (also Azure Flexible Server's built-in pooler, Supabase) is supported: every tenant binding is transaction-local (`set_config(..., true)`), so nothing survives a transaction. Two things need a real server session and must bypass the pooler:

- migrations and the startup migration lock — set `MigrationConnectionString` to the database directly;
- `Multiplexing` and `No Reset On Close` are refused when row-level security is on.

Multi-host / read replicas: a connection string with several hosts (`Host=primary,standby`) gives a multi-host data source; read-only Dapper sessions use it with `TargetSessionAttributes=PreferStandby`. Or set `ReadOnlyConnectionString`. Replicas lag — read your own writes from the primary.

## Row-level security

`WithRowLevelSecurity()` on the EF Core builder (or `RowLevelSecurity:Enabled` for a Dapper-only service) binds the caller's tenant to every transaction; the policy from `EnableTenantRowLevelSecurity` (EF Core migrations) is a single predicate, `tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid`, for `USING` and `WITH CHECK`, and uses the tenant index. No tenant bound means no rows.

RLS protects against application bugs (a missing filter, `IgnoreQueryFilters()`, a hand-written query). It does not stop SQL injection: injected SQL runs as the application role and can bind any tenant. Keep SQL parameterized.

A superuser, a role with `BYPASSRLS`, and a table owner bypass RLS. At startup the application role is checked (`PrivilegeCheck`: `Fail` by default, `Warn`, `Disabled`); `RowLevelSecurityPrivileges.CheckAsync(connection)` runs the same check on demand.

### Roles

```sql
-- Owner of the schema; runs migrations. Never used by the application at runtime.
CREATE ROLE app_migrator LOGIN PASSWORD '...';
GRANT CREATE, USAGE ON SCHEMA public TO app_migrator;

-- The application: no superuser, no BYPASSRLS, owns nothing.
CREATE ROLE app_runtime LOGIN PASSWORD '...' NOSUPERUSER NOBYPASSRLS;
GRANT USAGE ON SCHEMA public TO app_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO app_runtime;

-- Cross-tenant work (reports, back office, maintenance jobs). Its own credentials; app_runtime is NOT a member.
CREATE ROLE app_cross_tenant LOGIN PASSWORD '...' BYPASSRLS;   -- or NOBYPASSRLS + EnableTenantRowLevelSecurity(..., crossTenantRole: "app_cross_tenant")
GRANT USAGE ON SCHEMA public TO app_cross_tenant;
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_cross_tenant;
```

`ConnectionString` connects as `app_runtime`, `MigrationConnectionString` as `app_migrator`, `RowLevelSecurity:CrossTenantConnectionString` as `app_cross_tenant`. Inside an active `ICrossTenantScope`, Dapper sessions open on the cross-tenant data source automatically; an EF Core context calls `context.Database.UseCrossTenantConnection()`.

## Advisory locks

Lock names are namespaced so SharedKernel locks never collide with each other or with yours:

```csharp
AdvisoryLockKeys.Migration("OrderDbContext")  // "sk:migration:OrderDbContext" (IMigrationLock adds it itself)
AdvisoryLockKeys.Audit("sealer")              // "sk:audit:sealer"
AdvisoryLockKeys.ToKey(name)                  // the bigint for pg_advisory_* (FNV-1a 64, stable)
```

`IAdvisoryTransactionLock.AcquireAsync(connection, transaction, name, timeout)` hashes the name as given (pass a namespaced one); a timeout applies to the acquisition only — the transaction's previous `lock_timeout` is restored — and never rounds down to "wait forever".

## Errors

`PostgresExceptionClassifier.Classify(exception)` maps SQLSTATEs to the platform `Error` (unique → Conflict, foreign key → Validation/Conflict, RLS/privilege → Forbidden, serialization/deadlock/lock or statement timeout → transient Conflict, ...). For non-EF code:

```csharp
Result<int> result = await PostgresErrorMapping.TryAsync(() => connection.ExecuteAsync(sql, args));
```

## Telemetry

Npgsql emits its own `ActivitySource`/`Meter` named `"Npgsql"`; `WithPersistenceTelemetry` adds them.
