# Platform.SharedKernel — Root State Map

> **What this file is:** Macro-level implementation tracker for the entire SharedKernel mono-repo.  
> **What it is not:** A detailed task or phase log — those live in each domain's own `state-map.md`.  
> **Update policy:** Updated by `/sync-brain` whenever a domain changes phase, active work shifts, or a cross-domain dependency is resolved.

---

## Legend

| Symbol | Meaning |
|--------|---------|
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

**Standard build phases (in order):**

```
Design → Scaffold → Core → Tests → Docs → Published
```

> Phase definitions, per-package breakdowns, and task-level detail live exclusively in the domain's `state-map.md`. The root only tracks the current phase and its state.

---

## Active Work

> Domains currently `◐ In Progress`. This section is the first thing to update when work starts or finishes in any domain.

| Domain | Current Phase | Focus (one line) |
|--------|---------------|-----------------|
| [04.Contracts](04.Contracts/state-map.md) | Design | Define EventEnvelope<TEvent> transport wrapper with CorrelationId, CausationId, SourceService, and DomainEventVersion fields for cross-service domain event publishing |

<!--
Format when active:
| Domain | Current Phase | Focus (one line) |
|--------|---------------|-----------------|
| [01.Core](01.Core/state-map.md) | Scaffold | Creating project structure and .csproj files |
-->

---

## Blocked

> Domains at `⚑ Blocked`. State the blocker and which domain/external thing is blocking.

_No blockers._

<!--
Format when blocked:
| Domain | Blocked Phase | Blocker |
|--------|--------------|---------|
| [07.Messaging](07.Messaging/state-map.md) | Core | Waiting on 04.Contracts envelope type to be finalized |
-->

---

## Domain Summary Board

> One row per domain. **Summary: Done** and **Summary: Next** are single sentences max — detail goes in sub state-maps.

| # | Domain | Current Phase | State | Summary: Done | Summary: Next |
|---|--------|---------------|:-----:|---------------|---------------|
| 00 | [Governance](00.Governance/state-map.md) | Guard Purity Enforcement | `●` | SK0006 analyzer and DoesNotContainThrowIlPredicate IL rule implemented; all 11 guard purity tasks complete; 26 analyzer tests and 6 arch tests pass. | — |
| 01 | [Core](01.Core/state-map.md) | P-042 Error.BusinessRule Factory | `●` | ErrorType.BusinessRule enum member, Error.BusinessRule factory, and ErrorCodes.Domain.RuleViolated added to SharedKernel.Primitives; 56 Primitives + 65 Core tests passing. | — |
| 02 | [Caching](02.Caching/state-map.md) | Phase 31 (OTel Metrics) | `●` | Phase 31 complete — static Meter + 5 instruments in FusionCacheService; FusionCache events for hit/miss/eviction; factory Stopwatch; 209 FusionCache + 154 Redis tests passing. | — |
| 03 | [Domain](03.Domain/state-map.md) | Published | `●` | SharedKernel.Domain 1.2.0 and 1.3.0 packed and published to nupkgs/; manifests list only SharedKernel.Primitives and SharedKernel.Core; all 7 Published tasks complete. | — |
| 04 | [Contracts](04.Contracts/state-map.md) | Design | `◐` | — | Define EventEnvelope<TEvent> transport wrapper with CorrelationId, CausationId, SourceService, and DomainEventVersion fields for cross-service domain event publishing |
| 05 | [Application](05.Application/state-map.md) | — | `○` | — | — |
| 06 | [Persistence](06.Persistence/state-map.md) | — | `○` | — | — |
| 07 | [Messaging](07.Messaging/state-map.md) | — | `○` | — | — |
| 08 | [Storage](08.Storage/state-map.md) | — | `○` | — | — |
| 09 | [Search](09.Search/state-map.md) | — | `○` | — | — |
| 10 | [Intelligence](10.Intelligence/state-map.md) | — | `○` | — | — |
| 11 | [Communication](11.Communication/state-map.md) | — | `○` | — | — |
| 12 | [Security](12.Security/state-map.md) | — | `○` | — | — |
| 13 | [ServiceDefaults](13.ServiceDefaults/state-map.md) | — | `○` | — | — |
| 14 | [Presentation](14.Presentation/state-map.md) | — | `○` | — | — |
| 15 | [Integration](15.Integration/state-map.md) | — | `○` | — | — |
| 16 | [Testing](16.Testing/state-map.md) | — | `○` | — | — |
| 17 | [Workflows](17.Workflows/state-map.md) | — | `○` | — | — |

---

## Cross-Domain Dependencies

> Active coordination points between domains. Remove a row once the dependency is resolved.

_No active cross-domain dependencies._

<!--
Format when active:
| Waiting Domain | Needs From | What | Status |
|----------------|------------|------|--------|
| 05.Application | 03.Domain | IDomainEvent base type finalized | ◐ In progress in 03 |
-->

---

## Overall Progress

> Counts updated by `/sync-brain` whenever the board above changes.

| Phase | Domains |
|-------|---------|
| ● Guard Purity Enforcement | 1 |
| ● Phase 31 (OTel Metrics) | 1 |
| ● P-042 Error.BusinessRule Factory | 1 |
| ● Published | 1 |
| ● Docs | 0 |
| ● Tests | 0 |
| ● Core | 0 |
| ● Scaffold | 0 |
| ● Design | 0 |
| ◐ In Progress | 1 |
| ⚑ Blocked | 0 |
| ○ Not Started | 13 |

---

## Phase Backlog

> Phases queued by `arch-lead`. Each entry targets exactly one domain. Run `/dispatch-phase` to process pending phases in dependency order.
>
> **Status values:** `○` Pending — written by arch-lead, awaiting dispatch | `◐` Dispatched — domain planner has planned it | `●` Complete — domain implementation finished

<!--
Format when phases are present — arch-lead appends entries here using this exact structure:

---
### P-001 — {Capability Name}

**Status:** `○` Pending
**Work Order:** WO-001
**Domain:** 01.Core
**Depends on:** None

#### What is needed
{Clear description of the capability — what it does, what contracts it exposes, what behaviors it must guarantee. No file names or class names — those are for domain planners.}

#### Why this is needed
{Architectural rationale.}

#### Acceptance criteria
- [ ] {Criterion 1}
- [ ] {Criterion 2}
---

Rules:
- Phase IDs are globally unique: P-001, P-002, ... Increment from the highest existing ID.
- Work Order IDs group phases from the same user request: WO-001, WO-002, ... Increment from the highest existing WO.
- **Domain:** must match the canonical folder name: e.g., `01.Core`, `02.Caching`, `03.Domain`.
- **Depends on:** is either `None` or a comma-separated list of phase IDs (e.g., `P-001, P-003`).
- Horizontal rules `---` surround each entry as shown.
- Never remove or edit entries — only update **Status** from `○` to `◐` or `●`.
-->

---
### P-001 — Core Primitives Foundational Design

**Status:** `●` Complete
**Work Order:** WO-001
**Domain:** 01.Core
**Depends on:** None

#### What is needed

The `SharedKernel.Primitives` package — the zero-dependency foundation that every other domain in the SharedKernel references.

**Result pair (`Result<T>` and `Result`):**
Two cooperating types that implement railway-oriented programming. The generic `Result<T>` is a sealed class (not a struct — the zero-value problem with structs carrying generic payloads makes struct unsound at scale). It expresses either a success carrying a value of type `T`, or a failure carrying a single `Error`. The non-generic `Result` represents void operations and may be implemented as a readonly struct since it carries no typed value. Both expose `IsSuccess`, `IsFailure`, typed success/failure factories, and implicit conversion operators from `T` and from `Error`. The `.Value` accessor must throw `InvalidOperationException` when accessed on a failure; the `.Error` accessor must throw when accessed on a success. These two types must be designed together as a cohesive pair.

**Error type:**
A sealed record carrying a `Code` (string), `Message` (string), and `ErrorType` (enum). Factory methods cover: `Unexpected`, `Validation`, `NotFound`, `Conflict`, `Unauthorized`. A sentinel `Error.None` represents the absence of an error — null is never used for this purpose. An `ErrorCodes` static class ships well-known string constants for common codes (e.g., `OutOfRange`, `Required`, `NotFound`), organized by category as nested static classes. This is a string-constant approach rather than a nested enum — enums cannot be extended by consuming packages without forking the shared kernel, string constants can be supplemented locally.

**ValidationResult pair (`ValidationResult` and `ValidationResult<T>`):**
A separate result type for operations that can produce multiple errors simultaneously (compound validation). The non-generic form holds `IsValid` and `IReadOnlyList<Error>`. The generic form additionally holds a `Value`. These are sealed records. They are distinct from `Result<T>` — `Result<T>` is a single-error monad used for operation outcomes; `ValidationResult` is a multi-error aggregate used for input validation. Both live in `SharedKernel.Primitives` as they depend only on `Error`.

**`IClock` and `SystemClock`:**
`IClock` exposes `UtcNow` (`DateTimeOffset`) and `Today` (`DateOnly`). `SystemClock` is a sealed implementation wrapping `DateTimeOffset.UtcNow`. Direct use of `DateTime.UtcNow` or `DateTimeOffset.UtcNow` in any non-clock type is a hard violation — all time reads must go through `IClock`.

**`SmartEnum<TEnum, TValue>` abstract base:**
Supports typed enumerations with value and name lookup. Must not use reflection in any hot path — value lookup uses a static compile-time list built at type initialization. Exposes `FromValue` (throws on miss), `TryFromValue` (returns bool), `FromName` (throws on miss), `List` (full list), `Name`, and `Value`.

All types in this package have zero NuGet dependencies. Pure C# 13 targeting `net10.0`. All types must be AOT-safe: no reflection in hot paths, all types sealed or abstract where appropriate, no static mutable state.

#### Why this is needed

`SharedKernel.Primitives` is the layer zero of the entire platform. Every other SharedKernel package and every downstream microservice references it. Its design decisions propagate to hundreds of services. Getting the type shapes correct before implementation begins prevents breaking changes that would require coordinated upgrades across the entire ecosystem. The railway-oriented `Result<T>` pair eliminates exception-driven control flow for expected failures. The `ValidationResult` pair enables compound validation without collapsing to a single error. The `ErrorCodes` static class approach ensures error codes are consistent and extensible without enum versioning problems.

#### Acceptance criteria
- [ ] `Result<T>` is a sealed class; `.Value` throws `InvalidOperationException` on failure access; `.Error` throws on success access
- [ ] `Result` (non-generic) expresses void operations with success/failure factories and implicit operator from `Error`
- [ ] Implicit operators allow `T value` to produce `Result<T>.Success` and `Error error` to produce `Result<T>.Failure` without explicit wrapping
- [ ] `Error` sealed record has `Code`, `Message`, `Type` properties and five factory methods plus `Error.None` sentinel
- [ ] `ErrorCodes` static class ships well-known string constants organized in nested static category classes
- [ ] `ValidationResult` and `ValidationResult<T>` are sealed records supporting multi-error collection; distinct from `Result<T>`
- [ ] `IClock` interface exposes `UtcNow` and `Today`; `SystemClock` implements it with no mutable state
- [ ] `SmartEnum<TEnum, TValue>` abstract base uses static list for lookup — no reflection in `FromValue`, `TryFromValue`, `FromName`
- [ ] Zero NuGet package references in the `.csproj`
- [ ] All public types carry XML doc comments
- [ ] No `DateTime.UtcNow` direct usage anywhere in this package

---
### P-002 — Core Railway Extensions and BCL Utilities

**Status:** `●` Complete
**Work Order:** WO-001
**Domain:** 01.Core
**Depends on:** P-001

#### What is needed

The `SharedKernel.Core` package — the extension and exception layer that builds on `SharedKernel.Primitives`. References `SharedKernel.Primitives` and nothing else.

**Railway extension methods on `Result<T>`:**
Static extension methods that enable functional chaining over `Result<T>`:
- `Map<TOut>(Func<T, TOut>)` — transforms the value inside a success, passes through failure unchanged
- `MapError(Func<Error, Error>)` — transforms the error inside a failure, passes through success unchanged
- `Bind<TOut>(Func<T, Result<TOut>>)` — chains an operation that itself returns a `Result<TOut>`, short-circuits on failure
- `Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)` — folds the result to a single value
- `Tap(Action<T>)` — executes a side-effect on success without changing the result; returns the original

**Railway extension methods on non-generic `Result`:**
- `Match(Action onSuccess, Action<Error> onFailure)` — void-returning fold; necessary because the non-generic Result represents void operations

**Async railway overloads on `Task<Result<T>>`:**
All five of the above (`Map`, `MapError`, `Bind`, `Match`, `Tap`) must have async-aware overloads that accept `Task<Result<T>>` as `this` and return `Task<Result<...>>` or `Task<TOut>`. Overloads must also accept async lambdas (`Func<T, Task<TOut>>`) where appropriate. These are implemented without `async`/`await` on the outer extension where possible to avoid unnecessary state machine allocation.

**Base exception hierarchy:**
All exceptions derive from `SharedKernelException`. Every constructor requires an `Error` payload — string-only constructors are not permitted. Hierarchy: `SharedKernelException` (base) → `DomainException`, `ValidationException` (accepts `IReadOnlyList<Error>`), `NotFoundException`, `ConflictException`, `UnauthorizedException`. The `ValidationException` carries the full list of errors from a `ValidationResult`.

**BCL extension methods:**
- `string`: case converters (`ToSnakeCase`, `ToCamelCase`, `ToPascalCase`), `IsNullOrWhiteSpace()`
- `IEnumerable<T>`: `ToBatches(int size)` (yields `IEnumerable<IEnumerable<T>>`), `IsNullOrEmpty()`, `WhereNotNull()`
- `DateTimeOffset`: `ToUnixMilliseconds()`, `StartOfDay()`, `EndOfDay()`
- `Guid`: `IsEmpty()`

All extension methods are static and AOT-safe. No reflection. No dynamic dispatch.

#### Why this is needed

Railway-oriented error propagation requires chainable operators that do not exist on the `Result<T>` type itself — keeping the core type lean while the extension layer adds expressiveness. The async overloads are critical: microservice handlers are overwhelmingly async, and without `Task<Result<T>>` extensions every caller must `await` before chaining. The exception hierarchy bridges the `Result<T>` world to the exception world for framework-level error handling (e.g., middleware mapping `SharedKernelException` subtypes to HTTP status codes). The BCL extensions eliminate repeated utility code across hundreds of services.

#### Acceptance criteria
- [ ] All five `Result<T>` railway extensions implemented (`Map`, `MapError`, `Bind`, `Match`, `Tap`) with correct short-circuit semantics on failure
- [ ] Void `Match` extension implemented on non-generic `Result` accepting `Action onSuccess, Action<Error> onFailure`
- [ ] Async overloads exist for all five operators on `Task<Result<T>>`; at minimum sync-lambda overloads, ideally also async-lambda overloads
- [ ] `SharedKernelException` base and all five derived types implemented; every constructor requires an `Error` or `IReadOnlyList<Error>`
- [ ] All BCL extension methods implemented and covered by the surface documented above
- [ ] No `async`/`await` on the outer extension body where the only async work is `await`ing the input — use `.ContinueWith` or direct `Task` threading to avoid unnecessary state machines
- [ ] All public APIs carry XML doc comments
- [ ] Package references only `SharedKernel.Primitives` — no other NuGet or project references

---
### P-003 — Guard System: SharedKernel.Guards Package

**Status:** `●` Complete
**Work Order:** WO-002
**Domain:** 01.Core
**Depends on:** None

#### What is needed

A new `SharedKernel.Guards` package living in `01.Core/SharedKernel.Guards/`. It references `SharedKernel.Primitives` and `SharedKernel.Core` — the latter provides `DomainException` for the imperative throw path.

**Entry point and marker interface:**
A static `Guard` class exposes two static entry points. `Guard.Against` returns an `IGuardClause` marker — callers chain extension methods off it to obtain `Error?` (null means the guard passed; non-null means it failed). `Guard.Throw` is a companion static class whose members call the matching `Against` extension and, if a non-null `Error` is returned, immediately throw a `DomainException` carrying that error. The two entry points share the same underlying guard logic — `Throw.*` is a thin wrapper over `Against.*`.

**Guard clause extensions on `IGuardClause` — functional path (return `Error?`):**
All extensions are static methods, AOT-safe, zero-reflection. They return `Error?` — null signals the guard passed, a populated `Error` signals violation.

- Null/empty guards: `Null<T>` (reference type), `NullOrEmpty` (string), `NullOrWhiteSpace` (string)
- String length guards: `ShorterThan(string, int minLength)`, `LongerThan(string, int maxLength)`
- Numeric guards covering `int`, `decimal`, and `long`: `NegativeOrZero`, `Negative`, `NotPositive`
- Range guard: `OutOfRange<T>(T value, T min, T max)` constrained to `IComparable<T>`
- Default-value guard: `Default<T>(T value)` — uses `EqualityComparer<T>.Default`
- Guid guard: `InvalidGuid(Guid value)` — catches `Guid.Empty`
- Regex format guard: `InvalidFormat(string value, string pattern)` — uses a compiled/cached `Regex` with a bounded timeout to prevent ReDoS; never creates a new `Regex` instance per call
- Collection guards: `Empty<T>(IEnumerable<T>)`, `MaxCount<T>(IEnumerable<T>, int maxCount)`, `MinCount<T>(IEnumerable<T>, int minCount)` — each materializes the count once
- Email guard: `Email(string? value)` — uses the same bounded-timeout compiled regex strategy as `InvalidFormat`; no third-party NuGet
- Boolean predicate guards: `True(bool condition, Error error)`, `False(bool condition, Error error)` — caller supplies the `Error` directly, enabling arbitrary business-rule guards
- SmartEnum guard: `InvalidSmartEnum<TEnum, TValue>(TValue id)` constrained to `TEnum : SmartEnum<TEnum, TValue>` — calls `SmartEnum<TEnum, TValue>.TryFromValue`; this replaces the original `Enumeration<T>` guard which referenced a non-existent type

**Description strings:**
An internal `GuardDescriptions` static class owns all message templates as `const string` values. It is not part of the public API. Message templates for numeric and range guards use `{0}`, `{1}` placeholders interpolated via `string.Format` at the call site — no allocations beyond the error case.

**Imperative path (`Guard.Throw.*`):**
A companion nested static class `Guard.Throw` mirrors every `Against.*` extension as a void method. Each calls the matching `Against` extension, and if the returned `Error` is non-null, throws a `DomainException(error)`. This path is for callers that cannot tolerate continuing on guard failure (e.g., application-layer command handlers that want immediate short-circuit).

**Test project:**
`SharedKernel.Guards.Tests/` nested inside `SharedKernel.Guards/`. Covers both the functional path (assert returned `Error` on violation, assert null on pass) and the throw path (assert `DomainException` is thrown). Uses theory-driven parameterised tests for boundary conditions on numeric and string guards.

#### Why this is needed

Domain constructors, value objects, and application-layer command handlers all need a consistent precondition-enforcement vocabulary. Without a shared guard system, every team writes ad-hoc null checks and throws bare exceptions — inconsistent codes, inconsistent messages, no observable `Error` structure on the railway monad. The two-path design (functional `Against.*` returning `Error?` and imperative `Throw.*` throwing `DomainException`) satisfies both usage patterns: domain constructors that collect multiple guard results before deciding, and application handlers that want immediate short-circuit. Placing the package in `01.Core` as a distinct `SharedKernel.Guards` package — rather than folding it into `SharedKernel.Core` — keeps `SharedKernel.Core` focused and allows microservices that need only guards to take a smaller dependency.

#### Acceptance criteria
- [ ] `IGuardClause` marker interface is public; `DefaultGuardClause` implementation is private/sealed
- [ ] `Guard.Against` returns `IGuardClause`; all guard logic is invoked via extensions on that interface
- [ ] `Guard.Throw` nested static class mirrors every `Against` extension as a void method that throws `DomainException` on violation
- [ ] All extensions return `Error?` — null means guard passed, non-null means violation
- [ ] `Error` type used is `SharedKernel.Primitives.Errors.Error` — no parallel error type is introduced
- [ ] `InvalidSmartEnum<TEnum, TValue>` uses `SmartEnum<TEnum, TValue>.TryFromValue` — no reflection, no `Enumeration<T>` reference
- [ ] `InvalidFormat` and `Email` guards use a compiled/cached regex with a bounded timeout — no new `Regex` instance per call
- [ ] `GuardDescriptions` is internal — not part of the public API
- [ ] Collection guards (`Empty`, `MaxCount`, `MinCount`) enumerate the collection once only
- [ ] Package references only `SharedKernel.Primitives` and `SharedKernel.Core` — no other NuGet references
- [ ] All public types and extension method parameters carry XML doc comments
- [ ] `SharedKernel.Guards.Tests` covers both functional and throw paths; boundary conditions tested via theory-driven parameterised tests
- [ ] Package is AOT-safe: no reflection in any hot path, all types sealed where appropriate
---
### P-004 — Governance: Guard Purity Architecture Rule

**Status:** `●` Complete
**Work Order:** WO-002
**Domain:** 00.Governance
**Depends on:** P-003

#### What is needed

A new architecture enforcement rule in `00.Governance/SharedKernel.ArchitectureTests` that enforces the guard clause purity contract across all packages in the SharedKernel mono-repo and in any consuming service that references the governance package.

**Rule: Guard extension methods must not throw — they must return `Error?`**

The rule uses NetArchTest (or Roslyn analyzer, at the domain planner's discretion) to assert that methods on any type implementing `IGuardClause` do not contain `throw` statements. The `Guard.Throw.*` path is explicitly excluded from this rule — it is the designated throw surface. Any guard extension that throws directly bypasses the railway monad and violates the functional contract.

**Rule: `Guard.Against.*` and `Guard.Throw.*` are the only permitted guard entry points**

No consuming package or service may call `throw new DomainException(...)` directly inside a domain constructor or value object factory without going through either the guard system or an explicit `Result<T>` railway method. This rule is a lint/naming convention check — it does not enforce this at the IL level (which is infeasible), but documents the expected pattern and may be enforced via a Roslyn analyzer in a future phase.

The governance phase defines the rule specifications and the test fixtures. Actual Roslyn analyzer implementation (if chosen) is a sub-task for the `00.Governance` domain planner.

#### Why this is needed

The two-path guard design only delivers its architectural value if the functional path (`Against.*`) is provably pure — callers who depend on collecting `Error?` results cannot have the rug pulled out by an extension that throws instead of returning. An architecture test enforcing this contract prevents guard extensions from drifting into throw behavior as the package evolves. Without this enforcement, a future contributor adds a guard that throws, breaks domain constructors that assumed collection semantics, and the failure only surfaces at runtime.

#### Acceptance criteria
- [ ] An architecture test exists that loads all assemblies from `01.Core/SharedKernel.Guards` and asserts that no method on any type implementing `IGuardClause` has a `throw` expression (excluding `Guard.Throw.*` companion class)
- [ ] The rule is documented in the `00.Governance` domain brain with the rationale and exclusion list
- [ ] The test runs as part of the governance test suite and fails with a meaningful message identifying the offending method
---
### P-005 — Caching Abstractions Package: SharedKernel.Caching.Abstractions

**Status:** `●` Complete
**Work Order:** WO-003
**Domain:** 02.Caching
**Depends on:** None

#### What is needed

A new `SharedKernel.Caching.Abstractions` package that contains every contract currently embedded in `SharedKernel.Caching` and `SharedKernel.Caching.Redis`. This package must have zero infrastructure NuGet dependencies — it references only `01.Core` (for `Result<T>` and primitives if needed) and the BCL. No FusionCache, no StackExchange.Redis, no RedLock.

The package collects and owns the following contracts:

**Cache service contract (`ICacheService`):** The existing five-method interface (`GetAsync`, `SetAsync`, `GetOrSetAsync`, `RemoveAsync`, `RemoveByTagAsync`) migrated verbatim. No behavioral changes — only the namespace changes from `SharedKernel.Caching.Abstractions` (which already happened to be the namespace) to the new dedicated package.

**Cache policy (`CachePolicy`):** The existing sealed immutable record migrated verbatim. `CachePolicy` is a pure data type with no framework dependencies, so it belongs in abstractions alongside the interface that consumes it.

**Cache key provider contract (`ICacheKeyProvider`):** A new interface that standardizes how cache keys are constructed across all microservices. It must expose a method that accepts a key descriptor (a type, a set of segments, or a structured input) and returns a formatted, namespaced string key. The default key format convention is `{service}:{entity}:{id}` — this interface makes that contract explicit and injectable rather than an undocumented comment in `ICacheService`. Microservices that need custom key strategies implement this interface; services that follow the platform convention use the default `CacheKeyProvider` from `SharedKernel.Caching`.

**Distributed lock service contract (`IDistributedLockService`):** Migrated verbatim from `SharedKernel.Caching.Redis.Abstractions`. No behavioral changes.

**Redis channel service contract (`IRedisChannelService`):** A new interface for Redis Pub/Sub-based ephemeral message fanout. Exposes `PublishAsync(string channel, string message, CancellationToken ct)` and `SubscribeAsync(string channel, Func<string, ValueTask> handler, CancellationToken ct)` / `UnsubscribeAsync(string channel, CancellationToken ct)`. This interface is scoped explicitly to cache-adjacent signaling: cache invalidation signals, presence updates, lightweight real-time broadcast. It must never be used for durable, ordered, or guaranteed-delivery messaging — that is `07.Messaging`'s domain. The XML doc on the interface must state this scope constraint explicitly.

**Redis hash service contract (`IRedisHashService`):** A new interface for structured field-value storage within a single Redis key (Redis Hash data structure). Exposes `GetFieldAsync<T>`, `SetFieldAsync<T>`, `GetAllFieldsAsync<T>`, `DeleteFieldAsync`, `IncrementFieldAsync`. This is for structured projection/read-model patterns alongside cache, not a general Redis client. Field values are typed via STJ serialization contracts.

**Builder interface (`ICachingBuilder`):** Migrated from `SharedKernel.Caching.Extensions`. The builder interface must live in abstractions so that `SharedKernel.Caching.Redis` can extend it without taking a dependency on the FusionCache package — only on the abstractions package.

#### Why this is needed

The current structure forces any package that depends on `ICacheService` (notably `05.Application` for its `CachingBehavior` pipeline behavior) to transitively pull in FusionCache as a NuGet reference. That violates the `05.Application` hard rule: application layer may only reference abstractions, never concrete infrastructure packages. A dedicated abstractions package with zero infra dependencies resolves this entirely. It also enables the platform pattern: microservices reference only `SharedKernel.Caching.Abstractions`, register either `SharedKernel.Caching` (FusionCache) or a future alternative at the composition root, and are never coupled to the provider implementation.

#### Acceptance criteria
- [ ] New project `SharedKernel.Caching.Abstractions` exists in `02.Caching/SharedKernel.Caching.Abstractions/`
- [ ] Project targets `net10.0` with zero NuGet infrastructure references (no FusionCache, no StackExchange.Redis, no RedLock.net)
- [ ] `ICacheService` contract is present with all five methods
- [ ] `CachePolicy` sealed record is present with all existing factory methods and properties
- [ ] `ICacheKeyProvider` interface is present with a clear contract for producing namespaced, formatted cache keys
- [ ] `IDistributedLockService` interface is present (migrated from `SharedKernel.Caching.Redis`)
- [ ] `IRedisChannelService` interface is present with `PublishAsync`, `SubscribeAsync`, `UnsubscribeAsync`; XML doc explicitly states ephemeral/non-durable scope
- [ ] `IRedisHashService` interface is present with `GetFieldAsync<T>`, `SetFieldAsync<T>`, `GetAllFieldsAsync<T>`, `DeleteFieldAsync`, `IncrementFieldAsync`
- [ ] `ICachingBuilder` interface is present
- [ ] All public types carry XML doc comments
- [ ] Project is registered in `Platform.SharedKernel.slnx` under solution folder `02.Caching`
- [ ] Package is AOT-safe: no reflection, all types sealed or abstract as appropriate
---
### P-006 — Refactor SharedKernel.Caching to Depend on Abstractions + Add CacheKeyProvider

**Status:** `●` Complete
**Work Order:** WO-003
**Domain:** 02.Caching
**Depends on:** P-005

#### What is needed

Refactor the existing `SharedKernel.Caching` package to depend on `SharedKernel.Caching.Abstractions` instead of defining its own interfaces. This must be a non-breaking refactor: all existing DI extension signatures (`AddSharedKernelCaching`, `ICachingBuilder` extension methods) remain identical. The only observable change is that `ICacheService`, `CachePolicy`, `ICachingBuilder`, and `ICacheKeyProvider` are now sourced from the abstractions package.

**Structural changes:**
- Remove the inline `ICacheService` definition (now comes from abstractions)
- Remove the inline `CachePolicy` definition (now comes from abstractions)
- Remove the inline `ICachingBuilder` definition (now comes from abstractions)
- Add project reference to `SharedKernel.Caching.Abstractions`
- The `FusionCacheService` implementation remains here — it is the concrete FusionCache-backed implementation
- The `CacheJsonSerializerContext` STJ base remains here

**New capability — `CacheKeyProvider`:** A default implementation of `ICacheKeyProvider` that produces keys following the platform convention `{service}:{entity}:{id}` using a configurable service name prefix. It must be registered by `AddSharedKernelCaching` as the default `ICacheKeyProvider` singleton, overridable by the consuming service. The service name prefix is sourced from `CachingOptions` (a new required field). If not configured, it defaults to `"app"` to prevent misconfigured keys being silently produced.

The `CachingOptions` class gains a `ServiceName` property (string, defaults to `"app"`, validated non-null/non-whitespace).

**Test project update:** The `.Tests` project must add a test for `CacheKeyProvider` key format validation and override behavior.

#### Why this is needed

`SharedKernel.Caching` must become the FusionCache provider implementation package, not the source of interface definitions. Microservices that reference `SharedKernel.Caching.Abstractions` should never need to know that FusionCache is the underlying implementation. The `CacheKeyProvider` closes the gap between the documented convention (comment in the XML doc) and an actual enforced, injectable contract — preventing teams from inventing their own key formats.

#### Acceptance criteria
- [ ] `SharedKernel.Caching.csproj` has a project reference to `SharedKernel.Caching.Abstractions`
- [ ] `ICacheService`, `CachePolicy`, `ICachingBuilder` are removed from this package (sourced from abstractions)
- [ ] `FusionCacheService` still implements `ICacheService` from abstractions — zero behavioral changes
- [ ] `CacheKeyProvider` class implements `ICacheKeyProvider` using the `{service}:{entity}:{id}` format
- [ ] `CachingOptions` has a `ServiceName` property with validation
- [ ] `AddSharedKernelCaching` registers `CacheKeyProvider` as the default `ICacheKeyProvider` singleton
- [ ] All existing tests continue to pass — no regressions
- [ ] New tests cover `CacheKeyProvider` key format and `ServiceName` configuration
- [ ] All public types carry XML doc comments
---
### P-007 — Refactor SharedKernel.Caching.Redis + Add Channel and Hash Services

**Status:** `●` Complete
**Work Order:** WO-003
**Domain:** 02.Caching
**Depends on:** P-005

#### What is needed

Refactor the existing `SharedKernel.Caching.Redis` package to depend on `SharedKernel.Caching.Abstractions` instead of defining its own `IDistributedLockService`. Simultaneously add two new Redis-backed service implementations: `RedisChannelService` (implements `IRedisChannelService`) and `RedisHashService` (implements `IRedisHashService`).

**Structural changes:**
- Remove the inline `IDistributedLockService` definition (now comes from abstractions)
- Add project reference to `SharedKernel.Caching.Abstractions`; remove direct project reference to `SharedKernel.Caching` unless still needed for `ICachingBuilder` extension target
- All existing `RedLockDistributedLockService` and Redis L2 wiring remains unchanged

**New capability — `RedisChannelService`:**
Implements `IRedisChannelService` using StackExchange.Redis `ISubscriber`. Must use the `RedisChannel.Literal` pattern for channel names to avoid pattern-matching overhead. `SubscribeAsync` accepts a `Func<string, ValueTask>` handler and maintains an internal subscription registry keyed by channel name. `UnsubscribeAsync` removes the handler and unsubscribes from Redis. All handler exceptions must be caught and logged — they must never propagate to the Redis subscriber thread. The service is registered as a singleton by `AddRedisChannelService` DI extension.

**New capability — `RedisHashService`:**
Implements `IRedisHashService` using StackExchange.Redis `IDatabase.HashGetAsync`, `HashSetAsync`, `HashGetAllAsync`, `HashDeleteAsync`, `HashIncrementAsync`. All field values are serialized/deserialized using STJ source-generated contexts — the caller provides the `JsonTypeInfo<T>` as a parameter to typed methods. This keeps the service AOT-safe. A non-typed `string` overload is also provided for plain string field values. The service is registered as a singleton by `AddRedisHashService` DI extension. Both the channel service and hash service share a single `IConnectionMultiplexer` singleton — do not create separate connections.

**DI extension additions:**
- `AddRedisChannelService(this IServiceCollection services, string connectionString)` — registers `RedisChannelService`
- `AddRedisHashService(this IServiceCollection services)` — registers `RedisHashService` (reuses the existing `IConnectionMultiplexer` if already registered; throws with a clear message if `AddRedisDistributedLocking` has not been called first, since that registers the multiplexer)

**Test additions:**
- Integration test: `RedisChannelService` publish/subscribe round-trip via Testcontainers Redis
- Integration test: `RedisChannelService` unsubscribe stops message delivery
- Integration test: `RedisHashService` set/get field, get-all fields, delete field, increment field via Testcontainers Redis
- Integration test: multiple services sharing same multiplexer (connection is not duplicated)

#### Why this is needed

Redis Pub/Sub and Redis Hashes are natural Redis capabilities that microservices need alongside caching and locking. Placing them in `SharedKernel.Caching.Redis` keeps all Redis concerns in one provider package, minimizing the number of Redis connection pools in a service. The AOT-safe STJ approach for hash field serialization is required to stay consistent with the rest of the platform's AOT-first philosophy. The shared `IConnectionMultiplexer` pattern avoids connection proliferation — a common operational problem in Redis-heavy services.

#### Acceptance criteria
- [ ] `SharedKernel.Caching.Redis.csproj` references `SharedKernel.Caching.Abstractions` (not `SharedKernel.Caching` directly, unless `ICachingBuilder` extension requires it)
- [ ] `IDistributedLockService` removed from this package (sourced from abstractions)
- [ ] `RedisChannelService` implements `IRedisChannelService`; channel exceptions are caught and logged, never propagated
- [ ] `RedisHashService` implements `IRedisHashService` with typed STJ serialization for field values
- [ ] Both services share the singleton `IConnectionMultiplexer` — no new connections created
- [ ] `AddRedisChannelService` DI extension registers the channel service
- [ ] `AddRedisHashService` DI extension registers the hash service; throws a clear `InvalidOperationException` if multiplexer not registered
- [ ] All existing tests pass — no regressions on RedLock and L2 backplane behavior
- [ ] New integration tests cover Pub/Sub round-trip, unsubscribe, and all hash operations via Testcontainers
- [ ] All public types carry XML doc comments
- [ ] Package remains AOT-safe
---
### P-009 — Governance: Caching Abstractions Enforcement Rules

**Status:** `◐` Dispatched
**Work Order:** WO-003
**Domain:** 00.Governance
**Depends on:** P-005, P-006, P-007

#### What is needed

Two new architecture enforcement rules added to `00.Governance/SharedKernel.ArchitectureTests`:

**Rule 1 — No production package may reference `SharedKernel.Caching` or `SharedKernel.Caching.Redis` directly:**
Any production assembly (excluding `SharedKernel.Caching` and `SharedKernel.Caching.Redis` themselves, and excluding `13.ServiceDefaults` which is the composition root) that references the concrete caching packages rather than `SharedKernel.Caching.Abstractions` fails this rule. The test uses NetArchTest assembly scanning to detect direct references. The intent: `05.Application` and all domain/contract packages must only reference the abstractions. The composition root (`13.ServiceDefaults`) is the only place that may reference concrete providers.

**Rule 2 — `IRedisChannelService` must not be used as a substitute for `IMessageBus`:**
A documentation/lint rule (enforced by a naming convention analyzer or architecture comment) asserting that `IRedisChannelService` subscriptions must only be used for cache invalidation signals and ephemeral events, not for commands, domain events, or integration events. The implementation can be a Roslyn analyzer that warns when `IRedisChannelService` is injected into a class in a namespace containing `Command`, `Event`, `DomainEvent`, or `IntegrationEvent`. This enforces the pub/sub scope constraint at the IDE level.

**Rule documentation:** Both rules must be documented in the `00.Governance` domain brain with the full rationale and exemption list.

#### Why this is needed

Without enforcement, teams will inevitably reference `SharedKernel.Caching` (the concrete package) from `05.Application` handlers or domain logic — because it works, but creates invisible coupling to FusionCache. The architecture test catches this drift before it reaches production. The Pub/Sub scope rule prevents a gradual erosion of the `07.Messaging` / `02.Caching` boundary — Redis Pub/Sub is extremely easy to reach for, and without a lint rule, teams will use it for integration events that must actually be durable.

#### Acceptance criteria
- [ ] NetArchTest rule exists asserting no assembly other than the concrete caching packages and `13.ServiceDefaults` references `SharedKernel.Caching` or `SharedKernel.Caching.Redis`
- [ ] Rule test fails with a descriptive message identifying the offending assembly when violated
- [ ] Roslyn analyzer (or equivalent) warns when `IRedisChannelService` is injected in a class whose name or namespace suggests durable messaging (command/event/integration-event context)
- [ ] Both rules documented in `00.Governance` domain brain with rationale and exemption list
- [ ] Governance test suite passes with both new rules included
---
### P-010 — ServiceDefaults: Redis Health Check and Cache Readiness Probe

**Status:** `○` Pending
**Work Order:** WO-003
**Domain:** 13.ServiceDefaults
**Depends on:** P-005

#### What is needed

Standard health check and readiness probe registrations for the caching infrastructure, added to `13.ServiceDefaults/SharedKernel.ServiceDefaults`.

**Redis connectivity health check:** Uses `AspNetCore.HealthChecks.Redis` (or `Microsoft.Extensions.Diagnostics.HealthChecks` with a custom Redis probe) to verify the StackExchange.Redis connection is alive. The check is tagged `"redis"` and `"cache"` for selective probing. It must be opt-in — a service that only uses L1 cache must not fail health checks because no Redis is configured. Registration via `AddRedisHealthCheck(connectionString)` extension on `IHealthChecksBuilder`.

**Cache service readiness probe:** A lightweight health check that calls `ICacheService.GetAsync<string>` with a synthetic probe key and a short timeout. If the operation throws (Redis down and fail-safe exhausted), the check reports `Degraded` — not `Unhealthy` — because FusionCache's fail-safe may still serve stale data. If the L1 cache responds correctly (fail-safe), it reports `Healthy`. Tagged `"cache"`.

**OpenTelemetry caching metrics:** Instrument `ICacheService` hits/misses/errors as OTEL meters. The metrics are emitted via `System.Diagnostics.Metrics` with a meter named `"SharedKernel.Caching"`. Metrics: `cache.hits` (counter), `cache.misses` (counter), `cache.errors` (counter), `cache.operation.duration` (histogram). These must be instrumentable without modifying `FusionCacheService` directly — prefer a decorator pattern registered at the `13.ServiceDefaults` composition level, or leverage FusionCache's built-in events.

#### Why this is needed

K8s readiness and liveness probes depend on accurate health check signals. A service that uses Redis for L2 and loses connectivity should report `Degraded` (serving stale data via fail-safe) rather than `Unhealthy` (crash the pod), because FusionCache's fail-safe is precisely designed to absorb Redis outages. Getting this signal calibration correct prevents unnecessary K8s pod restarts during Redis rolling upgrades or network blips.

#### Acceptance criteria
- [ ] `AddRedisHealthCheck` extension registers a Redis connectivity check tagged `"redis"` and `"cache"`
- [ ] Redis health check is opt-in — not automatically applied by default service defaults registration
- [ ] `AddCacheReadinessCheck` extension registers a functional cache probe that returns `Degraded` (not `Unhealthy`) when Redis is unavailable but L1 fail-safe is active
- [ ] OTEL meters registered for `cache.hits`, `cache.misses`, `cache.errors`, `cache.operation.duration`
- [ ] All registrations reference `SharedKernel.Caching.Abstractions` only — not concrete caching packages
- [ ] XML doc comments on all public extension methods
---
### P-011 — Testing: Caching Test Doubles

**Status:** `○` Pending
**Work Order:** WO-003
**Domain:** 16.Testing
**Depends on:** P-005

#### What is needed

Three test double implementations added to `16.Testing/SharedKernel.Testing` that allow downstream microservice test projects to test caching behavior without spinning up Redis containers:

**`FakeCacheService`:** An in-memory `ICacheService` implementation backed by `Dictionary<string, object>` (or `ConcurrentDictionary` for thread safety in parallel tests). Must implement all five methods faithfully: `GetOrSetAsync` invokes the factory on miss and stores the result (no stampede simulation needed in unit tests), `RemoveByTagAsync` removes all keys associated with the given tag (requires tracking key-to-tag mapping internally), `RemoveAsync` removes a single key. Expiry is not simulated — all entries live indefinitely in the fake. The fake must expose a `Keys` property and a `Reset()` method for test assertions and cleanup.

**`FakeDistributedLockService`:** An in-memory `IDistributedLockService` implementation. `AcquireAsync` tracks acquired lock names in a `HashSet<string>`. If a lock is already held and `wait` is `TimeSpan.Zero`, returns null immediately. If `wait` is positive, waits up to the specified duration polling every `retry` interval. On dispose, releases the lock. The fake must expose a `HeldLocks` property and a `Reset()` method.

**`FakeRedisChannelService`:** An in-memory `IRedisChannelService` implementation. `PublishAsync` delivers the message synchronously to all registered handlers for the channel. `SubscribeAsync` registers a handler. `UnsubscribeAsync` removes it. Exceptions in handlers are rethrown (unlike the real implementation which swallows them) — this makes test failures visible. Exposes a `PublishedMessages` dictionary (channel → list of messages) for assertion.

All three fakes live in `16.Testing/SharedKernel.Testing` and are registered via DI extension `AddFakeCachingServices()` on `IServiceCollection`.

#### Why this is needed

Downstream microservice unit tests that test caching-aware code paths need a predictable, controllable `ICacheService` without spinning up Redis. Without these fakes, teams either write their own incomplete fakes, use NSubstitute mocks (which test nothing about behavior), or pay the overhead of Testcontainers for every unit test. The fakes in `SharedKernel.Testing` are the platform-standard test doubles — all teams use the same semantics, tests are consistent, and behavioral contracts are validated uniformly.

#### Acceptance criteria
- [ ] `FakeCacheService` implements `ICacheService`; `GetOrSetAsync` invokes factory on miss; `RemoveByTagAsync` correctly removes all entries with the tag; `Reset()` clears all state
- [ ] `FakeDistributedLockService` implements `IDistributedLockService`; lock contention with `wait: TimeSpan.Zero` returns null immediately; `Reset()` clears held locks
- [ ] `FakeRedisChannelService` implements `IRedisChannelService`; exceptions in handlers propagate (not swallowed); `PublishedMessages` dictionary accurately records all published messages per channel
- [ ] `AddFakeCachingServices()` registers all three fakes as singletons
- [ ] `16.Testing` package references `SharedKernel.Caching.Abstractions` — not the concrete packages
- [ ] All three fakes have their own unit tests within `SharedKernel.Testing.Tests`
- [ ] All public types carry XML doc comments

---

### P-012 — Caching: Cross-Service Cache Invalidation Broadcast

**Status:** `●` Complete
**Work Order:** WO-003
**Domain:** 02.Caching
**Depends on:** P-005, P-007

#### What is needed

A new `ICacheInvalidationBus` interface added to `SharedKernel.Caching.Abstractions`, with a Redis-backed implementation (`RedisCacheInvalidationBus`) in `SharedKernel.Caching.Redis`, and a receiver background service (`CacheInvalidationReceiver`) that auto-subscribes on startup to apply remote invalidations to the local FusionCache instance.

This capability closes the cross-service L1 invalidation gap: FusionCache's built-in Redis backplane propagates L1 invalidations across instances of the **same service**, but has no mechanism to tell a **different service** (Service B) to drop its L1 entries when Service A invalidates a shared concept.

---

**`ICacheInvalidationBus` interface (in `SharedKernel.Caching.Abstractions`):**

A publishing contract with four methods:

- `PublishKeyInvalidationAsync(string[] keys, CancellationToken ct)` — requests remote services to evict specific cache keys
- `PublishTagInvalidationAsync(string[] tags, CancellationToken ct)` — requests remote services to evict all entries carrying the given tags
- `PublishBroadcastInvalidationAsync(CancellationToken ct)` — requests all subscribing services to flush their entire L1 cache (use with care; documented as a break-glass operation)
- `PublishInvalidationAsync(CacheInvalidationMessage message, CancellationToken ct)` — low-level method that sends a pre-constructed message, used internally by the three convenience methods above

The interface carries XML documentation stating: "This contract is for cross-service cache invalidation only. It is not a message bus substitute. It provides no delivery guarantees — invalidation messages are ephemeral. If a service is offline when a message is published, it will not receive the invalidation and must rely on TTL expiry. Use `07.Messaging` for durable, ordered, or guaranteed delivery."

**`CacheInvalidationMessage` record (in `SharedKernel.Caching.Abstractions`):**

A sealed record that is the wire payload. Fields:

- `SourceService` (string) — the `ServiceName` from the sender's `CachingOptions`
- `InvalidationType` (enum: `Key`, `Tag`, `All`) — determines which fields are populated
- `Keys` (string[]?) — populated when `InvalidationType` is `Key`
- `Tags` (string[]?) — populated when `InvalidationType` is `Tag`
- `CorrelationId` (string) — a trace correlation token, populated from ambient activity if available, otherwise a new `Guid.NewGuid().ToString("N")`. This allows OTel spans to link publisher and receiver.
- `TimestampUtc` (DateTimeOffset) — sender's UTC timestamp at publish time; receivers may use this for clock-skew diagnostics

Serialization uses STJ with a source-generated context — no reflection. The record must be AOT-safe.

**Channel naming convention:**

All invalidation messages are published on the channel `sharedkernel:cache:invalidation:{target-service-name}` for targeted invalidation, or `sharedkernel:cache:invalidation:broadcast` for broadcast. The `{target-service-name}` is always lowercase, with spaces replaced by hyphens. Receiving services subscribe to:

1. Their own named channel (`sharedkernel:cache:invalidation:{own-service-name}`) — for targeted invalidations from a specific sender
2. The broadcast channel (`sharedkernel:cache:invalidation:broadcast`) — for platform-wide flushes

**`RedisCacheInvalidationBus` (in `SharedKernel.Caching.Redis`):**

Implements `ICacheInvalidationBus` using `IRedisChannelService.PublishAsync`. It serializes the `CacheInvalidationMessage` to JSON using STJ and publishes on the appropriate channel. No knowledge of the receiving side — pure sender. Registered as a singleton by `AddRedisCacheInvalidationBus(this ICachingBuilder builder)` DI extension.

**`CacheInvalidationReceiver` background service (in `SharedKernel.Caching.Redis`):**

A `BackgroundService` that subscribes to the local service's named channel and the broadcast channel on startup using `IRedisChannelService.SubscribeAsync`. On message receipt it:

1. Deserializes the `CacheInvalidationMessage` using the STJ source-generated context
2. Dispatches to `ICacheService` based on `InvalidationType`:
   - `Key` — calls `ICacheService.RemoveAsync(key)` for each key in the payload
   - `Tag` — calls `ICacheService.RemoveByTagAsync(tag)` for each tag
   - `All` — calls `ICacheService.RemoveAsync` on all known keys, or if that is not feasible, logs a structured warning and relies on TTL; the implementation must document this limitation
3. Creates an OTel activity span linked to the sender's `CorrelationId` for distributed tracing continuity
4. On any deserialization or removal error: logs as structured error (never throws) and continues processing subsequent messages

Registration: `AddCacheInvalidationReceiver(this ICachingBuilder builder)` — additive extension, opt-in. Services that only publish invalidations and never receive them do not need to register the receiver.

**DI extension shape:**

```csharp
services.AddSharedKernelCaching(options => { })
        .AddRedisL2(connectionString)
        .AddRedisCacheInvalidationBus()     // adds ICacheInvalidationBus publisher
        .AddCacheInvalidationReceiver();    // adds background service subscriber
```

**Integration tests (in `SharedKernel.Caching.Redis.Tests`):**

- Publish a `Key` invalidation from a simulated Service A; verify Service B's `ICacheService` has the key removed
- Publish a `Tag` invalidation; verify all tagged entries removed from receiver's cache
- Publish a broadcast; verify receiver's `RemoveAsync`/`RemoveByTagAsync` called appropriately
- Service offline scenario: publish while receiver is not subscribed; verify no error on sender side and receiver does not crash on reconnect
- OTel activity span created on receiver with correct `CorrelationId` from sender

#### Why this is needed

FusionCache's built-in Redis backplane is designed to propagate invalidations across instances of the **same** service — it solves the multi-replica L1 sync problem within a single service identity. It does not solve the cross-service problem: when Service A updates a shared concept (e.g., a product catalog item) and Service B has cached that concept in its own L1, Service B will serve stale data until its TTL expires. Without a standard abstraction, every team reinvents this signal differently — different channel names, different payload formats, inconsistent error handling. The result is operational confusion and silent staleness bugs. `ICacheInvalidationBus` makes this pattern first-class, with a single channel naming convention, a self-describing payload, and OTel tracing continuity between sender and receiver. Using `IRedisChannelService` as the transport (P-007) keeps the implementation thin and avoids a second Redis connection. The explicit "no guarantee" contract on the interface prevents misuse as a substitute for `07.Messaging`.

#### Acceptance criteria

- [ ] `ICacheInvalidationBus` interface exists in `SharedKernel.Caching.Abstractions` with four methods as specified; XML doc explicitly states the no-delivery-guarantee contract
- [ ] `CacheInvalidationMessage` sealed record exists in `SharedKernel.Caching.Abstractions` with all six fields; STJ source-generated serialization; AOT-safe
- [ ] Channel naming convention is enforced by `RedisCacheInvalidationBus` — format `sharedkernel:cache:invalidation:{service-name}` for targeted, `sharedkernel:cache:invalidation:broadcast` for broadcast
- [ ] `RedisCacheInvalidationBus` implements `ICacheInvalidationBus` using `IRedisChannelService`; no direct Redis client dependency
- [ ] `CacheInvalidationReceiver` is a `BackgroundService`; subscribes to own-service channel and broadcast channel on startup; unsubscribes on shutdown
- [ ] Receiver dispatches correctly for `Key`, `Tag`, and `All` invalidation types by calling `ICacheService` methods
- [ ] Receiver creates an OTel activity span with `CorrelationId` from the incoming message
- [ ] Receiver never propagates exceptions — all errors are logged and processing continues
- [ ] `AddRedisCacheInvalidationBus()` registers the bus; `AddCacheInvalidationReceiver()` registers the background service — both opt-in
- [ ] `CacheInvalidationReceiver` is correctly registered as a hosted service (not a scoped/transient service)
- [ ] `ICacheInvalidationBus` added to `SharedKernel.Caching.Abstractions` — no infrastructure package dependency introduced into the abstractions package
- [ ] Integration tests cover: Key invalidation, Tag invalidation, broadcast, sender-offline no-crash, OTel correlation
- [ ] All public types carry XML doc comments
- [ ] Package remains AOT-safe

---

### P-013 — Testing: FakeCacheInvalidationBus Test Double

**Status:** `○` Pending
**Work Order:** WO-003
**Domain:** 16.Testing
**Depends on:** P-012

#### What is needed

A single test double added to `16.Testing/SharedKernel.Testing` so downstream microservice test projects can assert on cross-service cache invalidation behavior without a Redis container.

**`FakeCacheInvalidationBus`:** An in-memory `ICacheInvalidationBus` implementation. Each publish method records the `CacheInvalidationMessage` it constructs in an internal list (`PublishedInvalidations`). On publish, it also synchronously invokes any registered handlers — enabling tests to wire up a `FakeCacheService` as the receiver and assert that the correct keys or tags were removed in the same test without any async plumbing.

Public surface:

- `PublishedInvalidations` — `IReadOnlyList<CacheInvalidationMessage>` — all messages published since last reset
- `Reset()` — clears `PublishedInvalidations` and removes all registered handlers
- `OnInvalidation(Func<CacheInvalidationMessage, ValueTask> handler)` — registers a handler that is invoked synchronously on each publish; allows tests to chain `FakeCacheService.RemoveAsync` as the downstream effect

The fake must NOT depend on `IRedisChannelService` — it is a pure in-memory implementation. All four `ICacheInvalidationBus` methods must be implemented. `PublishBroadcastInvalidationAsync` constructs a message with `InvalidationType.All` and an empty `Keys`/`Tags`.

The `AddFakeCachingServices()` DI extension in `SharedKernel.Testing` must be updated to also register `FakeCacheInvalidationBus` as the `ICacheInvalidationBus` singleton, keeping all fake caching registrations in a single call.

**Test coverage:**

- `FakeCacheInvalidationBus` records `Key` invalidations with correct key list
- `FakeCacheInvalidationBus` records `Tag` invalidations with correct tag list
- `FakeCacheInvalidationBus` records broadcast as `InvalidationType.All`
- `OnInvalidation` handler is invoked synchronously on each publish
- `Reset()` clears all state; subsequent assertions on `PublishedInvalidations` start fresh
- `AddFakeCachingServices()` registration: `ICacheInvalidationBus` resolves as `FakeCacheInvalidationBus`

#### Why this is needed

Any service that uses `ICacheInvalidationBus` to signal cross-service invalidation needs a way to assert "did my code publish the right invalidation?" without a Redis container. Without `FakeCacheInvalidationBus`, teams either skip these assertions entirely (silent regression risk) or spin up a Testcontainers Redis for what is fundamentally a unit-testable concern. The `OnInvalidation` handler registration pattern allows a single test to wire `FakeCacheInvalidationBus` → `FakeCacheService`, proving the full invalidation chain without infrastructure. This is consistent with the fake-first testing philosophy established by the existing three fakes in P-011.

#### Acceptance criteria

- [ ] `FakeCacheInvalidationBus` implements `ICacheInvalidationBus`; all four methods build a `CacheInvalidationMessage` and record it in `PublishedInvalidations`
- [ ] `PublishedInvalidations` is `IReadOnlyList<CacheInvalidationMessage>` — thread-safe internal storage
- [ ] `OnInvalidation(Func<CacheInvalidationMessage, ValueTask> handler)` accepts a handler invoked synchronously on each publish
- [ ] `Reset()` clears `PublishedInvalidations` and removes all registered handlers
- [ ] `FakeCacheInvalidationBus` has no dependency on `IRedisChannelService` or any Redis package
- [ ] `AddFakeCachingServices()` is updated to also register `FakeCacheInvalidationBus` as `ICacheInvalidationBus` singleton
- [ ] `16.Testing` package reference to `SharedKernel.Caching.Abstractions` covers `CacheInvalidationMessage` — no new infrastructure package references added
- [ ] All six acceptance test cases listed above are covered
- [ ] All public types carry XML doc comments

---

### P-014 — Caching: Rename SharedKernel.Caching to SharedKernel.Caching.FusionCache + Add CachePolicy.NeverExpire

**Status:** `◐` Dispatched
**Work Order:** WO-004
**Domain:** 02.Caching
**Depends on:** None

#### P-014: What is needed

Two coupled changes delivered as a single phase:

**Part A — Package rename.** The existing `SharedKernel.Caching` package must be renamed to `SharedKernel.Caching.FusionCache`. This is a provider-naming correction — the package is unambiguously a FusionCache implementation, and its current generic name misleads developers into treating it as the abstraction layer (which is `SharedKernel.Caching.Abstractions`). The rename covers: folder name, `.csproj` file name, `<PackageId>` property, `<AssemblyName>` if explicitly set, all `ProjectReference` paths in `SharedKernel.Caching.Redis.csproj` and the consumer-verify project, the solution file `Platform.SharedKernel.slnx` (solution folder entry), and the `<Description>` metadata. The `InternalsVisibleTo` attribute must be updated to reference `SharedKernel.Caching.FusionCache.Tests`. The nested test project folder and `.csproj` must be renamed to `SharedKernel.Caching.FusionCache.Tests` accordingly. All namespaces inside the package must update from `SharedKernel.Caching.*` to `SharedKernel.Caching.FusionCache.*` — the implementation namespace changes, the abstractions namespace (`SharedKernel.Caching.Abstractions`) does not change. The `02.Caching/CLAUDE.md` package table and the root `CLAUDE.md` abstractions table must be updated to reflect the new name.

**Part B — `CachePolicy.NeverExpire` preset.** A new factory property added to `CachePolicy` in `SharedKernel.Caching.Abstractions`. It must map to `TimeSpan.MaxValue` for both L1 and L2 durations, with `FailSafeEnabled = true` and no `EagerRefreshThreshold`. XML doc must state that this preset is intended for truly static data (reference tables, feature flag snapshots, lookup codes) and that cache invalidation must be managed explicitly via `ICacheService.RemoveAsync` or `ICacheInvalidationBus`. This is a pure addition to the abstractions package — no behavioral changes to existing presets.

#### P-014: Why this is needed

The current name `SharedKernel.Caching` creates a naming conflict with the mental model: developers new to the platform assume the "caching package" is the one they always reference, when in fact they should always reference `SharedKernel.Caching.Abstractions`. Every other multi-provider domain in the platform follows the `SharedKernel.{Capability}.{Provider}` pattern (`.MassTransit`, `.EfCore`, `.Meilisearch`). Caching must follow the same pattern to be consistent and self-documenting. At v1.0.0, the rename cost is zero. Delaying it increases the cost as more services onboard. The `CachePolicy.NeverExpire` preset prevents teams from using arbitrary large `TimeSpan` values for static data and makes the intent explicit and auditable.

#### P-014: Acceptance criteria

- [ ] Folder `02.Caching/SharedKernel.Caching/` renamed to `02.Caching/SharedKernel.Caching.FusionCache/`
- [ ] `SharedKernel.Caching.csproj` renamed to `SharedKernel.Caching.FusionCache.csproj`; `<PackageId>` updated to `SharedKernel.Caching.FusionCache`
- [ ] Nested test project renamed to `SharedKernel.Caching.FusionCache.Tests`; `InternalsVisibleTo` updated accordingly
- [ ] All namespaces inside the renamed package updated from `SharedKernel.Caching.*` to `SharedKernel.Caching.FusionCache.*`
- [ ] `SharedKernel.Caching.Redis.csproj` `<ProjectReference>` updated to point to renamed project
- [ ] `consumer-verify` project updated to reference renamed package
- [ ] `Platform.SharedKernel.slnx` solution entry updated
- [ ] `02.Caching/CLAUDE.md` package table updated
- [ ] Root `CLAUDE.md` "What Goes Where" table updated: "A new cache interface or policy" row points to `SharedKernel.Caching.Abstractions`; "FusionCache L1 provider" row added pointing to `SharedKernel.Caching.FusionCache`; "Redis-specific cache implementation" row updated to `SharedKernel.Caching.Redis`
- [ ] `CachePolicy.NeverExpire` static property exists in `SharedKernel.Caching.Abstractions` returning a policy with `TimeSpan.MaxValue` for both durations and `FailSafeEnabled = true`
- [ ] XML doc on `CachePolicy.NeverExpire` states the intended use case and explicit invalidation requirement
- [ ] Existing tests all pass under the new package name — no regressions
- [ ] New test covers `CachePolicy.NeverExpire` property values
- [ ] `02.Caching/state-map.md` updated to reflect renamed package

---

### P-015 — Application: ICacheableQuery Marker and CachingBehavior Pipeline Behavior

**Status:** `○` Pending
**Work Order:** WO-004
**Domain:** 05.Application
**Depends on:** P-014

#### P-015: What is needed

A MediatR pipeline behavior and supporting marker interface, added to `05.Application`, that gives any query automatic caching without boilerplate in the handler.

**`ICacheableQuery<TResponse>` marker interface:** A zero-member interface that query types implement to opt in to automatic cache wrapping. It must be constrained to `IRequest<TResponse>` (MediatR). Implementing this interface signals that the query result can be cached. The interface lives in `05.Application` (or a sub-package `05.Application.Behaviors` if the domain planner chooses to split it) and references `SharedKernel.Caching.Abstractions` for `CachePolicy` and `ICacheKeyProvider`.

**`ICacheableQuery<TResponse>` must expose two members:** `CachePolicy CachePolicy { get; }` — the policy to apply for this query type, and `string CacheKey { get; }` — the pre-computed key for this specific query instance. The key is provided by the query itself because the query holds the discriminating parameters (e.g., entity ID). The `ICacheKeyProvider` from the caching abstractions is available as a DI service for queries that want to use the platform key format — but calling it is the query's responsibility, not the behavior's. This keeps the behavior simple and the key construction explicit.

**`CachingBehavior<TRequest, TResponse>` pipeline behavior:** A `IPipelineBehavior<TRequest, TResponse>` implementation constrained to `TRequest : ICacheableQuery<TResponse>`. On `Handle`, it calls `ICacheService.GetOrSetAsync` using the key and policy from the query. The factory delegate calls `next()` (the inner handler). This ensures stampede protection automatically — FusionCache's `GetOrSetAsync` guarantees the factory is called exactly once per key under concurrent load. The behavior must be registered in the pipeline after the validation behavior (validation must run before cache lookup to avoid caching responses to invalid requests). Registration is via a DI extension method `AddCachingBehavior(this IServiceCollection services)` or equivalent that adds the behavior to the MediatR pipeline. Must not use `services.AddMediatR` internally — the consuming service already registers MediatR; this extension only adds the behavior.

**`ICacheableQuery<TResponse>` is not applied to commands:** Commands must never be cached. The constraint `TRequest : ICacheableQuery<TResponse>` ensures the behavior is only invoked for queries that explicitly opt in.

#### P-015: Why this is needed

Without a `CachingBehavior`, every query handler in every microservice that wants caching must hand-roll the same pattern: check cache, call handler, store result. Across hundreds of services, this produces inconsistency in key formats, TTL choices, stampede vulnerability (teams use `GetAsync` then `SetAsync` rather than `GetOrSetAsync`), and maintenance overhead. The pipeline behavior centralizes this into a single, tested, platform-standard implementation. The `ICacheableQuery<TResponse>` marker makes caching opt-in and self-documenting — the query type itself declares its caching policy and key, making it instantly visible during code review without needing to trace through DI registrations. The `05.Application` layer is the correct home because `05.Application` is permitted to reference `02.Caching` abstractions (both are below `06.Persistence` in the layering hierarchy).

#### P-015: Acceptance criteria

- [ ] `ICacheableQuery<TResponse>` marker interface exists in `05.Application` (or sub-package); constrained to `IRequest<TResponse>`; exposes `CachePolicy CachePolicy { get; }` and `string CacheKey { get; }`
- [ ] `CachingBehavior<TRequest, TResponse>` implements `IPipelineBehavior<TRequest, TResponse>` constrained to `TRequest : ICacheableQuery<TResponse>`
- [ ] Behavior calls `ICacheService.GetOrSetAsync` — never `GetAsync` + `SetAsync` in sequence
- [ ] Behavior is registered after the validation behavior in pipeline order
- [ ] Registration extension method (`AddCachingBehavior`) does not call `AddMediatR` internally
- [ ] `05.Application` project references `SharedKernel.Caching.Abstractions` — not `SharedKernel.Caching.FusionCache` or `SharedKernel.Caching.Redis`
- [ ] Unit tests covering: cache hit (handler not called), cache miss (handler called once), stampede protection (concurrent requests invoke factory once), command types bypassed (behavior not invoked for non-`ICacheableQuery` requests)
- [ ] All public types carry XML doc comments
- [ ] Package remains AOT-safe

---
### P-016 — Caching: Resolve Redis→FusionCache Layering Violation via ServiceName in Abstractions

**Status:** `◐` Dispatched
**Work Order:** WO-006
**Domain:** 02.Caching
**Depends on:** None

#### What is needed

`SharedKernel.Caching.Redis` currently carries a `ProjectReference` to `SharedKernel.Caching.FusionCache` solely to access `CachingOptions.ServiceName` — used by `RedisCacheInvalidationBus` and `CacheInvalidationReceiver` to construct channel names. This is a sibling-level layering violation: two provider packages (`FusionCache` and `Redis`) are at the same architectural level and must not depend on each other. A service that wants only Redis capabilities (channel service, hash service, invalidation bus) without FusionCache must not be forced to transitively depend on the FusionCache provider.

The fix has two parts:

**Part A — Introduce `CachingCoreOptions` in `SharedKernel.Caching.Abstractions`.**
A new sealed options class in the Abstractions package that carries only the properties needed by both providers: `ServiceName` (string, defaults to `"app"`) with the same semantics as today. This class must have zero NuGet infrastructure dependencies and must live in the `SharedKernel.Caching.Abstractions` namespace. It must be the single authoritative source of `ServiceName` for all three packages.

**Part B — Remove `SharedKernel.Caching.Redis`'s direct reference to `SharedKernel.Caching.FusionCache`.**
`RedisCacheInvalidationBus` and `CacheInvalidationReceiver` must be updated to depend on `IOptions<CachingCoreOptions>` (from Abstractions) instead of `IOptions<CachingOptions>` (from FusionCache). `AddSharedKernelCaching` in the FusionCache package must register `CachingCoreOptions` alongside its own `CachingOptions`, copying `ServiceName` into it so both options types are always in sync. The `ProjectReference` from `SharedKernel.Caching.Redis` to `SharedKernel.Caching.FusionCache` must be removed; only the reference to `SharedKernel.Caching.Abstractions` is retained.

`CachingOptions` in `SharedKernel.Caching.FusionCache` retains `ServiceName` for backward compatibility (FusionCache-specific concerns like `L1SizeLimit`, `CacheName`, `SerializerContext`, `CompressionOptions` remain there). Both options types read from compatible configuration sections; `CachingCoreOptions` binds to the same section so a single `appsettings.json` entry covers both.

All existing tests must continue to pass — this is a non-breaking restructure.

#### Why this is needed

Provider packages at the same layer must never depend on each other. `SharedKernel.Caching.Redis` is a Redis provider; `SharedKernel.Caching.FusionCache` is a FusionCache provider. A microservice that uses only Redis Pub/Sub or distributed locking — without L1 FusionCache — should not be forced to take a transitive FusionCache dependency. The layering violation also creates a hidden upgrade coupling: a breaking change in `SharedKernel.Caching.FusionCache`'s `CachingOptions` class automatically breaks the Redis package. Placing the shared state (`ServiceName`) in Abstractions, where it logically belongs (it is not FusionCache-specific), resolves both problems.

#### Acceptance criteria
- [ ] `CachingCoreOptions` class exists in `SharedKernel.Caching.Abstractions` with at minimum a `ServiceName` property (string, default `"app"`)
- [ ] `SharedKernel.Caching.Abstractions.csproj` gains `Microsoft.Extensions.Options` (or remains with only the DI abstractions reference, using the options pattern via DI — verify the minimal dependency needed)
- [ ] `SharedKernel.Caching.Redis.csproj` no longer has a `ProjectReference` to `SharedKernel.Caching.FusionCache`
- [ ] `RedisCacheInvalidationBus` and `CacheInvalidationReceiver` depend on `IOptions<CachingCoreOptions>` from Abstractions, not `IOptions<CachingOptions>` from FusionCache
- [ ] `AddSharedKernelCaching` registers `CachingCoreOptions` as a configured singleton so the Redis package can resolve it
- [ ] `CachingOptions.ServiceName` in FusionCache keeps its validation and default — both options types are in sync at registration time
- [ ] All 91 FusionCache tests and 74 Redis tests continue to pass — zero regressions
- [ ] A service that references only `SharedKernel.Caching.Redis` + `SharedKernel.Caching.Abstractions` (no FusionCache) can call `AddRedisChannelService`, `AddRedisHashService`, `AddRedisCacheInvalidationBus` without pulling in FusionCache

---
### P-017 — Caching: Fix AddRedisL2 Silent Serializer Override Breaking NativeAOT

**Status:** `◐` Dispatched
**Work Order:** WO-006
**Domain:** 02.Caching
**Depends on:** None

#### What is needed

`AddRedisL2` in `RedisServiceCollectionExtensions` calls `.WithSystemTextJsonSerializer()` again on the FusionCache builder after `AddSharedKernelCaching` has already registered the STJ serializer — potentially with a combined `JsonSerializerOptions` that includes the user-provided `SerializerContext`. This second `.WithSystemTextJsonSerializer()` call silently overwrites the options-aware registration with a reflection-based default, defeating the NativeAOT-safe serializer path entirely.

The fix: `AddRedisL2` must not register the STJ serializer at all. The serializer is already registered by `AddSharedKernelCaching` (either with or without a combined `JsonSerializerOptions`). FusionCache's `WithRegisteredSerializer()` will pick up the DI-registered `IFusionCacheSerializer` automatically. `AddRedisL2` should call `WithRegisteredDistributedCache()` and `WithStackExchangeRedisBackplane()` only — no serializer re-registration.

This is a correctness fix, not a new capability. The existing test suite must be extended with a regression test: verify that after calling `AddSharedKernelCaching(o => o.SerializerContext = ctx)` followed by `AddRedisL2(...)`, the registered `IFusionCacheSerializer` is the one that has the combined `JsonSerializerOptions` with `ctx` in the resolver chain — not a default reflection-based one.

#### Why this is needed

This is a silent NativeAOT correctness bug. A microservice author sets `SerializerContext` correctly in `AddSharedKernelCaching` for NativeAOT compliance. They then add `AddRedisL2` to enable L2 caching. The second serializer registration silently discards their context. Their service compiles and runs in non-AOT mode but fails at runtime in NativeAOT builds with a cryptic `InvalidOperationException` or `NotSupportedException` during L2 cache reads/writes. Because the failure is silent during development and only surfaces in AOT publishing, this bug is high-severity.

#### Acceptance criteria
- [ ] `AddRedisL2` no longer calls `.WithSystemTextJsonSerializer()` in any form
- [ ] `AddRedisL2` calls `.WithRegisteredDistributedCache()` and `.WithStackExchangeRedisBackplane()` only for FusionCache configuration
- [ ] New unit test: after `AddSharedKernelCaching(o => o.SerializerContext = ctx)` + `AddRedisL2(...)`, the DI-resolved `IFusionCacheSerializer` is a `FusionCacheSystemTextJsonSerializer` (or `BrotliCacheSerializer` wrapping one) that includes `ctx` in its type info resolver chain
- [ ] All existing 91 FusionCache + 74 Redis tests pass — zero regressions
- [ ] The test is placed in `SharedKernel.Caching.FusionCache.Tests` or a new `SharedKernel.Caching.Redis.Tests` DI test class

---
### P-018 — Caching: DI Ergonomics Hardening — Builder Pattern and Startup Guards

**Status:** `◐` Dispatched
**Work Order:** WO-006
**Domain:** 02.Caching
**Depends on:** P-016

#### What is needed

Three DI ergonomics defects to fix in a single cohesive phase:

**Fix 1 — `AddRedisDistributedLocking` must be an extension on `ICachingBuilder`, not `IServiceCollection`.**
The current signature `AddRedisDistributedLocking(this IServiceCollection services, ...)` breaks the fluent builder chain. A service wanting L2 + locking must split across two chains:
```csharp
var builder = services.AddSharedKernelCaching(...).AddRedisL2(...);
services.AddRedisDistributedLocking(connectionString);  // breaks the chain
```
The new signature is `AddRedisDistributedLocking(this ICachingBuilder builder, string connectionString, ...)`. The implementation is identical — only the entry point changes. The old `IServiceCollection` extension must be kept as a `[Obsolete]`-marked shim for one version to prevent a hard break for any consumers that have already adopted it.

**Fix 2 — `AddRedisChannelService` must guard that `IConnectionMultiplexer` is already registered.**
Currently `AddRedisChannelService` registers `RedisChannelService` without checking that `IConnectionMultiplexer` is registered. This produces a cryptic runtime DI failure. The guard should match the pattern used by `AddRedisHashService`: check `builder.Services.Any(sd => sd.ServiceType == typeof(IConnectionMultiplexer))` and throw `InvalidOperationException("AddRedisChannelService requires AddRedisL2 or AddRedisDistributedLocking to be called first to register IConnectionMultiplexer.")` if absent.

**Fix 3 — `AddCacheInvalidationReceiver` must guard that both `IRedisChannelService` and `ICacheService` are registered.**
The receiver depends on both. If either is absent, the hosted service will fail at resolve time. Add the same pre-registration check pattern as the other extensions.

All three fixes are additive or shim-compatible — no behavioral changes to registered services.

#### Why this is needed

DI ergonomics failures are a Day-1 microservice pain point. When a team calls `AddRedisChannelService()` without the multiplexer, they get an `InvalidOperationException: Unable to resolve service for type IConnectionMultiplexer while attempting to activate RedisChannelService` — typically only seen at the first request in production. Clear startup guards (`ValidateOnStart`-equivalent) surface these configuration errors at application startup, where they are far cheaper to diagnose and fix.

#### Acceptance criteria
- [ ] `AddRedisDistributedLocking(this ICachingBuilder builder, ...)` extension exists on `ICachingBuilder`; the old `IServiceCollection` overload is `[Obsolete]` and delegates to the new one
- [ ] `AddRedisChannelService` checks for `IConnectionMultiplexer` at registration time; throws `InvalidOperationException` with a clear message if absent
- [ ] `AddCacheInvalidationReceiver` checks for `IRedisChannelService` and `ICacheService` at registration time; throws `InvalidOperationException` with a clear message if either is absent
- [ ] Unit tests exist covering each guard path — verify the correct exception message is thrown
- [ ] All 91 FusionCache + 74 Redis tests pass — zero regressions

---
### P-019 — Caching: Wire L1SizeLimit into FusionCache Memory Cache + Verify L2 KeyPrefix Behavior

**Status:** `◐` Dispatched
**Work Order:** WO-006
**Domain:** 02.Caching
**Depends on:** None

#### What is needed

Two connected dead-configuration and correctness issues:

**Fix 1 — Wire `CachingOptions.L1SizeLimit` into FusionCache's in-process memory cache.**
`CachingOptions` defines `L1SizeLimit = 10_000` but `AddSharedKernelCaching` never reads it. FusionCache supports a `MemoryCacheOptions` configuration with `SizeLimit`. The DI registration must pass `options.L1SizeLimit` to the `MemoryCacheOptions` so the L1 cache is correctly bounded. Without this, the option is dead configuration — services set it thinking it limits their L1 cache footprint, but it has no effect.

The correct wiring is through FusionCache's `WithOptions(o => o.SizeLimit = ...)` on the `IFusionCacheBuilder` returned by `AddFusionCache()`. The `MemoryCache` backing FusionCache must have `SizeLimit` set. Because the entry size is not tracked by default, entries must be given a size of 1 (or the entry options must set `Size = 1`) — document this behavior and the implication that `L1SizeLimit` is a count, not a byte limit.

**Fix 2 — Verify and document L2 key prefix behavior.**
`AddRedisL2` accepts a `KeyPrefix` in `RedisL2Options` and passes it as `InstanceName` to `AddStackExchangeRedisCache`. FusionCache uses `IDistributedCache` as the L2 backing store. The `StackExchange.Redis` `IDistributedCache` implementation prepends `InstanceName` to every key. However, FusionCache may also apply its own key transformation. Write an integration test that sets `KeyPrefix = "myservice"`, writes a value through `ICacheService`, and directly inspects the raw Redis key to confirm the prefix is actually applied. If the prefix is not applied or is double-applied, fix the wiring. Add explicit documentation of the effective L2 key format.

#### Why this is needed

`L1SizeLimit` is a visible option on `CachingOptions`. Services that set it to tune their memory footprint will see no effect — a silent configuration lie. In a K8s environment where pod memory limits are strict, unbounded L1 caches can cause OOMKilled events. Fixing the wiring makes the option actually work. The `KeyPrefix` issue is a correctness risk: if the prefix is not applied, two different services sharing a Redis instance can collide on L2 keys (e.g., both caching `default:user:42`).

#### Acceptance criteria
- [ ] `AddSharedKernelCaching` passes `CachingOptions.L1SizeLimit` to FusionCache's memory cache configuration
- [ ] Unit test verifies that setting `L1SizeLimit = 100` and inserting 101 unique entries causes the oldest or least-recently-used entry to be evicted
- [ ] Integration test writes a key through `ICacheService` with `KeyPrefix = "myservice"` set and reads the raw Redis key via `IConnectionMultiplexer.GetDatabase().StringGetAsync(...)` to confirm the prefix is applied
- [ ] Documentation in `AddRedisL2` XML doc explicitly states the effective L2 key format including the prefix
- [ ] All existing 91 FusionCache + 74 Redis tests pass

---
### P-020 — Caching: Upgrade GetOrSetAsync Factory Delegate to ValueTask

**Status:** `◐` Dispatched
**Work Order:** WO-006
**Domain:** 02.Caching
**Depends on:** None

#### What is needed

`ICacheService.GetOrSetAsync<T>` currently accepts a factory delegate typed as `Func<CancellationToken, Task<T>>`. In .NET 10, the idiomatic async primitive is `ValueTask<T>`. Forcing a `Task<T>` return from factory callers whose operations return `ValueTask<T>` requires an unnecessary `.AsTask()` call (which allocates a wrapper `Task`) or an `await` (which allocates a state machine). For a cache factory that executes on every cache miss — a hot path — this allocation is real and recurring.

**Change:** Update `ICacheService.GetOrSetAsync<T>` factory parameter to `Func<CancellationToken, ValueTask<T>>`. Update `FusionCacheService.GetOrSetAsync` to adapt the `ValueTask<T>` factory into the `Task<T>` form expected by FusionCache's underlying API (FusionCache's API accepts `Task<T>` factories; the adapter is `async token => await factory(token)` — one state machine allocation per miss, not per call).

**Additionally:** Add an overload `GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T?>> factory, CachePolicy policy, CancellationToken ct)` that returns `T?` (nullable) for callers that need to cache a "not found" null result. The current `GetOrSetAsync<T>` returning `T` (non-nullable) makes it impossible to cache an absent result. The nullable overload uses `MaybeValue<T>` support in FusionCache to cache null correctly.

This is a **breaking change** to the `ICacheService` interface. It must be paired with updated XML documentation explaining the migration. Existing callers using `Task<T>` factories must wrap with `async ct => await existingTaskFactory(ct)` — document this in the XML doc.

#### Why this is needed

`ICacheService` is the most frequently used interface in the entire caching system. Every cache miss path invokes the factory delegate. The `Task<T>` factory forces unnecessary allocations for the majority of callers in modern .NET whose data-access layers return `ValueTask<T>`. The `T?` nullable overload fills a real behavioral gap: services that cache negative results (e.g., "product X does not exist") currently have no mechanism to cache this fact and will stampede on every request for a missing key. Since this is a breaking change, it is far cheaper to make at v1.0.0 before any services onboard than at v2.0.0 with hundreds of consumers.

#### Acceptance criteria
- [ ] `ICacheService.GetOrSetAsync<T>` factory parameter changed from `Func<CancellationToken, Task<T>>` to `Func<CancellationToken, ValueTask<T>>`
- [ ] `FusionCacheService.GetOrSetAsync<T>` adapts the `ValueTask<T>` factory to FusionCache's `Task<T>` API correctly
- [ ] A new `GetOrSetAsync<T>` overload accepting `Func<CancellationToken, ValueTask<T?>>` and returning `ValueTask<T?>` is added to `ICacheService` and implemented in `FusionCacheService`
- [ ] XML documentation on both methods explains the difference and the nullable overload's use case (caching negative results / absent keys)
- [ ] All existing tests updated to use `ValueTask<T>` factory delegates
- [ ] New tests: nullable factory returning null caches the null result (factory called once on second request, not twice)
- [ ] All 91 FusionCache + 74 Redis tests pass after migration

---
### P-021 — Caching: Batch Get and Set Operations (GetManyAsync / SetManyAsync)

**Status:** `●` Complete
**Work Order:** WO-007
**Domain:** 02.Caching
**Depends on:** None

#### What is needed

Extend `ICacheService` in `SharedKernel.Caching.Abstractions` with two batch operation methods:

- `GetManyAsync<T>(IEnumerable<string> keys, CancellationToken ct)` — returns `IReadOnlyDictionary<string, T?>` mapping each requested key to its cached value or `null` on miss. Keys not present in cache map to `null`; the dictionary always contains an entry for every requested key.
- `SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct)` — stores multiple entries under the given keys in a single logical operation, applying the same `CachePolicy` to all entries in the batch.

The FusionCache implementation in `SharedKernel.Caching.FusionCache` implements these by calling `TryGetAsync` and `SetAsync` in a loop (FusionCache does not have native batch API). This is the correct implementation given FusionCache's stampede-protection model.

The Redis implementation detail: when L2 is active, `GetManyAsync` should use a Redis pipeline (`IDatabase.StringGetAsync` over a batch) via an optional `IRedisL2BatchService` internal helper in `SharedKernel.Caching.Redis`, so the multiple L2 lookups collapse to a single round-trip. This optimization is an implementation detail and must not leak into the `ICacheService` abstraction.

Both methods must carry XML doc comments stating the batch semantics, the per-key CachePolicy constraint for `SetManyAsync`, and the behavior on partial misses.

#### Why this is needed

Services that fetch lists of entities by ID (product catalogs, user profiles, recommendation sets) call `GetAsync` in a loop today. Each call is a separate L2 Redis round-trip. A 50-item fetch becomes 50 sequential or parallel Redis calls where a single pipeline call would suffice. Beyond the latency cost, each loop call resets the FusionCache stampede-protection context — multiple concurrent requests for the same batch can trigger the factory for the same keys in parallel. `GetManyAsync` provides a first-class batch path. Without it, every service team writes their own loop helper with inconsistent semantics.

#### Acceptance criteria
- [ ] `ICacheService` gains `GetManyAsync<T>` and `SetManyAsync<T>` with the signatures above
- [ ] `FusionCacheService` implements both methods; `GetManyAsync` returns a dictionary with one entry per input key; missing keys map to `null`
- [ ] `SetManyAsync` applies a single `CachePolicy` to all entries in the batch
- [ ] Unit tests in `SharedKernel.Caching.FusionCache.Tests`: batch get with mixed hits/misses, batch set then get-many, empty key list returns empty dictionary
- [ ] Integration tests in `SharedKernel.Caching.Redis.Tests`: batch get-many with L2 active uses a single Redis pipeline round-trip (verified via a test that counts Redis commands)
- [ ] `FakeCacheService` in `16.Testing` is updated to implement the new methods
- [ ] All existing tests continue to pass — interface addition is backward-compatible for all existing implementations
- [ ] All public types carry XML doc comments
- [ ] Package remains AOT-safe
---

---
### P-022 — Caching: IRenewableLock — Lock Heartbeat and Renewal for Long-Running Operations

**Status:** `◐` Dispatched
**Work Order:** WO-007
**Domain:** 02.Caching
**Depends on:** None

#### What is needed

The current `IDistributedLockService.AcquireAsync` returns an `IAsyncDisposable?` that represents a fixed-expiry lock. There is no mechanism to extend the lock while the work is in progress. Long-running operations (saga steps, database migrations, large batch jobs) that exceed the lock's initial TTL will silently lose the lock — another node may acquire it, leading to concurrent execution.

Add an `IRenewableLock` interface to `SharedKernel.Caching.Abstractions`:

- `RenewAsync(CancellationToken ct)` — extends the lock expiry by the original `expiry` duration. Returns `bool` — `true` if renewal succeeded (lock was still held), `false` if the lock was lost (e.g., expired before renewal, or Redis was unavailable). Must never throw for a lost lock.
- `IsAcquired` — `bool` property; `true` if the lock is currently held.
- The interface extends `IAsyncDisposable` so existing disposal patterns continue working.

`IDistributedLockService` gains a new method `AcquireRenewableAsync(string resource, TimeSpan expiry, TimeSpan wait, TimeSpan retry, CancellationToken ct) → ValueTask<IRenewableLock?>` that returns the renewable handle (or `null` if not acquired).

The RedLock.net implementation in `SharedKernel.Caching.Redis` implements `IRenewableLock` by calling `RedLockFactory.CreateAsync` with a renewal loop. RedLock.net supports `ExtendAsync` on an acquired lock — use this if the version supports it; otherwise re-acquire on the same resource before expiry.

A convenience `KeepAliveAsync(IRenewableLock lock, TimeSpan renewalInterval, CancellationToken ct)` static extension method is provided in the Redis package for callers that want automatic background renewal via a `Task` that loops and calls `RenewAsync` until cancellation.

#### Why this is needed

The `expiry` parameter on `AcquireAsync` is a safety backstop, not a work-duration estimate. Any real-world long-running critical section (e.g., a distributed migration, a scheduled job that should not run concurrently) needs to renew the lock periodically. Without a renewal API, teams either set an excessively long TTL (unsafe — a crashed node holds the lock for the full duration) or implement their own RedLock renewal ad-hoc (inconsistent, untested). `IRenewableLock` standardizes this pattern across all services.

#### Acceptance criteria
- [ ] `IRenewableLock` interface exists in `SharedKernel.Caching.Abstractions` with `RenewAsync`, `IsAcquired`, and `IAsyncDisposable`
- [ ] `IDistributedLockService` gains `AcquireRenewableAsync` method returning `ValueTask<IRenewableLock?>`
- [ ] `RedLockRenewableLock` implements `IRenewableLock` in `SharedKernel.Caching.Redis`; `RenewAsync` returns `false` without throwing when the lock is lost
- [ ] `KeepAliveAsync` static extension method exists for background renewal
- [ ] `FakeDistributedLockService` in `16.Testing` is updated to implement `AcquireRenewableAsync` with a fake `IRenewableLock` that tracks renewal calls
- [ ] Integration tests: renewal succeeds before expiry, renewal returns `false` after expiry, `KeepAliveAsync` prevents lock loss across a simulated slow operation
- [ ] All existing `AcquireAsync` tests continue to pass
- [ ] All public types carry XML doc comments
- [ ] Package remains AOT-safe
---

---
### P-023 — Caching: Sliding Expiration Support in CachePolicy

**Status:** `●` Complete
**Work Order:** WO-007
**Domain:** 02.Caching
**Depends on:** None

#### What is needed

`CachePolicy` in `SharedKernel.Caching.Abstractions` currently only models absolute TTLs. Sliding expiration — where the TTL resets on each access — is a standard pattern for session-adjacent data (shopping carts, user preference snapshots, partial form state) where entries should expire only when genuinely idle, not after a fixed wall-clock duration.

Add a `SlidingWindow` property to `CachePolicy` (`TimeSpan?`, nullable, defaults to `null`). When set, it expresses the idle-expiry window: a cached entry is evicted if it has not been accessed within `SlidingWindow`. When both `L1Duration` and `SlidingWindow` are set, `L1Duration` acts as the absolute maximum TTL and `SlidingWindow` acts as the idle TTL — consistent with how `MemoryCache` models sliding expiration.

Add a factory method `CachePolicy.Sliding(TimeSpan window)` that returns a policy with `SlidingWindow = window` and absolute durations matching `Default` (so the entry has a reasonable upper bound alongside the sliding window).

In `FusionCacheService.BuildEntryOptions`, map `SlidingWindow` to FusionCache's `FailSafeThrottleDuration` if FusionCache supports sliding semantics, or implement it via a short `L1Duration` + aggressive `EagerRefreshThreshold` that approximates idle-expiry behavior. The exact mapping is the domain planner's implementation decision — document the approximation in the XML doc if native sliding is not available.

The `NeverExpire` preset must explicitly state that `SlidingWindow` is incompatible with it and must not be set together — add a validation guard in `CachingOptions` or in `BuildEntryOptions`.

#### Why this is needed

Shopping cart data, user sessions, and partial workflow state are the canonical sliding-expiration use cases. Every service team that needs this today must model it as a short absolute TTL with `SetAsync` on every access — expensive, boilerplate-heavy, and inconsistent. A first-class `CachePolicy.Sliding` preset documents the intent, standardizes the pattern, and lets the FusionCache implementation apply the most efficient mechanism available.

#### Acceptance criteria
- [ ] `CachePolicy` gains `SlidingWindow` property (`TimeSpan?`, nullable)
- [ ] `CachePolicy.Sliding(TimeSpan window)` factory method exists and produces a policy with `SlidingWindow` set
- [ ] `FusionCacheService.BuildEntryOptions` maps `SlidingWindow` to a FusionCache mechanism; behavior and any approximation are documented in XML doc
- [ ] Validation guard prevents `NeverExpire` and `SlidingWindow` being combined
- [ ] Unit tests: `Sliding` factory method properties, sliding-expired entry is not returned after idle period, absolute max TTL still applies when `L1Duration` is also set
- [ ] All existing `CachePolicy` tests continue to pass
- [ ] All public types carry XML doc comments
---

---
### P-024 — Caching: Cache Key Versioning Strategy

**Status:** `●` Complete
**Work Order:** WO-007
**Domain:** 02.Caching
**Depends on:** None

#### What is needed

When a cached DTO's shape changes across a deployment (new required field, renamed property, type change), existing Redis L2 entries with the old shape will cause silent deserialization failures or corrupt data on the first access after the upgrade. There is currently no standard mechanism to signal that cached entries for a given key are stale due to a schema change.

Add `KeyVersion(int version)` as a fluent method on `CachePolicy`, analogous to `WithTags`. When set, the version number is appended to the cache key by `ICacheKeyProvider.BuildKey` as a suffix segment using the convention `{service}:{entity}:{id}:v{version}`. The default version is `0` (no suffix appended, backward-compatible).

Update `CacheKeyProvider` in `SharedKernel.Caching.FusionCache` to accept an optional version parameter and append the `v{version}` suffix when the version is non-zero.

Update `ICachingBuilder`-level documentation to describe the deployment workflow for key versioning:
1. Increment the version in `CachePolicy.KeyVersion` alongside the DTO change.
2. Deploy — new entries write to the new versioned key; old entries at the old key expire naturally via TTL.
3. No cache flush or service disruption required.

This is a purely additive change — existing callers that do not call `KeyVersion` are unaffected (version defaults to `0`, suffix omitted, identical key format to today).

#### Why this is needed

Silent deserialization failures after DTO schema changes are one of the most common sources of post-deployment incidents in caching-heavy microservices. Teams typically respond by either flushing Redis manually (unsafe, causes a cold-start stampede on all pods simultaneously) or adding ad-hoc `_v2` suffixes to key strings (inconsistent, easy to forget). A first-class `KeyVersion` contract makes schema evolution an explicit, zero-downtime, zero-flush operation. The version is attached to the policy — where the DTO type is also declared — making it impossible to change the DTO without being forced to notice the versioning annotation.

#### Acceptance criteria
- [ ] `CachePolicy` gains a `KeyVersion` property (`int`, default `0`) and a `WithVersion(int version)` fluent method
- [ ] `ICacheKeyProvider.BuildKey` is updated to accept an optional `int version` parameter (or a `CachePolicy` overload); when `version > 0`, appends `:v{version}` to the key
- [ ] `CacheKeyProvider` in FusionCache implements the version suffix correctly
- [ ] `ICacheService.GetOrSetAsync`, `SetAsync`, `GetAsync`, `RemoveAsync` do NOT change their signatures — key versioning is the caller's responsibility via `ICacheKeyProvider`; the behavior change is inside `BuildKey`, not the cache service
- [ ] Unit tests: `BuildKey` produces correct versioned key, version `0` produces same output as today (no regression), `WithVersion(3)` chains correctly with `WithTags`
- [ ] Documentation in `CacheKeyProvider` XML doc describes the deployment workflow
- [ ] All existing `CacheKeyProvider` and `CachePolicyTests` pass
---

---
### P-025 — Caching: RedisChannelService Reconnect Resilience

**Status:** `◐` Dispatched
**Work Order:** WO-007
**Domain:** 02.Caching
**Depends on:** None

#### What is needed

`RedisChannelService` in `SharedKernel.Caching.Redis` currently subscribes to Redis channels via `ISubscriber.SubscribeAsync`. When the Redis connection drops and reconnects (`ConnectionMultiplexer.ConnectionRestored` event), all existing subscriptions created on the old connection are silently lost — the `ISubscriber` instance is invalidated and new messages on those channels are not delivered. There is no current resubscription logic.

The fix: `RedisChannelService` must listen to `IConnectionMultiplexer.ConnectionRestored` event. On connection restoration, it must resubscribe to all channels currently tracked in its internal subscription registry. This must be exception-safe — a failure during resubscription must be logged at `LogLevel.Error` and must not propagate. The resubscription must replay the full channel registry state atomically (lock around registry iteration and resubscription to prevent concurrent `SubscribeAsync` from corrupting the registry state during replay).

Add a `ConnectionHealthState` property to the service (`Connected | Reconnecting | Disconnected`) that reflects the current StackExchange.Redis connection state — this is exposed for health check consumption.

The `CacheInvalidationReceiver` `BackgroundService` implicitly benefits from this fix — its subscriptions are managed through `IRedisChannelService`, so resubscription becomes automatic.

#### Why this is needed

Redis rolling upgrades, pod restarts, and network blips all trigger connection drops. A service running `CacheInvalidationReceiver` that loses its Redis connection will silently stop receiving invalidation signals — its L1 cache will become progressively stale with no indication. The pod does not crash, health checks may still pass, but cached data becomes stale indefinitely. This is a silent correctness failure in production. Reconnect-triggered resubscription is the standard Redis client pattern and must be implemented in `RedisChannelService` to make the invalidation bus reliable.

#### Acceptance criteria
- [ ] `RedisChannelService` subscribes to `IConnectionMultiplexer.ConnectionRestored` on construction
- [ ] On connection restoration, all channels in the internal subscription registry are resubscribed atomically; failures are logged and do not propagate
- [ ] `ConnectionHealthState` property is exposed with values `Connected`, `Reconnecting`, `Disconnected` reflecting the multiplexer state
- [ ] Integration test: drop and restore Redis container connection; verify messages are delivered again after reconnect; verify resubscription count equals the pre-disconnect subscription count
- [ ] All existing `RedisChannelServiceIntegrationTests` continue to pass
- [ ] All public types carry XML doc comments
- [ ] Package remains AOT-safe
---

---
### P-026 — Caching: CachingCoreOptions Standalone DI Registration

**Status:** `◐` Dispatched
**Work Order:** WO-007
**Domain:** 02.Caching
**Depends on:** None

#### What is needed

`CachingCoreOptions` is currently populated only by `AddSharedKernelCaching` (the FusionCache provider). A microservice that uses `SharedKernel.Caching.Redis` without the FusionCache package — for example, a background worker that uses only distributed locking, Redis Pub/Sub, or the cache invalidation bus — cannot set `ServiceName`. The `CachingCoreOptions.ServiceName` remains `"app"`, causing all invalidation channels to be mis-named and all distributed lock resources to share the wrong service namespace.

The fix: add a standalone `AddCachingCoreOptions(this IServiceCollection services, Action<CachingCoreOptions> configure)` DI extension method in `SharedKernel.Caching.Abstractions`. This extension must have zero infrastructure dependencies — it registers and configures `IOptions<CachingCoreOptions>` using only `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Options`. Services that call `AddSharedKernelCaching` do not need to call this separately — `AddSharedKernelCaching` already populates `CachingCoreOptions`. Services that use only Redis capabilities call `AddCachingCoreOptions` as their sole caching setup step.

Update `AddRedisCacheInvalidationBus` and `AddRedisChannelService` to check that `IOptions<CachingCoreOptions>` is registered (not just `IConnectionMultiplexer`) — if `CachingCoreOptions.ServiceName` is still the default `"app"` in production, log a `LogLevel.Warning` during startup that the service name has not been configured (non-fatal, but actionable).

#### Why this is needed

A distributed locking or Pub/Sub-only consumer is a legitimate use case — background workers often use Redis for coordination without needing an in-process L1 cache. Forcing such a consumer to take a transitive dependency on FusionCache (by calling `AddSharedKernelCaching`) to merely set a service name violates the plug-in principle. `AddCachingCoreOptions` gives Redis-only consumers a clean, zero-infrastructure-dependency entry point for this one configuration concern.

#### Acceptance criteria
- [ ] `AddCachingCoreOptions(this IServiceCollection, Action<CachingCoreOptions>)` extension exists in `SharedKernel.Caching.Abstractions`
- [ ] The extension has no dependency on FusionCache, StackExchange.Redis, or any provider package
- [ ] `SharedKernel.Caching.Abstractions.csproj` gains `Microsoft.Extensions.Options` reference if not already present (verify current state — may only have DI.Abstractions)
- [ ] A service can call `services.AddCachingCoreOptions(o => o.ServiceName = "worker-service").AddRedisDistributedLocking(connStr)` with no other caching registration
- [ ] Integration test (in `SharedKernel.Caching.Redis.Tests`): `CachingCoreOptions.ServiceName` resolves correctly when set via `AddCachingCoreOptions` without `AddSharedKernelCaching`
- [ ] `AddRedisCacheInvalidationBus` logs `LogLevel.Warning` at startup if `ServiceName` is still the default `"app"`
- [ ] All existing tests continue to pass
- [ ] All public types carry XML doc comments
---

---
### P-027 — Caching: ICacheWarmupStrategy Contract and Startup Runner

**Status:** `◐` Dispatched
**Work Order:** WO-007
**Domain:** 02.Caching
**Depends on:** None

#### What is needed

Add an `ICacheWarmupStrategy` interface to `SharedKernel.Caching.Abstractions` and a `CacheWarmupHostedService` runner in `SharedKernel.Caching.FusionCache`.

**`ICacheWarmupStrategy`** contract (in Abstractions):
- `string Name { get; }` — unique identifier for logging
- `int Order { get; }` — execution order among multiple strategies (lower = earlier)
- `WarmupAsync(ICacheService cache, CancellationToken ct)` — the warm-up logic; implementations call `SetAsync` or `GetOrSetAsync` with pre-fetched data to prime the L1 cache before traffic arrives

**`CacheWarmupHostedService`** (in `SharedKernel.Caching.FusionCache`):
A `BackgroundService` that resolves all registered `ICacheWarmupStrategy` instances from DI, orders them by `Order`, and executes them sequentially during startup. It must:
- Log start and completion of each strategy at `LogLevel.Information` with the strategy name and duration
- Catch and log exceptions per strategy at `LogLevel.Error` — a failed warmup strategy must not prevent the pod from starting
- Complete execution before the application's readiness probe reports `Healthy` — if possible, integrate with `IHostedLifecycle.StartedAsync` to delay readiness until warmup completes (or expose an opt-in `WaitForWarmup = true` option in the DI extension)

DI registration: `AddCacheWarmup<TStrategy>(this ICachingBuilder builder)` where `TStrategy : ICacheWarmupStrategy`. Multiple strategies can be added in sequence.

#### Why this is needed

Every K8s pod deployment starts with a cold L1 cache. The first wave of requests hits the factory (database, downstream APIs) for every key — a thundering herd that can overwhelm dependencies immediately after a rolling deployment. Teams currently handle this with ad-hoc `IHostedService` implementations that vary in error handling and ordering. A standard `ICacheWarmupStrategy` contract gives teams a consistent pattern, ensures warmup completes before the pod advertises readiness, and makes warmup logic discoverable and testable in isolation.

#### Acceptance criteria
- [ ] `ICacheWarmupStrategy` interface exists in `SharedKernel.Caching.Abstractions` with `Name`, `Order`, and `WarmupAsync`
- [ ] `CacheWarmupHostedService` exists in `SharedKernel.Caching.FusionCache`; executes registered strategies in `Order` order; logs per-strategy start/completion/failure
- [ ] A failed strategy is caught, logged, and does not abort remaining strategies or prevent startup
- [ ] `AddCacheWarmup<TStrategy>` extension on `ICachingBuilder` registers the strategy
- [ ] Unit test: multiple strategies execute in correct order; failed strategy does not abort others; timing is logged
- [ ] `FakeCacheService` can be used as the warmup target in tests
- [ ] All public types carry XML doc comments
- [ ] Package remains AOT-safe
---

---
### P-028 — Caching: Multi-Tenant Cache Key Isolation

**Status:** `◐` Dispatched
**Work Order:** WO-007
**Domain:** 02.Caching
**Depends on:** None

#### What is needed

SaaS microservices serving multiple tenants need per-tenant cache key namespacing to prevent cross-tenant data leakage. Today, `ICacheKeyProvider.BuildKey` produces `{service}:{entity}:{id}` — there is no tenant segment. Teams handle this by prepending a tenant ID manually, which is error-prone, inconsistent, and bypasses the platform key format contract.

Add `ITenantCacheKeyProvider` to `SharedKernel.Caching.Abstractions`:
- Extends `ICacheKeyProvider` (inherits `BuildKey`)
- Adds `BuildTenantKey(string tenantId, string entity, string id, params string[] extraSegments)` → string
- Key format contract: `{service}:{tenant}:{entity}:{id}[:{extraSegment}...]`

Add a default `TenantCacheKeyProvider` implementation in `SharedKernel.Caching.FusionCache`:
- Implements `ITenantCacheKeyProvider` by prepending the `tenantId` segment after the service name
- Registered as `ITenantCacheKeyProvider` singleton by a new `AddTenantCacheKeyProvider()` extension on `ICachingBuilder`
- The standard `ICacheKeyProvider` registration is left unchanged — `AddTenantCacheKeyProvider` is additive

Note: `ITenantCacheKeyProvider` must NOT have any dependency on `12.Security`'s `ITenantProvider` — it accepts `tenantId` as an explicit string parameter so it can be used with any tenancy resolution strategy. The caller provides the tenant ID; this service does not resolve it from context. This keeps `02.Caching` independent of `12.Security` per layering rules.

#### Why this is needed

Multi-tenancy is not a niche pattern — it is the default architecture for SaaS platforms. Cross-tenant cache key collisions are a security and correctness failure, not merely a data quality issue. Without `ITenantCacheKeyProvider`, every tenant-aware service team writes their own tenant-prefix helper. The results diverge: some teams use `{tenant}:{service}:{entity}:{id}`, others use `{service}:{entity}:{id}:{tenant}`, others forget the prefix entirely on some paths. A first-class contract with a standard format eliminates this category of bug. The explicit `tenantId` parameter (not resolved from `IHttpContextAccessor`) keeps the method pure and testable without HTTP context.

#### Acceptance criteria
- [ ] `ITenantCacheKeyProvider` interface exists in `SharedKernel.Caching.Abstractions`; extends `ICacheKeyProvider`; `BuildTenantKey` format contract is `{service}:{tenant}:{entity}:{id}[:{extra}...]`
- [ ] `TenantCacheKeyProvider` implements `ITenantCacheKeyProvider` in `SharedKernel.Caching.FusionCache`
- [ ] `AddTenantCacheKeyProvider()` extension on `ICachingBuilder` registers `TenantCacheKeyProvider` as `ITenantCacheKeyProvider` singleton
- [ ] `ITenantCacheKeyProvider` has zero dependency on `12.Security` or `IHttpContextAccessor`
- [ ] Unit tests: `BuildTenantKey` format, tenant isolation (two different tenant IDs produce different keys for the same entity+id), format consistency with `BuildKey` (same segments sans tenant produce the base format)
- [ ] `16.Testing` test double updated: `FakeTenantCacheKeyProvider` added to `AddFakeCachingServices()` (or a separate `AddFakeTenantCachingServices()` extension)
- [ ] All public types carry XML doc comments
---

---
### P-029 — Caching: Polly v8 Circuit Breaker for Redis L2 Path

**Status:** `◐` Dispatched
**Work Order:** WO-007
**Domain:** 02.Caching
**Depends on:** None

#### What is needed

When Redis is degraded (high latency, partial connectivity), FusionCache's fail-safe serves stale L1 data — but every L2 operation still attempts the Redis call, accumulates timeout latency, and generates structured errors. Over a 30-second Redis outage, a high-traffic service may generate millions of redundant failed Redis calls, each adding latency to the response path.

Add opt-in Polly v8 circuit breaker configuration to `AddRedisL2` in `SharedKernel.Caching.Redis`.

A new `RedisL2Options.CircuitBreaker` nested options class controls:
- `Enabled` (bool, default `false`) — opt-in
- `FailureThreshold` (int, default `5`) — consecutive failures before opening
- `SamplingDuration` (TimeSpan, default `10s`) — time window for failure counting
- `BreakDuration` (TimeSpan, default `30s`) — how long the circuit stays open
- `MinimumThroughput` (int, default `3`) — minimum calls before the circuit can open

When `Enabled`, `AddRedisL2` wraps the `IConnectionMultiplexer` in a Polly v8 `ResiliencePipeline` with a circuit breaker strategy. The pipeline is registered as a DI service so `RedisChannelService`, `RedisHashService`, and the backplane can benefit from it. When the circuit is open, Redis operations short-circuit immediately (no timeout wait) and FusionCache's fail-safe serves L1.

The `Polly.Core` (v8) and `Microsoft.Extensions.Http.Resilience` packages are the only new dependencies. Both are AOT-compatible.

#### Why this is needed

FusionCache's fail-safe correctly protects L1 serving during Redis outages, but it does not prevent the latency overhead of repeated failed Redis connection attempts. A Polly circuit breaker complements fail-safe: when Redis is clearly down, the circuit opens and L2 calls short-circuit to `null` (triggering fail-safe from L1 immediately, with zero Redis wait time). This is the K8s-native pattern for graceful degradation — pods continue serving at full speed from L1 while Redis recovers, rather than accumulating timeout latency on every request.

#### Acceptance criteria
- [ ] `RedisL2Options` gains a `CircuitBreaker` nested options class with the five properties above
- [ ] `AddRedisL2` registers a Polly v8 `ResiliencePipeline` as a singleton when `CircuitBreaker.Enabled = true`
- [ ] The circuit breaker wraps Redis connection operations; when open, L2 calls short-circuit and FusionCache fail-safe takes over
- [ ] `Polly.Core` v8 is added as a NuGet reference to `SharedKernel.Caching.Redis.csproj`
- [ ] Unit tests: circuit opens after `FailureThreshold` consecutive failures, circuit opens and short-circuits subsequent calls immediately, circuit closes after `BreakDuration`
- [ ] `CircuitBreaker.Enabled = false` (the default) produces zero behavioral change — all existing tests pass
- [ ] All public types carry XML doc comments
- [ ] Package remains AOT-safe
---

---
### P-030 — Caching: ICacheService OTel Meters (Hits / Misses / Errors / Duration)

**Status:** `◐` Dispatched
**Work Order:** WO-007
**Domain:** 02.Caching
**Depends on:** None

#### What is needed

`FusionCacheService` currently emits structured log messages for cache hits, misses, and factory invocations at `LogLevel.Debug`. Production services monitoring with Grafana/Prometheus have no visibility into cache efficiency — there are no OTel meters, no histograms, and no counters that wire into the `System.Diagnostics.Metrics` infrastructure.

Add OTel instruments to `FusionCacheService` using `System.Diagnostics.Metrics` (BCL — no additional NuGet dependency):

- `cache.hits` — `Counter<long>` — incremented on every `GetAsync` or `GetOrSetAsync` that returns a cached value without invoking the factory. Tags: `cache.key_prefix` (the `{service}:{entity}` portion), `cache.level` (`"l1"` or `"l2"`)
- `cache.misses` — `Counter<long>` — incremented on every `GetAsync` that returns `null` and on every `GetOrSetAsync` factory invocation. Tags: `cache.key_prefix`
- `cache.factory.duration` — `Histogram<double>` (milliseconds) — records the factory execution time on cache misses. Tags: `cache.key_prefix`
- `cache.errors` — `Counter<long>` — incremented on any exception in `GetOrSetAsync` factory or `SetAsync`. Tags: `cache.error_type`
- `cache.evictions` — `Counter<long>` — subscribes to FusionCache's `ItemEvicted` event to count evictions. Tags: `cache.eviction_reason`

The `Meter` name must be `"SharedKernel.Caching"` with version `"1.0"`. The `Meter` instance is created once as a static field in `FusionCacheService` — AOT-safe as it uses `string` names only.

No new DI registration is needed — `System.Diagnostics.Metrics` instruments are always active once created; listeners (Prometheus, OTEL exporter) subscribe to the `MeterListener` from outside. The instruments are created unconditionally — they impose negligible overhead when no listener is attached.

FusionCache has a built-in events API (`FusionCacheEventHandlers`) — prefer subscribing to its events over duplicating call-site logic. Use `IFusionCache.Events.Memory.Hit`, `Events.Memory.Miss`, `Events.Memory.Eviction`, and factory-completion events where available.

#### Why this is needed

Cache efficiency is one of the most operationally critical metrics in a microservice platform. A cache hit rate drop from 95% to 70% means a 5x increase in database load — but without meters, this is invisible until latency rises. The `cache.hits` / `cache.misses` pair directly produces the hit-rate metric that SREs watch. `cache.factory.duration` catches slow factory regressions (e.g., a database index was dropped). `cache.evictions` with `eviction_reason` catches L1 memory pressure issues before they become OOM events. Using `System.Diagnostics.Metrics` (BCL, AOT-safe) rather than a third-party metrics package keeps the dependency footprint minimal.

#### Acceptance criteria
- [ ] `FusionCacheService` creates a `Meter` named `"SharedKernel.Caching"` and the five instruments listed above
- [ ] `cache.hits` and `cache.misses` are emitted correctly for `GetAsync` and `GetOrSetAsync` operations
- [ ] `cache.factory.duration` records elapsed time of the factory delegate on each cache miss
- [ ] `cache.errors` increments on factory exceptions and `SetAsync` exceptions
- [ ] `cache.evictions` increments when FusionCache evicts entries (subscribes to eviction events)
- [ ] Meter name is `"SharedKernel.Caching"` — consistent with the OTel ActivitySource name used by `CacheInvalidationReceiver` (`"SharedKernel.Caching.Invalidation"`)
- [ ] Unit tests: verify each counter and histogram increments under the correct conditions using `MeterListener`
- [ ] All existing tests continue to pass — meter recording is additive
- [ ] All public types carry XML doc comments
- [ ] No new NuGet dependencies — `System.Diagnostics.Metrics` is in the BCL
---

---
### P-031 — Testing: Extend FakeCacheService with Batch Operations and Add FakeTenantCacheKeyProvider

**Status:** `○` Pending
**Work Order:** WO-007
**Domain:** 16.Testing
**Depends on:** P-021, P-028

#### What is needed

Two additions to `16.Testing/SharedKernel.Testing` to keep the testing infrastructure in sync with the caching capability expansions in WO-007:

**Part A — Extend `FakeCacheService` with `GetManyAsync` / `SetManyAsync`** (depends on P-021):
Implement the new batch methods on the existing `FakeCacheService`. `GetManyAsync` returns a dictionary built by iterating the internal in-memory store and mapping each key to its value or `null`. `SetManyAsync` stores all entries in a loop. Behavior is consistent with the existing fake: no expiry simulation, thread-safe via `ConcurrentDictionary`.

**Part B — Add `FakeTenantCacheKeyProvider`** (depends on P-028):
Implement `ITenantCacheKeyProvider` as an in-memory fake that produces keys using the format `{service}:{tenant}:{entity}:{id}[:{extra}...]`. The implementation should be deterministic and inspectable — expose a `BuiltKeys` list (similar to `FakeCacheInvalidationBus.PublishedInvalidations`) so tests can assert on the keys that were constructed. Also expose a `Reset()` method.

Update `AddFakeCachingServices()` to register both new additions so a single call wires up the complete fake caching ecosystem.

#### Why this is needed

The testing domain must stay current with the capability domain. When `16.Testing` fakes lag behind `02.Caching` contracts, service teams either cannot test new capabilities or write their own incomplete fakes. Extending `FakeCacheService` with batch operations ensures teams using `GetManyAsync` / `SetManyAsync` in their handlers can test them without a Redis container. `FakeTenantCacheKeyProvider` enables assertion-level testing of tenant-aware cache key construction without needing a fully wired DI stack.

#### Acceptance criteria
- [ ] `FakeCacheService` implements `GetManyAsync<T>` and `SetManyAsync<T>` per the contracts defined in P-021
- [ ] `FakeTenantCacheKeyProvider` implements `ITenantCacheKeyProvider`; exposes `BuiltKeys` list and `Reset()` method
- [ ] `AddFakeCachingServices()` registers `FakeTenantCacheKeyProvider` as `ITenantCacheKeyProvider` singleton
- [ ] Unit tests in `SharedKernel.Testing.Tests` cover: batch get with mixed hits/misses, batch set, tenant key format assertion via `BuiltKeys`, `Reset()` clears state
- [ ] `16.Testing` references `SharedKernel.Caching.Abstractions` — no new concrete provider references
- [ ] All public types carry XML doc comments
---
### P-032 — Domain Foundation: DDD Building Blocks for SharedKernel.Domain

**Status:** `●` Complete
**Work Order:** WO-008
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

The complete `SharedKernel.Domain` package — the DDD primitive layer that every downstream microservice aggregate, entity, value object, and domain event derives from. This package must have zero NuGet dependencies, reference only `SharedKernel.Primitives`, and be pure C# 13 targeting `net10.0`.

---

**Core identity contracts (interfaces):**

- `IEntity<TId>` — marker interface expressing that a type has a typed, non-null identity key. Constrained to `where TId : notnull`.
- `IAggregateRoot<TId>` — extends `IEntity<TId>`. Exposes `IReadOnlyCollection<IDomainEvent> DomainEvents` and `void ClearDomainEvents()`. This is the only surface callers (infrastructure dispatch) should depend on — not the concrete base class.
- `IValueObject` — zero-member marker interface. Signals structural-equality semantics and identifies domain value types for architecture enforcement rules.
- `IDomainService` — zero-member marker interface. Tags classes that contain domain logic that does not naturally belong to a single aggregate or entity. Used by governance architecture tests to enforce that domain services carry no infrastructure dependencies.
- `IStronglyTypedId<TValue>` — marker interface for strongly-typed ID wrappers. Exposes a single `TValue Value { get; }`. Constrained to `where TValue : notnull`. Used by persistence and governance layers to detect ID types.

**Domain event contracts:**

- `IDomainEvent` — the canonical event marker. Exposes `Guid Id { get; }` (event identity, not aggregate identity) and `DateTimeOffset OccurredOn { get; }`. No other members — routing, serialization, and dispatch are infrastructure concerns and must not appear here.
- `DomainEvent` — abstract record implementing `IDomainEvent`. `Id` is set to `Guid.NewGuid()` at construction. `OccurredOn` is an `init`-only property — it must be supplied by the caller at construction time (typically `AggregateRoot.RaiseDomainEvent`), not defaulted internally with `DateTimeOffset.UtcNow`. Direct use of `DateTimeOffset.UtcNow` inside `DomainEvent` is a hard violation. The accepted pattern: the `RaiseDomainEvent(IDomainEvent)` overload on `AggregateRoot` receives a pre-constructed event; a companion protected `RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent> factory)` overload accepts a factory that receives the current UTC time sourced from the aggregate's `IClock` field. This ensures all domain events carry timestamps sourced from `IClock`, not from ambient `DateTimeOffset.UtcNow`.

**Audit and cross-cutting marker interfaces:**

- `IHasCreatedAudit` — exposes `string CreatedBy { get; }` and `DateTimeOffset CreatedOn { get; }`.
- `IHasAudit` — extends `IHasCreatedAudit`. Adds `string? ModifiedBy { get; }` and `DateTimeOffset? ModifiedOn { get; }`.
- `ISoftDeletable` — exposes `bool IsDeleted { get; }`, `DateTimeOffset? DeletedOn { get; }`, `string? DeletedBy { get; }`.
- `IHasConcurrency` — exposes `byte[] RowVersion { get; }`. EF Core / persistence interceptors use this for optimistic concurrency token mapping.
- `IHasTenant` — exposes `Guid TenantId { get; }`. Enables persistence layer query filters and infrastructure isolation. This is a marker only — the domain layer has no tenant resolution logic.

**Strongly-typed ID base:**

- `StronglyTypedId<TValue>` — abstract record implementing `IStronglyTypedId<TValue>`. Carries `TValue Value { get; init; }` as a required positional record property. Overrides `ToString()` to return `Value.ToString()!`. Provides `implicit operator TValue` for ergonomic unwrapping. The base record must carry an XML doc note that STJ serialization of strongly-typed IDs requires a custom `JsonConverter` in the consuming service's serialization context — the domain package does not ship converters (that belongs in `06.Persistence` or `04.Contracts`).

**Entity base:**

- `Entity<TId>` — abstract class implementing `IEntity<TId>`. Constructor `protected Entity(TId id)` plus a protected parameterless constructor for ORM materialization. Id is `TId Id { get; private init; }`. Identity equality: two entities are equal if and only if their `GetType()` matches and their `Id` values are equal (via `EqualityComparer<TId>.Default`). Transient detection via `IsTransient()`: returns `true` when `Id` equals `default(TId)`. Transient entities are never equal to any other entity, including themselves (use `RuntimeHelpers.GetHashCode` as the hash for transient instances). `operator ==` and `operator !=` must be defined. No infrastructure concerns anywhere in this class.

**AggregateRoot base:**

- `AggregateRoot<TId>` — abstract class extending `Entity<TId>`, implementing `IAggregateRoot<TId>`. Holds a private `List<IDomainEvent>` field. Exposes `IReadOnlyCollection<IDomainEvent> DomainEvents` (read-only view). `ClearDomainEvents()` empties the list. Protected `RaiseDomainEvent(IDomainEvent domainEvent)` adds to the list. Protected `RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent> factory)` calls `factory(_clock.UtcNow)` and adds the result — this is the IClock-sourced overload. `IClock` is a constructor parameter on `AggregateRoot<TId>` (not optional, not injected via property), injected by domain constructors from the application layer. The protected parameterless constructor (for ORM materialization) must set `_clock` to a `NullClock` internal sentinel that returns `DateTimeOffset.MinValue` — this prevents NPE during ORM hydration while making it observable if domain methods that raise events are accidentally called on an ORM-materialized instance without a clock. Protected static `void CheckRule(IBusinessRule rule)` throws `BusinessRuleViolationException` if the rule is broken.

**Auditable aggregate bases (minimal set — four only):**

- `AuditableAggregateRoot<TId>` — extends `AggregateRoot<TId>`, implements `IHasAudit`. `CreatedBy`, `CreatedOn`, `ModifiedBy`, `ModifiedOn` all have `private set` (populated by EF Core interceptors or persistence-layer conventions — not by the aggregate itself). Protected parameterless constructor for ORM. No `SetAudit()` method — audit properties are owned by the infrastructure interceptor.
- `SoftDeletableAggregateRoot<TId>` — extends `AggregateRoot<TId>`, implements `ISoftDeletable`. `IsDeleted`, `DeletedOn`, `DeletedBy` all have `private set`. Exposes a protected virtual `void Delete(string deletedBy)` method that sets the soft-delete fields and raises a domain event via the `Func<DateTimeOffset, IDomainEvent>` overload — but because the concrete event type is defined by the subclass, `Delete` must be `protected abstract void OnDelete()` and the base provides a `protected void MarkAsDeleted(string deletedBy)` helper that sets the audit fields. Concrete aggregates override `OnDelete` to raise the appropriate domain event.
- `AuditableSoftDeletableAggregateRoot<TId>` — extends `AggregateRoot<TId>`, implements `IHasAudit` and `ISoftDeletable`. Combines the two sets of properties. This covers the most common production pattern. `private set` on all audit and soft-delete fields.
- `FullAuditableAggregateRoot<TId>` — extends `AggregateRoot<TId>`, implements `IHasAudit`, `ISoftDeletable`, `IHasConcurrency`. The "kitchen sink" base for aggregates needing full audit, soft delete, and optimistic concurrency. `RowVersion` is `byte[] { get; protected set; }` — `protected set` because the persistence layer may need to update it via a property setter after fetch, not via constructor.

**Auditable entity bases (for owned child entities, not just aggregates):**

- `AuditableEntity<TId>` — extends `Entity<TId>`, implements `IHasAudit`. `private set` on all audit fields. No event machinery — entities do not raise domain events.
- `FullAuditableEntity<TId>` — extends `Entity<TId>`, implements `IHasAudit`, `ISoftDeletable`, `IHasConcurrency`.

**ValueObject base:**

- `ValueObject` — abstract record. Structural equality is provided by C# record semantics when `GetEqualityComponents()` is used to drive equality, but abstract records do not automatically wire this. The domain planner must choose between: (a) abstract class with explicit `GetEqualityComponents()` → `IEnumerable<object?>` and overridden `Equals`/`GetHashCode`, or (b) abstract record relying on the implementing record's positional equality — option (b) is simpler and fully AOT-safe. The domain planner must pick one pattern and document it clearly. Whichever approach is chosen, `IValueObject` must be implemented, and the `Validate()` hook pattern must be provided: a protected abstract `IEnumerable<Error>? Validate()` method (returning null means valid) that the `ValueObject` constructor calls — this integrates railway-style validation at construction time using `SharedKernel.Primitives.Error`.

**Business rule system:**

- `IBusinessRule` — exposes `string Message { get; }` and `bool IsBroken()`.
- `BusinessRuleViolationException` — derives from `SharedKernelException` (from `01.Core`). Carries `IBusinessRule Rule { get; }`. Constructor takes the rule and passes `new Error(rule.Message)` to the base.
- Composite rules: `AndBusinessRule` (both must pass — broken if either is broken), `OrBusinessRule` (at least one must pass — broken only if both are broken), `NotBusinessRule` (inverts). All three are `sealed`. The `AndBusinessRule.Message` aggregates messages from broken sub-rules using "; " as delimiter.
- `BusinessRuleExtensions` — static class with `.And()`, `.Or()`, `.Not()` extension methods on `IBusinessRule`.

**Policy system:**

- `IPolicy<T>` — exposes `bool IsCompliant(T subject)`.
- Composite policies: `AndPolicy<T>`, `OrPolicy<T>`, `NotPolicy<T>` — sealed. Mirror the business rule composites.
- `PolicyExtensions` — static class with `.And<T>()`, `.Or<T>()`, `.Not<T>()` extension methods on `IPolicy<T>`.
- Note: `IPolicy<T>` is the domain-level compliance check (is this subject in a valid state for a domain rule?); `IBusinessRule` is the instance-level invariant check (is this specific value valid?). They are distinct — policies evaluate domain objects, business rules evaluate raw values or primitives.

**Specification system:**

- `ISpecification<T>` — the query-expression contract consumed by repositories in `06.Persistence`. Must expose: `Expression<Func<T, bool>>? Criteria`, `IReadOnlyList<Expression<Func<T, object>>> Includes`, `Expression<Func<T, object>>? OrderBy`, `Expression<Func<T, object>>? OrderByDescending`, `IReadOnlyList<(Expression<Func<T, object>> KeySelector, bool Descending)> ThenBys`, `int? Skip`, `int? Take`, `bool IsDistinct`. This is a read-only contract — the infrastructure layer evaluates it, the domain layer only constructs it.
- `Specification<T>` — abstract base class implementing `ISpecification<T>`. Provides protected builder methods: `AddCriteria(Expression<Func<T, bool>>)`, `AddInclude(Expression<Func<T, object>>)`, `ApplyOrderBy(Expression<Func<T, object>>)`, `ApplyOrderByDescending(Expression<Func<T, object>>)`, `ApplyThenBy(Expression<Func<T, object>>, bool descending)`, `ApplyPaging(int skip, int take)`, `ApplyDistinct()`. All builder methods return `void` — specifications are built in the constructor of the concrete subclass. Concrete specifications are sealed records or sealed classes.
- `AndSpecification<T>` — combines two specifications using expression tree AND via `ExpressionVisitor`. The `ParameterReplacer` pattern from the proposal is correct — accept as-is.
- `OrSpecification<T>` — combines using expression tree OR.
- `NotSpecification<T>` — negates using `Expression.Not`.
- `SpecificationExtensions` — `.And<T>()`, `.Or<T>()`, `.Not<T>()` on `Specification<T>`.

**Exceptions:**

- `BusinessRuleViolationException` — as described above. Lives in `03.Domain` (not `01.Core`) because it depends on `IBusinessRule`, which is a domain concept.

**IClock usage:**

- `AggregateRoot<TId>` accepts `IClock` via constructor. Domain aggregate constructors downstream accept and pass `IClock`. This is the only permitted time source.
- A `NullClock` internal sealed class implements `IClock` returning `DateTimeOffset.MinValue` — used for ORM materialization protection as described.

#### Why this is needed

`03.Domain` is the most foundational mutable layer in the entire SharedKernel. Every downstream microservice's aggregate, entity, and value object derives from these building blocks. The design must be correct the first time — changing `Entity<TId>` equality semantics, the `ISpecification<T>` contract, or the `DomainEvent` timestamp approach after dozens of services have onboarded causes a platform-wide breaking change. The IClock-injected timestamp pattern prevents non-deterministic test failures (tests no longer race against wall-clock time). The minimal auditable base class set (four only) controls combinatorial explosion while covering all real production patterns. The specification system with expression composites gives repositories in `06.Persistence` a type-safe, composable query language without domain-layer infrastructure coupling.

#### Acceptance criteria
- [ ] `IEntity<TId>`, `IAggregateRoot<TId>`, `IValueObject`, `IDomainService`, `IStronglyTypedId<TValue>` interfaces implemented with documented constraints
- [ ] `IDomainEvent` interface with `Id` and `OccurredOn`; `DomainEvent` abstract record with `init`-only `OccurredOn`, not defaulted internally to `DateTimeOffset.UtcNow`
- [ ] `AggregateRoot<TId>` accepts `IClock` via constructor; `RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent>)` overload uses `_clock.UtcNow`; `NullClock` sentinel for ORM materialization path
- [ ] `Entity<TId>` equality is identity-based; transient entities are never equal to any other; `IsTransient()` detects default-value Id
- [ ] `CheckRule(IBusinessRule)` on `AggregateRoot<TId>` throws `BusinessRuleViolationException` when the rule is broken
- [ ] Exactly four auditable aggregate bases: `AuditableAggregateRoot<TId>`, `SoftDeletableAggregateRoot<TId>`, `AuditableSoftDeletableAggregateRoot<TId>`, `FullAuditableAggregateRoot<TId>` — no more, no less
- [ ] `AuditableEntity<TId>` and `FullAuditableEntity<TId>` for non-aggregate child entities
- [ ] `ValueObject` abstract base with a clear documented equality strategy (abstract class with `GetEqualityComponents()` OR abstract record — domain planner chooses and documents)
- [ ] `ValueObject` constructor calls `protected abstract IEnumerable<Error>? Validate()` — null means valid; non-null collection throws `ValidationException` from `01.Core`
- [ ] `IBusinessRule`, `BusinessRuleViolationException`, `AndBusinessRule`, `OrBusinessRule`, `NotBusinessRule`, `BusinessRuleExtensions` implemented
- [ ] `IPolicy<T>`, `AndPolicy<T>`, `OrPolicy<T>`, `NotPolicy<T>`, `PolicyExtensions` implemented
- [ ] `ISpecification<T>` interface with all seven members; `Specification<T>` abstract base with all protected builder methods
- [ ] `AndSpecification<T>`, `OrSpecification<T>`, `NotSpecification<T>` using expression tree composition; `SpecificationExtensions`
- [ ] `StronglyTypedId<TValue>` abstract record with `implicit operator TValue` and XML doc note on STJ converter requirement
- [ ] `IHasCreatedAudit`, `IHasAudit`, `ISoftDeletable`, `IHasConcurrency`, `IHasTenant` marker interfaces
- [ ] `IDomainEventHandler<TEvent>` is NOT present in this package — it belongs in `05.Application`
- [ ] Zero NuGet dependencies — references only `SharedKernel.Primitives`
- [ ] No `DateTime.UtcNow` or `DateTimeOffset.UtcNow` direct usage anywhere in this package
- [ ] All public types carry XML doc comments
- [ ] AOT-safe: no reflection in hot paths, no `Assembly.Load`, no `Activator.CreateInstance`
- [ ] Package is registered in `Platform.SharedKernel.slnx` under solution folder `03.Domain`
- [ ] Test project `SharedKernel.Domain.Tests` covers: entity equality (same Id = equal, different Id = not equal, transient = not equal to anything), aggregate event accumulation and `ClearDomainEvents`, value object structural equality, business rule composites (`And`, `Or`, `Not`), specification expression composition, `StronglyTypedId` implicit operator
---

---
### P-033 — Persistence: EF Core Domain Primitive Support (Value Converters, Interceptors, Query Filters)

**Status:** `○` Pending
**Work Order:** WO-008
**Domain:** 06.Persistence
**Depends on:** P-032

#### What is needed

EF Core-specific support in `06.Persistence/SharedKernel.Persistence.EfCore` for the domain primitives defined in P-032. This is the infrastructure mirror of the domain contracts — every interface from `03.Domain` that requires persistence behavior gets its EF Core implementation here.

**Strongly-typed ID value converters:**

A generic `StronglyTypedIdValueConverter<TId, TValue>` EF Core `ValueConverter` that maps `TId` (implementing `IStronglyTypedId<TValue>`) to its primitive `TValue` for database column storage. A companion `StronglyTypedIdValueConverterSelector` extension for `ModelConfigurationBuilder` that auto-registers the converter for all strongly-typed ID types detected via `IStronglyTypedId<TValue>`. This prevents per-aggregate manual converter registration — one call wires all IDs.

**ValueObject owned entity convention:**

A model building convention that detects properties typed as `IValueObject` implementors and applies `.OwnsOne()` automatically. This eliminates manual `modelBuilder.Entity<T>().OwnsOne(...)` calls in every `IEntityTypeConfiguration`. The convention must handle nested value objects (`.OwnsOne` with a nested `.OwnsOne` for composed value objects). XML doc must document the limitations: collections of value objects (`OwnsMany`) must still be configured manually.

**Audit interceptor (`AuditSaveChangesInterceptor`):**

An `ISaveChangesInterceptor` that automatically populates `IHasCreatedAudit` and `IHasAudit` fields on `EntityState.Added` and `EntityState.Modified` entries. Requires an `IUserContext` abstraction (from `12.Security`) injected via DI to supply `CreatedBy`/`ModifiedBy`. Requires `IClock` (from `01.Core`) for timestamps. The interceptor must be registered in `AddSharedKernelPersistence` as part of the EF Core interceptor chain. Must not throw when `IUserContext` is not registered — in that case it must use a configurable `FallbackUserIdentifier` (default: `"system"`) and log a `LogLevel.Warning` at startup.

**Soft-delete query filter convention:**

A model building convention that detects entity types implementing `ISoftDeletable` and applies a global query filter `e => !e.IsDeleted` automatically. This eliminates per-entity manual filter registration. Provides an extension method `IgnoreSoftDeleteFilter()` on `IQueryable<T>` for queries that intentionally need to see deleted records (admin panels, audit trails).

**Concurrency token convention:**

A model building convention that detects properties implementing `IHasConcurrency.RowVersion` and applies `.IsRowVersion()` automatically for SQL Server or `.UseXminAsConcurrencyToken()` for PostgreSQL/Npgsql. The convention must be provider-aware — it checks the configured database provider and applies the correct token strategy. If the provider is unknown, it falls back to `IsConcurrencyToken()` and logs a `LogLevel.Warning` at startup.

**Tenant query filter convention:**

A model building convention that detects entity types implementing `IHasTenant` and applies a global query filter `e => e.TenantId == currentTenantId` automatically, sourcing `currentTenantId` from an `ITenantProvider` (from `12.Security`). Requires `ITenantProvider` injected into the DbContext or the convention builder. Must be opt-in (`AddTenantIsolationFilter()` extension) — not applied by default, since not all services are multi-tenant.

#### Why this is needed

Without these conventions and interceptors, every microservice team must manually configure value converters, write audit interceptors, apply soft-delete query filters, and handle concurrency tokens for every aggregate — repeating the same boilerplate across hundreds of services. A single bug in one team's interceptor (e.g., not populating `CreatedBy` on insert) produces silent data quality issues in production. Centralizing these in `SharedKernel.Persistence.EfCore` guarantees consistent behavior across all services and eliminates the boilerplate entirely.

#### Acceptance criteria
- [ ] `StronglyTypedIdValueConverter<TId, TValue>` EF Core value converter exists; auto-registered via `ModelConfigurationBuilder` extension
- [ ] `ValueObject` owned entity convention auto-applies `.OwnsOne()` for `IValueObject` properties; XML doc documents `OwnsMany` limitation
- [ ] `AuditSaveChangesInterceptor` populates `IHasCreatedAudit` on `Added` and `IHasAudit` on `Modified`; uses `IUserContext` and `IClock`; falls back to `"system"` with a warning if `IUserContext` is absent
- [ ] Soft-delete query filter convention auto-applies `e => !e.IsDeleted` for `ISoftDeletable` entities; `IgnoreSoftDeleteFilter()` extension exists on `IQueryable<T>`
- [ ] Concurrency token convention auto-detects `IHasConcurrency.RowVersion`; applies `.IsRowVersion()` or `.UseXminAsConcurrencyToken()` based on provider
- [ ] `AddTenantIsolationFilter()` extension applies `e.TenantId == currentTenantId` query filter for `IHasTenant` entities; is opt-in and not part of default registration
- [ ] All conventions register through `AddSharedKernelEfCore` DI extension without requiring manual wiring per-aggregate
- [ ] All public types carry XML doc comments
- [ ] Integration tests using Testcontainers PostgreSQL cover: strongly-typed ID round-trip, audit field population on insert/update, soft-delete filter hides deleted records, `IgnoreSoftDeleteFilter` returns deleted records, concurrency conflict raises `DbUpdateConcurrencyException`
---

---
### P-034 — Governance: Domain Layer Architecture Enforcement Rules

**Status:** `◐` Dispatched
**Work Order:** WO-008
**Domain:** 00.Governance
**Depends on:** P-032

#### What is needed

New architecture enforcement rules in `00.Governance/SharedKernel.ArchitectureTests` that protect the `03.Domain` layer's purity contracts. These rules must run on every build via the existing governance test suite.

**Rule 1 — Domain layer must not reference infrastructure packages:**
Any assembly in `03.Domain` must not reference `06.Persistence`, `07.Messaging`, `08.Storage`, `09.Search`, `10.Intelligence`, `11.Communication`, or any package with `EntityFramework`, `MassTransit`, `Redis`, or `RabbitMQ` in its name. Uses NetArchTest assembly dependency scanning. Failure message must identify the offending reference.

**Rule 2 — `IDomainEventHandler<TEvent>` must not live in domain assemblies:**
Any type implementing `IDomainEventHandler<TEvent>` found in an assembly under `03.Domain` fails this rule. Handlers belong in `05.Application` or `07.Messaging`. This prevents teams from accidentally placing handler logic inside domain event types.

**Rule 3 — `DateTime.UtcNow` and `DateTimeOffset.UtcNow` must not be used in domain assemblies:**
A Roslyn analyzer (or IL-based rule via NetArchTest) that detects direct usage of `DateTime.UtcNow`, `DateTime.Now`, `DateTimeOffset.UtcNow`, or `DateTimeOffset.Now` in any assembly under `03.Domain` or `05.Application`. Only `IClock.UtcNow` is the permitted time source. Failure must produce a diagnostic with a clear message and a code fix suggestion.

**Rule 4 — Domain services must not have constructor-injected infrastructure dependencies:**
Types implementing `IDomainService` must not have constructor parameters whose types are from infrastructure namespaces (EF Core, MassTransit, StackExchange.Redis, etc.). Domain services may only accept `IClock`, other domain interfaces, and `01.Core` primitives. This rule uses NetArchTest constructor injection scanning.

**Rule documentation:** All four rules must be documented in the `00.Governance` domain brain with the rationale, the offending-pattern example, and the compliant-pattern example.

#### Why this is needed

`03.Domain` is the most architecturally sensitive layer in the SharedKernel. A single infrastructure reference accidentally introduced here — a developer imports a NuGet package with EF Core attributes for "convenience" — propagates a hard dependency on a specific infrastructure stack to every service that references the domain. Without automated enforcement, this drift is silent until a service tries to compile against a different provider and fails. The `DateTime.UtcNow` rule is equally critical: non-deterministic time in domain logic makes unit tests flaky and prevents in-memory simulation of time-sensitive domain rules (e.g., "entity expires after 30 days"). These rules have zero false-positive rate when properly scoped and provide high value with negligible test runtime overhead.

#### Acceptance criteria
- [ ] NetArchTest rule exists asserting no `03.Domain` assembly references infrastructure packages; fails with offending reference name
- [ ] Architecture test asserts no type in `03.Domain` assemblies implements `IDomainEventHandler<TEvent>`
- [ ] Roslyn analyzer or NetArchTest IL rule flags `DateTime.UtcNow`, `DateTime.Now`, `DateTimeOffset.UtcNow`, `DateTimeOffset.Now` usage in domain assemblies; produces diagnostic with fix suggestion
- [ ] NetArchTest rule asserts domain service constructors contain no infrastructure-layer type parameters
- [ ] All four rules documented in `00.Governance/CLAUDE.md` with rationale and examples
- [ ] Governance test suite passes with all new rules included; each rule has at least one "violating" assembly fixture test that demonstrates the rule fires correctly
---

---
### P-035 — Testing: Domain Primitive Fakers and Test Helpers

**Status:** `○` Pending
**Work Order:** WO-008
**Domain:** 16.Testing
**Depends on:** P-032

#### What is needed

Additions to `16.Testing/SharedKernel.Testing` that give downstream microservice test projects the standard test helpers they need to exercise domain logic without boilerplate.

**`FakeClock`:**
A controllable `IClock` implementation for tests. Exposes `SetUtcNow(DateTimeOffset value)` to control the current time and `Advance(TimeSpan duration)` to move time forward. `UtcNow` returns the currently set value; `Today` returns `DateOnly.FromDateTime(UtcNow.DateTime)`. The fake must be thread-safe (backing field behind a lock or `Interlocked`). Registered as the default `IClock` when `AddFakeDomainServices()` is called.

**`EntityFaker<TEntity, TId>` base:**
A Bogus-based `Faker<TEntity>` subclass that gives downstream test projects a consistent base for generating domain entity test data. Provides a `WithClock(IClock clock)` builder method so the fake entity is constructed with a controllable `FakeClock`. This is an abstract base — consuming test projects create concrete fakers by subclassing and specifying `RuleFor` definitions. Not a complete auto-faker: domain fakers must declare their rules explicitly because domain invariants must be respected.

**`DomainEventAssertions` extension:**
Static extension methods on `IReadOnlyCollection<IDomainEvent>` (the type returned by `AggregateRoot.DomainEvents`) for test assertions:
- `ContainsEventOfType<T>()` — asserts at least one event of type `T` is in the collection; returns the matching event for further assertions
- `ContainsExactly<T>(int count)` — asserts exactly `count` events of type `T`
- `HasNoEvents()` — asserts the collection is empty
- `HasNoEventsOfType<T>()` — asserts no event of type `T` is present

These are assertion helpers — they throw `InvalidOperationException` with descriptive messages on failure. They do NOT depend on xUnit, NUnit, or any test framework — they are pure assertion helpers compatible with any test framework.

**`BusinessRuleAssertions`:**
A static extension method `ShouldBeBroken(this IBusinessRule rule)` and `ShouldNotBeBroken(this IBusinessRule rule)` — framework-agnostic assertion helpers that throw `InvalidOperationException` with the rule message on failure.

**`SpecificationAssertions`:**
A static helper `SpecificationAssert.Satisfies<T>(ISpecification<T> spec, T entity)` and `DoesNotSatisfy<T>` — evaluates the specification's `Criteria` expression against a given entity instance (using `.Compile()`) so unit tests can assert specification logic without a database. Note: `Compile()` is reflection-based — acceptable in test assemblies, never in production.

**`AddFakeDomainServices()` DI extension:**
Registers `FakeClock` as `IClock` singleton. Other domain test helpers are static, not DI-registered.

#### Why this is needed

Domain unit tests are the most valuable, fastest tests in a microservice. They require zero infrastructure. But without a standard `FakeClock`, teams either inject `DateTimeOffset.UtcNow` directly (violating the hard rule) or write their own clock fakes per service — inconsistent, non-reusable. Without `DomainEventAssertions`, teams write verbose `DomainEvents.OfType<T>().Should().HaveCount(1)` everywhere — readable but repetitive across hundreds of aggregate tests. The `SpecificationAssert` helper is particularly valuable: it lets teams write specification unit tests that prove `new ActiveOrdersSpec().IsSatisfiedBy(completedOrder) == false` without a database — something most teams skip entirely without tooling support. Centralizing these helpers in `SharedKernel.Testing` ensures all 100+ microservice test suites use the same assertion vocabulary.

#### Acceptance criteria
- [ ] `FakeClock` implements `IClock`; `SetUtcNow`, `Advance`, `UtcNow`, `Today` work correctly; thread-safe
- [ ] `EntityFaker<TEntity, TId>` abstract Bogus base with `WithClock(IClock)` builder method
- [ ] `DomainEventAssertions`: `ContainsEventOfType<T>()`, `ContainsExactly<T>(int)`, `HasNoEvents()`, `HasNoEventsOfType<T>()` — all throw with descriptive messages on failure; no test framework dependency
- [ ] `BusinessRuleAssertions`: `ShouldBeBroken` / `ShouldNotBeBroken` extension methods on `IBusinessRule`; throw with rule message on failure
- [ ] `SpecificationAssert.Satisfies<T>` / `DoesNotSatisfy<T>` evaluate specification criteria via `.Compile()` for in-memory assertion
- [ ] `AddFakeDomainServices()` registers `FakeClock` as `IClock` singleton
- [ ] `16.Testing` references `SharedKernel.Domain` — this is permitted (testing domain may reference any layer)
- [ ] All test helpers have their own unit tests in `SharedKernel.Testing.Tests`
- [ ] All public types carry XML doc comments
- [ ] `FakeClock` is AOT-safe (but `SpecificationAssert` may use `.Compile()` — document this as test-only, never for production use)
---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} ({domain(s) affected}) — {trigger}`.

- [2026-05-14] Root state-map template created — `/sync-brain`
- [2026-05-14] P-001, P-002 written for WO-001 — 01.Core SharedKernel.Primitives and SharedKernel.Core design — arch-lead
- [2026-05-14] 01.Core → Design (◐) — Define Result/Error/ValidationResult/IClock/SmartEnum and railway extensions (state-map-phase)
- [2026-05-14] Phase(s) P-001, P-002 dispatched to core-arch-planner for 01.Core (dispatch-phase)
- [2026-05-14] Core → Design (●) — promoted from SK.01.Design (state-map-phase)
- [2026-05-14] Core → Scaffold (●) — promoted from SK.01.Scaffold (state-map-phase)
- [2026-05-14] Core → Core (●) — promoted from SK.01.Core (state-map-phase)
- [2026-05-14] Core → Tests (●) — promoted from SK.01.Tests (state-map-phase)
- [2026-05-14] Core → Docs (●) — promoted from SK.01.Docs (state-map-phase)
- [2026-05-14] Core → Published (●) — promoted from SK.01.Published (state-map-phase)
- [2026-05-14] P-001, P-002 → ● Complete — manually closed after SK.01.Published confirmed ● (gap: phase-backlog closure was not wired into implement-phase-core flow)
- [2026-05-14] P-003, P-004 written for WO-002 — SharedKernel.Guards package and governance purity rule — arch-lead (UPGRADE: DomainError→Error, Guards split from Core, Enumeration<T>→SmartEnum, throw path added)
- [2026-05-14] Governance → Design (◐) — Define architecture test asserting that IGuardClause extension methods never throw (state-map-phase)
- [2026-05-14] Phase(s) P-003 dispatched to core-arch-planner for 01.Core (dispatch-phase)
- [2026-05-15] Phase(s) P-004 dispatched to governance-arch-planner for 00.Governance (dispatch-phase)
- [2026-05-15] Governance → Design (●) — promoted from SK.00.Design (state-map-phase)
- [2026-05-15] Governance → Scaffold (●) — promoted from SK.00.Scaffold (state-map-phase)
- [2026-05-15] Governance → Core (●) — promoted from SK.00.Core (state-map-phase)
- [2026-05-15] Governance → Tests (●) — promoted from SK.00.Tests (state-map-phase)
- [2026-05-15] Governance → Docs (●) — promoted from SK.00.Docs (state-map-phase)
- [2026-05-15] Core → Design (●) — promoted from SK.01.Design (state-map-phase)
- [2026-05-15] Core → Scaffold (●) — promoted from SK.01.Scaffold (state-map-phase)
- [2026-05-15] Core → Core (●) — promoted from SK.01.Core (state-map-phase)
- [2026-05-15] Core → Tests (●) — promoted from SK.01.Tests (state-map-phase)
- [2026-05-15] Core → Docs (●) — promoted from SK.01.Docs (state-map-phase)
- [2026-05-15] Core → Published (●) — promoted from SK.01.Published (state-map-phase)
- [2026-05-15] Phase Backlog entries for 01.Core closed → ● Complete — 01.Core reached Published (state-map-phase)
- [2026-05-15] Governance → Published (●) — promoted from SK.00.Published (state-map-phase)
- [2026-05-15] Phase Backlog entries for 00.Governance closed → ● Complete — 00.Governance reached Published (state-map-phase)
- [2026-05-15] Governance → Guard Purity Enforcement (●) — promoted from SK.00.GuardPurity (state-map-phase)
- [2026-05-15] Caching → Design (●) — promoted from SK.02.Design (state-map-phase)
- [2026-05-15] Caching → Scaffold (●) — promoted from SK.02.Scaffold (state-map-phase)
- [2026-05-15] Caching → Core (●) — promoted from SK.02.Core (state-map-phase)
- [2026-05-15] Caching → Tests (●) — promoted from SK.02.Tests (state-map-phase)
- [2026-05-15] Caching → Docs (●) — promoted from SK.02.Docs (state-map-phase)
- [2026-05-15] Caching → Published (●) — promoted from SK.02.Published (state-map-phase)
- [2026-05-18] P-005–P-011 written for WO-003 — Caching system upgrade: .Abstractions split, ICacheKeyProvider, IRedisChannelService, IRedisHashService, CachingBehavior, governance rules, health checks, test doubles (02.Caching, 05.Application, 00.Governance, 13.ServiceDefaults, 16.Testing) — arch-lead (UPGRADE: single .Abstractions package, ICacheKeyProvider added, Redis Pub/Sub and Hash capabilities scoped to 02.Caching, CachingBehavior in 05.Application)
- [2026-05-18] Application → Design (◐) — Define ICacheableQuery marker interface and CachingBehavior pipeline behavior contract using ICacheService and ICacheKeyProvider from Caching.Abstractions (state-map-phase)
- [2026-05-18] P-008 removed from WO-003 — 05.Application not ready; board row reset to ○ Not Started; Overall Progress updated (arch-lead)
- [2026-05-18] P-012, P-013 added to WO-003 — Cross-service cache invalidation broadcast: ICacheInvalidationBus abstraction + RedisCacheInvalidationBus + CacheInvalidationReceiver (02.Caching), FakeCacheInvalidationBus test double (16.Testing) — arch-lead
- [2026-05-18] Phase(s) P-005, P-006, P-007, P-012 dispatched to caching-arch-planner for 02.Caching (dispatch-phase)
- [2026-05-18] P-005 → ● Complete — SharedKernel.Caching.Abstractions package created, all 11 contracts delivered, solution registered (state-map-phase)
- [2026-05-18] P-012 → ● Complete — RedisCacheInvalidationBus, CacheInvalidationReceiver, CacheInvalidationExtensions implemented; 62 Redis + 66 Caching tests passing (state-map-phase)
- [2026-05-18] Application → Design (◐) — Define ICacheableQuery marker and CachingBehavior pipeline behavior from SharedKernel.Caching.Abstractions (state-map-phase)
- [2026-05-18] Application reset to ○ Not Started — P-015 deferred; 05.Application domain not yet started; board cleared
- [2026-05-18] Phase(s) P-009 dispatched to governance-arch-planner for 00.Governance (dispatch-phase)
- [2026-05-18] Phase(s) P-014 dispatched to caching-arch-planner for 02.Caching (dispatch-phase)
- [2026-05-20] Caching → Phase 15 (AOT Hardening + ITypedHashStore) (●) — promoted from SK.02.AotHardening (state-map-phase)
- [2026-05-20] Caching → Phase 16 (Brotli L2 Compression) (●) — promoted from SK.02.BrotliCompression (state-map-phase)
- [2026-05-20] P-016–P-020 written for WO-006 — 02.Caching production-readiness review: layering violation fix (ServiceName to Abstractions), AddRedisL2 AOT serializer override bug, DI ergonomics hardening (builder pattern + startup guards), L1SizeLimit wiring + L2 key prefix verification, GetOrSetAsync ValueTask upgrade — arch-lead
- [2026-05-20] Phase(s) P-016, P-017, P-018, P-019, P-020 dispatched to caching-arch-planner for 02.Caching (dispatch-phase)
- [2026-05-20] Caching → Phase 17 (Redis→FusionCache Layering Fix) (●) — promoted from SK.02.LayeringFix (state-map-phase)
- [2026-05-20] Caching → Phase 18 (AddRedisL2 Silent Serializer Override Fix) (●) — promoted from SK.02.AotSerializerFix (state-map-phase)
- [2026-05-20] Caching → Phase 19 (DI Ergonomics Hardening) (●) — promoted from SK.02.DiErgonomics (state-map-phase)
- [2026-05-20] Caching → Phase 20 (Wire L1SizeLimit + Verify L2 KeyPrefix) (●) — promoted from SK.02.L1SizeLimit (state-map-phase)
- [2026-05-20] Caching → Phase 21 (GetOrSetAsync ValueTask Factory) (●) — promoted from SK.02.ValueTaskFactory (state-map-phase)
- [2026-05-21] Phase(s) P-021, P-022, P-023, P-024, P-025, P-026, P-027, P-028, P-029, P-030 dispatched to caching-arch-planner for 02.Caching (dispatch-phase)
- [2026-05-21] Caching → Phase 22 (Batch Get and Set Operations) (●) — promoted from SK.02.BatchOperations (state-map-phase)
- [2026-05-21] Caching → Phase 23 (IRenewableLock Heartbeat and Renewal) (●) — promoted from SK.02.RenewableLock (state-map-phase)
- [2026-05-21] Caching → Phase 24 (Sliding Expiration in CachePolicy) (●) — promoted from SK.02.SlidingExpiration (state-map-phase)
- [2026-05-21] Caching → Phase 25 (Cache Key Versioning Strategy) (●) — promoted from SK.02.KeyVersioning (state-map-phase)
- [2026-05-21] Caching → Phase 26 (RedisChannelService Reconnect Resilience) (●) — promoted from SK.02.ChannelReconnect (state-map-phase)
- [2026-05-21] Caching → Phase 27 (CachingCoreOptions Standalone DI Registration) (●) — promoted from SK.02.CachingCoreOptionsDi (state-map-phase)
- [2026-05-22] Caching → Phase 28 (ICacheWarmupStrategy and Startup Runner) (●) — promoted from SK.02.CacheWarmup (state-map-phase)
- [2026-05-22] Caching → Phase 29 (Multi-Tenant Cache Key) (●) — promoted from SK.02.TenantCacheKey (state-map-phase)
- [2026-05-22] Caching → Phase 30 (Redis Circuit Breaker) (●) — promoted from SK.02.RedisCircuitBreaker (state-map-phase)
- [2026-05-22] Caching → Phase 31 (OTel Metrics) (●) — promoted from SK.02.OtelMeters (state-map-phase)
- [2026-05-22] Domain → Design (◐) — Define DDD building block surface: Entity, AggregateRoot+IClock, ValueObject, specs, business rules, policies (state-map-phase)
- [2026-05-22] Phase(s) P-032 dispatched to domain-arch-planner for 03.Domain (dispatch-phase)
- [2026-05-22] Domain → Design (●) — promoted from SK.03.Design (state-map-phase)
- [2026-05-22] Domain → Core (●) — promoted from SK.03.Core (state-map-phase)
- [2026-05-22] Domain → Tests (●) — promoted from SK.03.Tests (state-map-phase)
- [2026-05-22] Domain → Docs (●) — promoted from SK.03.Docs (state-map-phase)
- [2026-05-22] Domain → Published (●) — promoted from SK.03.Published (state-map-phase)
- [2026-05-22] Phase Backlog entries for 03.Domain closed → ● Complete — 03.Domain reached Published (state-map-phase)
- [2026-05-26] Domain → Design (●) — D-15..D-18 (WO-009) verified against CLAUDE.md; all 18 design tasks now ● (state-map-phase)
- [2026-05-26] Domain → Tests (●) — promoted from SK.03.Tests (state-map-phase)
- [2026-05-26] Domain → Published (●) — promoted from SK.03.Published (state-map-phase)
- [2026-05-26] Phase Backlog entries for 03.Domain closed → ● Complete — 03.Domain reached Published (state-map-phase)
- [2026-05-27] Core → P-042 Error.BusinessRule Factory (●) — promoted from SK.01.P042 (state-map-phase)
- [2026-05-27] Domain → Design (●) — promoted from SK.03.Design (state-map-phase)
- [2026-05-27] Domain → Tests (●) — promoted from SK.03.Tests (state-map-phase)
- [2026-05-27] Domain → Docs (●) — promoted from SK.03.Docs (state-map-phase)

---
### P-036 — Domain: Fix Auditable Aggregate Hierarchy — FullAuditable Extends AuditableSoftDeletable

**Status:** `●` Complete
**Work Order:** WO-009
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

`FullAuditableAggregateRoot<TId>` currently extends `AggregateRoot<TId>` directly and duplicates the entire soft-delete machinery (`MarkAsDeleted`, `OnDelete`, all three soft-delete fields: `IsDeleted`, `DeletedOn`, `DeletedBy`) that is already implemented identically in `AuditableSoftDeletableAggregateRoot<TId>`. The code is character-for-character duplicated across the two classes.

The correct hierarchy is:

```
AggregateRoot<TId>
  ├── AuditableAggregateRoot<TId>                        (IHasAudit)
  ├── SoftDeletableAggregateRoot<TId>                    (ISoftDeletable)
  └── AuditableSoftDeletableAggregateRoot<TId>           (IHasAudit + ISoftDeletable)
        └── FullAuditableAggregateRoot<TId>              (IHasAudit + ISoftDeletable + IHasConcurrency)
```

`FullAuditableAggregateRoot<TId>` must be refactored to extend `AuditableSoftDeletableAggregateRoot<TId>` instead of `AggregateRoot<TId>`. It removes all duplicated fields and methods, retaining only `IHasConcurrency` membership and the `RowVersion` property (with `protected set`). All constructors must chain correctly through the new base.

The `IHasAudit`, `ISoftDeletable`, and `IHasConcurrency` interface declarations on `FullAuditableAggregateRoot<TId>` must be retained as explicit `implements` declarations for clarity (even though they are transitively satisfied), so that the type's full contract is visible without navigating the hierarchy.

The CLAUDE.md auditable aggregate bases section must be updated to reflect the new inheritance chain and remove the statement that `FullAuditableAggregateRoot` extends `AggregateRoot<TId>` directly.

All existing tests for `FullAuditableAggregateRoot` must continue to pass without modification — this is a pure refactor with no behavioral change.

#### Why this is needed

Duplicated soft-delete machinery across two classes in the same hierarchy is a maintenance trap. When the `MarkAsDeleted` behavior needs to change (e.g., adding a domain event overload, or changing how `DeletedOn` is sourced), the change must be made in two places. In a gold-standard SharedKernel referenced by hundreds of services, divergence between the two implementations over time is inevitable and creates subtle bugs. Inheritance is the correct tool here — the hierarchy must reflect the actual IS-A relationship.

#### Acceptance criteria

- [ ] `FullAuditableAggregateRoot<TId>` extends `AuditableSoftDeletableAggregateRoot<TId>` (not `AggregateRoot<TId>`)
- [ ] `FullAuditableAggregateRoot<TId>` contains no duplicated soft-delete fields (`IsDeleted`, `DeletedOn`, `DeletedBy`) — these are inherited
- [ ] `FullAuditableAggregateRoot<TId>` contains no duplicated `MarkAsDeleted` method — inherited from base
- [ ] `FullAuditableAggregateRoot<TId>` contains no duplicated `OnDelete` abstract method — inherited from base
- [ ] `FullAuditableAggregateRoot<TId>` retains `IHasConcurrency` interface declaration and `RowVersion` property with `protected set`
- [ ] Both constructors (primary and ORM-path) chain correctly through `AuditableSoftDeletableAggregateRoot<TId>`
- [ ] All existing `AuditableAggregateTests` tests continue to pass with zero modifications
- [ ] `03.Domain/CLAUDE.md` auditable aggregate bases section updated to document the corrected hierarchy
---

---
### P-037 — Domain: Correct CLAUDE.md — SharedKernel.Core Is a Declared Dependency

**Status:** `●` Complete
**Work Order:** WO-009
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

The `03.Domain/CLAUDE.md` currently states that `SharedKernel.Domain` references "only `SharedKernel.Primitives`." The actual `SharedKernel.Domain.csproj` references both `SharedKernel.Primitives` and `SharedKernel.Core`. This discrepancy is a documentation error — the code is correct, the CLAUDE.md is stale.

`SharedKernel.Core` is a necessary reference because:
- `BusinessRuleViolationException` extends `SharedKernelException` from `SharedKernel.Core.Exceptions`
- `ValueObject` throws `ValidationException` from `SharedKernel.Core.Exceptions`
- These are the only two places in the domain package that depend on `SharedKernel.Core`

Three targeted updates are needed:

1. The packages table in CLAUDE.md: update the `References` column for `SharedKernel.Domain` from "`SharedKernel.Primitives`" to "`SharedKernel.Primitives`, `SharedKernel.Core`".

2. The Technology Stack table: update the "Domain primitives" and "Validation errors" rows to reflect that `ValidationException` comes from `SharedKernel.Core.Exceptions` (not just `SharedKernel.Primitives`), and that `SharedKernelException` base also comes from `SharedKernel.Core`.

3. The Implementation Rules section: replace "zero NuGet dependencies — references only `SharedKernel.Primitives`" with the accurate statement that the package references `SharedKernel.Primitives` and `SharedKernel.Core` (both from `01.Core`), and has zero external NuGet dependencies outside of the SharedKernel mono-repo.

No production code changes. No test changes. Documentation correction only.

#### Why this is needed

A CLAUDE.md that contradicts the actual csproj causes every future agent and contributor working in `03.Domain` to operate from a false premise — either they trust the CLAUDE.md (wrong) or they trust the code (correct but they must discover the discrepancy themselves). The domain brain must be the single source of truth. Stale documentation in a gold-standard shared library is a reliability risk: it will cause future refactors to be designed around the wrong dependency model.

#### Acceptance criteria

- [ ] `03.Domain/CLAUDE.md` packages table `References` column for `SharedKernel.Domain` lists both `SharedKernel.Primitives` and `SharedKernel.Core`
- [ ] Technology Stack table accurately attributes `ValidationException` and `SharedKernelException` to `SharedKernel.Core.Exceptions`
- [ ] Implementation Rules section accurately states that the package has zero external NuGet dependencies and references two `01.Core` packages
- [ ] No production source files modified
- [ ] No test files modified
---

---
### P-038 — Domain: Add IsSatisfiedBy In-Memory Evaluation to Specification

**Status:** `●` Complete
**Work Order:** WO-009
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

Add an `IsSatisfiedBy(T entity)` method to `Specification<T>` (the abstract base class, not the interface) that evaluates the specification's `Criteria` predicate against an in-memory entity instance.

The implementation must:
- Compile the `Criteria` expression to a `Func<T, bool>` delegate exactly once per specification instance and cache it (a private nullable backing field initialized on first call, or equivalent). Re-compilation on every call would be unacceptably expensive for hot paths.
- When `Criteria` is null (a specification with no filter, matching all entities), return `true` unconditionally.
- Be a concrete non-virtual method on `Specification<T>` — subclasses must not be able to override it.

The `ISpecification<T>` interface must NOT gain this method — it is an implementation convenience on the concrete base class, not a contract for the read-only specification view. Repository implementations in `06.Persistence` consuming `ISpecification<T>` do not use this method; it is exclusively for in-domain and in-test use.

XML documentation must state: "Evaluates this specification's criteria predicate against a single in-memory entity. The compiled delegate is cached on first call. Returns `true` when `Criteria` is null (all entities satisfy a criteria-less specification)."

A test must be added covering: criteria-less specification returns `true`; entity matching the criteria returns `true`; entity not matching the criteria returns `false`; the compiled delegate is reused across multiple calls (no re-compilation — verifiable by confirming the same `Func<T,bool>` reference is used).

#### Why this is needed

Every team using specifications for in-domain validation or in unit tests must currently write `spec.Criteria?.Compile().Invoke(entity) ?? true` — an inconsistent, un-obvious pattern scattered across hundreds of services. This is the standard "double dispatch" gap in the Specification pattern. Providing `IsSatisfiedBy` on the base class gives teams a clean, discoverable API. The compiled-and-cached delegate is critical for performance: expression compilation is expensive (equivalent to a JIT compilation step), and specifications are often reused many times within a request. Without caching, using `IsSatisfiedBy` in a loop would be catastrophically slow.

#### Acceptance criteria

- [ ] `Specification<T>.IsSatisfiedBy(T entity)` method exists as a non-virtual concrete method
- [ ] `Criteria` is compiled exactly once; the compiled `Func<T, bool>` is cached in a private field
- [ ] When `Criteria` is null, `IsSatisfiedBy` returns `true`
- [ ] `ISpecification<T>` interface is NOT modified — no new member added
- [ ] XML doc on `IsSatisfiedBy` states caching behavior and null-criteria semantics
- [ ] Test: criteria-less spec satisfies all entities
- [ ] Test: entity matching criteria → `true`; entity not matching → `false`
- [ ] Test: `IsSatisfiedBy` called multiple times on same spec instance uses cached delegate (no re-compilation)
- [ ] `03.Domain/CLAUDE.md` specification system section updated to document `IsSatisfiedBy`
---

---
### P-039 — Domain: Add DomainEvent Typed Payload Base Record

**Status:** `●` Complete
**Work Order:** WO-009
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

Add a `DomainEvent<TPayload>` abstract record in `Events/` that extends `DomainEvent` and carries a strongly-typed payload of type `TPayload`.

The record must:
- Extend `DomainEvent` (which provides `Id` and `OccurredOn`)
- Add a single `required init` property `TPayload Payload { get; init; }` — the domain-specific event data
- Be constrained to `TPayload : notnull` to prevent nullable payload types
- Remain abstract — concrete event records seal it
- Carry XML documentation that explains the intended usage pattern and how `Payload` relates to the aggregate's state change

Example usage the documentation must illustrate:
```
public sealed record OrderPlacedEvent : DomainEvent<OrderPlacedPayload>; 
public sealed record OrderPlacedPayload(Guid OrderId, decimal Total);
```

This is additive only — the existing non-generic `DomainEvent` abstract record is not modified or deprecated. Teams that prefer flat properties directly on the event record continue to use `DomainEvent` directly.

The `IDomainEvent` interface must NOT be modified. Generic dispatch in `05.Application` can pattern-match on `IDomainEvent` and use the `Payload` property when the concrete type is `DomainEvent<TPayload>`.

A test must be added to `DomainEventTests` covering: a concrete `DomainEvent<TPayload>` record correctly exposes `Payload`; it implements `IDomainEvent`; `Id` is generated at construction; `OccurredOn` is set via the `required` init pattern.

#### Why this is needed

In large microservice ecosystems, domain events frequently carry a distinct payload object that is also published as an integration event to `04.Contracts`. Teams that use flat event properties must copy-map them to a payload DTO. A typed `DomainEvent<TPayload>` base allows the domain event's payload to be the same object that travels across service boundaries (after mapping through `04.Contracts` envelope types). It also enables generic MediatR notification handler constraints in `05.Application`: `IDomainEventHandler<TEvent> where TEvent : DomainEvent<TPayload>` — giving application layer handlers type-safe access to the structured payload without reflection or casting. This is purely additive — zero breaking changes.

#### Acceptance criteria

- [ ] `DomainEvent<TPayload>` abstract record exists in `Events/`, extends `DomainEvent`, constrained to `TPayload : notnull`
- [ ] `TPayload Payload { get; init; }` is a `required` property on the record
- [ ] `DomainEvent` (non-generic) is unchanged — no deprecation, no modification
- [ ] `IDomainEvent` interface is unchanged
- [ ] XML doc illustrates the intended usage pattern with a concrete example
- [ ] `03.Domain/CLAUDE.md` events section updated to document `DomainEvent<TPayload>`
- [ ] Test: concrete `DomainEvent<TPayload>` record exposes `Payload` correctly
- [ ] Test: implements `IDomainEvent`
- [ ] Test: `Id` is auto-generated, `OccurredOn` is init-only via `required`
---

---
### P-040 — Domain: Add AsNoTracking Flag to ISpecification

**Status:** `●` Complete
**Work Order:** WO-009
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

Add a `bool AsNoTracking { get; }` property to `ISpecification<T>` and implement it on `Specification<T>` with a `protected void ApplyNoTracking()` builder method and a default of `false`.

The semantics:
- `AsNoTracking = false` (default) — the repository may use EF Core change tracking. Use for specifications that precede an update operation.
- `AsNoTracking = true` — the repository must apply `AsNoTracking()` to the query. Use for read-only query specifications.

The `Specification<T>` abstract base must default `AsNoTracking` to `false` (safe default — no data loss risk). A `protected void ApplyNoTracking()` builder method sets it to `true`. Concrete specification subclasses call `ApplyNoTracking()` in their constructors when they represent read-only queries.

XML documentation on `ISpecification<T>.AsNoTracking` must state: "When `true`, the consuming repository must apply `AsNoTracking()` to the underlying query. Set this for read-only query specifications to avoid unnecessary change-tracking overhead. Default is `false` — safe for specifications used before write operations."

A convenience base class `ReadOnlySpecification<T>` (sealed abstract, extends `Specification<T>`) must be added that calls `ApplyNoTracking()` in its constructor, so teams building query-only specifications can extend `ReadOnlySpecification<T>` instead of calling `ApplyNoTracking()` manually.

Tests must cover: default value is `false`; `ApplyNoTracking()` sets it to `true`; `ReadOnlySpecification<T>` always returns `true`; composed specifications (`AndSpecification`, `OrSpecification`, `NotSpecification`) must carry the `AsNoTracking` value of the left specification (or `true` if either operand is `true` — the more restrictive wins because composites are always query-oriented).

#### Why this is needed

Repository implementations in `06.Persistence` consuming `ISpecification<T>` currently have no way to know whether the caller intends to modify the retrieved entity. They either always use `AsNoTracking` (unsafe for write paths) or never use it (suboptimal for the overwhelming majority of read-only queries in a CQRS microservice). Without `AsNoTracking` on the specification, every repository implementation must make this decision by convention or by adding its own parameter — producing inconsistency across hundreds of services. Making tracking intent part of the specification contract is the correct DDD approach: the query declaration describes its full intent, and the persistence layer honors it without additional parameters.

#### Acceptance criteria

- [ ] `bool AsNoTracking { get; }` exists on `ISpecification<T>`
- [ ] `Specification<T>` defaults `AsNoTracking` to `false`
- [ ] `protected void ApplyNoTracking()` builder method sets `AsNoTracking` to `true`
- [ ] `ReadOnlySpecification<T>` abstract class extends `Specification<T>` and calls `ApplyNoTracking()` in its constructor — always returns `true` for `AsNoTracking`
- [ ] `AndSpecification<T>`, `OrSpecification<T>`, `NotSpecification<T>` constructors propagate `AsNoTracking`: set to `true` if either operand has `AsNoTracking = true`
- [ ] XML doc on `ISpecification<T>.AsNoTracking` states the `false`-is-safe-default rationale
- [ ] `03.Domain/CLAUDE.md` specification system section updated to document `AsNoTracking` and `ReadOnlySpecification<T>`
- [ ] Tests: default `false`; `ApplyNoTracking()` sets `true`; `ReadOnlySpecification<T>` always `true`; composed specs propagate correctly
---

---
### P-041 — Domain: Tighten AggregateRoot.Now — Add Explicit Guard and Documentation

**Status:** `●` Complete
**Work Order:** WO-009
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

The `protected DateTimeOffset Now => _clock.UtcNow;` property on `AggregateRoot<TId>` is currently underdocumented. Its existence invites subclasses to use it for purposes beyond its intended use case (reading the current time for non-event domain operations like `UpdatedAt` fields set directly on the aggregate), which undermines the `IClock` injection discipline.

Two targeted changes:

**1. Add an explicit `[Obsolete]` warning-level annotation if `Now` is used to timestamp a domain event directly (documentation-only guidance, not a runtime change).** This cannot be enforced at compile time without a Roslyn analyzer, so the approach is XML documentation. The `protected DateTimeOffset Now` property must carry an explicit XML `<remarks>` block stating: "This accessor exists for edge-case domain operations that require the current time outside of domain event factories (e.g., computing a deadline, setting a non-event field). It must NOT be used to supply `OccurredOn` for domain events — use the `RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent> factory)` overload, which passes the clock timestamp directly to the event factory. Using `Now` inside a domain event construction bypasses the deterministic timestamp contract."

**2. Add a governance architecture test entry (in `00.Governance` or as a note in `03.Domain/CLAUDE.md`)** documenting the constraint: aggregate subclasses must not pass `Now` as the `OccurredOn` value inside a `RaiseDomainEvent(IDomainEvent)` pre-built call — they must use the factory overload. This is captured as an implementation rule update in `03.Domain/CLAUDE.md`.

No code changes to `AggregateRoot.cs` are required. This phase is documentation-only within `03.Domain`. A separate `00.Governance` phase (future) may introduce a Roslyn analyzer for compile-time enforcement.

#### Why this is needed

The `Now` property is a footgun. A developer who sees `protected DateTimeOffset Now` will naturally use it for event construction: `RaiseDomainEvent(new OrderCreatedEvent { OccurredOn = Now })`. This compiles and runs correctly — but it bypasses the deterministic timestamp contract. In tests, the `FixedClock` is injected to control event timestamps; calling `Now` directly in a pre-built event side-steps the factory overload and makes event timestamps non-deterministic in unit tests (they will use the real wall clock if the aggregate was constructed with `SystemClock`). The `RaiseDomainEvent(Func<...>)` factory overload exists precisely to prevent this — it ensures the clock is the sole timestamp source. Clear documentation of the constraint costs nothing and prevents a recurring anti-pattern.

#### Acceptance criteria

- [ ] `AggregateRoot<TId>.Now` property carries an XML `<remarks>` block explicitly stating it must NOT be used to supply `OccurredOn` for domain events
- [ ] `<remarks>` explains the correct alternative: `RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent> factory)` overload
- [ ] `03.Domain/CLAUDE.md` Implementation Rules section gains a rule: "Subclasses must use the `RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent>)` factory overload to timestamp events — passing `Now` as `OccurredOn` in a pre-built event is a soft violation documented in XML."
- [ ] No behavioral changes — no production logic modified
---

- [2026-05-22] Phase Backlog entries for 03.Domain closed → ● Complete — 03.Domain reached Published (state-map-phase)
- [2026-05-22] WO-009 (P-036–P-041) queued — 03.Domain architectural audit: aggregate hierarchy refactor, CLAUDE.md dependency correction, Specification.IsSatisfiedBy, typed DomainEvent base, ISpecification.AsNoTracking, AggregateRoot.Now guidance (arch-lead)
- [2026-05-22] Phase(s) P-034 dispatched to governance-arch-planner for 00.Governance (dispatch-phase)
- [2026-05-22] Phase(s) P-036, P-037, P-038, P-039, P-040, P-041 dispatched to domain-arch-planner for 03.Domain (dispatch-phase)
- [2026-05-22] 00.Governance → Design (●) — promoted from SK.00.Design; root board not regressed (domain already at Guard Purity Enforcement ●) (state-map-phase)
- [2026-05-26] 03.Domain → Core (●) — promoted from SK.03.Core (state-map-phase)
- [2026-05-26] 03.Domain → Docs (●) — promoted from SK.03.Docs (state-map-phase)
- [2026-05-27] WO-010 (P-042–P-044) queued — 03.Domain deep architectural audit: Error.BusinessRule factory in 01.Core; BusinessRuleViolationException error classification fix + ValueObject constructor hazard documentation; TenantedAggregateRoot family for IHasTenant (arch-lead)
- [2026-05-27] WO-011 (P-045–P-057) queued — 03.Domain gold-standard second-pass audit (13 phases, 4 domains): SingleValueObject<TValue>, DomainService abstract base, IHasVersion domain concurrency, IHasDomainEvents separation, specification sentinels, PagedSpecification, Result<T> factory pattern, DomainEventVersion attribute, DomainException hierarchy, ThenByDescending alias, Apply event-sourcing hook, EventEnvelope in 04.Contracts, governance rules, testing fakers (arch-lead)
- [2026-05-27] Contracts → Design (◐) — Define EventEnvelope<TEvent> transport wrapper with CorrelationId, CausationId, SourceService, and DomainEventVersion fields for cross-service domain event publishing (state-map-phase)
- [2026-05-27] Phase(s) P-042 dispatched to core-arch-planner for 01.Core (dispatch-phase)
- [2026-05-27] Phase(s) P-043, P-044, P-045, P-046, P-047, P-048, P-049, P-050, P-051, P-052, P-053, P-054 dispatched to domain-arch-planner for 03.Domain (dispatch-phase)
- [2026-05-27] Domain → Published (●) — promoted from SK.03.Published (state-map-phase)
- [2026-05-27] Phase Backlog entries for 03.Domain closed → ● Complete — 03.Domain reached Published (state-map-phase)

---
### P-042 — Core: Add Error.BusinessRule Factory to SharedKernel.Primitives

**Status:** `◐` Dispatched
**Work Order:** WO-010
**Domain:** 01.Core
**Depends on:** None

#### What is needed

A new `Error.BusinessRule(string code, string message)` factory method on the existing `Error` sealed record in `SharedKernel.Primitives`. This requires a corresponding `BusinessRule` member added to the `ErrorType` enum (alongside the existing `Unexpected`, `Validation`, `NotFound`, `Conflict`, `Unauthorized`).

The semantic contract: a `BusinessRule` error represents a domain invariant violation — the operation was valid in form but violated a domain rule (e.g., "You cannot cancel a shipped order", "Discount cannot exceed 100%"). It is categorically distinct from `Validation` (input format/presence errors) and `Unexpected` (system faults). At the HTTP presentation layer, `BusinessRule` maps to HTTP 422 Unprocessable Entity with a domain-specific error body, identical to `Validation` HTTP-wise but semantically distinct for domain logic and audit purposes.

The new factory method must be consistent with the existing factory methods in signature: `public static Error BusinessRule(string code, string message)` returning `new Error(code, message, ErrorType.BusinessRule)`.

The `ErrorCodes` static class must gain a `Domain` nested static class with at minimum one constant: `Domain.RuleViolated = "domain.rule.violated"` — the canonical code used by `BusinessRuleViolationException`. This prevents magic strings from migrating between packages.

Tests must be added covering: `Error.BusinessRule(...)` produces an error with `ErrorType.BusinessRule`; `ErrorCodes.Domain.RuleViolated` constant is non-null and non-empty; `Error.BusinessRule` is distinct from `Error.Validation` and `Error.Unexpected` by type.

#### Why this is needed

`BusinessRuleViolationException` in `03.Domain` currently constructs its `Error` payload using `Error.Unexpected("domain.rule.violated", rule.Message)`. This is a semantic misclassification: `Unexpected` signals a system fault — an unrecoverable error the caller could not have predicted or prevented. A domain business rule violation is the opposite: it is an expected, predictable rejection of an operation that violates a known domain invariant. Misclassifying domain rule violations as `Unexpected` causes downstream effects:

1. Presentation layer middleware that maps `ErrorType` to HTTP status codes will use the wrong status (500 Internal Server Error for `Unexpected` vs 422 Unprocessable Entity for domain violations).
2. Monitoring and alerting systems that count `Unexpected` errors will fire false alerts on normal business rule rejections.
3. API consumers and client error-handling logic lose the ability to distinguish "system broke" from "your operation violated a domain rule."

Adding `ErrorType.BusinessRule` is a purely additive change — existing `ErrorType` values are unchanged and no existing code breaks.

#### Acceptance criteria
- [ ] `ErrorType` enum gains a `BusinessRule` member
- [ ] `Error.BusinessRule(string code, string message)` factory method exists on `Error` sealed record; returns an `Error` with `Type == ErrorType.BusinessRule`
- [ ] `ErrorCodes.Domain` nested static class exists in `SharedKernel.Primitives`; contains `RuleViolated = "domain.rule.violated"` constant
- [ ] Existing `Error.Unexpected`, `Error.Validation`, `Error.NotFound`, `Error.Conflict`, `Error.Unauthorized` are unchanged
- [ ] Unit tests: `Error.BusinessRule(...)` type is `ErrorType.BusinessRule`; factory method is distinct from `Unexpected` and `Validation` by type; `ErrorCodes.Domain.RuleViolated` is non-null/non-empty
- [ ] All existing `SharedKernel.Primitives` and `SharedKernel.Core` tests continue to pass — additive change only
- [ ] All public types carry XML doc comments; `ErrorType.BusinessRule` XML doc states the HTTP 422 mapping and domain-invariant-violation semantics
- [ ] Package remains AOT-safe; no reflection
---

---
### P-043 — Domain: Fix BusinessRuleViolationException Error Classification + ValueObject Constructor Hazard Documentation

**Status:** `●` Complete
**Work Order:** WO-010
**Domain:** 03.Domain
**Depends on:** P-042

#### What is needed

Two targeted corrections to `SharedKernel.Domain` delivered as a single phase:

**Part A — Fix `BusinessRuleViolationException` error classification:**

`BusinessRuleViolationException` currently constructs its `Error` payload as `Error.Unexpected("domain.rule.violated", rule.Message)`. This must be changed to `Error.BusinessRule(ErrorCodes.Domain.RuleViolated, rule.Message)` using the new factory added in P-042. This is a one-line fix with a significant semantic impact — see P-042 rationale.

The change also eliminates the magic string `"domain.rule.violated"` from `03.Domain` by referencing `ErrorCodes.Domain.RuleViolated` from `SharedKernel.Primitives`. This is the only change to production code in this phase.

A regression test must verify that after the fix, a caught `BusinessRuleViolationException` carries an `Error` with `Type == ErrorType.BusinessRule` (not `ErrorType.Unexpected`). This test must be added to the existing test suite.

**Part B — Document the `ValueObject` constructor validation hazard:**

The `ValueObject` base constructor calls `protected abstract IEnumerable<Error>? Validate()` immediately. This is a correct and useful pattern for most cases, but it has a subtle initialization-order hazard: if a subclass's `Validate()` reads an instance member that is assigned in the subclass constructor body (not a field initializer), that member will have its default value (`null` / `0` / `false`) when `Validate()` runs, because the base constructor executes before the subclass constructor body.

The fix is documentation — not a behavioral change. The `ValueObject` XML documentation must be updated with an explicit `<remarks>` block that:

1. Names the hazard clearly: "The base constructor calls `Validate()` immediately. If your `Validate()` implementation reads properties that are assigned in the subclass constructor body (not as field initializers), they will have default values (`null` / `0` / `false`) when validation runs."
2. Documents the two safe patterns:
   - **Safe: Field initializer assignment** — use `public decimal Amount { get; } = amount;` via a primary constructor parameter, which executes before the base constructor. Or use a read-only auto-property set in a C# 12+ primary constructor.
   - **Safe: Factory method pattern** — keep the constructor `private` or `protected`, expose a static `Create(...)` factory method that constructs the object (triggering validation) and returns `Result<TValueObject>` for railway-friendly error handling instead of throwing. This is the recommended pattern for value objects that require non-trivial validation.
3. Adds an example of the factory method pattern in the `<example>` block.

No behavioral changes to `ValueObject.cs`. No new interfaces or methods. Documentation update only for Part B.

#### Why this is needed

**Part A:** `Error.Unexpected` is semantically "the system broke" — it maps to HTTP 500 in the presentation layer. A business rule violation is an expected rejection — it maps to HTTP 422. Every microservice that catches `BusinessRuleViolationException` and inspects `exception.Error.Type` will mishandle it until this is fixed. This is a correctness bug, not a style issue.

**Part B:** The `ValueObject` constructor hazard will silently produce incorrect validation behavior for any team that writes a value object in the natural C# style (assigning properties in the constructor body). Because `Validate()` returns no errors for default values (`0 <= Amount` is true for `Amount == 0`), many teams will not catch this bug in testing — their value objects will appear to validate correctly. The bug only manifests with invalid inputs like negative amounts where the check `Amount < 0` is false for the default `0`. Teams with non-zero validation thresholds will see ghost validations. Clear documentation of the hazard and the two safe patterns prevents this entire class of bug from propagating across hundreds of microservices.

#### Acceptance criteria
- [ ] `BusinessRuleViolationException` constructor uses `Error.BusinessRule(ErrorCodes.Domain.RuleViolated, rule.Message)` — no raw `Error.Unexpected(...)` call
- [ ] No magic string `"domain.rule.violated"` remains in `03.Domain` source — replaced by `ErrorCodes.Domain.RuleViolated` constant
- [ ] New test: caught `BusinessRuleViolationException` carries `Error` with `Type == ErrorType.BusinessRule`
- [ ] All existing `BusinessRuleCompositeTests` and `AggregateRootEventTests` pass without modification
- [ ] `ValueObject` XML documentation gains an explicit `<remarks>` block documenting the constructor-validation hazard, two safe patterns, and a factory method example
- [ ] `03.Domain/CLAUDE.md` Implementation Rules section gains a rule documenting the `ValueObject` constructor hazard and the two safe patterns
- [ ] All 125+ existing tests continue to pass — no behavioral regressions
- [ ] Package remains AOT-safe; no reflection added
---

---
### P-044 — Domain: Add TenantedAggregateRoot Family for IHasTenant

**Status:** `●` Complete
**Work Order:** WO-010
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

Three new abstract aggregate base classes in `03.Domain` that implement `IHasTenant`, giving multi-tenant microservices a concrete hierarchy anchor for tenant-scoped aggregates. The family must follow the same IS-A chain discipline as the existing auditable hierarchy.

**`TenantedAggregateRoot<TId>`:**

Extends `AggregateRoot<TId>`, implements `IHasTenant`. Adds a single property `Guid TenantId { get; private set; }`. The primary constructor accepts `TId id, Guid tenantId, IClock clock` and sets `TenantId = tenantId` before calling `base(id, clock)`. The protected ORM-path constructor (parameterless) chains `base()` and leaves `TenantId` at `Guid.Empty` — persistence interceptors or EF Core conventions will populate it on hydration. XML doc must state that `TenantId` has `private set` — it is populated at construction (from the application layer, typically from `ITenantProvider`) and must not change after creation. It must not reference `12.Security.ITenantProvider` directly — the domain receives `tenantId` as a primitive value, not a resolved service.

**`TenantedAuditableAggregateRoot<TId>`:**

Extends `AuditableAggregateRoot<TId>`, implements `IHasTenant`. Adds `TenantId` as above. This covers the most common SaaS pattern: a tenant-scoped aggregate with creation/modification audit. Constructor: `TId id, Guid tenantId, IClock clock`.

**`TenantedFullAuditableAggregateRoot<TId>`:**

Extends `FullAuditableAggregateRoot<TId>`, implements `IHasTenant`. Adds `TenantId` as above. This covers the full-stack SaaS pattern: tenant-scoped aggregate with full audit, soft-delete, and optimistic concurrency. Constructor: `TId id, Guid tenantId, IClock clock`.

**Design constraints:**
- All three classes live in `Aggregates/` subfolder alongside the existing hierarchy
- `TenantId` has `private set` on all three — it is a construction-time assignment only; tenant reassignment is a domain violation (a separate business rule, if needed at all)
- No dependency on `12.Security` is introduced — `tenantId` is a `Guid` parameter, not resolved from any service
- `IHasTenant` is explicitly declared on each class for discoverability, even though it could be inferred from the interface
- The `TenantedAggregateRoot<TId>` family does NOT add a `TenantedSoftDeletableAggregateRoot<TId>` — this would explode the hierarchy. The recommended pattern for soft-delete + tenancy is `TenantedFullAuditableAggregateRoot<TId>` (which inherits soft-delete through `FullAuditableAggregateRoot`).

**Tests:**
- `TenantedAggregateRoot<TId>`: `TenantId` is set correctly at construction; ORM-path constructor leaves `TenantId` at `Guid.Empty`; `TenantId` setter is `private` (verified via reflection)
- `TenantedAuditableAggregateRoot<TId>` IS-A `AuditableAggregateRoot<TId>` and implements `IHasTenant`
- `TenantedFullAuditableAggregateRoot<TId>` IS-A `FullAuditableAggregateRoot<TId>` and implements `IHasTenant`; `MarkAsDeleted` and domain events are inherited correctly

**Documentation:**
- `03.Domain/CLAUDE.md` auditable aggregate hierarchy section gains a new subsection documenting the three tenanted bases, the `TenantId` construction pattern, and the explicit note that `12.Security.ITenantProvider` must not be referenced from domain code — the tenant ID is passed as a `Guid` parameter resolved at the application layer.
- `TenantedAggregateRoot<TId>` XML doc carries a `<remarks>` block stating: "The tenant identifier is a construction-time assignment. The domain layer has no tenant resolution logic — supply `tenantId` from the application layer (e.g., from a resolved `ITenantProvider` in the command handler). Do not modify `TenantId` after construction."

#### Why this is needed

`IHasTenant` has been in the domain model since the initial build, but it is a stranded interface with no concrete base class implementing it. Every multi-tenant microservice team that uses `SharedKernel.Domain` must write their own tenant property on their aggregate, creating inconsistency across the platform:

- Some teams use `Guid TenantId { get; private set; }` — correct
- Some teams use `string TenantId { get; set; }` — incorrect, allows post-construction mutation
- Some teams reference `ITenantProvider` inside their aggregate constructor — a hard layering violation (domain depends on security infrastructure)
- Some teams forget the property entirely and rely on query filters in the repository, then discover they cannot enforce tenant boundaries in the domain

A platform SharedKernel that defines `IHasTenant` but ships no concrete implementation of it is incomplete. The three tenanted bases close this gap and provide a consistent, auditable, layering-compliant foundation for all multi-tenant aggregates. The `private set` discipline on `TenantId` prevents cross-tenant contamination bugs (an aggregate belonging to Tenant A cannot have its `TenantId` overwritten to Tenant B by a persistence interceptor bug). The explicit three-class family (lean / auditable / full-auditable) covers the three most common production patterns without forcing teams into a "kitchen sink" base for simple cases.

#### Acceptance criteria
- [ ] `TenantedAggregateRoot<TId>` exists in `Aggregates/`, extends `AggregateRoot<TId>`, implements `IHasTenant`; `TenantId { get; private set; }` set at construction; ORM-path constructor leaves `TenantId` as `Guid.Empty`
- [ ] `TenantedAuditableAggregateRoot<TId>` exists in `Aggregates/`, extends `AuditableAggregateRoot<TId>`, implements `IHasTenant`; same `TenantId` contract
- [ ] `TenantedFullAuditableAggregateRoot<TId>` exists in `Aggregates/`, extends `FullAuditableAggregateRoot<TId>`, implements `IHasTenant`; same `TenantId` contract; `MarkAsDeleted` and `OnDelete` are inherited, not re-declared
- [ ] `TenantId` has `private set` on all three classes — verified via reflection test
- [ ] No reference to `12.Security` or `ITenantProvider` introduced anywhere in `03.Domain`
- [ ] Tests: construction sets `TenantId` correctly; ORM path leaves `Guid.Empty`; IS-A assertions for all three classes; `MarkAsDeleted` works correctly on `TenantedFullAuditableAggregateRoot<TId>`
- [ ] `03.Domain/CLAUDE.md` hierarchy section updated to document the three tenanted bases and construction pattern
- [ ] `03.Domain/CLAUDE.md` Implementation Rules gains rule: `TenantId` is construction-time only; `ITenantProvider` must not be referenced from domain code — pass `tenantId` as a `Guid` parameter from the application layer
- [ ] All 125+ existing tests continue to pass — additive changes only
- [ ] Package remains AOT-safe; no reflection in production code
- [ ] `SharedKernel.Domain` version bumped to `1.2.0` in csproj after all WO-010 changes are complete
---

---
### P-045 — Domain: IHasDomainEvents — Decouple Event Collection Contract from Aggregate Identity

**Status:** `●` Complete
**Work Order:** WO-011
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

Currently `IAggregateRoot<TId>` carries both the identity contract (`Id`, via `IEntity<TId>`) and the domain event collection contract (`DomainEvents`, `ClearDomainEvents()`). These are two distinct concerns conflated into one interface.

A standalone `IHasDomainEvents` interface must be introduced that owns the event collection contract:
- `IReadOnlyCollection<IDomainEvent> DomainEvents { get; }`
- `void ClearDomainEvents()`

`IAggregateRoot<TId>` must then extend `IHasDomainEvents` (in addition to `IEntity<TId>`) instead of declaring `DomainEvents` and `ClearDomainEvents()` directly. This is a pure refactor — no behavioral change, no API surface removed.

The concrete `AggregateRoot<TId>` already implements both; it simply now satisfies two interfaces via hierarchy rather than one.

**Why this matters beyond aggregates:** Infrastructure dispatch code (EF Core interceptors, outbox publishers, saga orchestrators) that publishes domain events should depend on `IHasDomainEvents`, not on `IAggregateRoot<TId>`. Process managers and saga state machines in `17.Workflows` often need to accumulate and dispatch domain events without being aggregate roots with typed identities. `IHasDomainEvents` gives those types a clean, minimal contract without forcing them to also implement `IEntity<TId>`.

`AggregateRoot<TId>` remains the concrete base — nothing changes for 99% of consumers. The benefit is that infrastructure dispatch code becomes cleaner:
```csharp
// Before: forced to know about aggregate identity
void DispatchEvents(IAggregateRoot<object> root) { ... }

// After: decoupled from identity
void DispatchEvents(IHasDomainEvents entity) { ... }
```

`CLAUDE.md` must be updated to document `IHasDomainEvents` and the rule that infrastructure dispatch code should depend on it rather than `IAggregateRoot<TId>`.

#### Why this is needed

The domain event dispatch contract and the aggregate identity contract are orthogonal. Infrastructure components (EF Core `ISaveChangesInterceptor`, `IOutboxPublisher`, Temporal workflow activities) that scan tracked entities for domain events do not need to know about aggregate identity — they only need to know how to read and clear the event list. Coupling them to `IAggregateRoot<TId>` forces unnecessary knowledge of the generic `TId` type parameter and prevents non-aggregate types (process managers, sagas) from participating in the event dispatch pipeline. This is a foundational interface hygiene improvement.

#### Acceptance criteria
- [ ] `IHasDomainEvents` interface exists in `Abstractions/` with `DomainEvents` and `ClearDomainEvents()` members; XML doc states it is the event-dispatch contract for infrastructure
- [ ] `IAggregateRoot<TId>` extends `IHasDomainEvents` instead of declaring `DomainEvents` and `ClearDomainEvents()` directly; the public API of `IAggregateRoot<TId>` is unchanged
- [ ] `AggregateRoot<TId>` continues to implement both interfaces correctly — no behavioral change
- [ ] All existing tests pass with zero modification — this is a pure interface refactor
- [ ] `03.Domain/CLAUDE.md` `IAggregateRoot<TId>` section documents that it extends `IHasDomainEvents`; a new `IHasDomainEvents` entry is added to the Interface Contracts section
- [ ] `03.Domain/CLAUDE.md` Implementation Rules gains a rule: "Infrastructure dispatch code must depend on `IHasDomainEvents`, not `IAggregateRoot<TId>` — dependency on aggregate identity is not required for event dispatch"
- [ ] Package remains AOT-safe; no reflection
---

---
### P-046 — Domain: SingleValueObject<TValue> — Convenience Base for Single-Primitive Value Objects

**Status:** `●` Complete
**Work Order:** WO-011
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

The current `ValueObject` abstract base requires every implementor to override two abstract methods: `GetEqualityComponents()` and `Validate()`. For the overwhelmingly common case of a value object wrapping a single primitive value (e.g., `EmailAddress(string value)`, `CustomerId(Guid value)`, `Percentage(decimal value)`), this is repetitive boilerplate.

A `SingleValueObject<TValue>` abstract class must be added that extends `ValueObject` and provides:
- A `public TValue Value { get; }` property set from the constructor parameter
- A sealed override of `GetEqualityComponents()` that returns `[Value]` — no override needed by subclasses
- A sealed override of `ToString()` that returns `Value?.ToString() ?? string.Empty`
- An `implicit operator TValue` for ergonomic unwrapping, consistent with `StronglyTypedId<TValue>`
- `Validate()` remains `protected abstract IEnumerable<Error>?` — subclasses must still declare their validation rules

The constructor signature is `protected SingleValueObject(TValue value)` which sets `Value = value` and then calls `base()` (which in turn calls `Validate()`). Because `Validate()` must be able to reference `Value`, the assignment of `Value = value` must happen in a field initializer or in the subclass constructor body before calling `base()`. Given C# constructor execution order, `Value` must be assigned before `base()` is called — the standard pattern is to assign `Value` directly and call `base()` in the constructor body, but the base constructor calls `Validate()` before the subclass can assign `Value`. This is the same hazard documented in P-043 (Part B).

The correct implementation uses the following pattern: `SingleValueObject<TValue>` must NOT call `base()` in the traditional chain. Instead, it must accept the value, assign `Value = value`, and then manually invoke `Validate()` — bypassing the `ValueObject` base constructor (or `ValueObject` must provide an `init`-protected bypass mechanism). The domain planner must choose the cleanest C# solution. One approach: `SingleValueObject<TValue>` overrides the abstract `Validate()` as sealed-abstract-passthrough and calls validation explicitly after assignment using a `private void Initialize()` method, not by calling `base()`. The XML documentation must explain the constructor order clearly.

**Distinction from `StronglyTypedId<TValue>`:**
- `StronglyTypedId<TValue>` is for entity/aggregate identity keys — database-persisted primitive IDs
- `SingleValueObject<TValue>` is for domain concepts with validation — email addresses, money amounts, percentages, scores

These are explicitly separate and must not be merged. The XML doc must state this distinction.

A test covering: correct `Value` set; `GetEqualityComponents()` returns only `[Value]`; equality works by value (not reference); `implicit operator TValue` unwraps correctly; `Validate()` returning errors throws `ValidationException`.

#### Why this is needed

Approximately 60–70% of all value objects in a typical DDD microservice wrap a single primitive. Every `EmailAddress`, `PhoneNumber`, `Percentage`, `Score`, `Amount` value object currently requires identical boilerplate: a private backing field or auto-property, a `GetEqualityComponents()` returning `[_value]`, and a `ToString()` delegation. Across dozens of services and hundreds of value objects, this is measurable boilerplate. `SingleValueObject<TValue>` reduces a 10-line value object to 4 lines (constructor + `Validate()` body), while the `implicit operator TValue` eliminates explicit `.Value` unwrapping throughout application code. The distinction from `StronglyTypedId<TValue>` is architectural — strongly-typed IDs have no validation logic, just identity semantics; single value objects have business-rule validation.

#### Acceptance criteria
- [ ] `SingleValueObject<TValue>` abstract class exists in `ValueObjects/`; extends `ValueObject` (directly or via a valid inheritance chain that preserves `Validate()`)
- [ ] `Value` property is `public TValue Value { get; }` — readable, non-mutable after construction
- [ ] `GetEqualityComponents()` sealed override returns exactly `[Value]` — no subclass override needed
- [ ] `ToString()` sealed override returns `Value?.ToString() ?? string.Empty`
- [ ] `implicit operator TValue` is defined
- [ ] `Validate()` remains abstract — subclasses must declare their validation rules
- [ ] Construction-order hazard is solved correctly: `Value` is accessible when `Validate()` runs
- [ ] XML doc states the distinction from `StronglyTypedId<TValue>` explicitly with a side-by-side example
- [ ] Tests: `Value` set correctly; equality by value; implicit unwrap; failed `Validate()` throws `ValidationException`; `Value` is accessible inside `Validate()` at construction time
- [ ] All existing `ValueObject` tests continue to pass — additive change
- [ ] Package remains AOT-safe
---

---
### P-047 — Domain: DomainService Abstract Base — Provide CheckRule Access and DI Anchor

**Status:** `●` Complete
**Work Order:** WO-011
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

`IDomainService` is currently a zero-member marker interface. Domain services — stateless types that coordinate between aggregates or enforce cross-aggregate invariants — often need to enforce business rules. Currently, `CheckRule(IBusinessRule)` is a `protected static` method on `AggregateRoot<TId>`. Domain services cannot access it without copy-pasting the implementation.

A `DomainService` abstract class must be added that:
- Implements `IDomainService` (marker)
- Exposes `protected static void CheckRule(IBusinessRule rule)` — identical semantics to `AggregateRoot<TId>.CheckRule`: throws `BusinessRuleViolationException` if `rule.IsBroken()`
- Has no constructor parameters — domain services are stateless; they are registered in DI and may receive domain objects or `01.Core` abstractions (like `IClock`) via their own constructors in concrete subclasses
- Is abstract — it cannot be instantiated directly; teams subclass it with `sealed` concrete domain services

The method must be the same implementation as on `AggregateRoot<TId>`. A future refactor could extract a shared internal helper to avoid duplication, but the domain planner may choose to duplicate the three-line implementation rather than create an internal utility class that links the two hierarchies. Both approaches are valid — the domain planner decides.

**What DomainService does NOT provide:**
- No `IClock` injection (domain services that need time receive it via their own constructor parameters)
- No `DomainEvents` accumulation (domain services do not raise events — aggregates do)
- No repository access (domain services receive already-loaded aggregates from the application layer)

XML doc must state all three exclusions explicitly so teams understand the design intent.

A test must cover: a concrete domain service extending `DomainService` can call `CheckRule`; broken rule throws `BusinessRuleViolationException`; non-broken rule does not throw.

`CLAUDE.md` must gain a `DomainService (abstract class)` entry in the Interface Contracts section and an Implementation Rule: "Domain services must extend `DomainService`, not implement `IDomainService` directly. This provides `CheckRule` access and serves as the DI anchor for governance architecture rules."

#### Why this is needed

Without a concrete base, teams either implement `IDomainService` directly (no `CheckRule` access, leading to manual `if (rule.IsBroken()) throw new BusinessRuleViolationException(rule)` scattered everywhere) or they skip domain services entirely and push cross-aggregate coordination into application handlers. Both outcomes are anti-patterns. The abstract `DomainService` base is the exact parallel to `AggregateRoot<TId>` — both provide `CheckRule`, both are abstract, both enforce the rule that only these types can check business rules. Governance architecture tests (P-034) can then enforce that all `IDomainService` implementors extend `DomainService` (not a raw class), the same way they enforce `IAggregateRoot<TId>` implementors extend `AggregateRoot<TId>`.

#### Acceptance criteria
- [ ] `DomainService` abstract class exists in a `DomainServices/` folder (or alongside business rules — domain planner decides); implements `IDomainService`
- [ ] `protected static void CheckRule(IBusinessRule rule)` method exists; throws `BusinessRuleViolationException` when `rule.IsBroken()` is true
- [ ] `DomainService` has no mandatory constructor parameters — concrete subclasses add their own
- [ ] `DomainService` does NOT accumulate domain events; no `DomainEvents` or `RaiseDomainEvent` present
- [ ] XML doc states the three exclusions: no clock, no events, no repository access
- [ ] Tests: concrete domain service can call `CheckRule`; broken rule throws; non-broken does not throw
- [ ] `03.Domain/CLAUDE.md` gains `DomainService` abstract class in the Interface Contracts section
- [ ] `03.Domain/CLAUDE.md` Implementation Rules gains rule: all domain service implementations must extend `DomainService` abstract class
- [ ] All existing tests continue to pass — additive change
- [ ] Package remains AOT-safe
---

---
### P-048 — Domain: IHasVersion — Domain-Native Integer Version Counter for Optimistic Concurrency

**Status:** `●` Complete
**Work Order:** WO-011
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

`IHasConcurrency` currently carries `byte[] RowVersion { get; }` — an SQL Server/EF Core infrastructure concept. A `rowversion` is a SQL Server-specific binary timestamp with no meaning outside the database. This leaks infrastructure semantics into the domain contract.

A domain-native `IHasVersion` interface must be added:
- `int Version { get; }` — a monotonically increasing integer counter representing the number of committed mutations to the aggregate
- Semantics: `Version` starts at `0` when the aggregate is first created. Each time `RaiseDomainEvent` is called (or optionally each time `SaveChanges` succeeds — the domain planner must document which), `Version` is incremented by 1.
- `Version` enables event-sourced-style version tracking without an event store: the aggregate can assert "I was at version 5 when the application read me; if version has changed since then, reject the update" — the classic optimistic concurrency check.

**Concrete additions:**

`AggregateRoot<TId>` must expose `int Version { get; private set; }` implemented by incrementing `Version` in `RaiseDomainEvent(IDomainEvent)` and in `RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent>)`. This choice means `Version` tracks "how many events have been raised since creation" — it is a domain-level concept, not an infrastructure counter.

`IHasConcurrency` must be kept unchanged — it remains the SQL Server `byte[] RowVersion` contract for EF Core's concurrency token. `IHasVersion` is the domain-native alternative. They are not mutually exclusive: an aggregate can implement both if it uses both mechanisms.

`FullAuditableAggregateRoot<TId>` must not automatically implement `IHasVersion` (it already implements `IHasConcurrency`). Teams that want integer version tracking implement `IHasVersion` on their own aggregate by overriding or via a separate base. Alternatively, a new `VersionedAggregateRoot<TId>` base may be added (extends `AggregateRoot<TId>`, implements `IHasVersion`) — the domain planner decides whether this separate base is cleaner than putting `Version` on `AggregateRoot<TId>` directly.

**Important constraint:** If `Version` is placed on `AggregateRoot<TId>` directly, it becomes universal — every aggregate has a version counter even if they don't need it. A separate `VersionedAggregateRoot<TId>` keeps the hierarchy clean. The domain planner must decide and document the trade-off.

XML doc on `IHasVersion.Version` must state: "Domain-native integer version counter. Increments on every raised domain event. Distinct from `IHasConcurrency.RowVersion` which is an infrastructure-specific SQL Server binary token. Use `IHasVersion` for domain-level event versioning; use `IHasConcurrency` for EF Core optimistic concurrency tokens."

Tests: `Version` starts at 0; increments by 1 per `RaiseDomainEvent` call; remains unchanged when no events are raised.

#### Why this is needed

`byte[] RowVersion` on `IHasConcurrency` is infrastructure vocabulary that has accidentally migrated into the domain model. Teams who read `IHasConcurrency` and see `byte[] RowVersion` immediately think "EF Core" and "SQL Server" — infrastructure concerns have no place in the domain contract. A domain-native `int Version` counter is universally understood, provider-agnostic, and directly usable in domain logic (e.g., an aggregate can assert `if (loadedVersion != this.Version) throw new ConcurrencyException()`). It also opens the door to event sourcing: if an aggregate tracks its version via events, replaying events to a target version becomes trivial. Adding `IHasVersion` costs one integer field and two one-line increments in `RaiseDomainEvent` — the benefit-to-cost ratio is extremely high.

#### Acceptance criteria
- [ ] `IHasVersion` interface exists in `Abstractions/` with `int Version { get; }`; XML doc states domain-native semantics and distinction from `IHasConcurrency`
- [ ] Either `AggregateRoot<TId>` gains `int Version { get; private set; }` incremented in both `RaiseDomainEvent` overloads (making it universal), OR a new `VersionedAggregateRoot<TId>` base is added (extends `AggregateRoot<TId>`, implements `IHasVersion`) — domain planner documents the trade-off in CLAUDE.md and chooses one
- [ ] `IHasConcurrency` is unchanged — `byte[] RowVersion` remains for EF Core concurrency token compatibility
- [ ] `Version` starts at `0` on construction; increments by 1 on every `RaiseDomainEvent` call
- [ ] Tests: initial `Version` is 0; raises 3 events → `Version == 3`; `ClearDomainEvents()` does not decrement `Version`; two aggregates with same event count but different identities have independent `Version` values
- [ ] `03.Domain/CLAUDE.md` gains `IHasVersion` in Interface Contracts section and documents the `IHasConcurrency` vs `IHasVersion` distinction
- [ ] All existing tests continue to pass — additive change
- [ ] Package remains AOT-safe
---

---
### P-049 — Domain: DomainException Base — Introduce Intermediate Exception Between SharedKernelException and Domain Exceptions

**Status:** `●` Complete
**Work Order:** WO-011
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

Currently `BusinessRuleViolationException` extends `SharedKernelException` directly. The exception hierarchy in `01.Core` already defines a `DomainException` type that extends `SharedKernelException`. However, `BusinessRuleViolationException` does not extend `DomainException` — it was wired to extend `SharedKernelException` directly when the domain package was first built, predating the `DomainException` addition in `01.Core`.

`BusinessRuleViolationException` must be changed to extend `DomainException` (from `SharedKernel.Core.Exceptions`) instead of `SharedKernelException` directly. This establishes the correct exception hierarchy:

```
Exception
  └── SharedKernelException           (01.Core)
        └── DomainException           (01.Core)
              └── BusinessRuleViolationException  (03.Domain)
```

This is a non-breaking change for callers that `catch (BusinessRuleViolationException)` — they continue to work. It is a behavioral improvement for callers that `catch (DomainException)` — they will now also catch `BusinessRuleViolationException`, which is correct.

Additionally, consider whether a `DomainNotFoundException` belongs here. When an aggregate cannot be found by its ID, the repository (in `06.Persistence`) could throw a `DomainNotFoundException` that extends `DomainException`. However, the correct placement is debated: if the exception type is defined in `06.Persistence`, domain code cannot reference it. If defined in `03.Domain`, it is available everywhere but implies the domain knows about the concept of "not found from persistence." The verdict: `DomainNotFoundException` belongs in `03.Domain` as a domain-level concept — the domain can legitimately say "the aggregate with this identity does not exist" without referencing persistence infrastructure. The repository in `06.Persistence` throws it; the domain defines the type.

`DomainNotFoundException` must:
- Extend `DomainException` from `01.Core`
- Carry `Type aggregateType { get; }` and `object aggregateId { get; }` for identifying what was not found
- Have a consistent message format: `"Entity of type '{aggregateType.Name}' with id '{aggregateId}' was not found."`
- Not carry an `Error.NotFound(...)` directly in its constructor — the `Error` is constructed from the provided type and id, using `ErrorCodes.NotFound.Entity` (a constant to be added to `SharedKernel.Primitives` or using the existing `Error.NotFound` factory)

Tests: `BusinessRuleViolationException` IS-A `DomainException`; `DomainNotFoundException` IS-A `DomainException`; `DomainNotFoundException` message format is correct; catching `DomainException` catches both.

#### Why this is needed

`catch (DomainException ex)` is the canonical pattern for catching "anything the domain rejected." Middleware, error-handling pipelines, and saga compensations need to catch domain-layer errors without enumerating every specific exception type. If `BusinessRuleViolationException` does not extend `DomainException`, these catch blocks silently miss it — teams discover this in production when a saga compensation fails to trigger on a business rule violation. The `DomainNotFoundException` fills a real gap: every repository implementation currently invents its own "not found" exception or returns null (which forces null checks at every call site). A standard `DomainNotFoundException` in `03.Domain` gives all `06.Persistence` implementations a common base to throw, and all `05.Application` handlers a common type to catch.

#### Acceptance criteria
- [ ] `BusinessRuleViolationException` extends `DomainException` (from `SharedKernel.Core.Exceptions`) instead of `SharedKernelException` directly
- [ ] `DomainNotFoundException` exists in `Exceptions/`; extends `DomainException`; carries `Type AggregateType { get; }` and `object AggregateId { get; }`; message format is `"Entity of type '{name}' with id '{id}' was not found."`
- [ ] `DomainNotFoundException` constructor uses `Error.NotFound(...)` for the base `Error` payload
- [ ] Tests: `BusinessRuleViolationException IS-A DomainException`; `DomainNotFoundException IS-A DomainException`; catching `DomainException` catches both; `DomainNotFoundException` message format verified
- [ ] All existing exception-related tests continue to pass — catching `SharedKernelException` still works (hierarchy is extended, not broken)
- [ ] `03.Domain/CLAUDE.md` Exceptions section updated to document the corrected hierarchy and `DomainNotFoundException`
- [ ] Package remains AOT-safe
---

---
### P-050 — Domain: Specification Sentinels — AllSpecification and EmptySpecification

**Status:** `●` Complete
**Work Order:** WO-011
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

Two sentinel specification types that serve as identity elements for specification composition:

**`AllSpecification<T>`:**
- A concrete (not abstract) sealed specification that matches every entity
- `Criteria` is `null` (no filter) — the existing `IsSatisfiedBy` behavior returns `true` for null criteria, so this is consistent
- Use as the starting identity for building AND chains dynamically: `var spec = new AllSpecification<T>(); foreach (...) spec = spec.And(filter);`

**`EmptySpecification<T>`:**
- A concrete sealed specification that matches no entity
- `Criteria` is `_ => false` — an expression that always evaluates to false
- Use as the starting identity for building OR chains dynamically

Both types must be sealed concrete classes (not abstract) so they can be instantiated directly without subclassing.

Both must be documented with the composition pattern they serve (AND identity = start with `All`; OR identity = start with `Empty`) and an explicit code example showing the conditional filter building pattern.

A test covering: `AllSpecification<T>.IsSatisfiedBy(entity)` returns `true` for any entity; `EmptySpecification<T>.IsSatisfiedBy(entity)` returns `false` for any entity; `AllSpecification<T>.And(filterSpec)` produces a spec equivalent to `filterSpec` alone (filter criteria wins); `EmptySpecification<T>.Or(filterSpec)` produces a spec equivalent to `filterSpec` alone.

Additionally, the composite specifications `AndSpecification<T>` and `OrSpecification<T>` must have their null-criteria handling behavior explicitly documented and tested:
- `And` with one null-criteria operand: currently takes the other operand's criteria (effectively ignores the `AllSpec`) — this is the correct AND-identity behavior. Must be documented in XML with a `<remarks>` block.
- `Or` with one null-criteria operand: currently takes the other operand's criteria — this is NOT the correct OR-identity behavior (if one operand matches everything, the OR should also match everything). The domain planner must evaluate whether this is a bug and fix it if so.

#### Why this is needed

Dynamic filter building from user inputs (optional date ranges, status filters, category filters) is one of the most common specification use cases in CQRS read models. Without sentinels, every team writes defensive null-check code before composing specifications. With sentinels, the pattern becomes idiomatic and safe: start with `AllSpecification<T>`, chain `.And(filter)` for each active filter, pass the result to the repository. The behavior is mathematically correct (AND with "all" = the filter; OR with "none" = the filter) and eliminates the null-check guard pattern entirely. Documenting the existing null-criteria handling behavior in `And`/`Or` closes a latent bug risk — if the current behavior for `Or` with a null-criteria operand is wrong, it must be fixed now before P-033 (persistence layer) builds on it.

#### Acceptance criteria
- [ ] `AllSpecification<T>` sealed class exists; `Criteria` is `null`; `IsSatisfiedBy` returns `true` for any entity
- [ ] `EmptySpecification<T>` sealed class exists; `Criteria` is `_ => false`; `IsSatisfiedBy` returns `false` for any entity
- [ ] Both are concrete (not abstract) and instantiable without subclassing
- [ ] XML doc on both classes documents the composition identity pattern with a code example
- [ ] `OrSpecification<T>` null-criteria handling is evaluated and corrected if incorrect: if either operand has `null` criteria (matches everything), the OR result should also have `null` criteria (matches everything)
- [ ] `AndSpecification<T>` null-criteria handling is verified correct and documented: if one operand has `null` criteria, the AND takes the other operand's criteria
- [ ] Tests: sentinels match/reject all entities; `And` / `Or` composition with sentinels behaves as identity elements; `Or` null-criteria fix (if any) verified
- [ ] All existing specification tests continue to pass
- [ ] Package remains AOT-safe
---

---
### P-051 — Domain: PagedSpecification<T> — Convenience Base for Paged Query Specifications

**Status:** `●` Complete
**Work Order:** WO-011
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

A `PagedSpecification<T>` abstract class that extends `ReadOnlySpecification<T>` (inheriting `AsNoTracking = true`) and automatically applies paging in its constructor.

The constructor signature: `protected PagedSpecification(int page, int pageSize)` where:
- `page` is 1-based (page 1 = first page)
- `pageSize` is the number of results per page
- The constructor calls `ApplyPaging(skip: (page - 1) * pageSize, take: pageSize)` automatically

Validation guards in the constructor:
- `page < 1` → throws `ArgumentOutOfRangeException`
- `pageSize < 1` → throws `ArgumentOutOfRangeException`
- `pageSize > MaxPageSize` → throws `ArgumentOutOfRangeException` with a message naming `MaxPageSize`

`MaxPageSize` must be a `protected const int` with a default value of `1000`. Subclasses may shadow it with their own `const` to enforce a lower maximum (e.g., `new const int MaxPageSize = 50` for a list endpoint with a known upper bound).

The class exposes `public int Page { get; }` and `public int PageSize { get; }` as readable properties for convenience (e.g., for building `PagedList<T>` responses in the application layer).

A `PagedReadOnlySpecification<T>` alias name is not needed — `PagedSpecification<T>` inheriting from `ReadOnlySpecification<T>` makes the read-only intent clear.

XML doc must clarify: "All paged specifications are implicitly read-only (`AsNoTracking = true`). Paged queries are never followed by write operations — if a write operation needs to find and modify entities with paging, it must not use this base and must not apply `AsNoTracking`."

Tests: correct `Skip`/`Take` computed for `page=1, pageSize=10`; `page=3, pageSize=20`; invalid inputs throw `ArgumentOutOfRangeException`; `AsNoTracking` is `true` always; `Page` and `PageSize` properties read correctly.

#### Why this is needed

`(page - 1) * pageSize` is a formula every developer has typed hundreds of times. It is trivial but wrong when `page` is 0-based (a common off-by-one error teams make when mixing 0-based and 1-based pagination conventions). Centralizing this in `PagedSpecification<T>` with an explicit 1-based convention and guard clauses eliminates the entire class of "page 0 returns the first 20 items but page 1 also returns the first 20 items" bugs that appear in production APIs. The `MaxPageSize` guard prevents API abuse (requesting 100,000 items in one page) without requiring each team to add their own limit. Making `PagedSpecification<T>` extend `ReadOnlySpecification<T>` is architecturally correct — paged queries are always read queries.

#### Acceptance criteria
- [ ] `PagedSpecification<T>` abstract class exists in `Specifications/`; extends `ReadOnlySpecification<T>`
- [ ] Constructor `protected PagedSpecification(int page, int pageSize)` calls `ApplyPaging(skip: (page - 1) * pageSize, take: pageSize)`
- [ ] `ArgumentOutOfRangeException` thrown for `page < 1`, `pageSize < 1`, and `pageSize > MaxPageSize`
- [ ] `MaxPageSize` is `protected const int MaxPageSize = 1000`; subclasses may shadow with a lower value
- [ ] `Page { get; }` and `PageSize { get; }` properties are publicly readable
- [ ] `AsNoTracking` is always `true` (inherited from `ReadOnlySpecification<T>`)
- [ ] XML doc states the 1-based page convention, the `MaxPageSize` guard, and the read-only rationale
- [ ] Tests: correct Skip/Take for various page/pageSize inputs; guard throws; `Page`/`PageSize` readable; `AsNoTracking = true`
- [ ] All existing specification tests continue to pass — additive change
- [ ] Package remains AOT-safe
---

---
### P-052 — Domain: Specification Builder Ergonomics — ApplyThenByDescending Alias + Ordering Documentation

**Status:** `●` Complete
**Work Order:** WO-011
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

The current `Specification<T>` builder has `ApplyThenBy(Expression<Func<T, object>> keySelector, bool descending)` — a single method with a boolean flag for direction. This is correct but the `bool descending` parameter is easy to get wrong (passing `true` when you mean ascending). Adding a named alias for the descending case follows C# conventions (e.g., `OrderBy`/`OrderByDescending` in LINQ):

**Add `ApplyThenByDescending(Expression<Func<T, object>> keySelector)`** as a protected builder method that delegates to `ApplyThenBy(keySelector, descending: true)`.

This is a two-line addition but meaningfully improves readability:
```csharp
// Before (bool flag — easy to confuse):
ApplyThenBy(x => x.Name, true);

// After (self-documenting):
ApplyThenByDescending(x => x.Name);
```

Additionally, the `ISpecification<T>` documentation must be updated with a `<remarks>` block explicitly documenting the ordering precedence:
1. Primary sort: either `OrderBy` or `OrderByDescending` (mutually exclusive — last call wins)
2. Secondary sorts: `ThenBys` list, applied in order of `ApplyThenBy` / `ApplyThenByDescending` calls
3. If neither primary sort is set: `ThenBys` entries are ignored by well-behaved repositories

The `Specification<T>` documentation must state that calling both `ApplyOrderBy` and `ApplyOrderByDescending` is not an error at the domain level — the last call wins — but produces unexpected behavior and should be avoided. A governance rule (added in P-056) should flag this pattern.

Tests: `ApplyThenByDescending(expr)` adds an entry to `ThenBys` with `Descending = true`; sequential calls to `ApplyThenBy` and `ApplyThenByDescending` produce `ThenBys` in the correct order with the correct direction flags.

#### Why this is needed

The `bool descending` flag on `ApplyThenBy` is a minor but real ergonomic gap. In every other ordering API in the .NET ecosystem (LINQ, EF Core, MongoDB, Elasticsearch), the ascending/descending distinction is expressed as separate named methods rather than a boolean flag. Consistency with ecosystem conventions reduces cognitive overhead for teams adopting the SharedKernel specification pattern.

#### Acceptance criteria
- [ ] `protected void ApplyThenByDescending(Expression<Func<T, object>> keySelector)` method added to `Specification<T>`; delegates to `ApplyThenBy(keySelector, descending: true)`
- [ ] `ISpecification<T>` gains a `<remarks>` block documenting ordering precedence and the `ThenBys` list behavior when no primary sort is set
- [ ] `Specification<T>` XML doc states that calling both `ApplyOrderBy` and `ApplyOrderByDescending` is a soft violation
- [ ] Tests: `ApplyThenByDescending` produces correct `ThenBys` entry; mixed `ApplyThenBy`/`ApplyThenByDescending` calls produce correct direction flags in order
- [ ] All existing specification tests continue to pass — additive change
- [ ] Package remains AOT-safe
---

---
### P-053 — Domain: DomainEventVersion Attribute — Schema Versioning for Domain Events

**Status:** `●` Complete
**Work Order:** WO-011
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

When a domain event record's shape changes (new required field, renamed property, type change), consumers of that event — particularly integration event handlers in other services — face a deserialization mismatch. There is currently no standard mechanism in `03.Domain` to signal that an event type has a schema version.

A `[DomainEventVersion(int version)]` attribute must be added to `03.Domain/Events/`:
- `DomainEventVersionAttribute` inherits from `Attribute`
- Constructor: `public DomainEventVersionAttribute(int version)` where `version >= 1` (validated at construction; `ArgumentOutOfRangeException` if `version < 1`)
- Exposes `public int Version { get; }` as a readable property
- Is applied at the class/record level: `[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]`
- XML doc states: "Declares the schema version of this domain event record. Increment when adding, removing, or renaming properties. Version 1 is implied when the attribute is absent. Consuming services must handle event deserialization for the declared version."

Additionally, an `IDomainEvent` interface extension is **not appropriate** — adding `Version` to the interface would force all domain events to declare a version at runtime, adding allocation and coupling. The attribute approach is the correct .NET pattern: checked at compile time via reflection in governance tests, not at runtime in the hot path.

A static helper `DomainEventVersionHelper.GetVersion(Type domainEventType)` must be provided:
- Returns the `Version` from `DomainEventVersionAttribute` if present; returns `1` as default when the attribute is absent
- This helper is for use in `07.Messaging` serialization and governance tests, not for use in the domain layer itself

Tests: attribute applies to a domain event record; `GetVersion` returns declared version; `GetVersion` returns 1 when attribute is absent; version `< 1` throws `ArgumentOutOfRangeException`.

`CLAUDE.md` must document: "Apply `[DomainEventVersion(N)]` when changing an event's schema. Increment N on every breaking schema change. A missing attribute implies version 1. Use `DomainEventVersionHelper.GetVersion(type)` in messaging and governance layers to inspect the version."

#### Why this is needed

Domain events are the cross-service integration contract of the platform. Without schema versioning, a team that adds a required field to `OrderPlacedEvent` silently breaks all services that deserialize it without the new field. The `[DomainEventVersion]` attribute makes schema evolution explicit and auditable: code review sees the version increment and forces reviewers to consider backward compatibility. Governance architecture tests (P-056) can enforce that no `IDomainEvent` implementor changes its structure without an updated `[DomainEventVersion]` attribute (checked via git diff in CI). The static `DomainEventVersionHelper` gives the messaging layer a standardized way to read the declared version for routing event payloads to versioned deserializers.

#### Acceptance criteria
- [ ] `DomainEventVersionAttribute` class exists in `Events/`; `AttributeUsage` is `Class, Inherited = false, AllowMultiple = false`
- [ ] Constructor validates `version >= 1`; throws `ArgumentOutOfRangeException` for `version < 1`
- [ ] `public int Version { get; }` is readable after construction
- [ ] `DomainEventVersionHelper.GetVersion(Type)` static helper exists; returns declared version or `1` as default
- [ ] `IDomainEvent` interface is unchanged — no runtime `Version` property added
- [ ] XML doc on `DomainEventVersionAttribute` documents the versioning contract and deployment workflow
- [ ] Tests: attribute declaration; `GetVersion` with and without attribute; invalid version guard
- [ ] `03.Domain/CLAUDE.md` events section documents the attribute usage and versioning workflow
- [ ] Package remains AOT-safe (attribute reading uses reflection, but only in `DomainEventVersionHelper` which is for governance/messaging, not the domain hot path)
---

---
### P-054 — Domain: Aggregate Result Factory Pattern — IAggregateFactory<T> Convention and Result<T>-Returning Create

**Status:** `●` Complete
**Work Order:** WO-011
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

The platform uses `Result<T>` from `SharedKernel.Primitives` for railway-oriented programming. Domain aggregates currently either throw exceptions from their constructors (via `CheckRule`) or from `ValueObject.Validate()`. For application-layer callers that want railway-style error propagation without try/catch, there is no standard pattern for aggregate creation.

Two additions are needed:

**`IAggregateFactory<TAggregateRoot, TId>` marker interface:**
A zero-member marker interface (like `IDomainService`) that identifies a class as a factory for a specific aggregate type. Implementing this interface is optional convention — its primary use is for governance architecture tests that can enforce "every aggregate has a factory" if the team wants that rule.

```csharp
public interface IAggregateFactory<TAggregateRoot, TId>
    where TAggregateRoot : IAggregateRoot<TId>
    where TId : notnull
{
}
```

**`AggregateRoot<TId>.TryCreate` documentation pattern:**
Rather than shipping a concrete abstract `static Result<TAggregateRoot> Create(...)` method (which C# does not support as abstract statics in the same way for instance factories), the domain must document a standard convention:
- Every aggregate that needs railway-safe creation should expose a `public static Result<TSelf> Create(...params...)` factory method
- This factory method wraps the aggregate constructor in a try/catch that catches `BusinessRuleViolationException` and `ValidationException` and converts them to `Result.Failure(error)`
- `AggregateRoot<TId>` must provide a `protected static Result<T> TryCreate<T>(Func<T> factory)` helper that performs this wrapping so aggregates don't duplicate the try/catch pattern

The helper signature:
```
protected static Result<T> TryCreate<T>(Func<T> factory)
    catches BusinessRuleViolationException → Result.Failure(exception.Error)
    catches ValidationException → Result.Failure(ValidationResult with errors)
    returns Result.Success(factory())
```

This `TryCreate` helper eliminates per-aggregate try/catch boilerplate while keeping the `static Result<T> Create(...)` pattern at the aggregate level (not enforced by the base class).

`CLAUDE.md` must document this as a convention (not a hard rule) with an example showing a `static Result<Order> Create(OrderId id, string customerName, IClock clock)` factory method that calls `TryCreate(() => new Order(id, customerName, clock))`.

Tests: `TryCreate` returns `Success` when the constructor succeeds; returns `Failure` with `ErrorType.BusinessRule` when the constructor calls `CheckRule` with a broken rule; returns `Failure` with `ErrorType.Validation` when the constructor throws `ValidationException`.

#### Why this is needed

The exception-based path for domain invariants (`CheckRule` throwing `BusinessRuleViolationException`) is correct for enforcing invariants that must never be violated. But aggregate creation from application command handlers benefits from railway-style error propagation: `Result<Order> result = Order.Create(id, name, clock); if (result.IsFailure) return result;`. Without `TryCreate`, every aggregate team writes their own try/catch wrapper — producing inconsistent error structures and missing the correct `ErrorType` classification. The `IAggregateFactory<T>` interface gives governance a handle on factory types without constraining how they are implemented. Together these give teams a standard, idiomatic, railway-friendly aggregate creation pattern without changing the domain's core exception model.

#### Acceptance criteria
- [ ] `IAggregateFactory<TAggregateRoot, TId>` zero-member marker interface exists in `Abstractions/` with correct generic constraints
- [ ] `protected static Result<T> TryCreate<T>(Func<T> factory)` method exists on `AggregateRoot<TId>`; catches `BusinessRuleViolationException` → `Result.Failure(ex.Error)`; catches `ValidationException` → `Result.Failure(ex.Errors.First())` (or a composite error if `ValidationResult` is appropriate); wraps in `try/catch`
- [ ] `03.Domain/CLAUDE.md` documents the `static Result<T> Create(...)` convention as a recommended (not required) pattern; includes a complete code example
- [ ] Tests: `TryCreate` success path; `TryCreate` `BusinessRuleViolationException` produces `Failure` with `ErrorType.BusinessRule`; `TryCreate` `ValidationException` produces `Failure`
- [ ] All existing tests continue to pass — additive change
- [ ] Package remains AOT-safe (no reflection in `TryCreate`)
---

---
### P-055 — Contracts: EventEnvelope<TEvent> — Transport Metadata Wrapper for Domain Events

**Status:** `○` Pending
**Work Order:** WO-011
**Domain:** 04.Contracts
**Depends on:** P-053

#### What is needed

Domain events carry `Id` and `OccurredOn` — intrinsic domain properties. Transport concerns — correlation tracing, causation chains, schema versioning, routing keys — must not pollute the domain event model. They belong in a wrapper type at the contracts boundary.

An `EventEnvelope<TEvent>` sealed record must be added to `04.Contracts`:

```
EventEnvelope<TEvent>  where TEvent : IDomainEvent
    .EventId          → Guid           (copy of TEvent.Id)
    .OccurredOn       → DateTimeOffset  (copy of TEvent.OccurredOn)
    .EventType        → string          (full type name of TEvent, e.g. "OrderPlacedEvent")
    .EventVersion     → int             (from DomainEventVersionAttribute on TEvent, or 1 if absent)
    .CorrelationId    → string?         (set from ambient OTel ActivityContext or caller-provided; null if unavailable)
    .CausationId      → string?         (ID of the command or event that caused this event; null if unavailable)
    .SourceService    → string          (name of the service that raised this event; set at composition root)
    .Payload          → TEvent          (the wrapped domain event)
```

A static `EventEnvelope.Wrap<TEvent>(TEvent domainEvent, string sourceService, string? correlationId = null, string? causationId = null)` factory method must be provided that populates all fields from the event and metadata.

`EventEnvelope<TEvent>` must be STJ-serializable. A `JsonSerializerContext` source-generated entry must be provided for the common case of `EventEnvelope<DomainEvent>` (the non-generic base). Strongly-typed `EventEnvelope<OrderPlacedEvent>` requires the consuming service's STJ context — document this.

`04.Contracts` may reference `03.Domain` per layering rules (Contracts references Core + Domain). The reference to `IDomainEvent` and `DomainEventVersionHelper` from `03.Domain` is therefore permitted.

`CLAUDE.md` must be updated: the "What Goes Where" table must gain a row for `EventEnvelope<TEvent>` pointing to `04.Contracts`.

#### Why this is needed

CorrelationId and CausationId are universally needed for distributed tracing across services, but they are transport/infrastructure concerns — not domain concerns. Placing them on `IDomainEvent` or `DomainEvent` base would require every domain aggregate to know about distributed trace context, creating a coupling between domain logic and infrastructure metadata. The `EventEnvelope<TEvent>` wrapper cleanly separates the two concerns: the domain event carries domain data, the envelope carries transport metadata. The `07.Messaging` layer wraps domain events in envelopes before publishing; handlers in other services unwrap the envelope to access the domain event. This pattern is standard in event-driven architectures (CloudEvents, AMQP headers, MassTransit's `MessageContext`). The `SourceService` and schema version fields enable multi-service event routing and versioned deserialization without polling the event store for metadata.

#### Acceptance criteria
- [ ] `EventEnvelope<TEvent>` sealed record exists in `04.Contracts`; constrained to `TEvent : IDomainEvent`; all eight properties present as documented
- [ ] `EventEnvelope.Wrap<TEvent>(TEvent, string sourceService, string? correlationId, string? causationId)` static factory method exists; populates all fields correctly; `EventVersion` uses `DomainEventVersionHelper.GetVersion(typeof(TEvent))`
- [ ] `EventEnvelope<TEvent>` is STJ-serializable; a source-generated context entry is provided for `EventEnvelope<DomainEvent>` at minimum
- [ ] `04.Contracts` references `03.Domain` — this is permitted by layering rules; no new cross-domain layering violations introduced
- [ ] Root `CLAUDE.md` "What Goes Where" table gains `EventEnvelope<TEvent>` row pointing to `04.Contracts`
- [ ] Tests: `Wrap` factory populates all fields; `EventVersion` defaults to 1 when attribute absent; `EventVersion` uses declared version when attribute present; `CorrelationId`/`CausationId` are null when not provided
- [ ] All public types carry XML doc comments
- [ ] Package remains AOT-safe; STJ context is source-generated
---

---
### P-056 — Governance: Domain Gold-Standard Architecture Rules

**Status:** `○` Pending
**Work Order:** WO-011
**Domain:** 00.Governance
**Depends on:** P-045, P-047, P-053

#### What is needed

New architecture enforcement rules in `00.Governance/SharedKernel.ArchitectureTests` that protect the new domain contracts introduced in WO-011:

**Rule 1 — All `IDomainService` implementors must extend `DomainService` abstract class (not implement `IDomainService` directly):**
NetArchTest rule: any type implementing `IDomainService` that does NOT extend `DomainService` fails. This enforces P-047's mandate that domain services use the abstract base for `CheckRule` access and DI anchor. Exemption: `DomainService` itself.

**Rule 2 — Infrastructure dispatch code must depend on `IHasDomainEvents`, not `IAggregateRoot<TId>`:**
A documentation-level rule (not enforceable by NetArchTest alone without significant IL inspection). A Roslyn analyzer that warns when a type in a namespace containing `Interceptor`, `Publisher`, `Outbox`, or `Dispatcher` injects a parameter typed as `IAggregateRoot<>` rather than `IHasDomainEvents`. This enforces P-045's intent.

**Rule 3 — Domain event schema changes must carry a `[DomainEventVersion]` attribute increment:**
A CI-level governance check (Git diff-based): any modification to a type implementing `IDomainEvent` that adds, removes, or renames a property must carry a corresponding update to its `[DomainEventVersion]` attribute. This is checked via a post-build analyzer or a custom Git hook documented in `00.Governance`. The Roslyn analyzer approach: warn when a `IDomainEvent`-implementing type lacks `[DomainEventVersion]` entirely (not when the version is stale — that requires git diff).

**Rule 4 — `AggregateRoot<TId>` subclasses must not call both `ApplyOrderBy` and `ApplyOrderByDescending` in the same constructor:**
A Roslyn analyzer that detects specification constructors calling both primary ordering methods — a potential ordering conflict that produces non-deterministic sort results.

**Rule documentation:** All four rules must be documented in `00.Governance/CLAUDE.md` with rationale, offending-pattern example, and compliant-pattern example.

#### Why this is needed

Architecture rules without enforcement are suggestions. WO-011 introduces several conventions that will be violated by well-meaning developers who haven't read the CLAUDE.md. Rule 1 ensures domain services always have `CheckRule` access without copy-pasting. Rule 2 prevents infrastructure code from coupling to aggregate identity unnecessarily. Rule 3 forces schema versioning discipline — the most common source of cross-service event deserialization failures. Rule 4 catches an ordering ambiguity that would silently return non-deterministic results from queries. All four rules have near-zero false-positive rates when properly scoped to the relevant namespaces and type hierarchies.

#### Acceptance criteria
- [ ] Rule 1 NetArchTest: `IDomainService` implementors not extending `DomainService` are detected and fail with an identifying message
- [ ] Rule 2 Roslyn analyzer: injection of `IAggregateRoot<>` in dispatch-named contexts produces a warning with a link to `IHasDomainEvents`
- [ ] Rule 3 Roslyn analyzer: `IDomainEvent` implementing type lacking `[DomainEventVersion]` produces a warning
- [ ] Rule 4 Roslyn analyzer: specification constructors calling both `ApplyOrderBy` and `ApplyOrderByDescending` produce a warning
- [ ] All four rules documented in `00.Governance/CLAUDE.md` with rationale and examples
- [ ] Governance test suite passes with all new rules; each rule has a fixture test demonstrating it fires correctly
---

---
### P-057 — Testing: Domain Gold-Standard Test Helpers — Extended Fakers and Assertion Extensions

**Status:** `○` Pending
**Work Order:** WO-011
**Domain:** 16.Testing
**Depends on:** P-045, P-046, P-048, P-050, P-054

#### What is needed

Extended test helpers in `16.Testing/SharedKernel.Testing` for the new capabilities introduced in WO-011:

**`SingleValueObjectFaker<TValueObject, TValue>` abstract base:**
A Bogus-based abstract faker for `SingleValueObject<TValue>` subclasses. Exposes a `WithValue(TValue value)` builder method and a `WithRandomValue(Func<Faker, TValue> generator)` method for generating randomized valid values. Reduces the boilerplate of writing a Bogus faker for every single-primitive value object.

**`DomainVersionAssertions` — schema version assertion helpers:**
- `ShouldHaveVersion<TEvent>(int expectedVersion)` — asserts that `TEvent` (an `IDomainEvent` implementor) carries `[DomainEventVersion(N)]` where `N == expectedVersion`. Throws with a descriptive message on failure.
- `ShouldBeVersioned<TEvent>()` — asserts that `TEvent` carries any `[DomainEventVersion]` attribute (i.e., the team has not forgotten to version their event).

**Extended `DomainEventAssertions`:**
Extend the existing `DomainEventAssertions` from P-035 with:
- `ContainsEventWithVersion<T>(int version)` — asserts at least one event of type `T` is present AND `T` is annotated with `[DomainEventVersion(version)]`
- `HasRaisedExactlyNEvents(int n)` — asserts the total event count (not per-type) is exactly `n`; useful for invariant tests that must confirm no unexpected side-effect events were raised

**`SpecificationTestBuilder<T>` — in-memory specification test helper:**
A fluent builder for testing specifications against an in-memory collection:
```
SpecificationTestBuilder.For(spec)
    .Against(entities)        // IEnumerable<T>
    .ExpectCount(n)           // asserts n entities satisfy the spec
    .ExpectMatch(predicate)   // asserts all returned entities satisfy additional predicates
    .Assert()
```
This is a more ergonomic wrapper around `spec.IsSatisfiedBy(entity)` that handles the collection-level assertions with better failure messages.

**`FakeDomainNotFoundException` helper:**
A factory method `FakeDomainNotFoundException.For<TAggregate>(object id)` that creates a `DomainNotFoundException` (from P-049) for use in test setups where a repository mock or fake needs to throw a not-found exception.

All new helpers must be added to the `16.Testing/SharedKernel.Testing` package under appropriate namespaces. `AddFakeDomainServices()` must be updated to register any new DI-registered helpers.

#### Why this is needed

Test infrastructure must keep pace with domain capability. Without `SingleValueObjectFaker`, every service team writes its own Bogus configuration for their value objects — inconsistent, duplicated, and often wrong (using values that happen to pass validation rather than explicitly testing the boundaries). Without `DomainVersionAssertions`, teams have no automated way to assert that their event schema versions are declared and correct — the gap between "I meant to version this event" and "I actually annotated it" is a real production risk. The `SpecificationTestBuilder` provides a dramatically improved developer experience for specification unit testing compared to calling `IsSatisfiedBy` in a loop with manual count assertions. These helpers directly reduce the time cost of writing thorough domain tests — which increases coverage and reduces the risk of domain regressions.

#### Acceptance criteria
- [ ] `SingleValueObjectFaker<TValueObject, TValue>` abstract base exists; `WithValue`/`WithRandomValue` builders work correctly
- [ ] `DomainVersionAssertions.ShouldHaveVersion<TEvent>` and `ShouldBeVersioned<TEvent>` throw with descriptive messages when assertions fail
- [ ] `DomainEventAssertions` extended with `ContainsEventWithVersion<T>` and `HasRaisedExactlyNEvents`
- [ ] `SpecificationTestBuilder<T>` fluent API works correctly; `ExpectCount` and `ExpectMatch` throw on failure with entity details
- [ ] `FakeDomainNotFoundException.For<TAggregate>(object id)` factory method exists and produces a valid `DomainNotFoundException`
- [ ] `AddFakeDomainServices()` updated where applicable
- [ ] All test helpers have their own unit tests in `SharedKernel.Testing.Tests`
- [ ] `16.Testing` references `SharedKernel.Domain` (permitted) and `04.Contracts` (permitted) for `EventEnvelope` helper support
- [ ] All public types carry XML doc comments
---
