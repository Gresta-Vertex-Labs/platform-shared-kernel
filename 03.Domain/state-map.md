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

_Nothing in progress._

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

| Package               | Current Phase | State | Notes                         |
|-----------------------|---------------|:-----:|-------------------------------|
| `SharedKernel.Domain` | Published     | `●`   | References Primitives + Core  |

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
| D-15 | Design corrected auditable aggregate hierarchy (P-036/WO-009): `FullAuditableAggregateRoot<TId>` must extend `AuditableSoftDeletableAggregateRoot<TId>` (not `AggregateRoot<TId>` directly); document that `IsDeleted`, `DeletedOn`, `DeletedBy`, `MarkAsDeleted`, and `OnDelete` are all inherited — `FullAuditableAggregateRoot<TId>` retains only `IHasConcurrency` membership and `RowVersion` with `protected set`; update hierarchy diagram | SharedKernel.Domain | `○` |
| D-16 | Design `Specification<T>.IsSatisfiedBy(T entity)` (P-038/WO-009): non-virtual concrete method; compiles `Criteria` once to `Func<T, bool>` and caches in private nullable field; returns `true` when `Criteria` is null; `ISpecification<T>` interface must NOT gain this method — in-domain/in-test use only | SharedKernel.Domain | `○` |
| D-17 | Design `DomainEvent<TPayload>` abstract record (P-039/WO-009): extends `DomainEvent`; adds `TPayload Payload { get; init; }` as `required`; constrained to `TPayload : notnull`; remains abstract; non-generic `DomainEvent` is unchanged and not deprecated | SharedKernel.Domain | `○` |
| D-18 | Design `ISpecification<T>.AsNoTracking` flag (P-040/WO-009): `bool AsNoTracking { get; }` on interface; `Specification<T>` defaults to `false`; `protected void ApplyNoTracking()` builder method; `ReadOnlySpecification<T>` sealed abstract convenience base calls `ApplyNoTracking()` in constructor; composite specs propagate `true` if either operand is `true` (more restrictive wins) | SharedKernel.Domain | `○` |

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
| C-01 | Implement `IEntity<TId>`, `IAggregateRoot<TId>`, `IValueObject`, `IDomainService`, `IStronglyTypedId<TValue>` in `Abstractions/` with XML doc comments | SharedKernel.Domain | `●` |
| C-02 | Implement `IDomainEvent` interface and `DomainEvent` abstract record in `Events/`: `Guid Id = Guid.NewGuid()`, `DateTimeOffset OccurredOn` as `init`-only, no internal `DateTimeOffset.UtcNow` usage | SharedKernel.Domain | `●` |
| C-03 | Implement `NullClock` internal sealed class in `Aggregates/` implementing `IClock`, returning `DateTimeOffset.MinValue` — used exclusively by the ORM-path protected parameterless constructor | SharedKernel.Domain | `●` |
| C-04 | Implement `Entity<TId>` abstract class in `Entities/`: protected `Entity(TId id)` constructor + protected parameterless constructor; `TId Id { get; private init; }`; identity-based `Equals`/`GetHashCode`; `IsTransient()`; `operator ==` / `operator !=` | SharedKernel.Domain | `●` |
| C-05 | Implement `AggregateRoot<TId>` abstract class in `Aggregates/`: extends `Entity<TId>`, implements `IAggregateRoot<TId>`; `IClock` constructor injection; private `List<IDomainEvent>` field; `IReadOnlyCollection<IDomainEvent> DomainEvents`; `ClearDomainEvents()`; `protected RaiseDomainEvent(IDomainEvent)`; `protected RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent>)`; `protected static void CheckRule(IBusinessRule)` | SharedKernel.Domain | `●` |
| C-06 | Implement `IHasCreatedAudit`, `IHasAudit`, `ISoftDeletable`, `IHasConcurrency`, `IHasTenant` marker interfaces in `Abstractions/` | SharedKernel.Domain | `●` |
| C-07 | Implement four auditable aggregate bases in `Aggregates/`: `AuditableAggregateRoot<TId>`, `SoftDeletableAggregateRoot<TId>` (with `protected abstract void OnDelete()` and `protected void MarkAsDeleted(string deletedBy)`), `AuditableSoftDeletableAggregateRoot<TId>`, `FullAuditableAggregateRoot<TId>` — all audit fields use `private set`; `RowVersion` on full auditable uses `protected set` | SharedKernel.Domain | `●` |
| C-08 | Implement `AuditableEntity<TId>` and `FullAuditableEntity<TId>` in `Entities/` — no event machinery, `private set` on all audit fields | SharedKernel.Domain | `●` |
| C-09 | Implement `StronglyTypedId<TValue>` abstract record in `StronglyTypedIds/`: required `TValue Value { get; init; }`, `ToString()` returns `Value.ToString()!`, `implicit operator TValue`, XML doc note on STJ converter requirement | SharedKernel.Domain | `●` |
| C-10 | Implement `ValueObject` abstract class in `ValueObjects/`: `protected abstract IEnumerable<object?> GetEqualityComponents()`; `Equals`/`GetHashCode` using component iteration; implements `IValueObject`; constructor invokes `protected abstract IEnumerable<Error>? Validate()` — null means valid; non-null throws `ValidationException` | SharedKernel.Domain | `●` |
| C-11 | Implement `IBusinessRule` interface and `BusinessRuleViolationException` in `BusinessRules/` and `Exceptions/` respectively: exception extends `SharedKernelException`, carries `IBusinessRule Rule`, passes `new Error(rule.Message)` to base | SharedKernel.Domain | `●` |
| C-12 | Implement `AndBusinessRule`, `OrBusinessRule`, `NotBusinessRule` sealed composites in `BusinessRules/`; `AndBusinessRule.Message` aggregates broken sub-rule messages with "; " delimiter; implement `BusinessRuleExtensions` static class with `.And()`, `.Or()`, `.Not()` | SharedKernel.Domain | `●` |
| C-13 | Implement `IPolicy<T>` interface; `AndPolicy<T>`, `OrPolicy<T>`, `NotPolicy<T>` sealed composites in `Policies/`; `PolicyExtensions` static class with `.And<T>()`, `.Or<T>()`, `.Not<T>()` | SharedKernel.Domain | `●` |
| C-14 | Implement `ISpecification<T>` interface in `Specifications/` with all eight members: `Criteria`, `Includes`, `OrderBy`, `OrderByDescending`, `ThenBys`, `Skip`, `Take`, `IsDistinct` | SharedKernel.Domain | `●` |
| C-15 | Implement `Specification<T>` abstract base class in `Specifications/` with all protected builder methods (`AddCriteria`, `AddInclude`, `ApplyOrderBy`, `ApplyOrderByDescending`, `ApplyThenBy`, `ApplyPaging`, `ApplyDistinct`) — all return `void`; specifications built in concrete subclass constructors | SharedKernel.Domain | `●` |
| C-16 | Implement `AndSpecification<T>`, `OrSpecification<T>`, `NotSpecification<T>` in `Specifications/` using `ExpressionVisitor` with `ParameterReplacer` for expression tree composition; implement `SpecificationExtensions` with `.And<T>()`, `.Or<T>()`, `.Not<T>()` | SharedKernel.Domain | `●` |
| C-17 | Refactor `FullAuditableAggregateRoot<TId>` (P-036/WO-009): change base class from `AggregateRoot<TId>` to `AuditableSoftDeletableAggregateRoot<TId>`; remove all duplicated soft-delete fields (`IsDeleted`, `DeletedOn`, `DeletedBy`), `MarkAsDeleted`, and `OnDelete`; retain `IHasConcurrency` explicit interface declaration and `RowVersion { get; protected set; }`; chain both constructors correctly through the new base | SharedKernel.Domain | `○` |
| C-18 | Add `Specification<T>.IsSatisfiedBy(T entity)` (P-038/WO-009): sealed (non-virtual) concrete method; private nullable `Func<T, bool>? _compiledCriteria` backing field; lazy compile-and-cache on first call; return `true` when `Criteria` is null; add XML `<remarks>` documenting caching semantics and null-criteria behaviour | SharedKernel.Domain | `○` |
| C-19 | Add `DomainEvent<TPayload>` abstract record to `Events/` (P-039/WO-009): `abstract record DomainEvent<TPayload> : DomainEvent where TPayload : notnull`; `required TPayload Payload { get; init; }`; XML doc with concrete usage example (`OrderPlacedEvent : DomainEvent<OrderPlacedPayload>`) | SharedKernel.Domain | `○` |
| C-20 | Add `bool AsNoTracking { get; }` to `ISpecification<T>` and implement on `Specification<T>` (P-040/WO-009): private backing field defaulting to `false`; `protected void ApplyNoTracking()` sets it to `true`; update `AndSpecification<T>`, `OrSpecification<T>`, `NotSpecification<T>` constructors to set `AsNoTracking = true` if either operand has `AsNoTracking = true`; add `ReadOnlySpecification<T>` sealed abstract class that calls `ApplyNoTracking()` in its constructor | SharedKernel.Domain | `○` |
| C-21 | Add XML `<remarks>` block to `AggregateRoot<TId>.Now` property (P-041/WO-009): document that `Now` must NOT be used to supply `OccurredOn` for domain events; state the correct alternative (`RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent>)` factory overload); explain the deterministic timestamp contract and why bypassing it makes event timestamps non-deterministic in tests | SharedKernel.Domain | `○` |

---

## Phase: Tests

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| T-01 | Entity equality tests: same `Id` → equal; different `Id` → not equal; different concrete type same `Id` → not equal; transient entity (`Id == default`) never equals any other instance including itself | SharedKernel.Domain | `●` |
| T-02 | Aggregate event accumulation tests: event raised appears in `DomainEvents`; `ClearDomainEvents()` empties the list; events do not leak across aggregate instances; `RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent>)` passes clock time from injected `IClock` | SharedKernel.Domain | `●` |
| T-03 | `NullClock` sentinel tests: ORM-path constructor sets `_clock` to `NullClock`; `NullClock.UtcNow` returns `DateTimeOffset.MinValue` | SharedKernel.Domain | `●` |
| T-04 | ValueObject structural equality tests: identical components → equal; changed single component → not equal; null component handled; `GetHashCode` consistent with equality | SharedKernel.Domain | `●` |
| T-05 | ValueObject validation tests: `Validate()` returning null → construction succeeds; returning one or more `Error` → `ValidationException` thrown at construction | SharedKernel.Domain | `●` |
| T-06 | Business rule composite tests: `AndBusinessRule` broken when either sub-rule is broken; `OrBusinessRule` broken only when both sub-rules are broken; `NotBusinessRule` inverts broken state; `And`/`Or`/`Not` extension methods produce correct wrappers | SharedKernel.Domain | `●` |
| T-07 | `CheckRule` on `AggregateRoot<TId>`: broken rule → `BusinessRuleViolationException` thrown; non-broken rule → no exception; exception carries the `IBusinessRule` instance | SharedKernel.Domain | `●` |
| T-08 | Policy composite tests: `AndPolicy<T>` compliant only when both sub-policies are compliant; `OrPolicy<T>` compliant when at least one is; `NotPolicy<T>` inverts compliance | SharedKernel.Domain | `●` |
| T-09 | Specification composition tests: `AndSpecification<T>` expression evaluates `left AND right`; `OrSpecification<T>` evaluates `left OR right`; `NotSpecification<T>` negates; composed expressions can be compiled and evaluated against in-memory collections | SharedKernel.Domain | `●` |
| T-10 | `StronglyTypedId<TValue>` tests: implicit `operator TValue` unwraps correctly; `ToString()` returns `Value.ToString()`; value-equality between two instances with same inner value | SharedKernel.Domain | `●` |
| T-11 | Audit interface coverage tests: `AuditableAggregateRoot<TId>` properties are settable only via ORM reflection (verify `private set`); `FullAuditableAggregateRoot<TId>` `RowVersion` has `protected set`; `SoftDeletableAggregateRoot<TId>.MarkAsDeleted` sets all three soft-delete fields | SharedKernel.Domain | `●` |
| T-12 | `FullAuditableAggregateRoot<TId>` hierarchy refactor regression tests (P-036/WO-009): all existing `AuditableAggregateTests` pass with zero modifications; verify `FullAuditableAggregateRoot<TId>` IS-A `AuditableSoftDeletableAggregateRoot<TId>` via `is` assertion; verify `MarkAsDeleted` and `OnDelete` are inherited (not re-declared) | SharedKernel.Domain | `○` |
| T-13 | `Specification<T>.IsSatisfiedBy` tests (P-038/WO-009): criteria-less specification returns `true` for any entity; entity matching criteria returns `true`; entity not matching criteria returns `false`; call `IsSatisfiedBy` multiple times on same spec instance and confirm the `Func<T,bool>` reference is identical (cached, no recompilation) | SharedKernel.Domain | `○` |
| T-14 | `DomainEvent<TPayload>` tests (P-039/WO-009): concrete record implementing `DomainEvent<TPayload>` exposes `Payload` correctly; implements `IDomainEvent`; `Id` is auto-generated as non-empty `Guid`; `OccurredOn` is supply-only via `required init` | SharedKernel.Domain | `○` |
| T-15 | `ISpecification<T>.AsNoTracking` tests (P-040/WO-009): `Specification<T>` defaults to `false`; `ApplyNoTracking()` sets to `true`; `ReadOnlySpecification<T>` always returns `true`; `AndSpecification<T>` / `OrSpecification<T>` / `NotSpecification<T>` propagate `true` when either operand has `AsNoTracking = true`; spec with both operands `false` propagates `false` | SharedKernel.Domain | `○` |

---

## Phase: Docs

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| DO-01 | Ensure all public interfaces carry triple-slash XML doc comments: `IEntity<TId>`, `IAggregateRoot<TId>`, `IValueObject`, `IDomainService`, `IStronglyTypedId<TValue>`, `IDomainEvent`, `IHasCreatedAudit`, `IHasAudit`, `ISoftDeletable`, `IHasConcurrency`, `IHasTenant`, `IBusinessRule`, `IPolicy<T>`, `ISpecification<T>` | SharedKernel.Domain | `●` |
| DO-02 | Ensure all public abstract base types carry XML doc comments: `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, `DomainEvent`, `StronglyTypedId<TValue>`, `Specification<T>`, all auditable bases — include usage example in `<example>` blocks | SharedKernel.Domain | `●` |
| DO-03 | Add `<remarks>` block on `StronglyTypedId<TValue>` noting that STJ serialization requires a custom `JsonConverter` in the consuming service — this package ships no converters | SharedKernel.Domain | `●` |
| DO-04 | Add `<remarks>` block on `DomainEvent` documenting the `OccurredOn` sourcing rule: must be supplied via `IClock.UtcNow` from the aggregate's `RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent>)` overload; direct `DateTimeOffset.UtcNow` usage is a hard violation | SharedKernel.Domain | `●` |
| DO-05 | Add `<remarks>` block on `AggregateRoot<TId>.ClearDomainEvents()` documenting that only infrastructure dispatch code should call this; aggregates must never self-clear | SharedKernel.Domain | `●` |
| DO-06 | Add `<remarks>` block on `IPolicy<T>` vs `IBusinessRule` documenting the distinction: policies evaluate domain object compliance; business rules validate raw primitive invariants | SharedKernel.Domain | `●` |
| DO-07 | Write `README.md` for `SharedKernel.Domain/` covering: quick-start aggregate example, value object example with `Validate()` hook, specification usage pattern, business rule composition example, note on `IDomainEventHandler<TEvent>` living in `05.Application` | SharedKernel.Domain | `●` |
| DO-08 | Update `CLAUDE.md` auditable aggregate hierarchy section (P-036/WO-009): replace `FullAuditableAggregateRoot<TId> extends AggregateRoot<TId>` with the corrected `extends AuditableSoftDeletableAggregateRoot<TId>`; update hierarchy diagram and interface contract surface | SharedKernel.Domain | `○` |
| DO-09 | Correct `CLAUDE.md` SharedKernel.Core dependency statement (P-037/WO-009): packages table `References` column for `SharedKernel.Domain` must list both `SharedKernel.Primitives` and `SharedKernel.Core`; technology stack table attributes `ValidationException` and `SharedKernelException` to `SharedKernel.Core.Exceptions`; implementation rules section states zero external NuGet deps (two `01.Core` project refs) | SharedKernel.Domain | `○` |
| DO-10 | Update `CLAUDE.md` specification system section (P-038/WO-009): document `Specification<T>.IsSatisfiedBy(T entity)` — compiled delegate cached on first call, returns `true` when `Criteria` is null, non-virtual concrete method, not present on `ISpecification<T>` | SharedKernel.Domain | `○` |
| DO-11 | Update `CLAUDE.md` events section (P-039/WO-009): document `DomainEvent<TPayload>` abstract record — extends `DomainEvent`, `required TPayload Payload { get; init; }`, `TPayload : notnull`; include usage pattern example; state non-generic `DomainEvent` is unchanged | SharedKernel.Domain | `○` |
| DO-12 | Update `CLAUDE.md` specification system section (P-040/WO-009): document `bool AsNoTracking { get; }` on `ISpecification<T>`, `ApplyNoTracking()` builder, `ReadOnlySpecification<T>` convenience base, and composite propagation rule | SharedKernel.Domain | `○` |
| DO-13 | Update `CLAUDE.md` implementation rules section (P-041/WO-009): add rule — aggregate subclasses must use the `RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent>)` factory overload to timestamp events; passing `Now` as `OccurredOn` in a pre-built event is a soft violation documented in the `Now` property XML | SharedKernel.Domain | `○` |

---

## Phase: Published

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| P-01 | Set final NuGet package metadata in `SharedKernel.Domain.csproj`: `PackageVersion`, `PackageReleaseNotes`, `RepositoryUrl`, `PackageLicenseExpression`, `PackageReadmeFile` | SharedKernel.Domain | `●` |
| P-02 | Run `dotnet pack SharedKernel.Domain.csproj -c Release` and verify zero warnings, zero NuGet dependency entries in generated `.nupkg` manifest — only `SharedKernel.Primitives` dependency present | SharedKernel.Domain | `●` |
| P-03 | Reference `SharedKernel.Domain` from a consumer stub project; verify `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, `ISpecification<T>`, and `StronglyTypedId<TValue>` are all usable without additional package references | SharedKernel.Domain | `●` |
| P-04 | Publish `SharedKernel.Domain` to the internal NuGet feed; confirm package appears in feed and is resolvable by version | SharedKernel.Domain | `●` |
| P-05 | Re-pack and re-publish `SharedKernel.Domain` after WO-009 changes (P-036..P-041): bump `PackageVersion` to `1.1.0`; verify `.nupkg` manifest still lists only `SharedKernel.Primitives` and `SharedKernel.Core` as dependencies; update `PackageReleaseNotes` with WO-009 changes | SharedKernel.Domain | `○` |

---

## Phase Backlog — Architectural Audit (WO-009)

> All WO-009 backlog items have been promoted to formal phase tasks above (D-15..D-18, C-17..C-21, T-12..T-15, DO-08..DO-13, P-05). This section is retained for traceability.

| Root Phase | Title | Promoted to | Status |
| ---------- | ----- | ----------- | :----: |
| P-036 | Fix Auditable Aggregate Hierarchy | D-15, C-17, T-12, DO-08 | `○` |
| P-037 | Correct CLAUDE.md — SharedKernel.Core dependency | DO-09 | `○` |
| P-038 | Add IsSatisfiedBy in-memory evaluation | D-16, C-18, T-13, DO-10 | `○` |
| P-039 | Add DomainEvent typed payload base record | D-17, C-19, T-14, DO-11 | `○` |
| P-040 | Add AsNoTracking flag to ISpecification | D-18, C-20, T-15, DO-12 | `○` |
| P-041 | Tighten AggregateRoot.Now documentation | C-21, DO-13 | `○` |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
|-----------|-------|:-----:|:------:|:---------:|:-----:|
| `SK.03.Design` | Design | 18 | 14 | 4 | `◐` |
| `SK.03.Scaffold` | Scaffold | 5 | 5 | 0 | `●` |
| `SK.03.Core` | Core | 21 | 16 | 5 | `◐` |
| `SK.03.Tests` | Tests | 15 | 11 | 4 | `◐` |
| `SK.03.Docs` | Docs | 13 | 7 | 6 | `◐` |
| `SK.03.Published` | Published | 5 | 4 | 1 | `◐` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-05-22] Sub state-map initialized — phase key registry, 6 phases scaffolded at ○, no tasks yet
- [2026-05-22] P-032 (WO-008) — 57 tasks added across all 6 phases for SharedKernel.Domain DDD foundation (domain-arch-planner)
- [2026-05-22] SK.03.Design complete — all 14 design tasks verified against CLAUDE.md; phase promoted to ● (domain-phase-implementer)
- [2026-05-22] SK.03.Scaffold complete — csproj with Primitives ref, 10 subdirs with .gitkeep, test csproj with xUnit+FluentAssertions, 6 stub test files; both projects build 0 errors (domain-phase-implementer)
- [2026-05-22] SK.03.Core complete — all 16 production types implemented: interfaces, Entity/AggregateRoot/ValueObject bases, auditable hierarchy, StronglyTypedId, business rules, policies, specifications with ExpressionVisitor composition; builds 0 errors (domain-phase-implementer)
- [2026-05-22] SK.03.Tests complete — 106 tests passing across 8 test files; NullClock sentinel tests added to AggregateRootEventTests to cover T-03 ORM-path clock behavior (domain-phase-implementer)
- [2026-05-22] SK.03.Docs complete — XML doc examples added to all abstract bases; IPolicy(T) IBusinessRule distinction remarks enhanced; ClearDomainEvents remarks block explicit; README.md written covering aggregate, value object, spec, business rule, and policy patterns; 106 tests green (domain-phase-implementer)
- [2026-05-22] SK.03.Published complete — PackageVersion + PackageReleaseNotes added to csproj; packed 0 errors (deps: Primitives + Core, both 01.Core); consumer stub 17 tests green via local feed; pushed SharedKernel.Domain.1.0.0.nupkg to nupkgs/ feed (domain-phase-implementer)
- [2026-05-22] WO-009 (P-036..P-041) — 20 new tasks added across Design/Core/Tests/Docs/Published phases; backlog items promoted to formal task rows; Overall Progress table updated (domain-arch-planner)
