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

_Nothing in progress — all domains at ○ Not Started._

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
| 01 | [Core](01.Core/state-map.md) | Published | `●` | All 9 Published tasks complete — NuGet metadata on all five packages, all packed to local feed, consumer verification confirms Primitives + Core + Guards transitive dependency graph resolves correctly. | — |
| 02 | [Caching](02.Caching/state-map.md) | Phase 31 (OTel Metrics) | `●` | Phase 31 complete — static Meter + 5 instruments in FusionCacheService; FusionCache events for hit/miss/eviction; factory Stopwatch; 209 FusionCache + 154 Redis tests passing. | — |
| 03 | [Domain](03.Domain/state-map.md) | Design | `●` | All 14 design tasks verified against CLAUDE.md — interfaces, equality strategy, event contracts, audit hierarchy, business rules, policies, specifications, and IDomainEventHandler exclusion boundary documented. | Begin Scaffold phase: create csproj, directory structure, and solution registration. |
| 04 | [Contracts](04.Contracts/state-map.md) | — | `○` | — | — |
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
| ● Published | 1 |
| ● Docs | 0 |
| ● Tests | 0 |
| ● Core | 0 |
| ● Scaffold | 0 |
| ● Design | 1 |
| ◐ In Progress | 0 |
| ⚑ Blocked | 0 |
| ○ Not Started | 14 |

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

**Status:** `◐` Dispatched
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

**Status:** `○` Pending
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
