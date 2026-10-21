# 18.Idempotency — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Idempotency.Abstractions` | Abstractions | ● | The one purpose-keyed `IIdempotencyStore` (`IdempotencyPurpose.Request`/`.Message`), `IdempotencyReservation`, `IdempotencyTenantScope` (`no-tenant`), `AddIdempotencyStore<T>`/`HasIdempotencyStore`/`GetRequiredIdempotencyStore`. Consumed by `05`'s `IdempotencyBehavior` and `07`'s consumer idempotency. |
| `SharedKernel.Idempotency.Redis` | Adapter | ● | `RedisIdempotencyStore` (atomic Lua) for every purpose; `AddRedisIdempotency(p => …, o => …)`; message entries are hashes. Built on `Caching.Redis.Core` (declared adapter edge). |
| `SharedKernel.Idempotency.EfCore` | Adapter | ● | `EfCoreIdempotencyStore` (`INSERT … ON CONFLICT`); `AddEfCoreIdempotency(db => …, p => …, o => …)`; table keyed `(tenant_scope, purpose, key)`. Built on `Persistence.EfCore` (declared adapter edge). |

Test double: `FakeIdempotencyStore` + `AddFakeIdempotencyStore(purposes)` in `src/Infrastructure/Idempotency/SharedKernel.Idempotency.Testing`. Provider tests run in the Integration lane against real Redis/PostgreSQL.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | :---: |
| `SK.18.EfCoreSchemaMigration` | Opt-in shipped migration for the EF Core store's schema, applied on the migrator connection | ○ |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

### SK.18.EfCoreSchemaMigration — Opt-in shipped migration for the EF Core store's schema, applied on the migrator connection `○`

**Work order:** WO-090 / P-632 · **Depends on:** SK.06.PackageContextMigration (P-630)

**Verdict: accept.** `SharedKernel.Idempotency.EfCore` ships no migrations and its README leaves the table to a consumer-written migration; under the migrator/runtime role split the only thing a consumer can write is a startup migration on the runtime connection, which fails with permission denied. This phase ships the package's own initial migration and an opt-in flag that hands `IdempotencyDbContext` to 06's `AddMigrationOnStartup<TContext>`, so the schema is created by `app_migrator` on `NpgsqlDataSourceKeys.Migration` while the store runs as `app_runtime`. Everything is additive: the flag defaults off, so a 1.0.0 consumer (with or without a hand-made table or its own `dotnet ef` migration) sees no change; no stored format changes (rules 3, 12 hold; the table shape in the shipped migration is today's model byte for byte). The phase never calls `Database.MigrateAsync` and adds no DDL grant for the runtime role.

**Design (ratified by D-01 to D-05).**

1. **Opt-in.** Two new `EfCoreIdempotencyOptions` members: `MigrateOnStartup` (`bool`, default `false`) and `MigrationLockTimeout` (`TimeSpan?`, default `null` = persistence's default of 2 minutes; must be positive when set). `AddEfCoreIdempotency` evaluates `configureOptions` once, at registration, on a snapshot instance (the flag decides registration, which options binding cannot do after the fact); the snapshot's values are copied into the `IOptions` registration so the delegate runs exactly once. When `MigrateOnStartup` is true it calls `services.AddMigrationOnStartup<IdempotencyDbContext>(MigrationLockTimeout)` (`SharedKernel.Persistence`) and nothing else; when false it registers nothing new. Binding the section under `ISectionBoundOptions` stays a Known Limitation; a bound value could not flip this flag (documented).
2. **History table, only when opted in.** With the flag on, the context's relational options get `MigrationsHistoryTable("__ef_migrations_history_idempotency")` in the default schema, applied through the same internal options helper that disables retry (extension update, after the caller's `configureDbContext`, so it wins). With the flag off the options are untouched, because a 1.0.0 consumer may run their own migration for this context through DI against EF's default history table and must keep working. The name is a stored format of an opted-in database: renaming it later needs a migration story.
3. **The shipped migration** lives in `SharedKernel.Idempotency.EfCore` (`Migrations/`, an initial migration plus the model snapshot, found through the context's own assembly; a caller-set `MigrationsAssembly` overrides it, documented). Its `Up` is a single `DO` block: when `to_regclass('idempotency_keys')` is null it creates the table (columns, types, lengths and nullability exactly as `IdempotencyKeyRecordConfiguration`, primary key `(tenant_scope, purpose, key)`) and `ix_idempotency_keys_expires_at_utc`; when the relation already exists it does nothing at all (no `ALTER`, no `INSERT`, no index statement). Rationale for the guard being outside `CREATE ... IF NOT EXISTS`: PostgreSQL checks schema `CREATE` and table ownership before the existence test, so an existing table owned by another role would fail the migrator; the `to_regclass` guard touches nothing. The history row is still written by EF, so the next start is a no-op. Shape drift of a hand-made table is not inspected (the store fails on it exactly as it does today); the README says the hand-made table must follow the documented columns. `Down` drops the table (an explicit revert; documented as destructive). The snapshot is the model's, so a later model change generates a normal incremental migration; any such change to the table is a stored-format change (rule 3) and needs its own phase.
4. **Design-time factory** (internal, in the package, for maintainers regenerating the migration; not part of the public API): builds the context with `UsePostgres` from the `SK_IDEMPOTENCY_DESIGN_CONNECTION` environment variable, falling back to a credential-free localhost placeholder; the regeneration command and the factory's role go into `CLAUDE.md` `## Decisions`. The README's consumer-side factory recipe stays valid for services that keep their own migration and is demoted to the "manual fallback" recipe.
5. **Behavior under the role split.** Migrations run on the migration data source when `MigrationConnectionString` is configured, otherwise on the context's own connection (single-role hosts work); the host needs `AddSharedKernelPostgres`/`AddSharedKernelNpgsql` in the same container for the lock (else Error 6013 is logged and no lock is taken, as for `MigrateOnStartup`). The store connection itself is untouched: `UsePostgres(dataSource)` on the runtime data source. Grants are the canonical role script's default privileges (`ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator` reaches `app_runtime` for `idempotency_keys`; the cleanup recipe also needs `DELETE`, which those defaults include). The manual fallback grants `SELECT, INSERT, UPDATE, DELETE` explicitly.

| ID | Task | Package | State |
| --- | --- | --- | :---: |
| D-01 | Ratify the opt-in: `EfCoreIdempotencyOptions.MigrateOnStartup` (default `false`) and `MigrationLockTimeout`, registration-time evaluation of `configureOptions` once on a snapshot, `AddMigrationOnStartup<IdempotencyDbContext>` only when on, never `Database.MigrateAsync` | SharedKernel.Idempotency.EfCore | `○` |
| D-02 | Ratify the history table `__ef_migrations_history_idempotency` (default schema), applied only when opted in, and why it is never applied otherwise | SharedKernel.Idempotency.EfCore | `○` |
| D-03 | Ratify the guarded initial migration (`DO` block on `to_regclass`, no-op on an existing relation), its parity with `IdempotencyKeyRecordConfiguration`, `Down`, and the snapshot as the baseline for future changes | SharedKernel.Idempotency.EfCore | `○` |
| D-04 | Ratify the internal design-time factory (environment-variable connection, no credentials) and the regeneration command recorded in `CLAUDE.md` | SharedKernel.Idempotency.EfCore | `○` |
| S-01 | Add `Migrations/` (initial migration with its `Migration`/`DbContext` attributes, designer metadata, model snapshot) and the internal factory; keep the project analyzer-clean (hand-written files, no suppression; `TreatWarningsAsErrors` stays on) | SharedKernel.Idempotency.EfCore | `○` |
| C-01 | `EfCoreIdempotencyOptions.MigrateOnStartup` and `MigrationLockTimeout` with XML docs (the fail-open-style warning does not apply; state the opt-in, the history table, the migrator connection) | SharedKernel.Idempotency.EfCore | `○` |
| C-02 | `AddEfCoreIdempotency`: snapshot evaluation of `configureOptions`, lock-timeout check (`ArgumentOutOfRangeException` when not positive), conditional `AddMigrationOnStartup<IdempotencyDbContext>`, history-table application through `IdempotencyDbContextOptions` only when on; the off path registers byte-for-byte what 1.0.0 did | SharedKernel.Idempotency.EfCore | `○` |
| C-03 | The initial migration's `Up`/`Down` per D-03 | SharedKernel.Idempotency.EfCore | `○` |
| T-01 | Registration (Unit-style, in the existing test project): default off registers no hosted service and leaves the context options (history table, execution strategy) as before; on registers `ContextMigrationHostedService` once and sets the history table; `configureOptions` runs exactly once; non-positive lock timeout throws; second `AddEfCoreIdempotency` still throws; a pooled/`SharedKernelDbContext` rejection cannot occur here (the context is fixed) | SharedKernel.Idempotency.EfCore.Tests | `○` |
| T-02 | Model parity against real PostgreSQL: apply the shipped migration to an empty database; columns, types, lengths, nullability, primary key and the expiry index equal what the model declares (`Database.HasPendingModelChanges()` false and an `information_schema` comparison), and all four store statuses work on the migrated table (Integration lane, `PostgreSqlContainerFixture`) | SharedKernel.Idempotency.EfCore.Tests | `○` |
| T-03 | Real role split: `app_migrator` owns the schema, `app_runtime` has no `CREATE`; host with `MigrationConnectionString` and opt-in starts, the table and history table are owned by `app_migrator`, the history row exists, the store runs as `app_runtime` (`TryBeginAsync` -> `Started`, `CompleteAsync`, replay -> `Completed`, `ReleaseAsync`, cleanup `DELETE`), and `app_runtime` fails `CREATE TABLE` with 42501; no DDL is seen on the runtime data source (Integration lane) | SharedKernel.Idempotency.EfCore.Tests | `○` |
| T-04 | Existing table, three shapes, each with rows (a `Completed` entry with a response, an `InProgress` entry, an expired one): (a) hand-created from the documented columns by `app_migrator`; (b) created by the runtime role (owned by `app_runtime`); (c) created by a 1.0.0-style consumer `dotnet ef` migration with EF's default history table. Opting in then starts without error, a whole-table digest and the catalog definition of columns and indexes are identical before and after, the new history row is the only addition, the pre-existing `Completed` key still replays and the `InProgress` one still reports `InProgress`, and a second start is a no-op (Integration lane) | SharedKernel.Idempotency.EfCore.Tests | `○` |
| T-05 | Opt-out regression: a host that does not opt in on an empty database creates neither the table nor any history table (the first store call fails on the missing table exactly as in 1.0.0), and an opt-in host without `MigrationConnectionString` falls back to the context's own connection and creates the schema (Integration lane) | SharedKernel.Idempotency.EfCore.Tests | `○` |
| T-06 | Concurrency: two hosts start at once against one empty database; the migration runs once (one history row, no `42P07`), both hosts complete, and afterwards N parallel `TryBeginAsync` on one key yield exactly one `Started` (Integration lane) | SharedKernel.Idempotency.EfCore.Tests | `○` |
| DO-01 | EfCore README: replace "Create the table with a migration" with two recipes. Recipe 1 (supported): opt in with `o.MigrateOnStartup = true`, the host needs `AddSharedKernelPostgres`, set `MigrationConnectionString`, the history table name, migrations run on the migrator connection and the store on the runtime connection, readiness waits, the existing-table behavior, and the grants. Recipe 2 (manual fallback): the design-time factory and `dotnet ef` steps kept, the exact `CREATE TABLE` DDL, and the explicit `GRANT SELECT, INSERT, UPDATE, DELETE ON idempotency_keys TO app_runtime` for tables not created by `app_migrator`. Update Quick start, Configuration table (the two options, registration-time evaluation), Pitfalls (a caller-set `MigrationsAssembly`, a table with a drifted shape, `Down` is destructive) and Upgrading (additive, off by default); stay README-standard shaped | SharedKernel.Idempotency.EfCore | `○` |
| DO-02 | Consumer recipe `add-idempotency.md`: add an opt-in-migration paragraph to step 2 and the migrator/runtime grant note, prose only and listed under `unproven:` with the reason (the Shop does not exercise the flag), per the recipe standard; the Shop code and its existing `proven-by` paths are not touched | consumer/skill/sharedkernel/recipes | `○` |
| DO-03 | Domain `README.md`: one sentence that the EF Core store can create its own schema on startup (opt-in) | SharedKernel.Idempotency.EfCore | `○` |

**Acceptance:**
- [ ] A service that opts in and runs with a migrator connection gets `idempotency_keys` created by `app_migrator` and uses the store as `app_runtime`, proven against real PostgreSQL with both roles (T-03).
- [ ] Opting in on a database whose table exists already (hand-made, runtime-owned, or created by a 1.0.0-style consumer migration) neither fails nor changes any row or catalog definition (T-04).
- [ ] The default is off and an unchanged 1.0.0 registration behaves as before: no hosted service, no history table, no DDL (T-01, T-05).
- [ ] The migration runs once across concurrently starting replicas (T-06) and the shipped table equals the model (T-02).
- [ ] The package README and the `add-idempotency` recipe state the exact steps for the supported mechanism and the manual fallback with the grants (DO-01, DO-02).
- [ ] `FakeIdempotencyStore` is untouched; `samples/Shop` builds unchanged and the recipe's `proven-by` paths still resolve (verified by building the Shop and running the consumer-skill check).

## Blocked

None.

## Cross-Domain Dependencies

- **Inbound from 06.Persistence (`SK.06.PackageContextMigration`, P-630):** `AddMigrationOnStartup<TContext>(TimeSpan? lockTimeout = null)` in `SharedKernel.Persistence.EfCore` (done-and-merged is required before C-02 compiles; planned against the ratified D-01 signature). Reliance points 1 to 7 of that phase are honored: opt-in default off, no `Database.MigrateAsync`, non-pooled `AddDbContext`, plain `DbContext`, its own history table, one registration per context, no runtime-role DDL grants.
- **Verify on disk before implementing T-03:** whether `SharedKernel.Persistence.Testing` (`PostgresTestServer`, `PostgresTestRoles`) or `PostgreSqlContainerFixture` can provide the two-role database for this test project; if only the first can, referencing a Testing-tier package from a test project is legal. Any missing fixture capability is a note for `16.Testing`, not an edit here.
- **Outbound to 06.Persistence (note only):** the Npgsql README role script note (DO-02 of P-630) should mention that package-supplied contexts such as the idempotency one rely on the `ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator` defaults.
- **Outbound to consumer skill (`consumer/`):** DO-02 edits the recipe in `consumer/skill/sharedkernel/recipes/add-idempotency.md`; run `dotnet run eng/generate-package-index.cs` and the consumer-skill checks afterwards. A Shop use of the flag would make the snippet provable, but changing the Shop is outside this domain and not required (the recipe stays prose-only and `unproven`).
- **No obligation** on 05.Application, 07.Messaging, `.Abstractions`, the Redis provider or `FakeIdempotencyStore`: the contract and the stored format do not change.

The domain needs no readiness probe of its own: Redis and database connectivity are covered by the `redis` probe and the persistence readiness checks.
