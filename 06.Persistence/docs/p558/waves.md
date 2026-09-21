# P-558 — wave summary

Persistence gold-standard pass 2, 2026-09-21, branch `persistence-gold-pass-2` (base `d11f376f`). Summaries of the
per-wave session logs (the raw logs stayed in the session scratchpad). Finding IDs (A*, C*, S*, F*) refer to
[`p558-review-findings.md`](p558-review-findings.md) and [`p558-final-review-findings.md`](p558-final-review-findings.md);
design sections (D1–D7) to [`p558-design.md`](p558-design.md).

| Wave | Commit(s) | Scope |
| --- | --- | --- |
| Review | — | Five read-only reviewers (Abstractions/DX, EfCore, Auditing, Encryption, Npgsql/PostgreSQL/Dapper) → 34 correctness/security findings (A1–A34), a delete list (B) and candidate features (C). Owner decisions: async audit sealer, merged 05/06 contracts, PostgreSQL-only, all additions, no state-map phasing |
| 1a (D1) | `42afa74d` | Shared contracts |
| 1b (D2) | `b38f451e` | PostgreSQL-only |
| 2 (5 parallel streams, worktrees under `C:\wt`) | `6b3e92b1` (N), `d15e3731` (X), `4f599cd1` (A), `87e2aea6` (E2), `94122ba1` (E1), merges `41854561`, `ff2455df`, `f74f3dac`, `c1bd365d`, follow-ups `ed4f5a93` | Npgsql/Dapper/RLS, encryption v3, audit ledger v3, repositories/specifications, EfCore platform |
| 3a | `d46b555d` | Polish |
| Final review | — | Correctness (C1–C8), security (S1–S8) and DX (F1–F17) reviews with probe projects |
| 3b (R1, R2 in parallel) | `7b716e7d`, `54ade33a`, `53c3507d`, `473b418e`, merge `6f8a7b4c` | Remediation of the final review |
| 3c | `5749401c`, `f571a8f5`, `340c5fa0` | Post-merge follow-ups, namespace consolidation, `SharedKernel.Persistence.Testing` |
| Docs | this commit series | Brains, READMEs, changelog, handoff, these records |

## Wave 1a — shared contracts (D1)

- New package `05.Application/SharedKernel.Application.Abstractions` (MediatR-free, references only `Primitives`):
  `IRequestContext` (+ `ActorKind`, default-implemented `ClientId`/`SessionId`/`ImpersonatorId`), the one
  `IUnitOfWork` (`ExecuteInTransactionAsync`, `OnBeforeCommit`, `IsTransactionActive`; no `BeginTransactionAsync`),
  `IAuditTrailWriter`/`AuditEntry` (init record, `AuditOutcome`)/`AuditOutcome`. `SharedKernel.Application`
  type-forwards the context types.
- `TransactionBehavior` runs the handler inside `ExecuteInTransactionAsync` (retry-safe; truncates `OnCompleted`
  callbacks of a discarded attempt). Auditing split: outer `AuditingBehavior` records failures after rollback,
  internal `AuditingCommitBehavior` queues `Succeeded` on `OnBeforeCommit` (fixes A18). Command stage order:
  CommandScope → Idempotency → Auditing → Transaction → AuditingCommit → custom.
- Deleted the duplicate 05/06 seams (`ITransactionalUnitOfWork`, `IPersistenceTransaction`, `ICurrentActorContext`,
  `ICurrentTenantContext`, both `IAuditTrailWriter`s) and every `13.ServiceDefaults.Persistence` bridge; new
  `SharedKernel.ServiceDefaults.Security` (`AddSharedKernelRequestContext()`). `EfUnitOfWork` became the one
  implementation (fixes A28: clear only before a retried attempt).
- Governance: `UnitOfWorkSeamRules.SharedContractsAreNotRedeclared`; persistence layering allows only
  `Application.Abstractions`.

## Wave 1b — PostgreSQL-only (D2)

- `SharedKernel.Persistence.PostgreSQL` merged into `.EfCore` (project deleted); tests split into
  `EfCore.Tests` (unit) and `EfCore.Integration.Tests` (containers).
- snake_case via `EFCore.NamingConventions` (+ 63-byte truncation and `OwnedSharedTableKeyColumnConvention`);
  explicitly configured names are now kept verbatim.
- SQLSTATE classifier in `.Npgsql` (EF-free, reusable by Dapper), always registered by EfCore, extended map (A22).
- Retry on by default; `WithTransientFaultRetry`, string overloads and the retry-vs-transaction guards deleted (A21).
- `EnableDynamicJson` and `UseVector` opt-in on the data source (A32).

## Wave 2 — five streams

- **N (Npgsql + Dapper + RLS):** transaction-local tenant binding only (A1) — `DO`-block prefix outside a
  transaction, bind-once inside, `SaveChanges` forced into a transaction; escape token deleted, one-predicate policy,
  cross-tenant = separate DB role and keyed data source (A2); privilege startup check, `NO FORCE` in the disable
  helper (A3); namespaced advisory keys, `lock_timeout` restore/clamp (A30); Dapper's four base classes replaced by
  `IDbSessionFactory`/`IDbSession` (A31); keyed migration/read-only/cross-tenant data sources.
- **X (encryption v3):** recursive complex traversal (A9), normalizer by flags/name (A10), post-save state (A11),
  rotation under RLS with row-count sanity and `VerifyOnly` (A12), LINQ query guard instead of the regex guard (A13),
  model-wide purpose uniqueness and column sizing (A14); per-purpose HKDF keys, versioned blind-index keys,
  maintenance modes, key ring with on-miss refresh and staleness probe, per-tenant crypto-shredding
  (`perTenantKey` deleted).
- **A (audit ledger v3):** plain INSERT on the request path, async sealer in commit-safe xid order, AUDITv3 byte
  spec with test vectors, keyring rotation with `KeyRegression` detection, `SealAllChainsAsync`, anchor re-hash
  (A15), explicit system scope (A16), query-shaped indexes (A17), length validation and identity from context
  (A19), scope-gated verify-before-sign checkpoints (A20), GDPR payload erasure, audit-of-audit, triggers +
  privileges + self-check; contracts moved from `.Abstractions` into the package.
- **E2 (repositories + specifications):** `Spec.For<T>()` builder, typed `ThenInclude`, `ProjectionSpecification`,
  composites that never drop ordering silently, `Restore()`; read repositories never track, write repositories
  always do; call-site paging (`Paged`/`KeysetSpecification` deleted); server-side keyset projection with cached
  accessors (A27); bulk soft delete + purge + `Modified*` stamps (A5); full-chain setter resolution (A8); open-generic
  repository registration.
- **E1 (EfCore platform):** `AddSharedKernelPostgres` one-line entry point (no `.Build()`); per-lease identity;
  multi-context support (`IUnitOfWork<TContext>`, keyed); `ICallerDbContextFactory`; conventions instead of the
  configuration base class; one save interceptor (A29), created-audit columns immutable (A6), plain configuration
  scan (A7), all concurrency exceptions → Conflict (A4), domain events dispatched on every save (A24), `xmin` on
  every aggregate root, `ConcurrencyVersion`; read-replica routing deleted (A26); `ICrossTenantScope.Enter(reason)`
  (A23).
- **Integration:** merge conflict resolution, a captive-dependency bug found and fixed, loopback TLS default,
  migrations over the migration data source, seeders under RLS on the cross-tenant connection, packed
  ConsumerVerify 7/7 incl. the composed multi-tenancy + RLS + audit + encryption + retry scenario.

## Wave 3a — polish

Public API trimmed (EfCore 180 → 134 lines; Npgsql, Dapper, Auditing, Encryption, ServiceDefaults.Security
internals); sibling packages reach EfCore internals via IVT with exact-version nuspec pins; `CrossTenantScope` state
moved from `AsyncLocal` to the per-DI-scope instance (an `Enter` inside an awaited helper now holds for the scope);
`EntityTypeConfigurationBase`, `UseCommandTimeout`, `UseCompiledModel`, `UseStartupLockTimeout` deleted; RLS policy
names truncated consistently; FK classification on referenced-key updates; zero warnings in 06.

## Final review → wave 3b remediation (R1, R2)

- **R1:** one transaction per DI scope (`UnitOfWorkCoordinator`: every joinable context enlisted and saved,
  rollback-only joins, ambient transaction restored — C1, C2, C6); ambiguous commits never replayed (C3,
  `CommitOutcomeUnknownException`); detached shadow-`xmin` writes require an expected version (C4); grandchild root
  touch (C5); nullable keyset keys rejected (C7); throwing dispatcher clears the tracker (C8); every entity of a
  tenanted model tenant-scoped or `[TenantShared]` (S1), model-driven RLS migration helpers and coverage check
  (S4/F6), `ProtectedColumnUpdateGuard` (S6), no 403/409 existence oracle (S8); `IPersistenceStartup` ordering (F1),
  Money by convention (F4), design-time factory (F7), distinct readiness checks (F10), one "Postgres" spelling (F12),
  `EntityVersion` and bulk contract moved to Abstractions (F13), `ICallerDbContextFactory` overloads (F14), actionable
  tenant messages (F15).
- **R2:** maintenance/shredding require a caller-entered scope (S2); shredding completeness and tombstone re-check
  in the write transaction (S3); privilege check covers role membership and non-tenant permissive policies (S4);
  sealer without a trusted watermark + optional separate sealer role (S5); `ActorKind.Anonymous` (S7); one canonical
  role script and `CreateAuditLedgerTable(runtimeRole, sealerRole)` (F2); one configuration shape
  `SharedKernel:Persistence:{name}` (F9); `PostgresClassifiedErrorCodes` (F12); Npgsql command log at Debug (F16);
  README samples compiled by tests.

## Wave 3c — code

RLS coverage check on the shared catalog query; SK0201 requires `base.OnModelCreating` only; section-only
configuration and empty-assembly tests; **namespace consolidation** (every registration in `SharedKernel.Persistence`,
EF helpers in `SharedKernel.Persistence.EfCore`, enforced by `PersistenceNamespaceConventionRules`; a sample service
went from 12 to 7 persistence usings); **`SharedKernel.Persistence.Testing`** (packable fakes + `PostgresTestServer`/
`PostgresTestDatabase` with the canonical roles; guarded against production references). Verified: full Release build
0 errors, every suite green, packed ConsumerVerify 06 9/9 and 05 6/6.
