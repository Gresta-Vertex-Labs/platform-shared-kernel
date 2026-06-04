---
name: project_specification_evaluator_order
description: SpecificationEvaluator<T> canonical operation order — step 2b StringIncludes, paging always last
metadata:
  type: project
---

`SpecificationEvaluator<T>` in `SharedKernel.Persistence.EfCore` applies operations in this strict order:

```text
0.  IgnoreQueryFilters()   — only when spec.IncludeDeleted == true; before all other steps
1.  Criteria               — Where clause; null = no filter = all entities
2.  Includes               — expression-based Include / ThenInclude
2b. StringIncludes         — string-based Include(string) calls; after expression includes, before ordering (P-107)
3.  OrderBy/Descending     — primary sort
4.  ThenBys                — secondary sorts; only when primary sort is set
5.  Distinct
6.  AsNoTracking
7.  Skip / Take            — ALWAYS LAST for the aggregate pipeline
8.  Select(spec.Selector)  — projection overload only; applied after Skip/Take
```

**Why step 2b is between includes and ordering:** String includes are eager-loading, same nature as expression includes. Placing them before ordering ensures the full include graph is assembled before sorting or paging restricts the result set.

**Why paging is last:** Skip/Take must follow ordering to produce stable, deterministic pages.

**Why ThenBys require primary sort:** Applying ThenBy without an initial OrderBy on IQueryable produces an undefined query in EF Core.

**StringIncludes source:** `ISpecification<T>.StringIncludes` property (`IReadOnlyList<string>`) added to `SharedKernel.Domain` by the Domain agent. `AddStringInclude(null/whitespace)` throws `ArgumentException`. See [[project_projection_specification]].

**How to apply:** Any phase task touching `SpecificationEvaluator<T>` must preserve this order. All `ISpecificationEvaluator<T>` implementations must read both `spec.Includes` and `spec.StringIncludes` — omitting StringIncludes is a silent bug.
