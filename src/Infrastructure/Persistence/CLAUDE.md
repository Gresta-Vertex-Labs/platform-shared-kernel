# 06.Persistence — Domain Brain

> EF Core 10 and Dapper on **PostgreSQL only**. Implements `SharedKernel.Execution`'s `IUnitOfWork` and
> `IAuditTrailWriter` directly, reads `IRequestContext`/`TenantId`, and adds ORM-free repository/bulk/cross-tenant
> contracts, one registration entry point with conventions, one transaction per DI scope, multi-tenancy enforced twice
> (EF Core guard + transaction-local row-level security), opaque entity versions for ETags, field encryption v3 and a
> tamper-evident audit ledger (AUDITv3). It does **not** own the outbox (`07.Messaging` — no outbox type may exist
> here), specifications (`03.Domain`), paging DTOs (`04.Contracts`), readiness checks (`13.ServiceDefaults.Persistence`)
> or any other database. The platform never creates the database.

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Persistence.Abstractions` | Abstractions | ORM-free contracts: `IRepository<T,TId>`/`IReadRepository<T,TId>`, `EntityVersion`, `IBulkMutationRepository<T,TId>` + `BulkUpdateSetters<T>` + `AllRowsSpecification<T>`, `ICrossTenantScope`, `IDbConnectionFactory` + `CheckReadinessAsync`; hidden seams `ITenantSessionBinder`, `IAmbientDbTransaction`, `IMigrationLock`, `IAdvisoryTransactionLock` |
| `SharedKernel.Persistence.Npgsql` | Adapter | No EF Core. `NpgsqlDataSource` per connection name, TLS policy, keyed data sources (`NpgsqlDataSourceKeys.Migration`/`ReadOnly`/`CrossTenant`), advisory locks, transaction-local tenant binding, RLS privilege startup check, SQLSTATE classifier (`PostgresExceptionClassifier`, `PostgresErrorMapping`) |
| `SharedKernel.Persistence.EfCore` | Adapter (→ Npgsql) | Registration, contexts, repositories, `EfUnitOfWork<TContext>` over `UnitOfWorkCoordinator`, save interceptors, conventions, error classification, retry, RLS interceptors + migration helpers + coverage check, `ConcurrencyVersion` + version codec, `IPersistenceStartup`, migrations/seeding, design-time factory, jsonb, pgvector |
| `SharedKernel.Persistence.EfCore.Auditing` | Adapter (→ EfCore) | Audit ledger AUDITv3: request-path `IAuditTrailWriter`, background sealer, query/verify/export, checkpoints, maintenance, `audit-sealing` probe, self-check; packs `AUDIT-FORMAT.md` |
| `SharedKernel.Persistence.EfCore.Encryption` | Adapter (→ EfCore) | Field encryption v3: AES-256-GCM per-purpose keys, blind indexes, query guard, rotation job, tenant data keys and crypto-shredding, `field-encryption` probe |
| `SharedKernel.Persistence.Dapper` | Adapter (→ Npgsql) | `IDbSessionFactory`/`IDbSession` (joins the unit of work, binds the tenant, picks the role), type handlers (always includes the internal `TenantIdTypeHandler`) |
| `SharedKernel.Persistence.Testing` | Testing | Fakes and `PostgresTestServer` for consumers' tests |

`SharedKernel.Persistence.ConsumerVerify` (untiered, not packable) restores the **packed** packages and runs the
composed scenario against PostgreSQL. Encryption and Auditing use EfCore internals (IVT), so their nuspecs pin EfCore
to the **exact** version (`PinEfCoreDependencyToExactVersion`); EfCore pins Npgsql the same way
(`PinNpgsqlDependencyToExactVersion`).

**Namespaces** (`PersistenceNamespaceConventionRules.FindMisplacedExtensions`): registration/builder extensions in
`SharedKernel.Persistence`; EF Core model/migration/query helpers in `SharedKernel.Persistence.EfCore`; the context
types (`SharedKernelDbContext`, `TenantedDbContext`, `PersistenceContextDependencies`, `PersistenceFilterNames`,
`ICallerDbContextFactory<T>`) in `SharedKernel.Persistence.EfCore.Context`. No `*.Extensions` namespace.

## Public Entry Points

API detail, options and defaults: `src/Infrastructure/Persistence/README.md` (the 10-minute path) and each package
README. The canonical composition is compiled by
`src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Persistence/SharedKernel.ServiceDefaults.Persistence.Tests/Readme/PersistenceReadmeSampleTests.cs`.

- **EfCore** — `builder.AddSharedKernelPostgres<TContext>("orders", p => …)` (or the `IServiceCollection` overload with
  `configuration`); reads `ConnectionStrings:{name}` + `SharedKernel:Persistence:{name}`; no `.Build()`; registering the
  same `TContext` twice throws. Capability calls: `UseMultiTenancy(rowLevelSecurity, …)` (TenantedDbContext only),
  `UseAuditTrail()`, `UseFieldEncryption(k => …)`, `MigrateOnStartup()`.
- A context derives `SharedKernelDbContext` or `TenantedDbContext` with exactly
  `(DbContextOptions<T>, PersistenceContextDependencies)`. Built outside DI (a test, a tool):
  `options.UsePostgres(dataSource)` + `PersistenceContextDependencies.Create(...)`.
- Registered per context: scoped `TContext` and `IDbContextFactory<TContext>` (scope's caller), singleton
  `ICallerDbContextFactory<TContext>` (explicit caller), `IUnitOfWork<TContext>`, `IUnitOfWork` keyed by
  `typeof(TContext)` and unkeyed for the first context, open-generic repositories (closed registrations win).
- `ConcurrencyVersion`; `IRepository.UpdateAsync/DeleteAsync(aggregate, expectedVersion)`.
- `IPersistenceStartup`; `PostgresDesignTimeDbContextFactory<TContext>` for `dotnet ef` (override
  `ConfigurePersistence` so the migration model equals the runtime model).
- Migrations: `EnableTenantRowLevelSecurityForModel(TargetModel!)` / `EnableTenantRowLevelSecurity(table, …)`,
  `CreateAuditLedgerTable(runtimeRole, sealerRole)`, `CreateTenantEncryptionKeyTable`.
- **Abstractions** — `IReadRepository`/`IRepository` (incl. `ListPagedAsync`, `ListKeysetAsync`, `StreamAsync`),
  `IBulkMutationRepository` (`ExecuteUpdateAsync`, `ExecuteDeleteAsync`, `ExecutePurgeAsync`),
  `ICrossTenantScope.Enter("reason")`.
- **Npgsql** — `services.AddSharedKernelNpgsql(configuration, "orders")` for Dapper-only services;
  `PostgresErrorMapping.TryAsync` for raw ADO/Dapper. Do not name a connection `Encryption`, `Auditing`, `Dapper`,
  `Npgsql` or `ServiceName` (those `SharedKernel:Persistence:*` keys are taken). The canonical role script
  (`app_migrator`/`app_runtime`/`app_cross_tenant`/`app_audit_sealer`) is in its README.
- **Dapper** — `services.AddSharedKernelDapper(configuration, d => …)`; inject `IDbSessionFactory` → `OpenAsync()` /
  `OpenReadOnlyAsync()`; `IDbSession.Command(...)`, `CommitAsync`, `RequireTenantId()`.
- **Auditing** — `.UseAuditTrail()` (`AuditLedgerOptions`, `SharedKernel:Persistence:Auditing`); `IAuditQueryService`,
  `IAuditLedgerMaintenance`.
- **Encryption** — `.UseFieldEncryption(k => …)` (`SharedKernel:Persistence:Encryption`); `.Encrypt("purpose")
  .WithBlindIndex(…)` in entity configuration; `WhereEncryptedEquals`; `IEncryptionRotationJob`,
  `ITenantEncryptionKeyManager.ShredTenantAsync`.

## Rules & Invariants

1. **A derived context has exactly one constructor parameter besides options: `PersistenceContextDependencies`.**
   Per-capability parameters let a context compile into a silently unprotected state.
2. **Caller, domain-event dispatcher and cross-tenant scope are attached per lease and reset on dispose**, never
   through a constructor — this is what makes pooling safe. Singleton interceptors read identity from the context
   being saved, never from a captured field.
3. **One transaction per DI scope.** Every scoped context registers with `UnitOfWorkCoordinator`; never
   `Database.BeginTransaction`; `IUnitOfWork` has no `BeginTransactionAsync` (a held handle cannot be replayed).
   Contexts on the same database/role join; one that cannot join and holds changes at commit fails the commit.
4. **The delegate may run again** (retry is on by default): trackers are cleared before a retry; starting with staged
   changes under a retrying strategy is refused. `OnBeforeCommit` callbacks run after the last save, before commit,
   and are discarded with a retried attempt.
5. **Nested calls join; a joined failure marks the transaction rollback-only** (the outer call throws
   `TransactionRolledBackException`). **An ambiguous commit is never replayed** (`CommitOutcomeUnknownException`); a
   server-rejected one may be. Restore the previous `IAmbientDbTransaction` value, never clear it.
6. **Tenant binding is transaction-local only** (`set_config('app.tenant_id', …, true)`). Never session-level, never a
   connection-open interceptor, never a second setting or an escape token in policy text. Cross-tenant work runs on a
   separate database role (`NpgsqlDataSourceKeys.CrossTenant`); inside an active scope EF commands on the application
   connection throw until `UseCrossTenantConnection()` is called.
7. **Every entity type of a `TenantedDbContext` is tenant data (`IHasTenant`) or explicitly `[TenantShared]` /
   `IsTenantShared()`**, else the model build fails. Children get the same filter, guard, concurrency token and RLS
   as roots — never weaken `TenantIsolationConvention` to roots only. A `null` tenant fails closed.
8. **The save pipeline order is fixed:** soft delete → aggregate-root touch (a changed child marks its root so `xmin`
   advances) → audit stamps (actor = `UserId`, else `ServiceName`) → tenant stamping and write guard (skipped only
   while the context's cross-tenant scope is active). Domain events dispatch **before** the physical save on every
   save path. Encryption stays the **last** `SavingChanges` interceptor.
9. **Every production `IgnoreQueryFilters` is selective** (`[PersistenceFilterNames.SoftDelete]`, or under an active
   scope `[…, PersistenceFilterNames.Tenant]`); the parameterless overload drops the tenant filter too.
10. **Bulk setters fail closed** on a key, any concurrency token, `TenantId`, `CreatedBy`/`CreatedOn`, an encrypted
    column, or an unresolvable target (`ProtectedColumnUpdateGuard` enforces the same for raw `ExecuteUpdate`).
11. **Read repositories never track; write repositories always track**, regardless of the context default
    (`PersistenceInterfaceOwnershipRules.ReadOnlyRepositoriesNeverTrack`). No repository contract exposes
    `IQueryable<T>`. Paging happens at the call site only.
12. **The raw `xmin` never leaves `ConcurrencyVersion`.** `EntityVersion` is an opaque sealed token (`xmin` ‖
    aggregate binding enciphered as one AES-256 block under an HKDF subkey). No public API makes a version from a
    number; a new producer goes through the codec; client input never selects a key. A foreign, altered or unknown-key
    token is stale → `ConflictException` (`persistence.concurrency_conflict`). A wire-format change is a new format byte.
13. **A request thread does not call an asynchronous key provider.** `EntityVersionKeyWarmUp` loads the version key
    before traffic (blocking load only as fallback); the warm-up is never fatal and never a readiness condition.
14. **Error mapping:** 23505 → Conflict; 23503 → Validation or Conflict (decided by the FK and tracked changes);
    23502/23514/23P01/22001 → Validation; 40001/40P01/55P03/57014 → transient Conflict (not wrapped while retry is on);
    42501 (incl. RLS `WITH CHECK`) → Forbidden. A proven cross-tenant write answers the **same** Conflict as a stale
    version (no existence oracle).
15. **Audit request path takes no lock and assigns no sequence**; `Succeeded` is written only inside the business
    transaction (via `OnBeforeCommit`) — never in a standalone transaction. An unauthenticated caller is never audited
    as `System`. The ledger stays outside EF Core models and RLS; its tables are insert-only (triggers + grants +
    startup self-check).
16. **Any traversal of `.Encrypt()` annotations uses `PersistenceModelAnnotationNames.GetPropertiesIncludingComplex`**
    (a one-level loop misses nested complex properties and stores them in plaintext). Encryption is interceptor-based,
    never a `ValueConverter` (associated data binds purpose, primary key and tenant). Any use of an encrypted member
    other than `== null`/`!= null` throws before the query runs.
17. **Maintenance and shredding never enter the cross-tenant scope themselves** — the caller's entered scope is the
    authorization.
18. **Startup work that needs the schema waits for `IPersistenceStartup`** (audit self-check, sealer, RLS coverage
    check, database readiness).
19. **Tier hygiene:** no MediatR from any persistence package; Npgsql and Dapper never reference EF Core; Abstractions
    never references an ORM, Npgsql or Dapper; `IUnitOfWork`, `IRequestContext`, `IAuditTrailWriter` are declared only
    in `SharedKernel.Execution` (`UnitOfWorkSeamRules`).
20. **SQL is parameterized** (`SK0042`); a `TenantedDbContext.OnModelCreating` override calls `base` (`SK0201`); no
    hand-rolled AES or encryption converter; no reflection in a per-row path (exceptions: encryption's cached
    materialization setters, Dapper's mapping).
21. **Everything that can be wrong is validated at start without echoing a connection string**; types only sibling
    packages need are `internal` + IVT.

AOT is not a design constraint here (EF Core, Npgsql and Dapper are reflection-based), but expression trees are built
once and cached, and canonical encodings (AUDITv3, AAD, key canonicalization, advisory-lock hashing) are pure
`Span<byte>` code.

## Decisions

| Decision | Why |
| --- | --- |
| PostgreSQL only | RLS, `xmin`, advisory locks, `ON CONFLICT` and transaction-local settings carry the isolation and audit guarantees |
| One entry point with callbacks, conventions instead of base configurations | A service cannot forget a convention; ids, `TenantId`, `Money`, audit/soft-delete columns, `xmin` and snake_case map uniformly |
| `xmin` as the concurrency token, exposed only as a sealed `EntityVersion` | No extra column; the raw value is a database-wide counter that leaks write rates and could be forged |
| Single-block AES permutation for versions, not AES-GCM | Deterministic (same version → same ETag, `If-None-Match` works); GCM needs a random nonce |
| Readiness does not wait for the version key | A key-service outage must not take every endpoint out of rotation |
| Unknown-key version = stale (409/412), not 500 | After rotation a client re-reads once; no old-key list, so no client value selects a key |
| Transaction-local RLS binding | Safe behind transaction-mode PgBouncer and connection reuse; session settings leak across tenants |
| Cross-tenant work on a separate role, no escape token | A token in policy text is a bypass any SQL injection could use |
| Audit sealing by a background sealer in commit-safe xid order | The request path stays one insert; late commits are sealed in order without locks |
| Field encryption via interceptors with HKDF per-purpose keys | Associated data needs the primary key and tenant, which a `ValueConverter` cannot see |
| Tenant data keys + tombstones for crypto-shredding | Erases a tenant without rewriting every row; shredded values are refused on read |
| No outbox here | `07.Messaging` owns it through MassTransit's EF Core outbox |
| `18.Idempotency.EfCore` runs with retry off | Single atomic statements; fail-open must be fast |

## Logging

EventId block **6000–6999** (`LoggingEventIdRanges.Persistence`), written as `LoggingEventIdRanges.Persistence + n`:

| Sub-block | Package | In use |
| --- | --- | --- |
| 6000–6099 | EfCore | 6000–6006, 6008, 6010, 6013–6018, 6020–6021; 6022–6026 entity versions |
| 6100–6199 | Abstractions | 6150 cross-tenant scope entered |
| 6200–6299 | reserved | — |
| 6300–6349 | Npgsql | 6300–6306 |
| 6350–6399 | EfCore RLS (`RowLevelSecurityLog`) | 6350–6351 |
| 6400–6499 | Dapper | 6400–6401 (a rollback failure never masks the original error) |
| 6500–6699 | Encryption | 6500–6504, 6510–6513, 6520–6522 |
| 6700–6899 | Auditing | 6700–6713 |

Telemetry: `ActivitySource`/`Meter` `"SharedKernel.Persistence"` and `"SharedKernel.Persistence.EfCore.Auditing"`,
`Meter` `"SharedKernel.Persistence.EfCore.Encryption"`, plus Npgsql's `"Npgsql"`; wired by `13.ServiceDefaults`'
`WithPersistenceTelemetry()`. Every repository call is traced and every query `TagWith`'d with the specification type.
Dapper emits no spans (Npgsql traces commands). Npgsql's per-command Information log is lowered to Debug.

## Cross-Domain Couplings

- **01.Core / Execution:** implements `IUnitOfWork`, `IAuditTrailWriter`; reads `IRequestContext` (default
  `AnonymousRequestContext` → no tenant → fail closed) and `TenantId` (`default` never matches a filter).
  `SystemRequestContext` for seeders and jobs.
- **01.Core / Cryptography:** key provider, envelope encryption, HMAC, `SubkeyDerivation`, `FixedTimeComparison` —
  version keys, field encryption, audit MACs. The version codec and encryption use BCL `Aes`/`AesGcm` directly.
- **03.Domain:** entity/aggregate bases, `IHasTenant`, `ISoftDeletable`, `[TenantShared]`, `StronglyTypedId` (unwrapped
  via `op_Explicit`), `Money`, specifications, `IDomainEventDispatcher`.
- **04.Contracts:** `PageRequest`/`CursorPageRequest` in, `PagedList<T>`/`CursorPagedList<T>` out; a malformed
  cursor throws `ValidationException(pagination.cursor.invalid)`.
- **05.Application:** `Application.Pipeline`'s transaction behavior calls `IUnitOfWork.ExecuteInTransactionAsync`; its
  auditing behavior queues `Succeeded` with `OnBeforeCommit`. No reference in either direction beyond `Execution`.
- **07.Messaging:** `Messaging.MassTransit.EfCore` adds the outbox to a service's context.
- **13.ServiceDefaults:** `ServiceDefaults.Persistence` (`AddDatabaseReadinessCheck<T>`,
  `AddDapperDatabaseReadinessCheck`, `AddPersistenceStartupReadinessCheck`); `AddSharedKernelReadiness()` maps the
  `field-encryption` and `audit-sealing` probes; `ServiceDefaults.Security` supplies the HTTP `IRequestContext`.
- **14.Presentation:** `IfMatch<EntityVersion>` binds through `IParsable`; a version conflict is 412 on a conditional
  request (the 412 rule in `14.Presentation`), 409 otherwise; the default precondition-failed codes are pinned by
  `PresentationPreconditionCodesTests`.
- **18.Idempotency:** `Idempotency.EfCore` builds on `Persistence.EfCore` (declared edge).
- **00.Governance:** `PersistenceNamespaceConventionRules`, `PersistenceInterfaceOwnershipRules`,
  `UnitOfWorkSeamRules`, `TestingNeverReferencedByProduction`, SK0042, SK0201.
- Reference services: `samples/BillingApi`, `samples/Shop`.

## Testing

- **Unit lane:** `Persistence.Abstractions.Tests`, `Persistence.EfCore.Tests` (SQLite through the internal
  `UseProviderForTesting` seam; `TestPersistenceRegistration` wraps the real registration).
- **Integration lane** (Testcontainers PostgreSQL, `SharedKernel.Testing.Internal`'s `PostgreSqlContainerFixture`):
  `EfCore.Integration.Tests`, `Npgsql.Tests`, `Dapper.Tests`, `EfCore.Auditing.Tests`, `EfCore.Encryption.Tests`,
  `Persistence.Testing.Tests`, and `ServiceDefaults.Persistence.Tests` (13.ServiceDefaults).
- **RLS and tenant-isolation claims are proven through an unprivileged role** (a superuser bypasses RLS even under
  `FORCE`); attack tests build the detached stub or raw SQL a hostile caller would send.
- Concurrency, commit-order and retry claims are proven empirically (real concurrent writers, injected transient
  `PostgresException`s, a transaction that commits late), never by a sequential stand-in.
- `EntityVersionCodecTests` holds the known-answer test locking version format `0x01`; `AuditFormatVectorTests` parses
  the packed `AUDIT-FORMAT.md`; README samples are compiled by `ReadmeSampleTests`/`PersistenceReadmeSampleTests`.
- Fakes: `SharedKernel.Persistence.Testing` — catalogue in `src/Testing/CLAUDE.md`.
- Container suites that fail under whole-solution load are re-run in isolation before being treated as real.

## Known Limitations

- **No two-phase commit**: a context on another database or the cross-tenant role never joins the scope's
  transaction.
- **Nested audited command**: its `Succeeded` entry rolls back with a failing outer command; no separate `Failed`
  entry is written for it.
- `IRequestContext.ImpersonatorId` is not populated by the HTTP request context (`IUserContext` exposes no
  impersonation claim).
- Seeding under RLS requires `RowLevelSecurity:CrossTenantConnectionString`.
- `EnableTenantRowLevelSecurityForModel` in a later migration re-creates policies of existing tables — use the
  per-table helper for tables added later.
- Encryption: no specification form of `WhereEncryptedEquals` (use the `IQueryable` overload); no tenant data-key
  rotation (master-key rewrap is the KMS's job); `byte[]` and composite/shadow keys unsupported; writes that bypass EF
  Core are not tombstone-checked; another process may decrypt a shredded tenant from its cache for up to
  `TenantKeyCacheDuration`; the rotation CAS race has no dedicated test.
- Audit: a link forged for one record looks sealed until verification — prevented only by the separate sealer role;
  partitioned retention and signed offline export bundles are not implemented.
- Entity versions: a key rotation plus restart makes earlier ETags stale (412 once); versions exist only for tracked
  aggregates (no public codec for projections or Dapper read models); with an async-only provider the fallback load
  blocks the calling thread under a lock.
- The BCL `Aes`/`AesGcm` uses sit outside `SharedKernel.Cryptography`;
  `CryptoIsolationRules.NoRawSymmetricCipherOutsideCryptography` is not run against these assemblies.
- EfCore's public surface is large (~140 `PublicAPI` lines), mostly the subclassable repositories; accepted.
- No real PgBouncer container test; a `FakeUnitOfWork` rollback restores which aggregates a `FakeRepository` holds, not
  in-place changes to an aggregate object.
