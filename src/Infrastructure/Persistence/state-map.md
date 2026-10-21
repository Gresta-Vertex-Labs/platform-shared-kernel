# 06.Persistence — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Persistence.Abstractions` | Abstractions | ● | ORM-free: repositories, `EntityVersion`, bulk mutations, `ICrossTenantScope`, `IDbConnectionFactory`; implements Execution's `IUnitOfWork`/`IAuditTrailWriter` contracts, never redeclares them. |
| `SharedKernel.Persistence.Npgsql` | Adapter | ● | Data sources, TLS (`VerifyFull` by default), the canonical role script, SQLSTATE classifier, advisory locks, tenant session binding. |
| `SharedKernel.Persistence.EfCore` | Adapter | ● | `builder.AddSharedKernelPostgres<TContext>(…)` — conventions, one transaction per DI scope, tenant filter + write guard + transaction-local RLS, migrations/seeding; absorbed the former `.PostgreSQL` package (P-558). Edge → Npgsql. |
| `SharedKernel.Persistence.Dapper` | Adapter | ● | `IDbSessionFactory` sessions joining the unit of work, parameterized SQL only, type handlers incl. `TenantId`. Edge → Npgsql. |
| `SharedKernel.Persistence.EfCore.Auditing` | Adapter | ● | HMAC-chained append-only audit ledger v3, background sealer, `audit-sealing` readiness probe. Edge → EfCore. |
| `SharedKernel.Persistence.EfCore.Encryption` | Adapter | ● | Field encryption v3 with blind indexes, rotation and crypto-shredding, `field-encryption` readiness probe. Edge → EfCore. |

## Phase Key Registry

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

- **Outbound to 18.Idempotency (P-632):** `AddMigrationOnStartup<TContext>(TimeSpan? lockTimeout = null)` is available (`where TContext : DbContext`, namespace `SharedKernel.Persistence`, in `Persistence.EfCore`). `Idempotency.EfCore` must call it behind its own opt-in (default off), register `IdempotencyDbContext` with `AddDbContext` (non-pooled, not a `SharedKernelDbContext`), supply a non-default history table and the package's migrations and design-time factory, and never call `Database.MigrateAsync` itself. Migrations run on the migration data source when `MigrationConnectionString` is configured (the context's own connection otherwise), one replica at a time under `IMigrationLock` keyed by the context's full name; a lock timeout or failed migration fails host start and faults `IPersistenceStartup`. Grants are the canonical role script's (default privileges of `app_migrator`); add no DDL grants to the runtime role.
