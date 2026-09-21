> Durable copy of a P-558 session record (2026-09-21), copied verbatim from the session scratchpad. Paths such as
> `scratchpad/...` refer to that temporary folder and no longer exist. Code wins where this record and the code disagree.

# Persistence gold-standard pass 2 — binding design decisions (2026-09-21)

Source of findings: `p558-review-findings.md` (same folder). IDs below (A1…, B, C) refer to it.
Owner decisions (user, 2026-09-21): **audit = async sealer**, **05↔06 contracts merged**,
**PostgreSQL-only**, and ALL additions: one-line setup + DX, encryption migration tools,
multiple DbContexts + per-tenant crypto-shredding, specification API (03.Domain).
Nothing in 06.Persistence is published → breaking changes are free. 05.Application and 03.Domain are
published alpha → breaking changes allowed, but prefer [TypeForwardedTo] where it is cheap.
Every confirmed bug in section A is fixed regardless, and everything in section B is deleted.
Do NOT write state-map phases (user: "skip the phasing"). Docs/brains are updated in the final wave.

## D1. Shared contracts: new package `SharedKernel.Application.Abstractions` (05.Application)
- MediatR-free. References only `SharedKernel.Primitives` (+ DI abstractions if needed). Packable, PublicAPI tracked, README.
- Holds the single copy of:
  - `IRequestContext` (+ `SystemRequestContext`, `AnonymousRequestContext`), keep namespace
    `SharedKernel.Application.Context`; add `[TypeForwardedTo]` in `SharedKernel.Application`.
    Extend with what persistence needs for attribution: an actor kind (User/Service/System) and
    optional ClientId/SessionId/ImpersonatorId if cheap — replaces 06's `ICurrentActorContext`
    and `ICurrentTenantContext` (DELETE both, and `AnonymousActorContext`/`NullCurrentTenantContext`).
    Fail-closed default when nothing registered: anonymous, tenant null.
  - ONE unit-of-work contract (merge IUnitOfWork + ITransactionalUnitOfWork):
    `SaveChangesAsync`, `ExecuteInTransactionAsync(Func<CancellationToken,Task>)` / `<TResult>`
    (retry-safe: whole delegate inside the execution strategy), optional isolation level.
    Keep `BeginTransactionAsync` only if still needed; it throws under a retrying strategy.
    A pre-commit hook for the audit writer (e.g. `ITransactionScope.OnBeforeCommit(Func<CancellationToken,Task>)`)
    alongside 05's existing post-commit `ICommandScope.OnCompleted`.
  - `IAuditTrailWriter` + `AuditEntry` + `AuditOutcome` (the writer contract only; query/verify types
    move to the Auditing package — see D5).
- `SharedKernel.Application.Behaviors` uses these directly: `TransactionBehavior` runs the handler
  through `ExecuteInTransactionAsync` (eShop pattern; document "handlers must be re-runnable"),
  so retry and transactions coexist. `AuditingBehavior` runs OUTSIDE the transaction stage: Succeeded
  written via the pre-commit hook; Failed written after rollback (no ambient txn → no lock waits).
- 06.Persistence.Abstractions references `SharedKernel.Application.Abstractions` (legal: 06 → 01–05).
- DELETE: duplicate interfaces/records in 05.Behaviors and 06.Abstractions; 13.ServiceDefaults.Persistence
  adapters (`PersistenceUnitOfWorkAdapter`, `TransactionalPersistenceUnitOfWorkAdapter`,
  `AuditTrailWriterBridge`, `WithApplicationTransactionBehavior`, security actor/tenant bridge) — replaced
  by nothing (06 implements the shared contracts) plus at most ONE identity helper that implements
  `IRequestContext` over 12.Security `IUserContext`/`ITenantProvider` (lives in 13.ServiceDefaults,
  e.g. `AddSharedKernelRequestContext()`), which 05's AuthorizationBehavior also benefits from.
- Governance: remove/replace `UnitOfWorkInterfacesRemainDistinctPredicate` and update
  `PersistenceNeverReferencesApplicationOrSecurity` → persistence may reference
  `SharedKernel.Application.Abstractions` only (never MediatR, never `.Application`/`.Behaviors`/12.Security).

## D2. PostgreSQL-only; merge `.PostgreSQL` into `.EfCore`
- `SharedKernel.Persistence.EfCore` becomes the PostgreSQL EF Core package (references Npgsql EF provider
  and `SharedKernel.Persistence.Npgsql`). DELETE the `.PostgreSQL` package (move its content in:
  classifier, xmin, jsonb, pgvector helpers, RLS, migration helpers). Consider moving pgvector helpers
  to an optional `SharedKernel.Persistence.EfCore.Vector` only if Pgvector dependency is heavy; default: keep in.
- Naming: replace hand-rolled `SnakeCaseNamingConvention` with `EFCore.NamingConventions`
  (`UseSnakeCaseNamingConvention()`), keep only a 63-byte truncation convention if needed.
- Concurrency: every aggregate root gets `xmin` concurrency token by convention (no base-class
  requirement, no `RowVersion byte[]`); expected-version API for ETag/If-Match
  (`UpdateAsync(aggregate, expectedVersion)` or equivalent) → Conflict with current version.
- Classifier always registered; extended SQLSTATE map (A22), reusable from Dapper (classifier on
  `PostgresException` lives in `.Npgsql`).
- Retry ON by default (Npgsql execution strategy), one switch to opt out/configure. DELETE
  `WithTransientFaultRetry`, `TransientFaultRetryOptions`, `PersistenceRetryDiagnosticListener`,
  the Build/startup retry-vs-transaction guards.

## D3. One-line setup + DX (EfCore + Npgsql)
Target shape (names may be adjusted if clearly better; keep ONE obvious entry point):
```csharp
builder.AddSharedKernelPostgres<OrderDbContext>("orders", p => p
    .UseMultiTenancy(rowLevelSecurity: true)
    .UseAuditTrail()
    .UseFieldEncryption(k => ...));
public sealed class OrderDbContext(DbContextOptions<OrderDbContext> o, PersistenceContextDependencies d) : SharedKernelDbContext(o, d);
```
- Reads `ConnectionStrings:{name}` (Aspire/Testcontainers compatible) plus optional
  `SharedKernel:Persistence:{name}` options section. Honour explicit `SSL Mode` in the connection string;
  loopback host / Development may use Disable without extra acknowledgement; otherwise VerifyFull default.
  Validate at startup, not first query. DELETE string overloads, `periodicPasswordProvider` param;
  `configureDataSource` gets `IServiceProvider`. `EnableDynamicJson` opt-in.
- No terminal `.Build()` (callback style). Everything validated at startup (ValidateOnStart / build-time checks).
- Auto conventions always on: Money, StronglyTypedId auto-discovery (+ optional UUIDv7 via IIdGenerator),
  audit/soft-delete/tenant columns via conventions (not `EntityTypeConfigurationBase` inheritance; delete
  or thin the base), created-audit props `PropertySaveBehavior.Ignore` after save (A6).
- Open-generic `IRepository<,>`/`IReadRepository<,>` registered automatically; `EfRepository`/`EfReadRepository`
  concrete, subclassing optional. Repository shape: `IReadRepository` (no-tracking) and
  `IRepository : IReadRepository` (tracked + writes). DELETE `IRestorableRepository` (add `Restore()` on
  the soft-deletable aggregate base in 03.Domain), `GetByIdsChunkedAsync`. Bulk: soft-delete-aware
  `ExecuteDeleteAsync`, explicit `ExecutePurgeAsync`, auto Modified* setters, full-chain setter resolution (A5, A8).
- Multiple DbContexts per service: `IUnitOfWork` keyed per context (`IUnitOfWork<TContext>`-style),
  unkeyed = primary/first. DELETE read-replica routing (A26); document Npgsql multi-host instead.
- Domain events dispatched inside `SharedKernelDbContext.SaveChangesAsync` (all paths), startup warning/failure
  when aggregates raise events and no dispatcher registered (A24).
- Merge platform SaveChanges interceptors into one with single DetectChanges (A29); ConcurrencyInterceptor →
  internal translator; all concurrency exceptions → Conflict, tenant violation only when proven (A4).
- Delete: `PersistenceContextWiringValidator`, no-op builder methods, `AddBuildAction` public, scoped
  configuration scan predicate + `AdditionalConfiguredEntityTypes` (plain scan, opt-in filter hook) (A7).
- Public surface target: EfCore ≈ 70 entries. Interceptors/conventions/defaults internal.
- `ICrossTenantScope` → public factory/`Enter(reason)` on the injected service, actor auto-captured (A23).
- `IDbContextFactory` singleton-safe story (A-O8) — factory must not captive-capture scoped identity.
- Fix `ListKeysetProjectedAsync` server-side projection + cached accessors (A27);
  `ExecuteInTransactionAsync` tracker clearing (A28).

## D4. Row-level security + Npgsql + Dapper
- Transaction-local binding ONLY (`set_config(...,true)`), one statement for both settings, rebind only when
  (txn, tenant, scope) changed; RLS-enabled contexts require a transaction (the always-on transactional UoW
  path provides it; reads outside a transaction → open a short read txn or fail with a clear error — choose
  the safe, documented option). DELETE connection interceptor, `BindConnectionAsync`/`ResetConnectionAsync`.
  Validator rejects `Multiplexing`/`No Reset On Close` with RLS. Document PgBouncer transaction-mode support.
- DELETE escape token; policy = single predicate; cross-tenant = dedicated DB role (BYPASSRLS or role-specific
  policy) via a named data source; document honestly (RLS guards app bugs, not SQL injection). Startup
  self-check: runtime role not superuser/BYPASSRLS; fix Disable helper `NO FORCE`. Ship a role/grant script.
- Advisory locks: namespaced keys (`sk:migration:`, `sk:audit:`), fix lock_timeout leakage + clamp (A30);
  optional direct `MigrationConnectionString` for pooler setups.
- Dapper: replace 4 base classes with one injectable session service (connection + txn, ambient enlisted,
  tenant bound, async-only), plain Dapper extension methods on top; generic type-handler registration
  (`AddSmartEnum<,>`, `AddStronglyTypedId<,>`), JSONB handler with `JsonTypeInfo<T>`, pgvector; register
  identity defaults so Dapper-only services work; delete nested CLIENT spans + `NpgsqlTelemetryNames`.

## D5. Audit ledger v3 — async sealer (`SharedKernel.Persistence.EfCore.Auditing`)
- Request path: plain INSERT into append-only `audit_records` (no sequence, no hash, no lock, no retry).
  Succeeded → inside the business txn via the D1 pre-commit hook; Failed → own txn after rollback.
- Sealer: hosted service, leader-elected with `pg_try_advisory_lock` (namespaced), reads committed unsealed
  rows in commit-safe order, assigns per-chain sequence + prev hash + MAC into insert-only
  `audit_chain_links` (or sealed columns — pick the simplest immutable design), emits signed checkpoints
  to an `IAuditCheckpointSink` (default: store table; pluggable WORM sink). Configurable interval/batch.
  Must handle commit-order visibility safely (e.g. seal only rows older than a safety lag / by xid horizon).
- Chain key (TenantId, ResourceType); null tenant only under explicit system scope (A16).
- Format AUDITv3: big-endian GUIDs (RFC 9562), µs timestamps, presence flags, published byte-level spec
  + test vectors (doc file in package). Payload commitment: chain hashes `SHA-256(salt‖payload)`, payload +
  salt in erasable `audit_record_payloads` → GDPR erasure keeps chain intact, verify reports `PayloadErased`.
- Key rotation: keyring options (`CurrentKeyId`, `Keys{id:{Material,Order}}`), `IAuditRecordAuthenticator`
  (async, KMS-capable), verification result enum Intact/Broken/Unverifiable + failure kind, KeyRegression
  detection, `SealAllAsync` after compromise. Tests per reviewer list.
- Fix A15 (anchor re-hash), A17 (indexes), A19 (length validation, identity from context, trace_id),
  A20 (checkpoint scope/verify-before-sign/pinned key ids). Audit-of-audit reads for export/cross-tenant.
- DELETE regex `AuditRecordMutationGuardInterceptor`; keep cheap tracked-entity guard; startup self-check of
  privileges/triggers/ownership; `migrationBuilder.CreateAuditLedgerTable()` emits table+trigger.
- Move ledger query/verify/checkpoint contracts out of 06.Abstractions into the Auditing package.
- One-call setup (`.UseAuditTrail()`), validated at startup.

## D6. Encryption (`SharedKernel.Persistence.EfCore.Encryption`)
- Fix A9–A14 (recursive complex traversal, normalizer by name/flags, post-save state, rotation under RLS +
  row-count sanity + VerifyOnly, model-wide purpose uniqueness, complex shadow names, column sizing,
  stale-pending restore). Replace regex guard with `IQueryExpressionInterceptor` visitor (reject non-entity
  use of encrypted members incl. projections, LIKE, ordering, ExecuteUpdate setters).
- Key separation: per-purpose derived AES keys (cached per keyId+purpose). Separate, versioned blind-index key.
- Migration tools: rotation modes `ReEncrypt | RecomputeBlindIndexes | EncryptPlaintext | VerifyOnly`,
  replace global `AllowUnencryptedValues` with per-run mode. TPT/TPH correctness, batched CAS.
- `WhereEncryptedEquals(x => x.Email, value)` reading model annotations (+ spec criterion).
- Key-ring bridge: on-miss background refresh, staleness via probe, documented rollout protocol.
- DELETE `perTenantKey`. ADD per-tenant crypto-shredding: tenant data keys wrapped by KMS (01.Core envelope
  encryption), stored in a key table; `ShredTenantAsync` destroys the key.
- Setup: `.UseFieldEncryption(k => k.FromConfiguration() / UseKeyProvider<T>())` — no triple registration.

## D7. Specifications (03.Domain, published alpha)
- Inline builder (`Spec.For<T>().Where().Include().ThenInclude().OrderBy()`), typed ThenInclude,
  `ProjectionSpecification<T,TR>` base, paging at call site (`ListPagedAsync(spec, PageRequest)`,
  `ListKeysetAsync(spec, CursorPageRequest, ...)`) with typed afterId, `And`/`Or` no longer silently drop
  ordering (throw or merge), `ThenBy` without `OrderBy` throws. Keep existing subclass style working where cheap.

## Engineering rules for every wave
- Read repo `CLAUDE.md` (root) + `06.Persistence/CLAUDE.md` for conventions ([LoggerMessage] EventIds 6000-6999,
  magic-string constants, PublicAPI tracking is build-breaking, XML docs required, AddValidatedOptions).
- Build output is Turkish; use exit codes and `error CS`/`error MSB` greps. Docker required for container tests.
- Never `perl -i` with `local(@ARGV...)`; use the Edit tool for structural edits.
- Do NOT commit. Do NOT touch files owned by another concurrently running wave (listed in each brief).
- Tests: update/replace tests for everything changed; add regression tests for every A-finding fixed.
  Reuse encryption probe project `scratchpad/encprobe/` as a source of regression tests.
- End each wave with: build of touched projects green, their tests green (container suites run in isolation
  if flaky), and a log file `scratchpad/p558-<wave>-log.md` listing what changed, deviations, open items.
