> Durable copy of a P-558 session record (2026-09-21), copied verbatim from the session scratchpad. Paths such as
> `scratchpad/...` refer to that temporary folder and no longer exist. Code wins where this record and the code disagree.

# Final review findings at d46b555d (wave 3b) — remediation input

Probe projects (reusable as regression tests):
- correctness: scratchpad/final-review-corr/ (Probe.cs, Multi.cs)
- security:    scratchpad/final-review-sec/ (Probe.cs)
- DX samples:  scratchpad/final-review-dx/ (SampleA, SampleB, SampleC)

## Correctness (C)
- C1 High: multi-context — TransactionBehavior/AuditingCommitBehavior inject unkeyed IUnitOfWork (first context); repos for context B stage changes that are never saved. Fix: enlist every context resolved in the scope that has changes into one transaction on a shared connection (same data source) and save them all at commit; if contexts use different data sources, throw loudly if a non-committing context HasChanges() at commit. Startup warning otherwise.
- C2 High: shared scoped AmbientDbTransactionAccessor is overwritten/cleared by a nested UoW on another context (EfUnitOfWork.cs:175,209). Fix: stack restore in finally; refuse or enlist nested ExecuteInTransactionAsync on a different context while another is active (prefer enlist via C1's shared-connection mechanism).
- C3 High: commit failure with unknown outcome is retried → double apply. Fix: commit failures become non-retriable `CommitOutcomeUnknownException` (or commit-marker verification). Document.
- C4 Med-High: detached UpdateAsync/UpdateRangeAsync/DeleteAsync on roots with shadow xmin always Conflict (original 0). Fix: detached roots require an expected version (throw clear InvalidOperationException pointing at the overload) — or load current token; fix docs; test across all aggregate bases.
- C5 Med: grandchild change doesn't touch the root (FindParent only crosses FK when principal is an aggregate root). Fix: walk required FKs through any tracked principal until an aggregate root (visited set).
- C6 Med: nested command failed Result leaves staged changes that the outer commit persists. Fix: rollback-only marking for joined failures (outermost rolls back / throws) or savepoint + tracker revert.
- C7 Low: keyset paging with nullable reference key skips rows. Fix: reject nullable columns (model IsNullable) or null-aware seek.
- C8 Low: direct SaveChangesAsync with throwing dispatcher loses events. Fix: restore batch or clear tracker on dispatch exception.

## Security (S)
- S1 High: child entities without IHasTenant in a TenantedDbContext have no filter/guard/RLS; cross-tenant read and detached-graph hijack confirmed. DECISION: at model finalization in TenantedDbContext, every non-owned entity type must implement IHasTenant unless explicitly marked shared (e.g. `[TenantShared]` attribute or builder `.IsTenantShared()`), in which case it is documented as global reference data (no tenant filter, read-only by convention). Children get the same filter + write guard + TenantId concurrency token + RLS policy as roots. The save interceptor also sets a child's TenantId from the current tenant on Added if unset, and rejects mismatches.
- S2 Med-High: ShredTenantAsync (and IEncryptionRotationJob.RunAsync) self-enter the cross-tenant scope with no authorization. Fix: require an already-active ICrossTenantScope (like SealAllChainsAsync); never enter inside the API.
- S3 Med: shredding incomplete — root-key legacy values still decrypt; other instances cache the unwrapped key up to 5 min and keep writing (fresh blind indexes). Fix: ShredTenantAsync fails/reports when non-tenant-key payloads remain; refuse root-key decryption for shredded tenant under TenantDataKeys; re-check tombstone in the write transaction (or short cache + coordinated shred); maintenance counts reported honestly.
- S4 Med: RLS privilege check misses membership in the cross-tenant policy role (pg_has_role MEMBER); never verifies relrowsecurity/relforcerowsecurity + policy on every IHasTenant table. Fix both (also DX F6).
- S5 Low-Med: forged audit_chain_links row with max insert_xid stalls sealer; probe reports 0 backlog. Fix: sealer/probe select NOT EXISTS(link) rows below horizon instead of trusting max watermark (or separate sealer role/data source + REVOKE INSERT on links/checkpoints from app role — document).
- S6 Low: direct DbSet ExecuteUpdate can set TenantId / created-audit / concurrency token. Fix: core IQueryExpressionInterceptor guard like encryption's.
- S7 Low: anonymous callers audited as System. Fix: SecurityRequestContext maps anonymous to a distinct kind / "anonymous"; record authenticated flag.
- S8 Low: 403 vs 409 cross-tenant existence oracle without RLS. Fix: same response (Conflict/NotFound) for both unless RLS on.

## DX (F)
- F1 Critical: Auditing self-check + sealer run before MigrateOnStartup → Fail blocks fresh deploy; Warn spams. Fix: hosted services wait on migration completion (a persistence startup-completion signal), or Fail downgrades "missing table" when MigrateOnStartup registered.
- F2 High: README role scripts contradict (Npgsql default privileges grant UPDATE/DELETE → audit self-check fails). Fix: single canonical role script incl. audit REVOKEs; `CreateAuditLedgerTable(runtimeRole:)` issues REVOKEs.
- F3 High: READMEs show removed APIs (AddSharedKernelEfCore/WithMultiTenancy/Build, ITransactionalUnitOfWork, KeysetSpecification, …); 06 README empty → wave 3c docs (compile-checked samples).
- F4 High: Money not mapped by convention (README claims it is). Fix: map automatically, or platform error naming `builder.Money(e => e.Price)`; README truth.
- F5 High: no consumer testing package (SharedKernel.Testing not packable). → USER DECISION pending.
- F6 High: = S4 second half.
- F7 Med: CLI migrations with split roles (dotnet ef uses runtime conn string). Fix: design-time factory reading MigrationConnectionString + README recipe (Design package, idempotent script).
- F8 Med: MigrateOnStartup doesn't create DB. Fix: document; optionally create in Development when owner.
- F9 Med: config section confusion (`SharedKernel:Persistence:{name}` vs `SharedKernel:Persistence:Npgsql`). Fix: one config model + table in README.
- F10 Med: readiness: MigrateOnStartup doesn't gate StartupGate; DB readiness checks share name "database" (duplicate throws); no checks for encryption key-ring probe / audit sealing probe. Fix: distinct names, AddFieldEncryptionReadinessCheck, AddAuditSealingReadinessCheck, migration gates readiness.
- F11 Med: namespace sprawl (22 usings). Fix: consolidate consumer-facing registration/builder extensions into fewer namespaces (decide after R1/R2 land).
- F12 Med: Postgres vs PostgreSQL vs PostgreSql spelling; two extension classes. Fix: one spelling ("Postgres"), merge classes.
- F13 Med: xmin `uint` leaks into IRepository/ConcurrencyVersion — use opaque version type (ETag-friendly); Abstractions plumbing (ITenantSessionBinder, IAdvisoryTransactionLock, IAmbientDbTransaction, IMigrationLock, CrossTenantScope ctor) → internal/infrastructure; Npgsql TenantSessionSql internal; EfReadRepository.Query public IQueryable → protected/internal; IBulkMutationRepository into Abstractions; 13.ServiceDefaults.Persistence/Security PublicAPI tracking.
- F14 Low-Med: ICallerDbContextFactory.CreateDbContextAsync param order (ct second / overload).
- F15 Low-Med: garbled tenant-write message; actionable text.
- F16 Low: log noise (no-configurations log, Npgsql commands at Information, triple-logged exception, anonymous maintenance actor).
- F17 Low: OnDelete abstract on soft-deletable bases; ApplyThenBy vs ApplyOrderBy asymmetry; Domain README contradictions.
