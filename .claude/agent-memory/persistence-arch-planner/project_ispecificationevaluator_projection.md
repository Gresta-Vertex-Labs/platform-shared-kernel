---
name: ispecificationevaluator-projection
description: GetProjectedQuery is now on ISpecificationEvaluator<T> interface (P-097); EfReadRepository must not downcast to concrete SpecificationEvaluator<T>
metadata:
  type: project
---

GetProjectedQuery promoted to `ISpecificationEvaluator<T>` interface in Abstractions (P-097, WO-017).

**Why:** `EfReadRepository` previously downcasted its injected `ISpecificationEvaluator<TAggregate>` to the concrete `SpecificationEvaluator<TAggregate>` to call `GetProjectedQuery`. Any alternative evaluator (Cosmos, in-memory, Marten) would throw `InvalidCastException` at runtime on `ListProjectedAsync` or `GetBySpecProjectedAsync`. This silently breaks the abstraction boundary.

**How to apply:** The `_evaluator` field in `EfReadRepository` is typed `ISpecificationEvaluator<TAggregate>` — never the concrete type. All projection method calls go through the interface. Any new `ISpecificationEvaluator<T>` implementation must implement both `GetQuery` and `GetProjectedQuery`. Hard violation: downcasting `ISpecificationEvaluator<T>` to any concrete type is listed in CLAUDE.md implementation rules.

Related: [[project_specification_evaluator_order]]
