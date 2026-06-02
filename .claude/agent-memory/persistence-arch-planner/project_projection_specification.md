---
name: project_projection_specification
description: IProjectionSpecification<TAggregate,TResult> in Abstractions — prerequisite for projection reads (P-079, WO-014)
metadata:
  type: project
---

**P-079 decision (WO-014, 2026-06-02):** `IProjectionSpecification<TAggregate, TResult>` added to `SharedKernel.Persistence.Abstractions/Specifications/`.

Shape: extends `ISpecification<TAggregate>`; adds `Expression<Func<TAggregate, TResult>> Selector { get; }`.

Zero ORM dependencies — pure interface in Abstractions. The `Selector` is an expression tree, AOT-safe on `IQueryable`.

**Why:** Prerequisite for future `ListProjectedAsync<TResult>` and `GetBySpecProjectedAsync<TResult>` methods on `IReadRepository`. Without it, the abstraction layer cannot declare projection methods without coupling to EF Core.

**IDbConnectionFactory doc fix (same phase):** The "exclusively by Dapper" restriction was removed from `IDbConnectionFactory` XML doc. The factory is now documented as a neutral provider-agnostic connection factory. Any component needing a raw `IDbConnection` may inject it.

**How to apply:** When planning read projection tasks, always check whether `IProjectionSpecification` is in scope first. It is the required contract — do not plan `Select(...)` calls directly on repositories.
