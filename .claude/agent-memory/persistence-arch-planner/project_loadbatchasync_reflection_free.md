---
name: project_loadbatchasync_reflection_free
description: EncryptionRotationService.LoadBatchAsync reflection elimination via non-generic DbContext.Set/Skip/Take/ToListAsync
metadata:
  type: project
---

P-147 eliminated reflection (`GetMethod`/`MakeGenericMethod`/`Invoke`) from
`EncryptionRotationService.LoadBatchAsync`, which previously used
`typeof(DbContext).GetMethods().First(...).MakeGenericMethod(clrType).Invoke(...)`
to call the generic `DbContext.Set<T>()`.

Replacement uses the **non-generic** EF Core surface:
- `DbContext.Set(Type)` — non-generic overload returning a non-generic `IQueryable`.
- `System.Linq.Queryable.Skip(IQueryable, int)` / `.Take(IQueryable, int)` — non-generic
  extension overloads operating on `IQueryable` (not `IQueryable<T>`).
- `Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(IQueryable, CancellationToken)`
  — non-generic overload returning `Task<List<object>>`.

No `GetMethod`/`MakeGenericMethod`/`Invoke` anywhere in the rotation hot path.

**Why:** The reflection path was flagged as an AOT/trim risk and unnecessary —
EF Core ships non-generic overloads of all three operations specifically for
scenarios like this (dynamic entity-type iteration).

**How to apply:** Any future code that needs to operate generically across
multiple `DbContext` entity types at runtime (without knowing `T` at compile time)
should prefer these non-generic LINQ/EF Core overloads over
`MakeGenericMethod`/`Invoke`. This is the canonical pattern for this codebase now.

See also [[project_encryption_version_override]], [[project_encryption_subsystem]].
