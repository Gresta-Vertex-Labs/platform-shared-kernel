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

| Phase key | Phase | Status |
| --- | --- | --- |
| `SK.06.PackageContextMigration` | Migrate another package's `DbContext` on the migrator connection | ○ |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

### SK.06.PackageContextMigration — Migrate another package's DbContext on the migrator connection `○`

**Work order:** WO-090 / P-630 · **Depends on:** None

**Verdict: accept (the existing seam does not cover it).** Evidence: `MigrateOnStartup()` is a method of `EfCorePersistenceBuilder<TContext>` and registers `MigrationAndSeedHostedService<TContext>`, which is constrained to `SharedKernelDbContext` and applies only the service's own registered context. `AddEfCoreIdempotency` registers `IdempotencyDbContext` (a plain sealed `DbContext`) with `AddDbContext`, so it cannot be passed to `AddSharedKernelPostgres`; a seeder on the service's context cannot migrate it either (a seeder's context runs on the application connection, not the migration data source). Nothing else migrates it, so under the role split (runtime role without `CREATE`) the only option today is a hand-written startup migration on the runtime connection, which fails with permission denied.

Goal: one additive, opt-in registration, `services.AddMigrationOnStartup<TContext>(TimeSpan? lockTimeout = null)` (namespace `SharedKernel.Persistence`, `where TContext : DbContext`, in `SharedKernel.Persistence.EfCore`), that applies the pending migrations of a context a kernel package owns. It reuses the machinery of the service's own startup migration: the same `IMigrationLock` (advisory lock keyed by the context's full name, default 2 minutes), the migration data source (`NpgsqlDataSourceKeys.Migration`, the same lookup `MigrateOnStartup` uses) with the same fallback to the context's own connection when `MigrationConnectionString` is not configured, the same `PersistenceStartupSignal` (so `IPersistenceStartup` and the readiness check wait for it) and the same EfCore log ids. It runs migrations only (no seeders, no cross-tenant scope, no request caller). Nothing changes for an existing registration: the method is new, the hosted service is registered only when it is called, and a service that created the schema by hand and never calls it behaves as in 1.0.0.

**What `SK.18.*` (P-632, `Idempotency.EfCore`) may rely on once this phase is done:**

1. The public method above (signature ratified by D-01; PublicAPI additive). It is the only supported way; P-632 must call it (behind its own opt-in, default off) and must not call `Database.MigrateAsync` itself.
2. Migrations run on the `NpgsqlDataSourceKeys.Migration` data source when the host's `MigrationConnectionString` is configured, never on the runtime connection; with no migration data source the context's own connection is used (single-role deployments keep working). The lookup needs `AddSharedKernelPostgres`/`AddSharedKernelNpgsql` in the same container; without it there is no lock and Error 6013 is logged, exactly as for `MigrateOnStartup`.
3. One replica at a time under `IMigrationLock` (key = `typeof(TContext).FullName`); a lock timeout or any migration failure fails host start and faults `IPersistenceStartup`. Readiness stays not-ready until it completes.
4. The context is resolved from a fresh DI scope, so it must be non-pooled (`AddDbContext`, as P-632 does) and must not derive `SharedKernelDbContext` (that path is `MigrateOnStartup()`); both are rejected at start with a message naming the remedy. The migrations are those EF Core finds for the context's options: the context's own assembly by default (package-shipped migrations) or the `MigrationsAssembly` the caller set.
5. The context must use its own history table: a `(schema, table)` equal to EF Core's default (`public`/`__EFMigrationsHistory`) is rejected at start, because it would share history rows with the service's own context. P-632 chooses the name (for example `__ef_migrations_history_idempotency`) in its `UseNpgsql`/`UsePostgres` options callback.
6. Registering the same `TContext` twice throws (like `AddSharedKernelPostgres`).
7. Grants are the canonical role script's: tables created by `app_migrator` in `public` reach `app_runtime` through its `ALTER DEFAULT PRIVILEGES`. Nothing P-632 ships may add DDL grants to the runtime role. Not provided and owned by P-632: the migrations and design-time factory, the history-table choice, the opt-in flag, its README and its own tests.

| ID | Task | Package | State |
| --- | --- | --- | :---: |
| D-01 | Ratify the shape: `IServiceCollection.AddMigrationOnStartup<TContext>(TimeSpan? lockTimeout = null) where TContext : DbContext`, in `SharedKernel.Persistence` (registration namespace), duplicate registration throws, `SharedKernelDbContext`-derived and pooled contexts rejected | SharedKernel.Persistence.EfCore | `○` |
| D-02 | Ratify the shared runner: extract the lock, signal and log sequence of `MigrationAndSeedHostedService<TContext>` into one internal helper used by it and by the new `ContextMigrationHostedService<TContext>`; behavior of the existing service is unchanged (no new EventIds; 6001, 6003-6006 and 6013 are reused with the context type name) | SharedKernel.Persistence.EfCore | `○` |
| D-03 | Ratify the history-table rule (decision 5 above) and its start-time validation (`ValidateOnStart`, no connection string or host in the message) | SharedKernel.Persistence.EfCore | `○` |
| C-01 | `AddMigrationOnStartup<TContext>`: marker-based duplicate guard, argument and lock-timeout checks, `PersistenceStartupSignalRegistration.GetOrAdd` + `Expect(typeof(TContext))`, hosted-service registration | SharedKernel.Persistence.EfCore | `○` |
| C-02 | `ContextMigrationHostedService<TContext>` (internal, `where TContext : DbContext`): acquire `IMigrationLock`, resolve the context from a fresh scope, `SetDbConnection(migrationDataSource.CreateConnection(), contextOwnsConnection: true)` when the migration data source exists, `MigrateAsync`, complete/fail/cancel the signal; release the lock in `finally` | SharedKernel.Persistence.EfCore | `○` |
| C-03 | Start-time validator: context not `SharedKernelDbContext`, not pooled, history table is not the EF default; messages end in a period and name the remedy (E3) | SharedKernel.Persistence.EfCore | `○` |
| C-04 | Refactor `MigrationAndSeedHostedService<TContext>` onto the shared runner (D-02) with no behavior change | SharedKernel.Persistence.EfCore | `○` |
| C-05 | Record the new member in `PublicAPI.Unshipped.txt` with XML docs on the method | SharedKernel.Persistence.EfCore | `○` |
| T-01 | Registration guards: null arguments, non-positive lock timeout, duplicate `TContext`, `SharedKernelDbContext`-derived and pooled contexts, default history table all fail with the documented message; no hosted service is registered when the method is not called (a 1.0.0-shaped `AddSharedKernelPostgres` registration is byte-for-byte unchanged); existing `MigrationAndSeedHostedService` tests stay green after C-04 (Unit lane) | SharedKernel.Persistence.EfCore.Tests | `○` |
| T-02 | Real role split: `app_migrator` (owns the schema), `app_runtime` (`CREATE` revoked on `public`, DML through default privileges); a test-only plain `DbContext` with a hand-written migration and its own history table; host start with `MigrationConnectionString` applies it, the table owner is `app_migrator`, the history row exists, the runtime role reads and writes the table and fails `CREATE TABLE` with 42501 (Integration lane) | SharedKernel.Persistence.EfCore.Integration.Tests | `○` |
| T-03 | Not on the runtime connection: the same host without `MigrationConnectionString` under the role split fails start with the permission error and faults `IPersistenceStartup`; with a command-level capture on the migration data source the DDL is seen there and never on the runtime data source (Integration lane) | SharedKernel.Persistence.EfCore.Integration.Tests | `○` |
| T-04 | Cross-replica serialization with real concurrency: two hosts start against one database at once; the migration's `Up` runs once, both hosts complete, the lock is taken serially; a held external lock on the same key keeps `IPersistenceStartup.IsCompleted` false until released, and a short `lockTimeout` fails start with `TimeoutException` (Integration lane) | SharedKernel.Persistence.EfCore.Integration.Tests | `○` |
| T-05 | Coexistence: a service context registered with `AddSharedKernelPostgres(...).MigrateOnStartup()` and a package context with the new method migrate side by side in one database with separate history tables; readiness waits for both; a package context left on the default history table fails start (Integration lane) | SharedKernel.Persistence.EfCore.Integration.Tests | `○` |
| T-06 | Hand-created schema: a database whose table was created by hand, with the method never called, starts exactly as before and the package context is untouched (regression for the compatibility claim) (Integration lane) | SharedKernel.Persistence.EfCore.Integration.Tests | `○` |
| DO-01 | EfCore README: a recipe next to `MigrateOnStartup` for migrating a package-supplied context (when to use it, the history-table rule, fallback connection, readiness), plus the grants the migrator role needs (`CREATE` on the target schema, ownership of what it creates) and the runtime role gets (`USAGE` plus default privileges; a non-public schema needs both repeated for that schema); `PublicAPI` table row | SharedKernel.Persistence.EfCore | `○` |
| DO-02 | Npgsql README role script: one note that a package-supplied context needs no extra grants beyond `ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator`, and that tables created before the defaults existed need an explicit `GRANT` | SharedKernel.Persistence.Npgsql | `○` |
| DO-03 | Compile the README recipe in `ReadmeSample`-style test coverage if one exists for EfCore; otherwise cover it by T-02 (cross-domain note: `PersistenceReadmeSampleTests` lives in `13.ServiceDefaults`) | SharedKernel.Persistence.EfCore.Integration.Tests | `○` |
| T-07 | ConsumerVerify: add the registration to the composed scenario on the packed packages (Integration lane) | SharedKernel.Persistence.ConsumerVerify | `○` |

**Acceptance:**
- [ ] A package-supplied `DbContext` registered for migration runs once across replicas, before `IPersistenceStartup` completes, on the migration data source, and is never applied on the runtime connection when `MigrationConnectionString` is configured.
- [ ] The EfCore README documents the recipe beside `MigrateOnStartup`, with the migrator and runtime grants.
- [ ] The role-split integration test proves the runtime role cannot create the objects and the migrator role can.
- [ ] No existing registration changes behavior; `PublicAPI.Unshipped.txt` shows an addition only; no new EventId.
- [ ] The seven points listed above are true and covered by a test, so P-632 can depend on them.

## Blocked

None.

## Cross-Domain Dependencies

- **Outbound to 18.Idempotency (P-632):** `Idempotency.EfCore` consumes `AddMigrationOnStartup<IdempotencyDbContext>` behind an opt-in default-off option and must supply a non-default history table and the package's migrations; see the seven reliance points in `SK.06.PackageContextMigration`. P-632 depends on this phase being done (API and behavior); it may be planned against the ratified D-01 signature.
- **Note to 13.ServiceDefaults:** no change required; `AddPersistenceStartupReadinessCheck` already waits on `IPersistenceStartup`, which the new registration joins.
