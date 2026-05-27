---
name: project-wo010-wo011-phases
description: WO-010 (P-043, P-044) and WO-011 (P-045..P-054) — locked design decisions for SharedKernel.Domain phases
metadata:
  type: project
---

WO-010 and WO-011 added 49 tasks (D-19..D-30, C-22..C-34, T-16..T-27, DO-14..DO-26, P-06..P-07) to `03.Domain/state-map.md`. All design decisions are locked.

**Why:** Extends the DDD foundation with error correctness, multi-tenancy, event dispatch decoupling, single-value objects, domain services, version counters, exception hierarchy, specification sentinels/paging, event schema versioning, and aggregate factory patterns.

**How to apply:** Treat all decisions below as locked. Increment D/C/T/DO/P IDs from D-30/C-34/T-27/DO-26/P-07.

Key locked decisions for WO-010:

- `BusinessRuleViolationException` now uses `Error.BusinessRule(ErrorCodes.Domain.RuleViolated, rule.Message)` — NOT `Error.Unexpected`. This is a correctness fix: `ErrorType.BusinessRule` maps to HTTP 422, not HTTP 500.
- `BusinessRuleViolationException` hierarchy changed: now extends `DomainException` from `SharedKernel.Core.Exceptions` (not `SharedKernelException` directly). Catching `SharedKernelException` still works.
- `TenantedSoftDeletableAggregateRoot<TId>` intentionally absent — hierarchy explosion not justified.
- Tenanted bases: `TenantId { get; private set; }`, `Guid.Empty` in ORM-path constructor, no `ITenantProvider` reference in `03.Domain`.
- SharedKernel.Domain version bumped to 1.2.0 after WO-010, 1.3.0 after WO-011.

Key locked decisions for WO-011:

- `IHasDomainEvents` extracted from `IAggregateRoot<TId>` — pure interface refactor, zero behavioral change. Infrastructure dispatch code must depend on `IHasDomainEvents`, not `IAggregateRoot<TId>`.
- `SingleValueObject<TValue>` construction-order hazard solved by field-initializer assignment (Value assigned before base constructor via primary constructor parameter). NOT by a bypass hook in `ValueObject`.
- `IHasVersion` added **directly to `AggregateRoot<TId>`** (not a new `VersionedAggregateRoot<TId>` base) — keeps hierarchy single and clean. Trade-off: every aggregate root gets a version counter; this is acceptable since it has zero persistence cost until a repository reads it.
- `DomainNotFoundException` uses `Error.NotFound(...)` payload.
- `OrSpecification<T>` null-criteria fix: if either operand has null criteria, the combined Criteria is null (matches everything).
- `DomainEventVersionHelper.GetVersion(Type)` uses reflection (attribute reading) — acceptable at startup/registration time; not in hot paths.
- `TryCreate<T>` catches `ValidationException.Errors.First()` for the failure result — aligns with the railway pattern from `01.Core`.
- `PagedSpecification<T>.MaxPageSize = 1000` — subclasses may shadow with `new const int MaxPageSize = N`.

Related: [[project-domain-foundation]]
