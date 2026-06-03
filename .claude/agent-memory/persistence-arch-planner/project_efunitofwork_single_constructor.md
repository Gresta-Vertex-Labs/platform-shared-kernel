---
name: efunitofwork-single-constructor
description: EfUnitOfWork must have exactly one constructor with IDomainEventDispatcher? as nullable optional; second constructor creates DI ambiguity (P-098)
metadata:
  type: project
---

`EfUnitOfWork` has exactly one public constructor: `EfUnitOfWork(SharedKernelDbContext dbContext, IDomainEventDispatcher? dispatcher = null)` (P-098, WO-017).

**Why:** Two constructors — one with `(SharedKernelDbContext)` and one with `(SharedKernelDbContext, IDomainEventDispatcher?)` — create DI resolution ambiguity. .NET DI uses the constructor with the most resolvable parameters, which is non-deterministic when two constructors overlap. The container may silently select the shorter one and skip the dispatcher even when `IDomainEventDispatcher` IS registered. Domain events are then never dispatched — a silent behavioral failure in production that passes in direct-construction tests.

**How to apply:** The nullable optional `IDomainEventDispatcher?` parameter is the idiomatic .NET DI optional-dependency pattern. DI resolves `null` when not registered. Adding any second public constructor to `EfUnitOfWork` is a hard violation listed in CLAUDE.md.
