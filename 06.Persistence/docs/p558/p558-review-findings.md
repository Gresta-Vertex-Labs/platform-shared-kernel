> Durable copy of a P-558 session record (2026-09-21), copied verbatim from the session scratchpad. Paths such as
> `scratchpad/...` refer to that temporary folder and no longer exist. Code wins where this record and the code disagree.

# 06.Persistence — pre-publish deep review (2026-09-21)

Five independent read-only reviewers (Abstractions/DX, EfCore, Auditing, Encryption, Npgsql/PostgreSQL/Dapper).
Legend: ✔ = verified in code by me or reproduced by reviewer probe; ~ = strong trace, not executed.
Encryption probes (Testcontainers) live in `scratchpad/encprobe/` — reusable as regression tests.

## A. Correctness / security bugs (fix regardless of design choices)

| ID | Pkg | Sev | Finding | Fix |
|---|---|---|---|---|
| A1 ✔ | Npgsql/PG | Critical | RLS binds tenant with session-scoped `set_config(...,false)` on connection open. Under PgBouncer transaction/statement pooling (Azure Flexible built-in, Supabase, many k8s setups) the setting survives on the backend → next client reads another tenant's rows. `Multiplexing`/`No Reset On Close` also break it. Not documented anywhere. | Transaction-local binding only (`is_local=true`), rebind per command in txn; delete connection interceptor + `BindConnectionAsync`/`ResetConnectionAsync`; validator rejects Multiplexing/NoResetOnClose when RLS on. |
| A2 ✔ | PG | High | Cross-tenant escape token is a literal inside the policy text → readable by any role via `pg_policies`. Protects nothing; also must live in a migration file. OR-branch likely defeats tenant_id index use (~, EXPLAIN pending). | Delete token. Policy = `tenant_id = NULLIF(current_setting('app.tenant_id',true),'')::uuid`. Cross-tenant = separate DB role (BYPASSRLS / role-specific policy) via named data source. |
| A3 ✔ | PG | High | Superuser/BYPASSRLS/owner silently bypass RLS (local docker `postgres`). `DisableTenantRowLevelSecurity` never issues `NO FORCE`. | Startup self-check `rolsuper OR rolbypassrls`; ship role script; fix disable helper. |
| A4 ✔ | EfCore | High | `ConcurrencyInterceptor.TryTranslate`: TenantId is a concurrency token on tenanted types, 4/5 tenanted bases lack `IHasConcurrency` → any concurrent delete/update race becomes 403 "tenant isolation violation" + security metric noise. Non-tenant, non-concurrency → raw 500. | All `DbUpdateConcurrencyException` → Conflict; report tenant violation only when proven. |
| A5 ✔ | EfCore | High | `ExecuteDeleteAsync` hard-deletes `ISoftDeletable` aggregates (doc justifies it with a false "no server-side translation"). `ExecuteUpdateAsync` never stamps Modified*. | Soft-delete via ExecuteUpdate for ISoftDeletable; explicit `ExecutePurgeAsync`; auto audit setters. |
| A6 ✔ | EfCore | High | `Set.Update(detached)` marks CreatedBy/CreatedOn modified; AuditInterceptor never protects them → creation provenance overwritten. | `PropertySaveBehavior.Ignore` after-save on created-audit props via convention. |
| A7 ✔/~ | EfCore | High | Scoped `ApplyConfigurationsFromAssembly` predicate caches exposed types at first call: (a) context with no DbSets skips ALL configurations ✔; (b) types pulled in by an earlier configuration lose their own config incl. `.Encrypt()` → silent plaintext ~. | Plain unfiltered scan by default; opt-in `ShouldApplyConfiguration(Type)`; delete `AdditionalConfiguredEntityTypes`. |
| A8 ~ | EfCore | High | `UpdateSettersInspector` returns last member name only → `x => x.Contact.Ssn` resolves "Ssn", `FindProperty` null → encrypt guard passes → plaintext into encrypted column. Uses EF1001 internal API. | Resolve full member chain incl. complex properties; fail closed. |
| A9 ✔ | Encryption | Critical | `.Encrypt()` on a NESTED complex type stores plaintext silently (all traversals one level deep; guard misses it). Complex collections ~crash. | One recursive traversal helper shared by convention/guard/interceptor/rotation, or reject at model build. |
| A10 ✔ | Encryption | Critical | `.WithBlindIndex(normalize)` stores a `Func` as model annotation → `dotnet ef migrations add` fails ("Cannot scaffold C# literals of type Func"). Compiled models likely too. | Normalizer by name/flags enum (`Trim|CaseFold|...`) or registered `IBlindIndexNormalizer`. |
| A11 ✔ | Encryption | High | After every SaveChanges the entity stays `Modified` with ciphertext as OriginalValue → next save re-encrypts, rewrites, bumps xmin, re-stamps ModifiedBy (false audit). Every Modified entry re-encrypts ALL encrypted props (nonce budget, rotation CAS collisions). | Restore order fix + reset IsModified; skip untouched props. |
| A12 ~ | Encryption | Critical | Rotation opens raw connection → RLS never bound → FORCE RLS returns 0 rows → `Completed=true`, 0 remaining → operator retires key → data unreadable. | Open via EF + cross-tenant; row-count sanity check; VerifyOnly mode. |
| A13 ✔ | Encryption | High | Direct `ExecuteUpdate(SetProperty(x=>x.Email,"plain"))` writes plaintext (poison row breaks every list query). Regex guard misses LIKE/!=/OrderBy/GroupBy, false-positives on any column named `email` anywhere. Projections return ciphertext silently. | Replace regex `DbCommandInterceptor` with `IQueryExpressionInterceptor` visitor rejecting any non-entity use of encrypted members. |
| A14 ✔ | Encryption | Medium | Same purpose on two tables allowed + AAD lacks table identity → ciphertext swappable across tables with int keys. `WhereBlindIndexEquals` wrong shadow name for complex props. Doc example `HasMaxLength(20).Encrypt()` fails on insert (~58–110 chars stored). Possible double-encryption if a later SavingChanges interceptor throws ~. | Model-wide purpose uniqueness; fix complex shadow name; force `text` or compute size; restore stale pending at start. |
| A15 ✔ | Auditing | High | `VerifyChainFromCheckpointAsync` never re-hashes the anchor record → anchor content (snapshots, actor) editable while reporting Intact. | Recompute anchor MAC. |
| A16 ✔ | Auditing | High | Null tenant treated as "system chain" (fail-open): unresolved tenant can read/export system chain; writes land there silently. | Explicit system scope; throw otherwise. |
| A17 ~ | Auditing | High | Indexes lead with generated `chain_key` but every query filters tenant_id/resource_type → seq scans; verification hit hardest. | Filter on chain_key or `(tenant_id,resource_type,sequence) UNIQUE NULLS NOT DISTINCT`. |
| A18 ✔ | Auditing | High | Audit runs inside TransactionBehavior → SaveChanges/Commit failure never produces a Failed record; nested/audit-then-fail hits H8. Failed writes time out (5s lock) behind ANY open business txn on the same chain → the most important records dropped (only logged). | Auditing outside transaction; Succeeded via pre-commit hook; Failed after rollback. |
| A19 ✔ | Auditing | Med | Oversized AuditEntry field (e.g. ResourceId>200) → 22001 inside ambient txn → business write aborted. `OccurredOn` can go backwards vs Sequence. Identity fields (ImpersonatorId, SessionId, ClientId, SourceService) caller-supplied and never populated by the bridge. | Validate lengths; rollback to savepoint on any error; resolve identity from actor context/options. |
| A20 ✔ | Auditing | Med | Checkpoint service: caller-supplied tenant without cross-tenant check; signs a possibly tampered head; verifier trusts caller's signing key id. | Scope gate; incremental verify before signing; pinned accepted key ids. |
| A21 ✔ | EfCore/DX | Critical | Retry + transactional UoW rejected at Build AND startup; audit requires transactional UoW → every audited service must run WITHOUT transient-fault retry. Error messages recommend an impossible config. `WithTransientFaultRetry()` doesn't even enable retry (needs `UsePostgreSQL(maxRetryCount)` too). | Always register transactional UoW; TransactionBehavior uses `ExecuteInTransactionAsync` (eShop pattern); one retry switch, on by default. |
| A22 ✔ | PG/DX | High | Exception classifier (unique→409, FK→400) registered only by `AddSharedKernelPostgreSQL`; documented path `AddSharedKernelNpgsql + UsePostgreSQL(sp)` silently yields 500s. Missing SQLSTATEs: 23502, 23514, 23P01, 22001, 55P03, 57014, 42501. Mixed batch misclassified. | One entry point always registers it; reusable from Dapper. |
| A23 ✔ | Abstractions | High | `ICrossTenantScope` has no `Enter`; only concrete singleton does; `new CrossTenantScope().Enter()` is a no-op (per-instance AsyncLocal); doc sample doesn't compile. | `ICrossTenantScopeFactory.Enter(reason)` (or on interface), actor auto-captured, reason mandatory. |
| A24 ✔ | EfCore | High | Domain events dispatched only via IUnitOfWork; direct `DbContext.SaveChanges` (seeders, factory users, MassTransit outbox, sync save) skips them; with no dispatcher registered events are cleared silently. | Dispatch inside `SharedKernelDbContext.SaveChangesAsync`; warn/fail when no dispatcher. |
| A25 ✔ | EfCore | Med-High | Optimistic concurrency token (`RowVersion byte[]`) never regenerated outside PostgreSQL xmin → no lost-update detection on other providers; only applies via `EntityTypeConfigurationBase`. | Decide PG-only, or app-managed token; conventions instead of base class. |
| A26 ✔ | EfCore | Med | Read replica returns TRACKED entities from replica context (saves on primary persist nothing); replica options miss compiled model/timeout/logging. | Delete replica routing (see D). |
| A27 ✔ | EfCore | Med | `ListKeysetProjectedAsync` projects in memory with `Compile()` per call (doc promises SQL projection). | Server-side Select; cache compiled accessors. |
| A28 ✔ | EfCore | Med | `ExecuteInTransactionAsync` clears change tracker on FIRST attempt → silently discards staged changes. | Clear on retry only; throw if HasChanges on attempt 1. |
| A29 ✔ | EfCore | Med | O(N²) DetectChanges: SoftDelete rescue & Touch call `ChangeTracker.Entries()` per node. | One platform SaveChanges interceptor, one DetectChanges, snapshot. |
| A30 ✔ | Npgsql | Med | Advisory xact lock sets `lock_timeout` local for the rest of the txn (not just acquisition); sub-ms timeout → 0 = wait forever. | Restore previous value / poll try-lock; clamp ≥1ms. |
| A31 ✔ | Dapper | Med | TenantSafe services use sync Begin/Commit/Rollback in async methods; rollback can mask original error; doc says read-only txn, isn't. Configured timeout never reaches services (raw options ctor param). | Async APIs; IOptions or drop option. |
| A32 ~ | PG | Med | `useVector` on shared data source path never calls ADO-level `UseVector()`; only test uses connection-string overload. | Wire on data source; integration test via `UsePostgreSQL(sp)`. |
| A33 ✔ | Docs | High | 06.Persistence/README.md is 0 bytes; CLAUDE.md canonical wiring doesn't compile; EfCore README describes pre-P-557 bridges; `AddOrderBy` vs `ApplyOrderBy`; restore sample always null. | Generate samples from a compiled ConsumerVerify test. |
| A34 ✔ | CI | High | 3 new test projects (EfCore.Auditing/Encryption/Npgsql .Tests) absent from both slnf → CI red, never run. | Add to Integration.slnf. |

## B. Delete / over-engineering

- `WithTransientFaultRetry`, `TransientFaultRetryOptions`, `PersistenceRetryDiagnosticListener` (no-op + thread-unsafe listener).
- Read-replica routing in EfCore (use Npgsql multi-host / separate read context).
- `ConcurrencyInterceptor` (only calls base) → internal translator. `PersistenceContextWiringValidator` hosted service. `WithDbContextFactory()`/`RequireDbContextFactory()` no-ops. `AddBuildAction` public.
- Both regex SQL guards (audit mutation guard, encrypted equality guard) → DB privileges + trigger + startup self-check (audit); expression visitor (encryption).
- `perTenantKey` (docs overclaim; AAD already binds tenant).
- RLS escape token; RLS connection interceptor.
- Hand-rolled `SnakeCaseNamingConvention` → `EFCore.NamingConventions` (overrides explicit ToTable/HasColumnName, skips views, different digit rule — must decide before any consumer migration).
- `AddSharedKernelNpgsql(string)`, `UsePostgreSQL(string)`, `AddSharedKernelPostgreSQL(string)` (bypass VerifyFull/validation/disposal), `periodicPasswordProvider` param.
- Dapper: 4 inheritance base classes (~800 LOC pass-through) → one injectable session (`IDbSession.OpenAsync` returning connection+txn, ambient enlisted, tenant bound), plain Dapper on top. Dead `RegisterPlatformDefaults`/`Register`; per-type handler subclasses → generic registration.
- Dapper CLIENT spans nested inside Npgsql's; `NpgsqlTelemetryNames` (Npgsql 8+ has built-in source).
- `EnableDynamicJson()` unconditional → opt-in.
- `IRestorableRepository` → `Restore()` on the aggregate; `GetByIdsChunkedAsync`.
- Merge `IUnitOfWork`/`ITransactionalUnitOfWork` and `EfUnitOfWork`/`EfTransactionalUnitOfWork`.
- Public surface: EfCore 201 → ~70 (interceptors, conventions, Null/Anonymous contexts, meter internal; `PersistenceContextDependencies` ctor internal).
- Audit ledger contracts (~140 of 254 Abstractions API lines) → move into Auditing package; ADO.NET plumbing seams (`ITenantSessionBinder`, `IAdvisoryTransactionLock`, `IAmbientDbTransaction`, `IMigrationLock`) out of the app-facing namespace; `CrossTenantScope` impl + meter out of Abstractions.
- Pgvector forced on every PG consumer → `.PostgreSQL.Vector` split (optional).
- Model convention factories run per context construction.

## C. Missing features (candidates)

- One-line entry point with defaults (retry on, classifier, snake_case, xmin, Money + StronglyTypedId conventions, open-generic repositories, health, OTel) reading `ConnectionStrings:{name}` (Aspire/Testcontainers compatible); local-dev SSL without double acknowledgement for loopback.
- Open-generic `IRepository<,>`/`IReadRepository<,>` (no per-aggregate subclass pair).
- Strongly-typed ID auto-convention (+ optional UUIDv7 client key generation).
- Expected-version concurrency API (`UpdateAsync(agg, expectedVersion)`) pairing with 14.Presentation ETag/If-Match; concurrency token on every aggregate by convention.
- Multiple DbContexts per service (`IUnitOfWork<TContext>`).
- Aggregate-boundary touch for non-owned child entities (or delete the interceptor).
- `GetByIdAsync` loads full aggregate (AutoInclude guidance / aggregate query hook).
- Soft-delete-aware unique index helper; seeder cross-tenant handling; configurable migration lock timeout.
- Spec ergonomics (03.Domain, published): inline builder, typed ThenInclude, `ProjectionSpecification<T,TR>` base, paging at call site, typed `afterId`.
- Audit: keyring rotation (design in reviewer report: keyring options, `IAuditRecordAuthenticator` async/KMS-capable, status enum Intact/Broken/Unverifiable, KeyRegression check, SealAll after compromise), big-endian GUID + AUDITv3 + published byte spec, payload commitment table for GDPR erasure, W3C trace_id, audit-of-audit-reads, signed offline export bundle, partitioned retention, `CreateAuditLedgerTable()` migration helper, `IAuditContext` for before/after instead of request-supplied snapshot.
- Audit architecture option: async sealer (request path = plain INSERT; background leader seals chain) — removes advisory lock, savepoint retry, lock timeout, H8, dropped Failed records.
- Encryption: plaintext→encrypted migration + blind-index backfill (rotation modes ReEncrypt|RecomputeBlindIndexes|EncryptPlaintext|VerifyOnly), separate blind-index key (versioned `v1:` prefix), per-purpose derived AES keys (key separation / nonce budget), `WhereEncryptedEquals(x=>x.Email, value)` reading model annotations, key-ring on-miss refresh + probe staleness, TPT/TPH rotation correctness + batched CAS, per-tenant crypto-shredding (L), byte[]/DateOnly/decimal support.
- Npgsql: PgBouncer guidance + direct `MigrationConnectionString`, namespaced advisory keys, one-statement tenant bind (1 RTT), `MapEnum` hook with IServiceProvider, Dapper `JsonbTypeHandler<T>` + pgvector, keyed read-replica factory for Dapper.
- Composed ConsumerVerify test against Postgres (multi-tenancy + RLS + audit + encryption + bridges + retry).
