# 03.Domain — DDD Building Blocks

> **Audience:** maintainers and AI agents changing code in this folder.
> **Consumers** read [`SharedKernel.Domain/README.md`](SharedKernel.Domain/README.md); the folder overview is
> [`README.md`](README.md). This brain holds only what the source does not make obvious: rules, traps,
> couplings and decisions. It never repeats the README.

## What This Domain Is

The DDD primitives layer. Every aggregate, entity, value object, strongly-typed identifier and domain event
in a downstream service derives from the types here.

**Philosophy:** pure domain model; no I/O, no system clock, no logging, no DI; invalid input is a result;
validation reports every error; fail loudly instead of silently.

**Hard rules**

1. References `01.Core` only (`SharedKernel.Primitives`, `SharedKernel.Core`). Never persistence, messaging,
   DI, logging or HTTP types.
2. Never read `DateTime.UtcNow`/`DateTimeOffset.UtcNow`; time comes from `IClock`.
3. Every public API change is recorded in `PublicAPI.Unshipped.txt`; every public member has XML docs.
4. Shipped docs, XML comments and release notes never contain WO/P IDs or change history.
5. AOT and trimming are **not** constraints (user ruling, 2026-09-15). Choose the best consumer API, even with
   reflection, and document it honestly.

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Domain` | All DDD building blocks | `SharedKernel.Primitives`, `SharedKernel.Core` |

| Project | Purpose |
| --- | --- |
| `SharedKernel.Domain/SharedKernel.Domain.Tests/` | Unit tests; `DomainHardeningTests` and `MoneyHardeningTests` pin fixed defects |
| `SharedKernel.Domain.ConsumerVerify/` | Restores the **published** package from the feed and exercises the public API |

Build settings: zero third-party dependencies; `Microsoft.CodeAnalysis.PublicApiAnalyzers` (private);
`nullable`, `CS1591` and RS0016/17/22/24/25/36/37 are errors; XML docs and `README.md` ship in the package.

## Technology Stack

| Concern | Choice |
| --- | --- |
| Validation results | `SharedKernel.Primitives` `ValidationResult<T>`, `Error`, `ErrorCodes` |
| Guards | `SharedKernel.Guards` namespace inside `SharedKernel.Core` (`Guard.Throw.*` throws `DomainException`) |
| JSON | System.Text.Json converter factory for strongly-typed identifiers |
| Currency data | Compiled-in frozen ISO 4217 table (`CurrencyCatalog`) |

## DI Registration

None. Everything is a base class, an interface, an extension method or a static factory.

## AOT Compatibility

Not a design constraint. Reflection in use: `StronglyTypedIdJsonConverterFactory` (`MakeGenericType` + compiled
constructor delegate) and `DomainEventVersionHelper` (attribute lookup, cached).

---

## Interface Contracts

| Namespace (`SharedKernel.Domain.`) | Types |
| --- | --- |
| `Abstractions` | `IEntity<out TId>`, `IAggregateRoot<TId>`, `IHasDomainEvents`, `IHasClock`, `IHasVersion`, `IHasAudit`/`IHasCreatedAudit`, `ISoftDeletable`, `IHasTenant`, `IHasConcurrency`, `IHasAggregateId<TId>`, `IStronglyTypedId<TValue>`, `IDomainEventDispatcher`; markers `IAggregateFactory<,>`, `IDomainService`, `IValueObject` |
| `Aggregates` | `AggregateRoot<TId>`; `Auditable…`, `SoftDeletable…`, `AuditableSoftDeletable…`, `FullAuditable…AggregateRoot<TId>`; a `Tenanted…` counterpart of each |
| `Entities` | `Entity<TId>`, `AuditableEntity<TId>`, `SoftDeletableEntity<TId>`, `AuditableSoftDeletableEntity<TId>`, `FullAuditableEntity<TId>` |
| `Events` | `IDomainEvent`, `DomainEvent`, `DomainEvent<TPayload>`, `DomainEventVersionAttribute`, `DomainEventVersionHelper` |
| `BusinessRules` | `IBusinessRule` (`Code`, `Message`, `IsBroken`), `And/Or/NotBusinessRule`, `BusinessRuleExtensions` |
| `ValueObjects` | `ValueObject`, `SingleValueObject<TValue>` |
| `StronglyTypedIds` | `StronglyTypedId<TValue>`; `Serialization.StronglyTypedIdJsonConverterFactory`, `StronglyTypedIdJsonConverter<TId, TValue>` |
| `Specifications` | `ISpecification<T>`, `Specification<T>`, `ReadOnlySpecification<T>`, `PagedSpecification<T>`, `KeysetSpecification<T, TKey>`, `AllSpecification<T>`, `EmptySpecification<T>`, `And/Or/NotSpecification<T>`, `SpecificationExtensions` |
| `Policies` | `IPolicy<in T>`, `And/Or/NotPolicy<T>`, `PolicyExtensions` (incl. `ToRule`) |
| `DomainServices` | `DomainService` |
| `Monetary` | `Money`, `Currency`, `CurrencyCatalog`, `RoundingPolicy`, `CurrencyMismatchRule`, `IExchangeRateProvider`, `MoneyExtensions` |
| `Exceptions` | `BusinessRuleViolationException`, `DomainNotFoundException` |
| `Internal` (internal) | `DomainInvariants` (the only `CheckRule`/`TryCreate`/`NotNull` implementation), `SoftDeletion` |

Base-class matrix (closed; do not add combinations — a consumer needing another extends `AggregateRoot<TId>` and
implements the interfaces, which is all persistence reads):

| Non-tenanted aggregate | Extends | Tenanted counterpart | Entity mirror |
| --- | --- | --- | --- |
| `AggregateRoot` | `Entity` | `TenantedAggregateRoot` | `Entity` |
| `AuditableAggregateRoot` | `AggregateRoot` | `TenantedAuditableAggregateRoot` | `AuditableEntity` |
| `SoftDeletableAggregateRoot` | `AggregateRoot` | `TenantedSoftDeletableAggregateRoot` | `SoftDeletableEntity` |
| `AuditableSoftDeletableAggregateRoot` | `AggregateRoot` | `TenantedAuditableSoftDeletableAggregateRoot` | `AuditableSoftDeletableEntity` |
| `FullAuditableAggregateRoot` | `AuditableSoftDeletableAggregateRoot` | `TenantedFullAuditableAggregateRoot` | `FullAuditableEntity` |

---

## Implementation Rules

### Entities and aggregates

| Rule | Detail |
| --- | --- |
| Entity equality | Sealed. Same concrete runtime type and equal non-default `Id`. `ReferenceEquals` first, so a **transient** entity (default `Id`) equals itself and nothing else; its hash is by reference until an `Id` is assigned. `Entity<TId>`'s `id` argument is deliberately unguarded: default is the transient sentinel. |
| Clock | `AggregateRoot<TId>` holds a nullable `IClock`; the ORM constructor sets none. `Now` **throws `InvalidOperationException`** without one. Never add a sentinel clock (a `NullClock` returning `MinValue` once stamped year 0001 on every event of a loaded aggregate). Attach only through the explicit `IHasClock.AttachClock`. |
| Events | Only through `RaiseDomainEvent` (both overloads null-check). Each call increments `Version`, the **event sequence number** (`IHasVersion`), never a concurrency token. `ClearDomainEvents` does not reset it. |
| Audit fields | Private setters, written only by persistence. `RowVersion` has a protected setter (PostgreSQL maps it to `xmin`). |
| Tenancy | Tenanted bases guard `tenantId` with `Guard.Throw.InvalidGuid` (never `Guid.Empty`). `TenantId` never changes. The domain receives it as a `Guid`; never reference `ITenantProvider`. |
| Soft delete | Aggregates: `MarkAsDeleted(deletedBy)` uses `Now`, calls `OnDelete` once. Entities: `MarkAsDeleted(deletedBy, deletedOn)`, `deletedOn` must be UTC. Both go through `SoftDeletion.ShouldMarkDeleted`: a blank actor throws `DomainException` (even when already deleted); an already-deleted record is left untouched (no second event, original actor and time kept). |

### Rules, creation and exceptions

| Rule | Detail |
| --- | --- |
| Rule codes | `IBusinessRule.Code` is **required**; no default or fallback. `BusinessRuleViolationException` uses `rule.Code`. |
| Composites | `And`: first broken operand's code, broken messages joined with `"; "`, empty when unbroken. `Or`: left code. `Not`: requires its own code and message. |
| One implementation | `CheckRule`/`TryCreate` on `AggregateRoot`, `ValueObject` and (`CheckRule` only) `DomainService` delegate to `Internal.DomainInvariants`. Never reimplement. |
| `TryCreate<T>` | Returns `ValidationResult<T>`: `ValidationException` → all its errors; **any `DomainException`** (rules, guards) → its single error; anything else rethrows. Never catch broader. |
| `DomainNotFoundException` | Message `"{TypeName} '{id}' was not found."`; both arguments null-checked; code `not_found.default`. |

### Value objects

| Rule | Detail |
| --- | --- |
| Validation | **The base constructor never validates.** A subclass assigns members and calls `EnsureValid()` last; it throws `ValidationException` with every error. Omitting it compiles and silently skips validation — SK0037 reports it. |
| `SingleValueObject<TValue>` | Calls `EnsureValid()` in its own constructor; null value → `DomainException` with `ErrorCodes.Validation.Required` via `DomainInvariants.NotNull` (`Guard.Throw.Null` cannot take an unconstrained `TValue`). |
| Equality | Same runtime type, component-wise. A non-string `IEnumerable` component compares and hashes element by element. |
| Conversions | `SingleValueObject<TValue>` and `StronglyTypedId<TValue>` have **explicit** operators only; null → `ArgumentNullException`. |
| `Money` | Validated by constructor guards (`Guard.Throw.Null(currency)`, `InvalidEnumValue(roundingPolicy)`); `Validate()` returns empty. |

### Strongly-typed identifiers and JSON

- Shape: `public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);`.
- The factory handles any concrete `StronglyTypedId<TValue>` whose `TValue` STJ can serialize (no allowlist),
  writes the bare value, reads JSON `null` as null (`HandleNull => false`), and supports dictionary keys by
  delegating `Read/WriteAsPropertyName` to `TValue`'s converter. Construction failures are rethrown unwrapped
  from `TargetInvocationException`.

### Specifications

| Rule | Detail |
| --- | --- |
| Criteria | `AddCriteria` **accumulates with AND** and resets the compiled `IsSatisfiedBy` cache. Never make it replace. |
| Sorting | One primary sort; a second `ApplyOrderBy`/`ApplyOrderByDescending` throws. |
| Paging | `ApplyPaging` rejects negative skip and take < 1. `PagedSpecification` computes the offset in `long` and rejects one above `int.MaxValue`; `MaxPageSize` is 1000. |
| Keyset | `TKey : struct, IComparable<TKey>`. The `Id` tiebreak sorts **in the primary direction**; an ascending tiebreak under a descending key skips or repeats rows sharing a key. |
| Composites | Copy criteria and query shape through `private protected CopyQueryShapeFrom` (includes deduplicated by reference, string includes by ordinal value, tracking/split/include-deleted OR-ed). Never copy ordering, paging or `Distinct`. `Not` of a criteria-less spec matches nothing. |
| Soft-deleted rows | `IncludeDeleted` bypasses every EF Core global filter, tenancy included. |

### Policies, events and Money

| Rule | Detail |
| --- | --- |
| Policies | `IPolicy<in T>` is contravariant; `Explain` is empty when compliant. `NotPolicy`/`.Not(explanation)` require an explanation. `policy.ToRule(subject, code)` is the only bridge to `CheckRule`. |
| Event identity | `DomainEvent.Id` is `init`-settable, defaulting to `Guid.CreateVersion7()`, so it survives serialization. Never make it get-only. Every concrete event declares `[DomainEventVersion(n)]` (SK0009). |
| Money namespace | `SharedKernel.Domain.Monetary` (a namespace named `Money` collided with the type). |
| Rounding | Arithmetic uses banker's rounding; `Multiply`/`Divide` take a policy. `RoundingPolicy` = `BankersRounding, AwayFromZero, ToZero, Ceiling, Floor`; never `Up`/`Down`, which libraries define differently. |
| Mismatch | Cross-currency `+ - < > <= >= Min Max Sum` throw `BusinessRuleViolationException` with `money.currency_mismatch`. |
| Allocation | Largest remainder; leftover minor units go to the later indexes; parts always sum to the original. |
| Sum | `Sum()` throws `InvalidOperationException` on an empty sequence; `Sum(currency)` returns zero. |
| Catalog | Frozen, compiled in, carries `RegistryAsOf`. Update only against ISO 4217 amendments. |
| Conversion | `IExchangeRateProvider` is a port; `ConvertAsync` builds the result through the internal `Money.FromTrusted`. |

### General

- No static mutable state except the `DomainEventVersionHelper` cache.
- `ToString()` of `Money` is invariant culture (`"59.97 USD"`); culture only through `IFormattable`.

---

## Cross-Domain Couplings

Changes here that silently break another layer. Check the right column before merging.

| If you change… | Also check |
| --- | --- |
| The explicit operator on `StronglyTypedId<TValue>` | `06.Persistence` `SpecificationEvaluator` finds `op_Explicit` **by reflection** for keyset paging; a rename breaks at runtime, not compile time |
| `IHasClock`, the ORM constructors or `Now` | `06.Persistence` `DomainClockMaterializationInterceptor` |
| `IHasVersion` or the event sequence semantics | `06.Persistence` `EntityTypeConfigurationBase.ConfigureEventSequence` (maps `Version` as a required, non-concurrency column) |
| Keyset tiebreak direction | `06.Persistence` seek predicate, which flips both comparisons when descending |
| `CurrencyCatalog` codes or minor units | `01.Core` `SharedKernel.Validation` holds a second ISO 4217 table; keep them consistent |
| `Money`/`Currency` factories | `06.Persistence` `MoneyValueConverter`/`CurrencyValueConverter`; `16.Testing` `MoneyFaker` |
| `IBusinessRule`, `ValueObject` validation or `TryCreate` | `16.Testing` fixtures; `samples/OrderApi`; `00.Governance` SK0037 and `AggregateFactoriesMustCreateValidationResults` |
| `IDomainEventDispatcher` | `05.Application` `MediatRDomainEventDispatcher`; `06.Persistence` unit of work |
| Any public API | `PublicAPI.Unshipped.txt`, `SharedKernel.Domain.ConsumerVerify`, the package README |

---

## Decision Records

| Decision | Chosen | Rejected | Accepted cost |
| --- | --- | --- | --- |
| Value object validation | Explicit `EnsureValid()` last in the constructor | Base-constructor validation (virtual call before members are assigned); factory-only validation (least enforced) | An omitted call compiles; SK0037 reports it |
| Clock on loaded aggregates | Attach on load through `IHasClock`; throw when absent | Time passed into every mutating method (largest break); a null clock (silent wrong data) | Non-EF loaders must attach the clock themselves |
| `TryCreate` return type | `ValidationResult<T>` (keeps every error) | `Result<T>` (single error) | `Result<T>` interop is by the caller (`Errors[0]`) |
| Kept abstractions (user ruling) | `IHasVersion` as event sequence; `IAggregateFactory` with an architecture rule; `IPolicy<T>`; audit/soft-delete/tenant bases with missing tenanted combinations added | Removing them as unused | Larger surface to maintain |
| Rule codes | Required `Code` on every rule | Optional code with a shared default | Every rule author picks a code |
| Conversions | Explicit only | Implicit (identifiers flow into any `Guid`) | `.Value` or a cast at call sites |
| Equality strategy | Abstract classes + `GetEqualityComponents()` | Records (generated equality and constructors bypass validation) | More boilerplate per value object |
| AOT | Not a constraint | AOT-clean JSON limited to four identifier value types | Reflection in the JSON factory |

---

## Test Rules

- Every behaviour change is pinned in `DomainHardeningTests`/`MoneyHardeningTests` with the pre-fix behaviour
  noted, and proven by perturbation: reverting the fix fails the named tests.
- Test rules use their own `Code` (`"test.rule"` where the code is irrelevant).
- Value-object fixtures call `EnsureValid()`; a fixture relying on base-constructor validation is a test bug.
- Code snippets in the package README compile and their shown outputs are produced by running them.
- `ConsumerVerify` resolves the package from a feed and is updated with every public API change.

---

## Changelog

> Maintained by the domain agent. One line per significant change. Entries before 2026-09-15 are historical: they describe what was true when written (NullClock, implicit conversions, base-constructor validation, `Result<T>` from `TryCreate`, a single rule code, per-package versions), not current behaviour.

- [2026-05-22] Domain brain initialized — packages, interfaces, rules, AOT notes
- [2026-05-22] P-032 (WO-008) — full public surface documented: all interfaces, abstract bases, auditable hierarchy, ValueObject strategy decision, business rules, policies, specifications, strongly-typed IDs, NullClock sentinel, IDomainEventHandler exclusion boundary
- [2026-05-22] WO-009 (P-036..P-041) — refreshed: corrected FullAuditableAggregateRoot hierarchy (extends AuditableSoftDeletableAggregateRoot); corrected SharedKernel.Core dependency declaration; added IsSatisfiedBy and AsNoTracking to specification surface; added `DomainEvent<TPayload>`; added Now property usage constraint; updated all affected sections (domain-arch-planner)
- [2026-05-26] SK.03.Design closed — D-15..D-18 verified against CLAUDE.md; all 18 design tasks ●; no content edits needed (designs already present) (domain-phase-implementer)
- [2026-05-26] SK.03.Published complete — SharedKernel.Domain 1.1.0 packed; manifest deps: Primitives + Core only; no new architectural signals (domain-phase-implementer)
- [2026-05-27] WO-010 (P-043, P-044) — refreshed: BusinessRuleViolationException now extends DomainException and uses Error.BusinessRule; ValueObject constructor hazard documented; tenanted aggregate family (3 bases) added to hierarchy; IHasTenant implementation rules added; version bump to 1.2.0 planned (domain-arch-planner)
- [2026-05-27] WO-011 (P-045..P-054) — refreshed: IHasDomainEvents extracted; SingleValueObject<TValue> added; DomainService abstract base added; IHasVersion added; DomainNotFoundException added; AllSpecification/EmptySpecification sentinels added; PagedSpecification<T> added; ApplyThenByDescending alias added; DomainEventVersionAttribute/Helper added; IAggregateFactory + TryCreate<T> added; version bump to 1.3.0 planned (domain-arch-planner)
- [2026-05-27] SK.03.Published complete — SharedKernel.Domain 1.2.0 and 1.3.0 packed; manifests: Primitives + Core only; no new architectural signals (domain-phase-implementer)
- [2026-06-02] P-095/WO-016 — ISpecification(T).IncludeDeleted flag added to public surface; Specification(T).IncludeSoftDeleted() builder documented; composite spec propagation rule added (IncludeDeleted = true if any operand true, mirrors AsNoTracking); implementation rules section updated with IgnoreQueryFilters() bypass warning and tenant isolation caveat; AOT note added; test rule added; version bump to 1.4.0 planned (domain-arch-planner)
- [2026-06-02] P-081/WO-014 — IDomainEventDispatcher interface added to public surface (Abstractions/ section); implementation rule added (only dispatch interface permitted; empty-list no-op and exception propagation contract; opt-in DI; MediatR impl deferred to 05.Application); AOT note added; ContractShapeTests rule added; version bump to 1.5.0 planned (domain-arch-planner)
- [2026-06-12] P-152/WO-024 — `StronglyTypedIdJsonConverterFactory` + `StronglyTypedIdJsonConverter<TStronglyTypedId, TValue>` added to public surface (new StronglyTypedIds/Serialization/ section); supports Guid/int/long/string, bare-primitive wire format, opt-in registration via `options.Converters.Add(...)`; `StronglyTypedId<TValue>` "ships none" note replaced; implementation rules added (supported TValue shapes, concrete-type constructor shape requirement, opt-in-only); AOT note added (cached `Expression.New` activator, same class as `DomainEventVersionHelper` precedent, PublishAot caveat documented); test rule added (round-trip, wire-format, CanConvert negative cases); version bump to 1.6.0 planned (domain-arch-planner)
- [2026-06-15] SK.03.Docs complete (DO-29) — `StronglyTypedId<TValue>` source XML `<remarks>` updated to describe `StronglyTypedIdJsonConverterFactory` opt-in; `SharedKernel.Domain.csproj` bumped to 1.6.0 with WO-024 release notes; CLAUDE.md content already current from prior pass; 246 tests green (domain-phase-implementer)
- [2026-06-15] SK.03.Published complete (P-10) — SharedKernel.Domain 1.6.0 packed and verified; manifest deps: Primitives + Core only; consumer-verify confirms StronglyTypedIdJsonConverterFactory/Converter exported (19/19); all 6 phases of 03.Domain now ● complete; no new architectural signals (domain-phase-implementer)
- [2026-07-29] WO-051 (P-307..P-313) — CLAUDE.md refreshed for a seven-phase gap-fill batch targeting `SharedKernel.Domain` v1.7.0: (1) corrected a pre-existing documentation gap discovered during grounding research against shipped source — `StringIncludes`/`ISpecification<T>` and `AddStringInclude`/`Specification<T>` were already shipped but never documented in this brain; both now appear in the Specification system section; (2) `AndSpecification<T>`/`OrSpecification<T>`/`NotSpecification<T>` now documented to union `Includes`/`StringIncludes`, fixing a confirmed silent eager-loading data-loss defect (P-307); (3) added `KeysetSpecification<T, TKey>` cursor/seek-pagination base (mandatory Id tiebreaker) and `ISpecification<T>.AsSplitQuery` flag with `AsNoTracking`-identical propagation (P-308); (4) added `IHasAggregateId<TId>` opt-in marker mirroring `IHasTenant` (P-309); (5) added `ValueObject.TryCreate<T>`/`CheckRule`, achieving full parity with `AggregateRoot<TId>`'s existing helpers (P-310); (6) added `SharedKernel.Guards` as a third `01.Core` `ProjectReference` (Packages table updated), guarded `AggregateRoot<TId>`'s `clock` parameter (one guard site protects all four aggregate bases via constructor chaining), guarded `StronglyTypedId<TValue>`/`SingleValueObject<TValue>`'s `Value` for reference-type `TValue`, documented `Entity<TId>.id`'s deliberate unguarded transient-sentinel exception, cached `DomainEventVersionHelper.GetVersion`'s reflection lookup via `ConcurrentDictionary<Type,int>`, and added `IEquatable<Entity<TId>>`/`IEquatable<ValueObject>` (P-311); (7) added `IPolicy<T>.Explain` as a zero-breaking-change C# default interface member with composite aggregation mirroring `AndBusinessRule.Message` (P-312); (8) added `Specification<T>.Create(criteria)` ad hoc factory extending the `AllSpecification<T>`/`EmptySpecification<T>` sealed-wrapper precedent (P-313). Implementation Rules, AOT Compatibility, and Test Rules sections all extended accordingly; 41 new tasks recorded in state-map.md across all 6 phases (domain-arch-planner, WO-051)
- [2026-07-30] SK.03.Design closed — D-34..D-42 (WO-051/P-307..P-313) independently re-verified against shipped `.cs` files (not re-trusted from the arch-planner's changelog claim alone): composite spec Include/StringInclude propagation gap, tenanted-base `base(id, clock)` constructor chaining, `AndBusinessRule.Message`'s `string.Join("; ", ...)`, `ValueObject.cs`'s current XML `<example>`, and `SharedKernel.Guards`' `Guard.Throw.Null<T>` shape all confirmed accurate; no discrepancies found; no content edits needed (designs already present) (domain-phase-implementer)
- [2026-07-30] SK.03.Core closed — C-38..C-46 (WO-051/P-307..P-313) implemented: Include/StringInclude union-propagation fix in And/Or/NotSpecification; `IHasAggregateId<TId>`; `ValueObject.TryCreate<T>`/`CheckRule`; Guards adoption; `DomainEventVersionHelper` caching + `IEquatable<T>` on `Entity<TId>`/`ValueObject`; `IPolicy<T>.Explain` DIM; `Specification<T>.Create(criteria)`. **Corrected `KeysetSpecification<T, TKey>`'s constraint during implementation** — D-35's `where TKey : IComparable<TKey>` does not compile as designed; empirically confirmed the compiler erases `TKey?` to plain `TKey` for value-type closures absent a `struct` constraint, which would have silently broken the "null = first page" cursor contract for the design's own `DateTimeOffset` example. Shipped constraint is `struct, IComparable<TKey>`, narrowing `KeysetSpecification<T, TKey>` to value-typed sort keys only (documented in the Specification system section and Implementation Rules). All 46 Core tasks now `●`; 316/316 tests green (33 new), 0 build warnings (domain-phase-implementer)
- [2026-07-30] SK.03.Docs closed — DO-30..DO-36 (WO-051/P-307..P-313) independently re-verified against shipped `.cs` files (not re-trusted from the WO-051 arch-planner changelog claim alone): `AndSpecification<T>`/`OrSpecification<T>`/`NotSpecification<T>`'s `Includes`/`StringIncludes` union propagation, `KeysetSpecification<T, TKey>`'s constructor/guards/`struct` constraint, `ISpecification<T>.AsSplitQuery` + composite propagation, `IHasAggregateId<TId>`, `ValueObject.TryCreate<T>`/`CheckRule` (including the rewritten `Money.Create` `<example>`), the `SharedKernel.Guards` `ProjectReference` plus `AggregateRoot<TId>`/`StronglyTypedId<TValue>`/`SingleValueObject<TValue>` guard sites and `Entity<TId>.id`'s deliberate unguarded exception, `DomainEventVersionHelper`'s `ConcurrentDictionary` caching, `Entity<TId>`/`ValueObject`'s `IEquatable<T>` additions, `IPolicy<T>.Explain` DIM with `AndPolicy<T>`/`OrPolicy<T>`/`NotPolicy<T>` aggregation, and `Specification<T>.Create(criteria)`/`CriteriaSpecification<T>` — all confirmed to match this brain's existing content exactly, with zero discrepancies. No content edits required; only this changelog line added. 317/317 tests green (Release). All 36 Docs tasks now `●`; SK.03.Docs phase → `●` (domain-phase-implementer)
- [2026-07-30] SK.03.Published complete (P-11) — SharedKernel.Domain 1.7.0 packed and verified; manifest deps: Primitives + Core + Guards only (zero external NuGet); consumer-verify extended with 10 new tests confirming `KeysetSpecification<T,TKey>`, `AsSplitQuery`, `IHasAggregateId<TId>`, `ValueObject.TryCreate<T>`, `IPolicy<T>.Explain`, `Specification<T>.Create(criteria)`, and the P-307 composite Include/StringInclude union-propagation fix are all exported and functioning through a real PackageReference resolution (28/28 consumer tests green, 317/317 domain tests green); all 6 phases of 03.Domain (Design, Scaffold, Core, Tests, Docs, Published) now ● complete — WO-051 v1.7.0 cycle closed end to end; no new architectural signals beyond what Core/Docs already documented (domain-phase-implementer)

- [2026-08-26] WO-066/P-439 — CLAUDE.md refreshed for `Money`, this platform's most conspicuous pre-WO-066 gap given how fintech-grade the recent work orders (WO-058 step-up auth, WO-060 FAPI 2.0 hardening) had already become. Added a new "Money system" Interface Contracts subsection: `RoundingPolicy` (`BankersRounding` default / `AwayFromZero`); `CurrencyCatalog` (fixed static ISO 4217 minor-unit-exponent table, correctly distinguishing the zero-decimal and three-decimal exceptions from the 2-digit default); `Currency` (`SingleValueObject<string>` with a derived `MinorUnitDigits`, well-known static convenience instances); `Money` (`ValueObject`, not `SingleValueObject<TValue>` — two independent components — with unconditional construction-order-safe rounding, no separate excess-precision rejection path); the full arithmetic/comparison surface plus a largest-remainder-method `Allocate` proven to conserve minor units exactly; and a zero-I/O `IExchangeRateProvider` port plus `MoneyExtensions.ConvertAsync`, mirroring `IDomainEventDispatcher`'s already-shipped async-on-a-pure-interface precedent. Added `CurrencyMismatchRule` to the Business rule system section — a public, top-level, reusable `IBusinessRule` sibling to `AndBusinessRule`/`OrBusinessRule`/`NotBusinessRule`, deliberately reusing the already-shipped `ValueObject.CheckRule`/`BusinessRuleViolationException`/`ErrorType.BusinessRule` → HTTP 422 pipeline rather than inventing a new exception type for cross-currency rejection. Implementation Rules, AOT Compatibility, and Test Rules sections all extended accordingly. Purely additive — zero new NuGet dependency, zero new `ProjectReference` (still exactly `SharedKernel.Primitives` + `SharedKernel.Core` + `SharedKernel.Guards`), zero breaking change to existing public surface. Percentage/interest-calculation helpers explicitly declared out of scope (future extension only, on real consumer need). `samples/OrderApi/Domain/Money.cs`'s evaluation-for-replacement is explicitly flagged as outside this domain's jurisdiction — a cross-cutting follow-up for `arch-lead`/root `state-map.md`, never an action `03.Domain` takes itself. Also noted: per the root `CLAUDE.md`'s 2026-08-25 versioning switch, the Published phase for this work order no longer bumps a per-package `PackageVersion`/`PackageReleaseNotes` — MinVer now derives the shipped version repo-wide from the next `git tag`. 30 new tasks recorded in state-map.md across all 6 phases (domain-arch-planner, WO-066)
- [2026-09-02] SK.03.Core closed (WO-066/C-47..C-53) — `RoundingPolicy`/`CurrencyCatalog`/`Currency`/`CurrencyMismatchRule`/`Money` fully implemented; the "Money system" subsection and Implementation Rules corrected post-implementation for one genuine design/compiler mismatch (`Money`'s private-constructor field-initializer technique cannot compile with `private` accessibility on a `sealed` non-abstract class — the currency-null-guard moved into the constructor body, `Validate()` is now documented as a no-op) plus one previously-unspecified detail (`Allocate`'s leftover-minor-unit tie-break is by descending original index, matching the `$10.00`→`[3.33, 3.33, 3.34]` worked example). 417/417 tests green, 0 build warnings (domain-phase-implementer)
- [2026-09-10] WO-082/P-509 — `SharedKernel.Guards` was merged into `SharedKernel.Core` upstream (P-505, 01.Core), retiring `01.Core/SharedKernel.Guards/` as an independent package; the `SharedKernel.Guards`/`SharedKernel.Guards.Clauses`/`SharedKernel.Guards.Descriptions` C# namespaces were deliberately preserved on the merge (now physically hosted inside `SharedKernel.Core`). `SharedKernel.Domain.csproj`'s `ProjectReference` to the now-deleted `SharedKernel.Guards.csproj` was re-pointed onto `SharedKernel.Core.csproj` — already permitted by the root layering table ("03.Domain may reference 01.Core"), so no new layering exception was needed. Verified by direct source inspection that `AggregateRoot.cs`, `Money.cs` (the only two production `Guard.Against`/`Guard.Throw` call sites in this domain), `StronglyTypedId.cs`, and `SingleValueObject.cs` (comment-only `Guard.Throw.Null<T>` mentions, no live call) all needed zero source change — their existing `using SharedKernel.Guards;` directives resolve unchanged against the new assembly. Packages table, and the Implementation Rules/AOT Compatibility current-state prose that named `SharedKernel.Guards` as a live `ProjectReference`, updated to describe it as a namespace now hosted inside `SharedKernel.Core`; all prior WO-051/WO-066 changelog entries above are left untouched as historical record of what was true when written, per the root `CLAUDE.md`'s "historical record, not current state" convention for versioned/dependency claims. `dotnet build` clean (0 warnings/0 errors); `dotnet test` 423/423 green — no test file changed. This domain's own `00.Governance` layering re-verification (`SharedKernel.ArchitectureTests`' 03.Domain rules) is deferred to P-508, which cannot build until this phase lands (`SharedKernel.ArchitectureTests` itself still references the now-deleted `SharedKernel.Guards.csproj`). No new phase/task IDs were opened in `03.Domain/state-map.md` for this change — `03.Domain` is fully `●` Published and this is a mechanical upstream-merge follow-up, not a new architectural phase; the Package Board's `SharedKernel.Guards` dependency mentions describe the last real `dotnet pack` run (P-12, WO-066) and are intentionally left as-is until the next real re-pack (domain-phase-implementer)
- [2026-09-15] P-540 — pre-first-publish gold-standard pass, audited by execution. Fixed: transient entities not equal to themselves; aggregates loaded by an ORM stamping 0001-01-01 on events and DeletedOn (NullClock removed, IHasClock added, Now throws without a clock); AddCriteria replacing earlier criteria; TryCreate letting guard violations escape and truncating validation to one error (now ValidationResult<T>); DomainEvent.Id regenerated on deserialization (init + UUIDv7); BIF minor units and stale/missing ISO 4217 codes; Money.ToString printing the type name; duplicate soft-delete events; PagedSpecification offset overflow; empty tenants accepted; two primary sorts allowed; value-object collection components compared by reference; keyset Id tiebreak sorted against the seek predicate; strongly-typed ID JSON failing as dictionary keys and limited to four value types. Changed by user ruling: IBusinessRule.Code required; explicit EnsureValid; explicit conversions; Money moved to Monetary with IFormattable, predicates, Divide, Min/Max, Sum and ToZero/Ceiling/Floor; missing tenanted soft-delete bases and soft-deletable entity bases added; IHasVersion redefined as event sequence; IPolicy polished with ToRule; AOT dropped as a constraint. Public API tracked (404 lines). 425 → 511 domain tests; 10 perturbations each caught; cross-domain fixes in 05/06/16/samples; unit filter and Postgres integration suites green (domain-phase)
- [2026-09-15] Brain restructured for readability (tables, cross-domain couplings); README and folder landing page rewritten (agent)
