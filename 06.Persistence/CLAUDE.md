# 06.Persistence — PostgreSQL Data Access Layer

> **Audience:** maintainers and AI agents changing code in this folder. Consumers read
> [`README.md`](README.md) (the domain entry point) and each package's own `README.md`; this brain holds the
> rules, invariants and couplings the source does not make obvious. It describes the code **after P-558**
> (persistence gold-standard pass 2, 2026-09-21). The P-557 brain and the pre-P-557 brain are kept, superseded,
> in [`CLAUDE.archive.md`](CLAUDE.archive.md) — consult them only for the reasoning behind a decision, never for
> routing. The review findings and design decisions behind P-558 are in [`docs/p558/`](docs/p558/).

## What This Domain Is

The persistence layer of the platform: EF Core 10 and Dapper on **PostgreSQL only**. It implements the shared
contracts of `05.Application/SharedKernel.Application.Abstractions` (`IUnitOfWork`, `IRequestContext`,
`IAuditTrailWriter`) directly — there is no adapter or bridge between the MediatR pipeline and persistence — and
adds its own repository/specification contracts, multi-tenancy (application guard + row-level security), field
encryption and an audit ledger.

Philosophy: **One entry point. Conventions over configuration. One transaction per scope. Tenant isolation
enforced twice (EF Core and PostgreSQL). Fail at startup, not at the first request.**

> **Outbox scope:** owned entirely by `07.Messaging` (MassTransit `UseEntityFrameworkOutbox`). No outbox type
> (`OutboxMessage`, `IOutboxWriter`, `OutboxInterceptor`) may exist in this domain.

**Nothing in this domain is published yet** (see the root `P-558-SESSION-HANDOFF.md` for the publish set and order).

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Persistence.Abstractions` | ORM-free contracts: `IRepository<T,TId>`/`IReadRepository<T,TId>`, `EntityVersion`, `IBulkMutationRepository<T,TId>` + `BulkUpdateSetters<T>` + `AllRowsSpecification<T>`, `ICrossTenantScope` (+ its implementation `CrossTenantScope` and `AddSharedKernelCrossTenantScope()`), `IDbConnectionFactory` + readiness probe; infrastructure seams `ITenantSessionBinder`, `IAmbientDbTransaction`, `IMigrationLock`, `IAdvisoryTransactionLock` (`[EditorBrowsable(Never)]`) | `Primitives`, `Domain`, `Contracts`, `Application.Abstractions`, DI/Logging abstractions |
| `SharedKernel.Persistence.Npgsql` | No EF Core. The `NpgsqlDataSource` per connection name (`AddSharedKernelNpgsql`), options + TLS policy, keyed secondary data sources (`NpgsqlDataSourceKeys.Migration`/`ReadOnly`/`CrossTenant`), `IDbConnectionFactory`, advisory locks + `AdvisoryLockKeys`, transaction-local tenant binding (`ITenantSessionBinder`), RLS privilege startup check + internal `RowLevelSecurityCatalog`, SQLSTATE classifier (`PostgresExceptionClassifier`, `PostgresErrorMapping`, `PostgresClassifiedErrorCodes`) | `Abstractions`, `Configuration`, `Npgsql`, `Pgvector` |
| `SharedKernel.Persistence.EfCore` | **The PostgreSQL EF Core package** (the former `.PostgreSQL` package was merged in). `AddSharedKernelPostgres<TContext>` + `EfCorePersistenceBuilder<TContext>`, `SharedKernelDbContext`/`TenantedDbContext`, `PersistenceContextDependencies`, open-generic `EfRepository`/`EfReadRepository`, `TenantedRepository`, `EfUnitOfWork<TContext>` + `UnitOfWorkCoordinator`, the one save interceptor, conventions (snake_case via `EFCore.NamingConventions`, 63-byte identifiers, `xmin`, strongly-typed ids, `Money`, audit/soft-delete/tenant columns, tenant isolation), SQLSTATE classification, retry, RLS interceptors + migration helpers + coverage check, `ConcurrencyVersion` + the internal version codec (opaque ETags, P-562 X4), `IPersistenceStartup`, migrations/seeding, `PostgresDesignTimeDbContextFactory<T>`, jsonb, pgvector | `Abstractions`, `Npgsql`, `Domain`, `Core`, `Configuration`, `Cryptography` (version keys), `Npgsql.EntityFrameworkCore.PostgreSQL`, `Pgvector.EntityFrameworkCore`, `EFCore.NamingConventions` |
| `SharedKernel.Persistence.EfCore.Auditing` | Audit ledger v3 (AUDITv3): `UseAuditTrail()`, request-path writer (`IAuditTrailWriter`), background sealer, `IAuditQueryService`, `IAuditCheckpointService`/`IAuditCheckpointSink`, `IAuditLedgerMaintenance` (erasure, reseal), `IAuditRecordAuthenticator` (keyring), `IAuditSealingProbe`, self-check, `CreateAuditLedgerTable` migration helper, `AUDIT-FORMAT.md` (packed) | `EfCore` (exact version pin), `Cryptography`, `Configuration` |
| `SharedKernel.Persistence.EfCore.Encryption` | Field encryption v3: `UseFieldEncryption()`, `.Encrypt(purpose)`/`.WithBlindIndex(...)`, `WhereEncryptedEquals`, query guard, `IEncryptionRotationJob` (maintenance modes), tenant data keys + `ITenantEncryptionKeyManager.ShredTenantAsync`, key ring + probe | `EfCore` (exact version pin), `Cryptography`, `Configuration` |
| `SharedKernel.Persistence.Dapper` | `IDbSessionFactory`/`IDbSession` (connection + transaction that joins the unit of work, binds the tenant, picks the role), `DapperConfiguration`/`DapperConfigurationBuilder` type handlers, `AddSharedKernelDapper()` | `Npgsql` (never EF Core), `Configuration`, `Dapper` |

Outside this folder but part of the stack: **`16.Testing/SharedKernel.Persistence.Testing`** (packable, test
projects only — fakes of every contract, `PostgresTestServer`/`PostgresTestDatabase` with the canonical roles),
**`13.ServiceDefaults/SharedKernel.ServiceDefaults.Persistence`** (readiness checks) and
**`SharedKernel.ServiceDefaults.Security`** (`AddSharedKernelRequestContext()`, the `IRequestContext` over
`12.Security`). `SharedKernel.Persistence.ConsumerVerify` (not packable) restores the **packed** packages and runs
the composed scenario against PostgreSQL.

All packages: `net10.0`, nullable, XML docs, `PublicAPI.*.txt` tracking is build-breaking (`RS0016`/`RS0017`/…).
Encryption and Auditing reach EfCore's extension points through `InternalsVisibleTo`, so their nuspecs pin
`SharedKernel.Persistence.EfCore` to their **exact** version (`PinEfCoreDependencyToExactVersion`); EfCore pins
`SharedKernel.Persistence.Npgsql` the same way (`PinNpgsqlDependencyToExactVersion`). Mixed versions can never
restore together.

### Namespaces (mechanically enforced)

| Namespace | Holds |
| --- | --- |
| `SharedKernel.Persistence` | every registration/builder extension of every persistence package: `AddSharedKernelPostgres`, `EfCorePersistenceBuilder<T>`, `UseMultiTenancy`, `UseAuditTrail`, `UseFieldEncryption`, `UsePostgres`, `RowLevelSecurityCheckMode`, `AddSharedKernelNpgsql`, `AddSharedKernelDapper`, `AddSharedKernelAuditLedger`, `AddSharedKernelCrossTenantScope` |
| `SharedKernel.Persistence.EfCore` | EF Core model/migration/query helpers: `Money`, `HasJsonbColumn`, `HasVectorColumn`/`HasVectorIndex`, `VectorOrderingExpressions`, `IsTenantShared`, `Encrypt`/`WithBlindIndex`/`BlindIndexNormalization`, `WhereEncryptedEquals`, `UseCrossTenantConnection`, `EnableTenantRowLevelSecurity*`, `CreateAuditLedgerTable`, `CreateTenantEncryptionKeyTable` |
| `SharedKernel.Persistence.EfCore.Context` | `SharedKernelDbContext`, `TenantedDbContext`, `PersistenceContextDependencies`, `PersistenceFilterNames`, `ICallerDbContextFactory<T>` |

Contracts stay in `SharedKernel.Persistence.Abstractions.{Repositories,Context,Connections,Coordination,Diagnostics}`
and the feature namespaces (`...EfCore.Auditing`, `...EfCore.Encryption.*`, `...Dapper.Sessions`, `...Npgsql.*`).
`00.Governance`'s `PersistenceNamespaceConventionRules.FindMisplacedExtensions` fails the build when an extension
method on `IServiceCollection`/`IHostApplicationBuilder`/`EfCorePersistenceBuilder<T>`/`DbContextOptionsBuilder` lives outside
`SharedKernel.Persistence`, or an EF Core builder/migration/query extension outside `SharedKernel.Persistence.EfCore`.
No `*.Extensions` namespace exists in this domain.

---

## Technology Stack

| Concern | Technology |
| --- | --- |
| ORM | `Microsoft.EntityFrameworkCore` 10 + `Npgsql.EntityFrameworkCore.PostgreSQL` |
| Naming | `EFCore.NamingConventions` (`UseSnakeCaseNamingConvention(InvariantCulture)`) + internal `PostgresIdentifierLengthConvention` (63-byte UTF-8 cut + FNV-1a suffix) + `OwnedSharedTableKeyColumnConvention` |
| Concurrency | PostgreSQL `xmin` (shadow `uint` on every aggregate root; `RowVersion` mapped to it for `IHasConcurrency`), exposed only as an opaque `EntityVersion` token: `xmin` ‖ aggregate binding enciphered as one AES-256 block under an HKDF subkey of the service's key provider (P-562 X4) |
| Retry | Npgsql execution strategy, **on by default** (`MaxRetryCount` 6, `MaxRetryDelay` 30 s; `ConfigureProvider(o => o.MaxRetryCount = 0)` turns it off) |
| Micro-ORM | `Dapper` behind `IDbSession`; process-wide type map set only by `DapperConfiguration.Apply` |
| Crypto | `01.Core` `SharedKernel.Cryptography` (`IEncryptionKeyProvider`, `IEnvelopeEncryptionProvider`, `IHmacSigner`) + BCL `AesGcm`/HKDF |
| Vectors | `Pgvector` / `Pgvector.EntityFrameworkCore` (opt-in `UseVector`) |
| Telemetry | `ActivitySource`/`Meter` `"SharedKernel.Persistence"`, `"SharedKernel.Persistence.EfCore.Auditing"` (both), `Meter` `"SharedKernel.Persistence.EfCore.Encryption"`, Npgsql's own `"Npgsql"`; wired by `13.ServiceDefaults`' `WithPersistenceTelemetry()` by string name. Dapper emits no spans (Npgsql traces every command) |
| Logging | `[LoggerMessage]`, `LoggingEventIdRanges.Persistence` + offset: EfCore 6000–6099 (context/UoW 6014–6021, entity versions 6022–6026), Abstractions `CrossTenantScope` 6150, E2 range 6200–6299 reserved, Npgsql 6300–6399, EF RLS 6350–6351, Dapper 6400–6499, Encryption 6500–6699, Auditing 6700–6899 |

---

## Interface Contracts

### Shared contracts (owned by `05.Application/SharedKernel.Application.Abstractions`, implemented here)

- **`IUnitOfWork`** (`SharedKernel.Application.Transactions`): `SaveChangesAsync`, `IsTransactionActive`,
  `ExecuteInTransactionAsync(op[, isolationLevel])` / `<TResult>`, `OnBeforeCommit(callback)`. The operation may
  run more than once (retry); a returned failed `Result` (`IHasSuccessFlag`) rolls back; a call inside an active
  transaction joins it; `OnBeforeCommit` callbacks run after the last save, before commit, and are discarded with a
  retried attempt. `CommitOutcomeUnknownException` (commit failed without a server response — never replayed) and
  `TransactionRolledBackException` (an outer success on a rollback-only transaction). There is no
  `BeginTransactionAsync`: a caller-held handle cannot be replayed by a retrying strategy.
  EfCore adds `IUnitOfWork<TContext>` and a keyed `IUnitOfWork` per context type.
- **`IRequestContext`** (`SharedKernel.Application.Context`): who is calling — `IsAuthenticated`, `UserId`,
  `TenantId` (`Guid?`), `ActorKind` (`User`/`Service`/`System`/`Anonymous`), `ClientId`, `SessionId`,
  `ImpersonatorId`, `HasPermissionAsync`. Replaces P-557's `ICurrentActorContext`/`ICurrentTenantContext` (deleted).
  Default when none is registered: `AnonymousRequestContext` (no tenant → fail closed).
- **`IAuditTrailWriter`/`AuditEntry`/`AuditOutcome`** (`SharedKernel.Application.Auditing`): the caller supplies
  only action, resource, snapshots, outcome, error code, approval id, idempotency key; the writer resolves actor,
  tenant, client, session, time, trace and correlation itself.

### `SharedKernel.Persistence.Abstractions`

- **Repositories.** `IReadRepository<T,TId>` is **never tracked**: `GetByIdAsync` (whole aggregate), `GetByIdsAsync`,
  `ExistsAsync`, `FirstOrDefaultAsync(spec)`, `ListAsync`, `CountAsync` (`long`), `AnyAsync`,
  `ListPagedAsync(spec, PageRequest)`, `ListKeysetAsync<TKey>(spec, CursorPageRequest, keySelector, descending)`,
  `StreamAsync`, and `*ProjectedAsync` variants taking `IProjectionSpecification<T,TResult>` (projections keep a
  distinct name because `IRepository` re-declares `ListAsync`, which would hide a same-named overload).
  `IRepository<T,TId> : IReadRepository<T,TId>` is **always tracked**: `new` `GetByIdAsync`/`FirstOrDefaultAsync`/
  `ListAsync`, `AddAsync`/`AddRangeAsync`, `UpdateAsync(agg[, EntityVersion expectedVersion])`, `UpdateRangeAsync`,
  `DeleteAsync(agg[, EntityVersion])`, `DeleteRangeAsync`. Paging is always at the call site; a specification that
  pages itself, an offset page without a primary sort, or a keyset spec that declares ordering throws
  `InvalidOperationException`; a malformed cursor throws `ValidationException(pagination.cursor.invalid)`.
- **`EntityVersion`** — opaque readonly struct holding only a **sealed token** (21 bytes: format `0x01` + 20 provider
  bytes), never a database value (P-562 X4). `ToString()`/`TryFormat` is the ETag value — 28 characters of unpadded
  Base64Url; `Parse`/`TryParse` (and `IParsable`, so `IfMatch<EntityVersion>` binds) accept exactly that shape, quoted
  or `W/`-prefixed, and **never a number**; `[JsonConverter(EntityVersionJsonConverter)]` writes the token string
  (`None` → `null`) and reads the empty object `{}` the pre-X4 type always serialized to as `None` (documents stored
  before the upgrade, e.g. replayed idempotent responses, keep deserializing). `None` = never saved, no text form. The former raw seam `FromRowVersion`/`ToRowVersion` is gone:
  the only producer of tokens is EfCore's codec, reached through `ConcurrencyVersion`.
- **Bulk.** `IBulkMutationRepository<T,TId>`: `ExecuteUpdateAsync(spec, Action<BulkUpdateSetters<T>>)`,
  `ExecuteDeleteAsync(spec)` (soft-deletes `ISoftDeletable` rows), `ExecutePurgeAsync(spec)` (always physical).
  The spec must carry `Criteria` or be `AllRowsSpecification<T>`.
- **`ICrossTenantScope`** — `IsActive`, `Enter(string reason)` → `IDisposable`. `CrossTenantScope` keeps its depth on
  the **instance** (registered scoped), so an `Enter` anywhere in a DI scope — including inside an awaited helper —
  is visible to that scope's contexts/sessions until disposed, and never to another scope. Reason mandatory; every
  entry logged (6150) with actor, actor kind, tenant, reason and counted (tag `persistence.actor_kind`).
- **Infrastructure seams** (implemented by `.Npgsql`/`.EfCore`, hidden from IntelliSense): `ITenantSessionBinder.BindAsync(connection, transaction, tenantId)`
  (transaction-local only), `IAmbientDbTransaction.Current`, `IMigrationLock`, `IAdvisoryTransactionLock`.
- **Diagnostics.** `DatabaseReadinessResult` + `IDbConnectionFactory.CheckReadinessAsync` (bounded, never echoes
  `ex.Message`). No `IHealthCheck` here — `13.ServiceDefaults.Persistence` wraps it.

Specifications themselves (`ISpecification<T>`, `Specification<T>`, `Spec.For<T>()` builder, `ProjectionSpecification<T,TResult>`,
`IProjectionSpecification<T,TResult>`, typed `ThenInclude`, `And`/`Or`/`Not`) live in `03.Domain`.

### `SharedKernel.Persistence.EfCore`

**Entry point.** `builder.AddSharedKernelPostgres<TContext>("name", p => ...)` (`IHostApplicationBuilder`) or
`services.AddSharedKernelPostgres<TContext>(configuration, "name", p => ...)`. Callback style, no `.Build()`.
Builder members: `ConfigureProvider` (`PostgresProviderOptions`: `UseVector`, `MaxRetryCount`, `MaxRetryDelay`,
`AdditionalTransientErrorCodes`), `ConfigureDataSource((sp, ds) => …)`, `UseDataSource(NpgsqlDataSource)`,
`ConfigureDbContext((sp, o) => …)` (compiled model: `o.UseModel(...)`), `UseDbContextPooling(poolSize)`,
`UseServiceName`, `UseUuidV7Keys`, `MigrateOnStartup(lockTimeout?)`, `AddSeeder<T>`, `AddInterceptor<T>`; extensions
`UseMultiTenancy(rowLevelSecurity, rowLevelSecurityCheck)` (TenantedDbContext only), `UseAuditTrail()`,
`UseFieldEncryption(k => …)`. Registering the same `TContext` twice throws.

Registered per context: inner (pooled or plain) **singleton** `IDbContextFactory<TContext>` under a private key;
public **scoped** `IDbContextFactory<TContext>` (`TenantAwareDbContextFactory`: attaches the scope's `IRequestContext`,
`IDomainEventDispatcher` and `ICrossTenantScope` per lease); **singleton** `ICallerDbContextFactory<TContext>` (explicit
caller, for singletons/hosted services); scoped `TContext` (tracked by the scope's `UnitOfWorkCoordinator`);
`SharedKernelDbContext` unkeyed (first context) and keyed by `typeof(TContext)`; `IUnitOfWork<TContext>`, keyed
`IUnitOfWork`, unkeyed `IUnitOfWork` (first context); open-generic repositories (`RepositoryRegistration`: the
aggregate's context is found by probing the registered models once; ambiguity or no match is a clear error; closed
registrations win); the migration/seed hosted service when requested; `ValidateOnStart` model validation
(`PersistenceStartupValidator`). Shared, once: the PostgreSQL classifier (first), `PersistenceServiceOptions`
(`SharedKernel:Persistence`, `ServiceName`), `IClock`, `ICrossTenantScope` + anonymous `IRequestContext`,
`PersistenceContextDependencies`, the entity-version codec and its key warm-up (`EntityVersionKeyWarmUp`, a hosted
service), `IAmbientDbTransaction`, `UnitOfWorkCoordinator`, `ISpecificationEvaluator<>`.
Data source per connection name (`PostgresDataSources`): the first name is the unkeyed default, further names are
keyed by name; contexts with the same name share it.

**Context.** A derived context has exactly one constructor:
`(DbContextOptions<T> options, PersistenceContextDependencies dependencies)`. `PersistenceContextDependencies` has an
internal constructor; `PersistenceContextDependencies.Create(...)` exists for design-time factories, tools and tests.
`SharedKernelDbContext.RequestContext` and `.CrossTenantScope` are attached per lease and reset on
`Dispose`/`DisposeAsync` (the pool-return hook). `TenantedDbContext.CurrentTenantId` = `RequestContext.TenantId`.
Entity configurations: plain `ApplyConfigurationsFromAssembly(GetType().Assembly, ShouldApplyConfiguration)`
(skipped when the assembly has none); override `protected virtual bool ShouldApplyConfiguration(Type)` when several
contexts share an assembly. Conventions are created in `ConfigureConventions` once per model build; the model cache
key includes UUID v7 generation and the capability configurators.

**Conventions (always on, no base configuration class).** `DomainTypeMappings`: every `StronglyTypedId<T>` reachable
from the `DbSet`s or declared in the context's assembly gets a converter. `MoneyMapping`: every `Money` property is a
complex type `{p}_amount numeric(19,4)` + `{p}_currency char(3)`, required unless `Money?`; `builder.Money(...)` only
changes defaults. `DomainColumnConvention`: audit/soft-delete/tenant/version columns from the implemented interfaces,
TenantId index, `CreatedBy`/`CreatedOn` after-save `Ignore`. `XminConcurrencyTokenConvention` (first finalizing
convention): shadow `xmin` on every aggregate root. `SoftDeleteQueryFilterConvention`: named filter
`PersistenceFilterNames.SoftDelete`. `TenantIsolationConvention` (TenantedDbContext): see Multi-tenancy.
`ValueObjectOwnershipBuilder`: `IValueObject` properties → complex types. `EncryptAnnotationRegisteredGuardConvention`
(last): an `.Encrypt()` annotation without `UseFieldEncryption()` fails the model.

**Save pipeline.** One internal singleton `PersistenceSaveChangesInterceptor`: one `DetectChanges`, a snapshot with
auto-detect off, a key index for principal lookup; in order **soft delete** (a deleted `ISoftDeletable` becomes
modified; cascade-deleted dependents are soft-deleted or restored, so a soft-deleted order keeps its lines) →
**aggregate-root touch** (a changed owned entity, child or grandchild marks its aggregate root modified — `FindParent`
walks required FKs through tracked principals preferring composed roots — so the root's `xmin` is checked and
advanced) → **audit stamps** (actor = `UserId`, else `ServiceName`) → **tenant stamping and write guard**. Then
`SharedKernelDbContext.SaveChangesAsync` runs the domain-event dispatch loop **before** the physical save, on every
save path (unit of work, seeder, factory user). No dispatcher registered → events cleared + Warning 6014 (startup
warns 6016). A synchronous `SaveChanges` with pending events and a dispatcher throws. A throwing dispatcher clears
the change tracker and rethrows. Other always-on interceptors: `ProtectedColumnUpdateGuard`
(`IQueryExpressionInterceptor`: `ExecuteUpdate` may never set `TenantId`, `CreatedBy`/`CreatedOn` or a concurrency
token), `DomainClockMaterializationInterceptor` (attaches `IClock` via `IHasClock`). Capability packages' interceptors
run after the platform's and the user's (`IPersistenceOptionsExtension` is applied last) — encryption must be the last
`SavingChanges` interceptor.

**Errors.** Internal `PostgresDbUpdateExceptionClassifier` (always registered, first): 23505 → `ConflictException`;
23503 → `ValidationException` (missing reference) or `ConflictException` (`foreign_key_dependent_exists`), decided by
the FK in the model and the tracked changes on each side; 23502/23514/23P01/22001 → Validation; 40001/40P01/55P03/57014
→ transient Conflict; 42501 (incl. an RLS `WITH CHECK` failure) → `ForbiddenException`. A transient error is **not**
wrapped while retry is on, so the strategy can retry it. Every `DbUpdateConcurrencyException` →
`ConflictException` (`persistence.concurrency_conflict`) carrying the current version (`ConcurrencyVersion.TryGetCurrentVersion`);
a proven cross-tenant write is logged/counted but answered with the **same** Conflict (no existence oracle).

**Concurrency API.** `ConcurrencyVersion.Get(db, entity)`, `SetExpected(db, entity, EntityVersion)`,
`TryGetCurrentVersion(exception, out version)`, `ConflictErrorCode`. `UpdateAsync/DeleteAsync(detached)` on an
aggregate whose version is a shadow `xmin` throws `InvalidOperationException` pointing at the `expectedVersion` overload
(a detached root carries no version); aggregates with a CLR `RowVersion` attach as before. `Get` on an entity the context
does not track (shadow `xmin`, e.g. loaded by `IReadRepository`) throws instead of reporting a version; `Get` on an entity
never saved returns `EntityVersion.None`.

**Opaque versions (P-562 X4, `Concurrency/EntityVersionCodec.cs`, `EntityVersionKeyRing.cs`).** The raw `xmin` is a
transaction counter shared by the whole database, so it never leaves `ConcurrencyVersion`: every version is sealed.

- *Construction* — encode-then-encipher with AES-256 as a single-block permutation: block = `BE32(xmin) ‖ binding[12]`,
  binding = `SHA-256("SharedKernel.Persistence.EntityVersion.Binding/1" ‖ LP(root entity type name) ‖ LP(each primary-key
  provider value))[..12]`; token = `0x01 ‖ key check value[4] ‖ AES-256(K, block)`. Opening deciphers with the key whose
  check value matches and compares the 96-bit binding in fixed time (`FixedTimeComparison`) — a token of another aggregate
  (even with the same `xmin`), an altered or forged one opens with probability 2^-96. Deterministic (one block: no nonce,
  IV or padding), so the same version of the same aggregate always has the same ETag (`If-None-Match` works). Not
  AES-GCM/`ISymmetricEncryptionService` (random nonce: not deterministic); not SIV/GCM-SIV (not in the BCL, and the
  single-block PRP is the minimal deterministic AE for a one-block message); never XOR-with-a-keyed-hash (leaks `xmin`
  differences). `xmin` 0 is never sealed.
- *Keys* — `K` = HKDF-SHA256 subkey (`SubkeyDerivation`, purpose `"SharedKernel.Persistence.EntityVersion"` — what
  `provider.ForPurpose(...)` returns) of the service's root key provider: the `ISynchronousEncryptionKeyProvider` in the
  container, else the `IEncryptionKeyProvider` (used synchronously when it is also in-memory, e.g.
  `StaticEncryptionKeyProvider`; otherwise bridged, see *Warm-up*; refreshed in the background every 5 min, a failed
  refresh keeps the key and logs 6023). The check value is a separate HKDF output (purpose `….KeyCheck`).
  Root keys ≥ 32 bytes. One `EntityVersionCodec` singleton per service provider, attached to every context through
  `PersistenceContextDependencies.EntityVersions`; `PersistenceContextDependencies.Create(..., entityVersionKeys:)` for
  hand-built contexts.
- *Warm-up (asynchronous-only providers, `EntityVersionKeyWarmUp`)* — a hosted service registered once by
  `AddSharedKernelPostgres` loads the current key in `StartAsync`, before the host takes traffic, with no thread
  blocked: `EntityVersionKeyRing.WarmUpAsync` runs the provider call on the thread pool and waits at most
  `WarmUpTimeout` (10 s); after a timeout the load goes on and publishes the key when the provider answers. A failure
  (6025) or a timeout (6026) is logged and never fails startup. Once the key is loaded, request threads never call the
  provider (the background refresh does). **Fallback:** when no key is loaded — no host (a hand-built context, a tool),
  or the warm-up failed or has not finished — the first request that needs a version loads it on its own thread,
  blocking it under a lock (X4's original path). A no-op for in-memory providers and without a provider.
  **Readiness does not wait for the key** (decision, P-562 I4): persistence works without it — only issuing and
  checking versions needs it — so a key service outage must not take every endpoint out of rotation, and after a
  successful warm-up no traffic arrives before the key anyway. A service whose readiness should follow its key service
  adds 13's `AddKeyVaultKeyProviderReadinessCheck()`. `IPersistenceStartup` (schema readiness) never waits for it
  either.
- *Rotation* — sealed with the current key; opened with any key that was current earlier **in this process** (the 64
  most recent). Keys are never looked up by anything a client sends. A token under an unknown key (e.g. issued before a
  restart that rotated the key) is a stale version: `ConflictException` (`persistence.concurrency_conflict`), never a
  500 — `14.Presentation` answers it 412 when the request carries `If-Match`/`If-None-Match` (R7), 409 otherwise.
- *Failure semantics* — `ResolveExpected` opens the version **before** the repository attaches a detached aggregate, so
  a rejected version leaves the change tracker untouched; rejection is logged at Debug (6022, reason only, never the
  token). No key provider registered: `Get`/`SetExpected` throw `InvalidOperationException` naming what to register; the
  conflict translator then attaches no current version (`TryGetCurrentVersion` false) — a conflict is still a conflict.
  Sealing inside the translator never throws (6024).

**Repositories.** `EfReadRepository<T,TId>`/`EfRepository<T,TId>` are concrete and subclassable (public ctor
`(SharedKernelDbContext)`, virtual members, `protected virtual IQueryable<T> AggregateQuery()` used by
`GetByIdAsync`/`GetByIdsAsync` — put `Include`s there; `AutoInclude` is honoured). Tracked members use `AsTracking()`,
the read contract `AsNoTracking()`, regardless of the context default. Keyset: seek predicate with closure-held
parameters, strongly-typed id unwrap, `Take(limit+1)`, projected pages in SQL, compiled key accessors cached per member
path; nullable keys are rejected. Bulk setters are recorded in `BulkUpdateSetters<T>` and replayed onto EF's builder
(no EF internal API); targets resolve through the full member chain incl. nested complex properties and **fail closed**
(PK, any concurrency token, `TenantId`, `CreatedBy`/`CreatedOn`, encrypted columns, unresolvable/`EF.Property`);
`ExecuteUpdateAsync` stamps `ModifiedOn/By` unless the caller sets them. `TenantedRepository<T,TId>`:
`GetByIdForTenantAsync`/`GetByIdForTenantIncludingDeletedAsync` (require an active cross-tenant scope). Every
repository method is traced; every query is `TagWith`'d with the specification's type name.

**Unit of work — one transaction per DI scope (`UnitOfWorkCoordinator`, scoped).** Every scope-resolved context
registers with the coordinator; every `EfUnitOfWork<TContext>` delegates to it.

- `ExecuteInTransactionAsync` begins the transaction on the calling context and moves every tracked context that
  reaches the **same database** (host, port, database, user, and the same side of the cross-tenant switch) onto that
  connection (`SetDbConnection(owned:false)` + `UseTransaction`); contexts resolved during the transaction join on
  resolution. Before commit every enlisted context with changes is saved (until quiescent, 16 passes max), then the
  `OnBeforeCommit` callbacks run (a second save if they staged EF changes), then commit. Afterwards each joined
  context is moved back to its data source.
- A context that cannot join (another database/role, its own open connection or transaction) holding changes at
  commit → `InvalidOperationException`, rollback. **No two-phase commit.**
- Nested calls (any context's UoW) **join**. A joined operation that throws or returns a failed `Result` marks the
  transaction **rollback-only**: the outermost call rolls back, returning its own failed result, or throws
  `TransactionRolledBackException` if it would have succeeded.
- Retry: the whole delegate runs inside the execution strategy; trackers of enlisted contexts are cleared before a
  **retried** attempt only; starting with changes already staged under a retrying strategy is refused (they would be
  lost on replay).
- Commit: a failure with no `PostgresException` in the chain (I/O, timeout, cancellation) →
  `CommitOutcomeUnknownException`, never retried, trackers cleared; a server-rejected commit (40001, deferred
  constraint) rolled back and is retried/reported normally.
- `SaveChangesAsync` outside a transaction: the owner alone → plain save; owner + joinable contexts with changes →
  one transaction in the strategy (`acceptAllChanges:false`, `AcceptAllChanges` after commit).
- The transaction is published on `IAmbientDbTransaction` (Dapper sessions and the audit writer enlist) and the
  previous value is **restored** at the end, never blindly cleared.

**Startup.** `IPersistenceStartup` (`IsCompleted`, `WaitAsync`) completes when every context's
`MigrateOnStartup`/seeders finished (immediately when none), fails when they fail. `MigrationAndSeedHostedService`
(`StartAsync`, blocks host start) runs migrations over `NpgsqlDataSourceKeys.Migration` when registered, under
`IMigrationLock` (`sk:migration:{Context}`, default 2 min; EF Core 9+'s own migration lock is taken after it, same
order on every replica); seeders run as `SystemRequestContext("seeder:{Type}")` and, on a `TenantedDbContext`, inside
the context's cross-tenant scope — under RLS on `UseCrossTenantConnection()` (needs `CrossTenantConnectionString`); a
pooled context whose connection is swapped gets an unpooled copy. Order at host start: `ValidateOnStart` (options,
model) → hosted `StartAsync` (entity-version key warm-up, Npgsql RLS privilege check, migrations/seeders, encryption
key-ring load) → `StartedAsync`
(RLS coverage check, audit self-check — both after `IPersistenceStartup.WaitAsync`) → background sealer (waits too).
`AddDatabaseReadinessCheck<T>` is Unhealthy until `IPersistenceStartup.IsCompleted`.

**Design-time.** `PostgresDesignTimeDbContextFactory<TContext>(connectionName)`: connection from `--connection`, then
`SharedKernel:Persistence:{name}:MigrationConnectionString`, `SharedKernel:Persistence:Npgsql:MigrationConnectionString`,
`ConnectionStrings:{name}` (appsettings*.json + environment), retry off; abstract `Create(options, dependencies)`;
virtual `ConfigurePersistence(EfCorePersistenceBuilder<TContext>)` — the service's capability calls, from which only the
model conventions/configurators are taken (no interceptor, key or connection resolved), so the migration model equals
the runtime model (encrypted widths, blind-index columns). Without it an `.Encrypt()` model refuses to build.

### Multi-tenancy

Three independent layers; each alone is correct, together they are defense in depth.

1. **Model (`TenantIsolationConvention`, model-finalizing, TenantedDbContext only).** Every `IHasTenant` root type gets
   the named query filter `PersistenceFilterNames.Tenant` (`e.TenantId == CurrentTenantId`, fails closed on `null`)
   and `TenantId` as a **concurrency token** (a detached stub carrying another tenant's key matches zero rows). Every
   other non-owned, non-property-bag entity type must be `[TenantShared]` (`03.Domain`) or `builder.IsTenantShared()`
   — otherwise the model build fails. Children are tenant data: same filter, guard, token and RLS as roots.
2. **Save pipeline.** An added `IHasTenant` with `TenantId == Guid.Empty` is stamped with its tracked aggregate root's
   tenant, else the caller's; then the write guard rejects any add/modify/delete outside the current tenant or a
   tenant change (actionable `TenantIsolationErrors` messages). Skipped only while the context's
   `CrossTenantScope.IsActive`. Always on for a `TenantedDbContext` (`UseMultiTenancy()` states intent + RLS switch).
3. **PostgreSQL row-level security** (`UseMultiTenancy(rowLevelSecurity: true)`; Npgsql
   `RowLevelSecurity.Enabled`). One setting, `app.tenant_id`, bound **transaction-locally only**
   (`set_config(..., true)`), never for the session — safe behind transaction-mode PgBouncer and connection reuse.
   Internal EF interceptors: inside a transaction bind once per (context, EF `TransactionId`, tenant), rebind on tenant
   change or savepoint rollback; a query carries the bind in its own round trip (prefix), a `SaveChanges` batch gets a
   separate `set_config` command first (EF checks per-statement row counts by position); outside a transaction every
   command is prefixed with a `DO` block (one implicit transaction per command, no result set); `SaveChanges` under
   RLS is forced into a transaction (`AutoTransactionBehavior.Always`); migration commands untouched.
   Policy (`EnableTenantRowLevelSecurity(table, tenantColumn, schema, crossTenantRole?)` /
   `EnableTenantRowLevelSecurityForModel(model, crossTenantRole?)`): `ENABLE` + `FORCE`, one predicate
   `tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid` for `USING` and `WITH CHECK`, uses the tenant
   index; optional role-specific `USING (true)` policy for a cross-tenant role; `Disable*` drops policies, `NO FORCE`,
   `DISABLE`. Identifiers above 63 bytes are rejected; policy names go through the same truncate-and-hash. A migration's
   `TargetModel` is property bags (no CLR types): `DomainColumnConvention` stamps `SharedKernel:Persistence:Tenant` on every
   non-owned `IHasTenant` type and `TenantTables` reads the CLR type or that annotation; `ForModel` throws on zero tables.
   **Cross-tenant = a separate database role** (`BYPASSRLS` or the role-specific policy) on the keyed
   `NpgsqlDataSourceKeys.CrossTenant` data source: inside an active scope EF commands on the application connection
   throw — call `context.Database.UseCrossTenantConnection()`; using that connection outside a scope throws; Dapper
   sessions switch automatically; encryption maintenance uses it by itself. There is **no escape token**.
   Startup checks: Npgsql `RowLevelSecurityStartupCheck` (`PrivilegeCheck` Fail/Warn/Disabled, default Fail) — runtime
   role superuser, `BYPASSRLS`, owner or member of owner of an RLS table, or a permissive non-tenant policy applying to
   it (PUBLIC, itself, any role it is a member of); EfCore `RowLevelSecurityCoverageCheck<T>`
   (`RowLevelSecurityCheckMode` Fail, Warn in Development, Off) — every tenant table of the design-time model has RLS
   enabled + forced + a policy reading `app.tenant_id` (one catalog query). Options validation refuses `Multiplexing`
   and `No Reset On Close` with RLS.

### `SharedKernel.Persistence.Npgsql`

Connection name → `ConnectionStrings:{name}`; settings in `SharedKernel:Persistence:{name}` (a `ConnectionString` key
there overrides); only the unnamed `AddSharedKernelNpgsql(configuration)` reads `SharedKernel:Persistence:Npgsql`.
Reserved names: `Encryption`, `Auditing`, `Dapper`, `Npgsql`. `NpgsqlPersistenceOptions`: `SslMode?` (null = honour
the connection string, else `VerifyFull`; loopback → `Disable`; below VerifyFull allowed for loopback or Development,
else needs `AcknowledgeInsecureSslMode`, warning 6300; GSS encryption `Disable` unless the connection string sets
`GSS Encryption Mode`), statement/lock/idle timeouts, `MigrationConnectionString`,
`ReadOnlyConnectionString`, `RowLevelSecurity { Enabled, CrossTenantConnectionString, PrivilegeCheck }`,
`EnableDynamicJson` (opt-in), `UseVector`. Validated at start, messages never echo a connection string. Keyed data
sources exist only when configured (read-only falls back to multi-host `PreferStandby`, then primary).
`AdvisoryLockKeys`: `sk:migration:`/`sk:audit:` namespaces, `ToKey` = FNV-1a 64. Bounded xact lock restores the
previous `lock_timeout` and rounds up to ≥ 1 ms. Npgsql's per-command Information log is lowered to Debug on data
sources built here. `PostgresErrorMapping.TryAsync` gives Dapper/raw ADO the same `Error` as EF Core.

### `SharedKernel.Persistence.Dapper`

`IDbSessionFactory` (scoped): `OpenAsync()`, `OpenReadOnlyAsync()`, `OpenAsync(DbSessionOptions { ReadOnly,
IsolationLevel, EnlistInAmbientTransaction })`. `IDbSession`: `Connection`, `Transaction`, `IsEnlisted`, `IsReadOnly`,
`TenantId`, `RequireTenantId()`, `Command(sql, params, ct)` (adds transaction + `DefaultCommandTimeoutSeconds`),
`CommitAsync`. Inside a unit of work it enlists (commit/dispose no-ops); otherwise own connection + transaction,
dispose without commit rolls back (a rollback failure is logged 6400 and never masks the original error). Read-only
= `SET TRANSACTION READ ONLY` on the read-only data source. RLS on → tenant bound through `ITenantSessionBinder`;
inside a cross-tenant scope → cross-tenant data source (refused inside a unit of work). `DapperConfiguration.Apply`
is the one place the process-wide type map is set (`AddSmartEnum`, `AddStronglyTypedId`, `AddJsonb(JsonTypeInfo<T>)`,
`AddTypeHandler`, `MatchNamesWithUnderscores`; pgvector types always). `AddSharedKernelDapper` also TryAdds the
anonymous `IRequestContext` and `ICrossTenantScope`, so a Dapper-only service works.

### `SharedKernel.Persistence.EfCore.Auditing` — audit ledger v3 (async sealer)

- **Request path** (`UseAuditTrail()` → internal `EfAuditTrailWriter`): one statement (CTE) inserting
  `audit_records` + `audit_record_payloads`; no sequence, hash, lock or retry on the request path. `Succeeded` must run
  inside `IAmbientDbTransaction.Current` (throws otherwise) and commits with the business write — `05`'s
  `AuditingCommitBehavior` queues it on `OnBeforeCommit`. `Failed` is one autocommitted statement on its own
  connection. Idempotency: `ON CONFLICT DO NOTHING` on `(tenant_id, resource_type, idempotency_key) NULLS NOT
  DISTINCT`. Field lengths validated before any SQL (`AuditFieldLimits`). Identity from `IRequestContext` +
  `ServiceName`; an unauthenticated caller is always recorded `Anonymous`, never `System`; W3C trace id and baggage
  correlation id captured. A `null` tenant is allowed only for an authenticated `System` actor or inside an active
  cross-tenant scope (writer, queries, verification, checkpoints, erasure).
- **Sealer** (`AuditSealerHostedService` → `AuditSealingEngine`): each pass is one transaction taking
  `pg_try_advisory_xact_lock(ToKey("sk:audit:sealer"))` (leader per pass, pooler-safe), selecting records with
  `insert_xid < pg_snapshot_xmin(pg_current_snapshot())`, `insert_xid >= floor` and **no link yet**, in `(insert_xid,
  id)` order (commit-safe: a long transaction that commits late is sealed in xid order), appending insert-only
  `audit_chain_links` rows (per-chain sequence, previous MAC, MAC, key id, algorithm, format). `insert_xid` is stamped
  by a BEFORE INSERT trigger. The floor is learned in-process from its own scans (a new process scans the sealed
  prefix once); a forged max-xid link cannot stall it. Chains: one per `(TenantId, ResourceType)`; the unique
  `(tenant_id, resource_type, sequence) NULLS NOT DISTINCT` index is the second fence. Checkpoints every
  `Sealer:CheckpointInterval` to `IAuditCheckpointSink` (default insert-only `audit_checkpoints`). Optional separate
  sealer role: `Sealer:DataSourceName` (a keyed `AddSharedKernelNpgsql` registration).
- **Format AUDITv3** (`AUDIT-FORMAT.md`, test vectors parsed by `AuditFormatVectorTests`): big-endian RFC 9562 GUIDs,
  µs timestamps, presence flags, domain separators `AUDITv3`/`AUDITv3-PAYLOAD`/`AUDITv3-CHECKPOINT`, key id + algorithm
  inside the MAC. The chain commits to `SHA-256(salt ‖ payload)`; payload + salt live in erasable
  `audit_record_payloads`.
- **Keys**: `AuditLedgerOptions` (`SharedKernel:Persistence:Auditing`): `CurrentKeyId`, `Keys{id:{Material,Order}}`
  (≥ 32 bytes, current = newest), `CheckpointSigningKeyId`, `AcceptedCheckpointSigningKeyIds`,
  `Sealer{Enabled,Interval,BatchSize,CheckpointInterval,DataSourceName}`, `SelfCheck` (Off/Warn/Fail, default Warn —
  use Fail in production). `IAuditRecordAuthenticator` (async, KMS-capable); default keyring over `IHmacSigner`; a
  custom authenticator registered first wins.
- **Verification**: `AuditVerificationStatus` Intact/Broken/Unverifiable + `AuditVerificationFailureKind`
  (SequenceGap, HashMismatch, LinkMismatch, KeyRegression, AnchorMismatch, TailTruncated, UnknownKey, PayloadErased,
  NotSealed). Checkpoint anchors are re-authenticated. Checkpoint creation is scope-gated, verifies incrementally from
  the latest authentic checkpoint before signing, and the verifier accepts only pinned signing key ids.
  `IAuditLedgerMaintenance`: `SealPendingAsync`, `SealAllChainsAsync(reason)` (after a key compromise; later forgeries
  under the old key → KeyRegression), `ErasePayloadAsync`/`EraseResourcePayloadsAsync` (GDPR; chain stays Intact,
  reported as erased). Export and cross-tenant queries audit themselves (`AuditLedgerActions`).
- **Immutability**: triggers `ENABLE ALWAYS` rejecting UPDATE/DELETE/TRUNCATE on records, links, checkpoints and
  UPDATE/TRUNCATE on payloads (SQLSTATE 42501) + privileges from `CreateAuditLedgerTable(runtimeRole, sealerRole)`
  (revokes default privileges, grants exactly) + startup self-check (superuser, owner/member, UPDATE/DELETE/TRUNCATE,
  RLS on a ledger table, missing/disabled trigger, runtime INSERT on links when a separate sealer is configured). The
  ledger is **not** part of any EF Core model and is **never** RLS-protected (the sealer sees every tenant).
- `AddSharedKernelAuditLedger(IConfiguration)` is the idempotent service-collection form; `UseAuditTrail()` uses the
  configuration given to `AddSharedKernelPostgres`.

### `SharedKernel.Persistence.EfCore.Encryption` — field encryption v3

- **Interceptor-based, never a `ValueConverter`** (associated data needs the primary key and tenant). Encrypt in
  `SavingChanges` (only properties whose `IsModified` is true), decrypt in `IMaterializationInterceptor`; after a save
  the entry is reset to plaintext and unchanged (no re-encryption on the next save). Stale pending entries are
  restored at the start of each save.
- **Crypto**: AES-256-GCM (BCL `AesGcm`), random nonce, **per-purpose HKDF-SHA256 key** (cached per key id +
  purpose), payload = `01.Core` `EncryptedPayload` (key id = root key id or `skt:{tenant}`), AAD tag `SKENC2` binding
  purpose, canonical primary key and tenant. Purposes are unique model-wide (TPH siblings on one column allowed).
- **Model**: `.Encrypt(purpose)` on entity and complex properties at any depth (one recursive traversal helper shared
  by convention, guard, interceptor and maintenance); rejected at model build: non-string, complex collections,
  JSON/struct complex types, composite/shadow/unsupported primary keys. `HasMaxLength(n)` is widened to the
  ciphertext bound. Annotations are strings/flags/names only (migrations and compiled models scaffold).
- **Blind index**: `.WithBlindIndex(BlindIndexNormalization flags, string? normalizer)`, shadow
  `Path_With_UnderscoresBlindIndex` column; separate versioned keys (`IBlindIndexKeyProvider`, values `v1:<hex>`),
  HMAC over tenant ‖ normalized value; lookups match every configured version. Query with
  `WhereEncryptedEquals(x => x.Email, value[, tenantId])` (purpose, normalization, tenant from the model/context).
- **Query guard** (`EncryptedMemberQueryGuard : IQueryExpressionInterceptor`): any use of an encrypted member other
  than `== null`/`!= null` — filter, order, group, join, projection, `ExecuteUpdate` setter — throws before the query
  runs; matched by member metadata token per model (no false positives). Raw SQL is not checked.
- **Keys**: default key source = the `IEncryptionKeyProvider`/sync provider already in DI (register once);
  `FromConfiguration()`, `UseKeyProvider<T>()`. Async providers are bridged by `FieldKeyRing` (startup load, periodic
  refresh, on-miss background fetch with 30 s cooldown, `MaxKeyStaleness` → keyed probe
  `FieldEncryptionServiceKeys.KeyRingProbe` unhealthy). Options `SharedKernel:Persistence:Encryption`, validated at start.
- **Maintenance** (`IEncryptionRotationJob.RunAsync(EncryptionMaintenanceRequest, progress, ct)`): modes
  `VerifyOnly | ReEncrypt | RecomputeBlindIndexes | EncryptPlaintext` (flags); targets are distinct
  (schema, table, column) from relational mappings (TPH/TPT/TPC); one batched CAS `UPDATE … FROM unnest(...)` per batch;
  checkpoint by column; each batch in its own transaction with `SET LOCAL row_security = off` on the cross-tenant data
  source (errors instead of silently filtering), row-count sanity check when `RequireRowSecurityBypass = false`;
  `report.IsSafeToRetire(keyId)`. **Requires a caller-entered cross-tenant scope — never enters one itself.**
- **Tenant data keys / crypto-shredding** (`UseTenantDataKeys()`): per-tenant data key wrapped by an
  `IEnvelopeEncryptionProvider`, stored in `sk_tenant_encryption_keys` (`CreateTenantEncryptionKeyTable`), created on
  first write. `ITenantEncryptionKeyManager.ShredTenantAsync(tenantId, TenantShredOptions?, ct)` (caller-entered scope
  required) tombstones the key, clears the tenant's blind indexes, and refuses (`TenantShredIncompleteException`)
  when root-key or plaintext values of that tenant remain unless `AllowIncompleteErasure`. After a shred, root-key
  values of that tenant are refused on read; writes re-check the tombstone in the write transaction (`FOR SHARE` on
  the key row, serialized with the shred); maintenance never rewrites a shredded tenant's rows.

---

## Implementation Rules

**Invariants — do not break (each was a real defect once):**

- **A derived context has exactly one constructor parameter besides options: `PersistenceContextDependencies`.**
  Per-capability constructor parameters let a context compile into a silently unprotected state.
- **Identity, dispatcher and cross-tenant scope are attached per lease, never through a context constructor**, and
  reset on dispose — this is what makes pooling safe with multi-tenancy. Singleton infrastructure (interceptors) reads
  identity from the context being saved, never from a captured field.
- **One transaction per DI scope.** Every scoped context registers with `UnitOfWorkCoordinator`; never start a
  transaction on a context directly (`Database.BeginTransaction` fails under the retrying strategy anyway), never
  clear the ambient transaction blindly (restore the previous value), never let a joined failure commit.
- **An ambiguous commit is never replayed** (`CommitOutcomeUnknownException`); a server-rejected one may be.
- **Tenant binding is transaction-local only.** Never `set_config(..., false)`, never a connection-open interceptor,
  never a second setting or an escape token in policy text. Cross-tenant work uses a separate role.
- **Every entity type of a `TenantedDbContext` is tenant data or explicitly `[TenantShared]`.** Do not weaken
  `TenantIsolationConvention` to roots only — children were readable across tenants through their own `DbSet`.
- **Every production `IgnoreQueryFilters` is selective** (`[PersistenceFilterNames.SoftDelete]` or, under an active
  scope, `[..., PersistenceFilterNames.Tenant]`). The parameterless overload drops the tenant filter too.
- **Bulk setters fail closed** — an unresolvable target is rejected, never passed through.
- **Any traversal of `.Encrypt()` annotations uses the shared recursive helper** (`PersistenceModelAnnotationNames.GetPropertiesIncludingComplex`);
  a one-level loop is exactly how nested complex properties were once stored in plaintext.
- **Maintenance and shredding never enter the cross-tenant scope themselves** — the caller's entered scope is the
  authorization.
- **The audit request path takes no lock and assigns no sequence**; sealing is the sealer's job, in commit-safe xid
  order. `Succeeded` is written only inside the business transaction; never "fix" the ambient-transaction check by
  opening a standalone transaction.
- **An unauthenticated caller is never audited as `System`.**
- **The ledger stays outside EF Core models and outside RLS.**
- **Encryption stays the last `SavingChanges` interceptor** (options extensions are applied after platform and user
  interceptors).
- **Startup work that needs the schema waits for `IPersistenceStartup`** (audit self-check, sealer, RLS coverage
  check, database readiness).
- **Sibling packages pin EfCore (and EfCore pins Npgsql) to the exact version** — they use each other's internals.
- **The raw `xmin` never leaves `ConcurrencyVersion`** (P-562 X4). No public API creates an `EntityVersion` from a number
  or reads one out of it; a new producer of versions (a projection, a Dapper read model) goes through the codec with the
  same binding, never around it. Key lookups are never driven by client input (only keys the process made current).
- **A request thread does not call an asynchronous key provider** (P-562 I4). `EntityVersionKeyWarmUp` loads the version
  key before traffic; the blocking load in `EntityVersionKeyRing` is only the fallback (no host, failed or unfinished
  warm-up). Never make it the normal path again, and never make the warm-up fatal or a readiness condition.

**Hard violations:**

- Referencing MediatR, `SharedKernel.Application` (the MediatR package), `.Behaviors`, or `12.Security` from any
  persistence package. Only `SharedKernel.Application.Abstractions` is allowed (`PersistenceNeverReferencesApplicationOrSecurity`
  and `PersistenceForbiddenAssemblyReferences`, source and assembly level).
- `SharedKernel.Persistence.Dapper` or `.Npgsql` referencing EF Core; `.Abstractions` referencing an ORM, Npgsql or Dapper.
- Declaring `IUnitOfWork`, `IRequestContext`, `IAuditTrailWriter` (or the deleted `ITransactionalUnitOfWork`,
  `IPersistenceTransaction`, `ICurrentActorContext`, `ICurrentTenantContext`) anywhere but `Application.Abstractions`
  (`UnitOfWorkSeamRules.SharedContractsAreNotRedeclared`).
- A read repository that tracks (`PersistenceInterfaceOwnershipRules.ReadOnlyRepositoriesNeverTrack`, IL scan).
- Exposing `IQueryable<T>` from a repository contract; calling `SaveChanges` outside the unit of work/context save path.
- Interpolated SQL into `IDbSession.Command`/Dapper (`SK0042`); a `TenantedDbContext.OnModelCreating` override that
  skips `base.OnModelCreating` (`SK0201`).
- An outbox type; a hand-rolled encryption `ValueConverter`; hand-rolled AES.
- Referencing `SharedKernel.Persistence.Testing` (or `SharedKernel.Testing`) from production code
  (`TestingNeverReferencedByProduction`).
- Reflection in a per-row/per-call path. The documented exceptions: encryption's materialization-time setter
  resolution (`GetMemberInfo(forMaterialization: true, forSet: true)`), cached per model, and Dapper's own mapping.

**Conventions for new code:** new `[LoggerMessage]` events in the package's own EventId block; configuration types
implement `ISectionBoundOptions` and register with `AddValidatedOptions`; every option that can be wrong is validated
at start; public types that only sibling packages need are `internal` + IVT, not public; new registration/builder
extensions go in `SharedKernel.Persistence`, EF Core model/migration/query extensions in `SharedKernel.Persistence.EfCore`.

---

## DI Registration

The canonical composition (compiled and run by `13.ServiceDefaults/SharedKernel.ServiceDefaults.Persistence/…Tests/Readme/PersistenceReadmeSampleTests.cs`):

```csharp
builder.Services.AddOidcAuthentication(builder.Configuration);      // 12.Security
builder.Services.AddSharedKernelRequestContext();                   // 13.ServiceDefaults.Security → IRequestContext
builder.Services.AddSharedKernelCryptography(builder.Configuration);// IHmacSigner (audit), key providers (encryption)
builder.AddSharedKernelKeyVaultKeyProvider();                       // the root IEncryptionKeyProvider: field encryption and
                                                                    // entity versions (ETags) derive their own subkeys from it

builder.AddSharedKernelPostgres<OrderDbContext>("orders", p => p     // ConnectionStrings:orders + SharedKernel:Persistence:orders
    .UseMultiTenancy(rowLevelSecurity: true)
    .UseAuditTrail()                                                // SharedKernel:Persistence:Auditing
    .UseFieldEncryption(k => k.UseTenantDataKeys())                 // SharedKernel:Persistence:Encryption
    .MigrateOnStartup());

builder.Services.AddSharedKernelApplication(typeof(Program).Assembly, app => app  // 05: no adapters, persistence implements the contracts
    .WithTransactions().WithAuditing());

builder.Services.AddSharedKernelDapper(builder.Configuration, d => d.AddStronglyTypedId<OrderId, Guid>());  // optional

builder.Services.AddHealthChecks()                                  // 13.ServiceDefaults.Persistence
    .AddDatabaseReadinessCheck<OrderDbContext>()
    .AddPersistenceStartupReadinessCheck()
    .AddFieldEncryptionReadinessCheck()
    .AddAuditSealingReadinessCheck();
```

A Dapper-only service: `services.AddSharedKernelNpgsql(configuration, "orders"); services.AddSharedKernelDapper(...)`.
A second context: another `AddSharedKernelPostgres<ReportDbContext>("reporting")`; inject `IUnitOfWork<ReportDbContext>`
or `[FromKeyedServices(typeof(ReportDbContext))] IUnitOfWork` to start on it — any unit of work commits every joinable
context. A singleton: `ICallerDbContextFactory<T>.CreateDbContextAsync(new SystemRequestContext([], "job"), ct)`.
Hand-built (tools/tests): `options.UsePostgres(dataSource); new T(options.Options, PersistenceContextDependencies.Create())`.

---

## AOT Compatibility

Not a design constraint for this domain (EF Core, Npgsql and Dapper are reflection-based). Still: no
`MakeGenericMethod`/`Activator` in hot paths; expression trees built once and cached (keyset accessors, tenant
filter, bulk setters); the canonical encodings (AUDITv3, AAD, primary-key canonicalization, advisory-lock hashing)
are pure `Span<byte>` code; model-build-time scans are the accepted startup-only reflection.

---

## Test Rules

- Tests are nested in each package (`*.Tests`). **Unit lane** (`Platform.SharedKernel.Unit.slnf`, no Docker):
  `Persistence.Abstractions.Tests`, `Persistence.EfCore.Tests` (SQLite via the internal `UseProviderForTesting` seam —
  `TestPersistenceRegistration` keeps the old call shape over the real registration). **Integration lane**
  (`Platform.SharedKernel.Integration.slnf`, Testcontainers): `EfCore.Integration.Tests`, `Npgsql.Tests`,
  `Dapper.Tests`, `EfCore.Auditing.Tests`, `EfCore.Encryption.Tests`, `16.Testing/SharedKernel.Persistence.Testing.Tests`.
- **RLS and tenant-isolation claims are proven through an unprivileged role** — a superuser bypasses RLS even under
  `FORCE`. Attack scenarios build the detached stub / raw SQL a buggy or hostile caller would send; happy paths prove
  nothing about isolation.
- Concurrency/commit-order/retry claims are proven empirically (real concurrent writers, injected transient
  `PostgresException`s or `DbTransactionInterceptor` failures, a transaction that commits late), never by a sequential
  stand-in.
- Every finding fix carries a regression test (P-558 review IDs A*, C*, S*, F* appear in test names).
- Entity versions (P-562 X4, test names prefixed `X4_`): `EfCore.Tests/Concurrency/EntityVersionCodecTests` proves the
  construction in isolation — including a known-answer test that re-derives the token from the documented layout and
  locks the version-1 wire format — and `EfCore.Integration.Tests/Postgres/EntityVersionPostgresTests` proves it end to
  end (two rows sharing one `xmin`, tampering, rotation across a restart, no key provider). A wire-format change is a new
  format byte, never a silent edit of the known answer. The key warm-up is proven in the same two classes: after it the
  first version — and `ConcurrencyVersion.Get` through the real registration — calls no provider; a failure or a timeout
  is logged, not thrown, and the fallback still issues versions; a load that outlives the timeout still publishes the
  key; an in-memory provider is never called; and end to end with a KMS-style provider over PostgreSQL.
- README samples are compiled by tests: `PersistenceReadmeSampleTests` (domain README), `Encryption.Tests/Unit/ReadmeSampleTests`,
  `Auditing.Tests/Registration/ReadmeSampleTests`; `AuditFormatVectorTests` parses the packed `AUDIT-FORMAT.md`.
- `SharedKernel.Persistence.ConsumerVerify` runs against the **packed** packages (not in CI yet — see the handoff):
  `dotnet pack Platform.SharedKernel.slnx -c Release -o ./nupkgs -p:MinVerVersionOverride=1.0.0-local.N`, then
  `dotnet test 06.Persistence/SharedKernel.Persistence.ConsumerVerify -p:SharedKernelPackageVersion=1.0.0-local.N`.
- Build output on the maintainer machine is Turkish: judge by exit code and `error CS`/`error MSB`/`error RS` greps.
  `RS0026`/`RS0027` only surface on a real recompile (`--no-incremental` or a pack).
- Container suites that fail under whole-solution load are re-run in isolation before being treated as real.

---

## Known Limitations (deliberate, open)

- **No two-phase commit**: a context on another database (or the cross-tenant role) never joins the scope's
  transaction; holding changes at commit is refused.
- **Nested audited command**: its `Succeeded` entry rolls back with a failing outer command and no separate `Failed`
  entry is written for it (the outer command records the failure).
- **`IRequestContext.ImpersonatorId`** is not populated by `SecurityRequestContext` (`12.Security`'s `IUserContext`
  exposes no actor/impersonation claim).
- **Seeding under RLS** requires `RowLevelSecurity:CrossTenantConnectionString`.
- **`EnableTenantRowLevelSecurityForModel` in a later migration** re-creates policies of existing tables — use the
  per-table helper for tables added later.
- **Encryption**: no specification-criterion form of `WhereEncryptedEquals` (a spec has no context/keys/tenant — use the
  `IQueryable` overload or resolve the id first); no tenant data-key rotation (master-key rewrap is the KMS's job);
  `byte[]`, composite/shadow keys unsupported; writes that bypass EF Core (Dapper, raw SQL) are not tombstone-checked;
  another process may decrypt a shredded tenant's tenant-key values from its cache for up to `TenantKeyCacheDuration`;
  the rotation CAS race has no dedicated test.
- **Audit**: a link forged for one specific record makes it look sealed until verification reports it — prevented
  only by the separate sealer role (the self-check enforces the revoke when configured); partitioned retention, signed
  offline export bundles and `IAuditContext` are not implemented.
- **Public surface**: EfCore ≈ 140 `PublicAPI` lines (target was ~70) — the documented subclassable repositories account
  for most of it; accepted.
- No real PgBouncer container test (simulated by one un-reset physical connection); a `FakeUnitOfWork` rollback restores
  which aggregates a `FakeRepository` holds, not in-place changes to an aggregate object.
- `18.Idempotency.EfCore` runs its context with retry off on purpose (single atomic statements; fail-open must be fast).
- **Entity versions (P-562 X4)**: a key rotation followed by a restart makes the ETags issued before it stale (412 once,
  the client re-reads) — a process opens only keys it made current itself, by design (no client value selects a key);
  there is no configured list of previous version keys. Versions are issued only for tracked aggregates through
  `ConcurrencyVersion` (no public codec for projections or Dapper read models yet). The single-block AES call lives in
  EfCore, a direct BCL `Aes` use outside `SharedKernel.Cryptography` like the encryption package's `AesGcm` —
  `CryptoIsolationRules.NoRawSymmetricCipherOutsideCryptography` is not run against 06 assemblies; if it ever is, both
  move behind a `SharedKernel.Cryptography` primitive. With an asynchronous-only provider, the fallback load (no host,
  or a failed or unfinished warm-up) blocks the calling thread on the provider under a lock, and only a successful load
  is kept: during a key service outage every request that needs a version tries again in turn.

---

## Changelog

> One line per session. Pre-P-558 history: `CLAUDE.archive.md` and git history. P-558 narrative: `docs/p558/`.

- [2026-09-21] P-558 persistence gold-standard pass 2 — brain rewritten: PostgreSQL-only (`.PostgreSQL` merged into `.EfCore`), shared 05 contracts, one entry point, one transaction per scope, transaction-local RLS, encryption v3, audit ledger v3 with async sealer, `SharedKernel.Persistence.Testing` (agent)
- [2026-09-22] P-558 verification via `samples/BillingApi`: design-time `ConfigurePersistence`; `SharedKernel:Persistence:Tenant` annotation so `EnableTenantRowLevelSecurityForModel(TargetModel)` works on real migrations (throws on zero tables); `ConcurrencyVersion.Get` refuses untracked entities; GSS encryption off unless configured; Testing fakes roll back repository writes. Record: `P-558-SESSION-HANDOFF.md` §7
- [2026-09-24] P-562 X4 (owner-approved, from review finding S13): opaque ETags. `EntityVersion` holds only a sealed token (28-char Base64Url, never a number; JSON converter); `FromRowVersion`/`ToRowVersion` removed; EfCore seals `xmin` ‖ aggregate binding as one AES-256 block under an HKDF subkey of the service's key provider (new `SharedKernel.Cryptography` reference), opens it in `SetExpected`/`UpdateAsync`/`DeleteAsync` before attaching, treats foreign/altered/unknown-key tokens as stale (409/412), seals the conflict's current version; `PersistenceContextDependencies.Create(entityVersionKeys:)`; EventIds 6022–6024. **Breaking: every ETag value changes; services must register a key provider** (agent)
- [2026-09-24] P-562 integration stream I4: entity-version key warm-up. With an asynchronous-only key provider (a KMS), the internal hosted service `EntityVersionKeyWarmUp` (registered once by `AddSharedKernelPostgres`) loads the version key before the host takes traffic — provider call on the thread pool, at most 10 s, then it keeps loading in the background — so request threads no longer block on the provider; failure/timeout logged (6025/6026), never fatal; the first-use blocking load stays only as the documented fallback. Readiness does not wait for the key (decision recorded under *Opaque versions*). No public API change (agent)
