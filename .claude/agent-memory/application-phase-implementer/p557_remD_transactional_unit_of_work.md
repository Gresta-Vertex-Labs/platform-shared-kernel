---
name: p557_remD_transactional_unit_of_work
description: P-557 remediation wave D (2026-09-20) — added an opt-in ITransactionalUnitOfWork capability to 05.Application.Behaviors' local IUnitOfWork seam to fix audit records committing independently of the business write they attest to
metadata:
  type: project
---

Fixed a real, shipped-class defect (C2 from an adversarial P-557 review): `TransactionBehavior`
(`05.Application.Behaviors/Transaction/`) never opened a database transaction — it only ever called
`IUnitOfWork.SaveChangesAsync`. `06.Persistence.EfCore.Auditing`'s `EfAuditTrailWriter` requires an
ambient transaction (`IAmbientDbTransaction.Current`) to record a `Succeeded`-outcome entry, and that
accessor is populated ONLY by `06`'s `EfTransactionalUnitOfWork.BeginTransactionAsync`/
`ExecuteInTransactionAsync` — neither of which any `05`/`13` production code ever called. Net effect: an
audited command's "Succeeded" attestation could commit (standalone) before, and independently of, the
business write it described. A prior wave (B) added a throw-if-no-ambient-transaction guard to
`EfAuditTrailWriter`, which is the correct posture but left the happy path fully blocked until this fix.

**Fix shape — an opt-in capability layered onto the existing local seam, not a new seam:**
`05.Application.Behaviors/Transaction/ITransactionalUnitOfWork : IUnitOfWork` (one member,
`BeginTransactionAsync(CancellationToken) -> IPersistenceTransaction`) + a matching
`IPersistenceTransaction : IAsyncDisposable` (`CommitAsync`/`RollbackAsync`). `TransactionBehavior.Handle`
does a runtime `unitOfWork is ITransactionalUnitOfWork` check (after the existing nested-command
short-circuit) and, when true, opens the transaction BEFORE calling `next()` so `AuditingBehavior`
(registered inner to `TransactionBehavior` in the canonical command stage) can enlist. Both interfaces
follow the domain's existing same-name-different-namespace bridge convention (matching `IUnitOfWork`/
`IAuditTrailWriter`/`AuditEntry`) — `06.Persistence.Abstractions.UnitOfWork` has types of the identical
names, aliased at the `13.ServiceDefaults.Persistence` bridge.

**Why `BeginTransactionAsync`+handle, not `06`'s retry-safe `ExecuteInTransactionAsync`:**
`ExecuteInTransactionAsync`'s operation delegate triggers an unconditional `SaveChangesAsync`+commit
whenever it does NOT throw — but a `Result.Failure` is a normal return value, not a thrown exception, so
wrapping `next()` in it directly would have committed on business failures. Avoiding that would need a
sentinel-exception throw/catch purely to force rollback — much uglier than the handle-based
begin-then-inspect-then-commit-or-rollback shape, which maps directly onto what `TransactionBehavior`
already did for the plain path. Accepted trade-off, documented in the `13.ServiceDefaults.Persistence`
adapter's XML remarks, NOT fixed: a service combining Npgsql retry-on-failure
(`UsePostgreSQL(..., maxRetryCount)`) with `.WithTransactionalUnitOfWork()` will hit `06`'s own
pre-existing "`BeginTransactionAsync` throws when a retrying execution strategy is configured, use
`ExecuteInTransactionAsync` instead" rule for every transactional command. Revisit only if a future work
order specifically asks for retry+transactional-audit compatibility.

**Bridge pattern worth reusing — lazy-factory capability detection instead of eager service-collection
inspection:** `13.ServiceDefaults.Persistence`'s `PersistenceSecurityExtensions.WithApplicationTransactionBehavior<TContext>()`
used to do a plain `AddScoped<AppIUnitOfWork, PersistenceUnitOfWorkAdapter>()`. It now registers a
FACTORY that calls `sp.GetService<06's ITransactionalUnitOfWork>()` at first resolution (inside a real DI
scope) and picks between a new `TransactionalPersistenceUnitOfWorkAdapter` (when non-null) or the
original plain `PersistenceUnitOfWorkAdapter` (when null). This is deliberately NOT an eager
`builder.Services.Any(sd => sd.ServiceType == typeof(...))` check at the extension-method call site —
`EfCorePersistenceBuilder<TContext>.WithTransactionalUnitOfWork()` only flips a private bool; the actual
`06` `ITransactionalUnitOfWork` registration isn't added to the `IServiceCollection` until `.Build()`
runs. An eager check would make `WithApplicationTransactionBehavior()`'s position in the fluent chain
relative to `.WithTransactionalUnitOfWork()`/`.Build()` silently load-bearing. The lazy-factory pattern
makes call order irrelevant. **Apply this same pattern any time a `13.ServiceDefaults.*` bridge needs to
conditionally pick an adapter based on whether a sibling `EfCorePersistenceBuilder` opt-in was
called** — never inspect `builder.Services` synchronously for a flag that a DIFFERENT builder method only
materializes inside `.Build()`.

**16.Testing already has 06-shaped transactional fakes** — `SharedKernel.Testing.Persistence.FakeUnitOfWork`
implements `06.Persistence.Abstractions.UnitOfWork.ITransactionalUnitOfWork` (and transitively
`IUnitOfWork`), and `FakePersistenceTransaction` implements `06`'s `IPersistenceTransaction` — both
predate this session (P-335/WO-053 per their own doc comments). Reused directly in
`13.ServiceDefaults.Persistence.Tests` for a fast, DB-free adapter-delegation test instead of hand-rolling
fakes. Note there is a DELIBERATE naming collision, disambiguated only by namespace, between this and
`SharedKernel.Testing.Application.FakeUnitOfWork` (fakes `05`'s narrower local seam) — both already
documented this collision in their own XML remarks before this session touched anything.

**Real Postgres proof pattern for "does X actually enlist in the ambient transaction the pipeline
opened":** `06.Persistence.EfCore.Auditing.Tests`' own `AuditTransactionSemanticsPostgresTests`
hand-resolves `ITransactionalUnitOfWork` and calls `BeginTransactionAsync` directly — proving
`EfAuditTrailWriter`'s OWN transaction-semantics rule, but never proving any production caller actually
opens that transaction. The genuinely end-to-end proof has to dispatch through `ISender.Send` against a
REAL composed pipeline (`AddSharedKernelApplicationBehaviors().AddAuditingBehavior().AddTransactionBehavior().Build()`
+ the `13` bridges + real EF Core + real Postgres via Testcontainers) — added as
`13.ServiceDefaults.Persistence.Tests/Integration/AuditTransactionWiringPostgresTests.cs`. To force a
genuine "business write fails AFTER something else already staged inside the same transaction" scenario
deterministically (no PK-collision pre-seeding dance needed), mapped a test entity's string column to
`HasMaxLength(10)` (→ Postgres `varchar(10)`) and inserted a 50-char value — `SaveChangesAsync` throws a
real `DbUpdateException` (Postgres `22001`) precisely at the point `TransactionBehavior` calls it, after
`AuditingBehavior`'s audit INSERT has already been staged (not committed) in the same transaction.

See also [[seven_step_pipeline_implementation]] for the original `TransactionBehavior`/pipeline scaffold
this session extended, and [[generic_result_failure_construction]] for the unrelated
`FailureResponseFactory` reflection pattern this session did NOT need to touch.
