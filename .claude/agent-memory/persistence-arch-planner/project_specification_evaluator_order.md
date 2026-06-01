---
name: project_specification_evaluator_order
description: SpecificationEvaluator<T> canonical operation order — paging always last, ThenBys only with primary sort
metadata:
  type: project
---

`SpecificationEvaluator<T>` in `SharedKernel.Persistence.EfCore` applies operations in this strict order:

1. Criteria (Where clause — null = no filter = all entities)
2. Includes (Include / ThenInclude)
3. OrderBy / OrderByDescending (primary sort)
4. ThenBys (secondary sorts — only applied when a primary sort is already set)
5. Distinct
6. AsNoTracking
7. Skip / Take — ALWAYS LAST

**Why paging is last:** Skip/Take must follow ordering to produce stable, deterministic pages. Applying paging before ordering produces unpredictable results across pages.

**Why ThenBys require primary sort:** Applying ThenBy without an initial OrderBy on IQueryable produces an invalid/undefined query in EF Core.

**How to apply:** Any phase task that touches `SpecificationEvaluator<T>` must preserve this order. The test suite for P-074 includes a specific test verifying Skip/Take appear after OrderBy in the generated query expression tree — this is a canary for order violations.
