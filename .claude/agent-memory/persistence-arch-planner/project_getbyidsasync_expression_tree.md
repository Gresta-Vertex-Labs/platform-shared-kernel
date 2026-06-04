---
name: project_getbyidsasync_expression_tree
description: EfReadRepository.GetByIdsAsync must use expression-tree Contains, not EF.Property, to avoid silent client-side evaluation (P-105)
metadata:
  type: project
---

**P-105 Fix 2 (WO-018, 2026-06-03):** `EfReadRepository.GetByIdsAsync` previously used `EF.Property<TId>(e, "Id")` inside a `Contains` predicate. When `TId` is a `StronglyTypedId<Guid>` with a registered `StronglyTypedIdValueConverter`, EF Core may silently fall back to client-side evaluation — loading all rows and filtering in memory (full table scan).

**The fix:** Build the predicate as an expression tree:

```csharp
var param = Expression.Parameter(typeof(TAggregate), "e");
var idProp = Expression.Property(param, "Id");
var containsCall = Expression.Call(/* Enumerable.Contains<TId> */, Expression.Constant(idList), idProp);
var lambda = Expression.Lambda<Func<TAggregate, bool>>(containsCall, param);
```

EF Core's LINQ provider resolves the value converter at the property level for expression-tree lambdas, generating a server-side `WHERE "Id" IN (...)` with the converter applied to each element.

**Why:** The entire platform uses strongly-typed IDs. A silent `O(n)` full table scan on every `GetByIdsAsync` call is a critical performance defect in disguise — it only becomes visible under load or with profiling.

**How to apply:** `EF.Property<TId>(e, "Id")` inside a `Contains` predicate is a hard violation when a value converter is registered for `TId`. Use the expression-tree pattern above. See also [[project_repository_extensions]] for the behavioral contract (result order not guaranteed, missing IDs produce no entry, warn >1000 IDs).
