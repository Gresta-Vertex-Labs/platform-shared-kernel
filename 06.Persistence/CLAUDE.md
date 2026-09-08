# 06.Persistence — Data Access Layer

## What This Domain Is

The persistence primitives layer. Every repository, unit of work, and data-access abstraction in a downstream microservice derives from the interfaces and base types defined here. This domain may reference `01.Core`, `03.Domain`, and `05.Application` — it must never be referenced by those layers in return.

Philosophy: **Abstraction-first. Provider-swappable. Specification-driven. Interceptor-composed.**

> **Outbox scope:** The outbox pattern is owned entirely by `07.Messaging` via MassTransit's `UseEntityFrameworkOutbox`. No outbox types (`OutboxMessage`, `IOutboxWriter`, `OutboxInterceptor`) exist in this domain. Introducing any such type here is a hard violation — it creates competing infrastructure with no clear owner.

> **Shipped this pass (2026-09-02, Core phase — Tests/Docs/Published still pending):** EF Core value-conversion support for `03.Domain`'s `Money` value object — `CurrencyValueConverter`, `MoneyValueConverter`, `MoneyEntityTypeBuilderExtensions.OwnsMoney<TEntity>` (P-440/WO-066). **CORRECTION against the original design:** D-106's PREFERRED owned-type/reflection-located-constructor materialization path was evaluated against the real EF Core 10 assembly and found unreachable via any public API (`ITypeBase.ConstructorBinding` is read-only on every public metadata surface — `IMutableEntityType`, `IConventionEntityType`; a setter exists only on the internal `Metadata.Internal.TypeBase`). The packed-string fallback D-106 pre-authorized shipped instead — `Money`/`Currency` map to a SINGLE `"{amount}:{currencyCode}"` string column, not two independently-queryable columns. `.OwnsMoney(...)` REQUIRES `ModelConfigurationBuilderExtensions.ConfigureMoney()` to have been called from `ConfigureConventions()` first (a new, load-bearing requirement — see the Interface Contracts section) — omitting it crashes model building, since EF Core's automatic navigation discovery walks `Money`/`Currency` as candidate entity types before any per-property `.HasConversion()` call ever runs. Also shipped: the full append-only, hash-chained audit-trail capability — `IAuditTrailWriter`/`IAuditQueryService`/`AuditRecord`/`AuditEntry`/`IAuditActorContext`/`AuditResourceHistorySpecification`/`AuditActorActionsSpecification`/`AuditChainVerificationResult` in `.Abstractions`, plus `AuditRecordEntityConfiguration`/`EfAuditTrailWriter`/`AuditRecordHasher`/`AuditRecordImmutabilityInterceptor`/`AuditRecordImmutableException`/`EfAuditQueryService`/`EfCoreAuditActorContext`/`AuditTrailFeatureMarker`/`EfCorePersistenceBuilder.WithAuditTrail()` in `.EfCore` (P-456+P-457/WO-071) — distinct from the pre-existing `AuditInterceptor`, which only stamps mutable `CreatedBy`/`UpdatedAt` columns and preserves no history.
>
> **Shipped 2026-09-03 (Scaffold/Core, P-448/WO-068, BREAKING):** the async-contract migration cascading from `01.Core`'s P-446 (`IEncryptionKeyProvider.GetCurrentKey()`/`GetKey(string)` removed outright, replaced by `GetCurrentKeyAsync`/`GetKeyAsync`). `EncryptedValueConverter`'s constructor DROPPED its `IEncryptionKeyProvider keyProvider` parameter entirely (D-108) — it now calls `ISymmetricEncryptionService.Decrypt` directly and maps `result.Error?.Code == CryptographyErrorCodes.UnknownKeyId` → `EncryptionKeyNotFoundException`, any other failure → the existing generic tamper `CryptographicException`; observable exception behavior is unchanged, only the code path producing it. `EncryptionModelConvention`'s constructor correspondingly dropped its `IEncryptionKeyProvider?` parameter (D-109) — construction now branches on `_symmetricEncryptionService is not null` alone. `EncryptionOptionsKeyProvider`/`NullEncryptionKeyProvider` migrated to the async shape (D-110) — both return an ALREADY-COMPLETED `ValueTask<T>` (`IsCompletedSuccessfully == true`), since neither performs genuine I/O; `NullEncryptionKeyProvider.GetCurrentKeyAsync` still throws synchronously, before constructing any `ValueTask`. `01.Core`'s `ISymmetricEncryptionService` also gained async members (`EncryptAsync`/`DecryptAsync`/`EncryptToStringAsync`/`DecryptToStringAsync`) as part of the same P-446 shipment — this package's `NullSymmetricEncryptionService` stub had to implement them too (an incidental, not-separately-work-ordered fix required for the domain to compile at all). `SharedKernelDbContext`'s constructor KEEPS its `IEncryptionKeyProvider? encryptionKeyProvider` parameter for source-compatibility (downstream contexts still forward it to `base(...)` unchanged) but no longer stores or forwards it anywhere — `01.Core`'s `AesGcmEncryptionService` resolves `IEncryptionKeyProvider` from DI on its own. See "Encryption" below for the full picture and `state-map.md` D-108..D-112.
>
> **Fully shipped, no remaining blockers (2026-09-03, Tests phase, P-448/WO-068 closed end to end):** `16.Testing`/P-450 shipped, migrating `FakeEncryptionKeyProvider`/`FakeSymmetricEncryptionService` onto the async contracts and clearing the compile blocker that had forced the prior session onto a standalone throwaway harness. T-125..T-128 now run and pass inside the REAL `SharedKernel.Persistence.EfCore.Tests` project: 415/415 green, `dotnet pack` clean. Re-verifying in the real project (rather than trusting the harness's 33/33) surfaced two genuine test-quality gaps, both fixed test-only (no production code changed): `EncryptedValueConverterTests.Encrypt_ThenTamper_ThrowsCryptographicException` asserted only `Throw<Exception>()` — tightened to the concrete `CryptographicException` type T-126 requires; `NullEncryptionKeyProvider` (the no-op provider used when `.WithEncryption()` is never called) had zero test coverage anywhere — added `NullEncryptionKeyProviderTests.cs` (5 tests: synchronous pre-`ValueTask` throw, awaited-throw path, `GetKeyAsync` never throws, `GetKeyAsync` completes synchronously, singleton identity). All six `SK.06.*` phase keys are now `●` — this domain's entire tracked scope (WO-008 through WO-071) is complete end to end.
>
> **Shipped 2026-09-08 (Core/Tests/Docs/Published, P-498/WO-081, SEVERE — the single defect motivating this coordinated cross-domain wave):** `EncryptedValueConverter` closed a thread-pool-starvation hazard that becomes reachable the moment a KMS-backed `IEncryptionKeyProvider` (e.g. `01.Core`'s `AzureKeyVaultEncryptionKeyProvider`) is wired anywhere near this package's encryption pipeline — EF Core 10's `ValueConverter` has no async path, so every encrypted-column read/write calls `ISymmetricEncryptionService`'s SYNC `Encrypt`/`Decrypt`, which (per `01.Core`'s `SK.01.P491`/`SK.01.P492`) either blocks a real thread per call or throws `NotSupportedException` outright against any non-"genuinely synchronous" provider — cache-warm or not, since that gate is a static provider-identity check, never a per-call one. **The phase input's own literal premise (a `SavingChangesAsync`-only pre-warm hook) was evaluated and found unsound — see D-126's refutation record, kept as a standing design note** — it cannot cover the READ path at all (queries never call `SaveChangesAsync`) and, even for writes, a `CachedEncryptionKeyProvider`-wrapped KMS provider can never satisfy `IsGenuinelySynchronous` regardless of cache warmth. **What shipped:** `EncryptionOptionsKeyProvider`/`NullEncryptionKeyProvider` gained the `ISynchronousEncryptionKeyProvider` marker directly (D-127 — the single most load-bearing change; without it every EXISTING config-backed user's sync calls would break); a new, HONESTLY-marked `PreWarmedEncryptionKeyProvider` (D-129) serves exclusively from an in-memory cache, warmed by a NEW dual-hook `EncryptionKeyPreWarmingInterceptor` (`SavingChangesAsync` for writes, `IDbCommandInterceptor.ReaderExecutingAsync` — EF Core's genuine async pre-materialization extension point — for reads, D-130) and by a new boot-time `EncryptionKeyPreWarmingHostedService` (D-133); and `.WithEncryption()` gained a structural, keyed-DI isolation fix (D-131) — it now constructs its own persistence-scoped `ISymmetricEncryptionService`/`IEncryptionKeyProvider` directly and registers both under package-internal keyed-DI slots (`PersistenceEncryptionKeys`), never the ambient unkeyed slot, making the defect's actual root cause (silent DI-registration-order collision with an unrelated provider such as `13.ServiceDefaults`'s `AddSharedKernelKeyVaultKeyProvider`) structurally unreachable. A new opt-in `.WithExternalEncryptionKeyProvider<TProvider>()` directs this package at a KMS-backed provider (must be called after `.WithEncryption(...)`, enforced at `Build()` time). Also resolved `01.Core`'s own D-67 "hardest of the six" open question: `EncryptedValueConverter`'s new required `byte[] associatedData` parameter is derived from the property's own stable table+column storage identity, computed once per property at model-finalization time by `EncryptionModelConvention` — no row-PK access needed; an optional `associatedDataOverride` string on the extended `.Encrypt(...)` extension gives rename-safety. A genuinely nice, unplanned simplification surfaced during implementation: because this package now constructs `AesGcmEncryptionService` directly rather than DI-resolving a registered `ISymmetricEncryptionService`, the config-backed default path no longer requires the consumer to call `01.Core`'s `AddSharedKernelCryptography()` at all. `EncryptionRotationService` remains config-backed-only (D-134, explicit non-goal, not an oversight). Full 448/448 test suite green, `dotnet pack` clean. See "Encryption" below and `state-map.md` D-126..D-134.

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Persistence.Abstractions` | `IRepository<T,TId>`, `IReadRepository<T,TId>` (gains `GetByIdsChunkedAsync`, WO-051/P-323), `IUnitOfWork`, `ITransactionalUnitOfWork` (gains `ExecuteInTransactionAsync`, WO-051/P-320), `IDbConnectionFactory`, `ISpecificationEvaluator<T>` (gains `GetKeysetQuery<TKey>`, WO-051/P-317), `ByIdSpecification<T,TId>`, `KeysetPage<TAggregate,TKey>` (WO-051/P-317), `IRestorableRepository<TAggregate,TId>` (WO-053/P-337 — single-entity soft-delete restore), `Auditing/` folder (WO-071/P-456, shipped 2026-09-02): `IAuditTrailWriter`/`IAuditQueryService`/`AuditRecord`/`AuditEntry`/`IAuditActorContext`/`AuditResourceHistorySpecification`/`AuditActorActionsSpecification`/`AuditChainVerificationResult` — append-only, hash-chained audit trail, no update/delete member on the writer contract at all — pure interface library; no outbox types | `SharedKernel.Primitives`, `SharedKernel.Domain`, `SharedKernel.Contracts` (added P-080 — required for `PagedList<T>` in `IReadRepository.ListPagedAsync`) |
| `SharedKernel.Persistence.EfCore` | EF Core implementation: `EfRepository<T,TId>` (gains `RestoreAsync`, WO-053/P-337), `EfReadRepository<T,TId>` (gains `ListKeysetAsync<TKey>`, WO-051/P-317; gains `GetByIdsChunkedAsync`, WO-051/P-323; gains an optional `IReadReplicaContextAccessor<TContext>?` constructor parameter, WO-053/P-338), `EfUnitOfWork` (implements both `SharedKernel.Persistence.Abstractions.IUnitOfWork` and, opt-in, `SharedKernel.Application.Behaviors.IUnitOfWork`), `SharedKernelDbContext` (gains `CurrentUserContext`/`RefreshUserContext`, WO-051/P-322; gains an optional `AuditTrailFeatureMarker?` constructor parameter, WO-071/P-457), `SpecificationEvaluator<T>` (auto-`TagWith`, `AsSplitQuery`, keyset seek predicate — WO-051/P-317-319), interceptors (Audit, SoftDelete, Concurrency — always registered, no OutboxInterceptor; `ConcurrencyInterceptor` gains a Warning `[LoggerMessage]` log, WO-053/P-333; a FOURTH, opt-in-only `AuditRecordImmutabilityInterceptor` shipped WO-071/P-457), `TenantedDbContext` (gains `RefreshRequestContext` + pooling-safe/model-cache-safe filter rebuild, WO-051/P-322), `Conversions/` (WO-066/P-440, shipped 2026-09-02): `CurrencyValueConverter`/`MoneyValueConverter`/`MoneyEntityTypeBuilderExtensions.OwnsMoney<TEntity>`/`ModelConfigurationBuilderExtensions.ConfigureMoney()`, `Auditing/` (WO-071/P-457, shipped 2026-09-02): `AuditRecordEntityConfiguration`/`EfAuditTrailWriter`/`AuditRecordHasher`(internal)/`AuditRecordImmutabilityInterceptor`/`AuditRecordImmutableException`/`EfAuditQueryService`/`EfCoreAuditActorContext`/`AuditTrailFeatureMarker`, `EfCorePersistenceBuilder` (gains `.WithTransientFaultRetry()`, WO-051/P-320; gains `.WithDbContextPooling()`, WO-051/P-322; gains `.WithEncryption(IConfiguration,...)`/`.WithServiceName(IConfiguration)` config-binding overloads, WO-053/P-334; gains `.WithCommandTimeout(int)`, WO-053/P-337; gains `.WithReadReplica(Action<DbContextOptionsBuilder>)`, WO-053/P-338; gains `.WithAuditTrail()`, WO-071/P-457), `IReadReplicaContextAccessor<TContext>` (internal, WO-053/P-338), `PersistenceActivitySource`/`PersistenceTagKeys` (WO-051/P-319), `EncryptionKeyByteCache` (internal, WO-051/P-323), `PersistenceLogEvents`-style `[LoggerMessage]` partial methods across `ConcurrencyInterceptor`/`MigrationAndSeedHostedService`/the transient-retry diagnostic bridge/`EncryptionRotationService` (EventIds `6000-6099`, WO-053/P-333), `EncryptionOptions.SectionName`/`PersistenceServiceOptions.SectionName` (WO-053/P-334); **shipped 2026-09-03 (P-448/WO-068, breaking):** an async-contract migration REMOVING `EncryptedValueConverter`'s/`EncryptionModelConvention`'s direct `IEncryptionKeyProvider` dependency entirely and migrating `EncryptionOptionsKeyProvider`/`NullEncryptionKeyProvider` to `GetCurrentKeyAsync`/`GetKeyAsync`; **shipped 2026-09-08 (P-498/WO-081, SEVERE, breaking):** `EncryptedValueConverter` gains a required `byte[] associatedData` parameter (AAD, D-128); `PropertyBuilderEncryptExtensions.Encrypt(...)` gains an optional `associatedDataOverride` parameter; `EncryptionOptionsKeyProvider`/`NullEncryptionKeyProvider` implement `ISynchronousEncryptionKeyProvider` (D-127); new `PreWarmedEncryptionKeyProvider`/`EncryptionKeyPreWarmingInterceptor`/`EncryptionKeyPreWarmingHostedService`/`PersistenceEncryptionKeys` (internal, D-129/D-130/D-133/D-131); `EfCorePersistenceBuilder` gains `.WithExternalEncryptionKeyProvider<TProvider>()` (D-131), and `.WithEncryption()` now constructs its own keyed-DI-isolated persistence-scoped `ISymmetricEncryptionService` — no longer touches the ambient unkeyed `IEncryptionKeyProvider`/`ISymmetricEncryptionService` slot, and no longer requires the consumer to call `01.Core`'s `AddSharedKernelCryptography()` for the config-backed default path | `SharedKernel.Persistence.Abstractions`, `SharedKernel.Domain`, `SharedKernel.Cryptography` (01.Core, added P-227 — `EncryptedValueConverter` delegates AES-256-GCM to `ISymmetricEncryptionService`; also the source of `IContentHasher`, consumed by `EfAuditTrailWriter`/`EfAuditQueryService`'s hash chain, WO-071/P-457), `SharedKernel.Application.Behaviors` (05.Application, added P-228 — `EfUnitOfWork` dual-interface bridge), `Microsoft.EntityFrameworkCore` 10.0.5, `Microsoft.EntityFrameworkCore.Relational` 10.0.5, `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.5 (must match EFCore transitive — NU1605 fires if pinned lower), `Microsoft.Extensions.Logging.Abstractions` (added WO-053/P-333 — `[LoggerMessage]` source generator), `Microsoft.Extensions.Options.ConfigurationExtensions` (added WO-053/P-334 — `OptionsBuilder<T>.Bind(IConfiguration)`), `Microsoft.Extensions.DependencyInjection` — the concrete package, not merely `.Abstractions` (added WO-053/P-338 — `ActivatorUtilities.CreateInstance<T>` for the read-replica context) |
| `SharedKernel.Persistence.PostgreSQL` | PostgreSQL-specific conventions: `SnakeCaseNamingConvention`, `XminConcurrencyTokenConvention`/`XminRowVersionValueConverter` (WO-051/P-315 — the genuine, working `IHasConcurrency` mechanism), `UsePostgreSQL()` DI extension (gains opt-in `EnableRetryOnFailure` parameters, WO-051/P-320), JSONB column support (`HasJsonbColumn`, `JsonbColumnAttribute`), pgvector support (`HasVectorColumn`, `VectorColumnAttribute`; gains query-side `VectorDistanceMetric`/`VectorOrderingExpressions.ByDistance<TAggregate>(...)` nearest-neighbor ordering helper, WO-053/P-339), `NpgsqlConnectionFactory`, `AddSharedKernelPostgreSQL()` DI extension | `SharedKernel.Persistence.EfCore`, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.x, `Pgvector.EntityFrameworkCore` |
| `SharedKernel.Persistence.Dapper` | Dapper micro-ORM read-side: `StronglyTypedIdTypeHandler<TStronglyTypedId,TValue>`, `SmartEnumTypeHandler<TEnum,TValue>`, `DapperTypeHandlers` (idempotent `Register()`), `DapperReadService` base (gains multi-mapping `QueryAsync`/`QueryMultipleAsync`/protected `ConnectionFactory`, WO-051/P-321), `AddSharedKernelDapper()` DI extension | `SharedKernel.Persistence.Abstractions`, `SharedKernel.Persistence.PostgreSQL` (for `NpgsqlConnectionFactory`), `Dapper` |

All packages target `net10.0`, `ImplicitUsings` enabled, `Nullable` enabled. Test sub-folders live inside each project folder (never in a top-level `tests/`).

---

## Technology Stack

| Concern | Technology |
| --- | --- |
| Repository pattern | Pure C# 13 interfaces in `.Abstractions` — no ORM dependency |
| Unit of work | Pure C# 13 interface (`IUnitOfWork`) — provider-agnostic |
| EF Core ORM | `Microsoft.EntityFrameworkCore` 10.x |
| EF Core interceptors | `ISaveChangesInterceptor` — Audit, SoftDelete, Concurrency (three always-registered; no OutboxInterceptor); a fourth, OPT-IN-only `AuditRecordImmutabilityInterceptor` shipped WO-071/P-457, registered only via `.WithAuditTrail()` |
| Specification evaluation | Custom `SpecificationEvaluator<T>` translating `ISpecification<T>` to `IQueryable<T>` |
| PostgreSQL provider | `Npgsql.EntityFrameworkCore.PostgreSQL` 10.x |
| JSONB | Npgsql built-in JSON column support with STJ serialization |
| Vector search | `Pgvector.EntityFrameworkCore` for pgvector typed columns |
| Micro-ORM (read side) | `Dapper` |
| Connection factory | `Npgsql` (PostgreSQL) via `NpgsqlConnectionFactory` in the Dapper package |
| Multi-tenancy | `ITenantProvider` (from `SharedKernel.Security.Abstractions`) + `TenantedDbContext` + `TenantedRepository<T,TId>`; `NoOpTenantProvider` (returning `Guid.Empty`) registered by `.WithMultiTenancy()` as fallback |
| DI composition | `EfCorePersistenceBuilder` fluent builder via `AddSharedKernelEfCore<TContext>` |

---

## Interface Contracts

### `SharedKernel.Persistence.Abstractions` — public surface

> Zero ORM NuGet dependencies. References only `SharedKernel.Primitives` and `SharedKernel.Domain`.
> No outbox types exist here — outbox is entirely within `07.Messaging`.

#### Repository interfaces (`Repositories/`)

```text
IRepository<TAggregate, TId>
    where TAggregate : IAggregateRoot<TId>
    where TId : notnull
    .GetByIdAsync(TId id, CancellationToken ct)                                            → Task<TAggregate?>
    .GetBySpecAsync(ISpecification<TAggregate> spec, CancellationToken ct)                 → Task<TAggregate?>
    .AddAsync(TAggregate aggregate, CancellationToken ct)                                  → Task
    .UpdateAsync(TAggregate aggregate, CancellationToken ct)                               → Task
    .DeleteAsync(TAggregate aggregate, CancellationToken ct)                               → Task
    .ExistsAsync(TId id, CancellationToken ct)                                             → Task<bool>
    .AddRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct)               → Task
    .UpdateRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct)            → Task
    .DeleteRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct)            → Task
    NOTE: Write-side only. Does not expose IQueryable or raw SQL.
          GetBySpecAsync returns a TRACKED entity by default (spec's AsNoTracking flag honored).
          Write-side callers should use specs with AsNoTracking == false so subsequent mutations
          are detected by EF change detection without requiring an explicit .Update() call.
          ExistsAsync issues an EXISTS/ANY check — never materializes the aggregate.
          Bulk methods stage mutations without committing — same semantics as single-entity counterparts.

IReadRepository<TAggregate, TId>
    where TAggregate : IAggregateRoot<TId>
    where TId : notnull
    .GetBySpecAsync(ISpecification<TAggregate> spec, CancellationToken ct)                 → Task<TAggregate?>
    .ListAsync(ISpecification<TAggregate> spec, CancellationToken ct)                      → Task<IReadOnlyList<TAggregate>>
    .CountAsync(ISpecification<TAggregate> spec, CancellationToken ct)                     → Task<int>
    .AnyAsync(ISpecification<TAggregate> spec, CancellationToken ct)                       → Task<bool>
    .GetByIdsAsync(IEnumerable<TId> ids, CancellationToken ct)                             → Task<IReadOnlyList<TAggregate>>
    .GetByIdsChunkedAsync(IEnumerable<TId> ids, int chunkSize, CancellationToken ct)        → Task<IReadOnlyList<TAggregate>>
        (WO-051/P-323)
    .ListPagedAsync(ISpecification<TAggregate> spec, CancellationToken ct)                 → Task<PagedList<TAggregate>>
    .ListProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> spec, ct)    → Task<IReadOnlyList<TResult>>
    .GetBySpecProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> s, ct)  → Task<TResult?>
    .ListPagedProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> spec, ct) → Task<PagedList<TResult>>
    .StreamAsync(ISpecification<TAggregate> spec, CancellationToken ct)
        → IAsyncEnumerable<TAggregate>
    .StreamProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> spec, ct)
        → IAsyncEnumerable<TResult>
    .ListKeysetAsync<TKey>(KeysetSpecification<TAggregate,TKey> spec, CancellationToken ct)          → Task<KeysetPage<TAggregate,TKey>>
        where TKey : IComparable<TKey>                                                                (WO-051/P-317)
    NOTE: Read-side only. Specifications control tracking, filtering, ordering, paging, and includes.
          Callers use ReadOnlySpecification<T> or PagedSpecification<T> for read-heavy paths.
          GetByIdsAsync translates the IEnumerable<TId> membership check to a SINGLE Npgsql array
          parameter (WHERE "Id" = ANY(@ids)) — NOT a SQL-Server-style per-value IN (v1, v2, ...)
          expansion. There is no ~1000-parameter-style ceiling on PostgreSQL (CORRECTED, WO-051/P-323
          — the original "performance degrades above 1000 IDs" guidance was written assuming the
          SQL-Server IN(...) shape). The real practical constraint is the serialized array parameter's
          payload size and the materialized result set's memory footprint, not parameter count.
          Result order is not guaranteed; missing IDs produce no entry.
          GetByIdsChunkedAsync (WO-051/P-323): an OPT-IN sibling that issues ceil(N/chunkSize)
          sequential round trips instead of one, for callers who deliberately want bounded per-query
          memory/payload despite = ANY(@array) not strictly requiring it. Does NOT change
          GetByIdsAsync's own single-query behavior in any way — this is documented, opt-in guidance,
          not a silent behavior change. A re-derived guardrail: consider GetByIdsChunkedAsync above
          roughly 50,000 IDs to bound peak memory; below that, GetByIdsAsync's single = ANY(@array)
          query remains efficient.
          ListPagedAsync issues two DB round-trips (count + data) under the same DbContext scope.
          ListPagedProjectedAsync<TResult> (P-101): same two-round-trip pattern; count query uses GetQuery
          (no projection), data query uses GetProjectedQuery (with selector); returns PagedList<TResult>.
          Use this instead of ListPagedAsync when the caller needs DTOs, not aggregate roots.
          PagedList<T> is defined in SharedKernel.Contracts (04.Contracts).
          BREAKING CHANGE (P-080): GetByIdAsync has been removed from IReadRepository.
          Migration: replace readRepo.GetByIdAsync(id, ct) with
                     readRepo.GetBySpecAsync(new ByIdSpecification<TAggregate, TId>(id), ct).
          Note: IRepository (write side) retains its own GetByIdAsync — only the read side is affected.
          StreamAsync / StreamProjectedAsync<TResult> (P-149): BCL IAsyncEnumerable<T> — no new
          .Abstractions dependency. Intended for large result sets (exports, batch processing) where
          materializing an IReadOnlyList<T> would be memory-prohibitive. Spec's Skip/Take are honored
          as a row-window applied before streaming begins (not special-cased). AsNoTracking is forced
          unconditionally by the EfCore implementation regardless of the spec's AsNoTracking flag —
          see EfReadRepository for rationale.
          ListKeysetAsync<TKey> (WO-051/P-317): cursor/seek-pagination sibling to ListPagedAsync — see
          "Keyset (cursor) pagination" below for the full explanation. HARD CONSTRAINT: passing a
          KeysetSpecification<T,TKey> to ListAsync/GetBySpecAsync/CountAsync/AnyAsync instead of
          ListKeysetAsync<TKey> compiles and runs but silently ignores AfterKey/AfterId and always
          returns the first page — ListKeysetAsync<TKey> is the ONLY entry point that honors the cursor.

IRestorableRepository<TAggregate, TId>  (WO-053/P-337)
    where TAggregate : IAggregateRoot<TId>
    where TId : notnull
    .RestoreAsync(TAggregate aggregate, CancellationToken ct)                             → Task
    NOTE: Write-side only, reverses a prior soft delete. Same class constraint as IRepository<TAggregate,TId>
          itself — deliberately NOT narrowed to ISoftDeletable at the interface level (a generic
          implementing class cannot conditionally satisfy a narrower constraint for only some closed-generic
          instantiations). The EfCore implementation enforces the real ISoftDeletable-or-not distinction via
          a runtime guard — mirroring the BulkSpecificationGuard/UnsupportedSpecificationException precedent
          (compile-time-loose interface + runtime-enforced narrower constraint) rather than a parallel
          TenantedRepository-style base-class hierarchy. Only STAGES the change — does not call
          SaveChangesAsync — a subsequent IUnitOfWork.SaveChangesAsync() persists it, going through the same
          interceptor/audit/domain-event pipeline a normal update would. See "Soft-Delete Restore" below.
```

#### Unit of Work (`UnitOfWork/`)

```text
IUnitOfWork
    .SaveChangesAsync(CancellationToken ct)                    → Task<int>
    NOTE: Wraps the provider's commit boundary. EF Core implementation fires all three interceptors
          (Audit, SoftDelete, Concurrency) inside the same SaveChanges call.
          This is the ONLY permitted save boundary — calling DbContext.SaveChangesAsync directly
          outside EfUnitOfWork is a hard violation.

IPersistenceTransaction  (IAsyncDisposable)
    .CommitAsync(CancellationToken ct)                         → Task
    .RollbackAsync(CancellationToken ct)                       → Task
    NOTE: Provider-agnostic transaction handle. EF Core implementation wraps IDbContextTransaction.
          Callers must await DisposeAsync() (or use await using) — the handle disposes the underlying
          transaction resource. Zero ORM dependencies — lives in Abstractions (BCL types only).

ITransactionalUnitOfWork  (extends IUnitOfWork)
    .BeginTransactionAsync(CancellationToken ct)               → Task<IPersistenceTransaction>
    .ExecuteInTransactionAsync(Func<CancellationToken,Task> operation, CancellationToken ct) → Task
    .ExecuteInTransactionAsync<TResult>(Func<CancellationToken,Task<TResult>> operation, CancellationToken ct)
        → Task<TResult>                                                                        (WO-051/P-320)
    NOTE: Opens an explicit database transaction. Multiple repository operations within the
          returned IPersistenceTransaction scope are committed atomically via CommitAsync or
          rolled back via RollbackAsync. Domain event dispatch fires after CommitAsync, consistent
          with EfUnitOfWork semantics. Register via EfCorePersistenceBuilder.WithTransactionalUnitOfWork().
          Application layer injects ITransactionalUnitOfWork — never IDbContextTransaction directly.
          ExecuteInTransactionAsync (WO-051/P-320): the RETRY-SAFE alternative to BeginTransactionAsync's
          handle-based flow — see "Transient-fault retry" below. BCL-only signature (Func<>/Task/
          CancellationToken) — zero ORM types, lives in Abstractions. The operation delegate may run
          MORE THAN ONCE when a retrying execution strategy is configured — it must be safe to re-run.
```

#### Connection factory (`Connections/`)

```text
IDbConnectionFactory
    .CreateConnectionAsync(CancellationToken ct)               → Task<IDbConnection>
    NOTE: Returns an open connection. Caller is responsible for disposal.
          Connection pooling is provider-managed. This factory is not restricted to Dapper —
          any component needing a raw IDbConnection may inject it.
```

#### Diagnostics (`Diagnostics/`) — P-150

```text
DatabaseReadinessResult  (sealed record)
    (bool IsHealthy, TimeSpan Latency, string Provider, string? ErrorMessage)
    NOTE: BCL-only — no ORM types. IsHealthy is true only if the probe completed without error.
          ErrorMessage is null when IsHealthy is true.

IDbConnectionFactory.CheckReadinessAsync(CancellationToken ct)  (extension method) → Task<DatabaseReadinessResult>
    NOTE: Opens a connection via CreateConnectionAsync, runs "SELECT 1" via a GENUINE async call,
          times the round-trip with System.Diagnostics.Stopwatch. Provider is taken from the
          IDbConnection.GetType().Namespace-derived label (e.g., "Npgsql"). Never throws — any
          exception is caught and reported as IsHealthy = false with ErrorMessage populated.
          Only System.Data and System.Diagnostics types are used — zero new dependencies.
          GENUINE ASYNC (CORRECTED, WO-051/P-325 — the original implementation called the SYNCHRONOUS
          IDbCommand.ExecuteScalar() inside this async method, blocking a thread-pool thread for the
          DB round trip on every K8s readiness-probe firing, across every running pod): the command is
          safe-cast to System.Data.Common.DbCommand (every shipped IDbConnectionFactory implementation
          — NpgsqlConnectionFactory — returns a genuine DbConnection/DbCommand at runtime) and its true
          ExecuteScalarAsync(ct) is awaited; a synchronous ExecuteScalar() fallback is retained for
          correctness against any hypothetical non-DbCommand IDbCommand implementer.
          IDbConnectionFactory's public interface signature is completely unchanged by this fix — it
          is a purely internal, non-breaking implementation correction.
          This domain does NOT implement IHealthCheck — see Hard Violations. 13.ServiceDefaults
          wraps this extension (or SharedKernelDbContext.CheckReadinessAsync) inside an
          IHealthCheck adapter for ASP.NET Core health check middleware.
```

#### Specification evaluator contract (`Specifications/`)

```text
ISpecificationEvaluator<T>
    .GetQuery(IQueryable<T> inputQuery, ISpecification<T> spec) → IQueryable<T>
    .GetProjectedQuery<TResult>(IQueryable<T> inputQuery, IProjectionSpecification<T,TResult> spec) → IQueryable<TResult>
    .GetKeysetQuery<TKey>(IQueryable<T> inputQuery, KeysetSpecification<T,TKey> spec) → IQueryable<T>  where TKey : IComparable<TKey>
        (WO-051/P-317)
    NOTE: Applies criteria, includes (expression + string), ordering, paging, distinct, and AsNoTracking.
          GetProjectedQuery applies the full aggregate pipeline (steps 0–7 + 2b) then step 8 (Select).
          All three methods live in Abstractions so alternative evaluators (e.g., for Cosmos) implement the same
          interface without coupling to EF Core.
          BREAKING from original design: GetProjectedQuery was previously only on the concrete
          SpecificationEvaluator<T>; it is now on the interface (P-097). EfReadRepository no longer
          downcasts to the concrete type — any ISpecificationEvaluator<T> implementation must implement both.
          All ISpecificationEvaluator<T> implementations must read both spec.Includes (expression-based)
          and spec.StringIncludes (string-based) to be complete — omitting StringIncludes is a silent bug.
          GetKeysetQuery<TKey> (WO-051/P-317): a METHOD-level generic parameter beyond the interface's own T
          — deliberately chosen so the evaluator never needs to detect a KeysetSpecification<T,TKey> via
          reflection against an erased TKey; the caller (EfReadRepository.ListKeysetAsync<TKey>) always
          knows TKey statically. Translates spec.AfterKey/spec.AfterId into a genuine seek predicate — see
          "Keyset (cursor) pagination" below for the full pipeline description.

IProjectionSpecification<TAggregate, TResult>  (extends ISpecification<TAggregate>)
    .Selector                                                   → Expression<Func<TAggregate, TResult>>
    NOTE: Prerequisite for projection-read methods (ListProjectedAsync, GetBySpecProjectedAsync).
          Selector is an expression tree — AOT-safe on IQueryable.
          Implementations extend a concrete Specification<TAggregate> base and supply the Selector expression.
          Zero ORM dependencies — lives in Abstractions.

ByIdSpecification<TAggregate, TId>  (sealed class, implements ISpecification<TAggregate>)
    constructor: ByIdSpecification(TId id)
    .Criteria                                                   → Expression<Func<TAggregate, bool>>
    NOTE: Sets Criteria = e => e.Id.Equals(id). Expression tree — AOT-safe.
          Canonical replacement for the removed IReadRepository.GetByIdAsync.
          Usage: readRepo.GetBySpecAsync(new ByIdSpecification<TAggregate, TId>(id), ct).

KeysetPage<TAggregate, TKey>  (sealed record)  where TKey : IComparable<TKey>   (WO-051/P-317)
    .Items                                                      → IReadOnlyList<TAggregate>
    .NextAfterKey                                                → TKey?
    .NextAfterId                                                 → object?
    .HasMore                                                     → bool
    NOTE: Returned by IReadRepository.ListKeysetAsync<TKey>. NextAfterKey/NextAfterId are null/default
          exactly when HasMore == false (no further page) — callers pass them straight back as the next
          KeysetSpecification<T,TKey>'s afterKey/afterId constructor arguments to fetch the following page.
          Zero ORM dependency — BCL types plus the already-referenced SharedKernel.Domain type
          KeysetSpecification<T,TKey>.
```

#### Auditing (`Auditing/`) — P-456/WO-071, shipped 2026-09-02

Append-only, tamper-evident audit-trail contracts. Distinct from `AuditInterceptor` (EfCore), which only stamps mutable `CreatedBy`/`UpdatedBy`/`UpdatedAt` columns that the next edit overwrites — this capability preserves full history and proves it hasn't been tampered with via a hash chain.

```text
AuditRecord  (sealed record — SHIPPED)
    .Id                  → Guid            (assigned by the writer, e.g. Guid.CreateVersion7())
    .TenantId             → Guid            (Guid.Empty sentinel — mirrors ITenantProvider's existing convention)
    .ActorId              → string          (REUSES AuditInterceptor's existing audit-string-format convention:
                                              userId.ToString("D") or a service-name fallback — no new actor-identity shape)
    .Action                → string          (caller-defined verb/code)
    .ResourceType          → string
    .ResourceId            → string          (caller stringifies — aggregate PK types vary: Guid, StronglyTypedId<T>, int)
    .OccurredOn            → DateTimeOffset  (IClock-sourced by the WRITER — never caller-supplied, prevents forged timestamps)
    .BeforeSnapshot        → string?         (OPAQUE — caller pre-serializes; this package never parses/diffs it)
    .AfterSnapshot         → string?         (OPAQUE, same rule)
    .CorrelationId         → string?
    .ApprovalId            → string?         (optional maker-checker linkage to IRequiresDualApproval/P-380)
    .RecordHash            → string          (hex SHA-256 via 01.Core's IContentHasher, computed by the writer)
    .PreviousRecordHash    → string?         (null only for the first record in its (TenantId,ResourceType) chain)
    NOTE: ALL properties init-only — no update/delete surface anywhere on the type, structurally not conventionally.
          Deliberately does NOT implement IAggregateRoot<Guid> — that interface extends IHasDomainEvents, and
          forcing an always-empty domain-events collection onto a plain infrastructure record purely to satisfy
          IReadRepository<T,TId>'s generic constraint is an unjustified stretch. EfAuditQueryService (EfCore)
          instead builds directly on the unconstrained ISpecificationEvaluator<AuditRecord>.

AuditEntry  (sealed record — SHIPPED)
    .Action, .ResourceType, .ResourceId, .BeforeSnapshot, .AfterSnapshot, .CorrelationId, .ApprovalId
    NOTE: The CALLER-SUPPLIED input to IAuditTrailWriter.RecordAsync — deliberately SMALLER than AuditRecord.
          Id/TenantId/ActorId/OccurredOn/RecordHash/PreviousRecordHash are ALL resolved/computed by the writer,
          never caller-supplied — closes off "a caller fabricates its own audit trail" structurally.

IAuditTrailWriter  (interface — SHIPPED)
    RecordAsync(AuditEntry entry, CancellationToken ct = default) → Task<AuditRecord>
    NOTE: The SOLE member. No update/delete member exists on this interface AT ALL. Actor/tenant identity
          resolved internally via IAuditActorContext; hash chaining scoped per (TenantId, ResourceType)
          partition — never globally.

IAuditActorContext  (interface — SHIPPED, local seam)
    .ActorId              → string   (same audit-string-format convention as AuditRecord.ActorId)
    .TenantId              → Guid     (Guid.Empty sentinel)
    NOTE: A package-local seam, NOT a direct SharedKernel.Security.Abstractions reference — see
          "IAuditActorContext vs. the P-078 exception" below for exactly why, given this package's sibling
          SharedKernel.Persistence.EfCore already holds an approved Security.Abstractions reference.

IAuditQueryService  (interface — SHIPPED)
    GetResourceHistoryAsync(AuditResourceHistorySpecification spec, CancellationToken ct = default)
        → Task<IReadOnlyList<AuditRecord>>
    GetActorActionsAsync(AuditActorActionsSpecification spec, CancellationToken ct = default)
        → Task<IReadOnlyList<AuditRecord>>
    VerifyChainIntegrityAsync(Guid tenantId, string resourceType, DateTimeOffset from, DateTimeOffset to,
                               CancellationToken ct = default) → Task<AuditChainVerificationResult>
    NOTE: The two named access patterns the phase requires — "history of this resource" and "actions by this
          actor" — both paginated via KeysetSpecification<T,TKey> (P-308/WO-051), never a third paging model.
          CORRECTED against the root phase's own text during implementation: EfAuditQueryService (EfCore) calls
          ISpecificationEvaluator<AuditRecord>.GetKeysetQuery(...), NOT GetQuery(...) — per that interface's own
          documented hard constraint, GetQuery silently ignores a KeysetSpecification's AfterKey/AfterId and
          always returns the first page; only GetKeysetQuery honors the cursor.

AuditResourceHistorySpecification  (sealed class, extends KeysetSpecification<AuditRecord,DateTimeOffset>)
    constructor(Guid tenantId, string resourceType, string resourceId, DateTimeOffset? afterKey,
                Guid? afterId, bool descending, int take)
    NOTE: Criteria = r => r.TenantId == tenantId && r.ResourceType == resourceType && r.ResourceId == resourceId.
          keySelector: r => r.OccurredOn; idSelector: r => r.Id (mandatory tiebreaker, inherited automatically).

AuditActorActionsSpecification  (sealed class, extends KeysetSpecification<AuditRecord,DateTimeOffset>)
    constructor(Guid tenantId, string actorId, DateTimeOffset? afterKey, Guid? afterId, bool descending, int take)
    NOTE: Criteria = r => r.TenantId == tenantId && r.ActorId == actorId. Same key/tiebreaker shape as above.

AuditChainVerificationResult  (sealed record — SHIPPED)
    .IsIntact             → bool
    .BrokenAtRecordId      → Guid?   (null when intact)
    .RecordsChecked        → int
```

**`IAuditActorContext` vs. the P-078 exception:** `06.Persistence` already holds an approved, narrowly-scoped exception referencing `SharedKernel.Security.Abstractions` directly (P-078/WO-014, see the EfCore section below) — but that grant is scoped to `SharedKernel.Persistence.EfCore` specifically, **not** to `SharedKernel.Persistence.Abstractions`, where `IAuditTrailWriter`/`IAuditQueryService`/`IAuditActorContext` live. Extending the EfCore-only exception to Abstractions would be an unrecorded widening of a narrowly-granted exception — hence `IAuditActorContext` is a fresh local seam here, mirroring `05.Application`'s `IAuthorizationContext` bridge pattern in *shape* even though the two packages differ in *why* they need one (`05.Application` has zero `Security.Abstractions` access of any kind; `06.Persistence.Abstractions` specifically has never been granted the exception its own `.EfCore` sibling already has). `SharedKernel.Persistence.EfCore`'s implementation (P-457) ships a default `IAuditActorContext` (`EfCoreAuditActorContext`, registered by `.WithAuditTrail()` only when the consumer has not already registered their own) bridging the already-approved `IUserContext`/`ITenantProvider` — see `EfCoreAuditActorContext` below — an ergonomic advantage `05.Application`'s `IAuthorizationContext` structurally cannot offer, since that package has no comparable exception at all.

Retention/archival policy is explicitly out of scope for this capability — documented as a deferred follow-up, not silently unaddressed.

---

### `SharedKernel.Persistence.EfCore` — public surface

#### DbContext base (`Context/`)

```text
SharedKernelDbContext  (abstract class, extends DbContext)
    protected SharedKernelDbContext(
        DbContextOptions options,
        AuditInterceptor auditInterceptor,
        SoftDeleteInterceptor softDeleteInterceptor,
        ConcurrencyInterceptor concurrencyInterceptor,
        IEnumerable<ISaveChangesInterceptor>? additionalInterceptors = null,
        IOptionsMonitor<EncryptionOptions>? encryptionOptions = null,
        IEncryptionVersionOverride? encryptionVersionOverride = null,
        ISymmetricEncryptionService? symmetricEncryptionService = null,
        IEncryptionKeyProvider? encryptionKeyProvider = null,
        AuditTrailFeatureMarker? auditTrailMarker = null)
        (CORRECTED, WO-051/P-324, updated WO-071/P-457 — this is the REAL 10-parameter constructor,
         verified directly against SharedKernelDbContext.cs. The previously-documented 2-parameter
         shape ("options, additionalInterceptors") was wrong — a long-standing doc-drift defect, not
         a change introduced by either phase. The 10th parameter, auditTrailMarker, was added by
         WO-071/P-457 — its non-null presence signals OnModelCreating should apply
         AuditRecordEntityConfiguration to the model; see the Auditing — EfCore implementation
         subsection below. Consuming services never call this constructor directly; all ten
         parameters are resolved by DI when a concrete subclass is constructed through
         AddSharedKernelEfCore<TContext>(...).Build() or the WO-051/P-322 pooled registration path.
         D-109/P-448: encryptionKeyProvider is retained on this signature purely for downstream
         source-compatibility — it is no longer stored on the field, no longer forwarded to
         EncryptionModelConvention, and has no runtime effect. A downstream context's existing
         constructor forwarding this parameter to base(...) needs no change.)
    .CurrentUserContext                                        → IUserContext  (get; private set)  (WO-051/P-322)
    .RefreshUserContext(IUserContext userContext)               → void                              (WO-051/P-322)
    .SaveChangesAsync(CancellationToken ct)                    → Task<int>  (override — interceptors fire here)
    NOTE: Concrete downstream DbContexts must extend this base.
          Constructor registers platform interceptors (AuditInterceptor, SoftDeleteInterceptor,
          ConcurrencyInterceptor) first — supplied as already-constructed instances, not built
          in-line — then any additional interceptors supplied via additionalInterceptors.
          Platform interceptors always fire before consumer-supplied interceptors — this ordering is
          intentional and cannot be overridden.
          No OutboxInterceptor — outbox is MassTransit's concern at 07.Messaging.
          OnModelCreating calls modelBuilder.ApplyConfigurationsFromAssembly for the calling assembly.
          When auditTrailMarker is non-null (WO-071/P-457, i.e. .WithAuditTrail() was called),
          OnModelCreating ALSO explicitly applies AuditRecordEntityConfiguration — explicitly, not via
          the assembly scan above, since that type lives in THIS assembly
          (SharedKernel.Persistence.EfCore), not the downstream context's own assembly.
          Does not declare any entity DbSets — those belong to the consuming service's DbContext subclass.
          CurrentUserContext/RefreshUserContext (WO-051/P-322): CurrentUserContext is initialized from
          the AuditInterceptor/SoftDeleteInterceptor's own resolved IUserContext at construction time
          (no new constructor parameter). AuditInterceptor/SoftDeleteInterceptor read
          ((SharedKernelDbContext)eventData.Context).CurrentUserContext LIVE inside
          SavingChanges/SavingChangesAsync instead of a field captured in their OWN constructors —
          eventData.Context is always the CURRENT executing instance, never a stale reference. Under
          the default (non-pooled) .Build()/.WithDbContextFactory() registration this is already
          correct without ever calling RefreshUserContext — RefreshUserContext exists specifically
          for the WO-051/P-322 pooled registration path (.WithDbContextPooling()), which calls it once
          per lease. See "DbContext Pooling" below for the full hazard this closes.
          OnConfiguring pooling guard (CONFIRMED-BY-TESTING, WO-051/P-322): the interceptor-wiring
          and WithEncryptionVersionOverride mutation inside OnConfiguring is now wrapped in
          `if (!optionsBuilder.Options.IsFrozen)`. EF Core freezes the DbContextOptions built by
          AddPooledDbContextFactory's optionsAction before any pooled TContext instance is
          constructed, and OnConfiguring still runs once per pooled instance — mutating already-
          frozen options there throws "'OnConfiguring' cannot be used to modify DbContextOptions
          when DbContext pooling is enabled" the first time the context's internal services are
          built. For a non-pooled context (Options.IsFrozen == false) this guard is a no-op — the
          wiring proceeds exactly as before. See EfCorePersistenceBuilder.WithDbContextPooling()'s
          own note above for where interceptor wiring moves to instead under pooling.

SharedKernelDbContext.CheckReadinessAsync(CancellationToken ct)  (extension method) → Task<DatabaseReadinessResult>
    NOTE: P-150. EfCore-side readiness probe — uses Database.CanConnectAsync wrapped in a
          System.Diagnostics.Stopwatch, with Provider taken from Database.ProviderName (e.g.
          "Npgsql.EntityFrameworkCore.PostgreSQL"). Never throws — exceptions are caught and
          reported as IsHealthy = false with ErrorMessage populated. Returns the same
          DatabaseReadinessResult record defined in SharedKernel.Persistence.Abstractions/Diagnostics/.
          Prefer this overload when a DbContext is already in scope; prefer
          IDbConnectionFactory.CheckReadinessAsync for Dapper-only read services that have no DbContext.
          This domain does NOT implement IHealthCheck — see Hard Violations.
```

#### EF Core repositories (`Repositories/`)

```text
EfRepository<TAggregate, TId>  (abstract class, implements IRepository<TAggregate, TId>)
    .GetByIdAsync(TId id, CancellationToken ct)                                            → Task<TAggregate?>
    .GetBySpecAsync(ISpecification<TAggregate> spec, CancellationToken ct)                 → Task<TAggregate?>
    .AddAsync(TAggregate aggregate, CancellationToken ct)                                  → Task
    .UpdateAsync(TAggregate aggregate, CancellationToken ct)                               → Task   ← tracking-aware: skips .Update() for tracked entities
    .DeleteAsync(TAggregate aggregate, CancellationToken ct)                               → Task
    .ExistsAsync(TId id, CancellationToken ct)                                             → Task<bool>
    .AddRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct)               → Task
    .UpdateRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct)            → Task
    .DeleteRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct)            → Task
    protected virtual .MarkAsModifiedIfDetached(TAggregate aggregate)                      → void
    NOTE: Backed by DbContext.Set<TAggregate>(). Concrete repositories extend this — do not register
          EfRepository<T,TId> directly in DI without a concrete subclass. Never exposes IQueryable.
          GetBySpecAsync calls ISpecificationEvaluator<T>.GetQuery then FirstOrDefaultAsync; returns a
          tracked entity by default (spec's AsNoTracking flag honored — write-side callers should not set it).
          UpdateAsync checks DbContext.Entry(aggregate).State: if Detached calls .Update(); otherwise
          EF change detection handles dirty tracking automatically (avoids full-column UPDATE statements).
          UpdateRangeAsync applies the same detached-state check per entity. UpdateRange itself is synchronous.
    .RestoreAsync(TAggregate aggregate, CancellationToken ct)                              → Task   (WO-053/P-337)
    NOTE (RestoreAsync): implements IRestorableRepository<TAggregate,TId> UNCONDITIONALLY on every
          EfRepository<TAggregate,TId> instantiation — mirroring how IBulkMutationRepository is already
          implemented unconditionally regardless of whether TAggregate happens to be soft-deletable. If
          `aggregate is not ISoftDeletable`, throws InvalidOperationException naming the aggregate's CLR
          type — RestoreAsync has no meaning for a non-soft-deletable aggregate. Otherwise reuses
          MarkAsModifiedIfDetached (above) so the entry ends up Modified, then writes
          `context.Entry(aggregate).CurrentValues[nameof(ISoftDeletable.IsDeleted)] = false`,
          `[nameof(ISoftDeletable.DeletedOn)] = null`, `[nameof(ISoftDeletable.DeletedBy)] = null` — the
          SAME ChangeTracker.Entry(entity).CurrentValues[propertyName]-only mutation rule that governs
          AuditInterceptor/SoftDeleteInterceptor, extended here to repository-level restore code for the
          identical reason. Restoring an aggregate already not deleted is an idempotent no-op success.
          Only STAGES the mutation — a subsequent IUnitOfWork.SaveChangesAsync() persists it; because the
          entry is a normal Modified row at that point, AuditInterceptor sets ModifiedBy/ModifiedOn
          automatically with ZERO changes needed to either platform interceptor.

IBulkMutationRepository<TAggregate, TId>  (interface, SharedKernel.Persistence.EfCore — NOT Abstractions)
    .ExecuteUpdateAsync(ISpecification<TAggregate> spec,
        Action<UpdateSettersBuilder<TAggregate>> setPropertyCalls,
        CancellationToken ct)                                                          → Task<int>
    .ExecuteDeleteAsync(ISpecification<TAggregate> spec, CancellationToken ct)         → Task<int>
    NOTE: CORRECTED (P-148, supersedes original WO-024 spec text): the original spec referenced
          `Expression<Func<SetPropertyCalls<TAggregate>, SetPropertyCalls<TAggregate>>>` — this is the
          EF Core 5–8 era API. In EF Core 10.0.5, `SetPropertyCalls<T>` no longer exists; both
          `IQueryable<T>.ExecuteUpdate` / `ExecuteUpdateAsync` take
          `Action<UpdateSettersBuilder<TEntity>>` (a delegate, not an expression tree). The interface
          signature above reflects the actual EF Core 10 API. Lives in SharedKernel.Persistence.EfCore
          because UpdateSettersBuilder<TAggregate> is an EF Core type (Microsoft.EntityFrameworkCore.Query)
          — placing this interface in .Abstractions would introduce an ORM dependency there.
          Implemented directly by EfRepository<TAggregate, TId> (no separate base class).
          Both methods translate to a single server-side ExecuteUpdate / ExecuteDelete SQL statement —
          they bypass the ChangeTracker entirely, which means:
            - IUnitOfWork.SaveChangesAsync is NOT invoked and has no effect on these rows.
            - The three platform interceptors (Audit, SoftDelete, Concurrency) do NOT run.
            - Domain events are NOT collected or dispatched for affected aggregates.
          ExecuteDeleteAsync ALWAYS issues a hard physical DELETE, even when TAggregate implements
          ISoftDeletable — there is no server-side translation for "set IsDeleted = true" semantics
          via ExecuteDelete. Callers needing soft-delete semantics in bulk must use ExecuteUpdateAsync
          with an explicit setPropertyCalls delegate that sets the IsDeleted / DeletedOn columns, e.g.
          `setters => setters.SetProperty(x => ((ISoftDeletable)x).IsDeleted, true)`.

BulkSpecificationGuard  (internal static class)
    .Validate<T>(ISpecification<T> spec)                                              → void
    NOTE: Called at the start of both ExecuteUpdateAsync and ExecuteDeleteAsync. Throws
          UnsupportedSpecificationException if the spec has any of: non-default Includes,
          StringIncludes, OrderBy/OrderByDescending, ThenBys, Skip, or Take — these shapes have
          no meaning for a single server-side ExecuteUpdate/ExecuteDelete statement.
          IncludeDeleted, IsDistinct, and AsNoTracking are tolerated (the latter two are no-ops
          in this path). Only Criteria and IncludeDeleted are applied by the evaluator.

UnsupportedSpecificationException  (sealed class, extends SharedKernelException)
    .ctor(string reason)
    NOTE: Message format: "The specification cannot be used with bulk mutation operations: {reason}".

EfReadRepository<TAggregate, TId>  (abstract class, implements IReadRepository<TAggregate, TId>)
    .GetBySpecAsync(ISpecification<TAggregate> spec, CancellationToken ct)                 → Task<TAggregate?>
    .ListAsync(ISpecification<TAggregate> spec, CancellationToken ct)                      → Task<IReadOnlyList<TAggregate>>
    .CountAsync(ISpecification<TAggregate> spec, CancellationToken ct)                     → Task<int>
    .AnyAsync(ISpecification<TAggregate> spec, CancellationToken ct)                       → Task<bool>
    .GetByIdsAsync(IEnumerable<TId> ids, CancellationToken ct)                             → Task<IReadOnlyList<TAggregate>>
    .GetByIdsChunkedAsync(IEnumerable<TId> ids, int chunkSize, CancellationToken ct)        → Task<IReadOnlyList<TAggregate>>
        (WO-051/P-323)
    .ListPagedAsync(ISpecification<TAggregate> spec, CancellationToken ct)                 → Task<PagedList<TAggregate>>
    .ListProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> spec, ct)    → Task<IReadOnlyList<TResult>>
    .GetBySpecProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> s, ct)  → Task<TResult?>
    .ListPagedProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> spec, ct) → Task<PagedList<TResult>>
    .StreamAsync(ISpecification<TAggregate> spec, CancellationToken ct)
        → IAsyncEnumerable<TAggregate>
    .StreamProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> spec, ct)
        → IAsyncEnumerable<TResult>
    NOTE: Uses ISpecificationEvaluator<T> internally. Pass ReadOnlySpecification<T> subclasses to avoid
          unnecessary change-tracking. AsNoTracking() applied when spec.AsNoTracking == true.
          GetByIdsAsync uses an expression-tree Contains predicate (e => ids.Contains(e.Id)) so that
          registered ValueConverters (e.g., StronglyTypedIdValueConverter) are applied at the property
          level by the LINQ provider. Against Npgsql this generates a single server-side
          WHERE "Id" = ANY(@ids) array-parameter clause — NOT a per-value IN (v1, v2, ...) expansion
          (CORRECTED, WO-051/P-323 — see the Abstractions GetByIdsAsync note above for the full
          guardrail correction). The former EF.Property approach has been removed — it caused silent
          client-side evaluation with strongly-typed IDs.
          GetByIdsChunkedAsync (WO-051/P-323) issues ceil(N/chunkSize) sequential GetByIdsAsync-shaped
          round trips and concatenates the results — a purely additive, opt-in sibling; GetByIdsAsync's
          own implementation is completely unchanged by this addition.
          ListPagedAsync: count query strips Skip/Take; data query applies full spec; both under same DbContext.
          Projection methods delegate to ISpecificationEvaluator<T>.GetProjectedQuery (interface method since P-097);
          no downcast to concrete SpecificationEvaluator<T> — any ISpecificationEvaluator<T> implementation works.
          ListPagedProjectedAsync: count query uses GetQuery (no projection), data query uses GetProjectedQuery;
          returns PagedList<TResult>. Use instead of ListPagedAsync when caller needs DTOs.
          Select applied after paging in all projection paths.
          GetByIdAsync has been removed (P-080 breaking change) — use GetBySpecAsync(new ByIdSpecification<>(...)).
          StreamAsync / StreamProjectedAsync<TResult> (P-149): built on GetQuery / GetProjectedQuery via
          ISpecificationEvaluator<T>, then .AsNoTracking().AsAsyncEnumerable() with [EnumeratorCancellation]
          on the CancellationToken parameter. AsNoTracking() is applied UNCONDITIONALLY here — the ONE
          documented exception to "spec's AsNoTracking flag is honored" — because a long-lived streaming
          enumeration under change tracking would grow the ChangeTracker unbounded for the lifetime of
          the enumeration. If spec.Skip / spec.Take are set, they are applied as a normal row-window by
          the evaluator before the query is converted to IAsyncEnumerable<T> — no special-casing.
```

#### EF Core unit of work (`UnitOfWork/`)

```text
EfUnitOfWork  (sealed class, implements SharedKernel.Persistence.Abstractions.IUnitOfWork,
                                          SharedKernel.Application.Behaviors.IUnitOfWork)
    constructor: EfUnitOfWork(SharedKernelDbContext dbContext, IDomainEventDispatcher? dispatcher = null)
    .SaveChangesAsync(CancellationToken ct)                    → Task<int>
    NOTE: Exactly one public constructor. IDomainEventDispatcher? is a nullable optional parameter resolved
          by the DI container — no second constructor. Adding a second constructor creates DI ambiguity
          (container may silently pick the shorter one and skip the dispatcher). This is a hard violation.
          Delegates to SharedKernelDbContext.SaveChangesAsync.
          All three interceptors (Audit, SoftDelete, Concurrency) fire automatically before the commit.
          No OutboxInterceptor — MassTransit's UseEntityFrameworkOutbox handles outbox in 07.Messaging.
          After SaveChangesAsync succeeds: collects all domain events from ChangeTracker.Entries<IHasDomainEvents>();
          calls IDomainEventDispatcher.DispatchAsync(events, ct) if a dispatcher is registered;
          clears domain events on each aggregate regardless of dispatcher registration (prevents double-dispatch).
          IDomainEventDispatcher is from SharedKernel.Domain (03.Domain) — no 05.Application reference for
          this dependency (the event dispatcher is unrelated to the dual-IUnitOfWork bridge below).
          Dispatcher is optional: consuming services opt in by registering IDomainEventDispatcher in DI.
          Dispatch failure does not roll back the already-committed transaction (document as known trade-off).
          DUAL-INTERFACE BRIDGE (P-228): EfUnitOfWork additionally implements
          SharedKernel.Application.Behaviors.IUnitOfWork — the minimal local seam TransactionBehavior
          depends on, declared in 05.Application because 05.Application cannot reference 06.Persistence.
          Both interfaces declare a structurally compatible SaveChangesAsync(CancellationToken) →
          Task<int> member, so the single existing method body satisfies both contracts — no branching,
          no second method. Requires a direct ProjectReference from SharedKernel.Persistence.EfCore to
          SharedKernel.Application.Behaviors (legal: 06 may reference 01–05). Registration against the
          second interface is OPT-IN via EfCorePersistenceBuilder.WithApplicationTransactionBehavior()
          (see below) — omitting it leaves EfUnitOfWork registered only against
          SharedKernel.Persistence.Abstractions.IUnitOfWork, exactly as before P-228.

EfTransactionalUnitOfWork  (sealed class, implements ITransactionalUnitOfWork)
    .SaveChangesAsync(CancellationToken ct)                    → Task<int>      (inherited)
    .BeginTransactionAsync(CancellationToken ct)               → Task<IPersistenceTransaction>
    .ExecuteInTransactionAsync(...) / ExecuteInTransactionAsync<TResult>(...)                  (WO-051/P-320)
    NOTE: Registered only when EfCorePersistenceBuilder.WithTransactionalUnitOfWork() is called.
          Wraps DbContext.Database.BeginTransactionAsync; returns an EfPersistenceTransaction adapter
          that implements IPersistenceTransaction.
          DISPATCH DEFERRAL RULE (P-105): SaveChangesAsync checks DbContext.Database.CurrentTransaction.
          When a transaction IS active (non-null): calls only DbContext.SaveChangesAsync — domain event
          dispatch is deferred to EfPersistenceTransaction.CommitAsync (fires after DB commit succeeds).
          When NO transaction is active (null): calls SaveChangesAsync then dispatches immediately,
          matching EfUnitOfWork semantics exactly.
          EfPersistenceTransaction.RollbackAsync does NOT dispatch domain events.
          Application layer injects ITransactionalUnitOfWork — never IDbContextTransaction directly
          (hard violation, enforced by governance Rule P-103).
          RETRY-SAFETY GUARD (WO-051/P-320): BeginTransactionAsync() checks
          DbContext.Database.CreateExecutionStrategy().RetriesOnFailure BEFORE delegating to EF Core's
          native BeginTransactionAsync. When true, throws a platform-specific, actionable
          InvalidOperationException directing the caller to ExecuteInTransactionAsync instead — EF Core's
          retrying execution strategies require the ENTIRE transactional unit (begin through commit) to
          run inside one IExecutionStrategy.ExecuteAsync(...) delegate; the handle-based
          BeginTransactionAsync → caller-held IPersistenceTransaction → CommitAsync shape hands control
          back to arbitrary caller code in between, which is structurally incompatible with that
          contract. This guard is UNCONDITIONAL — it queries live EF Core state, not any
          EfCorePersistenceBuilder flag — so it correctly fires even when retry was enabled solely via
          UsePostgreSQL(..., maxRetryCount) without ever calling .WithTransientFaultRetry().
          ExecuteInTransactionAsync/<TResult> (WO-051/P-320): the retry-safe alternative — wraps
          DbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () => { begin; invoke
          operation; commit }), reusing the same begin/commit machinery (and the P-105 deferred-dispatch-
          until-commit rule) as BeginTransactionAsync/EfPersistenceTransaction.CommitAsync. The whole
          delegate — including a fresh BeginTransactionAsync — re-runs on each retry attempt; a failed
          attempt's transaction rolls back via IDbContextTransaction's dispose-without-commit semantics
          before the next attempt begins, so no partial/duplicate commit occurs. See "Transient-fault
          retry" below for the full picture, including the required pairing with UsePostgreSQL(...)'s
          retry parameters.
```

#### Specification evaluator (`Specifications/`)

```text
SpecificationEvaluator<T>  (sealed class, implements ISpecificationEvaluator<T>)
    .GetQuery(IQueryable<T> inputQuery, ISpecification<T> spec) → IQueryable<T>
    .GetProjectedQuery<TResult>(IQueryable<T> inputQuery, IProjectionSpecification<T,TResult> spec) → IQueryable<TResult>
    .GetKeysetQuery<TKey>(IQueryable<T> inputQuery, KeysetSpecification<T,TKey> spec) → IQueryable<T>     (WO-051/P-317)
    Applies in strict order (see the canonical table in Implementation Rules for the authoritative version):
        -1. TagWith(spec.GetType().Name) — automatic, zero-configuration, applied first (WO-051/P-319)
        0.  IgnoreQueryFilters() — only when spec.IncludeDeleted == true; applied before all other steps
        1.  Criteria (Where clause) — if non-null; null Criteria matches all entities
        1b. Keyset seek predicate — GetKeysetQuery<TKey> ONLY; skipped entirely on the first page
            (spec.AfterKey/AfterId both null); see "Keyset (cursor) pagination" below (WO-051/P-317)
        2.  Includes (expression-based eager loading — ThenInclude supported via include expressions)
        2b. StringIncludes — applied after expression includes and before ordering; each entry in
            spec.StringIncludes is applied via IQueryable<T>.Include(string); empty list is a no-op
        2c. AsSplitQuery() — only when spec.AsSplitQuery == true; applied to GetQuery, GetProjectedQuery,
            and GetKeysetQuery<TKey> alike (WO-051/P-318)
        3.  OrderBy / OrderByDescending — primary sort; last-call wins
        4.  ThenBys — secondary sorts; only applied if a primary sort is already set
        5.  Distinct (Distinct())
        6.  AsNoTracking (AsNoTracking())
        7.  Skip / Take — paging; ALWAYS applied last for the aggregate pipeline for GetQuery/
            GetProjectedQuery. GetKeysetQuery<TKey> DEVIATES (WO-051/P-317, documented explicitly, the
            SECOND deviation from "spec's own values are honored" alongside StreamAsync's forced
            AsNoTracking): never applies Skip (always 0 for a keyset spec); applies
            .Take(spec.Take!.Value + 1) — one row beyond the declared Take — so
            EfReadRepository.ListKeysetAsync<TKey> can compute HasMore without a second round-trip.
        8.  Select(spec.Selector) — projection overload only; applied after Skip/Take
    NOTE: Paging is always the final operation before projection so ordering is stable before any Skip/Take.
          ThenBys are silently ignored when no primary sort is configured.
          StringIncludes are intended for deep navigation paths (e.g., "Orders.Items.Product") where
          expression-based ThenInclude chains become cumbersome. Existing specs not calling
          AddStringInclude are unaffected — empty StringIncludes is a no-op.
          QueryableExtensions.IgnoreSoftDeleteFilter() has been removed (P-080 breaking change).
          Use spec.IncludeDeleted = true instead — the evaluator calls .IgnoreQueryFilters() automatically.
```

#### Keyset (cursor) pagination (WO-051/P-317)

Cursor/seek-pagination sibling to the existing offset-based `ListPagedAsync`/`PagedSpecification<T>` path — additive, not a replacement. Both remain fully available.

```text
Read path:  EfReadRepository.ListKeysetAsync<TKey>(KeysetSpecification<TAggregate,TKey> spec, ct)
                → calls _evaluator.GetKeysetQuery(DbContext.Set<TAggregate>(), spec)
                → materializes via ToListAsync (fetches spec.Take + 1 rows — see step 7 deviation above)
                → hasMore = rows.Count > spec.Take!.Value; trims to the declared page size when true
                → computes NextAfterKey/NextAfterId from the LAST row of the trimmed page by compiling
                  spec.OrderBy/OrderByDescending and spec.ThenBys[0].KeySelector to Func<TAggregate,object>
                  delegates (.Compile(), once per call — same accepted cost class as
                  Specification<T>.IsSatisfiedBy's Criteria.Compile()) and invoking them against the last
                  item, casting the boxed key result to TKey (safe — TKey is statically known at the
                  ListKeysetAsync<TKey> call site, zero reflection)
                → returns KeysetPage<TAggregate,TKey> { Items, NextAfterKey, NextAfterId, HasMore }

Seek predicate (GetKeysetQuery<TKey>, evaluator step 1b):
    Skipped entirely on the first page (spec.AfterKey/AfterId both null).
    Otherwise builds Expression<Func<T,bool>> for the standard keyset/seek-pagination tuple-comparison
    expansion:
        (OrderKey > @afterKey) OR (OrderKey == @afterKey AND Id > @afterId)
        — flipped to "<" throughout when spec.Descending == true.
    Built via Expression.Parameter/GreaterThan/LessThan/Equal/OrElse/AndAlso, using spec.OrderBy/
    OrderByDescending and spec.ThenBys[0].KeySelector (both already Expression<Func<T,object>>,
    populated by KeysetSpecification<T,TKey>'s own constructor) rebound onto one shared parameter via a
    local ParameterReplacer ExpressionVisitor. Each selector's outer Convert(..., typeof(object)) node is
    unwrapped back to its real typed operand (TKey / the Id CLR type) before comparison, and the
    AfterKey/AfterId constants are built via Expression.Constant(value, unwrappedOperandType) — CRITICAL
    so any registered ValueConverter (e.g. StronglyTypedIdValueConverter on the Id) is applied
    server-side by the LINQ provider, exactly the precedent P-105 established for GetByIdsAsync's
    Contains fix. Building the Id comparison via a boxed-object/EF.Property-style shortcut instead would
    silently reintroduce that exact defect.

When to prefer keyset over offset:
    - Large or actively-written tables where deep OFFSET pagination's O(n)-scan-and-discard cost is a
      real, measured problem.
    - Infinite-scroll / "load more" UI patterns with no need to jump to an arbitrary page number.
    - Correctness under concurrent inserts between page fetches — a genuine property offset paging
      lacks: a seek predicate anchored to the last-seen key/id value is immune to rows shifting the
      integer-offset page boundary; OFFSET-based paging can silently skip or duplicate rows when the
      underlying table is written to between two page fetches.
When to prefer offset (ListPagedAsync/PagedSpecification<T>):
    - The caller needs to jump to an arbitrary page number (page 7 of 40) or display a total-page-count
      UI, neither of which a cursor supports.
    - Small or rarely-written tables where the offset-scan cost is negligible.
```

#### Interceptors (`Interceptors/`)

```text
AuditInterceptor  (sealed class, implements ISaveChangesInterceptor)
    — On SavingChanges/SavingChangesAsync: populates IHasCreatedAudit.CreatedBy / CreatedOn for Added entries;
      populates IHasAudit.ModifiedBy / ModifiedOn for Modified entries.
    — Uses EF ChangeTracker entry CurrentValues[propertyName] — NEVER direct property setters on aggregates.
    — Constructor injection: IUserContext (scoped DI — see IUserContext pattern below), IClock (from 01.Core).

SoftDeleteInterceptor  (sealed class, implements ISaveChangesInterceptor)
    — On SavingChanges/SavingChangesAsync: converts Deleted state to Modified for ISoftDeletable entities;
      sets IsDeleted = true, DeletedOn = clock.UtcNow, DeletedBy = current user via EF ChangeTracker.
    — Non-ISoftDeletable entities pass through without modification.
    — Constructor injection: IUserContext (scoped DI), IClock.

ConcurrencyInterceptor  (sealed class, implements ISaveChangesInterceptor)
    — On SaveChangesFailed/SaveChangesFailedAsync: catches DbUpdateConcurrencyException for IHasConcurrency entries;
      rethrows as typed ConcurrencyException carrying Error.Conflict(...) from SharedKernel.Primitives.
    — Does NOT retry — conflict resolution is the application layer's responsibility.
    — Non-concurrency exceptions propagate unchanged.

NOTE: Exactly three interceptors are ALWAYS registered by this package. No OutboxInterceptor —
      MassTransit's UseEntityFrameworkOutbox is the outbox infrastructure owner at 07.Messaging.

AuditRecordImmutabilityInterceptor  (sealed class, implements ISaveChangesInterceptor — SHIPPED, P-457/WO-071)
    — A FOURTH interceptor, registered ONLY when the audit-trail capability is opted into via
      EfCorePersistenceBuilder.WithAuditTrail() — never registered by default, unlike the platform three above.
    — On SavingChanges/SavingChangesAsync: inspects ChangeTracker.Entries<AuditRecord>() and THROWS a new
      AuditRecordImmutableException (extends SharedKernelException) if ANY entry's EntityState is Modified
      or Deleted. This is the audit-trail capability's LOAD-BEARING piece — structural immutability enforced
      at the ORM boundary, not left to convention.
    — XML docs additionally recommend, IN CAPITALS, a DB-level REVOKE UPDATE, DELETE grant on the underlying
      table as defense-in-depth, mirroring this package's existing encryption/soft-delete documentation style.

EncryptionKeyPreWarmingInterceptor  (internal sealed class, extends SaveChangesInterceptor, implements
                                      IDbCommandInterceptor — SHIPPED, P-498/WO-081, D-130)
    — A FIFTH interceptor, registered ONLY by EfCorePersistenceBuilder.WithExternalEncryptionKeyProvider
      <TProvider>() (the KMS/external-provider encryption mode) — never present in the default
      config-backed .WithEncryption() path.
    constructor: EncryptionKeyPreWarmingInterceptor(PreWarmedEncryptionKeyProvider provider)
    NOTE: SINGLE-PARAMETER constructor — takes only the provider, NOT a separate IEncryptionVersionOverride
          (the design record's original two-parameter sketch was corrected during implementation:
          provider.WarmCurrentAsync() already resolves the override internally, so the interceptor
          itself never needs to see it).
    — Full contract documented under "Encryption" below. In summary: warms PreWarmedEncryptionKeyProvider's
      in-memory key cache via TWO async hooks — SavingChangesAsync (writes, before ConvertToProviderExpression
      runs) and IDbCommandInterceptor.ReaderExecutingAsync (reads, before row materialization begins) — so
      the synchronous ISymmetricEncryptionService.Encrypt/Decrypt calls EncryptedValueConverter must make
      (EF Core's ValueConverter has no async path) land on an already-warm cache entry in the overwhelming
      common case, closing the F1 SEVERE thread-pool-starvation defect for both the write AND read paths.
```

#### IUserContext injection pattern and audit string format

`AuditInterceptor` and `SoftDeleteInterceptor` require `IUserContext` from `SharedKernel.Security.Abstractions` to resolve the current user. `SharedKernel.Persistence.EfCore` holds a deliberate project reference to `SharedKernel.Security.Abstractions` — this is an approved layering exception: `12.Security.Abstractions` is a zero-dependency interface library, and the alternative (maintaining local interface copies) creates divergence risk. The pattern:

1. `IUserContext` is sourced from `SharedKernel.Security.Abstractions`. `UserId` is `Guid`; `IsAuthenticated` is `bool`.
2. `EfCorePersistenceBuilder.Build()` registers a scoped no-op `IUserContext` placeholder (`UserId = Guid.Empty`, `IsAuthenticated = false`) if no `IUserContext` is already registered in the DI container.
3. Consuming services register their own `IUserContext` implementation (from `12.Security.Oidc` or similar) before or after `.Build()` — the last registration wins.
4. Interceptors are registered as **scoped** services so they receive a per-request `IUserContext` from DI.

**Audit string format rule (P-091, updated WO-019):**

Audit columns (`CreatedBy`, `ModifiedBy`, `DeletedBy`) are `string` with `HasMaxLength(256)`. The string value is produced as:

- When `userContext.IsAuthenticated == true && userContext.UserId != Guid.Empty`: write `userContext.UserId.ToString("D")` — lowercase hyphenated GUID, 36 characters, e.g. `"a1b2c3d4-e5f6-7890-abcd-ef1234567890"`.
- Otherwise: write `PersistenceServiceOptions.ServiceName` — defaults to `"system"` but is configurable per-service via `EfCorePersistenceBuilder.WithServiceName(string)`.

No other format is permitted. The `"D"` format specifier is mandatory for authenticated users — `"N"`, `"B"`, `"P"`, and `"X"` formats are all violations. The unauthenticated fallback must always come from `PersistenceServiceOptions.ServiceName`; the hardcoded literal `"system"` is no longer permitted in `AuditInterceptor.ResolveUserId()` — it must be the options default value only.

#### Auditing — EfCore implementation (`Auditing/`) — P-457/WO-071, shipped 2026-09-02

Implements the `SharedKernel.Persistence.Abstractions` audit-trail contracts documented above (see that package's `Auditing` subsection for `AuditRecord`/`AuditEntry`/`IAuditTrailWriter`/`IAuditQueryService`/`IAuditActorContext`). Everything below is **opt-in only**, registered exclusively by `EfCorePersistenceBuilder<TContext>.WithAuditTrail()` — a service that never calls it gets none of these types, no `AuditRecord` table in its model, and `AuditRecordImmutabilityInterceptor` never registered (see Interceptors above).

```text
AuditRecordEntityConfiguration  (sealed class, implements IEntityTypeConfiguration<AuditRecord> directly — SHIPPED)
    — Deliberately does NOT extend EntityTypeConfigurationBase<TEntity,TId> — AuditRecord is a plain
      infrastructure record, not an aggregate root, and carries none of that base's marker interfaces
      (IHasConcurrency, ISoftDeletable, IHasCreatedAudit/IHasAudit, IHasTenant).
    — HasKey(x => x.Id); Id is ValueGeneratedNever() — the writer (EfAuditTrailWriter) always supplies
      Guid.CreateVersion7() itself, never DB-generated.
    — ActorId/CorrelationId/ApprovalId: HasMaxLength(256), matching AuditInterceptor's existing audit-
      string-format column length (P-091) — reuse, not a second convention.
    — RecordHash/PreviousRecordHash: HasMaxLength(64) — hex SHA-256 digest length (32 bytes → 64 hex chars).
    — BeforeSnapshot/AfterSnapshot: HasColumnType("text") — PLAIN TEXT, NEVER jsonb. This package never
      parses or validates caller-supplied snapshot content; it is opaque to every type in this section.
    — Two composite indexes, one per IAuditQueryService access pattern: (TenantId, ResourceType,
      ResourceId, OccurredOn) for GetResourceHistoryAsync; (TenantId, ActorId, OccurredOn) for
      GetActorActionsAsync.
    — Applied by SharedKernelDbContext.OnModelCreating ONLY when its optional AuditTrailFeatureMarker?
      constructor parameter is non-null — never discovered via ApplyConfigurationsFromAssembly, because
      this type lives in THIS assembly (SharedKernel.Persistence.EfCore), not the downstream context's.

AuditTrailFeatureMarker  (sealed class, no members — SHIPPED)
    — A DI-resolvable marker type standing in for a bool flag: DI cannot auto-resolve a raw primitive
      constructor parameter, only a registered type — the SAME mechanism already used for
      ISymmetricEncryptionService?/IEncryptionKeyProvider? signaling ".WithEncryption() was called" to
      SharedKernelDbContext's constructor. Registered as a singleton by .WithAuditTrail() only.
    — A downstream DbContext that wants the audit trail MUST declare AuditTrailFeatureMarker? in its own
      constructor and forward it to base(...) — see DI Registration example below.

EfAuditTrailWriter  (sealed class, implements IAuditTrailWriter — SHIPPED)
    — RecordAsync is SELF-CONTAINED: it reads the latest PreviousRecordHash for the (TenantId,
      ResourceType) partition, computes the new record's RecordHash via the shared AuditRecordHasher,
      adds the AuditRecord to the injected SharedKernelDbContext, and calls SaveChangesAsync ITSELF —
      immediately, NOT staged into the caller's ambient DbContext/IUnitOfWork alongside unrelated
      business-entity changes. Audit correctness must never be contingent on whether, or when, the
      caller's own unrelated IUnitOfWork.SaveChangesAsync() eventually fires.
    — Id = Guid.CreateVersion7(); OccurredOn = IClock.UtcNow (never caller-supplied — prevents forged
      timestamps); ActorId/TenantId resolved from the injected IAuditActorContext, never from a caller
      parameter.
    — DOCUMENTED, NOT FULLY CLOSED, race: PreviousRecordHash resolution and the new record's insert are
      two sequential statements. Under ordinary READ COMMITTED isolation, two truly concurrent
      RecordAsync calls against the SAME (TenantId, ResourceType) partition could both read the same
      "latest" hash and both succeed, forking the chain. VerifyChainIntegrityAsync would then detect
      exactly one of the two as broken. Closing this fully needs SERIALIZABLE isolation or a partition-
      scoped advisory lock — out of scope for this phase; ordinary application code does not, in
      practice, issue concurrent audit writes for the same resource.
    — DOCUMENTED, NOT FULLY CLOSED, same-millisecond tiebreak: when two records in the same partition
      share an identical OccurredOn, "latest" selection falls back to comparing Guid.CreateVersion7()
      Ids — CONFIRMED EMPIRICALLY (see Known Limitations below) that same-millisecond UUIDv7 values do
      NOT reliably compare in generation order. The hash chain itself stays internally CONSISTENT even
      when this happens; only the wall-clock "latest" selection is approximate in this narrow window.

EfAuditQueryService  (sealed class, implements IAuditQueryService — SHIPPED)
    — Built DIRECTLY on ISpecificationEvaluator<AuditRecord> + DbContext.Set<AuditRecord>(), NOT
      IReadRepository<AuditRecord,Guid> — AuditRecord deliberately does not implement IAggregateRoot<Guid>
      (see the Abstractions Auditing subsection above), so there is no IReadRepository to build on.
    — GetResourceHistoryAsync/GetActorActionsAsync call ISpecificationEvaluator<T>.GetKeysetQuery, NEVER
      GetQuery — GetQuery SILENTLY IGNORES a KeysetSpecification's AfterKey/AfterId and always returns
      the first page; only GetKeysetQuery honors the cursor. Passing a keyset spec to GetQuery compiles
      and runs with no error, which is exactly why this distinction is called out explicitly here.
    — VerifyChainIntegrityAsync streams records in a (TenantId, ResourceType, [from,to]) range ordered by
      (OccurredOn, Id) via AsAsyncEnumerable, recomputing each record's hash via the SAME
      AuditRecordHasher.ComputeHashHex the writer used and comparing it to the stored RecordHash (detects
      a mutated record), then confirms each record's PreviousRecordHash matches the actual hash of the
      record immediately preceding it in the scanned range (detects a missing/reordered link WITHIN the
      range — a predecessor outside [from, to] is not this call's concern). Returns
      AuditChainVerificationResult.Broken(recordId, recordsChecked) at the first failure, or
      .Intact(recordsChecked) if the whole range checks out.

AuditRecordHasher  (internal static class — SHIPPED)
    — The ONE place the exact field order and encoding for the hash-chain digest is defined, shared by
      EfAuditTrailWriter (write-time) and EfAuditQueryService (verify-time) so the two can never drift
      apart. ComputeHashHex(IContentHasher, ...) canonicalizes every AuditRecord field as a flat scalar,
      joined with the ASCII Unit Separator (U+001F) — a control character that can never appear in any
      of the canonicalized values, so it is a safe, unambiguous field delimiter — encodes as UTF-8, hashes
      via 01.Core's IContentHasher.ComputeHash(byte[]), and hexes the digest via the plain BCL static
      Convert.ToHexStringLower(byte[]). Delimited plain-text, NEVER a JSON round-trip — a generic
      serializer's field-ordering/escaping behavior could silently vary across versions; a fixed-order,
      fixed-delimiter concatenation cannot.

EfCoreAuditActorContext  (sealed class, implements IAuditActorContext — SHIPPED)
    — The DEFAULT IAuditActorContext, bridging the already-approved IUserContext/ITenantProvider
      (SharedKernel.Security.Abstractions, P-078 exception — see "IUserContext injection pattern" above).
      Registered by .WithAuditTrail() ONLY when the consuming service has not already registered its own
      IAuditActorContext (last-registration-wins is preserved as an override path, not a requirement).
    — ActorId computed via the SAME userId.ToString("D")/PersistenceServiceOptions.ServiceName-fallback
      format AuditInterceptor already uses (P-091) — reuse, not a second convention. TenantId delegates
      straight to ITenantProvider.TenantId.
    — A genuine ergonomic advantage this package can offer that 05.Application's equivalent
      IAuthorizationContext structurally cannot: that package has zero Security.Abstractions access of
      any kind and can never ship a default bridge of its own — see "IAuditActorContext vs. the P-078
      exception" in the Abstractions Auditing subsection above for the full reasoning.
```

#### Type configurations (`Configurations/`)

```text
EntityTypeConfigurationBase<TEntity, TId>  (abstract class, implements IEntityTypeConfiguration<TEntity>)
    — Applies when base.Configure(builder) is called:
        • Primary key on TId
        • Concurrency token (.IsConcurrencyToken() ONLY — see WO-051/P-315 correction below)
          for IHasConcurrency entities
        • Global query filter e => !e.IsDeleted for ISoftDeletable entities
        • Owned audit columns CreatedBy (max-length string, not null) and CreatedOn (DateTimeOffset, not null)
          for IHasCreatedAudit
        • Additionally ModifiedBy (nullable string) and ModifiedOn (nullable DateTimeOffset) for IHasAudit
        • TenantId column (Guid, not null) + tenant index for IHasTenant entities
    NOTE: Concrete configurations must call base.Configure(builder) first, then add entity-specific mappings.
          CONCURRENCY TOKEN CORRECTION (WO-051/P-315): the previous XML doc on this class claimed a
          "ConcurrencyTokenConvention" existed that would override .IsRowVersion() with ".UseXminAsConcurrencyToken()"
          for PostgreSQL — that type never existed anywhere outside the doc comment and a design-phase task
          description (root-caused: a provider-branching convention inside SharedKernel.Persistence.EfCore is
          architecturally impossible, since .UseXminAsConcurrencyToken() is an Npgsql-only extension method and
          EfCore must never reference Npgsql). Corrected split: this class marks the IHasConcurrency property with
          .IsConcurrencyToken() ONLY — a provider-neutral EF Core concept (the property is included in the UPDATE
          WHERE clause) with zero assumption about server-side auto-generation. The genuinely-working PostgreSQL
          mechanism (binding that property to the real xmin system column) lives entirely in
          SharedKernel.Persistence.PostgreSQL's XminConcurrencyTokenConvention — see that package's section below.
          Calling .IsRowVersion() directly here was provably non-functional against a plain PostgreSQL bytea
          column: nothing in Postgres auto-populates an arbitrary bytea on UPDATE the way SQL Server's native
          rowversion type does, so the token value never changed and concurrent writes never conflicted.

StronglyTypedIdValueConverter<TStronglyTypedId, TValue>  (sealed class, extends ValueConverter<TStronglyTypedId, TValue>)
    — Converts StronglyTypedId<TValue> to/from its primitive TValue for EF Core column mapping.
    — Uses implicit operator TValue for to-provider direction — no Activator.CreateInstance, no reflection.
    — Companion ModelConfigurationBuilder extension auto-registers the converter for all IStronglyTypedId<TValue>
      types, eliminating per-aggregate manual converter registration.

CurrencyValueConverter  (sealed class, extends ValueConverter<Currency, string> — SHIPPED, P-440/WO-066)
    — Converts 03.Domain's Currency (a SingleValueObject<string>) to/from its ISO 4217 code string.
    — To-provider: Currency's existing implicit operator to string — (string)currency, zero reflection.
    — From-provider: the PUBLIC Currency.Create(code) → Result<Currency> factory (P-310), unwrapping .Value —
      NEVER StronglyTypedIdValueConverter's reflection-located-constructor technique, which exists only
      because StronglyTypedId<TValue> (a different base hierarchy) has no public Create factory. A stored,
      unreconstructible code throws InvalidOperationException (data assumed already-validated at write time).

MoneyValueConverter  (sealed class, extends ValueConverter<Money, string> — SHIPPED, P-440/WO-066)
    — Packs Money's Amount + Currency into a SINGLE "{amount}:{currencyCode}" string column, reconstructed
      via the PUBLIC Money.Create(amount, currency) factory — zero reflection.
    — THIS IS THE SHIPPED SHAPE, NOT the two-column owned-type design originally preferred (see below for why).

MoneyEntityTypeBuilderExtensions.OwnsMoney<TEntity>(EntityTypeBuilder<TEntity> builder,
    Expression<Func<TEntity,Money>> propertyExpression, string? columnName = null)
    (static extension — SHIPPED, P-440/WO-066 — SIGNATURE CORRECTED from the original two-column design below)
    — Configures a Money-typed property's column name/max-length ONLY (HasMaxLength(40), optional HasColumnName)
      — it does NOT itself call .HasConversion(...). The conversion is registered GLOBALLY, once per DbContext,
      by ModelConfigurationBuilderExtensions.ConfigureMoney() (below) — REQUIRED to be called from
      ConfigureConventions() before .OwnsMoney(...) is used anywhere in that context's model.
    — DESIGN CORRECTION (D-106): the original PREFERRED path — an EF Core owned type with two independently
      queryable/filterable columns (Amount decimal(19,4), Currency string), materialized via a
      reflection-located-constructor + compiled-Expression.New technique mirroring StronglyTypedIdValueConverter
      to supply Money's private ctor's third argument (RoundingPolicy) — was EVALUATED AGAINST THE REAL EF CORE 10
      ASSEMBLY and found UNREACHABLE via any public API: ITypeBase.ConstructorBinding is read-only on every
      public metadata surface (IMutableEntityType, IConventionEntityType); a setter exists only on the
      internal Microsoft.EntityFrameworkCore.Metadata.Internal.TypeBase type (confirmed by reflecting over the
      real Microsoft.EntityFrameworkCore.dll, 10.0.10). Using it would mean reflecting into EF Core's OWN
      internals — a materially different, far more fragile risk than the already-accepted "reflect over our
      own domain type" precedent StronglyTypedIdValueConverter sets. The packed-string FALLBACK D-106
      pre-authorized shipped instead (MoneyValueConverter above).
    — DOCUMENTED COST of the packed-string shape: Amount/Currency are NOT independently queryable/filterable in
      SQL from this column (no WHERE Currency = 'USD' AND Amount > ..., no SUM(Amount) WHERE Currency = 'USD').
      A service genuinely needing that must maintain its own separate, independently-mapped shadow columns.
    — ValueObjectOwnershipBuilder's generic IValueObject scan EXCLUDES Money-typed properties — a Money
      property must always be configured explicitly via .OwnsMoney(...), never silently auto-owned with a
      wrong-precision default decimal column (mirrors the "precision/security-sensitive is opt-in-only"
      philosophy already applied to .Encrypt()).
    — BUG FOUND AND FIXED (2026-09-02, writing T-122/T-123): the Money-specific skip above was NOT sufficient
      on its own. A bare, standalone Currency-typed property (never wrapped in Money — e.g. a "preferred
      currency" field) also implements IValueObject and is ALSO globally scalar-converted by ConfigureMoney(),
      but had no explicit skip, so ValueObjectOwnershipBuilder tried OwnsOne(typeof(Currency), propName) on top
      of the already-scalar property and crashed model building ("property or navigation ... already exists").
      Fixed by generalising the skip: ValueObjectOwnershipBuilder.Apply now also skips any CLR property that
      is ALREADY mapped as a scalar EF property (entityType.FindProperty(propName) is not null) at scan time —
      covers Currency and any future globally-converted IValueObject type, not just a hardcoded Money check.
      Regression test: MoneyValueConverterTests.ValueObjectOwnershipBuilder_DoesNotAutoOwn_StandaloneCurrencyProperty.

ModelConfigurationBuilderExtensions.ConfigureMoney()  (extension on ModelConfigurationBuilder — SHIPPED, P-440/WO-066)
    — Registers CurrencyValueConverter/MoneyValueConverter GLOBALLY via Properties<Currency>()/Properties<Money>()
      .HaveConversion<...>() — mirrors the existing ConfigureStronglyTypedId<TId,TValue>() pattern exactly.
    — LOAD-BEARING, NOT OPTIONAL: call it from ConfigureConventions(), e.g.
          protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
          {
              configurationBuilder.ConfigureMoney();
              base.ConfigureConventions(configurationBuilder);
          }
      EF Core's automatic navigation/entity-type discovery walks a DbSet-reachable entity's CLR properties
      BEFORE OnModelCreating's body ever runs — a Money-typed property with no GLOBALLY-registered conversion
      yet gets auto-discovered as a navigation, and EF recurses into Money.Currency too, creating a phantom
      Currency entity type that then fails model finalization with "No suitable constructor was found for the
      type 'Currency'" (CONFIRMED EMPIRICALLY while building this converter). A later per-property
      .HasConversion(...) call inside .OwnsMoney(...) cannot undo this — it only converts the OUTER property
      back to scalar; the transitively-discovered, now-orphaned Currency entity type is left behind. Calling
      .OwnsMoney(...) on a context that never called ConfigureMoney() crashes at model-build time.
```

#### Conventions (`Conventions/`)

```text
ValueObjectOwnershipBuilder  (static class)
    .Apply(ModelBuilder modelBuilder) → void
    NOTE: Static utility method — NOT an IModelFinalizingConvention and does NOT auto-apply.
          Must be called manually from OnModelCreating after all entity configurations are applied.
          Scans all non-owned entity types; calls OwnsOne for any property whose CLR type implements
          IValueObject that is not already explicitly configured.
          OwnsMany collections are excluded — register those explicitly in entity configuration classes.
          Startup cost: O(n×m); early-exit when no IValueObject properties present.
          Do NOT register via ConfigureConventions — it will have no effect there.
```

#### Encryption (`Encryption/`) — WO-019

Field-level transparent encryption for `string` EF Core properties. Zero domain-layer leakage — entities carry no encryption attributes. All configuration lives in `IEntityTypeConfiguration<T>` implementations.

```text
EncryptionOptions  (options POCO, section "SharedKernel:Encryption")
    .SectionName      (public const string = "SharedKernel:Encryption")                    (WO-053/P-334)
    .Enabled          (bool, default false)   — master on/off switch; false = plaintext pass-through
    .CurrentVersion   (string)                — version tag for new encryptions, e.g. "v1"; must exist in Keys
    .Keys             (Dictionary<string,string>) — version → Base64-encoded 32-byte AES key
    NOTE: Startup validation fires when Enabled == true:
          (a) CurrentVersion non-null/non-empty; (b) CurrentVersion key exists in Keys;
          (c) every key value decodes to exactly 32 bytes.
          Register via EfCorePersistenceBuilder.WithEncryption(action) — the ORIGINAL code-based path,
          unchanged and fully supported — OR the genuinely real EfCorePersistenceBuilder.WithEncryption(
          IConfiguration configuration, Action<EncryptionOptions>? configure = null) config-binding
          overload (WO-053/P-334, closing a confirmed doc/code mismatch: this type's own doc long claimed
          a bound section that no code ever actually read). The config overload binds via
          configuration.GetSection(SectionName) — never a bare literal — then, when configure is supplied,
          layers it on top via the SAME .Configure(configure) registration the code-only overload already
          performs; normal IOptions<T> later-registration-wins semantics apply. The eager startup
          validation above is registered exactly once regardless of how many .WithEncryption(...) overloads
          are chained in the same builder.

PersistenceServiceOptions  (options POCO, section "SharedKernel:Persistence")
    .SectionName      (public const string = "SharedKernel:Persistence")                   (WO-053/P-334)
    .ServiceName      (string, default "system") — unauthenticated audit fallback written to CreatedBy/ModifiedBy/DeletedBy
    NOTE: Replaces the hardcoded "system" literal in AuditInterceptor.ResolveUserId().
          Register via EfCorePersistenceBuilder.WithServiceName(string) — the ORIGINAL direct-value path,
          unchanged and fully supported — OR the genuinely real EfCorePersistenceBuilder.WithServiceName(
          IConfiguration configuration) config-binding overload (WO-053/P-334), which binds via
          configuration.GetSection(SectionName). Composes with the string overload under the identical
          later-call-wins semantics as EncryptionOptions above.
          Startup validation: ServiceName must be non-null, non-empty, ≤ 256 characters — unchanged, and
          now fires against a config-bound value exactly as it already fires against a code-supplied one.

EncryptedValueConverter  (sealed class, extends ValueConverter<string, string>)
    constructor (CURRENT, shipped 2026-09-08 per D-128/P-498, BREAKING vs. the P-448 shape below):
        EncryptedValueConverter(IOptionsMonitor<EncryptionOptions> optionsMonitor,
                                 ISymmetricEncryptionService symmetricEncryptionService,
                                 byte[] associatedData,
                                 IEncryptionVersionOverride? versionOverride = null)
    SHIPPED (P-498/WO-081, D-128): gained a required byte[] associatedData parameter — the AAD
        01.Core's P-491 requires on every Encrypt/Decrypt call, inserted BETWEEN
        symmetricEncryptionService and the optional versionOverride. Computed ONCE by
        EncryptionModelConvention at model-finalization time per annotated property, as
        UTF8Bytes("{schema ?? "public"}.{table}.{column}") (or the caller-supplied
        associatedDataOverride string — see .Encrypt() below) — reproducible at decrypt time by
        construction, since one converter instance already exists per property and always supplies the
        SAME bytes; no row-PK access needed, closing 01.Core's own D-67 "hardest of the six" open
        question. Passed straight through to symmetricEncryptionService.Encrypt(plaintextBytes,
        associatedData)/.Decrypt(payload, associatedData). A mismatched AAD (e.g. a ciphertext value
        spliced from a DIFFERENT column into this one) fails authentication and flows through the
        EXISTING generic CryptographicException path — no new failure shape. RENAME HAZARD, documented
        IN CAPITALS on the type itself: renaming the table/column a property is stored under changes its
        derived AAD and makes every EXISTING row's ciphertext for that property permanently
        undecryptable; supply a stable associatedDataOverride BEFORE renaming if this is a concern.
    constructor (PRE-P-498, shipped 2026-09-03 per D-108/P-448, superseded above):
        EncryptedValueConverter(IOptionsMonitor<EncryptionOptions> optionsMonitor,
                                 ISymmetricEncryptionService symmetricEncryptionService,
                                 IEncryptionVersionOverride? versionOverride = null)
    SHIPPED (P-448/WO-068, D-108, breaking): the constructor DROPPED the IEncryptionKeyProvider
        keyProvider parameter entirely — SOURCE-VERIFIED redundant with ISymmetricEncryptionService.
        Decrypt's own Result<byte[]> failure surface, which already distinguishes
        CryptographyErrorCodes.UnknownKeyId (unresolvable key) from CryptographyErrorCodes.
        DecryptionFailed (tamper/wrong-key) — exactly the distinction the removed pre-check used to
        preserve. Decrypt now calls symmetricEncryptionService.Decrypt directly and maps
        result.Error?.Code == CryptographyErrorCodes.UnknownKeyId → EncryptionKeyNotFoundException
        (version); any other failure → the existing generic CryptographicException. Observable
        exception behavior is IDENTICAL to before P-448 — only the code path producing it changed.
        This means the EF Core ValueConverter pipeline NEVER touches IEncryptionKeyProvider — sync or
        async — directly again, which is how 01.Core's own P-446 design (async IEncryptionKeyProvider,
        GetCurrentKeyAsync/GetKeyAsync) stays solvable here WITHOUT blocking on async anywhere in this
        package: ISymmetricEncryptionService's retained-synchronous Encrypt/Decrypt members bridge
        onto the async IEncryptionKeyProvider internally (01.Core's own documented
        .GetAwaiter().GetResult() cost — genuinely non-blocking only when the registered provider
        resolves synchronously, e.g. this domain's config-based EncryptionOptionsKeyProvider; it
        WOULD block a real thread on a cache miss against a genuinely network-bound provider such as
        a future SharedKernel.Cryptography.KeyVault.Azure). EncryptionModelConvention's constructor
        correspondingly dropped its own IEncryptionKeyProvider? parameter and resolution (D-109). See
        "What Goes Where" / state-map.md D-108.
    NOTE: Non-generic — operates on string columns only.
          CRYPTOGRAPHY DELEGATION (P-227, supersedes the original WO-019 hand-rolled AesGcm design):
          all AES-256-GCM cryptographic operations (nonce generation, encrypt, tag-append, decrypt,
          tag verification) are delegated to SharedKernel.Cryptography.ISymmetricEncryptionService —
          zero direct AesGcm/RandomNumberGenerator calls remain in this converter or anywhere in
          SharedKernel.Persistence.EfCore. SharedKernel.Persistence.EfCore takes a direct
          ProjectReference to 01.Core/SharedKernel.Cryptography (legal: 06 may reference 01–05, no
          layering exception needed). This converter retains exclusive ownership of the EF Core
          column WIRE FORMAT — ISymmetricEncryptionService knows nothing about column storage strings.
          Ciphertext format (UNCHANGED, byte-for-byte, since WO-019): "v{version}:{Base64(nonce ||
          ciphertext || 16-byte auth-tag)}". The version prefix is mandatory — it identifies the
          decryption key.
          Encrypt path: calls symmetricEncryptionService.Encrypt(plaintextBytes) → receives an
          EncryptedPayload { KeyId, Nonce, Ciphertext, Tag } → this converter packs
          "v{KeyId}:{Base64(Nonce||Ciphertext||Tag)}" itself. KeyId is the EncryptionOptions version
          string (e.g. "v1") — no translation layer, because EncryptionOptionsKeyProvider (below)
          uses EncryptionOptions.Keys' dictionary keys directly as CryptographicKey.Id/KeyId.
          Decrypt path (SHIPPED shape per D-108/P-448): parses the version prefix and the packed
          nonce||ciphertext||tag blob from the stored string, reconstructs an EncryptedPayload
          { KeyId = parsedVersion, Nonce, Ciphertext, Tag }, and calls
          symmetricEncryptionService.Decrypt(payload) UNCONDITIONALLY → Result<byte[]> — there is no
          more direct pre-check against IEncryptionKeyProvider.GetKey/GetKeyAsync. A Result.Failure
          is mapped by ERROR CODE: result.Error?.Code == CryptographyErrorCodes.UnknownKeyId (the
          stored version is absent from EncryptionOptions.Keys) → EncryptionKeyNotFoundException
          (parsedVersion); any other failure (tamper / auth-tag mismatch / wrong key,
          CryptographyErrorCodes.DecryptionFailed) → the existing generic CryptographicException.
          This is a pure code-path change from the pre-P-448 design — the observable exception
          behavior for both cases is IDENTICAL to before; only what produces it changed (a
          pre-check dictionary lookup vs. a Result<T> error-code branch after the real Decrypt call).
          Legacy plaintext (no "v" prefix): returned as-is — safe migration path from unencrypted data.
          Enabled == false: pass-through in both directions, no ISymmetricEncryptionService calls made.
          Holds IOptionsMonitor<EncryptionOptions> — hot-reload of CurrentVersion and key changes
          takes effect on the next read/write without a service restart (delegated to
          EncryptionOptionsKeyProvider, which reads optionsMonitor.CurrentValue on every call).
          Target encrypt version resolution (P-147, relocated by P-227): resolved inside
          EncryptionOptionsKeyProvider.GetCurrentKeyAsync() as versionOverride.OverrideVersion ??
          options.CurrentVersion — see IEncryptionVersionOverride below for the rotation-scoped
          override seam; the precedence rule itself is unchanged from P-147. The converter calls
          this indirectly, via symmetricEncryptionService.Encrypt(...)'s internal key resolution —
          it never calls EncryptionOptionsKeyProvider directly itself (D-108/D-109).
          Do NOT instantiate directly in IEntityTypeConfiguration — use .Encrypt() extension (SK0304).
          SUPERSEDED (P-498/WO-081, D-131, shipped): ISymmetricEncryptionService is no longer resolved
          from the ambient/unkeyed DI slot — EfCorePersistenceBuilder.WithEncryption() constructs its
          own persistence-scoped AesGcmEncryptionService instance directly and registers it under a
          package-internal keyed-DI slot (PersistenceEncryptionKeys.SymmetricEncryptionServiceKey),
          resolved automatically by SharedKernelDbContext's constructor via
          CoreOptionsExtension.ApplicationServiceProvider. A genuinely nice side effect: the
          config-backed default path is now FULLY SELF-CONTAINED — the consuming service no longer
          needs to call 01.Core's AddSharedKernelCryptography() at all for field-level encryption to
          work. EfCorePersistenceBuilder.WithEncryption() performs an eager startup check
          (EncryptionStartupValidator, D-132) that a genuinely-synchronous IEncryptionKeyProvider is
          resolvable under the keyed slot — see "EfCorePersistenceBuilder encryption wiring" below.

EncryptionOptionsKeyProvider  (internal sealed class, implements
                                SharedKernel.Cryptography.ISynchronousEncryptionKeyProvider — SHIPPED)
    SHIPPED (P-498/WO-081, D-127): implements ", SharedKernel.Cryptography.ISynchronousEncryptionKeyProvider"
        (which itself extends IEncryptionKeyProvider) — a zero-member marker, an HONEST claim since this
        type is SOURCE-VERIFIED zero-I/O already (reads an already-loaded IOptionsMonitor.CurrentValue
        plus the already-synchronous EncryptionKeyByteCache).
        THE SINGLE MOST LOAD-BEARING TASK IN P-498: without it, 01.Core's P-492 gate would make every
        EXISTING config-backed .WithEncryption() user's sync Encrypt/Decrypt calls throw
        NotSupportedException the moment it is picked up. NullEncryptionKeyProvider gains the same marker
        for the same reason (it throws synchronously, before constructing any ValueTask — never blocks).
    constructor (CURRENT): EncryptionOptionsKeyProvider(IOptionsMonitor<EncryptionOptions> optionsMonitor,
                                               IEncryptionVersionOverride versionOverride,
                                               EncryptionKeyByteCache keyByteCache)
    .GetCurrentKeyAsync(CancellationToken ct = default)         → ValueTask<CryptographicKey>   (SHIPPED, D-110)
    .GetKeyAsync(string keyId, CancellationToken ct = default)  → ValueTask<CryptographicKey?>   (SHIPPED, D-110)
    SHIPPED (P-448/WO-068, D-110, breaking): migrated from the old sync GetCurrentKey()/GetKey(string)
      shape to the async pair above, per 01.Core's P-446 async IEncryptionKeyProvider contract.
      SOURCE-VERIFIED this implementation performs ZERO genuine I/O (reads an already-loaded
      IOptionsMonitor.CurrentValue plus the already-synchronous EncryptionKeyByteCache decode-once
      cache — pure in-memory/CPU work), so both members return an ALREADY-COMPLETED
      ValueTask<T> — `new ValueTask<T>(value)`, IsCompletedSuccessfully == true — never a genuinely
      suspended one. UNLIKE a genuinely network-bound provider (e.g. a future
      SharedKernel.Cryptography.KeyVault.Azure, P-447), AesGcmEncryptionService's internal
      .GetAwaiter().GetResult() bridge (01.Core's own documented cost) NEVER actually blocks a
      thread on real I/O when THIS config-based provider is registered — it would block a real
      thread only against a genuinely network-bound provider on a cache miss.
      NullEncryptionKeyProvider.GetCurrentKeyAsync still throws its InvalidOperationException
      SYNCHRONOUSLY, before constructing any ValueTask (not via a faulted one) — behavior-preserving
      relative to the old sync GetCurrentKey(). NullEncryptionKeyProvider.GetKeyAsync still returns
      an already-completed null result, unchanged.
    NOTE: P-227. Bridges EncryptionOptions (this domain's existing options POCO) to
          SharedKernel.Cryptography.IEncryptionKeyProvider (the seam ISymmetricEncryptionService
          requires for key resolution). This is the ONLY IEncryptionKeyProvider implementation this
          domain ships — it is registered scoped, specifically as the key provider backing
          EncryptedValueConverter's injected ISymmetricEncryptionService for the persistence layer.
          As of D-108/P-448, EncryptedValueConverter/EncryptionModelConvention never call this
          provider directly — only ISymmetricEncryptionService's own AesGcmEncryptionService
          implementation resolves it, from DI, internally.
          GetCurrentKeyAsync(): resolves the target version as
          versionOverride.OverrideVersion ?? optionsMonitor.CurrentValue.CurrentVersion (identical
          precedence to the pre-P-227 design), then resolves the decoded key bytes via
          EncryptionKeyByteCache.GetOrDecode(version, optionsMonitor.CurrentValue.Keys[version])
          (WO-051/P-323 — see below; previously called Convert.FromBase64String directly on every
          call), returns an already-completed ValueTask wrapping new CryptographicKey(version,
          decodedBytes). This is how the existing rotation-scoped IEncryptionVersionOverride seam
          (P-147) continues to direct which key a rotation batch encrypts with.
          GetKeyAsync(keyId, ct): looks up optionsMonitor.CurrentValue.Keys[keyId], resolves decoded
          bytes via the same EncryptionKeyByteCache.GetOrDecode — deliberately IGNORES
          versionOverride, because decryption always targets the exact KeyId recorded in the stored
          ciphertext's version prefix, never the current/override version. Returns an
          already-completed ValueTask wrapping null (never throws) when keyId is absent from Keys,
          per IEncryptionKeyProvider's documented contract.
          Registered TWICE by EfCorePersistenceBuilder.WithEncryption() (SHIPPED, D-131): once as
          ITSELF (the concrete type, scoped, unkeyed — safe, since it is internal and nothing else
          could ever target it), and once as the KEYED SharedKernel.Cryptography.IEncryptionKeyProvider
          under PersistenceEncryptionKeys.EncryptionKeyProviderKey, resolving to the same instance.
          NEVER registered as the unkeyed IEncryptionKeyProvider anymore — a consuming service that
          separately registers its own general-purpose IEncryptionKeyProvider (unkeyed, e.g. via its
          own AddSharedKernelCryptography() call) has ZERO effect on this persistence-scoped instance,
          and vice versa; the two are structurally isolated by the keyed slot, not merely
          "last-registration-wins by convention."
          Reads optionsMonitor.CurrentValue fresh on every call — never a captured snapshot — so
          hot-reload of EncryptionOptions.Keys/CurrentVersion is preserved exactly as before P-227.

EncryptionKeyByteCache  (internal sealed class)  (WO-051/P-323)
    .GetOrDecode(string version, string base64Value)            → byte[]
    NOTE: Caches decoded key bytes per version in a ConcurrentDictionary<string,byte[]>, so the
          common no-rotation-in-flight case Base64-decodes each key version at most ONCE per
          underlying EncryptionOptions value, rather than on every encrypted-column row read/write.
          Registered as a SINGLETON — deliberately NOT scoped like EncryptionOptionsKeyProvider
          itself, since decoded key bytes vary only with the config VALUE, not per request; a
          cross-scope cache is strictly more efficient than a per-scope one here.
          Subscribes ONCE, at singleton-construction time, to
          IOptionsMonitor<EncryptionOptions>.OnChange(_ => cache.Clear()) — a coarse, whole-cache
          invalidation on ANY EncryptionOptions change (config reload, a rotation adding a new key
          version, etc.). No per-scope subscription is ever created, so there is no subscription-leak
          risk from this cache's lifetime being singleton. Registered by
          EfCorePersistenceBuilder.WithEncryption() alongside EncryptionOptionsKeyProvider.

IEncryptionVersionOverride  (interface) / EncryptionVersionOverride  (sealed class — default impl)
    .OverrideVersion  (string?, mutable)
    NOTE: Scoped accessor (P-147) allowing EncryptionRotationService to direct a single batch's
          EncryptedValueConverter instances to encrypt with toVersion, without mutating
          EncryptionOptions.CurrentVersion. Registered as scoped by .WithEncryption(); a shared
          no-op instance (OverrideVersion always null) is used when not registered.
          EncryptionModelConvention resolves this from DI and passes it to every converter it constructs.
          Concurrent unrelated scoped DbContext instances are unaffected — each resolves its own
          scoped IEncryptionVersionOverride, defaulting to null (i.e., CurrentVersion).

.Encrypt(bool? enabled = true, string? associatedDataOverride = null)  (extension method on PropertyBuilder<T>)
    NOTE: Writes annotation "SharedKernel:Encrypt" = true/false on the property.
          This is the ONLY permitted way to mark a property for encryption.
          Called inside IEntityTypeConfiguration<TEntity>.Configure(builder):
              builder.Property(x => x.Email).HasMaxLength(255).Encrypt().IsRequired();
          Passing false explicitly opts the property out even if future bulk-annotation approaches are added.
          The method returns the builder for fluent chaining.
    SHIPPED (P-498/WO-081, D-128, additive/non-breaking — an optional parameter, existing
          callers unaffected): associatedDataOverride, when supplied, writes a second annotation
          ("SharedKernel:EncryptAssociatedDataOverride" — PropertyBuilderEncryptExtensions.
          AssociatedDataOverrideAnnotationKey) that EncryptionModelConvention uses AS the property's
          associated data (UTF8-encoded) INSTEAD OF the derived "{schema}.{table}.{column}" default —
          a stable, caller-chosen identity that survives a physical table/column rename. Omitting it
          preserves the derived-from-storage-identity default exactly.

EncryptionModelConvention  (sealed class, implements IModelFinalizingConvention)
    constructor (CURRENT, shipped 2026-09-03 per D-109/P-448, BREAKING vs. the pre-P-448 shape — the
                 IEncryptionKeyProvider? keyProvider parameter was DROPPED entirely):
        EncryptionModelConvention(IOptionsMonitor<EncryptionOptions> optionsMonitor,
                                   ISymmetricEncryptionService? symmetricEncryptionService = null,
                                   IEncryptionVersionOverride? versionOverride = null)
    NOTE: Runs at model-finalization time (after all IEntityTypeConfiguration implementations).
          Scans all entity type properties for the "SharedKernel:Encrypt" annotation.
          Applies EncryptedValueConverter to each annotated property where annotation == true,
          passing the resolved IEncryptionVersionOverride (or shared no-op instance) to each converter.
          Registered automatically in SharedKernelDbContext.OnModelCreating — no manual call needed.
          Behavior is gated by EncryptionOptions.Enabled inside the converter, not the convention —
          the convention always wires the converter; Enabled == false makes the converter a pass-through.
          SHIPPED (D-109/P-448): construction now branches on `_symmetricEncryptionService is not
          null` alone — the pre-P-448 `&& _keyProvider is not null` half of the condition is gone,
          since EncryptedValueConverter stopped needing a keyProvider argument at all (D-108).

EncryptionKeyNotFoundException  (sealed class, extends SharedKernelException)
    NOTE: Thrown by EncryptedValueConverter when the version prefix in stored ciphertext
          is not found in EncryptionOptions.Keys. Carries the unknown version string.
          Indicates a key was removed from options before all rows using it were rotated.

IEncryptionRotationJob  (interface)
    .RotateAsync(string fromVersion, string toVersion, CancellationToken ct) → Task<EncryptionRotationResult>
    NOTE: Abstraction for triggering key rotation. Registered only when .WithEncryption() is called.
          Trigger via Hangfire job, Temporal activity, hosted service, or management endpoint.
          Must NOT be injected in MediatR handlers, domain services, or any 03.Domain/05.Application type (SK0303).

EncryptionRotationResult  (record)
    .RowsProcessed   (int)
    .RowsRotated     (int)
    .RowsFailed      (int)
    .Errors          (IReadOnlyList<string>)

IEncryptedEntityBatchProcessor  (interface, internal — Encryption/Rotation/)
    .LoadBatchAsync(DbContext context, int skip, int take, CancellationToken ct) → Task<List<object>>
    NOTE: P-147 corrected design (see "EF Core 10 API correction" below). One closed generic
          implementation per encrypted entity type, registered at startup — NOT resolved via
          runtime MakeGenericMethod/Invoke.

EncryptedEntityBatchProcessor<TEntity>  (sealed class, implements IEncryptedEntityBatchProcessor)
    where TEntity : class
    .LoadBatchAsync(DbContext context, int skip, int take, CancellationToken ct) → Task<List<object>>
    NOTE: Body: `(await context.Set<TEntity>().Skip(skip).Take(take).ToListAsync(ct)).Cast<object>().ToList()`.
          context.Set<TEntity>() is the ordinary generic DbSet accessor — a direct generic method call,
          not a reflection invocation. Returns List<object> so the caller (EncryptionRotationService)
          can iterate with context.Entry(entity) regardless of CLR type.

EncryptionRotationService  (abstract class, implements IEncryptionRotationJob)
    NOTE: Uses IDbContextFactory<TContext> to open fresh contexts per batch (default batch size 500).
          Enumerates all entity types in the EF model that have encrypted properties (via
          context.Model.GetEntityTypes() and the "SharedKernel:Encrypt" annotation — model metadata
          access, not reflection over CLR members).
          LoadBatchAsync (P-147, CORRECTED): for each discovered entity type's ClrType, looks up the
          matching IEncryptedEntityBatchProcessor from a registry populated at startup (see
          EncryptedEntityBatchProcessorRegistry below) and calls its LoadBatchAsync. No
          GetMethod/MakeGenericMethod/Invoke anywhere in the rotation hot path or in registry lookup —
          the registry is a Dictionary<Type, IEncryptedEntityBatchProcessor> keyed by ClrType, populated
          via closed-generic constructor calls made at registration time (compile-time known types).
          For each batch: loads rows, checks whether stored value starts with "v{fromVersion}:";
          resolves the batch context's scoped IEncryptionVersionOverride and sets OverrideVersion = toVersion
          for the duration of SaveChangesAsync (resetting to null afterward) so EncryptedValueConverter
          re-encrypts with toVersion's key without mutating EncryptionOptions.CurrentVersion.
          Rotation is idempotent — rows already at toVersion are skipped.
          Keys read from IOptionsMonitor<EncryptionOptions> at RotateAsync call time.
          Subclasses supply TContext; may override batch size or pre/post-batch hooks.

EncryptedEntityBatchProcessorRegistry  (sealed class — Encryption/Rotation/)
    .TryGet(Type clrType, out IEncryptedEntityBatchProcessor? processor) → bool
    NOTE: P-147. Backs LoadBatchAsync's per-entity-type dispatch. Populated by
          EfCorePersistenceBuilder.WithEncryption() at DI-registration time: the builder iterates
          context.Model.GetEntityTypes() for entity types carrying the "SharedKernel:Encrypt"
          annotation on at least one property (the SAME discovery query EncryptionRotationService
          uses), and for each such ClrType constructs
          `new EncryptedEntityBatchProcessor<TEntity>()` via a small closed-generic helper method
          `CreateProcessor<TEntity>() => new EncryptedEntityBatchProcessor<TEntity>()` invoked through
          a switch/dispatch built from the model's entity CLR types at startup — a one-time,
          model-build-time operation, not a runtime hot-path reflection call.
          Because the set of entity types is enumerable from context.Model (not arbitrary user input),
          and EncryptedEntityBatchProcessor<TEntity> has a parameterless constructor, the registry can
          alternatively be populated via `Activator.CreateInstance(typeof(EncryptedEntityBatchProcessor<>)
          .MakeGenericType(clrType))` ONCE per entity type at startup — this is the documented,
          justified, model-build-time exception to the SK0xxx MakeGenericMethod/Invoke rule (same class
          of exception as ValueObjectOwnershipBuilder's startup-time model scan). Registered as a
          singleton; immutable after construction.
          WORDING RECONCILIATION (WO-051/P-324): `EfCorePersistenceExtensions.cs`'s own registration
          comment previously called this registry "reflection-free" — technically imprecise in
          isolation, since population DOES use the documented, justified, one-time, startup-only
          `Activator.CreateInstance`/`MakeGenericType` exception described above. The comment has been
          reworded to match THIS type's own accurate framing ("a justified, model-build-time exception
          to the reflection-elimination rule") rather than claiming zero reflection ever occurs. No
          runtime behavior changed — this is a documentation-only correction.

PreWarmedEncryptionKeyProvider  (internal sealed class — Encryption/, SHIPPED P-498/WO-081, D-129)
    implements SharedKernel.Cryptography.ISynchronousEncryptionKeyProvider (which itself extends
    IEncryptionKeyProvider)
    constructor: PreWarmedEncryptionKeyProvider(IEncryptionKeyProvider inner,
                                                  IEncryptionVersionOverride versionOverride)
    .GetCurrentKeyAsync(CancellationToken ct = default)         → ValueTask<CryptographicKey>
    .GetKeyAsync(string keyId, CancellationToken ct = default)  → ValueTask<CryptographicKey?>
    NOTE: The 06.Persistence-owned provider that makes external/KMS-backed column encryption viable at
          all under 01.Core's P-492 gate — wraps a consumer-supplied IEncryptionKeyProvider (e.g.
          AzureKeyVaultEncryptionKeyProvider, optionally already CachedEncryptionKeyProvider-wrapped —
          irrelevant to this type, which only ever calls the wrapped instance's ASYNC members, and only
          from its own async warm methods below). Backed by a ConcurrentDictionary<string,byte[]>
          (_warmCache) + a volatile _currentVersionTag.
          GetCurrentKeyAsync/GetKeyAsync serve EXCLUSIVELY from _warmCache — NEVER touch inner. This is
          what makes the ISynchronousEncryptionKeyProvider claim HONEST (P-492's "author-asserted, never
          inferred" rule): the never-blocks guarantee is enforced BY CONSTRUCTION, not by cache-hit
          probability. GetCurrentKeyAsync resolves the target tag as
          versionOverride.OverrideVersion ?? _currentVersionTag; if that tag is unwarmed (including
          _currentVersionTag itself being null, i.e. never warmed at all), throws
          InvalidOperationException SYNCHRONOUSLY, before constructing any ValueTask — mirrors
          NullEncryptionKeyProvider's existing synchronous-throw precedent. GetKeyAsync on a cache MISS
          returns an already-completed ValueTask wrapping null (never throws), per
          IEncryptionKeyProvider's documented contract — an unwarmed historical key version surfaces
          EXACTLY like today's unknown/removed-key case, flowing through the existing
          EncryptionKeyNotFoundException path unchanged.
          WarmCurrentAsync(CancellationToken ct) → ValueTask  (internal — the ONLY method permitted to
          call inner): awaits inner.GetCurrentKeyAsync(ct), populates _warmCache[key.Id] = key.Material,
          sets _currentVersionTag = key.Id; no-op if already warm for the resolved tag.
          WarmVersionAsync(string keyId, CancellationToken ct) → ValueTask  (internal): awaits
          inner.GetKeyAsync(keyId, ct), populates _warmCache[keyId] if found; no-op if already warm.
          Called EXCLUSIVELY from EncryptionKeyPreWarmingInterceptor's two async hooks and from
          .WithExternalEncryptionKeyProvider<TProvider>()'s one-time startup warm-up gate — never from
          anywhere on the synchronous ValueConverter pipeline.

EncryptionKeyPreWarmingInterceptor  (internal sealed class — Interceptors/, SHIPPED P-498/WO-081, D-130)
    extends SaveChangesInterceptor, implements IDbCommandInterceptor
    constructor: EncryptionKeyPreWarmingInterceptor(PreWarmedEncryptionKeyProvider provider)
    NOTE: CORRECTED against the original design sketch — a single-parameter constructor; no separate
          IEncryptionVersionOverride parameter is needed because provider.WarmCurrentAsync() already
          resolves the override internally.
    NOTE: A FIFTH interceptor (see "Interceptors" below) — registered ONLY by
          .WithExternalEncryptionKeyProvider<TProvider>(), never present in the default config-backed
          path (where PreWarmedEncryptionKeyProvider is never constructed at all).
          SavingChangesAsync(...): if ChangeTracker.Entries() contains ≥1 Added/Modified entry whose
          entity type carries the "SharedKernel:Encrypt" annotation on ≥1 property (the SAME
          model-metadata scan EncryptionRotationService/EncryptedEntityBatchProcessorRegistry already
          use — zero reflection over CLR members), awaits provider.WarmCurrentAsync(ct) BEFORE calling
          base.SavingChangesAsync(...) — genuinely runs before EF Core builds the command batches that
          invoke ConvertToProviderExpression (the sync Encrypt call site). Closes the WRITE half of the
          F1 defect.
          ReaderExecutingAsync(...): if eventData.Context's model carries the "SharedKernel:Encrypt"
          annotation on ≥1 property anywhere (checked once per DbContext CLR type, cached — a coarse,
          whole-model gate, not per-query), awaits provider.WarmCurrentAsync(ct) BEFORE calling
          base.ReaderExecutingAsync(...) — EF Core's genuine ASYNC pre-materialization extension point,
          firing before ExecuteReaderAsync returns a DbDataReader, strictly before any row's
          ConvertFromProviderExpression/sync Decrypt call. THIS is the hook that actually closes the F1
          defect's literal scenario ("every READ... 100 rows means 200 blocking Key Vault calls") — a
          write-only SavingChangesAsync hook alone cannot, since a query never calls SaveChangesAsync
          (see D-126's refutation record). Coarse-grained by design: warms whenever the model has ANY
          encrypted property, regardless of whether THIS query touches one — acceptable, since a warm
          no-op call is an O(1) dictionary check inside PreWarmedEncryptionKeyProvider, never a KMS round
          trip.

EncryptionKeyPreWarmingHostedService  (internal sealed class — Encryption/, SHIPPED P-498/WO-081, D-133)
    implements IHostedService
    constructor: EncryptionKeyPreWarmingHostedService(PreWarmedEncryptionKeyProvider provider)
    NOTE: StartAsync awaits provider.WarmCurrentAsync(ct) — a ONE-TIME, boot-time-only blocking wait,
          mirroring MigrationAndSeedHostedService's existing "block readiness until done" shape
          (WithMigrationsOnStartup()). StopAsync is a no-op. Registered automatically by
          .WithExternalEncryptionKeyProvider<TProvider>() — never by the config-backed default path.
          Guarantees PreWarmedEncryptionKeyProvider's synchronous-throw-if-unwarmed path
          (GetCurrentKeyAsync before any warm call) is reachable only under a genuine startup-ordering
          bug, never in normal operation.

PersistenceEncryptionKeys  (internal static class — Encryption/, SHIPPED P-498/WO-081, D-131)
    .EncryptionKeyProviderKey       (internal const string)
    .SymmetricEncryptionServiceKey  (internal const string)
    NOTE: The two package-internal keyed-DI service keys this package's own persistence-scoped
          encryption pipeline is registered under — NEVER exposed publicly, NEVER a cross-domain wire
          contract (no 01.Core WellKnownX-style registry entry needed). EfCorePersistenceBuilder.
          WithEncryption() registers the config-backed default under both keys;
          .WithExternalEncryptionKeyProvider<TProvider>() re-registers BOTH keys pointing through
          PreWarmedEncryptionKeyProvider instead — the .NET DI container resolves the LAST registration
          for a given (service type, key) pair, so the external-provider registration always wins once
          both are present, by design (last-registered-wins).
```

### EF Core 10 API correction (P-147, superseding original WO-024 spec text)

The original WO-024 spec for D-54 Issue 1 referenced a non-generic `DbContext.Set(Type entityType)`
overload. **This overload does not exist in EF Core 10.0.5** — `DbContext.Set` only has
`Set<TEntity>()` and `Set<TEntity>(string name)` generic overloads. Likewise,
`EntityFrameworkQueryableExtensions.ToListAsync` has no non-generic `IQueryable` overload — only
`ToListAsync<TSource>(IQueryable<TSource>, CancellationToken)`. The corrected design above
(`IEncryptedEntityBatchProcessor` / `EncryptedEntityBatchProcessor<TEntity>` /
`EncryptedEntityBatchProcessorRegistry`) achieves the same outcome — a reflection-free hot path —
using closed-generic types resolved once at startup instead of per-call `MakeGenericMethod`/`Invoke`.
This correction must be reflected in C-82/C-83 (Core phase) and T-47 (Tests phase) when implemented.

**EfCorePersistenceBuilder encryption wiring (WO-019):**

```text
.WithEncryption(Action<EncryptionOptions>? configure = null)
    — Registers EncryptionOptions via the Options system with the supplied configuration action.
    — Registers eager startup validation for EncryptionOptions.
    — SHIPPED (P-498/WO-081, D-131): registers EncryptionOptionsKeyProvider as ITSELF (scoped, unkeyed —
      safe, internal type nothing else could target), then constructs its OWN persistence-scoped
      `AesGcmEncryptionService` directly (mirroring `01.Core`'s own `AddSharedKernelCryptography()`
      wiring — same concrete type, substituting only the `IEncryptionKeyProvider` argument; NOT resolved
      as a registered DI service, a plain `new AesGcmEncryptionService(...)` call inside a factory
      delegate), registered under TWO package-internal keyed-DI slots
      (`PersistenceEncryptionKeys.EncryptionKeyProviderKey`/`.SymmetricEncryptionServiceKey`) —
      `EncryptionModelConvention` resolves the keyed instance exclusively, via
      `SharedKernelDbContext`'s constructor (see below). NEVER touches the ambient unkeyed
      `IEncryptionKeyProvider`/`ISymmetricEncryptionService` slot, in EITHER mode (config-backed or
      `.WithExternalEncryptionKeyProvider<TProvider>()`).
      **ROOT-CAUSE FIX, not a documentation-only change:** the PRE-P-498 unkeyed registration collided,
      by DI registration ORDER, with any OTHER `IEncryptionKeyProvider`/`ISymmetricEncryptionService`
      registration elsewhere in the same container — e.g. `13.ServiceDefaults`'s
      `AddSharedKernelKeyVaultKeyProvider` (P-449) registering `AzureKeyVaultEncryptionKeyProvider` —
      this WAS the SEVERE F1 defect's actual root cause, not merely a symptom of it. `EncryptedValueConverter`'s
      public constructor shape is unaffected by the DI-wiring change — direct construction/resolution of
      that argument was already forbidden (SK0304).
      GENUINELY NICE SIDE EFFECT: because this package now constructs `AesGcmEncryptionService` itself
      rather than DI-resolving it, the config-backed default path no longer requires the consuming
      service to call `01.Core`'s `AddSharedKernelCryptography()` at all — fully self-contained.
    — SHIPPED (P-498/WO-081, D-132): registers an eager startup check (`EncryptionStartupValidator`)
      that resolves `IEncryptionKeyProvider` under `PersistenceEncryptionKeys.EncryptionKeyProviderKey`;
      fails if unresolvable (unreachable through the builder itself — defense against a maintainer
      bypassing it entirely), and fails if
      `EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(...)` against whatever is found there
      returns false — defense-in-depth; D-127/D-129 make this unreachable-false on every path this
      package itself constructs. (CORRECTED against the original design sketch, which proposed checking
      "individual dependencies like `ISecureRandomGenerator`" — source-verified wrong:
      `AesGcmEncryptionService`'s constructor takes only `IEncryptionKeyProvider`, no other dependency
      exists to check.)
    — Sets flag: .Build() registers IEncryptionRotationJob → EncryptionRotationService (scoped).
    — Optional. Omitting leaves all existing behavior unchanged (Enabled == false by default).

.WithExternalEncryptionKeyProvider<TProvider>() where TProvider : class, IEncryptionKeyProvider
    (SHIPPED, P-498/WO-081, D-131)
    — MUST be called after .WithEncryption(...) — throws InvalidOperationException at .Build() time
      otherwise, mirroring the existing pooling+encryption/pooling+factory ordering guards.
    — Resolves TProvider from the AMBIENT (unkeyed) container — e.g. a KMS-backed provider such as
      01.Core's AzureKeyVaultEncryptionKeyProvider that a consuming service separately registers for its
      OWN general-purpose crypto needs, optionally already CachedEncryptionKeyProvider-wrapped (does not
      matter to this method — only the provider's ASYNC members are ever called).
    — Wraps it in a new PreWarmedEncryptionKeyProvider (D-129), registers THAT as the keyed
      IEncryptionKeyProvider backing this package's own keyed ISymmetricEncryptionService instance
      (see .WithEncryption()'s SUPERSEDED note above).
    — Registers EncryptionKeyPreWarmingInterceptor (D-130) on SharedKernelDbContext.
    — Registers a blocking, ONE-TIME, boot-only startup warm-up (D-133) — an IHostedService gating host
      readiness until PreWarmedEncryptionKeyProvider's current-version cache is populated, mirroring
      WithMigrationsOnStartup()'s existing "block readiness until done" shape.
    — This is the SANCTIONED way to point column-level encryption at a KMS/HSM-backed provider. A
      hand-rolled IEncryptionKeyProvider registered any other way is caught by the D-132 startup guard
      above, not silently accepted.
    — NON-GOAL (D-134): IEncryptionRotationJob/EncryptionRotationService remains config-backed-only
      (EncryptionOptions.Keys-driven) in this mode — KMS-backed rotation orchestration is explicitly out
      of scope for this phase; an unwarmed historical version during rotation surfaces as the existing
      EncryptionKeyNotFoundException per-row failure path, not a crash or a block.

.WithEncryption(IConfiguration configuration, Action<EncryptionOptions>? configure = null)   (WO-053/P-334)
    — Genuine config-binding overload, closing a confirmed doc/code mismatch (EncryptionOptions' own XML
      doc long claimed a bound section that no code ever read).
    — Binds via services.AddOptions<EncryptionOptions>().Bind(configuration.GetSection(
      EncryptionOptions.SectionName)) — never a bare GetSection("...") literal.
    — When configure is supplied, chains the SAME .Configure(configure) registration the code-only
      overload performs — normal IOptions<T> later-registration-wins semantics apply.
    — The eager startup validation registration is idempotent across repeated .WithEncryption(...) calls
      in the same builder chain (never duplicated).
    — Optional. The pre-existing Action<T>-only overload is completely unchanged and remains fully
      supported; both may be composed.

.WithServiceName(IConfiguration configuration)   (WO-053/P-334)
    — Genuine config-binding overload, closing the same class of doc/code mismatch as above.
    — Binds via services.AddOptions<PersistenceServiceOptions>().Bind(configuration.GetSection(
      PersistenceServiceOptions.SectionName)).
    — Composes with the pre-existing WithServiceName(string) overload (completely unchanged) under
      the identical later-call-wins semantics.
    — Existing startup validation (non-null/non-empty/≤256 chars) fires against the bound value exactly
      as it already fires against a code-supplied one.

.WithCommandTimeout(int commandTimeoutSeconds)   (WO-053/P-337)
    — Wraps the caller-supplied configureDb action to also call
      optionsBuilder.CommandTimeout(commandTimeoutSeconds) — the same wrapping pattern
      .WithCompiledModel(IModel) already uses for .UseModel(compiledModel).
    — DbContextOptionsBuilder.CommandTimeout(int?) is a Microsoft.EntityFrameworkCore.Relational
      extension — PROVIDER-NEUTRAL, already reachable from this package's existing
      Microsoft.EntityFrameworkCore.Relational reference. Deliberately NOT placed on
      UsePostgreSQL(...) (SharedKernel.Persistence.PostgreSQL) — unlike EnableRetryOnFailure (P-320),
      CommandTimeout has no Npgsql-only dependency to justify that placement.
    — No companion DI-registered options POCO — nothing else in this domain needs to query the
      configured timeout value at runtime the way EfTransactionalUnitOfWork queries
      Database.CreateExecutionStrategy().RetriesOnFailure.
    — Optional. Omitting preserves today's provider-default command timeout exactly.

.WithReadReplica(Action<DbContextOptionsBuilder> configureReplicaDb)   (WO-053/P-338)
    — Registers a KEYED singleton DbContextOptions<TContext> built from a fresh, independent
      DbContextOptionsBuilder<TContext>().Options after applying configureReplicaDb — the caller's own
      lambda supplies the Npgsql-specific replica connection string (e.g. optionsBuilder =>
      optionsBuilder.UseNpgsql(replicaConnectionString)), mirroring AddSharedKernelEfCore<TContext>'s
      own Action<DbContextOptionsBuilder> configureDb pattern exactly — EfCorePersistenceBuilder itself
      never references Npgsql.
    — Registers a SCOPED IReadReplicaContextAccessor<TContext> (see "Read-Replica Routing" below) whose
      implementation lazily constructs the replica TContext instance (once per DI scope, cached
      thereafter) via ActivatorUtilities.CreateInstance<TContext>(serviceProvider, replicaOptions),
      deliberately REUSING the same scope-ambient, DI-resolved AuditInterceptor/SoftDeleteInterceptor/
      ConcurrencyInterceptor instances (and their live CurrentUserContext, WO-051/P-322) the primary
      TContext for this scope already resolved — audit-field consistency between primary and replica
      reads is automatic.
    — Optional. Omitting leaves IReadReplicaContextAccessor<TContext> unregistered entirely — every
      IReadRepository operation continues to target the single primary connection, provably unchanged.

.WithApplicationTransactionBehavior()
    — Opt-in (P-228). Registers the same scoped EfUnitOfWork instance against
      SharedKernel.Application.Behaviors.IUnitOfWork in addition to its existing registration against
      SharedKernel.Persistence.Abstractions.IUnitOfWork — both registrations resolve the SAME scoped
      EfUnitOfWork instance per DI scope (not two independent instances).
      Enables 05.Application.Behaviors' TransactionBehavior to resolve a working unit-of-work without
      a hand-written composition-root adapter.
    — Optional. Omitting leaves SharedKernel.Application.Behaviors.IUnitOfWork unregistered — services
      not using TransactionBehavior, or bridging a non-EF-Core IUnitOfWork implementation, are unaffected.

.WithServiceName(string serviceName)
    — Registers PersistenceServiceOptions with the given ServiceName.
    — AuditInterceptor reads ServiceName as the unauthenticated audit fallback.
    — Optional. Omitting keeps the default "system" fallback.
```

**SharedKernelDbContext constructor update (WO-019):**
`SharedKernelDbContext` gains an additional optional constructor parameter `IOptionsMonitor<EncryptionOptions>? encryptionOptions`. When provided, `EncryptionModelConvention` receives the monitor. When not provided (or when `.WithEncryption()` is not called), the convention defaults to `Enabled = false` and all annotated properties are pass-through converters. This parameter is nullable/optional so all existing `SharedKernelDbContext` subclasses remain compatible without modification.

**`symmetricEncryptionService` constructor parameter — keyed-DI resolution (SHIPPED, P-498/WO-081, D-131):** the existing optional `ISymmetricEncryptionService? symmetricEncryptionService` constructor parameter is now a BACK-COMPAT-ONLY fallback (hand-constructed tests, or the "no `.WithEncryption()` call at all" pass-through case). The REAL wiring happens via a new private static `ResolveKeyedSymmetricEncryptionService(DbContextOptions options)` helper inside the constructor: it reads `options.FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider` (EF Core's own public mechanism for a `DbContext` to reach the container/scope that constructed it — always set when a downstream `TContext` is registered via `AddDbContext<TContext>`/`AddDbContextFactory<TContext>`, always `null` for a hand-built `DbContextOptionsBuilder<T>().Options`) and calls `.GetKeyedService<ISymmetricEncryptionService>(PersistenceEncryptionKeys.SymmetricEncryptionServiceKey)` on it. **The keyed result, when non-null, ALWAYS WINS over the constructor parameter.** This is deliberate: it stops a downstream `TContext` subclass that mirrors the base constructor's full parameter list from having `symmetricEncryptionService` silently auto-populated by DI from an unrelated ambient unkeyed `AddSharedKernelCryptography()` registration, which would otherwise reintroduce the exact registration-order collision this phase closes. **No downstream `TContext` subclass signature change is required** — this resolution happens entirely inside the base constructor body using the `options` parameter every subclass already forwards; it has NO dependency on `[FromKeyedServices]` constructor-parameter attributes (which would have required every downstream subclass to reference this package's internal key — structurally impossible since the key is `internal`).

#### Multi-tenancy (`MultiTenancy/`)

```text
TenantedDbContext  (abstract class, extends SharedKernelDbContext)
    — Overrides OnModelCreating to install a global query filter on all IHasTenant entities:
      e => e.TenantId == this.TenantProvider.TenantId
    — Filter is built using expression trees (Expression.Parameter / Expression.Property /
      Expression.Equal / Expression.Lambda) — no GetMethod, MakeGenericMethod, or Invoke calls.
    — Constructor accepts ITenantProvider (from SharedKernel.Security.Abstractions) and DbContextOptions.
    .TenantProvider                                            → ITenantProvider  (protected get; private set)
    .RefreshRequestContext(IUserContext userContext, ITenantProvider tenantProvider)  → void  (WO-051/P-322)
    NOTE: Multi-tenant services extend TenantedDbContext; single-tenant services extend SharedKernelDbContext.
          FILTER REBUILD (WO-051/P-322, corrects a CONFIRMED PRE-EXISTING DEFECT independent of
          pooling — verified directly against source, not assumed): the filter previously bound
          Expression.Constant(provider, typeof(ITenantProvider)) — baking the SPECIFIC ITenantProvider
          OBJECT captured at whichever instant OnModelCreating first ran directly into the compiled
          filter, as a literal constant. EF Core's default model cache is keyed only by
          (context.GetType(), designTime) and is shared PROCESS-WIDE across every instance of a
          DbContext type (confirmed by EncryptionAwareModelCacheKeyFactory's own XML doc: "In
          production, a service has exactly one container and therefore exactly one cached model").
          TenantedDbContext registered NO analogous cache-key factory for ITenantProvider (only
          encryption's IEncryptionVersionOverride got that treatment) — meaning a long-running
          multi-tenant service WITHOUT .WithEncryption() enabled would have its tenant filter
          permanently frozen to whichever ITenantProvider instance constructed the very first
          TenantedDbContext in the process, for the process's entire lifetime. THE CORRECTED filter
          now binds through Expression.Constant(this, GetType()) → .TenantProvider → .TenantId —
          referencing an instance member off the DbContext itself. EF Core's query-filter compilation
          specially rebinds a captured "this DbContext instance" constant to whichever instance is
          EXECUTING the query, not the instance whose OnModelCreating built the cached model — the
          same well-established idiom Microsoft's own docs recommend for multi-tenant query filters.
          Because TenantProvider now reads live off the CURRENT instance on every query, this closes
          BOTH the pre-existing non-pooled staleness defect AND the WO-051/P-322 pooling hazard in the
          one fix — see "DbContext Pooling" below.
          RefreshRequestContext (WO-051/P-322): calls the base RefreshUserContext(userContext) and
          sets TenantProvider to the supplied instance. Non-pooled callers never need to call this —
          the constructor-set value is already correct for a non-pooled instance's lifetime (a fresh
          instance is constructed per DI scope). Only the pooled registration path
          (.WithDbContextPooling()) calls it, once per lease.

TenantedRepository<TAggregate, TId>  (abstract class, extends EfRepository<TAggregate, TId>)
    .GetByIdForTenantAsync(TId id, Guid tenantId, CancellationToken ct) → Task<TAggregate?>
    .GetByIdForTenantIncludingDeletedAsync(TId id, Guid tenantId, CancellationToken ct) → Task<TAggregate?>
    NOTE: GetByIdForTenantAsync — bypasses the tenant filter only; preserves the soft-delete filter
          for ISoftDeletable entities (soft-deleted records are excluded). Use for admin cross-tenant lookups
          where only live records are expected.
          GetByIdForTenantIncludingDeletedAsync — bypasses BOTH the tenant filter AND the soft-delete filter
          via IgnoreQueryFilters(). Use only for audit, recovery, or data-export operations. XML doc must warn
          both filters are bypassed.
          Standard GetByIdAsync flows through the global tenant filter automatically.
          PRIMARY-KEY PREDICATE (WO-051/P-316): both methods build the Id-equality predicate via the same
          Expression.Parameter/Property/Equal/Lambda pattern EfRepository.ExistsAsync uses — NOT
          EF.Property<TId>(e,"Id"), which was migrated away from here for the identical reason P-105
          fixed it in EfReadRepository.GetByIdsAsync: it can silently fall back to client-side evaluation
          for strongly-typed IDs backed by a registered ValueConverter, defeating server-side filtering.
          The separate EF.Property<bool>(e, nameof(ISoftDeletable.IsDeleted)) call in GetByIdForTenantAsync
          is unaffected — it operates on a bool property with no ValueConverter involved.
```

**Guid.Empty no-tenant sentinel rule (P-092):**

`ITenantProvider.TenantId` is `Guid` (non-nullable). `NoOpTenantProvider` returns `Guid.Empty` explicitly. When no real provider is registered, the global filter becomes `e.TenantId == Guid.Empty`, which returns **zero rows** — no production entity should ever have `TenantId == Guid.Empty`. This is intentional and safe: teams that forget to register a real provider see an empty result set immediately rather than a cross-tenant data leak.

`EntityTypeConfigurationBase.ConfigureTenantColumn` carries an XML doc stating `TenantId == Guid.Empty` is forbidden in production rows.

#### DI extensions (`Extensions/`)

```text
AddSharedKernelEfCore<TContext>(IServiceCollection services, Action<DbContextOptionsBuilder> configureDb)
    → returns EfCorePersistenceBuilder

EfCorePersistenceBuilder
    .WithMultiTenancy()
        — Registers NoOpTenantProvider (ITenantProvider, returns Guid.Empty) as scoped placeholder.
        — Asserts at .Build() time that TContext extends TenantedDbContext; throws InvalidOperationException
          with actionable message if the assertion fails.
    .WithTransactionalUnitOfWork()
        — Registers ITransactionalUnitOfWork → EfTransactionalUnitOfWork (scoped) alongside IUnitOfWork.
        — Optional. Services that never need explicit transactions can omit this call.
        — Application layer injects ITransactionalUnitOfWork; never IDbContextTransaction directly.
    .WithDbContextFactory()
        — Calls services.AddDbContextFactory<TContext>(configureDb) in addition to AddDbContext.
        — Required for background services, hosted workers, Hangfire jobs, and Temporal activities.
        — Factory-created contexts receive NoOpUserContext (UserId = Guid.Empty) for audit fields,
          producing "system" audit values, unless a singleton IUserContext is registered.
        — Optional. Omit for services that have no background DbContext consumers.
    .AddInterceptor<TInterceptor>()
        — Registers one additional ISaveChangesInterceptor beyond the platform three.
        — Multiple calls accumulate; all fire after the platform interceptors in registration order.
        — TInterceptor is registered as scoped.
        — Platform three (Audit, SoftDelete, Concurrency) always fire first — this is non-negotiable.
    .WithCompiledModel(IModel compiledModel)
        — Wraps the caller-supplied configureDb action to also call optionsBuilder.UseModel(compiledModel).
        — Compiled models are produced via dotnet ef dbcontext optimize.
        — When used, ValueObjectOwnershipBuilder.Apply and runtime model-building scans do not run —
          all mappings must be present in the compiled model.
        — Pure pass-through: builder does not validate the model.
    .WithMigrationsOnStartup()
        — Opt-in. Marks that Database.MigrateAsync should run during host startup.
        — Does not by itself register a hosted service — see .Build() below.
        — Compatible with .WithCompiledModel(): MigrateAsync still applies pending SQL migrations;
          the compiled model only affects runtime query/model-building, not migration application.
    .AddSeeder<TSeeder>()
        — TSeeder : class, IDataSeeder<TContext> for the same TContext as the builder.
        — Registers TSeeder as scoped. Multiple calls accumulate into an ordered list, executed in
          call order during startup.
        — Opt-in. Seeders are idempotent by contract (not enforced by the framework) — see
          IDataSeeder<TContext> below.
    .WithTransientFaultRetry(int maxRetryCount = 6, TimeSpan? maxRetryDelay = null)   (WO-051/P-320)
        — Registers a TransientFaultRetryOptions singleton into DI for discoverability/observability.
        — CANNOT itself configure Npgsql (no reference) — documented explicitly as a two-call opt-in
          PAIR with UsePostgreSQL(..., maxRetryCount, maxRetryDelay) (PostgreSQL package): calling only
          this method without also passing matching values to UsePostgreSQL(...) registers the options
          singleton but enables NO actual retry behavior.
        — The retry-SAFETY correction for explicit transactions (EfTransactionalUnitOfWork's
          BeginTransactionAsync guard + ExecuteInTransactionAsync) is UNCONDITIONAL and does NOT depend
          on this method having been called — it queries live EF Core execution-strategy state, so it
          correctly protects a consumer who enabled retry solely via UsePostgreSQL(...).
        — See "Transient-fault retry" below for the full picture.
    .WithDbContextPooling(int poolSize = 1024)   (WO-051/P-322)
        — Opt-in ALTERNATIVE to the default always-scoped AddDbContext registration, for services
          wanting the reduced allocation/GC overhead of a pooled DbContext at high request throughput.
        — Calls services.AddPooledDbContextFactory<TContext>((sp, options) => {...}, poolSize) — NOT
          the simple Action<DbContextOptionsBuilder> overload. CONFIRMED-BY-TESTING CORRECTION
          (implementation session, WO-051/P-322): EF Core FREEZES the DbContextOptions built by the
          pool's own optionsAction BEFORE any TContext instance is ever constructed from the pool —
          `optionsBuilder.Options.IsFrozen == true` inside OnConfiguring for EVERY pooled instance,
          including the first. Any subsequent attempt to mutate options at that point (e.g. the
          platform three interceptors' own AddInterceptors call) throws
          "'OnConfiguring' cannot be used to modify DbContextOptions when DbContext pooling is
          enabled" the moment the context's internal services are first built (SaveChangesAsync,
          EnsureCreatedAsync, etc. — not immediately at construction). Therefore this method's
          optionsAction itself constructs the platform three interceptors (a throwaway
          NoOpUserContext seeds their constructor — harmless, since AuditInterceptor/
          SoftDeleteInterceptor read CurrentUserContext live off eventData.Context, never their own
          captured field) plus any .AddInterceptor<T>() additional types (via ActivatorUtilities
          against the root sp — a consumer interceptor needing its OWN scoped dependencies will fail
          fast with a clear DI error under pooling, a documented, out-of-scope-for-this-phase edge
          case) and calls options.AddInterceptors(...) BEFORE the freeze. See
          SharedKernelDbContext.OnConfiguring's own note below for the matching guard.
        — PLUS an additional services.AddScoped<TContext>(...) factory-delegate
          registration that resolves IDbContextFactory<TContext>, calls CreateDbContext(), then calls
          RefreshUserContext(...) (SharedKernelDbContext) and, when TContext extends TenantedDbContext,
          RefreshRequestContext(...) — so EXISTING consumer code that injects TContext directly (the
          normal AddDbContext ergonomic) keeps working completely unchanged while transparently
          renting from the pool.
        — DI's normal scoped-disposal-at-end-of-request calls Dispose/DisposeAsync on the
          pooled-factory-created instance, which is EF Core's own "return to pool" trigger — no
          custom cleanup hook is registered or needed.
        — REQUIRES the WO-051/P-322 SharedKernelDbContext/TenantedDbContext redesign
          (CurrentUserContext/RefreshUserContext, TenantProvider/RefreshRequestContext) — see
          "DbContext Pooling" below for the full correctness story this method depends on.
        — GUARDS (both throw an actionable InvalidOperationException at .Build() time):
            (1) Combined with .WithDbContextFactory() — both register a conflicting
                IDbContextFactory<TContext> (pooled vs. non-pooled).
            (2) Combined with .WithEncryption() — IEncryptionVersionOverride's existing
                rotation-scoped seam (P-147) has the IDENTICAL constructor-capture staleness hazard
                this method's redesign fixes for user/tenant context, and has NOT been proven safe
                under pooling by this phase. Extending the refresh-on-lease pattern to the encryption
                rotation seam is an explicit, documented follow-up candidate — not silently left broken.
        — Optional. Omitting leaves the default scoped AddDbContext registration completely unchanged.
    .Build()
        — Registers TContext as DbContext (scoped)
        — Registers SharedKernel.Persistence.Abstractions.IUnitOfWork → EfUnitOfWork (scoped)
        — Registers SharedKernel.Application.Behaviors.IUnitOfWork → the SAME scoped EfUnitOfWork
          instance ONLY when .WithApplicationTransactionBehavior() was called (P-228)
        — Registers ISpecificationEvaluator<T> → SpecificationEvaluator<T> (singleton — stateless)
        — Registers AuditInterceptor, SoftDeleteInterceptor, ConcurrencyInterceptor (scoped)
        — Registers any additional interceptors supplied via .AddInterceptor<T>() (scoped)
        — Registers no-op IUserContext placeholder (UserId = Guid.Empty, IsAuthenticated = false)
          if no IUserContext already registered; uses SharedKernel.Security.Abstractions.IUserContext
        — Calls AddDbContextFactory<TContext> when .WithDbContextFactory() was invoked
        — Applies UseModel(compiledModel) when .WithCompiledModel() was invoked
        — Registers MigrationAndSeedHostedService<TContext> as IHostedService ONLY when
          .WithMigrationsOnStartup() was called, or at least one .AddSeeder<TSeeder>() was called.
          If neither was called, no hosted service is registered — fully opt-in, zero overhead.
        — Registers EncryptionOptionsKeyProvider and performs the ISymmetricEncryptionService
          eager startup check ONLY when .WithEncryption() was called (P-227).
    NOTE: No outbox, Dapper, or PostgreSQL wiring in this builder. Those are separate concerns.
          ITransactionalUnitOfWork is only registered when .WithTransactionalUnitOfWork() is called.
          IUserContext and ITenantProvider are both sourced from SharedKernel.Security.Abstractions.
          All fluent methods return EfCorePersistenceBuilder for chaining.
```

#### Transient-fault retry (WO-051/P-320)

```text
TransientFaultRetryOptions  (record, EfCore)
    .MaxRetryCount   → int
    .MaxRetryDelay   → TimeSpan?
    NOTE: Registered as a singleton by EfCorePersistenceBuilder.WithTransientFaultRetry(). Purely a
          discoverability/DI-registration artifact — carries no behavior of its own.

Required two-call pairing to actually enable retry:
    services
        .AddSharedKernelEfCore<OrderDbContext>(options =>
            options.UsePostgreSQL(connectionString, maxRetryCount: 6))   // the ONLY legal EnableRetryOnFailure call site
        .WithTransientFaultRetry(maxRetryCount: 6)                       // DI registration + retry-safety framing
        .Build();

Explicit-transaction retry-safety correction:
    EF Core's retrying execution strategy requires the ENTIRE transactional unit (BeginTransaction
    through Commit) to run inside one IExecutionStrategy.ExecuteAsync(...) delegate. The existing
    ITransactionalUnitOfWork.BeginTransactionAsync() → caller-held IPersistenceTransaction handle →
    CommitAsync() shape hands control back to arbitrary caller code between begin and commit — structurally
    incompatible with that contract.
        - BeginTransactionAsync() now guards: when
          DbContext.Database.CreateExecutionStrategy().RetriesOnFailure is true, throws a platform-
          specific, actionable InvalidOperationException directing the caller to ExecuteInTransactionAsync
          — BEFORE EF Core's own native (less actionable) exception would otherwise fire. UNCONDITIONAL —
          fires regardless of whether .WithTransientFaultRetry() was called.
        - ExecuteInTransactionAsync(Func<CancellationToken,Task> operation, ct) / <TResult> overload is
          the retry-safe alternative: wraps Database.CreateExecutionStrategy().ExecuteAsync(async () =>
          { begin; operation(ct); commit }), reusing the existing begin/commit machinery (including the
          P-105 deferred-domain-event-dispatch-until-commit rule). The whole delegate — including a fresh
          BeginTransactionAsync — re-runs on each retry attempt; a failed attempt's transaction rolls back
          via IDbContextTransaction's dispose-without-commit semantics before the next attempt begins, so
          no partial/duplicate commit occurs.
        - CONSTRAINT: code running inside the operation delegate may execute MORE THAN ONCE under retry —
          it must be safe to re-run. This is a general EF Core retrying-execution-strategy constraint the
          platform inherits, not one this wrapper introduces.
```

#### DbContext Pooling (WO-051/P-322)

Opt-in alternative to the default scoped `AddDbContext` registration. Additive — the default remains unchanged for consumers who don't opt in.

```text
Why it exists:
    At "hundreds of services" scale, the default scoped-per-request DbContext allocation cost is a
    real, measurable overhead that EF Core's AddDbContextPool/AddPooledDbContextFactory exists
    specifically to reduce. This is the one gold-standard gap in this domain where doing it naively
    would be actively DANGEROUS rather than merely incomplete — Microsoft's own EF Core documentation
    warns against constructor-captured scoped services in pooled contexts, and this package's
    TenantedDbContext builds its global tenant query filter directly from exactly such a captured
    reference.

The hazard, confirmed against real shipped source (not hypothetical):
    - AuditInterceptor/SoftDeleteInterceptor capture IUserContext in their OWN constructors. Under
      the default scoped registration this is correct (a fresh interceptor is built per request).
      Under pooling, a pooled DbContext instance's constructor — and the interceptor instances passed
      into it — runs ONCE per pooled slot, not once per lease. Reused across an unrelated later
      request, the captured IUserContext silently misattributes audit fields to the wrong user.
    - TenantedDbContext.TenantProvider was a get-only property baked into the compiled tenant filter
      via Expression.Constant(specificObject, ...) at OnModelCreating time. Because EF Core's model
      cache is PROCESS-WIDE by default (one build per DbContext type per container — confirmed by
      EncryptionAwareModelCacheKeyFactory's own doc), this was ALREADY a confirmed, pre-existing
      cross-tenant-leak risk for any long-running multi-tenant service NOT using .WithEncryption()
      (whose own cache-key widening incidentally, and expensively, forces a fresh model build per
      distinct IEncryptionVersionOverride instance) — independent of pooling. See TenantedDbContext's
      own corrected doc above for the full story.

The fix (closes both hazards in one redesign):
    - SharedKernelDbContext gains CurrentUserContext (settable only via RefreshUserContext) and
      AuditInterceptor/SoftDeleteInterceptor read it LIVE off eventData.Context inside
      SavingChanges/SavingChangesAsync — eventData.Context is always the CURRENT executing instance,
      supplied fresh by EF Core on every interceptor callback, never a stale captured reference.
    - TenantedDbContext gains TenantProvider (settable only via RefreshRequestContext) and the tenant
      filter is rebuilt to bind through Expression.Constant(this, GetType()) rather than a specific
      captured ITenantProvider object — EF Core's query-filter compilation specially rebinds a
      captured "this DbContext instance" constant to whichever instance is EXECUTING the query, not
      the instance whose OnModelCreating built the (possibly process-wide-cached) model.
    - EfCorePersistenceBuilder.WithDbContextPooling(poolSize) wires AddPooledDbContextFactory<TContext>
      plus a scoped TContext factory delegate that creates-then-refreshes, so this all happens
      transparently — existing consumer code injecting TContext directly is unaffected.

>>> PROMINENT WARNING for future contributors <<<
    Any FUTURE constructor-captured scoped dependency added to SharedKernelDbContext or
    TenantedDbContext (or a consuming service's own subclass) MUST be threaded through the same
    refresh-on-lease pattern (a RefreshXxx method called by the pooled registration path) or it will
    SILENTLY reintroduce this exact class of cross-request data leak the moment a consumer opts into
    .WithDbContextPooling(). This is not a hypothetical — it is the precise defect this phase closes.
    A constructor parameter alone is NEVER sufficient for state that must vary per request once
    pooling is in play.

Guards:
    .WithDbContextPooling() + .WithDbContextFactory()  → InvalidOperationException at .Build()
    .WithDbContextPooling() + .WithEncryption()         → InvalidOperationException at .Build()
        (IEncryptionVersionOverride has the identical hazard; not yet proven safe under pooling —
         a documented follow-up, not silently broken)
```

---

#### Migrations and data seeding (`Seeding/`) — P-151

```text
IDataSeeder<TContext>  (interface)
    where TContext : DbContext
    .SeedAsync(TContext context, CancellationToken ct)          → Task
    NOTE: Idempotency is a CONTRACT, not enforced by the framework — implementations must check
          for existing data (or use upsert semantics) before inserting, since SeedAsync may run
          on every application startup. Each registered seeder receives its own DI scope and its
          own TContext instance, resolved via IDbContextFactory<TContext> (so .WithDbContextFactory()
          is implicitly required when any seeder is registered — EfCorePersistenceBuilder.Build()
          enables the factory automatically in this case if not already requested).

MigrationAndSeedHostedService<TContext>  (internal sealed class, implements IHostedService)
    where TContext : DbContext
    .StartAsync(CancellationToken ct)
    NOTE: Registered by EfCorePersistenceBuilder.Build() only when .WithMigrationsOnStartup() was
          called, or at least one seeder was registered via .AddSeeder<TSeeder>().
          Sequence on StartAsync:
            1. Acquire a PostgreSQL advisory lock via pg_advisory_lock(hashtext(lockKey)), using the
               existing IDbConnectionFactory — no new dependency on 02.Caching is introduced. The
               lock key is derived from the context type's full name so multiple replicas racing on
               startup serialize migration/seeding to a single instance.
            2. If migrations-on-startup was requested, call context.Database.MigrateAsync(ct). The
               compiled model (if supplied via .WithCompiledModel()) does not change this step —
               MigrateAsync applies SQL DDL independently of the runtime model.
            3. Run each registered seeder in registration order, each resolved in its own DI scope
               with its own TContext instance via IDbContextFactory<TContext>.
            4. Release the advisory lock via pg_advisory_unlock in a finally block, regardless of
               success or failure of steps 2-3.
          .StopAsync is a no-op.
          GENUINE ASYNC (CORRECTED, WO-051/P-325 — the original implementation called the SYNCHRONOUS
          IDbCommand.ExecuteNonQuery() for BOTH the lock acquire and release, inside this async
          StartAsync method, blocking a thread-pool thread on the less-frequent-but-still-real
          startup/migration path): both the acquire (step 1) and release (step 4) commands are
          safe-cast to System.Data.Common.DbCommand and their true ExecuteNonQueryAsync is awaited,
          with a synchronous fallback retained for any hypothetical non-DbCommand implementer.
          CRITICAL: the RELEASE call (step 4, inside the finally block) unconditionally uses
          CancellationToken.None, never the StartAsync-supplied token — the original synchronous
          ExecuteNonQuery() ignored cancellation entirely and always ran to completion; awaiting
          ExecuteNonQueryAsync with a possibly-already-cancelled token in the release path would risk
          throwing OperationCanceledException and SKIPPING the unlock, a genuine correctness
          regression this fix must not introduce. The ACQUIRE call (step 1) legitimately honors the
          original token — abandoning an acquire that's being cancelled is fine.
          IDbConnectionFactory's public interface signature is completely unchanged by this fix.
          Alternative for non-PostgreSQL providers or stronger guarantees: consumers may instead
          wrap their own startup logic with SharedKernel.Caching.Redis.DistributedLocking — this is
          documented as an option, not implemented by this hosted service.
          Non-goals: this is not a migration-authoring tool (use `dotnet ef migrations add` as
          normal) and does not replace .WithCompiledModel() (P-106) — compiled models and
          migrations operate independently.
```

#### Observability (`Diagnostics/`) — WO-051/P-319

```text
Automatic TagWith:
    SpecificationEvaluator<T>.GetQuery/GetProjectedQuery/GetKeysetQuery<TKey> each call
    .TagWith(spec.GetType().Name) as the FIRST operation in the pipeline (evaluator step -1) — zero
    configuration, zero new API surface, applies to every existing and future specification automatically.
    spec.GetType().Name is a CLR type name (e.g. "ActiveOrdersSpecification") — always safe against
    TagWith's comment-injection guard and carries zero row/parameter/tenant data.

PersistenceActivitySource  (internal static class, EfCore)
    .Source   → static readonly ActivitySource  ("SharedKernel.Persistence", "1.0")
    NOTE: Mirrors 02.Caching's SharedKernel.Caching/"1.0" ActivitySource precedent exactly
          (SharedKernel.Caching.FusionCache/Implementations/FusionCacheService.cs). BCL
          System.Diagnostics.ActivitySource — zero new NuGet dependency, AOT-safe.
          THIS EXACT NAME/VERSION IS THE COORDINATION POINT for 13.ServiceDefaults' paired phase P-326
          (WithPersistenceTelemetry-shaped wiring) — do not rename without updating that consumer.

PersistenceTagKeys  (internal static class, EfCore)  — colocated per the platform's Magic String Convention
    .AggregateType   = "persistence.aggregate_type"
    .Operation       = "persistence.operation"
    .Outcome         = "persistence.outcome"
    NOTE: The exception-type tag on failure reuses 01.Core's cross-domain WellKnownTagKeys.ErrorType
          ("error.type") rather than duplicating a domain-local key.

Traced operations (span name "{typeof(TAggregate).Name}.{OperationName}", ActivityKind.Client):
    EfRepository:      GetBySpecAsync (write-side tracked fetch), AddAsync, UpdateAsync, DeleteAsync,
                       AddRangeAsync, UpdateRangeAsync, DeleteRangeAsync, GetByIdAsync, ExistsAsync
                       (CORRECTED — GetBySpecAsync was omitted from this list in earlier drafts of this
                       section despite being traced in the shipped source since the method's own
                       introduction; DO-44/WO-051 doc-verification pass. GetByIdAsync/ExistsAsync were
                       genuinely NOT traced until WO-053/P-333 closed the gap — the two most frequently
                       called EfRepository members were, until then, the only ones without a span, now
                       wrapped in the same RepositoryTracing.ExecuteTracedAsync helper as every sibling)
    EfReadRepository:  GetBySpecAsync, ListAsync, CountAsync, AnyAsync, GetByIdsAsync, ListPagedAsync,
                       ListProjectedAsync, GetBySpecProjectedAsync, ListPagedProjectedAsync,
                       StreamAsync/StreamProjectedAsync<TResult> (span wraps the FULL enumeration —
                       start before the first yield, end after the last), ListKeysetAsync<TKey>
    NOT traced: EfRepository.ExecuteUpdateAsync/ExecuteDeleteAsync (IBulkMutationRepository) and
    EfReadRepository.GetByIdsChunkedAsync — the bulk-mutation pair bypasses RepositoryTracing
    consistent with their documented bypass of every other per-entity platform concern (interceptors,
    domain events); GetByIdsChunkedAsync produces its OWN traced spans indirectly, one per underlying
    GetByIdsAsync call it issues, rather than a single span of its own.
    Tagging: AggregateType/Operation set at span start; Outcome set to "success" or "failure" at
    completion (+ WellKnownTagKeys.ErrorType and ActivityStatusCode.Error on failure). NEVER a raw SQL
    parameter value, entity property value, or tenant/user identifier in any tag — mirrors the
    cache.key_prefix-never-full-key precedent from 02.Caching's own P-304. The shared tracing helper
    (RepositoryTracing.ExecuteTracedAsync/ExecuteTracedStreamAsync, internal to
    SharedKernel.Persistence.EfCore.Diagnostics) avoids duplicating the start/tag/try-catch/finish
    boilerplate across every repository method.
```

#### Structured Logging (WO-053/P-333)

`SharedKernel.Persistence.EfCore` claims the `6000-6099` sub-block of `01.Core`'s `LoggingEventIdRanges`-reserved `6000-6999` for `06.Persistence`. `SharedKernel.Persistence.Abstractions` — a pure interface library with no DI-resolved `ILogger` consumer — reserves no sub-block at all; a future package that starts logging (`.PostgreSQL`, `.Dapper`) claims the next unclaimed 100-wide slot (`6100-6199`, then `6200-6299`) at that time, never renumbering `6000-6099` retroactively. Every event below is a `[LoggerMessage]`-attributed partial method with an explicit `EventId` — never a direct `ILogger.LogX(...)` call.

```text
EventId  Name                              Level        Emitted by
6000     ConcurrencyConflictDetected       Warning      ConcurrencyInterceptor.TryTranslate (called from SharedKernelDbContext.SaveChanges/SaveChangesAsync, before the translated ConflictException is thrown)
6001     MigrationAndSeedStarted           Information  MigrationAndSeedHostedService.StartAsync (entry)
6002     SeederApplied                     Information  MigrationAndSeedHostedService (per successfully-applied seeder)
6003     MigrationAndSeedCompleted         Information  MigrationAndSeedHostedService (overall completion)
6004     MigrationAndSeedFailed            Warning      MigrationAndSeedHostedService (any caught failure, before rethrow)
6005     AdvisoryLockAcquired              Information  MigrationAndSeedHostedService (step 1 of StartAsync)
6006     AdvisoryLockReleased              Information  MigrationAndSeedHostedService (step 4, the finally block)
6007     TransientRetryAttempt             Warning      internal PersistenceRetryDiagnosticListener (one per retry)
6008     TransientRetryExhausted           Warning      EfUnitOfWork / EfTransactionalUnitOfWork (final failure after retries)
6009     EncryptionRotationBatchProcessed  Information  EncryptionRotationService (per batch boundary)
6010     EncryptionRotationCompleted       Information  EncryptionRotationService.RotateAsync (overall completion)

NOTE: ConcurrencyConflictDetected (6000) logs ONLY the conflicting entry's CLR type name
      (entry.Entity.GetType().Name via a {EntityType} named property) — never the row payload.
      CORRECTED (Design-confirmation pass, WO-053): the original draft of this design assumed the
      log call and the ConflictException construction both happen inside
      ConcurrencyInterceptor.SaveChangesFailed/SaveChangesFailedAsync. Verified against the real
      shipped source (WO-051/P-315, ConcurrencyInterceptor.cs's own class remarks): those two hooks
      are permanent, documented no-op overrides — EF Core 10 does not allow
      ISaveChangesInterceptor.SaveChangesFailed/SaveChangesFailedAsync to replace the exception
      propagating from SaveChanges/SaveChangesAsync (confirmed empirically by that phase). The real
      translation happens in ConcurrencyInterceptor.TryTranslate (today an internal STATIC method)
      called from SharedKernelDbContext.SaveChanges(bool)/SaveChangesAsync(bool, CancellationToken)'s
      own catch-filter (`catch (DbUpdateConcurrencyException ex) when (ConcurrencyInterceptor
      .TryTranslate(ex) is { } conflict) { throw conflict; }`), and the thrown type is
      SharedKernel.Core.Exceptions.ConflictException, not a bespoke "ConcurrencyException" (no such
      type exists in this domain). To log from the interceptor INSTANCE (needed so the injected
      ILogger<ConcurrencyInterceptor> is reachable), TryTranslate must become a non-static instance
      method, and SharedKernelDbContext.SaveChanges/SaveChangesAsync must call it via the context's
      existing `_concurrencyInterceptor` field (already held, already passed to the constructor —
      zero new plumbing) instead of the static `ConcurrencyInterceptor.TryTranslate(ex)` call. The
      log statement belongs inside TryTranslate, immediately before it returns the translated
      ConflictException — i.e., still "before rethrowing," just one call frame further out than the
      original draft assumed. Both call sites (SaveChanges and SaveChangesAsync) automatically pick
      up the logging once TryTranslate itself logs — no per-call-site duplication needed.
      TransientRetryAttempt (6007) is registered ONLY when EfCorePersistenceBuilder.WithTransientFaultRetry()
      is called — an internal DiagnosticListener subscription against EF Core's own provider-neutral
      Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ExecutionStrategyRetrying / ExecutionStrategyEventData
      (defined in the core Microsoft.EntityFrameworkCore assembly, never Npgsql's) — preserving the
      "EfCore never references Npgsql" hard rule. TransientRetryExhausted (6008) has no EF Core diagnostic
      event of its own to hook — it is emitted by wrapping the outermost Database.CreateExecutionStrategy()
      call inside EfUnitOfWork.SaveChangesAsync and EfTransactionalUnitOfWork.SaveChangesAsync/
      ExecuteInTransactionAsync (the platform's ONLY two save boundaries), catching the final propagating
      exception when RetriesOnFailure == true, logging, then rethrowing unchanged — full coverage falls
      out naturally since every save funnels through one of these two classes.
      EncryptionRotationBatchProcessed/Completed (6009/6010) NEVER log a key byte, a Base64-encoded key
      string, or any column plaintext/ciphertext value — only counts (RowsInBatch/RowsProcessed/
      RowsRotated/RowsFailed) and already-non-secret version-tag strings (e.g. "v1"/"v2").
      Message templates use PascalCase named placeholders ({EntityType}, {ContextType}, {SeederType},
      {AttemptNumber}, {AttemptCount}, {FromVersion}, {ToVersion}, {BatchNumber}, {RowsInBatch},
      {RowsProcessed}, {RowsRotated}, {RowsFailed}) — never positional, never string-interpolated.
      CorrelationId/TraceId/TenantId are never passed as explicit template parameters — they flow
      ambiently through the OpenTelemetry logging pipeline per the platform's Logging Conventions.
```

#### Soft-Delete Restore (WO-053/P-337)

Reverses `SoftDeleteInterceptor`'s delete — additive to the existing automatic delete path, which had no equally-easy reverse path before this phase.

```text
Single-entity restore:  IRestorableRepository<TAggregate,TId>.RestoreAsync(aggregate, ct)
    Implemented unconditionally by EfRepository<TAggregate,TId> (see the EfCore Repositories section
    above). Runtime-guards against a non-ISoftDeletable TAggregate; otherwise flips IsDeleted/DeletedOn/
    DeletedBy via ChangeTracker.CurrentValues and marks the entry Modified. Only STAGES the change — a
    subsequent IUnitOfWork.SaveChangesAsync() persists it, going through the SAME interceptor/audit/
    domain-event pipeline a normal update would (AuditInterceptor sets ModifiedBy/ModifiedOn as a
    byproduct of the entry being an ordinary Modified row — zero interceptor changes were needed).

Bulk restore:  a USAGE PATTERN of the ALREADY-SHIPPED IBulkMutationRepository.ExecuteUpdateAsync — NOT
    a new method. Mirrors the existing bulk soft-delete example exactly, in reverse:

        var restoredCount = await orderRepository.ExecuteUpdateAsync(
            new WithDeletedOrdersSpecification(),   // IncludeDeleted = true so soft-deleted rows are matched
            setters => setters
                .SetProperty(o => ((ISoftDeletable)o).IsDeleted, false)
                .SetProperty(o => ((ISoftDeletable)o).DeletedOn, (DateTimeOffset?)null)
                .SetProperty(o => ((ISoftDeletable)o).DeletedBy, (string?)null),
            ct);

    Carries the SAME "bypasses SaveChangesAsync, the three platform interceptors, and domain event
    dispatch" documented bypass every other IBulkMutationRepository call already carries — bulk restore
    is not audited and does not fire domain events, exactly like bulk soft-delete does not.
```

#### Read-Replica Routing (WO-053/P-338)

Opt-in routing of `IReadRepository<TAggregate,TId>` operations to a separate PostgreSQL connection, distinct from the primary connection `IRepository<TAggregate,TId>` writes always use. Additive — default (no replica configured) behavior is provably unchanged.

```text
IReadReplicaContextAccessor<TContext>  (internal, SharedKernel.Persistence.EfCore — a wiring detail,
                                        not a consumer-facing abstraction)
    .GetEffectiveContext(TContext primaryContext)               → TContext
    NOTE: Returns primaryContext UNCONDITIONALLY whenever primaryContext.Database.CurrentTransaction !=
          null — an explicit EF Core transaction is currently open on the primary, so reads must never
          diverge from it. Otherwise returns the lazily-constructed, scope-cached replica TContext
          instance registered by EfCorePersistenceBuilder.WithReadReplica(...). When
          .WithReadReplica(...) was never called, this accessor is simply never registered in DI.

EfReadRepository<TAggregate,TId>  (extension per WO-053/P-338)
    constructor gains: IReadReplicaContextAccessor<TContext>? replicaAccessor = null   (optional, nullable)
    NOTE: Purely additive — every existing EfReadRepository subclass continues to compile and behave
          identically without passing anything new. Every read method's DbContext.Set<TAggregate>()/
          _evaluator.GetQuery(...) call site is redirected through a private computed property
          `EffectiveContext => replicaAccessor?.GetEffectiveContext(DbContext) ?? DbContext` — resolved
          AFRESH on every call, never cached, since transaction state can change between two read calls
          issued against the SAME injected repository instance. EfRepository/IRepository (the write side)
          are completely untouched by this feature — writes always target the primary connection
          unconditionally, with no accessor parameter of any kind.

READ-AFTER-WRITE CONSISTENCY IS THE CALLER'S RESPONSIBILITY ONCE REPLICA ROUTING IS ENABLED. A command
handler that writes then immediately reads via IReadRepository in the same logical operation MAY
OBSERVE STALE DATA under replication lag. A read issued inside an active transaction (BeginTransactionAsync/
ExecuteInTransactionAsync) is NEVER routed to the replica, even when replica routing is otherwise enabled
— reads inside an explicit transaction always see the primary.
```

---

### `SharedKernel.Persistence.PostgreSQL` — public surface

#### Conventions and extensions (`Conventions/`, `Extensions/`)

```text
SnakeCaseNamingConvention  (implements IModelFinalizingConvention)
    — Converts all table names, column names, index names, and constraint names to snake_case.
    — Applied automatically when UsePostgreSQL() DI extension is called.

XminConcurrencyTokenConvention  (implements IModelFinalizingConvention)  (WO-051/P-315)
    — Scans the finalized model for entity types implementing IHasConcurrency; for each, locates the
      property already marked .IsConcurrencyToken() == true by EntityTypeConfigurationBase (EfCore
      package — see that section's WO-051/P-315 correction) and reconfigures it:
          .HasColumnName("xmin"), .HasColumnType("xid"),
          .HasConversion(new XminRowVersionValueConverter()), ValueGenerated.OnAddOrUpdate.
    — Registered automatically when UsePostgreSQL() is called, via the same IConventionSetPlugin/
      service-replacement mechanism SnakeCaseNamingConvention already uses; runs after (or idempotently
      alongside) SnakeCaseNamingConvention — "xmin" is already lowercase/snake_case-compatible.
    — THIS is the genuine, working PostgreSQL concurrency-token mechanism the platform relies on for
      IHasConcurrency/FullAuditableAggregateRoot<TId>/FullAuditableEntity<TId> — see EntityTypeConfigurationBase's
      corrected doc (EfCore section) for the full "why .IsRowVersion() alone doesn't work on Postgres" story.

XminRowVersionValueConverter  (sealed class, extends ValueConverter<byte[], uint>)  (WO-051/P-315)
    — Round-trips the domain's byte[] RowVersion representation to/from Npgsql's native uint xid value
      via System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian/WriteUInt32BigEndian (4-byte
      big-endian). BCL-only, zero reflection.

UsePostgreSQL(DbContextOptionsBuilder optionsBuilder, string connectionString,
              int? maxRetryCount = null, TimeSpan? maxRetryDelay = null,
              ICollection<string>? errorCodesToAdd = null)
    → configures Npgsql provider + SnakeCaseNamingConvention + XminConcurrencyTokenConvention +
      vector support + JSONB defaults + (opt-in) EnableRetryOnFailure
    NOTE: Single call in DI composition replaces manual provider + naming convention wiring.
          Retry parameters (WO-051/P-320): the ONLY legal call site for Npgsql's EnableRetryOnFailure —
          SharedKernel.Persistence.EfCore never references Npgsql, so this is the sole place transient-
          fault retry can actually be wired. When maxRetryCount is null (the default), behavior is
          byte-for-byte unchanged from today. When supplied, internally threads
          npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(maxRetryCount.Value,
          maxRetryDelay ?? TimeSpan.FromSeconds(30), errorCodesToAdd) into the UseNpgsql(...) call.
          See "Transient-fault retry" below (EfCore section) for the required pairing with
          EfCorePersistenceBuilder.WithTransientFaultRetry() and the explicit-transaction retry-safety
          correction.
```

#### JSONB support (`Jsonb/`)

```text
JsonbColumnAttribute  [AttributeUsage(Property)]
    — Marks an EF Core property for JSONB column storage using Npgsql JSON column type.
    — Consumed by JsonbEntityTypeBuilderExtension to apply .HasColumnType("jsonb").

JsonbEntityTypeBuilderExtension  (static extension on EntityTypeBuilder<T>)
    .HasJsonbColumn<TProperty>(propertyExpression)             → EntityTypeBuilder<T>
    NOTE: STJ serialization is configured globally; individual JSONB columns do not need per-column converters.
```

#### pgvector support (`Vector/`)

```text
VectorColumnAttribute  [AttributeUsage(Property)]
    — Marks a float[] or Vector property for pgvector column storage.

VectorEntityTypeBuilderExtension  (static extension on EntityTypeBuilder<T>)
    .HasVectorColumn<TProperty>(propertyExpression, int dimensions) → EntityTypeBuilder<T>
    NOTE: Requires pgvector extension enabled in PostgreSQL. Call EnsureVectorExtension()
          in the migration or startup to create it if absent.

VectorDistanceMetric  (enum, `Vector/`)                                                     (WO-053/P-339)
    Cosine | L2
    NOTE: CORRECTED (Design-confirmation pass, WO-053): the original draft claimed this enum "mirrors
          the exactly-two distance functions Pgvector.EntityFrameworkCore's own VectorExtensions
          exposes." Verified against the real shipped package (reflection against
          Pgvector.EntityFrameworkCore 0.3.0's actual assembly): there is no type named
          VectorExtensions at all — the distance/similarity functions live on
          Pgvector.EntityFrameworkCore.VectorDbFunctionsExtensions (see VectorOrderingExpressions
          below) — and that class exposes SIX members, not two: CosineDistance, L2Distance,
          L1Distance, HammingDistance, JaccardDistance, MaxInnerProduct. This enum is a DELIBERATE
          scope narrowing to the two most common similarity metrics (cosine and Euclidean/L2) — the
          root CLAUDE.md's own WO-053/P-339 changelog entry independently confirms this scope ("a
          cosine/L2 distance-ordering helper"), so Cosine|L2 remains the correct, ratified shape. Only
          the ORIGINAL JUSTIFICATION ("mirrors the exactly-two... exposes") was factually wrong; the
          corrected rationale is: a deliberately narrower set than the full six-function
          VectorDbFunctionsExtensions surface. A future phase may extend this enum (L1/Hamming/
          Jaccard/MaxInnerProduct) if a concrete consumer need emerges — not attempted here.

VectorOrderingExpressions  (static class, `Vector/`)                                        (WO-053/P-339)
    .ByDistance<TAggregate>(Expression<Func<TAggregate,Vector>> vectorSelector, Vector queryVector,
        VectorDistanceMetric metric)                                → Expression<Func<TAggregate,object>>
    NOTE: The first query-side pgvector ergonomics this domain ships — HasVectorColumn/VectorColumnAttribute
          above map a column only (a direct HasColumnType("vector(N)") string, not even routed through
          Pgvector.EntityFrameworkCore's own helper types); this helper is the platform's first assist for
          the one thing a consumer actually wants to do with a vector column — find the nearest rows to a
          query vector. Builds the SAME boxed-to-object key-selector expression shape ISpecification<T>.
          OrderBy/OrderByDescending already store (matching the KeysetSpecification<T,TKey> boxing
          precedent), wrapping vectorSelector's member access in an Expression.Call invoking
          Pgvector.EntityFrameworkCore.VectorDbFunctionsExtensions.CosineDistance/L2Distance (chosen by
          metric) against the query vector.
          CORRECTED (WO-053 Tests phase, T-118/T-119 — genuine runtime defect found and fixed, never
          exercised against a real database until this pass): the original implementation passed
          queryVector as a bare Expression.Constant(queryVector, typeof(Vector)). Running the FIRST
          real end-to-end query against a live pgvector-enabled PostgreSQL container proved this
          throws a genuine PostgresException ("42601: syntax error at or near '['") — EF Core's query
          pipeline treats a bare ConstantExpression as an already-evaluated INLINE SQL literal and
          renders it via Vector.ToString() ("[1,2,3]") with NO quoting/cast, since
          Pgvector.EntityFrameworkCore's distance-function SQL translator does not attach a "vector"
          RelationalTypeMapping to an inline constant the way it does for a genuine ADO.NET query
          parameter. THE FIX: queryVector is wrapped in a private single-property holder class and
          accessed via Expression.Property(Expression.Constant(holder), nameof(holder.Value)) —
          reproducing the EXACT shape the C# compiler emits for a captured local variable inside an
          ordinary LINQ lambda closure (a display-class instance held as a ConstantExpression, read
          via member access). EF Core's parameter-extraction visitor recognizes this shape and
          promotes it to a genuine query parameter (confirmed via the generated SQL changing from
          "ORDER BY p.embedding <=> [1,0,0]" to "ORDER BY p.embedding <=> @Value"), which Npgsql then
          writes through its normal, already-proven-working Vector parameter type mapping — the same
          path every INSERT/UPDATE of a Vector-typed column already uses successfully. Zero change to
          this method's public signature or its zero-reflection MethodInfo-capture technique — only
          the query-vector-argument construction changed.
          CORRECTED (Design-confirmation pass, WO-053): the original draft named the containing type
          "VectorExtensions" and implied a `(Vector, Vector)` signature. Verified against the real
          shipped Pgvector.EntityFrameworkCore 0.3.0 assembly via direct reflection: no
          "VectorExtensions" type exists at all. The real, sealed, static type is
          Pgvector.EntityFrameworkCore.VectorDbFunctionsExtensions, and every one of its six distance/
          similarity members (CosineDistance/L2Distance/L1Distance/HammingDistance/JaccardDistance/
          MaxInnerProduct) is declared as `public static double MethodName(this object a, object b)` —
          `object` parameters, not `Vector` parameters (these are EF-Core query-translation
          placeholder methods, meaningfully invoked only inside a LINQ expression tree that
          VectorDbFunctionsTranslatorPlugin rewrites to the real `<=>`/`<->` SQL operator server-side;
          calling them directly, client-side, is not a supported code path).
          The MethodInfo for each distance function is STILL captured via a STATICALLY-TYPED delegate
          cast with ZERO reflection — ((Func<Vector,Vector,double>)VectorDbFunctionsExtensions.
          CosineDistance).Method — resolved entirely by the C# COMPILER, never Type.GetMethod/
          MakeGenericMethod at runtime. This works DESPITE the real method's `object,object` signature
          because Pgvector.Vector is a reference type (a class, not a struct) — confirmed via
          reflection — and C#'s method-group-to-delegate conversion permits contravariant reference-type
          parameter widening (a method accepting `object` satisfies a `Func<Vector,Vector,double>`
          delegate site); this was independently compiled and confirmed to produce the expected
          MethodInfo, not merely assumed from the C# language spec. The captured MethodInfo's own
          .GetParameters() report `object`/`object`, not `Vector`/`Vector` — Expression.Call's argument
          type-checking accepts the Vector-typed vectorSelector/queryVector expressions directly against
          those `object` parameters via the identical implicit reference-conversion rule, so no
          Expression.Convert/boxing node is needed in the built expression tree.
          Deliberately does NOT call ApplyOrderBy itself and introduces NO new Specification<T>
          base class — the returned expression is passed by the CONSUMER's own Specification<TAggregate>
          subclass into ITS OWN protected ApplyOrderBy call from within that subclass's constructor, exactly
          how KeysetSpecification<T,TKey>/PagedSpecification<T> already populate protected members from
          their own constructors. Composes automatically with the consumer's own Criteria/Includes/Take —
          ZERO SpecificationEvaluator<T> pipeline changes, since nothing about the evaluator's existing
          OrderBy→...→Take handling changes (proven end to end against a real server, WO-053 Tests phase
          T-120: Criteria filters and Take caps the result set while distance ordering remains correct).
          Scoped to Vector-typed properties only — a documented limitation, not a defect. A float[]-typed
          vector column (the OTHER type HasVectorColumn supports) is out of scope for this helper.

    Usage:
        public sealed class NearestProductsSpecification : Specification<Product>
        {
            public NearestProductsSpecification(Vector queryEmbedding, int topK)
            {
                AddCriteria(p => p.IsActive);
                ApplyOrderBy(VectorOrderingExpressions.ByDistance<Product>(
                    p => p.Embedding, queryEmbedding, VectorDistanceMetric.Cosine));
                ApplyPaging(skip: 0, take: topK);
            }
        }
```

#### PostgreSQL DI registration (`Extensions/`)

```text
AddSharedKernelPostgreSQL(IServiceCollection services, string connectionString)
    → registers Npgsql IDbConnectionFactory (for Dapper in the same service)
    → configures NpgsqlDataSource with STJ options
    NOTE: Downstream services call AddDbContext<TContext>(...).UsePostgreSQL(conn) separately —
          this extension only wires the shared NpgsqlDataSource and IDbConnectionFactory.
```

---

### `SharedKernel.Persistence.Dapper` — public surface

#### Dapper connection factory (`Connections/`)

```text
NpgsqlConnectionFactory  (sealed class, implements IDbConnectionFactory)
    .CreateConnectionAsync(CancellationToken ct)               → Task<IDbConnection>
    NOTE: Returns an open NpgsqlConnection. Caller disposes.
          Backed by injected NpgsqlDataSource — connection pooling is managed by Npgsql.
```

#### Type handlers (`TypeHandlers/`)

```text
StronglyTypedIdTypeHandler<TStronglyTypedId, TValue>  (abstract class, extends SqlMapper.TypeHandler<TStronglyTypedId>)
    .SetValue(IDbDataParameter parameter, TStronglyTypedId value) → void
    .Parse(object value)                                          → TStronglyTypedId
    NOTE: Consuming services implement a concrete handler per strongly-typed ID type (one line).
          Registered via DapperTypeHandlers.Register() at startup.

SmartEnumTypeHandler<TEnum, TValue>  (abstract class, extends SqlMapper.TypeHandler<TEnum>)
    .SetValue(IDbDataParameter parameter, TEnum value)            → void
    .Parse(object value)                                          → TEnum
    NOTE: Uses SmartEnum<TEnum,TValue>.TryFromValue — no reflection.

DapperTypeHandlers  (static class)
    .Register()                                                   → void
    NOTE: Call once at startup to register all type handlers. Idempotent.
```

#### Read-model base (`ReadModels/`)

```text
DapperReadService  (abstract class)
    protected DapperReadService(IDbConnectionFactory connectionFactory)
    protected ConnectionFactory                                                                       → IDbConnectionFactory  (protected get)  (WO-051/P-321)
    protected .QueryAsync<TResult>(string sql, object? parameters, CancellationToken ct) → Task<IEnumerable<TResult>>
    protected .QuerySingleOrDefaultAsync<TResult>(string sql, object? parameters, CancellationToken ct) → Task<TResult?>
    protected .ExecuteAsync(string sql, object? parameters, CancellationToken ct) → Task<int>
    protected .QueryAsync<TFirst,TSecond,TReturn>(string sql, Func<TFirst,TSecond,TReturn> map,
        object? parameters = null, string splitOn = "Id", CancellationToken ct = default)
        → Task<IEnumerable<TReturn>>                                                                   (WO-051/P-321)
    protected .QueryAsync<TFirst,TSecond,TThird,TReturn>(string sql, Func<TFirst,TSecond,TThird,TReturn> map,
        object? parameters = null, string splitOn = "Id", CancellationToken ct = default)
        → Task<IEnumerable<TReturn>>                                                                   (WO-051/P-321)
    protected .QueryMultipleAsync<TResult>(string sql, Func<SqlMapper.GridReader,Task<TResult>> readFunc,
        object? parameters = null, CancellationToken ct = default)
        → Task<TResult>                                                                                (WO-051/P-321)
    NOTE: All methods open and dispose the connection per call via IDbConnectionFactory.
          SQL is caller-supplied — no query builder abstraction is provided at this layer.
          Parameterized queries only — no string interpolation in SQL. This rule is UNCHANGED and
          applies identically to every new method below.
          ConnectionFactory (WO-051/P-321): the connection factory field was promoted from a private
          field to this protected property specifically as the documented, supported extension seam
          for any Dapper capability this base class doesn't itself wrap (e.g. a stored-procedure call
          with output parameters, a bulk-copy operation) — a subclass reuses the SAME
          open-per-call/dispose-per-call connection lifecycle instead of independently re-injecting a
          second IDbConnectionFactory.
          Multi-mapping QueryAsync<...> overloads (WO-051/P-321): thin wrappers over Dapper's own
          splitOn-based multi-mapping IDbConnection.QueryAsync<TFirst,TSecond,TReturn>(sql, map,
          param, splitOn: splitOn) — for join-projection queries. Only the two- and three-type arities
          are provided; additional arities (Dapper supports up to seven) may be added following the
          identical pattern if a future need arises.
          QueryMultipleAsync<TResult> (WO-051/P-321): wraps Dapper's grid-reader pattern
          (IDbConnection.QueryMultipleAsync) for reading multiple result sets from ONE round trip. The
          connection is opened, connection.QueryMultipleAsync(sql, parameters) produces a
          SqlMapper.GridReader, the caller-supplied readFunc is invoked against that reader WHILE the
          connection remains open (a GridReader streams sequential result sets over one open
          connection — it cannot be read after the connection closes), and only THEN is the reader
          then the connection disposed. Callers read each result set off the GridReader inside
          readFunc via its own .ReadAsync<T>()/.ReadSingleAsync<T>() calls.
```

---

## Implementation Rules

### Hard violations (never do these)

- `SharedKernel.Persistence.Abstractions` introducing **any ORM NuGet dependency** — it may only reference `SharedKernel.Primitives` and `SharedKernel.Domain`.
- Any interface or type in `.Abstractions` exposing `IQueryable<T>` to callers — all queries are expressed via `ISpecification<T>`.
- Calling `DbContext.SaveChanges[Async]` directly anywhere outside `EfUnitOfWork` — `IUnitOfWork.SaveChangesAsync` is the only permitted save boundary.
- Adding `OutboxMessage`, `IOutboxWriter`, or any outbox type to this domain — outbox belongs to `07.Messaging`.
- Adding an `OutboxInterceptor` to `SharedKernelDbContext` — MassTransit's `UseEntityFrameworkOutbox` registers its own interceptors at the `07.Messaging` composition layer.
- `AuditInterceptor` or `SoftDeleteInterceptor` calling property setters directly on aggregate instances — all field writes must go through `ChangeTracker.Entry(entity).CurrentValues[propertyName]`.
- `ConcurrencyInterceptor` swallowing non-concurrency exceptions — only `DbUpdateConcurrencyException` for `IHasConcurrency` entities is caught and rethrown.
- `SpecificationEvaluator<T>` applying paging (`Skip`/`Take`) before ordering — paging is always the final operation.
- String interpolation in any SQL inside `DapperReadService` subclasses — parameterized queries only (SQL injection risk).
- Using `Activator.CreateInstance` or reflection in `StronglyTypedIdValueConverter` — use the `implicit operator TValue` (a static method call).
- Using reflection in `SmartEnumTypeHandler` — use `SmartEnum<TEnum,TValue>.TryFromValue`.
- Adding messaging concerns (`IMessageBus`, `IEventPublisher`) to this domain — outbox message writes are the persistence boundary; dispatching belongs in `07.Messaging`.
- Adding domain logic to any type in this domain — this layer is pure data-access plumbing.
- Adding a second public constructor to `EfUnitOfWork` — exactly one constructor with `IDomainEventDispatcher?` as a nullable optional parameter is required; a second constructor creates DI ambiguity where the container silently picks the shorter one and skips the dispatcher.
- Injecting `Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction` directly in application layer handlers — explicit transaction control must go through `ITransactionalUnitOfWork.BeginTransactionAsync` which returns an `IPersistenceTransaction` handle; direct `IDbContextTransaction` injection bypasses the abstraction and couples application code to EF Core.
- Downcasting `ISpecificationEvaluator<T>` to `SpecificationEvaluator<T>` (or any concrete type) — `GetProjectedQuery` is now on the interface; any cast to the concrete type breaks alternative evaluator implementations at runtime.
- Inline `new NpgsqlConnection(connStr)` in Dapper services — `IDbConnectionFactory` is the only permitted connection source.
- Any static mutable state.
- Adding a project reference from `SharedKernel.Persistence.EfCore` to any `12.Security` package other than `SharedKernel.Security.Abstractions` — the single approved exception is `SharedKernel.Security.Abstractions` (zero-dependency interface library). All other `12.Security.*` packages are forbidden.
- Using `nameof(IEntity<TId>.Id)` in EF Core configurations — `IEntity<TId>` is a **marker interface** with no `Id` property; `Id` is declared on `Entity<TId>`. Use the string literal `"Id"` or `nameof(Entity<TId>.Id)` (requires a concrete TEntity constraint).
- Omitting the nested test-project exclusion items from production `.csproj` files — every production csproj that has a nested `*.Tests` subfolder must include: `<Compile Remove="*.Tests\**" />`, `<EmbeddedResource Remove="*.Tests\**" />`, `<None Remove="*.Tests\**" />`. Without these the SDK globs pick up test `.cs` files and the production build fails.
- Writing audit strings in any format other than `userId.ToString("D")` or `PersistenceServiceOptions.ServiceName` — the `"D"` lowercase hyphenated GUID format is the only permitted authenticated value; the unauthenticated fallback must come from options (never a hardcoded string literal other than the options default). `"N"`, `"B"`, `"P"`, `"X"` GUID formats are violations.
- Placing encryption attributes (any attribute whose name contains `Encrypt` or `Encrypted`) on domain entity classes — encryption is configured exclusively via `PropertyBuilder<T>.Encrypt()` inside `IEntityTypeConfiguration<TEntity>` implementations. Attributes on domain types create an infrastructure concern in the domain layer, violating DDD purity (SK0302).
- Instantiating `EncryptedValueConverter` directly inside `IEntityTypeConfiguration<TEntity>.Configure(builder)` and passing it to `.HasConversion(converter)` — `EncryptionModelConvention` applies the converter automatically after model finalization; manual instantiation produces duplicate or inconsistent converter registration (SK0304).
- Injecting `IEncryptionRotationJob` in any MediatR handler, domain service, application command/query handler, or any type in `03.Domain` or `05.Application` — key rotation is an infrastructure operation triggered via hosted service, Hangfire job, Temporal activity, or management endpoint only (SK0303).
- Using `AesGcm`, `Aes`, `SymmetricAlgorithm`, or any BCL symmetric cipher directly in `03.Domain` or `05.Application` layer types — all field-level encryption goes through `EncryptedValueConverter<T>` registered by the model convention (SK0301).
- Removing a key version from `EncryptionOptions.Keys` before completing the rotation of all rows that were encrypted with that version — doing so causes `EncryptionKeyNotFoundException` at query time for any row still carrying a ciphertext prefixed with the removed version.
- Using `GetMethod`, `MakeGenericMethod`, or `Invoke` in `TenantedDbContext.OnModelCreating` — the global tenant filter must be built using expression trees (`Expression.Parameter`, `Expression.Property`, `Expression.Equal`, `Expression.Lambda`) and applied via the non-generic `modelBuilder.Entity(clrType).HasQueryFilter(lambdaExpr)` overload.
- Storing `Guid.Empty` as a `TenantId` in production rows — `Guid.Empty` is the reserved no-tenant sentinel used by `NoOpTenantProvider`; any row with `TenantId == Guid.Empty` will be invisible to all tenanted queries.
- Calling `QueryableExtensions.IgnoreSoftDeleteFilter()` — this class has been deleted (P-080). Use `spec.IncludeDeleted = true` on the specification; `SpecificationEvaluator<T>` calls `.IgnoreQueryFilters()` automatically at step 0.
- Calling `IReadRepository.GetByIdAsync(id, ct)` — this method has been removed (P-080 breaking change). Use `readRepo.GetBySpecAsync(new ByIdSpecification<TAggregate, TId>(id), ct)` instead. `IRepository` (write side) still has `GetByIdAsync`.
- Calling `IDomainEventDispatcher.DispatchAsync` from anywhere other than `EfUnitOfWork.SaveChangesAsync` — domain event dispatch is triggered exclusively post-commit by `EfUnitOfWork`; dispatching from application layer is a responsibility violation (07.Messaging publishes integration events from dispatched domain events).
- Applying `.Select(spec.Selector)` before `Skip/Take` in the `SpecificationEvaluator` projection path — projection must always be the final step (step 8) after paging (step 7) to preserve the paging-last invariant.
- Calling `DispatchAndClearEventsAsync` inside `EfTransactionalUnitOfWork.SaveChangesAsync` when `DbContext.Database.CurrentTransaction` is non-null — dispatch must be deferred to `EfPersistenceTransaction.CommitAsync`; premature dispatch causes duplicate events when the flow is `BeginTransactionAsync → SaveChangesAsync → CommitAsync`.
- Using `EF.Property<TId>(e, "Id")` inside a `Contains` predicate for `GetByIdsAsync` — this approach may silently fall back to client-side evaluation when `TId` is a strongly-typed ID with a registered `ValueConverter`; use an expression-tree `Contains` lambda instead so the converter is applied at the property level by the LINQ provider.
- Registering consumer interceptors that fire before the platform three (Audit, SoftDelete, Concurrency) — platform interceptors are always composed first in `SharedKernelDbContext`; consumer interceptors added via `.AddInterceptor<T>()` are always appended after.
- Setting `AsNoTracking = true` on a specification passed to `IRepository.GetBySpecAsync` (write-side) — the entity returned will be detached and subsequent mutations will not be detected by change tracking, forcing a full-column UPDATE via `.Update()`; write-side specs must leave `AsNoTracking` unset.
- Expecting `IUnitOfWork.SaveChangesAsync`, the three platform interceptors, or domain event dispatch to run for rows affected by `IBulkMutationRepository.ExecuteUpdateAsync` or `ExecuteDeleteAsync` — both compile to a single server-side `ExecuteUpdate`/`ExecuteDelete` SQL statement that bypasses the `ChangeTracker` entirely. If audit fields, soft-delete flags, or domain events must be applied, do not use bulk mutation — load and save the aggregates normally.
- Calling `IBulkMutationRepository.ExecuteDeleteAsync` on an `ISoftDeletable` aggregate and expecting a soft delete — `ExecuteDeleteAsync` always issues a hard physical `DELETE`. Use `ExecuteUpdateAsync` with an explicit `setPropertyCalls` expression that sets the `IsDeleted`/`DeletedOn` columns if soft-delete semantics are required in bulk.
- Passing a specification with `Includes`, `StringIncludes`, `OrderBy`/`OrderByDescending`, `ThenBys`, `Skip`, or `Take` to `IBulkMutationRepository.ExecuteUpdateAsync`/`ExecuteDeleteAsync` — `BulkSpecificationGuard.Validate` throws `UnsupportedSpecificationException` for these shapes; only `Criteria` and `IncludeDeleted` are meaningful for a single bulk statement.
- Expecting `StreamAsync`/`StreamProjectedAsync<TResult>` to honor `spec.AsNoTracking == false` — both methods force `AsNoTracking()` unconditionally regardless of the specification's flag, to prevent unbounded `ChangeTracker` growth during long-lived enumeration. If tracked entities are required, use `ListAsync`/`ListPagedAsync` instead.
- Implementing `IHealthCheck` (Microsoft.Extensions.Diagnostics.HealthChecks) anywhere in `06.Persistence` — readiness probing is exposed as `DatabaseReadinessResult` plus `IDbConnectionFactory.CheckReadinessAsync`/`SharedKernelDbContext.CheckReadinessAsync` extension methods only; wrapping these in an `IHealthCheck` adapter is `13.ServiceDefaults`'s responsibility.
- Adding a project reference from any `06.Persistence` package to `02.Caching` (any `SharedKernel.Caching.*` package) to implement the `MigrationAndSeedHostedService` advisory lock — the lock uses the existing `IDbConnectionFactory` and PostgreSQL `pg_advisory_lock`/`pg_advisory_unlock`. Consumers wanting a stronger or cross-database lock may wrap their own startup logic with `SharedKernel.Caching.Redis.DistributedLocking` themselves; this domain does not take that dependency.
- Implementing `IDataSeeder<TContext>.SeedAsync` without an idempotency check (existence check or upsert) — `MigrationAndSeedHostedService` may invoke seeders on every application startup; non-idempotent seeders will duplicate data on redeploys.
- Calling `AesGcm`, `RandomNumberGenerator`, or any other `System.Security.Cryptography` symmetric-cipher type directly inside `EncryptedValueConverter` or anywhere else in `06.Persistence.EfCore` (P-227) — all field-level encryption cryptographic operations are delegated to the injected `SharedKernel.Cryptography.ISymmetricEncryptionService`; hand-rolling AES-GCM here duplicates `01.Core`'s own hard rule against hand-rolled symmetric encryption and was the exact defect P-227 corrected.
- Changing the on-disk ciphertext wire format (`"v{version}:{Base64(nonce||ciphertext||tag)}"`) for any reason, including the P-227 cryptography-delegation refactor — existing encrypted columns must continue to decrypt without a data migration; the wire format is owned exclusively by `EncryptedValueConverter`, never by `ISymmetricEncryptionService` or `EncryptedPayload`.
- Calling `06.Persistence`'s own `AddSharedKernelCryptography()` from inside this domain — `ISymmetricEncryptionService` registration is the CONSUMING SERVICE's responsibility (via `01.Core/SharedKernel.Cryptography`'s own DI extension); `EfCorePersistenceBuilder.WithEncryption()` only verifies the registration exists and fails fast with an actionable message if it does not.
- Constructing a second, independent `EfUnitOfWork`-equivalent instance to bridge `SharedKernel.Application.Behaviors.IUnitOfWork` (P-228) — `EfCorePersistenceBuilder.WithApplicationTransactionBehavior()` must register the SAME scoped `EfUnitOfWork` instance against both `IUnitOfWork` interfaces; two independent instances per scope would double-fire interceptors and domain event dispatch if both were ever resolved and called within the same logical operation.
- Registering `SharedKernel.Application.Behaviors.IUnitOfWork` unconditionally in `EfCorePersistenceBuilder.Build()` — the registration is opt-in via `.WithApplicationTransactionBehavior()` only; every `06.Persistence` consumer does not use `05.Application.Behaviors`' `TransactionBehavior`, and an unconditional registration would force an implicit, undocumented dependency surface on every consumer.
- Calling `EntityTypeConfigurationBase`'s concurrency-token step as `.IsRowVersion()` on PostgreSQL, or bypassing `EntityTypeConfigurationBase`/`XminConcurrencyTokenConvention` to hand-roll an alternative concurrency token — `.IsRowVersion()` alone is provably non-functional against a plain PostgreSQL `bytea` column (WO-051/P-315); the only genuinely-working mechanism is `.IsConcurrencyToken()` (EfCore) + `XminConcurrencyTokenConvention`'s `xmin` binding (PostgreSQL).
- Using `EF.Property<TId>(e, "Id")` for a primary-key equality predicate anywhere in this domain, including new code — the expression-tree `Expression.Parameter`/`Property`/`Equal`/`Lambda` pattern (`EfRepository.ExistsAsync`, and as of WO-051/P-316 `TenantedRepository`'s two `GetByIdForTenant*` methods) is the only permitted approach, for the same silent-client-eval-with-`ValueConverter`s reason P-105 already established.
- Passing a `KeysetSpecification<T,TKey>` to `IReadRepository.ListAsync`/`GetBySpecAsync`/`CountAsync`/`AnyAsync` and expecting the cursor to be honored — those overloads accept the base `ISpecification<T>` and know nothing about `AfterKey`/`AfterId`; they compile and run but silently ignore the cursor and always return the first page. `ListKeysetAsync<TKey>` is the only entry point that applies the seek predicate (WO-051/P-317).
- Calling `ITransactionalUnitOfWork.BeginTransactionAsync()` when a retrying execution strategy is configured (`Database.CreateExecutionStrategy().RetriesOnFailure == true`) — `EfTransactionalUnitOfWork` throws an actionable `InvalidOperationException` directing callers to `ExecuteInTransactionAsync` instead; do not catch and suppress this exception to force the old handle-based flow under retry — it is structurally incompatible with EF Core's retrying-execution-strategy contract (WO-051/P-320).
- Writing code inside an `ITransactionalUnitOfWork.ExecuteInTransactionAsync` operation delegate that is not safe to run more than once — the delegate re-executes on every retry attempt under a configured retrying execution strategy; non-idempotent side effects (e.g. calling an external, non-idempotent API) inside the delegate will replay on retry (WO-051/P-320).
- Placing a raw SQL parameter value, entity property value, or tenant/user identifier in an `Activity.SetTag(...)` call anywhere in this domain — `PersistenceActivitySource`'s spans carry only low-cardinality, non-sensitive metadata (aggregate type name, operation name, outcome, error type), mirroring the `cache.key_prefix`-never-full-key precedent from `02.Caching`'s P-304 (WO-051/P-319).
- Using a raw string literal at an `Activity.SetTag(...)` call site anywhere in this domain instead of `PersistenceTagKeys`/`01.Core`'s `WellKnownTagKeys` — enforced platform-wide by `SK0022` (WO-051/P-319).
- Adding a new constructor-captured scoped dependency (e.g. `IUserContext`, `ITenantProvider`, or any future per-request service) to `SharedKernelDbContext`/`TenantedDbContext` — or a consuming service's own subclass — without also threading it through the `RefreshUserContext`/`RefreshRequestContext` refresh-on-lease pattern. Under `.WithDbContextPooling()`, a constructor-only-captured scoped dependency freezes to whichever request first constructed that pooled instance and silently leaks into every later, unrelated request that reuses it (WO-051/P-322).
- `AuditInterceptor`/`SoftDeleteInterceptor` reading their own constructor-captured `IUserContext` field inside `SavingChanges`/`SavingChangesAsync` instead of `((SharedKernelDbContext)eventData.Context).CurrentUserContext` — the constructor-captured field is exactly the pooling-unsafe pattern WO-051/P-322 eliminated; `eventData.Context` is always the current executing instance (WO-051/P-322).
- Building `TenantedDbContext`'s global tenant filter via `Expression.Constant(specificProviderObject, ...)` instead of binding through `Expression.Constant(this, GetType())` → `.TenantProvider` → `.TenantId` — the former bakes a specific object reference into the (process-wide-cached) compiled model, permanently freezing every subsequent query to whichever `ITenantProvider` instance built the model first; the latter is rebound by EF Core to the CURRENT executing instance on every query (WO-051/P-322, corrects a confirmed pre-existing defect — see `TenantedDbContext`'s doc above).
- Combining `.WithDbContextPooling()` with `.WithDbContextFactory()` or with `.WithEncryption()` — both throw an actionable `InvalidOperationException` at `.Build()` time; catching and suppressing either guard to force the combination anyway reintroduces a conflicting `IDbContextFactory<TContext>` registration or the unproven `IEncryptionVersionOverride`-under-pooling staleness hazard, respectively (WO-051/P-322).
- Calling `Convert.FromBase64String` directly on an `EncryptionOptions.Keys` value inside `EncryptionOptionsKeyProvider` (or anywhere else resolving an encryption key) instead of going through `EncryptionKeyByteCache.GetOrDecode` — bypasses the decode-once-per-value cache this phase introduced (WO-051/P-323).
- Assuming `EfReadRepository.GetByIdsAsync` has a hard ~1000-ID parameter ceiling and pre-emptively chunking every call site — Npgsql translates the membership check to a single `= ANY(@array)` parameter with no such ceiling; use `GetByIdsChunkedAsync` deliberately (roughly above 50,000 IDs, to bound memory/payload), not reflexively (WO-051/P-323).
- String interpolation in the `sql` argument passed to `DapperReadService.QueryAsync<TFirst,TSecond,TReturn>`, `QueryAsync<TFirst,TSecond,TThird,TReturn>`, or `QueryMultipleAsync<TResult>` — the parameterized-queries-only rule applies identically to every `DapperReadService` method, new or existing (WO-051/P-321).
- Disposing the connection inside `DapperReadService.QueryMultipleAsync<TResult>` before `readFunc` completes reading the `SqlMapper.GridReader` — a `GridReader` streams sequential result sets over one open connection; closing the connection early corrupts or fails any subsequent `.ReadAsync<T>()` call inside `readFunc` (WO-051/P-321).
- Using `MigrationAndSeedHostedService`'s original `CancellationToken` for the advisory-lock RELEASE call inside the `finally` block — the release must unconditionally use `CancellationToken.None`; using the original (possibly-cancelled) token risks throwing `OperationCanceledException` and skipping the unlock, leaving the advisory lock held (WO-051/P-325).
- Calling a direct `ILogger.LogX(...)` extension method or hand-writing a `LoggerMessage.Define` delegate anywhere in this domain — every production log statement uses the `[LoggerMessage]` source-generated partial-method pattern with an explicit `EventId` inside this domain's reserved `6000-6099` sub-block (WO-053/P-333).
- Logging a key byte, a Base64-encoded encryption key string, or any column plaintext/ciphertext value from `EncryptionRotationService`'s batch-progress logs, or from any log statement anywhere in this domain — only counts and already-non-secret version-tag strings (e.g. `"v1"`) are permitted (WO-053/P-333).
- Passing a bare `configuration.GetSection("SharedKernel:Encryption")`/`"SharedKernel:Persistence"` string literal at any call site instead of `EncryptionOptions.SectionName`/`PersistenceServiceOptions.SectionName` — enforced platform-wide by `SK0022` (WO-053/P-334).
- Calling `IRestorableRepository<TAggregate,TId>.RestoreAsync` and expecting `IUnitOfWork.SaveChangesAsync` to be called automatically — like every other `EfRepository` write method, it only STAGES the mutation; a caller must still call `SaveChangesAsync` explicitly (WO-053/P-337).
- Calling `RestoreAsync` against a `TAggregate` that does not implement `ISoftDeletable` and expecting it to silently succeed — `EfRepository<TAggregate,TId>.RestoreAsync` throws `InvalidOperationException` naming the aggregate type, since restore has no meaning for a non-soft-deletable aggregate (WO-053/P-337).
- Placing command-timeout configuration on `UsePostgreSQL(...)` (`SharedKernel.Persistence.PostgreSQL`) — `DbContextOptionsBuilder.CommandTimeout(int?)` is a provider-neutral `Microsoft.EntityFrameworkCore.Relational` extension, already reachable from `EfCorePersistenceBuilder` (`SharedKernel.Persistence.EfCore`) without any Npgsql-specific call; use `.WithCommandTimeout(int)` there instead (WO-053/P-337).
- Routing an `IReadRepository<TAggregate,TId>` read to the configured replica connection while an EF Core transaction is active on the primary (`Database.CurrentTransaction != null`) — `IReadReplicaContextAccessor<TContext>.GetEffectiveContext` unconditionally returns the primary context in that case; bypassing or short-circuiting this check reintroduces a read-your-own-write consistency hazard the design specifically closes (WO-053/P-338).
- Caching or reusing the result of `IReadReplicaContextAccessor<TContext>.GetEffectiveContext` across multiple calls on the same `EfReadRepository` instance — it must be resolved fresh on every call, since transaction state can legitimately change between two read calls issued against the same injected repository (WO-053/P-338).
- Using `Type.GetMethod`/`MakeGenericMethod` to resolve `Pgvector.EntityFrameworkCore.VectorDbFunctionsExtensions.CosineDistance`/`.L2Distance`'s `MethodInfo` inside `VectorOrderingExpressions.ByDistance` — use the statically-typed delegate-cast `.Method` property (`((Func<Vector,Vector,double>)VectorDbFunctionsExtensions.CosineDistance).Method`), resolved entirely by the compiler via reference-type delegate contravariance against the real `(object,object)`-parameter method — never `Type.GetMethod`/`MakeGenericMethod` at runtime (WO-053/P-339).
- **(P-440/WO-066, shipped)** Configuring a `Money`-typed property via the generic `ValueObjectOwnershipBuilder` auto-owned scan instead of the dedicated `.OwnsMoney(...)` extension — `Money` is deliberately EXCLUDED from that scan precisely to prevent a silent, wrong-precision default `decimal` column from ever being applied to monetary data.
- **(P-440/WO-066, shipped)** Calling `.OwnsMoney(...)` without first calling `ModelConfigurationBuilderExtensions.ConfigureMoney()` from `ConfigureConventions()` — crashes model building (EF Core auto-discovers `Money`/`Currency` as phantom navigation/entity types before any per-property `.HasConversion()` call ever runs). Reconstructing a `Currency` value from a stored column via reflection (a located constructor + `Expression.New`, mirroring `StronglyTypedIdValueConverter`) instead of the public `Currency.Create(code)` factory — `Currency`/`Money` derive from `ValueObject`/`SingleValueObject<TValue>`, which already expose a public `Create` factory (03.Domain P-310); the reflection technique exists only for `StronglyTypedId<TValue>`, a different base hierarchy with no such factory.
- **(P-448/WO-068, shipped 2026-09-03, BREAKING)** Reintroducing a direct `IEncryptionKeyProvider` call — sync or `.GetAwaiter().GetResult()`-bridged — anywhere inside `EncryptedValueConverter`/`EncryptionModelConvention` or any other EF Core `ValueConverter`/model-finalizing convention in this package. The migrated design removes the dependency entirely rather than blocking on it; a future maintainer reaching for `IEncryptionKeyProvider` inside this package's synchronous pipeline has mis-diagnosed the problem — the fix is almost always to inspect `ISymmetricEncryptionService`'s `Result<T>.Error.Code` instead (see `EncryptedValueConverter`'s Decrypt path above).
- **(P-456/P-457, WO-071, shipped)** Any update or delete code path reaching an `AuditRecord` row — through `IAuditTrailWriter` (no such member exists on the interface), through `IRepository<AuditRecord,...>`/`IUnitOfWork` (this type is never routed through the general write-side aggregate pipeline), or through a raw `DbSet<AuditRecord>` call bypassing `AuditRecordImmutabilityInterceptor`. The interceptor is the load-bearing structural guarantee — an audit contract that is only "immutable by convention" is not meaningfully different from the mutable `AuditInterceptor` columns this capability exists to replace.
- **(P-456, WO-071, shipped)** Parsing, diffing, or otherwise inspecting `AuditRecord.BeforeSnapshot`/`AfterSnapshot` inside `SharedKernel.Persistence.Abstractions` or `SharedKernel.Persistence.EfCore` — these fields are OPAQUE, caller-pre-serialized values (mirrors `IIdempotencyResponseStore`'s "store persists what it's handed" pattern); this package's job is to store and hash-chain them, never to understand their content.
- **(P-456, WO-071, shipped)** Referencing `SharedKernel.Security.Abstractions.IUserContext` directly from `SharedKernel.Persistence.Abstractions` to resolve `IAuditActorContext`'s default behavior — the P-078 exception is scoped to `SharedKernel.Persistence.EfCore` only; `.Abstractions` gets a fresh local seam instead (see the Auditing contract block above).
- **(P-457, WO-071, shipped)** Making `AuditRecord` implement `IAggregateRoot<Guid>` so `EfAuditQueryService` can reuse `IReadRepository<AuditRecord,Guid>` — forces an always-empty `IHasDomainEvents` collection onto a plain infrastructure record for no benefit; build directly on the unconstrained `ISpecificationEvaluator<AuditRecord>` instead. Also: calling `ISpecificationEvaluator<AuditRecord>.GetQuery(...)` instead of `.GetKeysetQuery(...)` for `AuditResourceHistorySpecification`/`AuditActorActionsSpecification` — `GetQuery` silently ignores the cursor and always returns the first page.
- **(P-498/WO-081, SHIPPED)** Registering a hand-rolled `IEncryptionKeyProvider` for `.WithEncryption()`'s use that neither implements `ISynchronousEncryptionKeyProvider` (an honest, author-asserted, zero-I/O claim) NOR is wired via `.WithExternalEncryptionKeyProvider<TProvider>()` — this is caught by the D-132 startup fail-fast guard, but the correct fix is always to use one of the two sanctioned paths, never to bypass the builder to reach the ambient container directly.
- **(P-498/WO-081, SHIPPED)** Assuming `CachedEncryptionKeyProvider` (`01.Core`, bounded-TTL decorator) is sufficient, on its own, to make a KMS-backed `IEncryptionKeyProvider` safe for `EncryptedValueConverter`'s synchronous pipeline — it is NOT: `EncryptionKeyProviderCapabilities.IsGenuinelySynchronous` recursively unwraps to the true leaf provider, and a KMS provider can never honestly assert the marker regardless of cache warmth. `PreWarmedEncryptionKeyProvider` (D-129) — a DIFFERENT, `06.Persistence`-owned type — is what actually earns the marker, by never touching its wrapped provider from its own synchronous-contract members at all.
- **(P-498/WO-081, SHIPPED)** Deriving `EncryptedValueConverter`'s associated data from mutable row content (a primary key value, a timestamp, any per-row field) instead of the property's own stable table+column storage identity (or an explicit `.Encrypt(associatedDataOverride:)` string) — AAD must be reproducible at decrypt time from context available then, and a vanilla EF Core `ValueConverter` has no row-level access at all; deriving it from anything per-row is not merely a style preference, it does not compile against the available API surface.
- **(P-498/WO-081, SHIPPED)** Calling `PreWarmedEncryptionKeyProvider.WarmCurrentAsync`/`WarmVersionAsync` from anywhere other than `EncryptionKeyPreWarmingInterceptor`'s two hooks or `.WithExternalEncryptionKeyProvider<TProvider>()`'s one-time startup gate — in particular, never from inside `GetCurrentKeyAsync`/`GetKeyAsync` themselves (that would reintroduce the exact blocking-on-cache-miss hazard this type exists to structurally rule out) and never from a per-request/per-query code path (warming belongs on the two coarse-grained hooks, not scattered ad hoc).
- **(P-498/WO-081, SHIPPED)** Assuming a consuming service can resolve the ambient, unkeyed `SharedKernel.Cryptography.IEncryptionKeyProvider`/`ISymmetricEncryptionService` and expect it to be the SAME instance `.WithEncryption()` uses internally — as of this phase, `.WithEncryption()` never touches the unkeyed slot; its own instance is always resolved via a package-internal keyed-DI registration. This is the deliberate structural fix that makes the SEVERE F1 defect's DI-collision root cause unreachable.

### Specification evaluator ordering (canonical)

```text
-1. TagWith(spec.GetType().Name)  ← automatic, zero-configuration, first operation (WO-051/P-319)
0.  IgnoreQueryFilters()  ← only when spec.IncludeDeleted == true; before all other steps
1.  Criteria  (Where clause — null = no filter = all entities)
1b. Keyset seek predicate  ← GetKeysetQuery<TKey> ONLY; skipped on the first page (WO-051/P-317)
2.  Includes  (expression-based Include / ThenInclude)
2b. StringIncludes  (string-based Include paths — applied after expression includes, before ordering)
2c. AsSplitQuery()  ← only when spec.AsSplitQuery == true; applies to GetQuery/GetProjectedQuery/
    GetKeysetQuery<TKey> alike (WO-051/P-318)
3.  OrderBy / OrderByDescending  (primary sort)
4.  ThenBys  (secondary sorts — only when primary sort is set)
5.  Distinct
6.  AsNoTracking
7.  Skip / Take  ← ALWAYS LAST for the aggregate pipeline. DEVIATION: GetKeysetQuery<TKey> never applies
    Skip (always 0) and applies Take(spec.Take!.Value + 1) instead of spec.Take verbatim — the SECOND
    documented exception to "spec's own values are honored," alongside StreamAsync's forced AsNoTracking
    (WO-051/P-317)
8.  Select(spec.Selector)  ← projection overload only; applied after Skip/Take
```

Step 1b note: only meaningful for `GetKeysetQuery<TKey>` — `GetQuery`/`GetProjectedQuery` never apply it (a `KeysetSpecification<T,TKey>` passed to those methods silently returns the first page every time — see the "Passing a KeysetSpecification..." hard violation above).

Step 2b note: `spec.StringIncludes` contains paths like `"Orders.Items.Product"`. Each is applied via `IQueryable<T>.Include(string)`. Empty list is a no-op — existing specs are unaffected. Null/whitespace paths are rejected by `AddStringInclude` with `ArgumentException`.

Step 2c note: `spec.AsSplitQuery` defaults `false` — a single-query plan is never wrong, only potentially less efficient/prone to duplicate-row Cartesian-product artifacts when a spec declares 2+ collection `Includes`. Default behavior for every existing specification is byte-for-byte unchanged.

### Known limitation: `Guid.CreateVersion7()` same-millisecond tiebreak reliability (WO-071/P-457 finding)

**CONFIRMED EMPIRICALLY (200,000-trial experiment) while building `EfAuditTrailWriter`:** two `Guid.CreateVersion7()` values generated within the SAME millisecond do NOT reliably compare in their generation order — roughly 50% of same-millisecond pairs sort opposite to their true creation order, under BOTH `Guid.CompareTo()` and byte-lexicographic (RFC 4122 big-endian) comparison. Root cause: .NET's basic `Guid.CreateVersion7()` fills the sub-millisecond bits with cryptographically random data, not a monotonic counter — expected, spec-compliant behavior for a non-monotonic UUIDv7 implementation, but NOT a guarantee that two values minted moments apart in real wall-clock time will compare in that order.

**Platform-wide impact:** every `KeysetSpecification<T,TKey>` (P-308/WO-051) mandates `Id` as the secondary sort-key tiebreaker for rows sharing an identical primary sort-key value. Any consumer whose `Id` is a `Guid.CreateVersion7()` value AND whose primary sort key can tie at millisecond resolution (e.g. `AuditRecord.OccurredOn` under bursty/high-throughput writes) inherits this same-millisecond ordering unreliability. `EfAuditTrailWriter`'s own "find the latest record in this partition" lookup is one concrete instance — documented in its own XML remarks. The hash chain itself stays internally CONSISTENT even when this happens (every `PreviousRecordHash` still points to a real, correctly-hashed prior record — `VerifyChainIntegrityAsync` still reports it intact); only the wall-clock "latest" selection is approximate in this narrow window.

**Not fixed, out of scope for a single package:** closing this fully means not relying on `Guid` comparison for chronological tiebreaking anywhere two rows can tie at millisecond resolution — either a monotonic-counter UUID variant or an explicit strictly-increasing sequence column, a bigger design change than any one consumer of `KeysetSpecification<T,TKey>` can make locally. Recorded here so a future arch-planner session investigating keyset-tiebreak reliability does not have to rediscover this from scratch.

### Known limitation: unwarmed historical key versions in external-provider mode (P-498/WO-081, SHIPPED — a documented, permanent design trade-off, not a pending item)

`EncryptionKeyPreWarmingInterceptor`'s `ReaderExecutingAsync` hook is deliberately COARSE: it warms only the CURRENT encryption version before a query executes, since `01.Core`'s `IEncryptionKeyProvider` contract exposes no way to enumerate every version a KMS-backed provider might ever have minted. A row encrypted under an OLDER version that has never been explicitly warmed in this process (via `WarmVersionAsync`, called only from a rotation-aware caller) will surface as the existing, already-handled `EncryptionKeyNotFoundException` per-row failure path on decrypt — a correct, non-blocking, non-corrupting failure mode, but a genuine functional gap relative to the config-backed default (where every version in `EncryptionOptions.Keys` is always resolvable). This is an accepted, explicitly-documented trade-off, not an oversight: the alternative (proactively warming every historical version on every query) is not achievable against the current `IEncryptionKeyProvider` contract surface, and exhaustive pre-warming would defeat the entire point of not touching the KMS on the hot path. Services that need reliable historical-version decryption under active rotation in external-provider mode should explicitly call `WarmVersionAsync` for the versions they know are in play (e.g. from a rotation-aware background job) before relying on it.

### Interceptor-only save mutation rule

`AuditInterceptor` and `SoftDeleteInterceptor` must set field values exclusively via:

```csharp
context.Entry(entity).CurrentValues[nameof(IHasCreatedAudit.CreatedBy)] = userContext.UserId;
```

Never:

```csharp
((IHasCreatedAudit)entity).CreatedBy = userContext.UserId;  // ← VIOLATION
```

---

## DI Registration (expected shape)

```csharp
// Minimal EF Core wiring (single-tenant service)
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .Build();

// Multi-tenant EF Core wiring
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .WithMultiTenancy()   // TContext must extend TenantedDbContext or Build() throws
    .Build();

// With explicit transaction support (optional — only for services that need ITransactionalUnitOfWork)
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .WithTransactionalUnitOfWork()   // registers ITransactionalUnitOfWork → EfTransactionalUnitOfWork
    .Build();

// Consuming service overrides the IUserContext placeholder with its real implementation
// IUserContext is from SharedKernel.Security.Abstractions; UserId is Guid
services.AddScoped<IUserContext, OidcUserContext>();

// Multi-tenant service overrides the NoOpTenantProvider with its real implementation
// ITenantProvider is from SharedKernel.Security.Abstractions; TenantId is Guid (Guid.Empty = no tenant)
services.AddScoped<ITenantProvider, ClaimsTenantProvider>();

// Per-aggregate repository pair — one registration per aggregate in the consuming service
services.AddScoped<IRepository<Order, OrderId>, OrderEfRepository>();
services.AddScoped<IReadRepository<Order, OrderId>, OrderEfReadRepository>();

// With IDbContextFactory for background services (optional)
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .WithDbContextFactory()   // registers IDbContextFactory<OrderDbContext>
    .Build();

// With custom service-specific interceptor (optional — fires after platform three)
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .AddInterceptor<OrderAuditBridgeInterceptor>()
    .Build();

// With EF Core compiled model (optional — for AOT / cold-start performance)
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .WithCompiledModel(OrderDbContextModel.Instance)  // produced by dotnet ef dbcontext optimize
    .Build();

// PostgreSQL — shared NpgsqlDataSource and IDbConnectionFactory for Dapper
services.AddSharedKernelPostgreSQL(connectionString);

// Dapper — type handlers registered at startup; IDbConnectionFactory already registered above
services.AddSharedKernelDapper();

// With field-level encryption (optional — WO-019, cryptography delegated to 01.Core since P-227)
// Keys sourced from appsettings, environment variables, or Azure Key Vault mappings
// IMPORTANT (P-227): AddSharedKernelCryptography() MUST be called — WithEncryption() only verifies
// ISymmetricEncryptionService is resolvable and throws an actionable error if it is missing.
services.AddSharedKernelCryptography(configuration);   // 01.Core/SharedKernel.Cryptography
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .WithEncryption(enc =>
    {
        enc.Enabled = true;
        enc.CurrentVersion = "v1";
        enc.Keys["v1"] = "<Base64-encoded 32-byte key>";
        enc.Keys["v2"] = "<Base64-encoded 32-byte key>";   // add before rotating
    })
    .WithServiceName("order-service")   // replaces "system" in audit fallback
    .Build();

// Consuming service entity configuration (inside IEntityTypeConfiguration<Customer>.Configure):
//   builder.Property(x => x.Email).HasMaxLength(255).Encrypt().IsRequired();
//   builder.Property(x => x.PhoneNumber).HasMaxLength(20).Encrypt().IsRequired();

// Bridging 05.Application.Behaviors' TransactionBehavior to a real EfUnitOfWork (optional — P-228)
// Replaces the hand-written composition-root adapter previously documented in 05.Application/CLAUDE.md.
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .WithApplicationTransactionBehavior()   // registers EfUnitOfWork against SharedKernel.Application.Behaviors.IUnitOfWork too
    .Build();
// Application-layer registration (05.Application.Behaviors), unchanged:
//   services.AddSharedKernelApplicationBehaviors(cfg => cfg.AddTransactionBehavior());
// No manual adapter needed — TransactionBehavior resolves the same scoped EfUnitOfWork instance.

// Triggering key rotation — inject IEncryptionRotationJob in a Hangfire job / hosted service
//   await rotationJob.RotateAsync(fromVersion: "v1", toVersion: "v2", ct);

// With migrations-on-startup and data seeders (optional — P-151)
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .WithMigrationsOnStartup()           // runs Database.MigrateAsync via MigrationAndSeedHostedService
    .AddSeeder<ReferenceDataSeeder>()    // runs SeedAsync once per registered seeder, in order
    .AddSeeder<DefaultTenantSeeder>()
    .Build();
// ReferenceDataSeeder : IDataSeeder<OrderDbContext> — SeedAsync must check for existing rows
// before inserting (idempotency is a contract, not enforced by the framework).

// Bulk mutation — server-side ExecuteUpdate / ExecuteDelete (P-148)
// Bypasses SaveChangesAsync, the three platform interceptors, and domain event dispatch.
var updatedCount = await orderRepository.ExecuteUpdateAsync(
    new OrdersOlderThanSpecification(cutoffDate),
    setters => setters.SetProperty(o => o.Status, OrderStatus.Archived),
    ct);

var deletedCount = await draftOrderRepository.ExecuteDeleteAsync(
    new DraftOrdersSpecification(),
    ct);
// ExecuteDeleteAsync always issues a hard physical DELETE, even for ISoftDeletable aggregates.

// Streaming large result sets (P-149) — AsNoTracking forced unconditionally
await foreach (var order in orderReadRepository.StreamAsync(new AllOrdersSpecification(), ct))
{
    // process one Order at a time without materializing the full list
}

await foreach (var dto in orderReadRepository.StreamProjectedAsync(new OrderSummaryProjection(), ct))
{
    // process one OrderSummaryDto at a time
}

// Readiness probes (P-150) — consumed by 13.ServiceDefaults health check adapters
var dbContextReadiness = await dbContext.CheckReadinessAsync(ct);
var connectionFactoryReadiness = await connectionFactory.CheckReadinessAsync(ct);
// Both return DatabaseReadinessResult { IsHealthy, Latency, Provider, ErrorMessage } and never throw.

// Keyset (cursor) pagination (WO-051/P-317) — additive alongside ListPagedAsync
public sealed class OrdersByCreatedOnKeyset : KeysetSpecification<Order, DateTimeOffset>
{
    public OrdersByCreatedOnKeyset(DateTimeOffset? afterKey, object? afterId, int take)
        : base(o => o.CreatedOn, o => o.Id, afterKey, afterId, descending: false, take)
    {
    }
}

var page1 = await orderReadRepository.ListKeysetAsync(new OrdersByCreatedOnKeyset(null, null, take: 50), ct);
if (page1.HasMore)
{
    var page2 = await orderReadRepository.ListKeysetAsync(
        new OrdersByCreatedOnKeyset(page1.NextAfterKey, page1.NextAfterId, take: 50), ct);
}
// Passing OrdersByCreatedOnKeyset to ListAsync/GetBySpecAsync instead would silently ignore the cursor.

// AsSplitQuery (WO-051/P-318) — opt out of a single Cartesian-joined query for 2+ collection Includes
public sealed class OrderWithLineItemsAndPaymentsSpec : Specification<Order>
{
    public OrderWithLineItemsAndPaymentsSpec(OrderId id)
    {
        AddCriteria(o => o.Id == id);
        AddInclude(o => o.LineItems);   // collection #1
        AddInclude(o => o.Payments);    // collection #2 — Cartesian risk without AsSplitQuery
        ApplySplitQuery();
    }
}

// Transient-fault retry (WO-051/P-320) — opt-in, disabled by default; a REQUIRED two-call pair
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UsePostgreSQL(connectionString, maxRetryCount: 6))   // the only legal EnableRetryOnFailure call site
    .WithTransientFaultRetry(maxRetryCount: 6)                       // registers TransientFaultRetryOptions; framing only
    .WithTransactionalUnitOfWork()
    .Build();
// Held-open explicit transactions must migrate to ExecuteInTransactionAsync when retry is enabled —
// BeginTransactionAsync() throws an actionable InvalidOperationException under a retrying strategy:
await transactionalUnitOfWork.ExecuteInTransactionAsync(async ct =>
{
    await orderRepository.AddAsync(order, ct);
    await transactionalUnitOfWork.SaveChangesAsync(ct);
}, ct);
// The delegate may run more than once under retry — it must be safe to re-run.

// DbContext pooling (WO-051/P-322) — opt-in; REQUIRES TContext/its dependencies to never rely on
// constructor-only-captured scoped state without a RefreshXxx re-injection path (see the domain's
// own SharedKernelDbContext/TenantedDbContext, which already ship this correctly).
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UsePostgreSQL(connectionString))
    .WithDbContextPooling(poolSize: 1024)   // AddPooledDbContextFactory<TContext> + scoped refresh-on-lease wrapper
    .Build();
// Consumer code is UNCHANGED — inject OrderDbContext directly, exactly as with the default
// registration; pooling and the per-lease RefreshUserContext/RefreshRequestContext calls are
// transparent. NEVER combine with .WithDbContextFactory() or .WithEncryption() — both throw at Build().

// DapperReadService multi-mapping and QueryMultipleAsync (WO-051/P-321)
public sealed class OrderSummaryReadService : DapperReadService
{
    public OrderSummaryReadService(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public Task<IEnumerable<OrderWithCustomerDto>> GetOrdersWithCustomerAsync(CancellationToken ct) =>
        QueryAsync<OrderRow, CustomerRow, OrderWithCustomerDto>(
            sql: """
                 SELECT o.id, o.total, c.id, c.name
                 FROM orders o JOIN customers c ON c.id = o.customer_id
                 """,
            map: (order, customer) => new OrderWithCustomerDto(order, customer),
            splitOn: "id",
            ct: ct);

    public Task<OrderDashboardDto> GetDashboardAsync(CancellationToken ct) =>
        QueryMultipleAsync(
            sql: "SELECT COUNT(*) FROM orders; SELECT SUM(total) FROM orders WHERE status = 'Paid';",
            readFunc: async grid =>
            {
                var count = await grid.ReadSingleAsync<int>();
                var revenue = await grid.ReadSingleAsync<decimal>();
                return new OrderDashboardDto(count, revenue);
            },
            ct: ct);
}
// A subclass needing a capability this base class doesn't wrap (e.g. a bulk-copy operation) reuses
// the same connection-per-call lifecycle via the protected ConnectionFactory property directly.

// GetByIdsChunkedAsync (WO-051/P-323) — opt-in, does not change GetByIdsAsync's own behavior
var orders = await orderReadRepository.GetByIdsChunkedAsync(veryLargeIdBatch, chunkSize: 10_000, ct);
// For batches under roughly 50,000 IDs, prefer GetByIdsAsync directly — a single = ANY(@array) query.

// Encryption/service-name configuration-section binding (WO-053/P-334) — additive alongside the
// existing code-based Action<T>/direct-value paths; both may be composed under normal IOptions<T>
// later-registration-wins semantics
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .WithEncryption(configuration)                 // binds from configuration.GetSection(EncryptionOptions.SectionName)
    .WithServiceName(configuration)                // binds from configuration.GetSection(PersistenceServiceOptions.SectionName)
    .Build();
// appsettings.json:
//   "SharedKernel": { "Encryption": { "Enabled": true, "CurrentVersion": "v1", "Keys": { "v1": "<base64>" } },
//                     "Persistence": { "ServiceName": "order-service" } }

// Soft-delete restore (WO-053/P-337) — single entity, stages only; caller still calls SaveChangesAsync
var order = await orderRepository.GetBySpecAsync(new ByIdSpecification<Order, OrderId>(orderId), ct);
if (order is not null)
{
    await orderRepository.RestoreAsync(order, ct);
    await unitOfWork.SaveChangesAsync(ct);   // AuditInterceptor sets ModifiedBy/ModifiedOn as a normal Modified row
}

// Bulk restore (WO-053/P-337) — a pattern over the existing ExecuteUpdateAsync, not a new method;
// bypasses SaveChangesAsync/interceptors/domain events exactly like bulk soft-delete does
var restoredCount = await orderRepository.ExecuteUpdateAsync(
    new WithDeletedOrdersSpecification(),
    setters => setters
        .SetProperty(o => ((ISoftDeletable)o).IsDeleted, false)
        .SetProperty(o => ((ISoftDeletable)o).DeletedOn, (DateTimeOffset?)null)
        .SetProperty(o => ((ISoftDeletable)o).DeletedBy, (string?)null),
    ct);

// Command timeout (WO-053/P-337) — opt-in; omitting preserves today's provider-default timeout exactly
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .WithCommandTimeout(commandTimeoutSeconds: 30)
    .Build();

// Read-replica routing (WO-053/P-338) — opt-in; omitting leaves every read/write on the single
// primary connection, provably unchanged. Reads inside an active transaction are NEVER routed to
// the replica, even when this is configured.
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(primaryConnectionString))
    .WithReadReplica(options => options.UseNpgsql(replicaConnectionString))
    .Build();
// READ-AFTER-WRITE CONSISTENCY BECOMES THE CALLER'S RESPONSIBILITY ONCE ENABLED — a handler that
// writes then immediately reads via IReadRepository in the same logical operation may observe
// stale data under replication lag.

// pgvector nearest-neighbor query (WO-053/P-339) — composes normally with Criteria/paging
public sealed class NearestProductsSpecification : Specification<Product>
{
    public NearestProductsSpecification(Vector queryEmbedding, int topK)
    {
        AddCriteria(p => p.IsActive);
        ApplyOrderBy(VectorOrderingExpressions.ByDistance<Product>(
            p => p.Embedding, queryEmbedding, VectorDistanceMetric.Cosine));
        ApplyPaging(skip: 0, take: topK);
    }
}
var nearest = await productReadRepository.ListAsync(new NearestProductsSpecification(queryVector, topK: 10), ct);

// Money EF Core mapping (P-440/WO-066, SHIPPED). ConfigureMoney() is REQUIRED — see the Conversions/
// section above for why omitting it crashes model building.
public sealed class InvoiceDbContext : SharedKernelDbContext
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureMoney();   // registers CurrencyValueConverter/MoneyValueConverter globally
        base.ConfigureConventions(configurationBuilder);
    }
}

public sealed class InvoiceConfiguration : EntityTypeConfigurationBase<Invoice, InvoiceId>
{
    public override void Configure(EntityTypeBuilder<Invoice> builder)
    {
        base.Configure(builder);
        builder.OwnsMoney(x => x.Total);   // single packed "{amount}:{currencyCode}" column, HasMaxLength(40)
    }
}

// Append-only audit trail (P-456/P-457, WO-071, SHIPPED) — opt-in;
// omitting .WithAuditTrail() leaves IAuditTrailWriter/IAuditQueryService unregistered.
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .WithAuditTrail()   // registers EfAuditTrailWriter, EfAuditQueryService, AuditRecordImmutabilityInterceptor,
                         // AuditTrailFeatureMarker (singleton), and a default IAuditActorContext bridging the
                         // already-registered IUserContext/ITenantProvider
    .Build();

// OrderDbContext's OWN constructor must declare and forward AuditTrailFeatureMarker? — same pattern
// .WithEncryption() already requires for ISymmetricEncryptionService?/IEncryptionKeyProvider?. A raw bool
// flag cannot do this — DI cannot auto-resolve a primitive constructor parameter, only a registered type.
public sealed class OrderDbContext : SharedKernelDbContext
{
    public OrderDbContext(
        DbContextOptions<OrderDbContext> options,
        AuditInterceptor audit, SoftDeleteInterceptor softDelete, ConcurrencyInterceptor concurrency,
        IEnumerable<ISaveChangesInterceptor>? additionalInterceptors,
        AuditTrailFeatureMarker? auditTrailMarker)   // <-- REQUIRED for AuditRecord to join this context's model
        : base(options, audit, softDelete, concurrency, additionalInterceptors, auditTrailMarker: auditTrailMarker)
    { }
}

// A consuming service MAY override the default IAuditActorContext (last-registration-wins, mirroring
// the IUserContext/ITenantProvider placeholder-override pattern above) — optional, not required.
services.AddScoped<IAuditActorContext, MyCustomAuditActorContext>();

var record = await auditTrailWriter.RecordAsync(new AuditEntry
{
    Action = "CustomerLimitChanged",
    ResourceType = nameof(Customer),
    ResourceId = customer.Id.ToString(),
    BeforeSnapshot = JsonSerializer.Serialize(beforeState),
    AfterSnapshot = JsonSerializer.Serialize(afterState),
    ApprovalId = approvalRecord?.Id.ToString(),
}, ct);
```

`SharedKernel.Persistence.Abstractions` ships **no DI extensions** — it is a pure interface library.

---

## AOT Compatibility

- `IRepository<TAggregate, TId>`, `IReadRepository<TAggregate, TId>`, `IUnitOfWork`, `IDbConnectionFactory`, `ISpecificationEvaluator<T>` are interfaces — AOT-safe by definition.
- `SpecificationEvaluator<T>` applies `Expression<Func<T, bool>>` expression trees to `IQueryable<T>` — expression trees on `IQueryable` are AOT-safe when lambda bodies do not reference runtime-only reflection APIs.
- `StronglyTypedIdValueConverter<TStronglyTypedId, TValue>` uses the `implicit operator` (a static method) — no reflection, AOT-safe.
- `SmartEnumTypeHandler<TEnum,TValue>` uses `SmartEnum<TEnum,TValue>.TryFromValue` — no reflection in the hot path, AOT-safe.
- `AuditInterceptor` and `SoftDeleteInterceptor` access EF Core shadow properties by string key — shadow property access via `CurrentValues[name]` is AOT-safe (no reflection on CLR types).
- `TenantedDbContext.OnModelCreating` global filter is built with expression trees (`Expression.Parameter`, `Expression.Property`, `Expression.Equal`, `Expression.Lambda`) — no `GetMethod`/`MakeGenericMethod`/`Invoke` calls; fully AOT-safe.
- `ValueObjectOwnershipBuilder` scans entity types at model-build time — O(n×m) startup cost (n entity types, m properties per type); early-exit when no `IValueObject` properties found prevents unnecessary allocation; model-build time only, not a hot path. The `GetProperties(BindingFlags.Public | BindingFlags.Instance)` call at the `entityType.ClrType` usage site carries `[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]` to suppress IL2026/IL2075 trim warnings; this annotation is acceptable because the call is model-build time only.
- `EncryptedValueConverter` (P-227, delegation refactor) no longer calls `AesGcm`/`RandomNumberGenerator` directly — it delegates to the injected `ISymmetricEncryptionService` (`01.Core/SharedKernel.Cryptography`, itself BCL-only and AOT-compatible per `01.Core/CLAUDE.md`). The converter's own remaining responsibility (wire-format string parsing/packing, Base64 encode/decode, byte-slice concatenation) uses only BCL `string`/`Convert`/`Span<byte>` operations — no reflection. `IOptionsMonitor<EncryptionOptions>` and `IEncryptionVersionOverride` access are property reads on DI-managed instances. AOT-safe end-to-end.
- `EncryptionOptionsKeyProvider` (P-227) reads `IOptionsMonitor<EncryptionOptions>.CurrentValue` (property read) and performs a `Dictionary<string,string>` lookup plus `Convert.FromBase64String` — no reflection. Implements `SharedKernel.Cryptography.IEncryptionKeyProvider`, a pure interface. AOT-safe.
- `IEncryptionVersionOverride`/`EncryptionVersionOverride` (P-147) are a pure interface plus a tiny mutable-property class — BCL-only, zero reflection, AOT-safe. `EncryptionModelConvention` resolves the scoped instance via normal DI and passes it to `EncryptedValueConverter`'s constructor — a constructor call, not `Activator.CreateInstance`.
- `EncryptionModelConvention` scans `modelBuilder.Model.GetEntityTypes()` and `entityType.GetProperties()` at model-finalization time — model-build time only, not a hot path. The same `[DynamicallyAccessedMembers]` pattern applies if CLR property access is needed for annotation scanning; annotation access via EF Core's `IAnnotation` API is AOT-safe by design.
- `EncryptionRotationService` uses `IDbContextFactory<TContext>` which is AOT-compatible as of EF Core 8+. Batch processing is ordinary LINQ against the already-materialized EF model — no runtime-model scanning in the hot path.
- `SnakeCaseNamingConvention` operates on EF Core model metadata at model-building time — not in hot paths, AOT-safe.
- `JsonbEntityTypeBuilderExtension` and `VectorEntityTypeBuilderExtension` configure the EF model at startup — AOT-safe.
- `Microsoft.EntityFrameworkCore` — fully AOT-compatible as of .NET 8+ with compiled models; verify on each major upgrade.
- `Npgsql.EntityFrameworkCore.PostgreSQL` — verify AOT status on each major upgrade; abstraction boundary allows a provider swap.
- `Dapper` — uses reflection for parameter binding and result mapping. Known AOT limitation. All Dapper code is behind `DapperReadService` so the AOT boundary is contained to that class.
- `EfCorePersistenceBuilder` uses generic type constraints to validate `TContext` at compile time where possible; startup-time `InvalidOperationException` for the multi-tenancy mismatch guard is acceptable (DI composition is not AOT-critical path).
- `IPersistenceTransaction` and `ITransactionalUnitOfWork` are pure interfaces — AOT-safe by definition. Zero ORM dependencies in Abstractions.
- `EfTransactionalUnitOfWork` and `EfPersistenceTransaction` delegate to EF Core's `IDbContextTransaction` which is AOT-compatible as of EF Core 8+; verify on each major upgrade.
- `ByIdSpecification<TAggregate, TId>` uses `e => e.Id.Equals(id)` as an expression tree — AOT-safe on `IQueryable`.
- `SpecificationEvaluator<T>.GetProjectedQuery<TResult>` applies `.Select(spec.Selector)` where `Selector` is `Expression<Func<TAggregate, TResult>>` — expression trees on `IQueryable` are AOT-safe when the lambda body contains no runtime reflection APIs.
- `EfUnitOfWork` domain event dispatch: `ChangeTracker.Entries<IHasDomainEvents>()` is a generic EF Core API — AOT-safe. `IDomainEventDispatcher` is a pure interface; no reflection in the dispatch path.
- `EfUnitOfWork`'s dual-interface implementation (P-228) — declaring two interfaces on one class and satisfying both from a single method body is resolved entirely at compile time by the C# compiler; zero runtime type inspection, zero reflection. `EfCorePersistenceBuilder.WithApplicationTransactionBehavior()`'s registration (`services.AddScoped<SharedKernel.Application.Behaviors.IUnitOfWork>(sp => ...)`) is an ordinary DI factory delegate — AOT-safe.
- `EncryptionRotationService.LoadBatchAsync` (P-147, corrected for EF Core 10 — see "EF Core 10 API correction" above) is reflection-free in its hot path: each encrypted entity type has a closed-generic `EncryptedEntityBatchProcessor<TEntity>` (using ordinary `context.Set<TEntity>().Skip(skip).Take(take).ToListAsync(ct)`), looked up by `ClrType` from `EncryptedEntityBatchProcessorRegistry` — a plain `Dictionary<Type, IEncryptedEntityBatchProcessor>` lookup, no `GetMethod`/`MakeGenericMethod`/`Invoke` per call. The registry itself is populated once at startup (model-build time) — see the registry's NOTE for the documented, justified, startup-only `Activator.CreateInstance`/`MakeGenericType` exception (same class of exception as `ValueObjectOwnershipBuilder`). Hot-path AOT-safe; startup-time exception documented and isolated.
- `IBulkMutationRepository.ExecuteUpdateAsync` (P-148, corrected for EF Core 10) takes `Action<UpdateSettersBuilder<TAggregate>>` — a delegate consumed by EF Core's `ExecuteUpdateAsync` LINQ provider; the lambda body (`setters => setters.SetProperty(x => x.Prop, value)`) is translated by the provider via expression-tree analysis of the `SetProperty` calls performed against `UpdateSettersBuilder<T>`, not via runtime reflection. AOT-safe.
- `BulkSpecificationGuard.Validate<T>` (P-148) inspects `ISpecification<T>` properties (`Includes`, `StringIncludes`, `OrderBy`, etc.) via the interface's typed members — no reflection.
- `StreamAsync`/`StreamProjectedAsync<TResult>` (P-149) return BCL `IAsyncEnumerable<T>` via EF Core's `AsAsyncEnumerable()` — AOT-safe as of EF Core 8+. `[EnumeratorCancellation]` is a BCL attribute consumed by the compiler-generated async iterator, no reflection at runtime.
- `DatabaseReadinessResult` (P-150) is a BCL-only `sealed record` (`bool`, `TimeSpan`, `string`, `string?`) — AOT-safe by definition. `IDbConnectionFactory.CheckReadinessAsync` uses only `System.Data.IDbCommand.ExecuteScalar` and `System.Diagnostics.Stopwatch` — no reflection. `SharedKernelDbContext.CheckReadinessAsync` uses `Database.CanConnectAsync` and `Database.ProviderName` — both AOT-compatible EF Core APIs as of EF Core 8+.
- `IDataSeeder<TContext>` (P-151) is a pure generic interface — AOT-safe. `MigrationAndSeedHostedService<TContext>` uses `Database.MigrateAsync` (AOT-compatible EF Core 8+) and `IDbContextFactory<TContext>.CreateDbContextAsync` (AOT-compatible) — no reflection. The PostgreSQL advisory lock uses parameterized `pg_advisory_lock(hashtext(@lockKey))`/`pg_advisory_unlock` via `IDbConnectionFactory` and `IDbCommand` — no reflection.
- `XminRowVersionValueConverter` (WO-051/P-315) uses `System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian`/`WriteUInt32BigEndian` on `Span<byte>` — BCL, zero reflection, AOT-safe. `XminConcurrencyTokenConvention` scans `modelBuilder.Model.GetEntityTypes()` at model-finalization time (not a hot path) via EF Core's `IAnnotation`/metadata API — no CLR reflection.
- `TenantedRepository`'s corrected Id predicate (WO-051/P-316) uses the same `Expression.Parameter`/`Property`/`Equal`/`Lambda` construction already proven AOT-safe for `EfRepository.ExistsAsync` — expression trees on `IQueryable`, no runtime-only reflection APIs.
- `SpecificationEvaluator<T>.GetKeysetQuery<TKey>`'s seek-predicate builder (WO-051/P-317) is entirely `Expression<Func<T,bool>>` tree construction (`Expression.Parameter`/`GreaterThan`/`LessThan`/`Equal`/`OrElse`/`AndAlso`/`Constant`) plus a local `ParameterReplacer` `ExpressionVisitor` — no reflection, AOT-safe by the same reasoning already established for `AndSpecification<T>`'s composite predicate rebinding. `EfReadRepository.ListKeysetAsync<TKey>` compiles `spec.OrderBy`/`spec.ThenBys[0].KeySelector` via `.Compile()` once per call — the same accepted expression-compilation cost class as `Specification<T>.IsSatisfiedBy`.
- `SpecificationEvaluator<T>.GetQuery`/`GetProjectedQuery`/`GetKeysetQuery<TKey>`'s `.AsSplitQuery()` application (WO-051/P-318) is a plain `IQueryable<T>` extension-method call gated on a `bool` property read — no reflection, AOT-safe.
- `SpecificationEvaluator<T>`'s automatic `.TagWith(spec.GetType().Name)` (WO-051/P-319) uses `Type.Name` (a property read, not a reflection-heavy member enumeration) — AOT-safe, same class of usage already accepted for `DomainEventVersionHelper`/`StronglyTypedIdJsonConverterFactory` precedents in `03.Domain`.
- `PersistenceActivitySource`/`PersistenceTagKeys` (WO-051/P-319) are a `static readonly System.Diagnostics.ActivitySource` and a `static class` of `const string` fields — pure BCL, zero reflection, AOT-safe, identical reasoning to `02.Caching.FusionCache`'s own `ActivitySource` precedent.
- `TransientFaultRetryOptions` (WO-051/P-320) is a plain BCL record (`int`, `TimeSpan?`) — AOT-safe. `UsePostgreSQL(...)`'s retry parameters thread into Npgsql's `EnableRetryOnFailure` — an ordinary typed method call, no reflection. `EfTransactionalUnitOfWork.ExecuteInTransactionAsync`/the `BeginTransactionAsync` retry guard use `Database.CreateExecutionStrategy()`/`IExecutionStrategy.RetriesOnFailure`/`.ExecuteAsync(...)` — public, AOT-compatible EF Core 8+ APIs, no reflection.
- `DapperReadService`'s multi-mapping `QueryAsync<...>`/`QueryMultipleAsync<TResult>` (WO-051/P-321) call straight into Dapper's own `IDbConnection.QueryAsync<...>`/`QueryMultipleAsync` — the SAME known Dapper reflection limitation already documented for the base class's three original methods, contained behind the same `DapperReadService` AOT boundary; no NEW reflection surface is introduced. The promoted `ConnectionFactory` property is a plain property read — AOT-safe.
- `SharedKernelDbContext.RefreshUserContext`/`TenantedDbContext.RefreshRequestContext` (WO-051/P-322) are ordinary property setters — AOT-safe. `EfCorePersistenceBuilder.WithDbContextPooling(...)` calls `AddPooledDbContextFactory<TContext>` — a public, AOT-compatible EF Core 8+ API (same compatibility class as `AddDbContextFactory<TContext>`, already used by `.WithDbContextFactory()`) — plus an ordinary `services.AddScoped<TContext>(factory delegate)` registration, no reflection. The rebuilt `TenantedDbContext` tenant filter still uses only `Expression.Parameter`/`Constant`/`Property`/`Equal`/`Lambda` — the same AOT-safe expression-tree construction already established, with `Expression.Constant(this, GetType())` replacing `Expression.Constant(provider, typeof(ITenantProvider))`; `GetType()` is a virtual property read on `this`, not reflection over arbitrary members.
- `EncryptionKeyByteCache` (WO-051/P-323) is a `ConcurrentDictionary<string,byte[]>` behind a plain `GetOrDecode` method plus an `IOptionsMonitor<T>.OnChange` delegate subscription — pure BCL, zero reflection, AOT-safe. `GetByIdsChunkedAsync` (WO-051/P-323) is ordinary `IEnumerable<TId>` chunking (`Skip`/`Take` over the caller's sequence) plus repeated calls to the already-AOT-safe `GetByIdsAsync` — no new reflection surface.
- The `System.Data.Common.DbCommand` safe-cast async pattern (WO-051/P-325, `CheckReadinessAsync` and `MigrationAndSeedHostedService`'s advisory-lock calls) is a plain `is DbCommand` runtime type check followed by a virtual method call (`ExecuteScalarAsync`/`ExecuteNonQueryAsync`) — no reflection, AOT-safe.
- The new `[LoggerMessage]` partial methods across `ConcurrencyInterceptor`/`MigrationAndSeedHostedService`/`EncryptionRotationService` (WO-053/P-333) are source-generated at compile time — zero runtime reflection, the same AOT-safe pattern the root `CLAUDE.md`'s platform-wide Logging Conventions mandate everywhere. The internal `PersistenceRetryDiagnosticListener`'s `System.Diagnostics.DiagnosticListener` subscription reads `Microsoft.EntityFrameworkCore.Diagnostics.ExecutionStrategyEventData`'s public properties (`ExceptionsEncountered.Count`) via ordinary typed property access — no reflection.
- `EncryptionOptions`/`PersistenceServiceOptions`'s new `.WithEncryption(IConfiguration,...)`/`.WithServiceName(IConfiguration)` overloads (WO-053/P-334) call `OptionsBuilder<T>.Bind(IConfiguration)` — a public, AOT-compatible `Microsoft.Extensions.Options.ConfigurationExtensions` API (uses reflection internally for property binding, the same accepted class of Options-pattern binding cost every `IConfiguration`-bound POCO on the platform already carries; not a new AOT concern this domain introduces).
- `EfRepository<TAggregate,TId>.RestoreAsync` (WO-053/P-337) uses only the already-AOT-safe `ChangeTracker.Entry(entity).CurrentValues[propertyName]` shadow-property-style access (identical reasoning already established for `AuditInterceptor`/`SoftDeleteInterceptor`) plus a plain `is ISoftDeletable` runtime type check — no reflection. `EfCorePersistenceBuilder.WithCommandTimeout` (WO-053/P-337) is a plain method call on `DbContextOptionsBuilder` — AOT-safe.
- `EfCorePersistenceBuilder.WithReadReplica`/`IReadReplicaContextAccessor<TContext>` (WO-053/P-338) use `ActivatorUtilities.CreateInstance<TContext>(serviceProvider, replicaOptions)` — a `Microsoft.Extensions.DependencyInjection` API that DOES use reflection internally to match `TContext`'s constructor against DI-resolvable services, the same well-established ASP.NET Core "construct via DI with one overridden argument" idiom used platform-wide for typed-client/handler construction; invoked ONCE per DI scope (lazy, cached thereafter), never per-call — a startup/first-access-time cost, not a hot-path one, the same class of accepted exception already documented for `ValueObjectOwnershipBuilder`/`EncryptedEntityBatchProcessorRegistry`.
- `VectorOrderingExpressions.ByDistance<TAggregate>` (WO-053/P-339) builds an `Expression<Func<TAggregate,object>>` via `Expression.Call` referencing `Pgvector.EntityFrameworkCore.VectorExtensions.CosineDistance`/`.L2Distance`'s `MethodInfo` through a statically-typed delegate cast (`((Func<Vector,Vector,double>)VectorExtensions.CosineDistance).Method`) — resolved entirely by the C# compiler at compile time, zero `Type.GetMethod`/`MakeGenericMethod` at runtime. Expression trees on `IQueryable` are AOT-safe, the same reasoning already established for every other specification ordering expression on this platform.
- **(P-440/WO-066, shipped)** `CurrencyValueConverter` uses `Currency`'s `implicit operator string` (to-provider) and the public `Currency.Create(code)` factory (from-provider) — zero reflection, same class of AOT-safety as `StronglyTypedIdValueConverter`'s implicit-operator direction, but with NO reflection-located-constructor step at all on the from-provider side (an improvement, not merely parity). `MoneyValueConverter`/`.OwnsMoney` ship the D-106 packed-string FALLBACK, not the originally-preferred reflection-located-constructor owned-type path — that path was confirmed unreachable via any public EF Core 10 API (see the Conversions/ section above), so this package carries ZERO reflection for Money/Currency mapping, an improvement over even the fallback's own original framing.
- **(P-448/WO-068, shipped 2026-09-03, BREAKING)** `EncryptionOptionsKeyProvider`'s migrated `GetCurrentKeyAsync`/`GetKeyAsync` wrap the existing synchronous, reflection-free logic (`IOptionsMonitor.CurrentValue` property read, `Dictionary` lookup, `EncryptionKeyByteCache.GetOrDecode`) in `new ValueTask<T>(value)` — ordinary BCL async plumbing, zero reflection, AOT-safe. `EncryptedValueConverter`'s simplified `Decrypt` inspects `Result<byte[]>.Error?.Code` (a plain property read/equality comparison against `CryptographyErrorCodes.UnknownKeyId`) — no reflection, AOT-safe.
- **(P-456/P-457, WO-071, shipped)** `AuditRecord`/`AuditEntry`/`AuditChainVerificationResult` are plain `sealed record`s with BCL-typed (`Guid`/`string`/`DateTimeOffset`/`int`/`bool`) properties — AOT-safe by definition. `AuditResourceHistorySpecification`/`AuditActorActionsSpecification` build their `Criteria`/keyset key-selector expressions the SAME way every other `KeysetSpecification<T,TKey>` subclass on this platform already does — `Expression<Func<T,...>>` construction via ordinary C# lambda syntax, compiled by the C# compiler, no runtime reflection. **CORRECTION:** the internal `AuditRecordHasher` helper (shared by `EfAuditTrailWriter`/`EfAuditQueryService`, so write-time and verify-time hashing can never drift) calls `01.Core`'s `IContentHasher.ComputeHash(byte[])` — NOT a `ComputeHashHex` member, which does not exist on the real shipped `IContentHasher` — then hexes the digest itself via the plain BCL static `Convert.ToHexStringLower(byte[])`; both are reflection-free. `AuditRecordImmutabilityInterceptor` inspects `ChangeTracker.Entries<AuditRecord>()`'s `EntityState` — a generic, AOT-safe EF Core API, the same class already established for `EfUnitOfWork`'s domain-event dispatch. `Guid.CreateVersion7()` is a plain BCL static method — AOT-safe, though see the Implementation Rules/Known Limitations note on same-millisecond `Guid.CreateVersion7()` ordering below (a correctness, not AOT, caveat).

---

## Test Rules

- Unit tests for each package live in the nested `.Tests/` folder inside that package's folder.
- `SharedKernel.Persistence.Abstractions.Tests/` — interface contract shape; assertion that no outbox types exist in this package.
- `SharedKernel.Persistence.EfCore.Tests/` — interceptor integration tests with SQLite provider; `SpecificationEvaluator<T>` expression evaluation tests; `EfRepository` / `EfReadRepository` round-trips with real SQLite; `TenantedDbContext` isolation; `EfCorePersistenceBuilder` smoke tests. **Exception, `Auditing/` tests only (WO-071/P-457, shipped):** use `Microsoft.EntityFrameworkCore.InMemory` (test-only package, added to `Directory.Packages.props`/this project pinned 10.0.5), NOT SQLite — SQLite's EF Core provider refuses to translate `ORDER BY` on any `DateTimeOffset` expression at all (a LINQ-translation-level restriction, confirmed empirically, not row-count-dependent), which makes `AuditRecord`'s `OccurredOn`-ordered hash-chain/keyset queries completely untestable against SQLite. This mirrors the same class of SQLite limitation `KeysetTestAggregate` already works around by choosing a `long` sort key (not possible for `AuditRecord`, whose `OccurredOn` is fixed at `DateTimeOffset` by design D-113). Production targets PostgreSQL, which orders by `timestamptz` without issue.
- `SharedKernel.Persistence.PostgreSQL.Tests/` — integration tests with a real PostgreSQL Testcontainer; JSONB round-trip; vector column read/write; snake_case naming verification via `DbContext.Model`.
- `SharedKernel.Persistence.Dapper.Tests/` — type handler round-trip tests; `DapperReadService` query tests with a real PostgreSQL Testcontainer.
- `AuditInterceptor` tests: verify `CreatedBy`/`CreatedOn` set on Added entities; `ModifiedBy`/`ModifiedOn` set on Modified; no audit mutation on Deleted entities (SoftDeleteInterceptor handles those); `IsAuthenticated == false` → `CreatedBy` receives `"system"`; `IsAuthenticated == true` with valid `UserId` → `CreatedBy` receives the lowercase hyphenated GUID string (`ToString("D")` format).
- `SoftDeleteInterceptor` tests: verify Deleted state converted to Modified; `IsDeleted = true`; `DeletedOn` and `DeletedBy` set (same adapter rules as audit: `"system"` or GUID `"D"` format); soft-deleted records excluded by global query filter; non-soft-deletable entity passes through.
- `ConcurrencyInterceptor` tests: `DbUpdateConcurrencyException` is caught and rethrown as a typed `ConcurrencyException` carrying `Error.Conflict(...)`; non-concurrency exceptions are not swallowed.
- `SpecificationEvaluator<T>` tests: criteria, ordering (asc/desc), ThenBys, paging, distinct, AsNoTracking each verified independently; paging applied after ordering (determinism test); null Criteria matches all entities; ThenBys ignored without primary sort.
- `StronglyTypedIdValueConverter<TStronglyTypedId, TValue>` tests: round-trip (entity to DB value, DB value to entity) with a concrete strongly-typed ID.
- `SmartEnumTypeHandler<TEnum,TValue>` tests: SetValue writes the underlying `TValue`; Parse reads back the correct enum member; unknown value handled without reflection.
- `DapperReadService` tests: parameterized query returns correct result; `IDbConnectionFactory` called once per operation; connection disposed after each call.
- `EfRepository.ExistsAsync` tests: returns `true` for existing ID, `false` for missing ID; does not materialize the entity.
- `EfReadRepository.GetByIdsAsync` tests: partial match (some IDs missing — only matching returned); empty input → empty list; all IDs present → full list; result order is not asserted.
- `EfRepository.UpdateAsync` tests: tracked entity with one changed property — only that column is Modified; tracked entity with no mutations — no UPDATE statement issued; detached entity — full-column UPDATE issued (unchanged behavior).
- `TenantedDbContext` isolation tests: no-op `ITenantProvider` (returns `Guid.Empty`) → all queries return zero rows; real provider returning `tenantId = X` → only rows with `TenantId == X` returned; filter is applied at query execution time not startup.
- `ContractShapeTests` in Abstractions: verify `IRepository` has `ExistsAsync`; `IReadRepository` has `GetByIdsAsync` and `ListPagedProjectedAsync`; `IProjectionSpecification` exists in `Specifications/`, extends `ISpecification`, has `Selector` of correct expression type; `ISpecificationEvaluator<T>` declares `GetProjectedQuery`; `ITransactionalUnitOfWork` extends `IUnitOfWork` and declares `BeginTransactionAsync`; `IPersistenceTransaction` declares `CommitAsync` and `RollbackAsync`; no ORM assembly in package.
- **SQLite for EfCore tests** — no Testcontainers needed; SQLite covers all EF Core LINQ and interceptor behavior.
- **Testcontainers PostgreSQL** for PostgreSQL and Dapper tests — no mocked database connections. Import helpers from `16.Testing/SharedKernel.Testing`.
- **Standard test package set** (all test `.csproj` files): `xunit` 2.9.3, `xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.13.0, `coverlet.collector` 6.0.4, `FluentAssertions` 8.4.0. EfCore tests also add `Microsoft.EntityFrameworkCore.Sqlite` 10.0.5 and `NSubstitute` 5.3.0.
- **GlobalUsings.cs required** — every test project must include a `GlobalUsings.cs` containing `global using Xunit;`. `ImplicitUsings` does not auto-import xUnit attributes.
- `EfRepository` bulk write tests (P-080): `AddRangeAsync` — N rows written after `SaveChangesAsync`, zero rows before; `UpdateRangeAsync` — tracked entities: only changed columns Modified; detached entities: all columns Modified; `DeleteRangeAsync` — soft-deletable entities: all `IsDeleted = true`; non-soft-deletable: rows removed; SQLite provider.
- Projection read tests (P-080): `ListProjectedAsync<TResult>` — N aggregates project to N DTOs; paging applied before Select (verify via expression tree order); `GetBySpecProjectedAsync<TResult>` — match returns projected result, no-match returns null; SQLite.
- `ListPagedAsync` tests (P-080): count query strips Skip/Take; data query applies full spec; `PagedList` `TotalCount` is correct; empty result: `TotalCount == 0`; page beyond data: empty `Items`, correct `TotalCount`; `ContractShapeTests` verify method declared on `IReadRepository`.
- `EfUnitOfWork` domain event dispatch tests (P-080): dispatcher registered → events dispatched and cleared post-commit; no dispatcher → events cleared, no dispatch; double-dispatch prevention (events cleared before second `SaveChangesAsync`); dispatch failure does not roll back committed data (documented known trade-off).
- `IncludeDeleted` evaluator tests (P-080): `spec.IncludeDeleted == false` (default) → soft-deleted rows excluded; `spec.IncludeDeleted == true` → soft-deleted rows returned; `QueryableExtensions` class absent from assembly (reflection assertion).
- `ByIdSpecification` and `IReadRepository.GetByIdAsync` removal tests (P-080): `ByIdSpecification` `Criteria` matches only the given ID; `GetBySpecAsync(new ByIdSpecification<>(...))` returns correct aggregate; `ContractShapeTests` assert `IReadRepository` has no `GetByIdAsync` method; `IRepository` still has `GetByIdAsync`.
- `ListPagedProjectedAsync` tests (P-101): 10 aggregates → page 2 size 3 returns 3 DTOs, `TotalCount == 10`; empty set: `TotalCount == 0`, `Items == []`; page beyond data: empty items, correct `TotalCount`; SQLite provider.
- `ITransactionalUnitOfWork` tests (P-099): begin → add entity → commit → entity persisted; begin → add entity → rollback → entity NOT persisted; SQLite provider.
- `TenantedRepository.GetByIdForTenantIncludingDeletedAsync` tests (P-100): soft-delete entity; `GetByIdForTenantAsync` returns null; `GetByIdForTenantIncludingDeletedAsync` returns entity; SQLite provider.
- `AsNoTracking` behavioral tests (P-104): `ListAsync` with `AsNoTracking = true` → entities have `EntityState.Detached`; `ListAsync` without → entities tracked; SQLite provider.
- `EfTransactionalUnitOfWork` double-dispatch fix tests (P-105): (1) begin → `SaveChangesAsync` → `CommitAsync` → domain events dispatched exactly once (after commit); (2) `SaveChangesAsync` via `EfTransactionalUnitOfWork` without open transaction → dispatched once immediately; (3) begin → `SaveChangesAsync` → `RollbackAsync` → events NOT dispatched; domain events cleared in all paths; SQLite provider.
- `EfReadRepository.GetByIdsAsync` strongly-typed ID test (P-105): entity with `StronglyTypedId<Guid>` PK and registered `StronglyTypedIdValueConverter`; `GetByIdsAsync` returns correct entities; SQL log shows server-side `IN (...)` clause (no client-side evaluation log entry); all existing `GetByIdsAsync` behavioral tests continue to pass.
- `IRepository.GetBySpecAsync` write-side tests (P-106): spec matching by non-PK field returns tracked entity (mutation detected without explicit `.Update()`); spec matching nothing returns null; spec with `AsNoTracking = true` returns detached entity; `ContractShapeTests` verify `IRepository<T,TId>` declares `GetBySpecAsync`.
- `EfCorePersistenceBuilder.WithDbContextFactory()` smoke test (P-106): `IDbContextFactory<TContext>` resolvable when `.WithDbContextFactory()` called; not resolvable when omitted; factory-created context is operable (non-null, can execute a query); SQLite provider.
- Custom interceptor firing order test (P-106): platform three fire before consumer-supplied interceptors; two consumer interceptors fire in registration order after the platform three; SQLite provider.
- Compiled model passthrough smoke test (P-106): `.WithCompiledModel(model)` → resolved `DbContext` options contain the compiled model (reference equality or extensions check); no behavioral assertion beyond successful context creation.
- `SpecificationEvaluator<T>` string include tests (P-107): string include path applied → navigation property populated; expression include + string include both applied → both navigation properties populated; string includes applied after expression includes and before primary sort (ordering verification via query log or expression tree); `ContractShapeTests` verify `ISpecification<T>` declares `StringIncludes` property.
- PostgreSQL package integration tests (P-108) — all require real PostgreSQL Testcontainer: (1) `SnakeCaseNamingConvention` produces snake_case names in `DbContext.Model`; (2) JSONB round-trip: insert + fetch + verify deserialization; (3) pgvector round-trip: insert + fetch + verify dimensions; (4) `NpgsqlConnectionFactory.CreateConnectionAsync` returns open `NpgsqlConnection`; (5) `AddSharedKernelPostgreSQL` smoke: `IDbConnectionFactory` resolves as `NpgsqlConnectionFactory`.
- Dapper package integration tests (P-109) — all require real PostgreSQL Testcontainer: (1) `QueryAsync` returns correct rows from parameterized query; (2) `QuerySingleOrDefaultAsync` returns entity / null; (3) `ExecuteAsync` returns affected row count; (4) connection opened once and disposed per operation (no leaks); (5) `StronglyTypedIdTypeHandler` round-trip; (6) `SmartEnumTypeHandler` round-trip.
- `EncryptionRotationService.LoadBatchAsync` reflection-free regression tests (P-147, corrected for EF Core 10): rotation over a multi-entity-type model rotates rows for all encrypted entity types via `EncryptedEntityBatchProcessorRegistry` + `EncryptedEntityBatchProcessor<TEntity>.LoadBatchAsync` (`context.Set<TEntity>().Skip().Take().ToListAsync(ct)`); batch-boundary tests at exactly, one less than, and one more than `BatchSize`; existing rotation idempotency tests continue to pass unmodified; registry lookup is a `Dictionary<Type, IEncryptedEntityBatchProcessor>` hit for every registered encrypted entity type (no missing-type fallback needed since the registry is built from the same model enumeration as `DiscoverEncryptedEntityTypes`).
- `IEncryptionVersionOverride` rotation-scoped override tests (P-147): `RotateAsync("v1","v2")` while `CurrentVersion == "v1"` re-encrypts rotated rows with `v2`'s key; `EncryptionOptions.CurrentVersion` remains `"v1"` after rotation; a concurrent unrelated scoped `EncryptedValueConverter` continues encrypting with `CurrentVersion` (no cross-scope leakage of the override); a fresh scoped converter after rotation resolves `OverrideVersion == null`.
- Doc-drift correction verification (P-147): no `EncryptedValueConverter<T>`/`EncryptedValueConverter<string>` generic type exists in the `Encryption/` namespace; `EncryptionModelConvention` references the non-generic `EncryptedValueConverter`; existing converter round-trip tests continue to pass unmodified.
- `IBulkMutationRepository.ExecuteUpdateAsync`/`ExecuteDeleteAsync` integration tests — SQLite (P-148): `ExecuteUpdateAsync` with a criteria-only spec returns the matched row count and updates only matched rows; `ExecuteDeleteAsync` physically removes matched rows even when the aggregate implements `ISoftDeletable`; both bypass `AuditInterceptor` (no `ModifiedBy`/`ModifiedOn` updates unless explicitly set via the `Action<UpdateSettersBuilder<TAggregate>>` delegate) and domain event dispatch; `spec.IncludeDeleted == true` applies `IgnoreQueryFilters` so previously soft-deleted rows are also matched.
- `BulkSpecificationGuard`/`UnsupportedSpecificationException` unit tests (P-148): each of `Includes`, `StringIncludes`, `OrderBy`/`OrderByDescending`, `ThenBys`, `Skip`, `Take` set to a non-default value throws `UnsupportedSpecificationException` naming the offending property; a spec with only `Criteria` (plus `IncludeDeleted`/`IsDistinct`/`AsNoTracking`) passes; `ExecuteUpdateAsync`/`ExecuteDeleteAsync` surface the exception directly with no SQL issued.
- `IReadRepository.StreamAsync`/`StreamProjectedAsync<TResult>` integration tests — SQLite (P-149): streaming a spec matching N rows yields exactly N items via `IAsyncEnumerable<T>` regardless of `spec.AsNoTracking`, with `ChangeTracker.Entries().Count() == 0` after full enumeration; `StreamProjectedAsync<TResult>` matches `ListProjectedAsync` for the same spec; `Skip`/`Take` bounds the streamed count to the expected window; cancellation mid-enumeration stops further yields; `ContractShapeTests` confirm both methods are declared on `IReadRepository<TAggregate, TId>`.
- `DatabaseReadinessResult`/`CheckReadinessAsync` tests (P-150): `SharedKernelDbContext.CheckReadinessAsync` against a healthy in-memory SQLite context returns `IsHealthy == true` with non-negative `Latency` and non-empty `Provider`; against an invalid connection string returns `IsHealthy == false` with `ErrorMessage` populated, without throwing; `IDbConnectionFactory.CheckReadinessAsync` against a healthy PostgreSQL Testcontainer returns `IsHealthy == true`; against a factory that throws on `CreateConnectionAsync` returns `IsHealthy == false` without throwing; `ContractShapeTests` confirm `DatabaseReadinessResult` is a `sealed record` with exactly the four documented properties in `SharedKernel.Persistence.Abstractions`.
- `IDataSeeder<TContext>`/`MigrationAndSeedHostedService` integration tests — SQLite/Testcontainers (P-151): `.AddSeeder<TSeeder>()` registers the seeder as scoped and the hosted service; starting the host invokes `SeedAsync` exactly once per registered seeder in registration order; running the host twice does not duplicate rows when the seeder checks for existing data; `.WithMigrationsOnStartup()` against PostgreSQL applies all pending migrations before any seeder runs; omitting both options registers no hosted service; two concurrent host instances racing against the same PostgreSQL Testcontainer serialize via the advisory lock with no duplicate-key or migration-conflict errors.
- **`ManyServiceProvidersCreatedWarning` suppression in EfCore tests** — any test class that creates more than ~20 distinct `DbContextOptions` hashes across the entire test process triggers EF Core's internal `ServiceProviderCache` limit and throws `ManyServiceProvidersCreatedWarning` as an error. Suppress via `.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))` in EVERY `DbContextOptionsBuilder` setup across ALL test classes that use the same `DbContext` type. One unsuppressed call in a sibling test class is enough to exceed the limit.
- **Model cache isolation for encryption tests** — `EncryptionAwareModelCacheKeyFactory` keys the EF Core model cache on `(contextType, designTime, IEncryptionVersionOverride reference)` — the override is compared by reference. Tests that construct `MultiPropDbContext` (or any `SharedKernelDbContext` subclass using encryption) without an explicit `IEncryptionVersionOverride` all share the `EncryptionVersionOverride.NoOp` singleton, producing the same cache key. The first test builds the model with its `IOptionsMonitor<EncryptionOptions>` captured in the converter; subsequent tests with different monitors get the CACHED model. Fix: always pass `new EncryptionVersionOverride()` (a fresh instance per call) in each test's `DbContextOptionsBuilder` setup so every test gets a unique cache entry and its own `IOptionsMonitor` captured.
- **`EncryptionRotationService.RotateAsync` `fromVersion` semantics** — `fromVersion` does NOT filter which rows are processed. All rows with any encrypted property are unconditionally re-encrypted using `toVersion`'s key. `fromVersion` is retained for API stability and audit/logging purposes only. Tests must not assert that rows prefixed with a different version are skipped — the correct assertion is `RowsRotated == total row count` regardless of what `fromVersion` value is passed.
- `EncryptedValueConverter` wire-format regression tests (P-227): a ciphertext string produced by the OLD hand-rolled `AesGcm` implementation (fixture captured before the delegation refactor, same key material) decrypts correctly under the NEW `ISymmetricEncryptionService`-delegating converter — proves the wire format is byte-for-byte unchanged and no data migration is required for existing encrypted columns. Round-trip (encrypt then decrypt with the new converter), legacy-plaintext pass-through, and `Enabled == false` pass-through tests are re-run unmodified against the refactored converter.
- `EncryptionOptionsKeyProvider` tests (P-227, method names updated to the async shape by P-448/D-110): `GetCurrentKeyAsync()` precedence (`OverrideVersion ?? CurrentVersion`) matches the pre-P-227 `EncryptedValueConverter.Encrypt` precedence exactly; `GetKeyAsync(keyId)` returns `null` (not a throw) for an unknown version; hot-reload via `IOptionsMonitor.CurrentValue` is observed on the next call without re-resolving the provider from DI; both members return an already-completed `ValueTask` (`IsCompletedSuccessfully == true`) for this config-based provider (P-448/D-110).
- `EfUnitOfWork` dual-interface tests (P-228): resolving `SharedKernel.Persistence.Abstractions.IUnitOfWork` and `SharedKernel.Application.Behaviors.IUnitOfWork` from the same DI scope (after `.WithApplicationTransactionBehavior()`) returns the SAME instance by reference equality; `SaveChangesAsync` via either interface reference fires the identical interceptor chain and post-commit dispatch exactly once; omitting `.WithApplicationTransactionBehavior()` leaves `SharedKernel.Application.Behaviors.IUnitOfWork` unresolvable.
- `TransactionBehavior` end-to-end consumer-verify test (P-228): a full MediatR pipeline test host wiring `AddSharedKernelEfCore<TestDbContext>(...).WithApplicationTransactionBehavior().Build()` alongside `05.Application.Behaviors`' `AddTransactionBehavior()` — dispatching a test command persists the staged mutation exactly once after the handler returns; a thrown handler exception prevents any persistence (no partial commit). This test supersedes the documentation-only adapter example previously carried only in `05.Application/CLAUDE.md`.
- PostgreSQL concurrency-token tests (WO-051/P-315): a REAL PostgreSQL Testcontainers test — two `DbContext`s load the same `IHasConcurrency` row, both mutate different properties, the first `SaveChangesAsync` succeeds and `xmin` genuinely changes (verified by re-query), the second (stale `xmin`) throws `DbUpdateConcurrencyException` rethrown as `ConcurrencyException`/`Error.Conflict`; `XminRowVersionValueConverter` round-trip tests (boundary values `0`/`uint.MaxValue`); `XminConcurrencyTokenConvention` model-metadata test confirming `ColumnName == "xmin"`/`ColumnType == "xid"`/`ValueGenerated == ValueGenerated.OnAddOrUpdate` survives `SnakeCaseNamingConvention`; `ConcurrencyInterceptorTests.cs`'s two non-functional placeholder assertions replaced with a provider-neutral SQLite proof via a manually-forced stale `OriginalValues[nameof(RowVersion)]`.
- `TenantedRepository` strongly-typed-ID server-side translation test (WO-051/P-316): mirrors `GetByIdsAsyncStronglyTypedIdTests.cs` — a tenanted aggregate keyed by `StronglyTypedId<Guid>`; `GetByIdForTenantAsync`/`GetByIdForTenantIncludingDeletedAsync` return the correct entity with no client-side-evaluation log entry; existing tenant-isolation assertions continue to pass unmodified.
- Keyset pagination tests (WO-051/P-317): `GetKeysetQuery<TKey>` seek-predicate unit tests (first page applies no predicate; second page continues the sequence with no gap/overlap; `Descending` variant); `ListKeysetAsync<TKey>` full-walk correctness (repeatedly following `NextAfterKey`/`NextAfterId` visits every row exactly once, final page has `HasMore == false`); a correctness test under CONCURRENT INSERTS between page fetches proving no duplicated/skipped rows relative to an equivalent offset-paged scenario; strongly-typed-ID Id-tiebreaker server-side translation; `ContractShapeTests` for `ListKeysetAsync<TKey>`/`GetKeysetQuery<TKey>`/`KeysetPage<TAggregate,TKey>`; explicit regression proof that `ListPagedAsync`/`PagedSpecification<T>` remain unmodified.
- `AsSplitQuery` tests (WO-051/P-318): a spec with 2+ collection `Includes` and `AsSplitQuery = true` issues multiple SQL statements (captured via EF Core command logging) instead of one joined statement, with correct, duplicate-free materialized results, for both `GetQuery` and `GetProjectedQuery`; default (`AsSplitQuery = false`) behavior for existing specs is unchanged.
- Observability tests (WO-051/P-319): `TagWith` unit test confirming the generated SQL carries a comment with the originating spec's `GetType().Name`; `ActivityListener`-based tests for both a successful and a failing repository operation, asserting the expected `PersistenceTagKeys`/`WellKnownTagKeys.ErrorType` tags and `ActivityStatusCode`; a negative-assertion test confirming no span tag ever equals a raw SQL parameter, entity property, or tenant/user identifier from the test fixture.
- **Cross-test `ActivitySource` isolation for exact-count assertions (discovered during WO-051/P-319 implementation):** `PersistenceActivitySource.Source` is a single process-wide static, and xUnit runs different test CLASSES in parallel by default — `RepositoryTracingTests` and `StreamingRepositoryTests` both drive `TestAggregate.StreamAsync` through the same traced repository, so a bare `ActivityListener` filtering only on `OperationName` can observe a concurrently-running sibling test's span and inflate a count that must be EXACTLY one (e.g. proving `StreamAsync` produces one span per full enumeration, not one per item — a stronger claim than `02.Caching`'s own `OtelTracingTests`/`OtelMetricsTests` `>= 1` existence-style tolerance, which is insufficient here). Fix: start a local root `Activity` via the plain `System.Diagnostics.Activity` API (`using var rootActivity = new Activity("Test.Root").Start();` — no `ActivitySource`/listener needed for this to work, `Activity.Start()` alone sets `Activity.Current`) BEFORE registering the `ActivityListener`; `RepositoryTracing.StartActivity` calls `PersistenceActivitySource.Source.StartActivity(name, kind)` with no explicit parent, which defaults to `Activity.Current` — so every span this test's own call produces is a direct child of `rootActivity`. Filter observed activities to `activity.ParentId == rootActivity.Id` to isolate this test's own spans from any concurrently-running sibling test's spans of the identical operation name. Apply this pattern to any future exact-count (not just existence) `ActivityListener`-based assertion added to this domain's test suite.
- **SQLite and Npgsql both route store-generated-value modification commands through `ExecuteReader`, not `ExecuteNonQuery` (discovered during WO-051/P-317..P-323 Tests-phase implementation):** any `INSERT`/`UPDATE` statement needing a `RETURNING` clause to read back a database-generated value — e.g. any entity with an `IHasConcurrency`/`xmin`-bound `RowVersion` property, and (empirically observed) every plain SQLite `INSERT` in this codebase's fixtures — is executed by the provider via `DbCommandInterceptor.ReaderExecuting(Async)`, never `NonQueryExecuting(Async)`. Two concrete failure modes this causes: (1) a `DbCommandInterceptor`-based SQL-statement-COUNTING test (e.g. proving `AsSplitQuery` issues N statements, or `GetByIdsChunkedAsync` issues `ceil(N/chunkSize)` round trips) that seeds fixture rows earlier in the SAME context's lifetime will see its `ReaderExecuting` count inflated by every seed-phase INSERT — fix by snapshotting a baseline count immediately AFTER seeding completes and asserting only the DELTA the operation under test produces, never an absolute count; (2) a `DbCommandInterceptor`-based FAULT-INJECTION test that only overrides `NonQueryExecuting(Async)` (e.g. to simulate a transient `TimeoutException` for retry-strategy tests) can silently never fire if the target entity's INSERT/UPDATE actually executes via the reader path — override BOTH `NonQueryExecuting(Async)` and `ReaderExecuting(Async)`, sharing one counter/fault-trigger, so the interceptor is correct regardless of which path a given command takes. Never assume `ExecuteNonQuery` is used for writes in this domain's tests.
- **`IHasConcurrency`/PostgreSQL-provider model-metadata assertions do not require a live Testcontainer:** accessing `DbContext.Model` triggers EF Core's in-memory model build/finalization (including every `IModelFinalizingConvention` pass, e.g. `XminConcurrencyTokenConvention`) without ever opening a database connection — only executing an actual query needs real connectivity. A `DbContextOptionsBuilder<T>().UsePostgreSQL(anySyntacticallyValidConnectionString)` followed by `ctx.Model.FindEntityType(...).FindProperty(...)` is a fast, Testcontainer-free way to assert `ColumnName`/`ColumnType`/`ValueGenerated` on a PostgreSQL-specific mapping.
- **Decode-once/cache-hit proofs for internal caching classes need no counting seam added to production code:** reference-equality on the returned value is sufficient proof of exactly-once work when the underlying operation always allocates a fresh object (e.g. `Convert.FromBase64String` always returns a new `byte[]`) — a cache (such as `EncryptionKeyByteCache`) returning the SAME instance across repeated calls for the same key is direct evidence the expensive path ran only once. This domain's existing `InternalsVisibleTo` grant from `SharedKernel.Persistence.EfCore` to its own `.Tests` project already makes such internal classes directly constructible in tests — no seam, wrapper, or spy is needed.
- Transient-fault retry tests (WO-051/P-320): a PostgreSQL Testcontainers test injecting a fault type Npgsql's transient-fault classifier genuinely recognizes (not an arbitrary thrown exception) proves transparent retry when `UsePostgreSQL(..., maxRetryCount)` is configured, alongside a control proving a genuinely non-transient failure (e.g. a unique-constraint violation) still propagates immediately; `ExecuteInTransactionAsync` correctness (commit-on-success, rollback-and-no-dispatch-on-exception, dispatch-exactly-once-after-commit); `BeginTransactionAsync`'s retry guard throwing the platform's actionable exception under a configured retrying strategy while succeeding normally without one (with all existing `ITransactionalUnitOfWork` tests unmodified); a retry-under-failure test proving `ExecuteInTransactionAsync` produces no duplicate/partial commit across a retried attempt.
- `DapperReadService` multi-mapping/`QueryMultipleAsync` integration tests (WO-051/P-321) — all require a real PostgreSQL Testcontainer: (1) `QueryAsync<TFirst,TSecond,TReturn>` against a genuine two-table join returns correctly composed objects; (2) `QueryAsync<TFirst,TSecond,TThird,TReturn>` against a genuine three-table join; (3) `QueryMultipleAsync<TResult>` against a genuine multi-statement SQL batch reads two distinct result sets sequentially off one `GridReader`/connection; (4) a subclass calling `ConnectionFactory.CreateConnectionAsync` directly proves the same open-per-call/dispose-per-call lifecycle as the base class's own methods, with no leaked connection.
- `.WithDbContextPooling()` correctness tests (WO-051/P-322) — the GATING test: `poolSize = 1` (guaranteeing the same underlying instance is reused); Request A (scope 1, Tenant A/User A) inserts a row and disposes its scope; Request B (scope 2, Tenant B/User B) resolves `TContext` (the same pooled instance) and asserts it sees ONLY Tenant B's rows and correctly attributes `CreatedBy` to User B, never Tenant A/User A. A SEPARATE, equally-gating non-pooled regression proof: two sequential `TenantedDbContext` instances of the SAME concrete subclass (no pooling), each constructed with a DIFFERENT `ITenantProvider`, correctly isolate — proving the confirmed pre-existing model-cache staleness defect is fixed, not just the pooling-specific hazard. Every existing non-pooled `AuditInterceptor`/`SoftDeleteInterceptor`/`TenantedDbContext` isolation test continues to pass unmodified. `.WithDbContextPooling()` combined with `.WithDbContextFactory()` or `.WithEncryption()` throws an actionable `InvalidOperationException` at `.Build()`; pooling alone succeeds. An allocation comparison test (repeated resolve/dispose, pooled vs. default, `GC.GetAllocatedBytesForCurrentThread()` delta with generous tolerance) demonstrates the intended benefit without CI-timing flakiness.
- `EncryptionOptionsKeyProvider`/`EncryptionKeyByteCache` caching tests (WO-051/P-323, method names updated to the async shape by P-448/D-110): repeated `GetCurrentKeyAsync()`/`GetKeyAsync(version)` calls for the same version decode the underlying Base64 value exactly once; a simulated `IOptionsMonitor<EncryptionOptions>` reload (e.g. rotation adding a key) clears the cache and the next call reflects the updated `Keys` correctly; existing P-227 precedence tests continue to pass unmodified (async-signature updates aside).
- `GetByIdsChunkedAsync` tests (WO-051/P-323): `chunkSize < N` issues exactly `ceil(N/chunkSize)` round trips (query-count assertion) and returns the full, correct, duplicate-free set; `chunkSize >= N` issues exactly one round trip; empty input → empty list, zero round trips; every existing `GetByIdsAsync` test continues to pass completely unmodified (proving this phase changed only documentation, not `GetByIdsAsync`'s own behavior).
- Genuine-async readiness/advisory-lock tests (WO-051/P-325): a fake `DbConnection`/`DbCommand` pair whose SYNCHRONOUS `ExecuteScalar()`/`ExecuteNonQuery()` overrides THROW (failing the test loudly if the sync path is ever hit) and whose async overrides record invocation — `CheckReadinessAsync` and `MigrationAndSeedHostedService`'s acquire/release both complete successfully against this fake, proving the async overload is genuinely invoked, not merely present; a separate test pre-cancels the token before `StartAsync` reaches the `finally` block and asserts the release call still completes (proving `CancellationToken.None`, not the caller's token, is used on the release path); every existing `DatabaseReadinessResult`/`IDataSeeder`/`MigrationAndSeedHostedService` test continues to pass completely unmodified.
- **`SharedKernel.Persistence.PostgreSQL.Tests` and `SharedKernel.Persistence.Dapper.Tests` consolidate onto `16.Testing`'s `PostgreSqlContainerFixture` (WO-053/P-336)** — this domain's own Postgres integration tests now run against the SAME shared, canonical fixture every downstream consuming service is expected to standardize on, rather than a locally-drifted copy (dogfooding the platform's own recommendation). `PostgreSQL.Tests` shares ONE container instance across THREE of its four Postgres-touching classes (`ConcurrencyIntegrationTests`, `KeysetPaginationIntegrationTests`, `TransientFaultRetryIntegrationTests`) via `[CollectionDefinition("PostgreSQL")] : ICollectionFixture<PostgreSqlContainerFixture>` — completing the sharing those classes' pre-existing `[Collection("PostgreSQL")]` tags always implied but never wired up. **CORRECTED (implementation session, not "all four" as originally designed):** `PostgreSQLIntegrationTests` deliberately does NOT join this shared collection — its pgvector round-trip test requires the `pgvector/pgvector:pg16` image, and the shared fixture is permanently pinned to plain `postgres:16.4` (the pgvector extension binary is simply absent from a vanilla PostgreSQL image, so `CREATE EXTENSION vector` cannot succeed against it); it keeps its own dedicated, independently-managed container. `Dapper.Tests`' single Postgres-touching class uses `IClassFixture<PostgreSqlContainerFixture>` (no collection needed, it is the only consumer in that assembly). Neither test project's own `.csproj` needed a new `ProjectReference` — both already referenced `SharedKernel.Testing`; the now-redundant direct `Testcontainers.PostgreSql` `PackageReference` was removed from `Dapper.Tests.csproj` (no file there references that namespace directly anymore) but deliberately LEFT IN PLACE on `PostgreSQL.Tests.csproj`, since `PostgreSQLIntegrationTests.cs` still directly instantiates `PostgreSqlBuilder`/`PostgreSqlContainer` for its own dedicated container.
- **Two gotchas discovered only by actually implementing the WO-053/P-336 consolidation, not foreseeable from the design alone:** (1) EF Core's `Database.EnsureCreatedAsync()` is coarse-grained — it checks whether the TARGET DATABASE has ANY tables at all (the provider's `HasTables()` check), never specifically the calling model's own tables. Sharing the fixture's one literal database (`sharedkernel_test`) across multiple DIFFERENTLY-SHAPED `DbContext` models (as originally designed) would make every model AFTER the first silently skip its own schema creation — `EnsureCreatedAsync()` sees `HasTables() == true` already and no-ops, producing a "relation does not exist" failure the first time that model's table is queried. Fix: each of the three migrated classes targets its OWN uniquely-named database within the one shared container (via `new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = "sk_persistence_concurrency" /* etc. */ }.ConnectionString`) rather than the fixture's shared default database — `EnsureCreatedAsync()` can create a brand-new named database from scratch (Npgsql's create-database path connects to the "postgres" administrative database every vanilla PostgreSQL image always provisions), preserving genuine per-class schema isolation while still eliminating the redundant container starts. (2) `DROP TABLE` ordering becomes load-bearing the moment a database persists across xUnit test METHODS rather than being recreated fresh per method — `DapperReadServiceIntegrationTests`' own schema-setup script (kept in its `IAsyncLifetime.InitializeAsync`, now running against the ONE shared database via `IClassFixture<PostgreSqlContainerFixture>`) dropped a parent table (`dapper_customer`) before its FK-dependent children (`dapper_order`/`dapper_payment`) — harmless when each test method got a brand-new container, but the SECOND test method's `InitializeAsync` call failed with Npgsql `PostgresException` `2BP01` ("cannot drop table ... because other objects depend on it") once the child tables from the first run were still present. Always drop child (FK-holding) tables before parent tables in any schema-setup script that may run more than once against a persistent database.
- Structured logging tests (WO-053/P-333): `16.Testing`'s in-memory `ILogger`/`ILoggerFactory` test double is the assertion mechanism for every new `[LoggerMessage]` call site in this domain — never a hand-rolled `ILogger` mock, never an assertion on a rendered message string; assert by `EventId` and structured property value.
- **Cross-test `DiagnosticListener` contamination for `PersistenceRetryDiagnosticListener` exact-count assertions (discovered during WO-053/P-333 Tests-phase implementation, the SAME class of hazard as the `PersistenceActivitySource` isolation bullet above, one layer lower in the BCL):** `PersistenceRetryDiagnosticListener` subscribes to `System.Diagnostics.DiagnosticListener.AllListeners` — a single PROCESS-WIDE static — filtered only by listener name (`"Microsoft.EntityFrameworkCore"`) and event name (`CoreEventId.ExecutionStrategyRetrying.Name`), with no per-`DbConnection`/per-test correlation mechanism available on the diagnostic payload the way `Activity.ParentId` provides for tracing spans. A test proving `PersistenceRetryDiagnosticListenerTests` observes exactly N retry events was contaminated by `RetryExhaustionLoggingTests` (a different, concurrently-running xUnit test CLASS) forcing genuine EF Core retries via the identical `AlwaysRetryStrategyFactory`/`FaultInjectingInterceptor` fixture technique in the same process — every retry event either test's `DbContext` produced was visible to BOTH tests' listener instances simultaneously, inflating the count. Unlike the `Activity.ParentId`-based fix available for tracing spans, there is no equivalent parent-correlation field on `ExecutionStrategyEventData`/the diagnostic `KeyValuePair<string,object?>` payload to filter by. Fix: tag every test class that forces genuine EF Core execution-strategy retries into one shared xUnit collection (`[CollectionDefinition("RetryDiagnostics")]` + `[Collection("RetryDiagnostics")]` on each class) so xUnit runs them sequentially relative to each other, never concurrently — apply this collection tag to any FUTURE test class added to this domain that constructs its own retry-forcing `DbContext` fixture, not just the two that exist today.
- Configuration-binding tests (WO-053/P-334): an in-memory `Microsoft.Extensions.Configuration.ConfigurationBuilder` source (never a real `appsettings.json` file on disk) is sufficient to prove `.WithEncryption(IConfiguration,...)`/`.WithServiceName(IConfiguration)` bind correctly and that startup validation still fires against an invalid bound shape.
- Soft-delete restore tests (WO-053/P-337): SQLite is sufficient for the single-entity `RestoreAsync` path and its `ISoftDeletable` runtime guard (no PostgreSQL-specific behavior involved); bulk restore is proven the same way bulk soft-delete already is — a criteria-only spec plus an explicit `setPropertyCalls` delegate, no new production surface to test beyond the documented pattern itself.
- Command-timeout tests (WO-053/P-337) require a REAL PostgreSQL Testcontainer and a genuinely slow query (`pg_sleep(...)`) — SQLite has no meaningful command-timeout behavior to prove against.
- Read-replica routing tests (WO-053/P-338) require TWO independent PostgreSQL Testcontainers seeded with deliberately DIFFERENT marker rows, standing in for primary/replica — proving routing occurred means proving WHICH container's data was observed, not merely that a call succeeded.
- pgvector nearest-neighbor tests (WO-053/P-339) require a real PostgreSQL Testcontainer with the pgvector extension enabled and rows seeded at manually-computed KNOWN distances from a fixed query vector — asserting "some order" is not sufficient; the expected order must be independently computed and compared exactly, and captured SQL command text must confirm a server-side `<=>`/`<->` operator, never client-side evaluation.
- **Testing an `internal` `[LoggerMessage]` emitter from a SIBLING package's test project with no `InternalsVisibleTo` grant (WO-053 Tests-phase, T-100):** `PersistenceRetryDiagnosticListener` is `internal` to `SharedKernel.Persistence.EfCore`, which grants `InternalsVisibleTo` only to `SharedKernel.Persistence.EfCore.Tests` — `SharedKernel.Persistence.PostgreSQL.Tests` cannot reference the type by name to construct an `InMemoryLogger<PersistenceRetryDiagnosticListener>` directly (a compile error), yet T-100 requires proving its `6007`/`6008` logging against a REAL PostgreSQL Testcontainer, which only the PostgreSQL test project can provide. Resolved without widening `InternalsVisibleTo` and without referencing the internal type at all: register `16.Testing`'s `AddInMemoryLoggerFactory()` (which wires `ILoggerFactory` → `InMemoryLoggerFactory` plus the open-generic BCL `Logger<>` → `ILogger<>` mapping — the exact mechanism `AddLogging()` itself uses) into the same `ServiceCollection` as `AddSharedKernelEfCore<TContext>(...).WithTransientFaultRetry().Build()`; resolve the listener purely through its PUBLIC `IHostedService` surface (`provider.GetServices<IHostedService>()`, calling `StartAsync`/`StopAsync` manually since no full `IHost` is built) so the type name is never needed; read its captured records back via `InMemoryLoggerFactory.GetLogger(categoryName)` using the internal type's own `.FullName` as a STRING LITERAL (`"SharedKernel.Persistence.EfCore.Diagnostics.PersistenceRetryDiagnosticListener"`) — a category name is public information (it appears in real log output), so naming it as a string carries none of the accessibility restriction naming the `Type` itself would. Public types needing the same proof (e.g. `EfUnitOfWork`'s `6008` exhaustion log) can instead use `typeof(EfUnitOfWork).FullName!` directly. Reusable for any future cross-package internal-logger assertion in this domain.
- **`CREATE EXTENSION IF NOT EXISTS vector` must run on a THROWAWAY connection before the EF Core-managed Npgsql connection pool opens its first connection (WO-053 Tests-phase, T-118/T-119):** Npgsql resolves the `vector` PostgreSQL type's OID once per `NpgsqlDataSource`/connection-pool lifetime, at first connection open, using whatever is in `pg_type`/`pg_extension` at that moment. Issuing `CREATE EXTENSION IF NOT EXISTS vector` as the pool's own FIRST command (e.g. via `ctx.Database.ExecuteSqlRawAsync(...)` on a freshly-constructed `DbContext` whose options already call `UsePostgreSQL(...)`) opens that same pool's first connection before the extension exists, permanently poisoning its cached type mapping for the rest of that pool's lifetime — every subsequent `Pgvector.Vector`-typed parameter write then fails with `System.NotSupportedException: Cannot resolve 'vector' to a fully qualified datatype name`, even though the extension now genuinely exists in the database. Fix: run `CREATE EXTENSION IF NOT EXISTS vector` on a separate, throwaway `NpgsqlDataSourceBuilder(connectionString).Build()` connection FIRST, then construct the EF Core `DbContext`/pool — mirrors `PostgreSQLIntegrationTests`' own pre-existing raw-ADO.NET precedent, which happened to avoid this failure mode only because it never uses a `Vector`-typed ADO.NET parameter (it casts a string literal via `'[1,2,3]'::vector` instead).
- **(P-440/WO-066, shipped — PARTIAL coverage, gap flagged for the Tests phase)** Money round-trip tests must cover a REPRESENTATIVE set of minor-unit-digit cases, not just the two-decimal default — at minimum one zero-decimal currency (JPY), one two-decimal currency (USD/EUR), and one three-decimal currency (BHD). The Core-phase session shipped JPY (0-decimal) and USD/EUR (2-decimal) round-trip coverage (`MoneyValueConverterTests`) but did NOT add a 3-decimal-currency (BHD) case — an open gap for the Tests phase to close. **CORRECTED assertion, packed-string shape (D-106 fallback shipped, not the two-column design):** `Amount`/`Currency` are NOT independently queryable in SQL from the shipped single packed-string column — do not write a raw `WHERE`-shaped assertion expecting separate columns; assert the round-tripped `Money.Amount`/`Money.Currency` values in application code after materialization instead.
- **(P-448/WO-068, shipped 2026-09-03, BREAKING)** The full pre-existing `EncryptionDelegationTests`/`EncryptionKeyCachingTests`/`EncryptionModelConventionTests`/`EncryptionModelConventionExtendedTests`/`EncryptionRotationServiceTests`/`EncryptionRotationExtendedTests` suites were migrated to the new shape — observable behavior (which exception type surfaces for which failure) is unchanged; only signatures/call sites and, in `EncryptionDelegationTests`, the mechanism proving the unknown-key-id case (now a `Result<T>` error-code assertion after a real `Decrypt` call, rather than "Decrypt was never called") changed. `SharedKernel.Persistence.EfCore.Tests` itself cannot compile in-place this session (blocked on `16.Testing`/P-450, unrelated to this migration — see the domain summary note above), so the three genuinely-changed files (`EncryptedValueConverterTests`/`EncryptionDelegationTests`/`EncryptionKeyCachingTests`) were verified by an independent throwaway harness project referencing only the already-built `SharedKernel.Persistence.EfCore.dll` — **33/33 pass**, including one genuine test-fixture bug the harness caught and this session fixed (a too-short `"vv1:AAAA"` fixture payload that only worked under the OLD pre-check's early-exit; D-108 removing that pre-check means the converter's own length guard now runs first, so the fixture needed a properly-sized payload to actually reach `Decrypt` and prove the intended `Result.Error.Code` mapping). Re-run via the real project once P-450 ships.
- **(P-456/P-457, WO-071, shipped — PARTIAL coverage, gaps flagged for the Tests phase)** The `AuditRecordImmutabilityInterceptor` throw-on-Modified/Deleted test is the GATING test for this capability — shipped (`Interceptor_ThrowsAuditRecordImmutableException_OnUpdate`/`_OnDelete`) — an audit contract that is only "immutable by convention" (i.e., this test is skipped or weakened) is not meaningfully different from the mutable `AuditInterceptor` columns this capability exists to replace. Hash-chain tamper-detection tests must mutate a historical record bypassing the interceptor entirely (simulating a DBA-level bypass or a missing `REVOKE` grant). **CORRECTED mechanism, not raw SQL:** the shipped test suite uses the `Microsoft.EntityFrameworkCore.InMemory` provider (see Test Rules' provider note below), which does not support raw SQL execution at all — the shipped `VerifyChainIntegrityAsync_TamperedRecordHash_DetectedAsBroken` test instead opens a SECOND `AuditTestDbContext` against the same backing store, registered WITHOUT `AuditRecordImmutabilityInterceptor`, and mutates through it — the same "a different, non-compliant writer touched the data" scenario, achieving the identical proof (mutating through EF Core's own tracked change path with the guard PRESENT would just re-trigger it and prove nothing). A real Testcontainers/PostgreSQL suite MAY additionally use literal raw SQL for the same proof. Shipped: two different `(TenantId, ResourceType)` partitions proven to chain INDEPENDENTLY on write (`RecordAsync_DifferentResourceTypePartition_DoesNotChainAcrossPartitions`/`_DifferentTenantPartition_...`). NOT YET shipped, an open gap for the Tests phase: proving that BREAKING one partition's chain leaves `VerifyChainIntegrityAsync`'s result for an unrelated partition unaffected — the write-time independence tests above do not by themselves prove that.

---

## Changelog

> Maintained by the persistence domain agent. One line per significant change.

- [2026-06-01] Domain brain initialized — packages, interfaces, rules, AOT notes, test rules
- [2026-06-01] Major refresh: outbox types removed from Abstractions and EfCore scope (MassTransit EF outbox owns outbox at 07.Messaging); SharedKernelDbContext now registers exactly three interceptors (no OutboxInterceptor); ICurrentTenantService added to EfCore package; EfCorePersistenceBuilder fluent DI builder documented; IUserContext placeholder injection pattern documented; DI registration shape updated; test rules split by SQLite (EfCore) vs Testcontainers (PostgreSQL/Dapper); implementation rules reorganized with hard-violation list; WO-008 (P-033) and WO-013 (P-065 through P-074) phases reflected
- [2026-06-01] SK.06.Scaffold complete — EfCore package versions pinned (DI.Abstractions 10.0.5); nested test exclusion pattern and IEntity marker-only rule added to Implementation Rules; standard test package set and GlobalUsings.cs requirement added to Test Rules (sync-brain)
- [2026-06-02] WO-016 (P-091, P-092, P-093, P-094) and WO-014 (P-078, P-079, P-082) planned — audit string adapter rule added (userId.ToString("D") or "system"); Guid.Empty no-tenant sentinel documented; IRepository/IReadRepository extended with ExistsAsync/GetByIdsAsync; IProjectionSpecification added to Abstractions; IDbConnectionFactory doc restriction removed; EfRepository.UpdateAsync tracking optimization documented; TenantedDbContext reflection-elimination and ValueObjectOwnershipBuilder early-exit documented; deliberate layering exception for Security.Abstractions reference recorded; ICurrentTenantService removed in favour of ITenantProvider from Security.Abstractions (persistence-arch-planner)
- [2026-06-02] WO-014 (P-080) planned — bulk write methods (AddRangeAsync/UpdateRangeAsync/DeleteRangeAsync) added to IRepository and EfRepository; projection reads (ListProjectedAsync/GetBySpecProjectedAsync) added to IReadRepository and EfReadRepository; ListPagedAsync added returning `PagedList<T>` (Abstractions gains SharedKernel.Contracts reference); IDomainEventDispatcher optional hook added to EfUnitOfWork (post-commit dispatch + event clear); QueryableExtensions.IgnoreSoftDeleteFilter deleted — replaced by spec.IncludeDeleted flag handled by SpecificationEvaluator (step 0 IgnoreQueryFilters); IReadRepository.GetByIdAsync removed (breaking change) — `ByIdSpecification<TAggregate,TId>` added as canonical replacement; SpecificationEvaluator pipeline updated with step 0 and step 8; all hard violations, AOT notes, test rules, and interface contracts updated (persistence-arch-planner)
- [2026-06-02] SK.06.Scaffold S-06 and S-07 complete — EfCore→Security.Abstractions and Abstractions→Contracts project references added to csproj files; both already documented; no CLAUDE.md content changes needed (sync-brain)
- [2026-06-03] WO-017: GetProjectedQuery promoted to `ISpecificationEvaluator<T>`; ITransactionalUnitOfWork/IPersistenceTransaction added to Abstractions; EfTransactionalUnitOfWork/EfCorePersistenceBuilder.WithTransactionalUnitOfWork added to EfCore; ListPagedProjectedAsync added to IReadRepository/EfReadRepository; ValueObjectOwnershipConvention renamed to ValueObjectOwnershipBuilder with static-utility note; TenantedRepository.GetByIdForTenantAsync soft-delete-preserving semantics documented + GetByIdForTenantIncludingDeletedAsync added; EfUnitOfWork single-constructor hard rule added; three new hard violations (downcast, second constructor, direct IDbContextTransaction injection) (sync-brain)
- [2026-06-03] WO-018 (P-105..P-109) planned: EfTransactionalUnitOfWork double-dispatch fix documented (dispatch deferred to EfPersistenceTransaction.CommitAsync when CurrentTransaction active); EfReadRepository.GetByIdsAsync expression-tree Contains fix documented (eliminates silent client-side evaluation with strongly-typed ID converters); ValueObjectOwnershipBuilder DynamicallyAccessedMembers annotation documented in AOT notes; IRepository.GetBySpecAsync added (write-side tracked fetch by spec); EfCorePersistenceBuilder gains `WithDbContextFactory()`, `AddInterceptor<T>()`, `WithCompiledModel(IModel)`; SharedKernelDbContext constructor extended for additional interceptors; SpecificationEvaluator canonical pipeline updated with step 2b (StringIncludes — between expression includes and OrderBy); PostgreSQL package fully documented (SnakeCaseNamingConvention, JSONB, pgvector, NpgsqlConnectionFactory, AddSharedKernelPostgreSQL); Dapper package fully documented (StronglyTypedIdTypeHandler, SmartEnumTypeHandler, DapperTypeHandlers, DapperReadService, AddSharedKernelDapper); five new hard violations; DI registration examples expanded; test rules updated with nine new test scenarios (persistence-arch-planner)
- [2026-06-04] WO-019 (P-111..P-113) planned: field-level AES-256-GCM encryption subsystem documented — EncryptedValueConverter<string>, EncryptionOptions, PersistenceServiceOptions, EncryptionModelConvention (IModelFinalizingConvention), PropertyBuilder.Encrypt() annotation extension, IEncryptionRotationJob/EncryptionRotationService, EncryptionKeyNotFoundException; audit fallback hardcoded "system" replaced by configurable PersistenceServiceOptions.ServiceName; EfCorePersistenceBuilder gains .WithEncryption() and .WithServiceName(); SharedKernelDbContext gains optional IOptionsMonitor<EncryptionOptions> constructor parameter; six new hard violations; AOT notes for AesGcm/EncryptionModelConvention/EncryptionRotationService; DI registration example updated; test rules added for converter round-trip, tamper detection, hot-reload, legacy plaintext path, convention integration, rotation idempotency (arch-lead)
- [2026-06-12] WO-024 (P-147..P-151) planned: doc-drift corrected — `EncryptedValueConverter<string>`/`EncryptedValueConverter<T>` (nonexistent generic) replaced everywhere with the actual non-generic `EncryptedValueConverter : ValueConverter<string, string>`; new `IEncryptionVersionOverride`/`EncryptionVersionOverride` scoped seam documented (mutable `OverrideVersion`, resolved by `EncryptionModelConvention`, set/reset by `EncryptionRotationService.RotateAsync` around `SaveChangesAsync` without mutating `EncryptionOptions.CurrentVersion`); `EncryptionRotationService.LoadBatchAsync` documented as reflection-free via non-generic `Set(Type)`/`Skip`/`Take`/`ToListAsync(IQueryable, ct)`; new `IBulkMutationRepository<TAggregate, TId>` (EfCore-only — `SetPropertyCalls<TAggregate>` is an EF Core type) with `ExecuteUpdateAsync`/`ExecuteDeleteAsync`, implemented directly by `EfRepository<TAggregate, TId>`, both bypassing `SaveChangesAsync`/platform interceptors/domain event dispatch, `ExecuteDeleteAsync` always a hard physical DELETE even for `ISoftDeletable`; new `BulkSpecificationGuard`/`UnsupportedSpecificationException` restricting bulk specs to `Criteria`/`IncludeDeleted` only; `IReadRepository`/`EfReadRepository` gain `StreamAsync`/`StreamProjectedAsync<TResult>` returning `IAsyncEnumerable<T>` with AsNoTracking forced unconditionally (the one documented exception to the spec's AsNoTracking flag) and Skip/Take honored as a row-window; new `DatabaseReadinessResult` sealed record plus `IDbConnectionFactory.CheckReadinessAsync` (Abstractions) and `SharedKernelDbContext.CheckReadinessAsync` (EfCore) readiness probes, both BCL-only and never-throwing, with an explicit no-`IHealthCheck`-in-this-domain rule (that's `13.ServiceDefaults`'s job); new `IDataSeeder<TContext>` plus opt-in `EfCorePersistenceBuilder.WithMigrationsOnStartup()`/`.AddSeeder<TSeeder>()` registering `MigrationAndSeedHostedService<TContext>` which runs a PostgreSQL advisory lock (via existing `IDbConnectionFactory`, no new `02.Caching` reference), `Database.MigrateAsync`, and ordered seeders; ten new hard violations; AOT notes for the reflection-free rotation path, `IEncryptionVersionOverride`, `SetPropertyCalls<T>` expression trees, `IAsyncEnumerable<T>` streaming, BCL-only readiness probes, and `IDataSeeder`/`MigrationAndSeedHostedService`; eight new/expanded test-rule scenarios; four new DI registration examples (seeders/migrations, bulk mutation, streaming, readiness probes) (persistence-arch-planner)
- [2026-06-16] SK.06.Tests T-40..T-54 complete — three new test patterns added to Test Rules: (1) `ManyServiceProvidersCreatedWarning` suppression via `ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))` on every `DbContextOptionsBuilder` in every test class sharing the same DbContext type; (2) model cache isolation for encryption tests — `EncryptionAwareModelCacheKeyFactory` keys on the `IEncryptionVersionOverride` reference, so tests using different `IOptionsMonitor` instances but the same singleton override share a cached model and converter — fix by passing `new EncryptionVersionOverride()` per test; (3) `EncryptionRotationService.RotateAsync` `fromVersion` does NOT filter rows — all rows unconditionally re-encrypted with `toVersion` (sync-brain)
- [2026-06-15] SK.06.Design D-54..D-58 closeout — two EF Core 10.0.5 API-compatibility corrections verified by compiling sample code against the real package: (1) D-54/P-147 — `DbContext.Set(Type entityType)` non-generic overload does NOT exist in EF Core 10 (only `Set<TEntity>()`/`Set<TEntity>(string)`), and `EntityFrameworkQueryableExtensions.ToListAsync` has no non-generic `IQueryable` overload; `EncryptionRotationService.LoadBatchAsync`'s reflection-free design is corrected to a closed-generic `IEncryptedEntityBatchProcessor`/`EncryptedEntityBatchProcessor<TEntity>` per encrypted entity type, dispatched via a `EncryptedEntityBatchProcessorRegistry` (`Dictionary<Type, IEncryptedEntityBatchProcessor>`) populated once at startup from `context.Model.GetEntityTypes()` — hot path is a dictionary lookup + ordinary `context.Set<TEntity>().Skip().Take().ToListAsync(ct)`, with one documented startup-time `MakeGenericType`/`Activator.CreateInstance` exception (same class as `ValueObjectOwnershipBuilder`); (2) D-55/P-148 — `SetPropertyCalls<T>` no longer exists in EF Core 10; `IBulkMutationRepository<TAggregate,TId>.ExecuteUpdateAsync` signature corrected from `Expression<Func<SetPropertyCalls<TAggregate>, SetPropertyCalls<TAggregate>>>` to `Action<UpdateSettersBuilder<TAggregate>>` (the actual EF Core 10 `ExecuteUpdateAsync` delegate parameter type); D-56 (`IAsyncEnumerable`/`AsAsyncEnumerable`), D-57 (`CanConnectAsync`/`ProviderName`/`IDbCommand.ExecuteScalar`), and D-58 (`IHostedService`/`IDbContextFactory`/`Database.MigrateAsync`/`pg_advisory_lock` via `IDbConnectionFactory`) all confirmed implementable as originally specified — no changes. All five corrections/confirmations apply to C-82..C-92 (Core) and T-47..T-54 (Tests) when implemented (persistence-phase-implementer)
- [2026-06-30] WO-037 (P-227, P-228) planned. **P-227** — `EncryptedValueConverter`'s hand-rolled `AesGcm`/`RandomNumberGenerator` mechanism is replaced by delegation to `01.Core/SharedKernel.Cryptography`'s `ISymmetricEncryptionService`; new direct `ProjectReference` from `SharedKernel.Persistence.EfCore` to `SharedKernel.Cryptography` (legal, no layering exception); the converter's constructor gains an `ISymmetricEncryptionService` parameter and now packs/parses the unchanged `"v{version}:{Base64(nonce||ciphertext||tag)}"` wire format around calls to `Encrypt(byte[])`/`Decrypt(EncryptedPayload)` instead of calling `AesGcm` directly; new internal `EncryptionOptionsKeyProvider` implements `SharedKernel.Cryptography.IEncryptionKeyProvider`, bridging `EncryptionOptions`/`IOptionsMonitor` (hot-reload preserved) and relocating the existing P-147 `IEncryptionVersionOverride` precedence rule (`OverrideVersion ?? CurrentVersion`) into `GetCurrentKey()`; `EncryptionKeyNotFoundException` is preserved as the public exception type, now triggered by a `GetKey()` pre-check before delegating to `Decrypt`; `EfCorePersistenceBuilder.WithEncryption()` gains an eager startup check that `ISymmetricEncryptionService` is resolvable (the consuming service must call `AddSharedKernelCryptography()` itself — this domain does not call it); five new hard violations (no hand-rolled AES in this domain, wire format must never change, no self-registering cryptography, no duplicate `EfUnitOfWork`-equivalent instances, no unconditional `Behaviors.IUnitOfWork` registration); root `CLAUDE.md`'s encryption "What Goes Where" row to be corrected (by root-level sync) from "two independent implementations" to "delegation". **P-228** — `EfUnitOfWork` gains a second interface declaration, `SharedKernel.Application.Behaviors.IUnitOfWork`, satisfied by its existing `SaveChangesAsync` method body; new direct `ProjectReference` from `SharedKernel.Persistence.EfCore` to `05.Application/SharedKernel.Application.Behaviors` (legal: 06 may reference 01–05); new opt-in `EfCorePersistenceBuilder.WithApplicationTransactionBehavior()` registers the same scoped `EfUnitOfWork` instance against both `IUnitOfWork` interfaces, shipping as code the bridge `05.Application/CLAUDE.md` had only documented as a future option since WO-035; omitting the call leaves `Build()` unchanged. DI registration examples added for both phases; AOT notes added for the delegation refactor and the dual-interface declaration; Cross-Domain Dependencies and Packages table updated with the two new project references; 21 new state-map tasks (D-59..D-64, S-13..S-14, C-93..C-97, T-55..T-60, DO-37..DO-38) (persistence-arch-planner)
- [2026-07-30] WO-051 batch 2 (P-321..P-325) planned — CLAUDE.md refreshed to target-state for the second and final batch of this work order (44 new `○` tasks in state-map.md: D-77..D-84, C-118..C-128, T-83..T-97, DO-47..DO-52, P-05..P-08). `SharedKernelDbContext`'s constructor doc corrected from a long-standing fictitious 2-parameter shape to the real, source-verified 9 parameters; Package Board's `.Abstractions`/`.EfCore` rows corrected from stale "Core" to "Published" in the same pass. **P-321** — `DapperReadService` gains two multi-mapping `QueryAsync` overloads (Dapper's `splitOn`-based join projection, two- and three-type arities) and a `QueryMultipleAsync<TResult>` grid-reader wrapper (connection held open for the full `readFunc` duration — a `GridReader` cannot outlive its connection); the connection factory field is promoted from private to `protected ConnectionFactory` as the documented extension seam for capabilities this base class doesn't itself wrap. **P-322** — the most consequential phase in this batch: read directly against shipped source, confirmed `AuditInterceptor`/`SoftDeleteInterceptor` capture `IUserContext` in their OWN constructors (safe today only because a fresh instance is built per non-pooled scope) and `TenantedDbContext.TenantProvider` is a get-only property baked into the compiled tenant filter via `Expression.Constant(specificObject, ...)`. Cross-referencing `EncryptionAwareModelCacheKeyFactory`'s own doc — EF Core's default model cache is process-wide, "one container, one cached model" — and confirming no analogous cache-key factory exists for `ITenantProvider`, surfaced a CONFIRMED, PREVIOUSLY-UNDETECTED DEFECT INDEPENDENT OF POOLING: a long-running multi-tenant service without `.WithEncryption()` would have its tenant filter permanently frozen to the first request's `ITenantProvider`. The fix — `SharedKernelDbContext.CurrentUserContext`/`RefreshUserContext`, interceptors reading `eventData.Context` live instead of a captured field, `TenantedDbContext.RefreshRequestContext` plus a filter rebuilt to bind through `Expression.Constant(this, GetType())` (EF Core's documented per-instance rebinding idiom for exactly this multi-tenancy shape) — closes BOTH the pre-existing staleness defect and the new pooling hazard in one redesign. New opt-in `EfCorePersistenceBuilder.WithDbContextPooling(poolSize)` layers `AddPooledDbContextFactory<TContext>` plus a scoped create-then-refresh `TContext` factory delegate, transparent to existing direct-injection consumer code; two `.Build()`-time guards block combining pooling with `.WithDbContextFactory()` or `.WithEncryption()` (the latter's `IEncryptionVersionOverride` has the identical unproven-under-pooling hazard, an explicit documented follow-up, not silently broken). A new "DbContext Pooling" CLAUDE.md section carries a PROMINENT warning that any future constructor-captured scoped dependency must be threaded through the same refresh-on-lease pattern. **P-323** — `EncryptionOptionsKeyProvider` gains an internal singleton `EncryptionKeyByteCache` (decode-once-per-config-value, `IOptionsMonitor.OnChange`-driven `Clear()`); `GetByIdsAsync`'s "1000 IDs" guidance corrected to describe Npgsql's actual single-parameter `= ANY(@array)` translation (no SQL-Server-style ceiling), with a re-derived ~50,000-ID memory guardrail and a new opt-in `GetByIdsChunkedAsync` sibling that changes nothing about `GetByIdsAsync`'s own behavior. **P-324** — all four packages gain a real `README.md` wired via `PackageReadmeFile` (previously wired in none); the `EfCorePersistenceExtensions.cs`/`EncryptedEntityBatchProcessorRegistry.cs` "reflection-free" wording inconsistency reconciled to the registry's own more accurate "justified, model-build-time exception" framing — documentation-only, zero runtime change. **P-325** — `DbConnectionFactoryDiagnosticsExtensions.CheckReadinessAsync`'s `ExecuteScalar()` and `MigrationAndSeedHostedService`'s advisory-lock `ExecuteNonQuery()` calls (both confirmed synchronous inside `async` methods via direct source read) are corrected via a safe cast to `System.Data.Common.DbCommand` and its true async members, with zero change to `IDbConnectionFactory`'s public interface; the release-path call unconditionally uses `CancellationToken.None` to preserve the original always-completes unlock guarantee, a correctness nuance called out prominently to prevent a future "fix" from reintroducing the skipped-unlock hazard. Eleven new hard violations, five new AOT notes, six new DI Registration examples, and six new Test Rules bullets added; this closes WO-051 (persistence-arch-planner, WO-051 batch 2 — work order complete)
- [2026-07-30] WO-051 batch 1 (P-315..P-320) planned — CLAUDE.md refreshed to target-state (forward-looking) for six phases, not yet implemented (62 new `○` tasks in state-map.md: D-65..D-76, C-98..C-117, T-61..T-82, DO-39..DO-46; no new Scaffold/Published tasks — no new packages or NuGet dependencies). Batch 1 of 2 for this work order; a second batch (P-321..P-325) follows in a separate dispatch. **P-315** — a real, previously-undetected defect confirmed and corrected: `EntityTypeConfigurationBase`'s unconditional `.IsRowVersion()` on a plain PostgreSQL `bytea` column is provably non-functional (nothing in Postgres auto-populates an arbitrary `bytea` on UPDATE the way SQL Server's native `rowversion` does), and the `ConcurrencyTokenConvention` its own XML doc (and design task D-05) claimed existed was never built, because a provider-branching convention inside `SharedKernel.Persistence.EfCore` is architecturally impossible (`.UseXminAsConcurrencyToken()` is Npgsql-only; EfCore must never reference Npgsql). Corrected split ships across two packages: EfCore marks `.IsConcurrencyToken()` only (provider-neutral); new `SharedKernel.Persistence.PostgreSQL` `XminConcurrencyTokenConvention`/`XminRowVersionValueConverter` binds that property to the real `xmin` system column via a `byte[]`↔`uint` converter — the genuine, working mechanism. D-05 corrected in place in state-map.md with an explicit supersession note rather than silently rewritten. **P-316** — `TenantedRepository`'s two `GetByIdForTenantAsync*` methods migrate their `EF.Property<TId>(e,"Id")` primary-key predicate to the same expression-tree pattern `EfRepository.ExistsAsync` already uses (P-099) — a sibling class that never received the P-105-era fix. **P-317** (depends on `03.Domain`'s `KeysetSpecification<T,TKey>`/P-308 — not yet implemented there, new Cross-Domain Dependencies row added) — new `ISpecificationEvaluator<T>.GetKeysetQuery<TKey>` (a method-level generic parameter beyond `T`, avoiding any `TKey`-erasure reflection) translates `AfterKey`/`AfterId` into a genuine seek predicate at new pipeline step 1b; new `IReadRepository.ListKeysetAsync<TKey>` + `KeysetPage<TAggregate,TKey>` give callers `HasMore`/next-cursor via a `Take+1` probe row — the SECOND explicitly documented deviation from "Skip/Take always last, spec's own values honored" (after `StreamAsync`'s forced `AsNoTracking`), since `Skip` is never applied for a keyset spec. Offset paging (`ListPagedAsync`/`PagedSpecification<T>`) is completely unchanged and remains available. **P-318** (also depends on P-308) — `AsSplitQuery` applied at new pipeline step 2c (after StringIncludes, before OrderBy) in `GetQuery`/`GetProjectedQuery`/`GetKeysetQuery<TKey>`; default `false` behavior unchanged. **P-319** — automatic `.TagWith(spec.GetType().Name)` on every generated query at pipeline step -1, zero configuration; new `SharedKernel.Persistence`/`"1.0"` `ActivitySource` (naming deliberately mirrors `02.Caching`'s `SharedKernel.Caching`/`"1.0"` `ActivitySource`, found already shipped in `FusionCacheService.cs` ahead of its own WO-050 phase) plus domain-local `PersistenceTagKeys` wrap `EfRepository`/`EfReadRepository` operations in spans — reusing `01.Core`'s existing `WellKnownTagKeys.ErrorType` for the failure case; never a raw parameter/entity/tenant value in any tag. This `ActivitySource` name/version is the coordination point for `13.ServiceDefaults`' paired phase P-326 (WO-051 dispatcher note), documented prominently in a new Observability section. **P-320** — `UsePostgreSQL(...)` gains optional `EnableRetryOnFailure` parameters (the only legal Npgsql call site, same layering constraint P-315 resolved); `EfCorePersistenceBuilder.WithTransientFaultRetry()` is a documented convenience/DI-pairing partner, not itself capable of configuring Npgsql. The genuine correction: EF Core's retrying execution strategy requires the whole transactional unit inside one `ExecuteAsync` delegate, which the existing handle-based `BeginTransactionAsync()`/`CommitAsync()` API structurally cannot satisfy — new `ITransactionalUnitOfWork.ExecuteInTransactionAsync`/`<TResult>` is the retry-safe alternative (reusing the existing begin/commit machinery and P-105's deferred-dispatch-until-commit behavior), and `EfTransactionalUnitOfWork.BeginTransactionAsync` gains an unconditional `RetriesOnFailure` guard (independent of whether `.WithTransientFaultRetry()` was called) that fails fast with an actionable, platform-specific message instead of EF Core's native one. Seven new hard violations added; canonical pipeline table, AOT notes, DI Registration examples, and Test Rules all extended accordingly (persistence-arch-planner, WO-051 batch 1)
- [2026-07-30] WO-051 Design phase (D-65..D-84, 84/84) implemented end to end across all four packages; state-map's stale Cross-Domain Dependencies row for 03.Domain's `KeysetSpecification<T,TKey>`/`AsSplitQuery` corrected to "Available" (confirmed shipped in `SharedKernel.Domain` v1.7.0). Two genuine implementation corrections found only via real testing, not foreseeable from design alone: `EfCorePersistenceBuilder.WithDbContextPooling()` must pre-wire the platform three interceptors into the pool's own `(sp, options)` optionsAction and `SharedKernelDbContext.OnConfiguring` needs an `Options.IsFrozen` guard, because EF Core freezes pooled `DbContextOptions` before `OnConfiguring`'s own interceptor-wiring mutation runs; a cross-test `ActivitySource` isolation pattern (parent-`Activity`-correlation via `new Activity("Test.Root").Start()`) was added to Test Rules for exact-count tracing assertions, distinct from `02.Caching`'s existence-style tolerance. 376/376 tests green across Abstractions/EfCore/PostgreSQL/Dapper (persistence-phase-implementer)
- [2026-07-31] SK.06.Core closed (128/128) — verified all 31 remaining WO-051 Core tasks (C-98..C-128) file-by-file against this file's own already-accurate target-state docs; the implementation code had existed on disk since the prior session's unclosed pass but state-map.md/tests were never re-confirmed. One genuine drifted defect found and fixed: `EfTransactionalUnitOfWork.BeginTransactionAsync`/`ExecuteInTransactionAsync` had come to construct an undocumented second class `EfTransactionalPersistenceTransaction` instead of extending the documented `EfPersistenceTransaction` adapter with post-commit dispatch, leaving the original `EfPersistenceTransaction` as dead, non-dispatching code — consolidated back to the single `EfPersistenceTransaction` class this file already described, no doc-content change needed since the prose was already correct. 376/376 tests green (Abstractions 49, EfCore 294, PostgreSQL 18, Dapper 15). WO-051's Tests/Docs/Published phases still carry pending tasks (persistence-phase-implementer)
- [2026-07-31] SK.06.Tests closed (97/97, T-61..T-97) — 24 tasks already implemented in prior unclosed sessions verified file-by-file; nine genuine coverage gaps closed with new tests (xmin converter/convention, keyset second-page/concurrent-insert proofs, keyset `ContractShapeTests`, `AsSplitQuery` multi-statement proofs for `GetQuery`/`GetProjectedQuery`, tracing failure-path/sensitive-value tests, PostgreSQL transient-fault-injection + retry-under-failure tests, encryption key-cache decode-once proofs, `GetByIdsChunkedAsync` round-trip tests); three new Test Rules bullets added documenting the `ExecuteReader`-for-writes gotcha (SQLite and Npgsql both route store-generated-value INSERT/UPDATE through the reader path, not `ExecuteNonQuery`), the Testcontainer-free model-metadata-assertion technique, and the reference-equality decode-once-proof technique. 416/416 tests green (Abstractions 54, EfCore 314, PostgreSQL 33, Dapper 15). WO-051's Docs (38/52)/Published (4/8) phases still carry pending tasks (persistence-phase-implementer)
- [2026-08-03] WO-053 (P-333, P-334, P-336, P-337, P-338, P-339) planned — CLAUDE.md refreshed to target-state for six independent gold-standard-completeness phases, none yet implemented (76 new `○` tasks in state-map.md: D-85..D-103, S-15..S-18, C-129..C-145, T-98..T-121, DO-53..DO-64; no new Published tasks). **P-333** — closes this domain's outstanding WO-041 `[LoggerMessage]` retrofit: zero production log output existed anywhere in `06.Persistence` despite `01.Core`'s registry reserving `6000-6999` for it. `SharedKernel.Persistence.EfCore` claims sub-block `6000-6099` (Abstractions reserves none — it never logs); eleven new `[LoggerMessage]` events span `ConcurrencyInterceptor` (conflict detection), `MigrationAndSeedHostedService` (lifecycle + advisory lock), a new internal `PersistenceRetryDiagnosticListener` bridging EF Core's own provider-neutral `CoreEventId.ExecutionStrategyRetrying` diagnostic event (never Npgsql-specific — preserves the EfCore-never-references-Npgsql hard rule), and `EncryptionRotationService` (batch progress, with an explicit no-key-material/no-plaintext logging guarantee). Also closes a small, thematically adjacent tracing gap: `EfRepository.GetByIdAsync`/`.ExistsAsync` were the only two public members never wrapped in `RepositoryTracing`. **P-334** — a confirmed doc/code mismatch: `EncryptionOptions`/`PersistenceServiceOptions` each claimed a bound configuration section that no code ever read. New `SectionName` constants plus genuine `IConfiguration`-accepting overloads on `.WithEncryption()`/`.WithServiceName()` (via `Microsoft.Extensions.Options.ConfigurationExtensions`), composing with the pre-existing `Action<T>`/direct-value paths under ordinary `IOptions<T>` later-registration-wins semantics — neither existing path changes. **P-336** — verified the drift was materially worse than initially framed: not one drifted fixture but SIX independent ad hoc PostgreSQL Testcontainer setups (four inline `IAsyncLifetime` test classes plus one dead, never-instantiated `Fixtures/PostgreSqlContainerFixture.cs` in `PostgreSQL.Tests`, plus one more inlined in `Dapper.Tests`), all pinned to the OBSOLETE `postgres:16-alpine`/`new XBuilder().WithImage(...)` construction shape `16.Testing` itself already moved off of at its own Testcontainers 4.13.0 bump. Consolidates onto `16.Testing`'s canonical `PostgreSqlContainerFixture` via a new `[CollectionDefinition("PostgreSQL")]` completing the sharing four pre-existing `[Collection("PostgreSQL")]` tags always implied but never wired up. **P-337** — a bundle of two independent, low-risk completions (mirroring the WO-051/P-323 bundling precedent): single-entity soft-delete restore ships as a new `IRestorableRepository<TAggregate,TId>` (Abstractions, ORM-agnostic — unlike `IBulkMutationRepository`) implemented unconditionally by `EfRepository<TAggregate,TId>` with a runtime `ISoftDeletable` guard mirroring the shipped `BulkSpecificationGuard` precedent; bulk restore needs zero new production code, closed purely as a documented `ExecuteUpdateAsync` usage pattern. Command timeout is placed on `EfCorePersistenceBuilder.WithCommandTimeout(int)`, not `UsePostgreSQL(...)` — `DbContextOptionsBuilder.CommandTimeout(int?)` is a provider-NEUTRAL `Microsoft.EntityFrameworkCore.Relational` extension, unlike the genuinely Npgsql-only `EnableRetryOnFailure` (P-320). **P-338** — the most architecturally involved phase in this batch: opt-in `IReadRepository` routing to a second, lazily-constructed `TContext` instance built via `ActivatorUtilities.CreateInstance<TContext>` against a keyed replica `DbContextOptions<TContext>`, deliberately reusing the SAME scope-ambient DI-resolved interceptor instances (and their live `CurrentUserContext`, WO-051/P-322) the primary context already resolved, so audit consistency is automatic with zero extra plumbing. `EfReadRepository` gains one optional, nullable, defaulting-to-`null` constructor parameter — every existing subclass is unaffected. The "never route inside an active transaction" hard rule is enforced by checking live `Database.CurrentTransaction` state, mirroring `EfTransactionalUnitOfWork`'s own existing retry-guard pattern. **P-339** — `HasVectorColumn`/`VectorColumnAttribute` were confirmed to do nothing beyond raw column-type mapping; zero query-side pgvector help existed. New `VectorOrderingExpressions.ByDistance<TAggregate>(...)` returns a boxed `Expression<Func<TAggregate,object>>` (never calling `AddOrderBy` itself — no protected-member-access problem, no new `Specification<T>` base class) for the CONSUMER's own spec subclass to wire into its own protected `AddOrderBy` call, exactly how `KeysetSpecification<T,TKey>` already populates protected members; the `MethodInfo` for `Pgvector.EntityFrameworkCore.VectorExtensions.CosineDistance`/`.L2Distance` is captured via a statically-typed delegate-cast `.Method` property, resolved by the compiler, zero runtime `GetMethod`/`MakeGenericMethod`. Fourteen new hard violations, six new AOT notes, roughly a dozen new DI Registration examples, and eight new Test Rules bullets added across all six phases; three new CLAUDE.md narrative sections added ("Structured Logging," "Soft-Delete Restore," "Read-Replica Routing") alongside an expansion of the existing pgvector support section. No Layering Rules, Package Naming, or new-package/new-third-party-technology changes anywhere in this dispatch (persistence-arch-planner, user request, WO-053, P-333/P-334/P-336/P-337/P-338/P-339)
- [2026-08-03] SK.06.Design closed for WO-053 (D-85..D-103, 19/19) — reconciled the six-phase target-state design above against real shipped source rather than trusting the planning pass's prose; the design descriptions for the fourteen tasks that reference PRE-EXISTING code (D-87..D-90, D-92..D-98, D-100..D-101) were verified accurate with zero drift (`MigrationAndSeedHostedService`'s four-step `StartAsync` sequence, `EncryptionRotationService`'s per-batch `OnBatchCompleted` hook, `RepositoryTracing.ExecuteTracedAsync`'s existing shape, `EncryptionOptions`/`PersistenceServiceOptions`' own doc claims, `EfCorePersistenceBuilder.WithEncryption`/`.WithServiceName`/`.WithCompiledModel`'s existing wrapping pattern, `IRepository<TAggregate,TId>`'s constraint shape, `EfRepository.MarkAsModifiedIfDetached`, `ISpecification<T>.OrderBy`'s boxed-to-object shape — all read directly, not assumed) and D-95's Testcontainers-consolidation claims (the dead `Fixtures/PostgreSqlContainerFixture.cs`, all four inline-container `PostgreSQL.Tests` classes, the Dapper inline container, `16.Testing`'s canonical fixture shape, both `.csproj` references) were independently confirmed byte-for-byte. **Two genuine drift findings, both corrected in place above:** (1) **D-86** — the original design assumed `ConcurrencyInterceptor`'s Warning log belongs inside `SaveChangesFailed`/`SaveChangesFailedAsync`, immediately before "constructing/throwing the translated `ConcurrencyException`." The real shipped source (WO-051/P-315) makes both of those hooks permanent, documented no-ops — EF Core 10 does not allow them to replace the exception propagating from `SaveChanges`/`SaveChangesAsync` — and the actual translation happens in `ConcurrencyInterceptor.TryTranslate` (today `internal static`), called from `SharedKernelDbContext.SaveChanges(bool)`/`SaveChangesAsync(bool, CancellationToken)`'s own catch-filter, throwing `SharedKernel.Core.Exceptions.ConflictException` — no `ConcurrencyException` type exists in this domain. Corrected design: `TryTranslate` becomes a non-static instance method (reachable via `SharedKernelDbContext`'s existing `_concurrencyInterceptor` field — zero new plumbing), logging immediately before it returns the translated `ConflictException`; both `SaveChanges` and `SaveChangesAsync` pick up the logging automatically. (2) **D-102/D-103** — the original design claimed `Pgvector.EntityFrameworkCore`'s `VectorExtensions` class exposes exactly two distance functions (`CosineDistance`/`L2Distance`) with a `(Vector, Vector)` signature. Verified via direct reflection against the real shipped `Pgvector.EntityFrameworkCore` 0.3.0 assembly: no type named `VectorExtensions` exists at all — the real, sealed static type is `Pgvector.EntityFrameworkCore.VectorDbFunctionsExtensions`, exposing SIX members (`CosineDistance`/`L2Distance`/`L1Distance`/`HammingDistance`/`JaccardDistance`/`MaxInnerProduct`), each declared `static double MethodName(this object a, object b)` — `object` parameters, EF-Core query-translation placeholder methods, not directly invocable client-side. `VectorDistanceMetric`'s `Cosine|L2`-only scope remains correct (independently confirmed by the root `CLAUDE.md`'s own WO-053/P-339 entry, "a cosine/L2 distance-ordering helper") — only the justification ("mirrors the exactly-two... exposes") was wrong; corrected to "a deliberate narrowing of the real six-function surface." The statically-typed delegate-cast technique (`((Func<Vector,Vector,double>)VectorDbFunctionsExtensions.CosineDistance).Method`) was independently compiled and confirmed to still work with ZERO reflection despite the real `object,object` signature, because `Pgvector.Vector` is a reference type (a class) and C# permits contravariant reference-type parameter widening in a method-group-to-delegate conversion — verified, not merely asserted from the language spec. Both corrections are now reflected in the "Structured Logging" and pgvector query-ergonomics sections above; no further design-level changes were needed for the remaining 17 tasks. `06.Persistence`'s own root Phase Backlog entries (P-333/P-334/P-336/P-337/P-338/P-339) remain `◐ Dispatched` — this closes only the local `SK.06.Design` phase-key; Core-phase implementation (C-129..C-145) is a future session's work (persistence-phase-implementer)
- [2026-08-03] SK.06.Scaffold closed (18/18) — S-17 completed the T-106..T-108 PostgreSQL Testcontainers fixture migration in the same session (`PostgreSQL.Tests`/`Dapper.Tests` onto `16.Testing`'s `PostgreSqlContainerFixture`); Test Rules' P-336 bullet corrected in place — only three of four `PostgreSQL.Tests` classes join the shared fixture (`PostgreSQLIntegrationTests` keeps its own dedicated pgvector-enabled container, since the shared fixture's plain `postgres:16.4` image cannot run its `CREATE EXTENSION vector` test) — plus a new bullet documenting two genuine, implementation-discovered gotchas: `EnsureCreatedAsync()`'s coarse `HasTables()` check requires per-class uniquely-named databases when sharing one Testcontainer across differently-shaped `DbContext` models, and `DROP TABLE` ordering becomes load-bearing (child-before-parent) once a database persists across xUnit test methods via `IClassFixture<T>`. `dotnet test` green (33/33 PostgreSQL.Tests, 15/15 Dapper.Tests) against a real Docker daemon (persistence-phase-implementer)
- [2026-08-03] SK.06.Core closed (145/145) — implemented all 17 remaining WO-053 Core tasks (C-129..C-145) exactly per the already-reconciled Design-phase docs above, zero further drift found: `[LoggerMessage]` logging (EventIds 6000-6010) across `ConcurrencyInterceptor`/`MigrationAndSeedHostedService`/a new `PersistenceRetryDiagnosticListener`/`EfUnitOfWork`+`EfTransactionalUnitOfWork`/`EncryptionRotationService`; `RepositoryTracing` wrap for `GetByIdAsync`/`ExistsAsync`; `EncryptionOptions`/`PersistenceServiceOptions.SectionName` + `IConfiguration`-binding `WithEncryption`/`WithServiceName` overloads; `IRestorableRepository<TAggregate,TId>`/`EfRepository.RestoreAsync`; `WithCommandTimeout`; `IReadReplicaContextAccessor<TContext>`/`WithReadReplica`/`EfReadRepository` replica routing; `VectorDistanceMetric`/`VectorOrderingExpressions.ByDistance` in `SharedKernel.Persistence.PostgreSQL`. One genuine cross-test bug found and fixed during Tests-phase implementation (folded into this same session): `PersistenceRetryDiagnosticListener`'s process-wide `DiagnosticListener.AllListeners` subscription caused count-inflation between concurrently-running test classes forcing genuine EF Core retries via the same fixture technique — fixed via a shared xUnit collection, documented as a new Test Rules bullet (the `DiagnosticListener` analogue of the existing `ActivitySource` cross-test-isolation bullet). 92 new tests added; full suites green (EfCore 353/353, PostgreSQL 41/41 against a real Docker daemon). WO-053's Tests/Docs phases still carry pending tasks (T-98..T-121/DO-53..DO-64 beyond what this session's tests satisfied — a future session reconciles state-map coverage) (persistence-phase-implementer)
- [2026-08-04] SK.06.Tests closed for WO-053 (T-98..T-105, T-110..T-121, 20/20) — verified each task file-by-file against the 92 tests the prior Core-phase session had already written: 10 of 20 were already fully satisfied verbatim (T-102/T-103/T-110/T-111/T-115 exactly; T-98/T-99/T-101 functionally covered, strengthened with explicit exact-count/`RowsInBatch`/`BatchNumber`/full-negative-scan assertions the task text specifically called for). Ten genuine gaps closed with new tests: T-100 (PostgreSQL Testcontainers `6007`/`6008` retry logging — the two SQLite-based Core-phase tests proved the logging mechanism but never against real Npgsql; resolved the internal `PersistenceRetryDiagnosticListener`'s lack of an `InternalsVisibleTo` grant to `SharedKernel.Persistence.PostgreSQL.Tests` by exercising it purely through its public `IHostedService`/`ILogger<T>` surface via `16.Testing`'s `InMemoryLoggerFactory` category-string lookup — a new, reusable pattern for testing internal `[LoggerMessage]` emitters from a sibling package's test project without widening `InternalsVisibleTo`); T-104/T-105 (configuration-binding composition-precedence and validator-registered-exactly-once tests); T-112 (bulk restore pattern proof — no new production method, per its own documented "usage pattern" framing); T-113 (real `pg_sleep(...)` command-timeout enforcement, distinct from the existing SQLite metadata-only test); T-114/T-116 (a new two-independent-Testcontainer `IClassFixture` proving read-replica routing at the connection level, not just the API surface); T-117 (replica-instance reference-equality-across-two-calls, added to the existing SQLite suite); T-118/T-119/T-120 (real pgvector nearest-neighbor correctness/composability — see the GATING defect below); T-121 (a repeated-calls/split-timing behavioral proxy for the zero-reflection claim, pure unit test, no container). **GATING genuine production defect found and fixed while writing T-118/T-119** — `VectorOrderingExpressions.ByDistance` (C-145, WO-053 Core phase) had never been executed against a real database before this session: passing `queryVector` as a bare `Expression.Constant(queryVector, typeof(Vector))` produces a genuine `PostgresException` ("42601: syntax error at or near '['") the moment the query actually runs, because EF Core's query pipeline treats an already-bare `ConstantExpression` as an inline SQL literal rendered via `Vector.ToString()` ("[1,2,3]") with no quoting/cast — `Pgvector.EntityFrameworkCore`'s distance-function translator does not attach a `vector` `RelationalTypeMapping` to an inline constant the way it does for a genuine ADO.NET parameter. Fixed by wrapping the value in a private single-property holder accessed via `Expression.Property(Expression.Constant(holder), nameof(holder.Value))` — reproducing the exact shape the C# compiler emits for a captured local variable inside an ordinary LINQ closure, which EF Core's parameter-extraction visitor recognizes and promotes to a real query parameter (confirmed via the generated SQL changing from `ORDER BY p.embedding <=> [1,0,0]` to `ORDER BY p.embedding <=> @Value`). Zero change to the method's public signature, its zero-reflection `MethodInfo`-capture technique, or its boxed-`object` return shape. The "Query-side pgvector ergonomics" CLAUDE.md section above is corrected in place with the full before/after, and two independently-stale `AddOrderBy(...)` usage-example/prose references (the real shipped method is `ApplyOrderBy`) are corrected alongside it — neither was caught by the Design-phase or Core-phase reconciliation passes, since neither ever compiled/ran the usage example. A second, independent genuine defect (Npgsql's own "vector" type OID resolving as unknown, `NotSupportedException: Cannot resolve 'vector' to a fully qualified datatype name") surfaced purely in TEST SETUP, not production code: `CREATE EXTENSION IF NOT EXISTS vector` must run on a throwaway connection BEFORE the EF Core-managed Npgsql connection pool opens its first connection — Npgsql resolves the "vector" type OID once per data-source lifetime (at first connection open), so issuing the `CREATE EXTENSION` as the pool's own first command permanently poisons that pool's type cache for its entire lifetime, even though the extension now exists. Full suites green: EfCore 358/358 (was 353 — 5 net new/strengthened tests), PostgreSQL 53/53 (was 41 — 12 net new tests: 3 retry-logging + 2 command-timeout + 2 replica-routing + 1 replica-reference-equality + 3 vector-correctness/composability + 1 vector-behavioral-proxy, some existing methods also gained assertions without adding new `[Fact]`s), both against a real Docker daemon. This closes every remaining WO-053 task in this domain's own state-map (persistence-phase-implementer)

- [2026-08-26] Four pending phase definitions processed from root `state-map.md`'s Phase Backlog, in dependency order: P-440/WO-066 (EF Core value-conversion support for `03.Domain`'s `Money`), P-448/WO-068 (migrate this domain's `IEncryptionKeyProvider` consumers onto `01.Core`'s new async contract, breaking), P-456/WO-071 (append-only audit-trail contracts in `.Abstractions`), P-457/WO-071 (EfCore immutable audit-trail + hash-chain implementation). All four design-locked this pass; no implementation yet — see `state-map.md`'s new `D-104`..`D-125` (design, all `●`) and `S-19`..`S-22`/`C-146`..`C-165`/`T-122`..`T-138`/`DO-65`..`DO-68`/`P-09`..`P-12` (all `○`, pending). Key decisions: (1) `CurrencyValueConverter`'s from-provider direction uses the public `Currency.Create` factory, never `StronglyTypedIdValueConverter`'s reflection-located-constructor technique — `ValueObject`/`SingleValueObject<TValue>` already expose that factory (03.Domain P-310), so the heavier technique was never needed; `Money`'s owned-type materialization against its private 3-arg constructor is flagged as a genuine open risk with a preferred/fallback resolution recorded for Core-phase, pending `Money`'s own implementation. (2) P-448's "central design problem" (EF Core's `ValueConverter` pipeline is synchronous) is resolved by REMOVING `EncryptedValueConverter`'s direct `IEncryptionKeyProvider` dependency entirely — source-verified the existing decrypt-path pre-check is redundant with `ISymmetricEncryptionService.Decrypt`'s own `CryptographyErrorCodes.UnknownKeyId`-vs-`DecryptionFailed` distinction — rather than by blocking on async anywhere in this domain; corrected a pre-existing, unrelated doc/code drift discovered along the way (this file's `EncryptedValueConverter` constructor doc had already dropped the `IEncryptionKeyProvider keyProvider` parameter that the real shipped source still carries). (3) P-456's `IAuditActorContext` corrects an imprecision in its own root phase text: this domain already holds an approved `SharedKernel.Security.Abstractions` exception (P-078/WO-014), but it's scoped to `.EfCore` only, never `.Abstractions` — exactly why a fresh local seam belongs in Abstractions regardless, and exactly why `.EfCore`'s implementation (P-457) CAN ship a default `IAuditActorContext` bridging the already-approved `IUserContext`/`ITenantProvider`, an ergonomic advantage `05.Application`'s `IAuthorizationContext` structurally cannot offer. (4) Refined P-457's own "implement via `IReadRepository<T,TId>`" suggestion: `AuditRecord` deliberately does not implement `IAggregateRoot<Guid>` (would force an always-empty domain-events collection onto a plain infrastructure record); `EfAuditQueryService` builds directly on the unconstrained `ISpecificationEvaluator<AuditRecord>` instead. `AuditRecordImmutabilityInterceptor` (a new, opt-in-only fourth interceptor) is the load-bearing structural guarantee; `EfAuditTrailWriter.RecordAsync` is self-contained and calls `SaveChangesAsync` itself rather than staging into the ambient unit of work. Domain summary, Packages table, Technology Stack, Interface Contracts (new Auditing subsection in Abstractions; Money conversion + fourth interceptor + corrected/annotated Encryption section in EfCore), Implementation Rules (7 new hard violations), DI Registration (Money + `.WithAuditTrail()` examples), AOT Compatibility, and Test Rules all updated (persistence-arch-planner)
- [2026-09-02] WO-066/P-440 (Money EF Core value conversion) and WO-071/P-456+P-457 (append-only hash-chained audit trail) SHIPPED — Core phase (C-146..C-148, C-153..C-165, 16/20 tasks; C-149..C-152/WO-068 remain `○`, blocked on 01.Core P-446, still `◐` Dispatched). Two design corrections: D-106's preferred owned-type/reflection-located-constructor Money materialization is unreachable via any public EF Core 10 API (`ITypeBase.ConstructorBinding` read-only everywhere public) — shipped the packed-string fallback instead, requiring a new `ConfigureMoney()` call in `ConfigureConventions()`; `EfAuditQueryService` calls `GetKeysetQuery`, not `GetQuery` (which silently ignores a keyset cursor). New DI pattern: `AuditTrailFeatureMarker`, a DI-resolvable marker type standing in for a `bool` flag (DI cannot auto-resolve primitives) so `SharedKernelDbContext` can conditionally apply `AuditRecordEntityConfiguration`. Test-only `Microsoft.EntityFrameworkCore.InMemory` added (Directory.Packages.props + EfCore.Tests.csproj) — SQLite cannot translate `ORDER BY DateTimeOffset` at all, blocking `AuditRecord.OccurredOn`-ordered query tests. New known-limitation section recorded: `Guid.CreateVersion7()` same-millisecond ordering is unreliable (confirmed empirically), a platform-wide `KeysetSpecification<T,TKey>` caveat, not fixed here. 435 tests green, 0 regressions, full solution build clean. Packages table, Technology Stack, Interface Contracts (Auditing block, Conversions block), Implementation Rules (hard violations + new Known Limitation subsection), DI Registration examples, AOT Compatibility, and Test Rules all updated to reflect shipped (not design-locked) status; two coverage gaps flagged for the Tests phase (BHD 3-decimal Money round-trip; cross-partition `VerifyChainIntegrityAsync` independence) (persistence-phase-implementer)
- [2026-09-02] WO-066/WO-071 Tests-phase pass (T-122, T-123, T-129..T-138, 12/16 remaining SK.06.Tests tasks) — T-122 REWRITTEN in `state-map.md` after independently re-verifying (reflection against the real EF Core 10.0.10 assembly, not merely trusted from the Core-phase session's prose) that `ITypeBase.ConstructorBinding` has no public setter anywhere and no `HasConstructorBinding(...)`-shaped builder method exists outside `Metadata.Internal`/`Metadata.Runtime` — the packed-string `Money` mapping is genuinely forced, so T-122 now asserts full round-trip fidelity across JPY/USD/BHD plus a companion test making the Amount/Currency non-queryability capability loss concrete and observable, rather than the originally-specified (infeasible) two-column SQL assertion. **GATING regression found and fixed while writing T-123**: `ValueObjectOwnershipBuilder`'s Money-only skip (D-107) was insufficient — a standalone `Currency` property crashed model building identically; generalised the skip to any CLR property already mapped as a scalar EF property (see this file's Money/ValueObjectOwnershipBuilder section for the full note). T-125..T-128 (WO-068/P-448) remain blocked — `01.Core` P-446 still not shipped, re-confirmed by reading the real `IEncryptionKeyProvider.cs` this session. T-133/T-134/T-135 satisfied with documented backend-availability caveats (no Docker daemon this session; `AuditRecord` requires the InMemory provider, which generates no SQL) — T-134/T-135 proven via LINQ expression-tree inspection instead of captured-SQL/`.TagWith(...)` text. `SharedKernel.Persistence.EfCore.Tests` 409/409 green (13 new), `SharedKernel.Persistence.Abstractions.Tests` 63/63 green (12 new, new `Auditing/AuditingContractTests.cs`). SK.06.Tests now 134/138 `◐` — not promoted (persistence-phase-implementer)
- [2026-09-03] WO-068/P-448 SHIPPED (S-20, C-149..C-152, DO-66, P-10) — `01.Core`'s P-446 (async `IEncryptionKeyProvider`) landed, clearing the blocker; verified the real shipped `01.Core` source (sync `Encrypt`/`Decrypt` retained on `ISymmetricEncryptionService`, bridging via `.GetAwaiter().GetResult()`) before relying on it. `EncryptedValueConverter`/`EncryptionModelConvention` constructors both DROP `IEncryptionKeyProvider` (D-108/D-109, breaking); `EncryptionOptionsKeyProvider`/`NullEncryptionKeyProvider` migrated to `GetCurrentKeyAsync`/`GetKeyAsync` (D-110). Incidental fix required for the package to compile at all: `01.Core`'s `ISymmetricEncryptionService` also gained async members as part of P-446 — `NullSymmetricEncryptionService` needed them implemented too. `SharedKernelDbContext` keeps its `encryptionKeyProvider` constructor parameter for downstream source-compatibility but no longer stores/forwards it. `dotnet build`/`dotnet pack` both clean on `SharedKernel.Persistence.EfCore`. Migrated `EncryptedValueConverterTests`/`EncryptionDelegationTests`/`EncryptionKeyCachingTests` onto the async shape; **could not run inside the real `SharedKernel.Persistence.EfCore.Tests`** — it references `16.Testing`, which is itself broken pending `16.Testing`'s own P-450 (out of this domain's control) — independently verified via a standalone throwaway harness project (no `16.Testing` dependency): 33/33 pass. That harness caught and fixed one genuine test-fixture bug (not production): a too-short `"vv1:AAAA"` fixture relied on the OLD pre-check's early-exit; D-108 removing that pre-check meant the fixture needed a properly-sized (≥28-byte) payload to actually reach `Decrypt`. `06.Persistence/CLAUDE.md` fully migrated off "design-locked/pending" language for P-448 across the domain summary, Packages table, Encryption interface-contracts section, DI-registration section, AOT Compatibility, Test Rules, and Implementation Rules. SK.06.Scaffold/Core/Docs/Published all promoted to `●`; SK.06.Tests remains `◐` (134/138) — blocked on `16.Testing`/P-450, not `01.Core`/P-446 (persistence-phase-implementer)
- [2026-09-03] WO-068/P-448 CLOSED (T-125..T-128) — `16.Testing`/P-450 shipped, clearing the blocker; re-verified in the REAL `SharedKernel.Persistence.EfCore.Tests` project (415/415 green, `dotnet pack` clean), not the prior harness. Found and fixed two test-only gaps: a loose `Throw<Exception>()` tightened to the concrete `CryptographicException` (T-126); `NullEncryptionKeyProvider` had zero coverage — added `NullEncryptionKeyProviderTests.cs` (5 tests). SK.06.Tests now 138/138 `●` — all six `SK.06.*` phase keys `●`, this domain's tracked scope is complete end to end; stale "still blocked" callout at the top of this file corrected. Root Phase Backlog P-448 closed; P-440/P-456/P-457 independently re-verified against their own acceptance criteria and also closed (persistence-phase-implementer)
- [2026-09-08] P-498 (WO-081, SEVERE) processed — this domain's phase in the coordinated cross-domain breaking wave whose `01.Core` foundation (`SK.01.P491` associated-data-required `ISymmetricEncryptionService`, `SK.01.P492` the `ISynchronousEncryptionKeyProvider` capability gate) was design-locked earlier the same session by `core-arch-planner`. **Refuted the phase input's own literal premise** (D-126, same discipline `02.Caching`/P-497 applied against its own phase input this session): a `SavingChangesAsync`-only pre-warming hook cannot close this defect — it never fires for a query (the read path is what the motivating F1 scenario actually describes), and even for writes, wrapping a KMS provider in `01.Core`'s `CachedEncryptionKeyProvider` does not satisfy `IsGenuinelySynchronous` regardless of cache warmth, since that check is a static provider-identity test a KMS provider can never honestly pass. Redesigned around: (1) marking the existing config-backed `EncryptionOptionsKeyProvider`/`NullEncryptionKeyProvider` genuinely synchronous, closing what would otherwise be a silent regression for every existing user the moment `01.Core`'s P-492 ships; (2) a new, honestly-marked `PreWarmedEncryptionKeyProvider` that never touches its wrapped provider from its own synchronous-contract members, serving only from an explicitly-warmed in-memory cache; (3) a dual-hook `EncryptionKeyPreWarmingInterceptor` — `SavingChangesAsync` for writes, `IDbCommandInterceptor.ReaderExecutingAsync` (EF Core's genuine async pre-materialization extension point) for reads — closing both halves of the defect, not just the write half the input text focused on; (4) a structural, keyed-DI isolation fix (`.WithExternalEncryptionKeyProvider<TProvider>()`, replacing `.WithEncryption()`'s prior unkeyed `IEncryptionKeyProvider` registration) that makes the SEVERE bug's actual root cause — an accidental DI-registration-order collision with an unrelated ambient KMS provider registration, e.g. `13.ServiceDefaults`'s `AddSharedKernelKeyVaultKeyProvider` — structurally unreachable, not merely fail-fast-detected. Also resolved `01.Core`'s own D-67 "hardest of the six" AAD-derivation open question: associated data is bound to each property's stable table+column storage identity, computed once at model-finalization time — no row-level access needed, with an optional `associatedDataOverride` escape hatch for rename-safety, documented as a hazard otherwise. Nine new Design tasks (D-126..D-134, all `●`, design-locked); eighteen `○` Core/Tests/Docs/Published tasks depend on `01.Core`'s `SK.01.P491`/`SK.01.P492` shipping past design-lock into Core before this package can compile against the new `ISymmetricEncryptionService` shape. `SK.06.Design` now 134/134 `●` (still promotable); `SK.06.Core`/`SK.06.Tests`/`SK.06.Docs`/`SK.06.Published` all moved from `●` to `◐` — none promoted. No root `state-map.md`, root `CLAUDE.md`, or `.slnx` changes made (out of jurisdiction) (persistence-arch-planner)
- [2026-09-08] P-498/WO-081 SHIPPED (C-166..C-172, T-139..T-146, DO-69/DO-70, P-13) — `01.Core`'s `SK.01.P491`/`SK.01.P492` confirmed shipped on disk before starting. Two corrections against the design record during implementation: `EncryptionKeyPreWarmingInterceptor`'s constructor takes only `PreWarmedEncryptionKeyProvider` (the design sketch's second `IEncryptionVersionOverride` parameter was unnecessary — `WarmCurrentAsync()` already resolves it internally); `EncryptionStartupValidator`'s D-132 check resolves a SECOND keyed-DI slot (`PersistenceEncryptionKeys.EncryptionKeyProviderKey`, holding whichever `IEncryptionKeyProvider` backs the persistence-scoped service) rather than checking "individual dependencies like `ISecureRandomGenerator`" as originally sketched — source-verified `AesGcmEncryptionService`'s constructor takes only one parameter, so there was nothing else to check. `SharedKernelDbContext`'s new keyed-service resolution reads `DbContextOptions.FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider` (EF Core's own public mechanism) rather than requiring `[FromKeyedServices]` attributes on every downstream context subclass (which would have needed the internal key exposed cross-assembly — structurally impossible) — zero downstream signature changes required. Unplanned nice side effect found during implementation: the config-backed default path no longer needs `01.Core`'s `AddSharedKernelCryptography()` called at all, since this package now constructs `AesGcmEncryptionService` directly. Three pre-existing rotation test files had an now-obsolete manual unkeyed `ISymmetricEncryptionService` registration removed (depended on the pre-P-498 unkeyed slot this phase deliberately stopped populating). 8 new test files/~30 new tests added under `SharedKernel.Persistence.EfCore.Tests/Encryption/`; full suite 448/448 green; `dotnet pack` clean. `06.Persistence/CLAUDE.md` migrated off "DESIGN-LOCKED/implementation pending" language across the domain summary, Packages table, Encryption interface-contracts block (including two new types not previously listed: `EncryptionKeyPreWarmingHostedService`, `PersistenceEncryptionKeys`), the `.WithEncryption()`/`.WithExternalEncryptionKeyProvider<TProvider>()` wiring section, the `SharedKernelDbContext` constructor note, the five P-498 Hard-violations entries, and the "unwarmed historical key versions" known-limitation section. `README.md` corrected (dropped the now-unnecessary `AddSharedKernelCryptography()` call from the config-backed quick-start) and gained a new "KMS-backed field-level encryption" subsection. `SK.06.Core`/`SK.06.Tests`/`SK.06.Docs`/`SK.06.Published` promoted to `●` (persistence-phase-implementer)
