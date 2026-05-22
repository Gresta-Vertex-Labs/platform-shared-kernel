---
name: project-domain-foundation
description: P-032 WO-008 — SharedKernel.Domain DDD foundation design decisions and public surface locked in
metadata:
  type: project
---

P-032 (WO-008) planned the complete `SharedKernel.Domain` package. 57 tasks across 6 phases added to `03.Domain/state-map.md`. All design decisions are locked.

**Why:** `03.Domain` is the most foundational mutable layer — changing entity equality semantics or the ISpecification contract after downstream services onboard causes platform-wide breaking changes. Decisions must be correct first time.

**How to apply:** Treat all design decisions below as locked. Do not re-open without a formal WO.

Key locked decisions:

- `ValueObject` uses abstract class + `GetEqualityComponents() → IEnumerable<object?>` — NOT abstract record positional equality. Reason: multi-component equality, `Validate()` constructor hook requires explicit constructor control.
- `DomainEvent.OccurredOn` is `init`-only, no default. Must be supplied via `AggregateRoot.RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent>)` using `_clock.UtcNow`. Direct `DateTimeOffset.UtcNow` anywhere in this package is a hard violation.
- `NullClock` internal sealed sentinel (returns `DateTimeOffset.MinValue`) assigned in ORM-path protected parameterless constructor of `AggregateRoot<TId>`.
- `IDomainEvent` has two members: `Guid Id` and `DateTimeOffset OccurredOn`. No routing, serialization, or dispatch members — those are infrastructure concerns.
- Exactly four auditable aggregate bases: `AuditableAggregateRoot<TId>`, `SoftDeletableAggregateRoot<TId>`, `AuditableSoftDeletableAggregateRoot<TId>`, `FullAuditableAggregateRoot<TId>`.
- `SoftDeletableAggregateRoot<TId>` uses `protected abstract void OnDelete()` + `protected void MarkAsDeleted(string deletedBy)` pattern — concrete aggregate raises its domain event in `OnDelete`.
- `RowVersion` on `FullAuditableAggregateRoot<TId>` and `FullAuditableEntity<TId>` has `protected set` (persistence layer needs post-fetch update); all other audit fields are `private set`.
- `IDomainEventHandler<TEvent>` is explicitly excluded — belongs in `05.Application`.
- `ISpecification<T>` has eight members: `Criteria`, `Includes`, `OrderBy`, `OrderByDescending`, `ThenBys`, `Skip`, `Take`, `IsDistinct`. Specifications built only in concrete subclass constructors.
- `AndSpecification<T>`, `OrSpecification<T>`, `NotSpecification<T>` use `ExpressionVisitor` with `ParameterReplacer` pattern.
- `StronglyTypedId<TValue>` ships no STJ `JsonConverter` — consuming services own converters.
- `IPolicy<T>` evaluates domain object compliance; `IBusinessRule` validates raw primitive invariants — distinct concepts, not interchangeable.

Related: [[project-shared-kernel]]
