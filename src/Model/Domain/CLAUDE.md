# 03.Domain — Domain Brain

> `SharedKernel.Domain` is the DDD primitives package: every aggregate, entity, value object, strongly-typed
> identifier, domain event, business rule, policy, specification and `Money` in a consuming service derives from the
> types here. It is pure domain code — no I/O, no system clock, no logging, no DI, no persistence, messaging or HTTP
> types. Invalid input is a result, validation reports every error, and the package fails loudly rather than silently.
> It deliberately does not own paging (the repository call site does), wire contracts (`04.Contracts`), domain-event
> dispatch (`05.Application`) or ORM mapping (`06.Persistence`). The API manual is `SharedKernel.Domain/README.md`;
> this brain holds only the rules, traps, couplings and decisions the source does not make obvious.

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Domain` | Model | All DDD building blocks. References `SharedKernel.Primitives`, `SharedKernel.Core` (guards) and `SharedKernel.Execution` (`TenantId`) only; zero third-party packages (only the private `PublicApiAnalyzers`). |
| `SharedKernel.Domain.ConsumerVerify` | — (not packable) | Restores the packed `SharedKernel.Domain` through a `PackageReference` and exercises the public API. |

## Public Entry Points

No DI registration and no configuration section — everything is a base class, an interface, an extension method or a
static factory. Members and examples: `SharedKernel.Domain/README.md`.

| Namespace (`SharedKernel.Domain.`) | Key types |
| --- | --- |
| `Abstractions` | `IEntity<TId>`, `IAggregateRoot<TId>`, `IHasDomainEvents`, `IHasClock` (`AttachClock`), `IHasVersion`, `IHasAudit`/`IHasCreatedAudit`, `ISoftDeletable`, `IHasTenant`, `IHasConcurrency`, `IHasAggregateId<TId>`, `IStronglyTypedId<TValue>`, `IDomainEventDispatcher`, `TenantSharedAttribute` (`[TenantShared]` global reference data in a tenanted model); markers `IAggregateFactory<,>`, `IDomainService`, `IValueObject` |
| `Aggregates` | `AggregateRoot<TId>`, `Auditable…`, `SoftDeletable…`, `AuditableSoftDeletable…`, `FullAuditable…AggregateRoot<TId>`, and a `Tenanted…` counterpart of each |
| `Entities` | `Entity<TId>`, `AuditableEntity<TId>`, `SoftDeletableEntity<TId>`, `AuditableSoftDeletableEntity<TId>`, `FullAuditableEntity<TId>` |
| `Events` | `IDomainEvent`, `DomainEvent`, `DomainEvent<TPayload>`, `DomainEventVersionAttribute`, `DomainEventVersionHelper` |
| `BusinessRules` | `IBusinessRule` (`Code`, `Message`, `IsBroken`), `AndBusinessRule`/`OrBusinessRule`/`NotBusinessRule`, `BusinessRuleExtensions` |
| `ValueObjects` | `ValueObject` (`EnsureValid`, `TryCreate<T>`, `CheckRule`), `SingleValueObject<TValue>` |
| `StronglyTypedIds` | `StronglyTypedId<TValue>`; `.Serialization`: `StronglyTypedIdJsonConverterFactory`, `StronglyTypedIdJsonConverter<TId, TValue>` |
| `Specifications` | `ISpecification<T>`, `Specification<T>` (+ `Specification<T>.Create(criteria)`), `Spec.For<T>()` → `SpecificationBuilder<T>` / `IncludableSpecificationBuilder<T, TProperty>` (typed `ThenInclude`; `.Select` → projection), `ProjectionSpecification<T, TResult>`/`IProjectionSpecification<T, TResult>`, `AllSpecification<T>`, `EmptySpecification<T>`, `AndSpecification<T>`/`OrSpecification<T>`/`NotSpecification<T>`, `SpecificationExtensions` |
| `Policies` | `IPolicy<in T>` (`Explain`), `AndPolicy<T>`/`OrPolicy<T>`/`NotPolicy<T>`, `PolicyExtensions` (`And`, `Or`, `Not`, `ToRule`) |
| `DomainServices` | `DomainService` |
| `Monetary` | `Money`, `Currency`, `CurrencyCatalog`, `RoundingPolicy`, `CurrencyMismatchRule`, `IExchangeRateProvider`, `MoneyExtensions` (`ConvertAsync`) |
| `Exceptions` | `BusinessRuleViolationException`, `DomainNotFoundException` |
| `Internal` (internal) | `DomainInvariants` (the only `CheckRule`/`TryCreate`/`NotNull` implementation), `SoftDeletion` |

Base-class matrix (closed — do not add combinations; a consumer needing another extends `AggregateRoot<TId>` and
implements the interfaces, which is all persistence reads):

| Non-tenanted aggregate | Extends | Tenanted counterpart | Entity mirror |
| --- | --- | --- | --- |
| `AggregateRoot` | `Entity` | `TenantedAggregateRoot` | `Entity` |
| `AuditableAggregateRoot` | `AggregateRoot` | `TenantedAuditableAggregateRoot` | `AuditableEntity` |
| `SoftDeletableAggregateRoot` | `AggregateRoot` | `TenantedSoftDeletableAggregateRoot` | `SoftDeletableEntity` |
| `AuditableSoftDeletableAggregateRoot` | `AggregateRoot` | `TenantedAuditableSoftDeletableAggregateRoot` | `AuditableSoftDeletableEntity` |
| `FullAuditableAggregateRoot` | `AuditableSoftDeletableAggregateRoot` | `TenantedFullAuditableAggregateRoot` | `FullAuditableEntity` |

## Rules & Invariants

**Package boundary**

1. Reference only `Primitives`, `Core` and `Execution`; never `SharedKernel.Contracts`
   (`DomainNeverReferencesContracts`), logging, DI, persistence, messaging or HTTP types.
2. Never read `DateTime.UtcNow`/`DateTimeOffset.UtcNow`; time comes from `IClock`.
3. XML docs and the README never contain work-order IDs or change history.
4. No static mutable state except the `DomainEventVersionHelper` cache.

**Entities and aggregates**

5. Entity equality is sealed: same runtime type and equal non-default `Id`, `ReferenceEquals` first. A transient entity
   (default `Id`) equals only itself and hashes by reference. `Entity<TId>`'s `id` argument stays unguarded — default is
   the transient sentinel.
6. `AggregateRoot<TId>.Now` throws `InvalidOperationException` when no clock is attached. The ORM constructor sets none;
   attach only through `IHasClock.AttachClock`. Never add a sentinel/null clock — it stamps year 0001 on loaded aggregates.
7. Raise events only through `RaiseDomainEvent`. Each call increments `Version` — the event sequence number
   (`IHasVersion`), never a concurrency token; `ClearDomainEvents` does not reset it.
8. Audit fields have private setters and are written only by persistence; `RowVersion` has a protected setter (mapped to
   PostgreSQL `xmin`).
9. Tenancy uses `SharedKernel.Execution.Tenancy.TenantId`. `Tenanted…` constructors guard with
   `Guard.Throw.InvalidGuid`, so `default(TenantId)` throws `DomainException`. `TenantId` never changes. The application
   layer supplies it (typically `IRequestContext.TenantId`); the domain never resolves tenants.
10. Soft delete goes through `SoftDeletion.ShouldMarkDeleted`: a blank actor throws `DomainException` (even when already
    deleted); an already-deleted record is left untouched (no second event, original actor/time kept). Aggregates:
    `MarkAsDeleted(deletedBy)` uses `Now` and calls `virtual OnDelete` once; protected, idempotent `Restore()` +
    `virtual OnRestore()` undo it. Entities: `MarkAsDeleted(deletedBy, deletedOn)` with a UTC `deletedOn`.

**Rules, creation, exceptions**

11. `IBusinessRule.Code` is required — no default or fallback. `BusinessRuleViolationException` uses `rule.Code`.
12. Composites: `And` → first broken operand's code, broken messages joined with `"; "`; `Or` → left code; `Not`
    requires its own code and message.
13. `CheckRule`/`TryCreate` on `AggregateRoot`, `ValueObject` and (`CheckRule` only) `DomainService` delegate to
    `Internal.DomainInvariants`. Never reimplement them.
14. `TryCreate<T>` returns `ValidationResult<T>`: `ValidationException` → all its errors; any `DomainException` → its
    single error; anything else rethrows. Never catch broader.
15. `DomainNotFoundException` message is `"{TypeName} '{id}' was not found."`, both arguments null-checked, code
    `not_found.default`.

**Value objects and identifiers**

16. The `ValueObject` base constructor never validates. A subclass assigns members and calls `EnsureValid()` last (it
    throws `ValidationException` with every error). Omitting it compiles and skips validation — SK0037 reports it.
17. `SingleValueObject<TValue>` calls `EnsureValid()` itself; a null value → `DomainException`
    (`ErrorCodes.Validation.Required`) via `DomainInvariants.NotNull`.
18. Value-object equality: same runtime type, component-wise; a non-string `IEnumerable` component compares and hashes
    element by element.
19. `SingleValueObject<TValue>` and `StronglyTypedId<TValue>` have explicit conversion operators only; null →
    `ArgumentNullException`. Do not rename the explicit operator (06.Persistence finds `op_Explicit` by reflection).
20. Strongly-typed id shape: `public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);`. The JSON
    factory handles any `TValue` STJ can serialize, writes the bare value, reads JSON `null` as null, supports dictionary
    keys, and rethrows construction failures unwrapped from `TargetInvocationException`.

**Specifications**

21. `AddCriteria` accumulates with AND and resets the compiled `IsSatisfiedBy` cache. Never make it replace.
22. One primary sort: a second `ApplyOrderBy`/`ApplyOrderByDescending` throws; `ApplyThenBy`/`ApplyThenByDescending`
    without a primary sort throw.
23. Paging belongs at the repository call site (`ListPagedAsync`, `ListKeysetAsync`; ceiling
    `PageRequest.MaxPageSize`). `ApplyPaging`/`ApplyTake` remain for fixed windows ("ten most recent"); 06.Persistence's
    paged and bulk paths reject a specification that sets `Skip`/`Take`.
24. Composites (`And`/`Or`/`Not`) combine criteria, merge includes and string includes (deduplicated), OR the
    split-query/include-deleted/`IsDistinct` flags, and carry the single ordered operand's ordering. Two ordered operands,
    or any operand with `Skip`/`Take`, throw `InvalidOperationException` — never dropped silently. `Not` of a
    criteria-less spec matches nothing.
25. `IncludeDeleted` lifts only the soft-delete filter; the tenant filter and row-level security still apply.
26. Tracking is the repository's decision — do not add a tracking flag to specifications.

**Policies, events, Money**

27. `IPolicy<in T>` is contravariant; `Explain` is empty when compliant; `NotPolicy`/`.Not(explanation)` require an
    explanation. `policy.ToRule(subject, code)` is the only bridge to `CheckRule`.
28. `DomainEvent.Id` is `init`-settable, defaulting to `Guid.CreateVersion7()`, so it survives serialization — never
    make it get-only. Every concrete event declares `[DomainEventVersion(n)]` (SK0009).
29. `Money` lives in `SharedKernel.Domain.Monetary` (a namespace named `Money` collided with the type). It validates in
    constructor guards; `Validate()` returns empty. `ToString()` is invariant culture (`"59.97 USD"`); culture only via
    `IFormattable`.
30. Arithmetic uses banker's rounding; `Multiply`/`Divide` take a `RoundingPolicy` (`BankersRounding`, `AwayFromZero`,
    `ToZero`, `Ceiling`, `Floor` — never `Up`/`Down`).
31. Cross-currency `+ - < > <= >= Min Max Sum` throw `BusinessRuleViolationException` with `money.currency_mismatch`.
32. `Allocate` uses largest remainder; leftover minor units go to the later indexes; parts always sum to the original.
33. `Sum()` throws `InvalidOperationException` on an empty sequence; `Sum(currency)` returns zero.
34. `CurrencyCatalog` is frozen, compiled in, and carries `RegistryAsOf`; update only against ISO 4217 amendments.
    `IExchangeRateProvider` is a port; `ConvertAsync` builds the result through the internal `Money.FromTrusted`.

## Decisions

| Decision | Why |
| --- | --- |
| Value objects validate by an explicit `EnsureValid()` last in the constructor | Base-constructor validation runs before members are assigned; records' generated constructors bypass validation. SK0037 catches the omission |
| Abstract classes + `GetEqualityComponents()` for value objects, not records | Records' generated equality and `with` bypass validation |
| Loaded aggregates get their clock attached via `IHasClock`; `Now` throws when absent | Aggregate methods need no time parameter; a null clock would produce silent wrong data |
| `TryCreate` returns `ValidationResult<T>` | Keeps every error; `Result<T>` holds one |
| `IBusinessRule.Code` is required | Clients branch on codes and localization looks messages up by them |
| Explicit conversions only on ids and single value objects | Implicit operators let an `OrderId` flow into any `Guid` |
| `IHasVersion` is the event sequence, not a concurrency token | Concurrency is `xmin`/`EntityVersion` in 06.Persistence |
| Paging and tracking are repository concerns, not specification concerns | One place validates page input; the same spec serves tracked and untracked reads |
| `IAggregateFactory`, `IPolicy<T>` and the full audit/soft-delete/tenant base matrix are kept | Owner ruling: consumers get every combination without hand-wiring; `AggregateFactoriesMustCreateValidationResults` enforces the factory shape |
| AOT/trimming is not a constraint here | The JSON factory uses `MakeGenericType` + a compiled constructor delegate; `DomainEventVersionHelper` uses cached attribute lookup. Choose the best consumer API |
| Domain-event dispatch contract here, implementation in 05.Application | The domain declares `IDomainEventDispatcher`; `AddSharedKernelApplication` registers the native dispatcher |

## Logging

EventId block **3000–3999** (`LoggingEventIdRanges.Domain`) is reserved but unused: `SharedKernel.Domain` is
logging-free by rule (Model tier; `ILogger` is never injected into a domain type). Do not add `[LoggerMessage]` here.

## Cross-Domain Couplings

Changes here that silently break another domain:

| If you change… | Also check |
| --- | --- |
| The explicit operator on `StronglyTypedId<TValue>` | `06.Persistence` `KeysetQueryableExtensions` locates `op_Explicit` by reflection to unwrap ids in the keyset seek — a rename breaks at runtime |
| `IHasClock`, the ORM constructors or `Now` | `06.Persistence` `DomainClockMaterializationInterceptor` |
| `IHasTenant` or the `Tenanted…` bases, `[TenantShared]` | `06.Persistence` `TenantId` value converter (EF Core), `TenantIdTypeHandler` (Dapper), tenant filter, write guard, RLS binding and the "every entity is `IHasTenant` or `[TenantShared]`" model check |
| `IHasVersion` or event-sequence semantics | `06.Persistence` `DomainColumnConvention` (maps `Version` as a required, non-concurrency column) |
| Specification shape (`Skip`/`Take`, includes, flags, ordering) | `06.Persistence` `SpecificationEvaluator`, `PagingGuard`, `BulkSpecificationGuard`; keyset seek flips both comparisons when descending |
| `CurrencyCatalog` codes or minor units | `01.Core` `SharedKernel.Validation` has a second ISO 4217 table (`CurrencyCode`, `Internal/IsoData`) — keep them consistent |
| `Money`/`Currency` factories or the private persistence constructor | `06.Persistence` `MoneyMapping`/`CurrencyValueConverter`; `16.Testing` `MoneyFaker`; `00.Governance` SK0034 |
| `IBusinessRule`, `ValueObject` validation or `TryCreate` | `16.Testing` domain assertions; the Shop's `Shop.Ordering.Domain` (`samples/Shop`); `00.Governance` SK0037 and `AggregateFactoriesMustCreateValidationResults` |
| `IDomainEventDispatcher` | `05.Application` `SharedKernel.Application.Pipeline` `DomainEventDispatcher`; 06.Persistence dispatches before each save |
| Any public API | `PublicAPI.Unshipped.txt`, `SharedKernel.Domain.ConsumerVerify`, the package README |

Inbound references: every Application/Infrastructure package that touches aggregates (05, 06, 16, samples).
`SharedKernel.Contracts` and `SharedKernel.Domain` never reference each other.

## Testing

- `SharedKernel.Domain/SharedKernel.Domain.Tests` — Unit lane only, no fixtures. Every behaviour fix adds a case to
  `DomainHardeningTests` or `MoneyHardeningTests` that fails when the fix is reverted.
- Test rules use their own `Code` (`"test.rule"` where the code is irrelevant); value-object fixtures call `EnsureValid()`.
- Code snippets in the package README compile and their shown outputs come from running them.
- `SharedKernel.Domain.ConsumerVerify` consumes the packed package; update it with every public API change.
- Fakes: `SharedKernel.Testing` (`Domain/` helpers, `FakeClock`) — catalogue in `src/Testing/CLAUDE.md`.

## Known Limitations

- An omitted `EnsureValid()` compiles; only SK0037 catches it.
- Non-EF loaders must attach the clock themselves (`IHasClock.AttachClock`) or `Now` throws.
- `TryCreate` interop with `Result<T>` is the caller's job (`Errors[0]`).
- The strongly-typed id JSON factory and event-version lookup use reflection; not trim/AOT-clean.
