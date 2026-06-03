---
name: phase-numbering-state
description: Last known phase and work order numbers in the root state-map Phase Backlog
metadata:
  type: project
---

As of 2026-06-03, the last phase written to `state-map.md` Phase Backlog is **P-104** under **WO-017**.

Next new phase must be **P-105**. Next new Work Order must be **WO-018**.

**How to apply:** Always read the current Phase Backlog before assigning new IDs — this memory is a starting point, not a substitute for reading the file.

**WO-017 context:** EfCore package audit — 8 phases across 3 domains:
- P-097 06.Persistence: Promote `GetProjectedQuery` to `ISpecificationEvaluator<T>` interface in Abstractions; remove concrete downcast in `EfReadRepository`
- P-098 06.Persistence: Fix `EfUnitOfWork` dual-constructor DI ambiguity → single constructor with nullable `IDomainEventDispatcher?`
- P-099 06.Persistence: Fix `ExistsAsync` `EF.Property` shadow accessor; add `ITransactionalUnitOfWork` + `IPersistenceTransaction` to Abstractions + `EfTransactionalUnitOfWork` to EfCore
- P-100 06.Persistence: Fix `TenantedRepository.GetByIdForTenantAsync` unintentional soft-delete bypass; add `GetByIdForTenantIncludingDeletedAsync` variant
- P-101 06.Persistence: Add `ListPagedProjectedAsync<TResult>` to `IReadRepository` + `EfReadRepository` (depends on P-097)
- P-102 06.Persistence: Rename `ValueObjectOwnershipConvention` → `ValueObjectOwnershipBuilder` to clarify it is not a real EF Core convention
- P-103 00.Governance: Three architecture rules — no ISpecificationEvaluator downcast, single-constructor UoW, no IDbContextTransaction in application layer (depends on P-097, P-099)
- P-104 16.Testing: Test coverage gaps — AsNoTracking behavior, assembly scan OnModelCreating, transaction scope, ListPagedProjectedAsync (depends on P-097, P-099, P-101)

**Domains touched:** 06.Persistence (already ●), 00.Governance (already ●), 16.Testing (already ◐) — no `state-map-phase` calls needed.

**Key architectural decisions made in WO-017:**
1. `ISpecificationEvaluator<T>` must expose `GetProjectedQuery` — concrete downcast in EfReadRepository is a hard layering violation
2. `EfUnitOfWork` single-constructor pattern with `IDomainEventDispatcher?` nullable is the idiomatic .NET optional-dependency pattern
3. `ITransactionalUnitOfWork` / `IPersistenceTransaction` abstraction in Abstractions keeps EF Core's `IDbContextTransaction` out of application layer
4. `TenantedRepository.GetByIdForTenantAsync` must preserve soft-delete filter; `GetByIdForTenantIncludingDeletedAsync` is the explicit bypass variant
5. `ListPagedProjectedAsync<TResult>` is the canonical "paged DTOs" method; two DB round-trips, count without projection + data with projection
6. `ValueObjectOwnershipConvention` renamed to `ValueObjectOwnershipBuilder` — it does not implement `IModelFinalizingConvention` and the name was misleading
