# 06.Persistence — Domain Brain

> EF Core 10 and Dapper on **PostgreSQL only**. This domain implements `SharedKernel.Execution`'s `IUnitOfWork` and
> `IAuditTrailWriter` directly (no bridge to the request pipeline), reads `IRequestContext`/`TenantId`, and adds
> ORM-free repository/bulk/cross-tenant contracts, one registration entry point with conventions, one transaction per
> DI scope, multi-tenancy enforced twice (EF Core guard + transaction-local row-level security), opaque entity
> versions for ETags, field encryption v3 and a tamper-evident audit ledger (AUDITv3). It does **not** own the outbox
> (`07.Messaging`, MassTransit `UseEntityFrameworkOutbox` — no outbox type may exist here), specifications (`03.Domain`),
> paging DTOs (`04.Contracts`), health checks (`13.ServiceDefaults.Persistence`), or any database other than PostgreSQL.
> The platform never creates the database.

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Persistence.Abstractions` | Abstractions | ORM-free contracts: `IRepository<T,TId>`/`IReadRepository<T,TId>`, `EntityVersion`, `IBulkMutationRepository<T,TId>` + `BulkUpdateSetters<T>` + `AllRowsSpecification<T>`, `ICrossTenantScope` (+ `CrossTenantScope`, `AddSharedKernelCrossTenantScope()`), `IDbConnectionFactory` + `CheckReadinessAsync`/`DatabaseReadinessResult`; hidden seams `ITenantSessionBinder`, `IAmbientDbTransaction`, `IMigrationLock`, `IAdvisoryTransactionLock` |
| `SharedKernel.Persistence.Npgsql` | Adapter | No EF Core. `NpgsqlDataSource` per connection name (`AddSharedKernelNpgsql`), `NpgsqlPersistenceOptions` + TLS policy, keyed data sources (`NpgsqlDataSourceKeys.Migration`/`ReadOnly`/`CrossTenant`), `IDbConnectionFactory`, advisory locks (`AdvisoryLockKeys`), transaction-local tenant binding, RLS privilege startup check, SQLSTATE classifier (`PostgresExceptionClassifier`, `PostgresErrorMapping`, `PostgresClassifiedErrorCodes`) |
| `SharedKernel.Persistence.EfCore` | Adapter (→ Npgsql) | The PostgreSQL EF Core package: registration, contexts, repositories, `EfUnitOfWork<TContext>` over `UnitOfWorkCoordinator`, the save interceptor, conventions, error classification, retry, RLS interceptors + migration helpers + coverage check, `ConcurrencyVersion` + the version codec, `IPersistenceStartup`, migrations/seeding, design-time factory, jsonb, pgvector |
| `SharedKernel.Persistence.EfCore.Auditing` | Adapter (→ EfCore) | Audit ledger AUDITv3: request-path `IAuditTrailWriter`, background sealer, query/verify/export, checkpoints (`IAuditCheckpointSink`), maintenance, `IAuditRecordAuthenticator`, `audit-sealing` probe (`AuditSealingReadiness`), self-check; `AUDIT-FORMAT.md` is packed |
| `SharedKernel.Persistence.EfCore.Encryption` | Adapter (→ EfCore) | Field encryption v3: AES-256-GCM per-purpose keys, blind indexes, query guard, rotation/maintenance job, tenant data keys and crypto-shredding, `field-encryption` probe (`FieldEncryptionReadiness`) |
| `SharedKernel.Persistence.Dapper` | Adapter (→ Npgsql) | `IDbSessionFactory`/`IDbSession` (joins the unit of work, binds the tenant, picks the role), `DapperConfiguration`/`DapperConfigurationBuilder` type handlers (always includes the internal `TenantIdTypeHandler`), `AddSharedKernelDapper()` |

`SharedKernel.Persistence.ConsumerVerify` (untiered, not packable) restores the **packed** packages and runs the
composed scenario against PostgreSQL. Adapter→adapter edges are declared in each csproj's
`<SharedKernelAllowedAdapterReferences>`. Encryption and Auditing use EfCore internals (IVT), so their nuspecs pin
EfCore to the **exact** version (`PinEfCoreDependencyToExactVersion`); EfCore pins Npgsql the same way
(`PinNpgsqlDependencyToExactVersion`). All packages track `PublicAPI.*.txt` (build-breaking) and require XML docs.

**Namespaces (enforced by `PersistenceNamespaceConventionRules.FindMisplacedExtensions`):** every registration/builder
extension (`IServiceCollection`, `IHostApplicationBuilder`, `EfCorePersistenceBuilder<T>`, `DbContextOptionsBuilder`)
lives in `SharedKernel.Persistence`; EF Core model/migration/query helpers (`Money`, `HasJsonbColumn`,
`HasVectorColumn`/`HasVectorIndex`, `VectorOrderingExpressions`, `IsTenantShared`, `Encrypt`/`WithBlindIndex`,
`WhereEncryptedEquals`, `UseCrossTenantConnection`, `EnableTenantRowLevelSecurity*`, `CreateAuditLedgerTable`,
`CreateTenantEncryptionKeyTable`) in `SharedKernel.Persistence.EfCore`; `SharedKernelDbContext`, `TenantedDbContext`,
`PersistenceContextDependencies`, `PersistenceFilterNames`, `ICallerDbContextFactory<T>` in
`SharedKernel.Persistence.EfCore.Context`. No `*.Extensions` namespace.

## Public Entry Points

**EfCore** — `builder.AddSharedKernelPostgres<TContext>("orders", p => …)` (`IHostApplicationBuilder`) or
`services.AddSharedKernelPostgres<TContext>(configuration, "orders", p => …)`; reads `ConnectionStrings:{name}` +
`SharedKernel:Persistence:{name}`; no `.Build()`; registering the same `TContext` twice throws. Builder:
`ConfigureProvider` (`PostgresProviderOptions`: `UseVector`, `MaxRetryCount` (default 6), `MaxRetryDelay` (30 s),
`AdditionalTransientErrorCodes`), `ConfigureDataSource`, `UseDataSource`, `ConfigureDbContext` (compiled model),
`UseDbContextPooling`, `UseServiceName` (or `SharedKernel:Persistence:ServiceName`, default `"system"`), `UseUuidV7Keys`,
`MigrateOnStartup(lockTimeout?)`, `AddSeeder<T>`, `AddInterceptor<T>`; capability extensions
`UseMultiTenancy(rowLevelSecurity, rowLevelSecurityCheck)` (TenantedDbContext only), `UseAuditTrail()`,
`UseFieldEncryption(k => …)`.
- A context derives `SharedKernelDbContext` or `TenantedDbContext` with exactly
  `(DbContextOptions<T>, PersistenceContextDependencies)`; override `ShouldApplyConfiguration(Type)` when several
  contexts share an assembly. Hand-built (tools/tests): `options.UsePostgres(dataSource)` +
  `PersistenceContextDependencies.Create(...)`.
- Registered per context: scoped `TContext`, scoped `IDbContextFactory<TContext>` (attaches the scope's caller),
  singleton `ICallerDbContextFactory<TContext>` (explicit caller, for singletons), `IUnitOfWork<TContext>`, keyed
  `IUnitOfWork` (key `typeof(TContext)`), unkeyed `IUnitOfWork`/`SharedKernelDbContext` (first context), open-generic
  repositories (context found by probing registered models; closed registrations win).
- `ConcurrencyVersion.Get(db, entity)`, `SetExpected`, `TryGetCurrentVersion(exception, out version)`,
  `ConflictErrorCode`; `IRepository.UpdateAsync/DeleteAsync(aggregate, expectedVersion)`.
- `IPersistenceStartup` (`IsCompleted`, `WaitAsync`); `PostgresDesignTimeDbContextFactory<TContext>(connectionName)`
  for `dotnet ef` (override `ConfigurePersistence` with the service's capability calls so the migration model equals
  the runtime model).
- Migrations: `migrationBuilder.EnableTenantRowLevelSecurityForModel(TargetModel!)` /
  `EnableTenantRowLevelSecurity(table, …)`, `context.Database.UseCrossTenantConnection()`,
  `IgnoreQueryFilters([PersistenceFilterNames.SoftDelete])` / `PersistenceFilterNames.Tenant` (inside a scope only).

**Abstractions** — repositories: `IReadRepository` (`GetByIdAsync`, `GetByIdsAsync`, `ExistsAsync`,
`FirstOrDefaultAsync`, `ListAsync`, `CountAsync`, `AnyAsync`, `ListPagedAsync(spec, PageRequest)`,
`ListKeysetAsync(spec, CursorPageRequest, keySelector, descending)`, `StreamAsync`, `*ProjectedAsync`);
`IRepository` adds tracked reads and `AddAsync`/`UpdateAsync`/`DeleteAsync` (+ ranges). Bulk:
`ExecuteUpdateAsync(spec, s => s.SetProperty(...))`, `ExecuteDeleteAsync`, `ExecutePurgeAsync`.
`ICrossTenantScope.Enter("reason")`.

**Npgsql** — `services.AddSharedKernelNpgsql(configuration, "orders")` (Dapper-only services; the unnamed overload
reads `SharedKernel:Persistence:Npgsql`). `NpgsqlPersistenceOptions`: `SslMode`, `AcknowledgeInsecureSslMode`,
timeouts, `MigrationConnectionString`, `ReadOnlyConnectionString`,
`RowLevelSecurity { Enabled, CrossTenantConnectionString, PrivilegeCheck }`, `EnableDynamicJson`, `UseVector`. Reserved
connection names: `Encryption`, `Auditing`, `Dapper`, `Npgsql`. `PostgresErrorMapping.TryAsync` for raw ADO/Dapper.
The canonical role script (`app_migrator`/`app_runtime`/`app_cross_tenant`/`app_audit_sealer`) is in its README.

**Dapper** — `services.AddSharedKernelDapper(configuration, d => d.AddStronglyTypedId<OrderId, Guid>())`
(`AddSmartEnum`, `AddJsonb(JsonTypeInfo<T>)`, `AddTypeHandler`, `MatchNamesWithUnderscores`); inject
`IDbSessionFactory` → `OpenAsync()`, `OpenReadOnlyAsync()`, `OpenAsync(DbSessionOptions)`; `IDbSession.Command(sql,
params, ct)`, `CommitAsync`, `RequireTenantId()`.

**Auditing** — `.UseAuditTrail()` (section `SharedKernel:Persistence:Auditing`, `AuditLedgerOptions`: `CurrentKeyId`,
`Keys`, `CheckpointSigningKeyId`, `AcceptedCheckpointSigningKeyIds`, `Sealer { Enabled, Interval, BatchSize,
CheckpointInterval, DataSourceName, MaxReadyLag }`, `SelfCheck` Off/Warn/Fail). `IAuditQueryService` (verify, export),
`IAuditLedgerMaintenance` (`SealPendingAsync`, `SealAllChainsAsync`, `ErasePayloadAsync`,
`EraseResourcePayloadsAsync`). Migration: `CreateAuditLedgerTable(runtimeRole, sealerRole)`.

**Encryption** — `.UseFieldEncryption(k => k.UseTenantDataKeys())` (section `SharedKernel:Persistence:Encryption`;
key source defaults to the `IEncryptionKeyProvider` in DI, or `FromConfiguration()`/`UseKeyProvider<T>()`); in
`IEntityTypeConfiguration<T>`: `.Encrypt("purpose").WithBlindIndex(BlindIndexNormalization…)`; query with
`WhereEncryptedEquals(x => x.Email, value)`. Maintenance: `IEncryptionRotationJob.RunAsync(new
EncryptionMaintenanceRequest { Mode = … })`, `report.IsSafeToRetire(keyId)`; `ITenantEncryptionKeyManager
.ShredTenantAsync(tenantId)`; migration `CreateTenantEncryptionKeyTable`.

The canonical composition is compiled by
`src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Persistence/…Tests/Readme/PersistenceReadmeSampleTests.cs`; the
10-minute path is `src/Infrastructure/Persistence/README.md`.

## Rules & Invariants

1. **A derived context has exactly one constructor parameter besides options: `PersistenceContextDependencies`.**
   Per-capability parameters let a context compile into a silently unprotected state.
2. **Caller, domain-event dispatcher and cross-tenant scope are attached per lease and reset on dispose**, never
   through a constructor — this is what makes pooling safe. Singleton interceptors read identity from the context
   being saved, never from a captured field.
3. **One transaction per DI scope.** Every scoped context registers with `UnitOfWorkCoordinator`; never
   `Database.BeginTransaction` directly; `IUnitOfWork` has no `BeginTransactionAsync` (a held handle cannot be
   replayed). Contexts on the same database/role join the transaction; a context that cannot join and holds changes at
   commit fails the commit (no two-phase commit).
4. **The delegate may run again** (retry is on by default): trackers are cleared before a retried attempt; starting
   with staged changes under a retrying strategy is refused. `OnBeforeCommit` callbacks run after the last save,
   before commit, and are discarded with a retried attempt.
5. **Nested calls join; a joined failure marks the transaction rollback-only** — the outer call rolls back and throws
   `TransactionRolledBackException` if it would have succeeded. **An ambiguous commit is never replayed**
   (`CommitOutcomeUnknownException`); a server-rejected commit may be. Restore the previous `IAmbientDbTransaction`
   value, never clear it.
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
11. **Read repositories never track; write repositories always track**, regardless of the context default. No
    repository contract exposes `IQueryable<T>`. Paging happens at the call site only.
12. **The raw `xmin` never leaves `ConcurrencyVersion`.** `EntityVersion` is an opaque sealed token (28-char
    Base64Url; `xmin` ‖ aggregate binding enciphered as one AES-256 block under an HKDF subkey of the service's key
    provider). No public API makes a version from a number; a new producer goes through the codec. Keys are never
    selected by client input. A foreign, altered or unknown-key token is a stale version → `ConflictException`
    (`persistence.concurrency_conflict`). A wire-format change is a new format byte.
13. **A request thread does not call an asynchronous key provider.** `EntityVersionKeyWarmUp` loads the version key
    before traffic; the blocking load is only the fallback. The warm-up is never fatal and never a readiness condition.
14. **Error mapping:** 23505 → Conflict; 23503 → Validation or Conflict (decided by the FK and tracked changes);
    23502/23514/23P01/22001 → Validation; 40001/40P01/55P03/57014 → transient Conflict (not wrapped while retry is on);
    42501 (incl. RLS `WITH CHECK`) → Forbidden. A proven cross-tenant write answers the **same** Conflict as a stale
    version (no existence oracle).
15. **Audit request path takes no lock and assigns no sequence**; `Succeeded` is written only inside the business
    transaction (`IAmbientDbTransaction.Current`, via `OnBeforeCommit`) — never "fix" it by opening a standalone
    transaction. An unauthenticated caller is never audited as `System`. The ledger stays outside EF Core models and
    outside RLS; its tables are insert-only (triggers + grants + startup self-check).
16. **Any traversal of `.Encrypt()` annotations uses `PersistenceModelAnnotationNames.GetPropertiesIncludingComplex`**
    (a one-level loop once stored nested complex properties in plaintext). Encryption is interceptor-based, never a
    `ValueConverter` (associated data binds purpose, primary key and tenant). Any use of an encrypted member other than
    `== null`/`!= null` throws before the query runs.
17. **Maintenance and shredding never enter the cross-tenant scope themselves** — the caller's entered scope is the
    authorization.
18. **Startup work that needs the schema waits for `IPersistenceStartup`** (audit self-check, sealer, RLS coverage
    check, database readiness).
19. **Tier hygiene:** no Host package, ASP.NET Core or MediatR from any persistence package; Npgsql and Dapper never
    reference EF Core; Abstractions never references an ORM, Npgsql or Dapper; `IUnitOfWork`, `IRequestContext`,
    `IAuditTrailWriter` are declared only in `SharedKernel.Execution` (`UnitOfWorkSeamRules`).
20. **SQL is parameterized** (`SK0042`); a `TenantedDbContext.OnModelCreating` override calls `base` (`SK0201`); no
    hand-rolled AES or encryption converter; no reflection in a per-row path (exceptions: encryption's cached
    materialization setters, Dapper's mapping).
21. **New code:** `[LoggerMessage]` in the package's own sub-block; options implement `ISectionBoundOptions` and
    register with `AddValidatedOptions`; everything that can be wrong is validated at start without echoing a
    connection string; types only siblings need are `internal` + IVT.

AOT is not a design constraint here (EF Core, Npgsql and Dapper are reflection-based), but expression trees are built
once and cached, and canonical encodings (AUDITv3, AAD, key canonicalization, advisory-lock hashing) are pure
`Span<byte>` code.

## Decisions

| Decision | Why |
| --- | --- |
| PostgreSQL only | RLS, `xmin`, advisory locks, `ON CONFLICT` and transaction-local settings carry the isolation and audit guarantees; a lowest-common-denominator provider would lose them |
| One entry point with callbacks, conventions instead of base configurations | A service cannot forget a convention; mapping of ids, `TenantId`, `Money`, audit/soft-delete columns, `xmin` and snake_case is uniform |
| `xmin` as the concurrency token, exposed only as a sealed `EntityVersion` | No extra column; the raw value is a database-wide counter that leaks write rates and could be forged |
| Single-block AES permutation for versions, not AES-GCM | Deterministic (same version → same ETag, `If-None-Match` works) and minimal; GCM needs a random nonce |
| Readiness does not wait for the version key | Only issuing/checking versions needs it; a key-service outage must not take every endpoint out of rotation |
| Unknown-key version = stale (409/412), not 500 | After a rotation plus restart a client re-reads once; no configured list of old keys, so no client value selects a key |
| Transaction-local RLS binding | Safe behind transaction-mode PgBouncer and connection reuse; session settings leak across tenants |
| Cross-tenant work on a separate role, no escape token | A token in policy text is a bypass any SQL injection could use |
| Audit sealing by a background sealer in commit-safe xid order | The request path stays one insert; chains are appended without locks and late commits are sealed in order |
| Field encryption via interceptors with HKDF per-purpose keys | Associated data needs the primary key and tenant, which a `ValueConverter` cannot see |
| Tenant data keys + tombstones for crypto-shredding | Erasure of a tenant without rewriting every row; shredded values are refused on read |
| No outbox here | `07.Messaging` owns it through MassTransit's EF Core outbox |
| `18.Idempotency.EfCore` runs with retry off | Single atomic statements; fail-open must be fast |

## Logging

EventId block **6000–6999** (`LoggingEventIdRanges.Persistence`):

| Sub-block | Package | In use |
| --- | --- | --- |
| 6000–6099 | EfCore | 6000–6006, 6008, 6010, 6013–6018, 6020–6021 context/unit of work/startup; 6014 events cleared without a dispatcher, 6016 no dispatcher at startup; 6022–6026 entity versions (6022 rejected token at Debug, 6023 refresh failed, 6024 sealing in translator failed, 6025 warm-up failed, 6026 warm-up timeout) |
| 6100–6199 | Abstractions | 6150 cross-tenant scope entered (actor, kind, tenant, reason) |
| 6200–6299 | reserved | — |
| 6300–6349 | Npgsql | 6300–6306 (6300 TLS below `VerifyFull` for a non-loopback host) |
| 6350–6399 | EfCore RLS (`RowLevelSecurityLog`) | 6350–6351 |
| 6400–6499 | Dapper | 6400 rollback failure (never masks the original error), 6401 |
| 6500–6699 | Encryption | 6500–6504, 6510–6513, 6520–6522 |
| 6700–6899 | Auditing | 6700–6713 |

Telemetry: `ActivitySource`/`Meter` `"SharedKernel.Persistence"` and `"SharedKernel.Persistence.EfCore.Auditing"`,
`Meter` `"SharedKernel.Persistence.EfCore.Encryption"`, plus Npgsql's `"Npgsql"`; wired by `13.ServiceDefaults`'
`WithPersistenceTelemetry()`. Every repository call is traced and every query `TagWith`'d with the specification type.
Dapper emits no spans (Npgsql traces commands). Npgsql's per-command Information log is lowered to Debug.

## Cross-Domain Couplings

- **01.Core / Execution:** implements `IUnitOfWork` (+ `IUnitOfWork<TContext>`), `IAuditTrailWriter`
  (`AuditEntry`/`AuditOutcome`); reads `IRequestContext` (default `AnonymousRequestContext` → no tenant → fail
  closed) and `TenantId` (EF `TenantIdValueConverter`, Dapper `TenantIdTypeHandler`; `default` never matches a filter).
  `SystemRequestContext` for seeders and jobs.
- **01.Core / Cryptography:** `IEncryptionKeyProvider`, `IEnvelopeEncryptionProvider`, `IHmacSigner`,
  `SubkeyDerivation`, `FixedTimeComparison` — version keys, field encryption, audit MACs. The version codec and
  encryption use BCL `Aes`/`AesGcm` directly (see Known Limitations).
- **03.Domain:** entities, aggregate roots, `IHasTenant`, `ISoftDeletable`, `[TenantShared]`, `StronglyTypedId`,
  `Money`, `IValueObject`, specifications (`Spec.For<T>()`, `ProjectionSpecification<T,TResult>`),
  `IDomainEventDispatcher`, `IHasClock`.
- **04.Contracts:** `PageRequest`/`CursorPageRequest` in, `PagedList<T>`/`CursorPagedList<T>` out; a malformed
  cursor throws `ValidationException(pagination.cursor.invalid)`.
- **05.Application:** `Application.Pipeline`'s transaction behavior calls `IUnitOfWork.ExecuteInTransactionAsync`; its
  auditing behavior queues `Succeeded` with `OnBeforeCommit`. No reference in either direction beyond `Execution`.
- **07.Messaging:** `Messaging.MassTransit.EfCore` adds the outbox to a service's context.
- **13.ServiceDefaults:** `ServiceDefaults.Persistence` (`AddDatabaseReadinessCheck<T>`,
  `AddDapperDatabaseReadinessCheck`, `AddPersistenceStartupReadinessCheck`); `AddSharedKernelReadiness()` maps the
  `field-encryption` and `audit-sealing` probes; `ServiceDefaults.Security` supplies the HTTP `IRequestContext`.
- **14.Presentation:** `IfMatch<EntityVersion>` binds through `IParsable`; a version conflict is 412 on a conditional
  request (R7 in `14.Presentation`), 409 otherwise. The default precondition-failed codes are pinned by a governance test.
- **18.Idempotency:** `Idempotency.EfCore` builds on `Persistence.EfCore` (declared edge).
- **00.Governance:** `PersistenceNamespaceConventionRules`, `PersistenceInterfaceOwnershipRules`
  (`ReadOnlyRepositoriesNeverTrack`, IL scan), `UnitOfWorkSeamRules`, `TestingNeverReferencedByProduction`, SK0042,
  SK0201.
- Reference services: the Shop (`samples/Shop`) — Ordering (EF Core, RLS, field encryption, audit ledger), Billing (EF Core, RLS), Inventory and Reports (Dapper).

## Testing

- **Unit lane** (`Platform.SharedKernel.Unit.slnf`): `Persistence.Abstractions.Tests`, `Persistence.EfCore.Tests`
  (SQLite through the internal `UseProviderForTesting` seam; `TestPersistenceRegistration` wraps the real
  registration).
- **Integration lane** (`Platform.SharedKernel.Integration.slnf`, Testcontainers PostgreSQL via
  `src/Testing/SharedKernel.Testing.Internal`'s `PostgreSqlContainerFixture`): `EfCore.Integration.Tests`,
  `Npgsql.Tests`, `Dapper.Tests`, `EfCore.Auditing.Tests`, `EfCore.Encryption.Tests`, and
  `src/Infrastructure/Persistence/SharedKernel.Persistence.Testing/SharedKernel.Persistence.Testing.Tests`.
- **RLS and tenant-isolation claims are proven through an unprivileged role** (a superuser bypasses RLS even under
  `FORCE`); attack tests build the detached stub or raw SQL a hostile caller would send.
- Concurrency, commit-order and retry claims are proven empirically (real concurrent writers, injected transient
  `PostgresException`s, a transaction that commits late), never by a sequential stand-in.
- Entity versions: `EfCore.Tests/Concurrency/EntityVersionCodecTests` (includes a known-answer test locking format
  `0x01`) and `EfCore.Integration.Tests/Postgres/EntityVersionPostgresTests` (shared `xmin`, tampering, rotation,
  warm-up, KMS-style provider).
- README samples are compiled by tests (`PersistenceReadmeSampleTests`, Encryption `ReadmeSampleTests`, Auditing
  `ReadmeSampleTests`); `AuditFormatVectorTests` parses the packed `AUDIT-FORMAT.md`.
- `SharedKernel.Persistence.ConsumerVerify` runs against packed packages in CI; build and pack steps are in
  `CONTRIBUTING.md` / `eng/README.md`.
- Consumers use `src/Infrastructure/Persistence/SharedKernel.Persistence.Testing`: `AddFakeRepository<T,TId>()`, `AddFakeUnitOfWork()`
  (`TransientFailures` proves a handler re-runnable), `AddFakeAuditTrailWriter()`, `AddFakeCrossTenantScope()`,
  `FakeDbConnectionFactory`, `PostgresTestServer`/`PostgresTestDatabase` (canonical roles); the caller is
  `SharedKernel.Testing`'s `TestRequestContext`.
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
- Entity versions: a key rotation followed by a restart makes earlier ETags stale (412 once); versions are issued only
  for tracked aggregates (no public codec for projections or Dapper read models); with an async-only provider the
  fallback load blocks the calling thread under a lock. The BCL `Aes`/`AesGcm` uses sit outside
  `SharedKernel.Cryptography`; `CryptoIsolationRules.NoRawSymmetricCipherOutsideCryptography` is not run against these
  assemblies.
- EfCore's public surface is large (~140 `PublicAPI` lines), mostly the subclassable repositories; accepted.
- No real PgBouncer container test; a `FakeUnitOfWork` rollback restores which aggregates a `FakeRepository` holds, not
  in-place changes to an aggregate object.
