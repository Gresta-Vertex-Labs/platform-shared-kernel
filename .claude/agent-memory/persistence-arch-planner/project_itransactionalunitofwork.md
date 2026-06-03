---
name: itransactionalunitofwork
description: ITransactionalUnitOfWork + IPersistenceTransaction live in Abstractions (BCL types only); EfTransactionalUnitOfWork + EfPersistenceTransaction live in EfCore; registered via WithTransactionalUnitOfWork() (P-099)
metadata:
  type: project
---

`ITransactionalUnitOfWork` and `IPersistenceTransaction` added to `SharedKernel.Persistence.Abstractions` (P-099, WO-017).

**Interface shape:**
- `IPersistenceTransaction : IAsyncDisposable` — `CommitAsync(CancellationToken) → Task`, `RollbackAsync(CancellationToken) → Task`
- `ITransactionalUnitOfWork : IUnitOfWork` — `BeginTransactionAsync(CancellationToken) → Task<IPersistenceTransaction>`

**Why:** Without this abstraction, application layer code needing explicit transactions injected `SharedKernelDbContext` or `IDbContextTransaction` directly — both bypass the repository pattern and couple application logic to EF Core. This is the most common forcing function for DbContext injection in microservices.

**How to apply:** Application layer injects `ITransactionalUnitOfWork` — never `IDbContextTransaction` directly (hard violation in CLAUDE.md). EfCore package provides `EfPersistenceTransaction` (wraps `IDbContextTransaction`) and `EfTransactionalUnitOfWork`. Registration is optional: only call `.WithTransactionalUnitOfWork()` on `EfCorePersistenceBuilder` for services that need explicit transactions. `IPersistenceTransaction` uses only BCL types — Abstractions package must NOT gain any EF Core NuGet reference to accommodate it.

**ExistsAsync fix:** Also in P-099 — `EfRepository.ExistsAsync` changed from `EF.Property<TId>(e, "Id")` (shadow property accessor, fragile on concrete CLR properties) to expression-tree approach: `Expression.Property(param, "Id")` + `Expression.Equal` + compiled lambda. Same pattern as `ByIdSpecification<TAggregate,TId>`.
