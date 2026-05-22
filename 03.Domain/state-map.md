# 03.Domain — State Map

> **What this file is:** Phase and task tracker for all work within `03.Domain`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.03.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
|--------|---------|
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition |
|-----------|-------------------|-------------------|
| `SK.03.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.03.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.03.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.03.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.03.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.03.Published` | Published | All tasks in Phase: Published are `●` |

---

## Active Work

| Task                                | Phase Key  | Package              | State |
|-------------------------------------|------------|----------------------|:-----:|
| Implement core DDD building blocks  | SK.03.Core | SharedKernel.Domain  | `○`   |

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement Entity<TId> base class | SK.03.Core | SharedKernel.Domain | ◐ |
-->

<!--  Design phase completed 2026-05-22 — all 14 tasks verified against CLAUDE.md  -->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| Example blocked task | SK.03.Core | Waiting on upstream decision |
-->

---

## Package Board

| Package               | Current Phase | State | Notes                      |
|-----------------------|---------------|:-----:|----------------------------|
| `SharedKernel.Domain` | Core          | `◐`   | References Primitives only |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
|---------------|------------------|------|--------|
| `SK.03.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`IClock`, `Error`, `SharedKernelException`) | Available |

---

## Phase: Design

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-01 | Define all core identity interfaces: `IEntity<TId>`, `IAggregateRoot<TId>`, `IValueObject`, `IDomainService`, `IStronglyTypedId<TValue>` — document generic constraints and member signatures | SharedKernel.Domain | `●` |
| D-02 | Define `IDomainEvent` contract: `Guid Id` + `DateTimeOffset OccurredOn`; define `DomainEvent` abstract record shape — `init`-only `OccurredOn` with no default; document hard rule that `DateTimeOffset.UtcNow` must not appear inside this type | SharedKernel.Domain | `●` |
| D-03 | Design `AggregateRoot<TId>` constructor injection of `IClock`; choose `NullClock` internal sentinel (returns `DateTimeOffset.MinValue`) for ORM-materialization path; document the `RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent>)` factory overload pattern | SharedKernel.Domain | `●` |
| D-04 | Design `Entity<TId>` identity equality: `GetType()` + `EqualityComparer<TId>.Default`; transient detection via `IsTransient()` when `Id.Equals(default)`; `RuntimeHelpers.GetHashCode` for transient instances; define `operator ==` and `operator !=` | SharedKernel.Domain | `●` |
| D-05 | Choose `ValueObject` equality strategy: abstract class with `GetEqualityComponents() → IEnumerable<object?>`, explicit `Equals`/`GetHashCode` override — chosen over abstract record positional equality because: (a) value objects often have multi-component equality that does not map cleanly to positional records, (b) the `Validate()` hook requires a constructor, which abstract records handle less clearly; document decision | SharedKernel.Domain | `●` |
| D-06 | Design `ValueObject` validation hook: `protected abstract IEnumerable<Error>? Validate()` called in base constructor; null means valid; non-null throws `ValidationException` from `01.Core` using the returned errors | SharedKernel.Domain | `●` |
| D-07 | Define audit and cross-cutting marker interfaces: `IHasCreatedAudit`, `IHasAudit`, `ISoftDeletable`, `IHasConcurrency`, `IHasTenant` — document member signatures, access modifiers, and ownership (infrastructure populates, domain does not write) | SharedKernel.Domain | `●` |
| D-08 | Design four auditable aggregate bases: `AuditableAggregateRoot<TId>`, `SoftDeletableAggregateRoot<TId>` (with `MarkAsDeleted` helper + `abstract OnDelete()`), `AuditableSoftDeletableAggregateRoot<TId>`, `FullAuditableAggregateRoot<TId>` — document `private set` vs `protected set` decisions per property | SharedKernel.Domain | `●` |
| D-09 | Design two auditable entity bases (non-aggregate child entities): `AuditableEntity<TId>` and `FullAuditableEntity<TId>` — no event machinery | SharedKernel.Domain | `●` |
| D-10 | Design `StronglyTypedId<TValue>` abstract record: required positional `TValue Value`, `ToString()` override, `implicit operator TValue`; XML doc note on STJ converter requirement (domain ships no converters) | SharedKernel.Domain | `●` |
| D-11 | Design business rule system: `IBusinessRule` interface; `BusinessRuleViolationException` (extends `SharedKernelException`, carries `IBusinessRule Rule`); `AndBusinessRule`, `OrBusinessRule`, `NotBusinessRule` sealed composites; `BusinessRuleExtensions` with `.And()`, `.Or()`, `.Not()` | SharedKernel.Domain | `●` |
| D-12 | Design policy system: `IPolicy<T>` interface; `AndPolicy<T>`, `OrPolicy<T>`, `NotPolicy<T>` sealed composites; `PolicyExtensions` — document distinction from `IBusinessRule` (policies evaluate domain objects; rules evaluate primitive values/invariants) | SharedKernel.Domain | `●` |
| D-13 | Design specification system: `ISpecification<T>` contract (seven members: `Criteria`, `Includes`, `OrderBy`, `OrderByDescending`, `ThenBys`, `Skip`, `Take`, `IsDistinct`); `Specification<T>` abstract base with protected builder methods (constructor-only build pattern); `AndSpecification<T>`, `OrSpecification<T>`, `NotSpecification<T>` via `ExpressionVisitor` `ParameterReplacer`; `SpecificationExtensions` | SharedKernel.Domain | `●` |
| D-14 | Confirm `IDomainEventHandler<TEvent>` is explicitly excluded from this package — belongs in `05.Application`; document the boundary decision in CLAUDE.md | SharedKernel.Domain | `●` |

---

## Phase: Scaffold

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| S-01 | Create `03.Domain/SharedKernel.Domain/SharedKernel.Domain.csproj` targeting `net10.0`; add `<ProjectReference>` to `SharedKernel.Primitives`; set `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, NuGet metadata (`PackageId`, `Version`, `Description`, `Authors`, `PackageTags`) | SharedKernel.Domain | `●` |
| S-02 | Create directory structure inside `SharedKernel.Domain/`: `Abstractions/`, `Aggregates/`, `Entities/`, `Events/`, `ValueObjects/`, `BusinessRules/`, `Policies/`, `Specifications/`, `Exceptions/`, `StronglyTypedIds/` | SharedKernel.Domain | `●` |
| S-03 | Register `SharedKernel.Domain.csproj` in `Platform.SharedKernel.slnx` under solution folder `03.Domain` | SharedKernel.Domain | `●` |
| S-04 | Create `03.Domain/SharedKernel.Domain/SharedKernel.Domain.Tests/SharedKernel.Domain.Tests.csproj` (`classlib`, `net10.0`); add xUnit, FluentAssertions, and `ProjectReference` to `SharedKernel.Domain`; register in `.slnx` | SharedKernel.Domain | `●` |
| S-05 | Create empty stub test files (one per test group): `EntityEqualityTests.cs`, `AggregateRootEventTests.cs`, `ValueObjectEqualityTests.cs`, `BusinessRuleCompositeTests.cs`, `SpecificationCompositionTests.cs`, `StronglyTypedIdTests.cs` | SharedKernel.Domain | `●` |

---

## Phase: Core

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| C-01 | Implement `IEntity<TId>`, `IAggregateRoot<TId>`, `IValueObject`, `IDomainService`, `IStronglyTypedId<TValue>` in `Abstractions/` with XML doc comments | SharedKernel.Domain | `○` |
| C-02 | Implement `IDomainEvent` interface and `DomainEvent` abstract record in `Events/`: `Guid Id = Guid.NewGuid()`, `DateTimeOffset OccurredOn` as `init`-only, no internal `DateTimeOffset.UtcNow` usage | SharedKernel.Domain | `○` |
| C-03 | Implement `NullClock` internal sealed class in `Aggregates/` implementing `IClock`, returning `DateTimeOffset.MinValue` — used exclusively by the ORM-path protected parameterless constructor | SharedKernel.Domain | `○` |
| C-04 | Implement `Entity<TId>` abstract class in `Entities/`: protected `Entity(TId id)` constructor + protected parameterless constructor; `TId Id { get; private init; }`; identity-based `Equals`/`GetHashCode`; `IsTransient()`; `operator ==` / `operator !=` | SharedKernel.Domain | `○` |
| C-05 | Implement `AggregateRoot<TId>` abstract class in `Aggregates/`: extends `Entity<TId>`, implements `IAggregateRoot<TId>`; `IClock` constructor injection; private `List<IDomainEvent>` field; `IReadOnlyCollection<IDomainEvent> DomainEvents`; `ClearDomainEvents()`; `protected RaiseDomainEvent(IDomainEvent)`; `protected RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent>)`; `protected static void CheckRule(IBusinessRule)` | SharedKernel.Domain | `○` |
| C-06 | Implement `IHasCreatedAudit`, `IHasAudit`, `ISoftDeletable`, `IHasConcurrency`, `IHasTenant` marker interfaces in `Abstractions/` | SharedKernel.Domain | `○` |
| C-07 | Implement four auditable aggregate bases in `Aggregates/`: `AuditableAggregateRoot<TId>`, `SoftDeletableAggregateRoot<TId>` (with `protected abstract void OnDelete()` and `protected void MarkAsDeleted(string deletedBy)`), `AuditableSoftDeletableAggregateRoot<TId>`, `FullAuditableAggregateRoot<TId>` — all audit fields use `private set`; `RowVersion` on full auditable uses `protected set` | SharedKernel.Domain | `○` |
| C-08 | Implement `AuditableEntity<TId>` and `FullAuditableEntity<TId>` in `Entities/` — no event machinery, `private set` on all audit fields | SharedKernel.Domain | `○` |
| C-09 | Implement `StronglyTypedId<TValue>` abstract record in `StronglyTypedIds/`: required `TValue Value { get; init; }`, `ToString()` returns `Value.ToString()!`, `implicit operator TValue`, XML doc note on STJ converter requirement | SharedKernel.Domain | `○` |
| C-10 | Implement `ValueObject` abstract class in `ValueObjects/`: `protected abstract IEnumerable<object?> GetEqualityComponents()`; `Equals`/`GetHashCode` using component iteration; implements `IValueObject`; constructor invokes `protected abstract IEnumerable<Error>? Validate()` — null means valid; non-null throws `ValidationException` | SharedKernel.Domain | `○` |
| C-11 | Implement `IBusinessRule` interface and `BusinessRuleViolationException` in `BusinessRules/` and `Exceptions/` respectively: exception extends `SharedKernelException`, carries `IBusinessRule Rule`, passes `new Error(rule.Message)` to base | SharedKernel.Domain | `○` |
| C-12 | Implement `AndBusinessRule`, `OrBusinessRule`, `NotBusinessRule` sealed composites in `BusinessRules/`; `AndBusinessRule.Message` aggregates broken sub-rule messages with "; " delimiter; implement `BusinessRuleExtensions` static class with `.And()`, `.Or()`, `.Not()` | SharedKernel.Domain | `○` |
| C-13 | Implement `IPolicy<T>` interface; `AndPolicy<T>`, `OrPolicy<T>`, `NotPolicy<T>` sealed composites in `Policies/`; `PolicyExtensions` static class with `.And<T>()`, `.Or<T>()`, `.Not<T>()` | SharedKernel.Domain | `○` |
| C-14 | Implement `ISpecification<T>` interface in `Specifications/` with all eight members: `Criteria`, `Includes`, `OrderBy`, `OrderByDescending`, `ThenBys`, `Skip`, `Take`, `IsDistinct` | SharedKernel.Domain | `○` |
| C-15 | Implement `Specification<T>` abstract base class in `Specifications/` with all protected builder methods (`AddCriteria`, `AddInclude`, `ApplyOrderBy`, `ApplyOrderByDescending`, `ApplyThenBy`, `ApplyPaging`, `ApplyDistinct`) — all return `void`; specifications built in concrete subclass constructors | SharedKernel.Domain | `○` |
| C-16 | Implement `AndSpecification<T>`, `OrSpecification<T>`, `NotSpecification<T>` in `Specifications/` using `ExpressionVisitor` with `ParameterReplacer` for expression tree composition; implement `SpecificationExtensions` with `.And<T>()`, `.Or<T>()`, `.Not<T>()` | SharedKernel.Domain | `○` |

---

## Phase: Tests

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| T-01 | Entity equality tests: same `Id` → equal; different `Id` → not equal; different concrete type same `Id` → not equal; transient entity (`Id == default`) never equals any other instance including itself | SharedKernel.Domain | `○` |
| T-02 | Aggregate event accumulation tests: event raised appears in `DomainEvents`; `ClearDomainEvents()` empties the list; events do not leak across aggregate instances; `RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent>)` passes clock time from injected `IClock` | SharedKernel.Domain | `○` |
| T-03 | `NullClock` sentinel tests: ORM-path constructor sets `_clock` to `NullClock`; `NullClock.UtcNow` returns `DateTimeOffset.MinValue` | SharedKernel.Domain | `○` |
| T-04 | ValueObject structural equality tests: identical components → equal; changed single component → not equal; null component handled; `GetHashCode` consistent with equality | SharedKernel.Domain | `○` |
| T-05 | ValueObject validation tests: `Validate()` returning null → construction succeeds; returning one or more `Error` → `ValidationException` thrown at construction | SharedKernel.Domain | `○` |
| T-06 | Business rule composite tests: `AndBusinessRule` broken when either sub-rule is broken; `OrBusinessRule` broken only when both sub-rules are broken; `NotBusinessRule` inverts broken state; `And`/`Or`/`Not` extension methods produce correct wrappers | SharedKernel.Domain | `○` |
| T-07 | `CheckRule` on `AggregateRoot<TId>`: broken rule → `BusinessRuleViolationException` thrown; non-broken rule → no exception; exception carries the `IBusinessRule` instance | SharedKernel.Domain | `○` |
| T-08 | Policy composite tests: `AndPolicy<T>` compliant only when both sub-policies are compliant; `OrPolicy<T>` compliant when at least one is; `NotPolicy<T>` inverts compliance | SharedKernel.Domain | `○` |
| T-09 | Specification composition tests: `AndSpecification<T>` expression evaluates `left AND right`; `OrSpecification<T>` evaluates `left OR right`; `NotSpecification<T>` negates; composed expressions can be compiled and evaluated against in-memory collections | SharedKernel.Domain | `○` |
| T-10 | `StronglyTypedId<TValue>` tests: implicit `operator TValue` unwraps correctly; `ToString()` returns `Value.ToString()`; value-equality between two instances with same inner value | SharedKernel.Domain | `○` |
| T-11 | Audit interface coverage tests: `AuditableAggregateRoot<TId>` properties are settable only via ORM reflection (verify `private set`); `FullAuditableAggregateRoot<TId>` `RowVersion` has `protected set`; `SoftDeletableAggregateRoot<TId>.MarkAsDeleted` sets all three soft-delete fields | SharedKernel.Domain | `○` |

---

## Phase: Docs

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| DO-01 | Ensure all public interfaces carry triple-slash XML doc comments: `IEntity<TId>`, `IAggregateRoot<TId>`, `IValueObject`, `IDomainService`, `IStronglyTypedId<TValue>`, `IDomainEvent`, `IHasCreatedAudit`, `IHasAudit`, `ISoftDeletable`, `IHasConcurrency`, `IHasTenant`, `IBusinessRule`, `IPolicy<T>`, `ISpecification<T>` | SharedKernel.Domain | `○` |
| DO-02 | Ensure all public abstract base types carry XML doc comments: `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, `DomainEvent`, `StronglyTypedId<TValue>`, `Specification<T>`, all auditable bases — include usage example in `<example>` blocks | SharedKernel.Domain | `○` |
| DO-03 | Add `<remarks>` block on `StronglyTypedId<TValue>` noting that STJ serialization requires a custom `JsonConverter` in the consuming service — this package ships no converters | SharedKernel.Domain | `○` |
| DO-04 | Add `<remarks>` block on `DomainEvent` documenting the `OccurredOn` sourcing rule: must be supplied via `IClock.UtcNow` from the aggregate's `RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent>)` overload; direct `DateTimeOffset.UtcNow` usage is a hard violation | SharedKernel.Domain | `○` |
| DO-05 | Add `<remarks>` block on `AggregateRoot<TId>.ClearDomainEvents()` documenting that only infrastructure dispatch code should call this; aggregates must never self-clear | SharedKernel.Domain | `○` |
| DO-06 | Add `<remarks>` block on `IPolicy<T>` vs `IBusinessRule` documenting the distinction: policies evaluate domain object compliance; business rules validate raw primitive invariants | SharedKernel.Domain | `○` |
| DO-07 | Write `README.md` for `SharedKernel.Domain/` covering: quick-start aggregate example, value object example with `Validate()` hook, specification usage pattern, business rule composition example, note on `IDomainEventHandler<TEvent>` living in `05.Application` | SharedKernel.Domain | `○` |

---

## Phase: Published

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| P-01 | Set final NuGet package metadata in `SharedKernel.Domain.csproj`: `PackageVersion`, `PackageReleaseNotes`, `RepositoryUrl`, `PackageLicenseExpression`, `PackageReadmeFile` | SharedKernel.Domain | `○` |
| P-02 | Run `dotnet pack SharedKernel.Domain.csproj -c Release` and verify zero warnings, zero NuGet dependency entries in generated `.nupkg` manifest — only `SharedKernel.Primitives` dependency present | SharedKernel.Domain | `○` |
| P-03 | Reference `SharedKernel.Domain` from a consumer stub project; verify `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, `ISpecification<T>`, and `StronglyTypedId<TValue>` are all usable without additional package references | SharedKernel.Domain | `○` |
| P-04 | Publish `SharedKernel.Domain` to the internal NuGet feed; confirm package appears in feed and is resolvable by version | SharedKernel.Domain | `○` |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
|-----------|-------|:-----:|:------:|:---------:|:-----:|
| `SK.03.Design` | Design | 14 | 14 | 0 | `●` |
| `SK.03.Scaffold` | Scaffold | 5 | 5 | 0 | `●` |
| `SK.03.Core` | Core | 16 | 0 | 16 | `○` |
| `SK.03.Tests` | Tests | 11 | 0 | 11 | `○` |
| `SK.03.Docs` | Docs | 7 | 0 | 7 | `○` |
| `SK.03.Published` | Published | 4 | 0 | 4 | `○` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-05-22] Sub state-map initialized — phase key registry, 6 phases scaffolded at ○, no tasks yet
- [2026-05-22] P-032 (WO-008) — 57 tasks added across all 6 phases for SharedKernel.Domain DDD foundation (domain-arch-planner)
- [2026-05-22] SK.03.Design complete — all 14 design tasks verified against CLAUDE.md; phase promoted to ● (domain-phase-implementer)
- [2026-05-22] SK.03.Scaffold complete — csproj with Primitives ref, 10 subdirs with .gitkeep, test csproj with xUnit+FluentAssertions, 6 stub test files; both projects build 0 errors (domain-phase-implementer)
