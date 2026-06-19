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

| Domain                                                | Current Phase   | Focus (one line)                                                                                                                                 |
|-------------------------------------------------------|-----------------|--------------------------------------------------------------------------------------------------------------------------------------------------|
| [04.Contracts](04.Contracts/state-map.md)             | Design          | Add ResultEnvelopeExtensions static class with ToEnvelope/ToResult bridge methods between Result<T> and Envelope<T> in SharedKernel.Contracts.Mapping namespace |
| [16.Testing](16.Testing/state-map.md)                 | Design          | Add EfCore test DbContext base, persistence-aware aggregate fakers, and EfCore assertion helpers to SharedKernel.Testing                         |

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
| 00 | [Governance](00.Governance/state-map.md) | Governance: Architecture Rules for WO-026 Communication Quality Improvements | `●` | All 5 tasks complete — GrpcNeverReferencesContracts added to CommunicationLayeringRules, locking the P-163 dead-reference removal permanently; 88/88 architecture tests pass. | — |
| 01 | [Core](01.Core/state-map.md) | P-042 Error.BusinessRule Factory | `●` | ErrorType.BusinessRule enum member, Error.BusinessRule factory, and ErrorCodes.Domain.RuleViolated added to SharedKernel.Primitives; 56 Primitives + 65 Core tests passing. | — |
| 02 | [Caching](02.Caching/state-map.md) | Phase 36 (Redis Pub/Sub and Invalidation Package Extraction) | `●` | Phase 36 complete — ephemeral Redis Pub/Sub signaling and cache invalidation (RedisChannelService, RedisCacheInvalidationBus, CacheInvalidationReceiver, AddRedisChannelService, AddRedisCacheInvalidationBus, AddCacheInvalidationReceiver) extracted from SharedKernel.Caching.Redis into new package SharedKernel.Caching.Redis.PubSub, depending only on SharedKernel.Caching.Abstractions + SharedKernel.Caching.Redis.Core; SharedKernel.Caching.Redis slimmed to its L2-only end state; 28 Redis + 41 Redis.DistributedLocking + 30 Redis.HashStore + 33 Redis.Core + 41 Redis.PubSub tests passing. WO-023 (Redis package split, Phases 32-36) fully complete. | — |
| 03 | [Domain](03.Domain/state-map.md) | Published | `●` | SK.03.Published complete (10/10) — SharedKernel.Domain 1.6.0 packed and verified (manifest deps: SharedKernel.Core + SharedKernel.Primitives only); StronglyTypedIdJsonConverterFactory/Converter confirmed exported via consumer-verify (19/19 tests); 246 domain tests green; all 6 phases of 03.Domain now complete. | — |
| 04 | [Contracts](04.Contracts/state-map.md) | Design | `◐` | — | Add ResultEnvelopeExtensions static class with ToEnvelope/ToResult bridge methods between Result<T> and Envelope<T> in SharedKernel.Contracts.Mapping namespace |
| 05 | [Application](05.Application/state-map.md) | — | `○` | — | — |
| 06 | [Persistence](06.Persistence/state-map.md) | Published | `●` | All 4 packages packed and verified — PostgreSQL and Dapper NuGet metadata confirmed; 203 tests green across all four test projects; complete domain done. | — |
| 07 | [Messaging](07.Messaging/state-map.md) | RoutingSlip | `●` | SK.07.RoutingSlip complete (10/10) — IRoutingSlipBuilder + IMessageBus.ExecuteRoutingSlipAsync in Abstractions; RoutingSlipActivityBase<TArgs,TLog>, MassTransitRoutingSlipBuilder, AddRoutingSlipActivity<T>() in MassTransit; 101 total MassTransit tests green. | — |
| 08 | [Storage](08.Storage/state-map.md) | — | `○` | — | — |
| 09 | [Search](09.Search/state-map.md) | — | `○` | — | — |
| 10 | [Intelligence](10.Intelligence/state-map.md) | — | `○` | — | — |
| 11 | [Communication](11.Communication/state-map.md) | Tests | `●` | T-01–T-26 complete — 203/203 tests passing across Rest (66), Grpc (55), GraphQL (43), Internal (39); all handler, interceptor, resilience, filter, and resolver tests green. | Begin Docs phase (XML doc comments across all four packages). |
| 12 | [Security](12.Security/state-map.md) | Published | `●` | Both packages packed to `.nupkg` + `.snupkg`; 13 Abstractions + 33 Oidc tests passing; full NuGet metadata present. | — |
| 13 | [ServiceDefaults](13.ServiceDefaults/state-map.md) | Scaffold | `●` | Scaffold phase (S-01–S-10) complete — real project/package references landed for SharedKernel.ServiceDefaults and SharedKernel.MultiTenancy (replacing the bare .csproj stubs), Extensions/HealthChecks/Telemetry/Probes and Resolution/Middleware/Extensions folder structures, nested .Tests projects referencing SharedKernel.Testing, .slnx registration; dotnet build verified clean across all four projects. | Begin Core phase (C-01–C-19) — AddServiceDefaults, liveness/readiness health check split, StartupGate, full SharedKernel.MultiTenancy resolution-strategy surface, and dependency-specific health check/telemetry extensions. |
| 14 | [Presentation](14.Presentation/state-map.md) | — | `○` | — | — |
| 15 | [Integration](15.Integration/state-map.md) | — | `○` | — | — |
| 16 | [Testing](16.Testing/state-map.md) | Design | `◐` | — | Add EfCore test DbContext base, persistence-aware aggregate fakers, and EfCore assertion helpers to SharedKernel.Testing |
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
| ● Phase 34 (Redis Distributed Locking Package Extraction) | 1 |
| ● P-042 Error.BusinessRule Factory | 1 |
| ● Published | 4 |
| ● RoutingSlip | 1 |
| ● Governance: Architecture Rules for WO-026 Communication Quality Improvements | 1 |
| ● Design | 1 |
| ● Docs | 0 |
| ● Tests | 1 |
| ● Core | 0 |
| ● Scaffold | 1 |
| ◐ In Progress | 2 |
| ⚑ Blocked | 0 |
| ○ Not Started | 7 |

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

**Status:** `●` Complete
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

**Status:** `◐` Dispatched
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

**Status:** `●` Complete
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

**Status:** `●` Complete
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

**Status:** `●` Complete
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

**Status:** `●` Complete
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

**Status:** `●` Complete
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

**Status:** `●` Complete
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

**Status:** `●` Complete
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

**Status:** `●` Complete
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

**Status:** `●` Complete
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

**Status:** `●` Complete
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

**Status:** `●` Complete
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

**Status:** `●` Complete
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

**Status:** `●` Complete
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

**Status:** `●` Complete
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

**Status:** `●` Complete
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
- [2026-05-30] Phase(s) P-056, P-063 dispatched to governance-arch-planner for 00.Governance (dispatch-phase)
- [2026-05-30] Phase(s) P-055, P-058, P-059, P-060, P-061, P-062 dispatched to contracts-arch-planner for 04.Contracts (dispatch-phase)
- [2026-05-30] Contracts → Design (●) — promoted from SK.04.Design (state-map-phase)
- [2026-05-30] Contracts → Scaffold (●) — promoted from SK.04.Scaffold (state-map-phase)
- [2026-05-30] Contracts → Core (●) — promoted from SK.04.Core (state-map-phase)
- [2026-05-30] Contracts → Tests (●) — promoted from SK.04.Tests (state-map-phase)
- [2026-05-30] Contracts → Docs (●) — promoted from SK.04.Docs (state-map-phase)
- [2026-05-30] Contracts → Published (●) — promoted from SK.04.Published (state-map-phase)
- [2026-05-30] Phase Backlog entries for 04.Contracts closed → ● Complete — 04.Contracts reached Published (state-map-phase)
- [2026-05-31] Governance → Design (●) — promoted from SK.00.Design (state-map-phase)
- [2026-05-31] Governance → Core (●) — promoted from SK.00.Core (state-map-phase)
- [2026-06-01] 06.Persistence → Design (◐) — Define complete public surface for all four persistence packages and scaffold csproj wiring (state-map-phase)
- [2026-06-01] 00.Governance → Design (◐) — Add five persistence architecture enforcement rules to ArchitectureTests (state-map-phase)
- [2026-06-01] 16.Testing → Design (◐) — Add PostgreSQL Testcontainer fixture, EfCore test DbContext base, persistence-aware fakers, outbox assertions (state-map-phase)
- [2026-06-01] Phase(s) P-075 dispatched to governance-arch-planner for 00.Governance (dispatch-phase)
- [2026-06-01] Phase(s) P-033, P-065, P-066, P-067, P-068, P-069, P-070, P-073, P-074 dispatched to persistence-arch-planner for 06.Persistence (dispatch-phase)
- [2026-06-01] Persistence → Design (●) — promoted from SK.06.Design (state-map-phase)
- [2026-06-01] Persistence → Scaffold (●) — promoted from SK.06.Scaffold (state-map-phase)
- [2026-06-01] Persistence → Core (●) — promoted from SK.06.Core (state-map-phase)
- [2026-06-01] Persistence → Tests (●) — promoted from SK.06.Tests (state-map-phase)
- [2026-06-01] Persistence → Docs (●) — promoted from SK.06.Docs (state-map-phase)
- [2026-06-01] Persistence → Published (●) — promoted from SK.06.Published (state-map-phase)
- [2026-06-01] Phase Backlog entries for 06.Persistence closed → ● Complete — 06.Persistence reached Published (state-map-phase)
- [2026-06-01] Security → Design (◐) — Create SharedKernel.Security.Abstractions with IUserContext and ITenantProvider (state-map-phase)
- [2026-06-02] Security → Design (●) — promoted from SK.12.Design (state-map-phase)
- [2026-06-02] Security → Scaffold (●) — promoted from SK.12.Scaffold (state-map-phase)
- [2026-06-02] Security → Core (●) — promoted from SK.12.Core (state-map-phase)
- [2026-06-02] Security → Tests (●) — promoted from SK.12.Tests (state-map-phase)
- [2026-06-02] Security → Docs (●) — promoted from SK.12.Docs (state-map-phase)
- [2026-06-02] Security → Published (●) — promoted from SK.12.Published (state-map-phase)
- [2026-06-02] Phase Backlog entries for 12.Security closed → ● Complete — 12.Security reached Published (state-map-phase)
- [2026-06-02] Governance → Design (●) — promoted from SK.00.Design (state-map-phase)
- [2026-06-02] Phase(s) P-095 dispatched to domain-arch-planner for 03.Domain (dispatch-phase)
- [2026-06-02] Phase(s) P-078, P-079, P-082, P-091, P-092, P-093, P-094 dispatched to persistence-arch-planner for 06.Persistence (dispatch-phase)
- [2026-06-02] P-081 moved from 05.Application → 03.Domain: IDomainEventDispatcher interface belongs in Domain layer, not Application layer — eliminates 06.Persistence→05.Application coupling (arch-fix)
- [2026-06-02] P-080 Depends on updated: P-081 removed as Application dep (now in 03.Domain), P-095 added (IncludeDeleted prereq); P-080 unblocked (arch-fix)
- [2026-06-03] Persistence → Core (●) — promoted from SK.06.Core; 41/41 tasks complete; 112 tests green (state-map-phase)
- [2026-06-02] Phase(s) P-081 dispatched to domain-arch-planner for 03.Domain (dispatch-phase)
- [2026-06-02] Phase(s) P-080 dispatched to persistence-arch-planner for 06.Persistence (dispatch-phase)
- [2026-06-02] Phase(s) P-083, P-096 dispatched to governance-arch-planner for 00.Governance (dispatch-phase)
- [2026-06-02] Domain → Design (●) — promoted from SK.03.Design (state-map-phase)
- [2026-06-02] Domain → Core (●) — promoted from SK.03.Core (state-map-phase)
- [2026-06-02] Domain → Tests (●) — promoted from SK.03.Tests (state-map-phase)
- [2026-06-02] Domain → Docs (●) — promoted from SK.03.Docs (state-map-phase)
- [2026-06-02] Domain → Published (●) — promoted from SK.03.Published (state-map-phase)
- [2026-06-02] Phase Backlog entries for 03.Domain closed → ● Complete — 03.Domain reached Published (state-map-phase)
- [2026-06-02] Persistence → Design (●) — promoted from SK.06.Design; D-15..D-25 verified complete (state-map-phase)
- [2026-06-03] Persistence → Tests (●) — promoted from SK.06.Tests (state-map-phase)
- [2026-06-03] Persistence → Docs (●) — promoted from SK.06.Docs (state-map-phase)
- [2026-06-03] Governance → Core (●) — promoted from SK.00.Core (state-map-phase)
- [2026-06-03] Phase(s) P-097, P-098, P-099, P-100, P-101, P-102 dispatched to persistence-arch-planner for 06.Persistence (dispatch-phase)
- [2026-06-03] Persistence → Published (●) — promoted from SK.06.Design; WO-017 all 30 tasks complete, 158 tests green (state-map-phase)
- [2026-06-03] P-078, P-079, P-080, P-082, P-091, P-092, P-093, P-094, P-097, P-098, P-099, P-100, P-101, P-102 → ● Complete — Phase Backlog sync; all 06.Persistence phases confirmed ● in sub state-map; statuses were stuck at ◐ Dispatched (manual sync)
- [2026-06-03] Phase(s) P-103 dispatched to governance-arch-planner for 00.Governance (dispatch-phase)
- [2026-06-03] Phase(s) P-105, P-106, P-107, P-108, P-109 dispatched to persistence-arch-planner for 06.Persistence (dispatch-phase)
- [2026-06-04] Persistence → Design (●) — promoted from SK.06.Design; all 43 design tasks complete including WO-018 P-105..P-109 (state-map-phase)
- [2026-06-04] Persistence → Scaffold (●) — promoted from SK.06.Scaffold (state-map-phase)
- [2026-06-04] Persistence → Core (●) — promoted from SK.06.Core (state-map-phase)
- [2026-06-04] Persistence → Tests (●) — promoted from SK.06.Tests (state-map-phase)
- [2026-06-04] Persistence → Docs (●) — promoted from SK.06.Docs (state-map-phase)
- [2026-06-04] Persistence → Published (●) — promoted from SK.06.Published (state-map-phase)
- [2026-06-04] Phase Backlog entries for 06.Persistence closed → ● Complete — 06.Persistence reached Published (state-map-phase)
- [2026-06-04] Phase(s) P-110 dispatched to governance-arch-planner for 00.Governance (dispatch-phase)
- [2026-06-04] Phase(s) P-111, P-112, P-113 dispatched to persistence-arch-planner for 06.Persistence (dispatch-phase)
- [2026-06-04] Persistence → Design (●) — promoted from SK.06.Design; all 53 design tasks complete including WO-019 encryption subsystem (state-map-phase)
- [2026-06-04] Persistence → Core (●) — promoted from SK.06.Core; WO-019 encryption subsystem 81/81 tasks complete (state-map-phase)
- [2026-06-04] Phase(s) P-114 dispatched to governance-arch-planner for 00.Governance (dispatch-phase)
- [2026-06-04] Governance → Tests (●) — promoted from SK.00.Tests (state-map-phase)
- [2026-06-04] Governance → Docs (●) — promoted from SK.00.Docs (state-map-phase)
- [2026-06-04] Governance → Persistence Architecture Enforcement (●) — promoted from SK.00.PersistenceEnforcement (state-map-phase)
- [2026-06-05] Governance → Persistence Architecture Rules Phase 2 — Interface Migration Enforcement (●) — promoted from SK.00.PersistenceEnforcement2 (state-map-phase)
- [2026-06-05] Governance → Architecture Rule — IUserContext Audit String Adapter and Repository Contract Completeness (●) — promoted from SK.00.PersistenceContractCompleteness (state-map-phase)
- [2026-06-05] Governance → Governance: Architecture Rules for EfCore Package Hygiene (●) — promoted from SK.00.EfCorePackageHygiene (state-map-phase)
- [2026-06-05] Governance → Governance: TenantedDbContext Tenant-Filter Guard Architecture Rule (●) — promoted from SK.00.TenantedDbContextGuard (state-map-phase)
- [2026-06-05] Governance → Governance: Architecture Rules for DB Encryption Pattern Correctness (●) — promoted from SK.00.EncryptionPatternGuard (state-map-phase)
- [2026-06-05] Messaging → Scaffold (●) — promoted from SK.07.Scaffold (state-map-phase)
- [2026-06-05] Messaging → Core (●) — promoted from SK.07.Core (state-map-phase)
- [2026-06-08] Messaging → Tests (●) — promoted from SK.07.Tests (state-map-phase)
- [2026-06-08] Messaging → Docs (●) — promoted from SK.07.Docs (state-map-phase)
- [2026-06-08] Messaging → Published (●) — promoted from SK.07.Published (state-map-phase)
- [2026-06-08] Phase Backlog entries for 07.Messaging closed → ● Complete — 07.Messaging reached Published (state-map-phase)
- [2026-06-08] Messaging → Design (●) — promoted from SK.07.Design (state-map-phase)
- [2026-06-08] Messaging → Scaffold (●) — promoted from SK.07.Scaffold (state-map-phase)
- [2026-06-08] Messaging → Core (●) — promoted from SK.07.Core (state-map-phase)
- [2026-06-08] Messaging → Tests (●) — promoted from SK.07.Tests (state-map-phase)
- [2026-06-08] Messaging → Docs (●) — promoted from SK.07.Docs (state-map-phase)
- [2026-06-08] Messaging → Resilience (●) — promoted from SK.07.Resilience (state-map-phase)
- [2026-06-08] Messaging → Scheduling (●) — promoted from SK.07.Scheduling (state-map-phase)
- [2026-06-09] Messaging → Saga (●) — promoted from SK.07.Saga (state-map-phase)
- [2026-06-09] Messaging → Batch (●) — promoted from SK.07.Batch (state-map-phase)
- [2026-06-09] Messaging → Routing (●) — promoted from SK.07.Routing (state-map-phase)
- [2026-06-09] Governance → Governance: Messaging Architecture Rules (●) — promoted from SK.00.MessagingArchRules (state-map-phase)
- [2026-06-09] Messaging → Idempotency (●) — promoted from SK.07.Idempotency (state-map-phase)
- [2026-06-09] Messaging → HeaderPropagation (●) — promoted from SK.07.HeaderPropagation (state-map-phase)
- [2026-06-10] Messaging → VersionTranslation (●) — promoted from SK.07.VersionTranslation (state-map-phase)
- [2026-06-10] Governance → Governance: Extended Messaging Architecture Rules — Fault Consumers, Scheduling, Singleton Guards (●) — promoted from SK.00.ExtendedMessagingArchRules (state-map-phase)
- [2026-06-15] Domain → Published (●) — SK.03.Core completed (37/37, C-37 StronglyTypedIdJsonConverter/Factory); promoted from SK.03.Core (state-map-phase)
- [2026-06-16] Persistence → Docs (●) — SK.06.Docs 36/36 ●; DO-29..DO-36 XML docs complete for encryption, bulk mutation, streaming, readiness probes, seeding (state-map-phase)
- [2026-06-16] Communication → Design (●) — promoted from SK.11.Design (state-map-phase)
- [2026-06-16] Communication → Scaffold (●) — promoted from SK.11.Scaffold (state-map-phase)
- [2026-06-17] Communication → Rest (●) — promoted from SK.11.Rest (state-map-phase)
- [2026-06-17] Communication → Grpc (●) — promoted from SK.11.Grpc (state-map-phase)
- [2026-06-17] Phase Backlog P-156 → ● Complete — SK.11.Grpc done (state-map-phase)
- [2026-06-17] Communication → GraphQL (●) — promoted from SK.11.GraphQL (state-map-phase)
- [2026-06-17] Phase Backlog P-157 → ● Complete — SK.11.GraphQL done (state-map-phase)
- [2026-06-18] Communication → Internal (●) — promoted from SK.11.Internal (state-map-phase)
- [2026-06-18] Phase Backlog P-155 → ● Complete — SK.11.Internal done (state-map-phase)
- [2026-06-18] Communication → Rest (●) — R-11–R-18 WO-026 correctness fixes complete; promoted from SK.11.Rest (state-map-phase)
- [2026-06-18] Communication → Grpc (●) — G-10–G-13 complete; promoted from SK.11.Grpc (state-map-phase)
- [2026-06-18] Communication → GraphQL (●) — GQ-09 FromPagedList factory complete; 43/43 tests; promoted from SK.11.GraphQL (state-map-phase)
- [2026-06-19] Governance → Governance: Architecture Rules for WO-026 Communication Quality Improvements (●) — promoted from SK.00.WO026CommunicationQuality (state-map-phase)
- [2026-06-19] Phase Backlog P-167 → ● Complete — SK.00.WO026CommunicationQuality done (state-map-phase)
- [2026-06-19] 13 → Design (◐) — Scaffold real project references for ServiceDefaults/MultiTenancy, then AddServiceDefaults() with liveness/readiness split and StartupGate (state-map-phase)
- [2026-06-19] 13 → Scaffold (●) — promoted from SK.13.Scaffold (10/10); dotnet build verified clean across SharedKernel.ServiceDefaults, SharedKernel.MultiTenancy, and both nested .Tests projects (state-map-phase)
- [2026-06-19] Phase(s) P-173 dispatched to governance-arch-planner for 00.Governance (dispatch-phase)
- [2026-06-19] Phase(s) P-172 dispatched to messaging-arch-planner for 07.Messaging (dispatch-phase)
- [2026-06-19] Phase(s) P-010, P-122, P-132, P-169, P-170, P-171 dispatched to servicedefaults-arch-planner for 13.ServiceDefaults (dispatch-phase)
- [2026-06-19] 13 → Design (●) — promoted from SK.13.Design (state-map-phase)

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
- [2026-06-01] Persistence → Scaffold (●) — promoted from SK.06.Scaffold (state-map-phase)
- [2026-06-08] Phase(s) P-123 dispatched to governance-arch-planner for 00.Governance (dispatch-phase)
- [2026-06-08] Phase(s) P-125, P-126, P-127, P-128, P-129, P-130, P-131 dispatched to messaging-arch-planner for 07.Messaging (dispatch-phase)
- [2026-06-09] Phase(s) P-133 dispatched to governance-arch-planner for 00.Governance (dispatch-phase)
- [2026-06-09] Phase(s) P-134, P-135, P-136, P-137, P-139 dispatched to messaging-arch-planner for 07.Messaging (dispatch-phase)
- [2026-06-09] 07.Messaging → ConsumerDefinition (●) — promoted from SK.07.ConsumerDefinition (state-map-phase)
- [2026-06-10] Messaging → RoutingSlip (●) — promoted from SK.07.RoutingSlip (state-map-phase)
- [2026-06-10] Phase Backlog entries P-134, P-135, P-136, P-139 closed → ● Complete — WO-022 messaging phase backlog (Idempotency, HeaderPropagation, ConsumerDefinition, VersionTranslation, RoutingSlip) fully implemented (implement-phase-messaging)
- [2026-06-11] Phase(s) P-140, P-141, P-142, P-143, P-144 dispatched to caching-arch-planner for 02.Caching (dispatch-phase)
- [2026-06-11] Caching → Phase 32 (Redis Connection Core Extraction) (●) — promoted from SK.02.RedisConnectionCore (state-map-phase)
- [2026-06-11] Caching → Phase 33 (Redis L2 Backplane Package Refactor) (●) — promoted from SK.02.RedisL2Refactor (state-map-phase)
- [2026-06-11] Caching → Phase 34 (Redis Distributed Locking Package Extraction) (●) — promoted from SK.02.RedisLockingExtraction (state-map-phase)
- [2026-06-12] Caching → Phase 35 (Redis Hash Store Package Extraction) (●) — promoted from SK.02.RedisHashExtraction (state-map-phase)
- [2026-06-12] Caching → Phase 36 (Redis Pub/Sub and Invalidation Package Extraction) (●) — promoted from SK.02.RedisPubSubExtraction; WO-023 (Phases 32-36) fully complete (state-map-phase)
- [2026-06-12] Phase(s) P-145 dispatched to governance-arch-planner for 00.Governance (dispatch-phase)
- [2026-06-12] Phase(s) P-152 dispatched to domain-arch-planner for 03.Domain (dispatch-phase)
- [2026-06-12] Phase(s) P-147, P-148, P-149, P-150, P-151 dispatched to persistence-arch-planner for 06.Persistence (dispatch-phase)
- [2026-06-15] Governance → Architecture Rules for Redis Package Topology (●) — promoted from SK.00.RedisTopology (state-map-phase)
- [2026-06-15] Domain → Published (●) — promoted from SK.03.Tests; T-30 StronglyTypedIdJsonConverterTests complete, 246 tests green (state-map-phase)
- [2026-06-15] Domain → Published (●) — promoted from SK.03.Docs; DO-29 complete, csproj bumped to 1.6.0, 246 tests green (state-map-phase)
- [2026-06-15] Domain → Published (●) — promoted from SK.03.Published; P-10 complete, SharedKernel.Domain 1.6.0 packed and verified, all 6 phases of 03.Domain now complete (state-map-phase)
- [2026-06-15] Phase Backlog entries for 03.Domain closed → ● Complete — 03.Domain reached Published (state-map-phase)
- [2026-06-16] Persistence → Tests (●) — promoted from SK.06.Tests; T-40..T-54 complete, 245 tests green across all four test projects (state-map-phase)
- [2026-06-16] Communication → Design (◐) — Implement SharedKernel.Communication.Rest - Polly v8 resilience, CorrelationId/TenantId delegation handlers, ProblemDetails deserialization (state-map-phase)
- [2026-06-16] Phase(s) P-153 dispatched to governance-arch-planner for 00.Governance (dispatch-phase)
- [2026-06-16] Phase(s) P-154, P-155, P-156, P-157 dispatched to communication-arch-planner for 11.Communication (dispatch-phase)
- [2026-06-17] Governance → Governance: Architecture Rule Forbidding Reflection-Based Generic Method Invocation (●) — promoted from SK.00.ReflectionGuard (state-map-phase)
- [2026-06-17] P-009, P-034, P-056, P-063, P-075, P-083, P-096, P-103, P-110, P-114, P-123, P-153 → ● Complete — stale Governance Phase Backlog entries closed (state-map-phase)
- [2026-06-18] Phase(s) P-159 dispatched to governance-arch-planner for 00.Governance (dispatch-phase)
- [2026-06-18] Governance → Governance: Architecture Rules for Communication Layer (●) — promoted from SK.00.CommunicationArchRules (state-map-phase)
- [2026-06-18] Phase Backlog P-159 → ● Complete — SK.00.CommunicationArchRules done (state-map-phase)
- [2026-06-18] Contracts → Design (◐) — Add ResultEnvelopeExtensions ToEnvelope/ToResult bridge in SharedKernel.Contracts.Mapping (state-map-phase)
- [2026-06-18] Phase(s) P-166 dispatched to contracts-arch-planner for 04.Contracts (dispatch-phase)
- [2026-06-18] Phase(s) P-160, P-161, P-162, P-163, P-164, P-165 dispatched to communication-arch-planner for 11.Communication (dispatch-phase)
- [2026-06-18] Communication → Internal (●) — promoted from SK.11.Internal (state-map-phase)
- [2026-06-18] Communication → Tests (●) — promoted from SK.11.Tests (state-map-phase)

---
### P-042 — Core: Add Error.BusinessRule Factory to SharedKernel.Primitives

**Status:** `●` Complete
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

**Status:** `●` Complete
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

**Status:** `●` Complete
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

---
### P-058 — Contracts: Scaffold SharedKernel.Contracts Project Structure

**Status:** `●` Complete
**Work Order:** WO-012
**Domain:** 04.Contracts
**Depends on:** None

#### What is needed

The physical project scaffold for `SharedKernel.Contracts` — the single package in the `04.Contracts` domain. This phase produces a buildable, compilable, but empty project structure. No implementation logic is written here — only the structural skeleton that subsequent phases will populate.

**Project file (`SharedKernel.Contracts.csproj`):**
Targets `net10.0`. References `SharedKernel.Primitives` (from `01.Core`) and `SharedKernel.Domain` (from `03.Domain`) as project references. Has zero external NuGet dependencies beyond `System.Text.Json` which is in-box with `net10.0`. All standard NuGet packaging metadata must be present: `<PackageId>`, `<Version>` (start at `1.0.0`), `<Description>`, `<Authors>`, `<PackageTags>` (`contracts`, `dtos`, `integration-events`, `shared-kernel`). XML documentation generation must be enabled (`<GenerateDocumentationFile>true</GenerateDocumentationFile>`).

**Folder structure inside `04.Contracts/SharedKernel.Contracts/`:**
- `Pagination/` — placeholder for `PagedList<T>`
- `Envelope/` — placeholder for `Envelope` and `Envelope<T>`
- `Events/` — placeholder for `IIntegrationEvent` and `EventEnvelope<TEvent>`
- `Serialization/` — placeholder for `ContractsJsonContext`

**Test project (`SharedKernel.Contracts.Tests/`):**
Nested inside `04.Contracts/SharedKernel.Contracts/SharedKernel.Contracts.Tests/`. Targets `net10.0` as a `classlib`. References `SharedKernel.Contracts` and `SharedKernel.Testing`. xUnit, FluentAssertions (or equivalent), and the test runner are added as NuGet package references. The test project must be a compilable stub with one placeholder test class — no test logic yet.

**Solution registration:**
Both `SharedKernel.Contracts.csproj` and `SharedKernel.Contracts.Tests.csproj` must be registered in `Platform.SharedKernel.slnx` under the `04.Contracts` solution folder.

#### Why this is needed

Scaffold is the prerequisite for all implementation phases. The project structure must be established, buildable, and solution-registered before Core, Tests, Docs, or Published phases can proceed. Separating scaffold from implementation is the platform standard — it ensures the CI build pipeline can verify compilation from the first commit, rather than discovering structural errors after significant implementation work has been done.

#### Acceptance criteria
- [ ] `04.Contracts/SharedKernel.Contracts/SharedKernel.Contracts.csproj` exists; targets `net10.0`; references `SharedKernel.Primitives` and `SharedKernel.Domain`; zero external NuGet dependencies; all packaging metadata present
- [ ] XML documentation generation enabled in the project file
- [ ] Four empty placeholder subfolders exist: `Pagination/`, `Envelope/`, `Events/`, `Serialization/`
- [ ] `SharedKernel.Contracts.Tests/` nested test project exists; references `SharedKernel.Contracts` and `SharedKernel.Testing`; has at least one compilable placeholder test class
- [ ] Both projects registered in `Platform.SharedKernel.slnx` under solution folder `04.Contracts`
- [ ] `dotnet build` on the solution succeeds with zero errors and zero warnings for the new projects
---

---
### P-059 — Contracts: Core Implementation — All Five Public Surfaces

**Status:** `●` Complete
**Work Order:** WO-012
**Domain:** 04.Contracts
**Depends on:** P-058, P-055

#### What is needed

Full implementation of the five public surfaces of `SharedKernel.Contracts` as defined in `04.Contracts/CLAUDE.md`. P-055 (WO-011) delivers `EventEnvelope<TEvent>` — this phase delivers the remaining four surfaces plus the STJ context that covers all five.

**Surface 1 — `PagedList<T>` (in `Pagination/`):**

A `sealed record` with `required init` properties: `IReadOnlyList<T> Items`, `int Page` (1-based), `int PageSize`, `int TotalCount`. Computed properties: `int TotalPages` (`= (int)Math.Ceiling((double)TotalCount / PageSize)` — must handle zero `PageSize` without divide-by-zero, documented in XML), `bool HasNextPage` (`= Page < TotalPages`), `bool HasPreviousPage` (`= Page > 1`). A static factory `PagedList<T>.Create(IReadOnlyList<T> items, int page, int pageSize, int totalCount)` is the sole permitted construction path — the primary record constructor is `private init` to prevent ad-hoc construction that bypasses the factory. `Create` must validate: `page >= 1`, `pageSize >= 1`, `totalCount >= 0`; throw `ArgumentOutOfRangeException` for invalid inputs. The factory is a pure pass-through for valid inputs — it sets all four properties and lets computed properties derive. The page convention is strictly 1-based — this is consistent with `PagedSpecification<T>` in `03.Domain`. No `PageSize == 0` allowed (prevents divide-by-zero in `TotalPages`).

**Surface 2 — `Envelope` and `Envelope<T>` (in `Envelope/`):**

`Envelope` is a `sealed record` for void operations. Properties: `bool IsSuccess`, `Error? Error` (null when `IsSuccess = true`). Static factories: `Envelope.Ok()` returns success, `Envelope.Fail(Error error)` returns failure. `Fail(Error.None)` is an invalid operation — must throw `ArgumentException` with message `"Cannot create a failure envelope with Error.None."`. Implicit operator: `implicit operator Envelope(Error error)` delegates to `Envelope.Fail(error)`. `Envelope<T>` is a `sealed record` for operations returning a value. Properties: `bool IsSuccess`, `T? Value` (null when `IsSuccess = false`), `Error? Error` (null when `IsSuccess = true`). Static factories: `Envelope<T>.Ok(T value)` (rejects null `value` with `ArgumentNullException`), `Envelope<T>.Fail(Error error)` (rejects `Error.None`). Implicit operators: `implicit operator Envelope<T>(T value)` and `implicit operator Envelope<T>(Error error)`. These are the cross-service transport counterparts to `Result<T>` from `01.Core` — they must never be returned from application layer methods; they are only constructed at service boundaries (presentation layer, gRPC, HTTP client response mapping). XML doc must state this boundary contract explicitly.

**Surface 3 — `IIntegrationEvent` (in `Events/`):**

A marker interface with `Guid EventId { get; }` and `DateTimeOffset OccurredOn { get; }`. No other members. Implementations must be `sealed record` or `sealed class` — XML doc states this. `IIntegrationEvent` is the public contract projection of domain events — its `EventId` maps to `IDomainEvent.Id` from the originating domain event, preserving traceability. XML doc must state: "Integration events are immutable DTOs. No behavior, no domain logic. Consumers must never cast `IIntegrationEvent` back to a domain type."

**Surface 4 — `EventEnvelope<TEvent>` (in `Events/`):**

This surface is delivered by P-055 (WO-011). This phase integrates it into the full package — it is already specified there and no rework is needed. If P-055 has been completed before this phase is dispatched, the implementor must verify the `EventEnvelope<TEvent>` type is present and matches the `04.Contracts/CLAUDE.md` specification. If P-055 has not been completed, this phase depends on it and must wait.

**Surface 5 — `ContractsJsonContext` (in `Serialization/`):**

A `partial class ContractsJsonContext : JsonSerializerContext` decorated with `[JsonSourceGenerationOptions]` and `[JsonSerializable]` attributes covering all types in this package: `PagedList<object>` (the generic form; consumers add their own `T`), `Envelope`, `Envelope<object>`, `IIntegrationEvent`, `EventEnvelope<DomainEvent>`. The context must be `internal partial` — consuming services do not use this context directly; they extend it in their own `JsonSerializerContext`. XML doc must instruct: "Do not reference this context directly. Instead, add your own `partial JsonSerializerContext` that includes `[JsonSerializable(typeof(EventEnvelope<YourEvent>))]` and merge it with this context via `JsonSerializerOptions.TypeInfoResolverChain`." The context must not include any reflection-based fallback — `[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]` or equivalent must be set.

All five surfaces must be implemented with zero NuGet infrastructure dependencies, pure C# 13, and full AOT safety.

#### Why this is needed

`04.Contracts` is the lingua franca of the platform — every microservice that publishes or consumes integration events, returns paged responses, or wraps results in a cross-service envelope depends on this package. Getting these surfaces right before any downstream domain (Application, Messaging, Persistence) builds on them prevents breaking changes at the worst possible time. The strict factory-only construction for `PagedList<T>` prevents subtle pagination bugs (page 0, negative total counts) across hundreds of services. The `Envelope<T>` boundary contract prevents `Result<T>` from leaking across service boundaries — a common anti-pattern that causes serialization failures in polyglot environments. The STJ source-generated context is mandatory for AOT compliance — reflection-based JSON serialization in integration events is a silent AOT bomb.

#### Acceptance criteria
- [ ] `PagedList<T>` sealed record exists in `Pagination/`; `Create` static factory is the only construction path; `TotalPages`, `HasNextPage`, `HasPreviousPage` are computed correctly; `Page` is 1-based; `Create` rejects `page < 1`, `pageSize < 1`, `totalCount < 0` with `ArgumentOutOfRangeException`
- [ ] `Envelope` sealed record exists with `Ok()`, `Fail(Error)` factories and implicit `Error` operator; `Fail(Error.None)` throws `ArgumentException`
- [ ] `Envelope<T>` sealed record exists with `Ok(T)`, `Fail(Error)` factories and two implicit operators; `Ok(null)` throws `ArgumentNullException`; `Fail(Error.None)` throws `ArgumentException`
- [ ] `IIntegrationEvent` marker interface exists with `EventId` and `OccurredOn`; XML doc states implementations must be `sealed record` or `sealed class`
- [ ] `EventEnvelope<TEvent>` exists (delivered by P-055 or implemented here if P-055 completes first); matches `04.Contracts/CLAUDE.md` specification in full
- [ ] `ContractsJsonContext` partial `JsonSerializerContext` exists in `Serialization/`; covers all package types; is `internal`; uses source-generation only — no reflection fallback
- [ ] `dotnet build` produces zero errors and zero warnings (no nullable, no XML doc, no AOT analyzer warnings)
- [ ] Zero external NuGet dependencies in the `.csproj` — only `SharedKernel.Primitives` and `SharedKernel.Domain` project references
- [ ] All public types carry XML doc comments including boundary contract notes on `Envelope<T>` and `IIntegrationEvent`
---

---
### P-060 — Contracts: Test Suite — Unit Tests and STJ Round-Trip Coverage

**Status:** `●` Complete
**Work Order:** WO-012
**Domain:** 04.Contracts
**Depends on:** P-059

#### What is needed

A comprehensive unit test suite in `SharedKernel.Contracts.Tests/` covering all five public surfaces with their behavioral, boundary, and serialization contracts.

**`PagedList<T>` tests:**
- `TotalPages` computed correctly for typical inputs (e.g., 100 items / 10 per page = 10, 101 items / 10 = 11, 5 items / 10 = 1)
- `TotalPages` when `TotalCount = 0` returns 0 (not divide-by-zero)
- `HasNextPage` is true for all pages except the last; false on the last page
- `HasPreviousPage` is true for all pages except the first; false on page 1
- Single-page case: `HasNextPage = false`, `HasPreviousPage = false`
- `Create` factory rejects `page = 0`, `page = -1`, `pageSize = 0`, `totalCount = -1`
- Empty list: `Create` with `Items = []`, `TotalCount = 0` returns valid record; `TotalPages = 0`, `HasNextPage = false`, `HasPreviousPage = false`
- Record structural equality: two `PagedList<T>` with identical field values are equal
- STJ round-trip: `PagedList<string>` serializes and deserializes correctly using `ContractsJsonContext`; no reflection fallback triggered

**`Envelope` and `Envelope<T>` tests:**
- `Envelope.Ok()` produces `IsSuccess = true`, `Error = null`
- `Envelope.Fail(error)` produces `IsSuccess = false`, `Error = error`
- `Envelope.Fail(Error.None)` throws `ArgumentException`
- Implicit operator: `Error someError = ...; Envelope e = someError;` produces failure
- `Envelope<T>.Ok(value)` produces `IsSuccess = true`, `Value = value`, `Error = null`
- `Envelope<T>.Fail(error)` produces `IsSuccess = false`, `Value = null` (or default), `Error = error`
- `Envelope<T>.Ok(null)` throws `ArgumentNullException` (for reference type `T`)
- `Envelope<T>.Fail(Error.None)` throws `ArgumentException`
- Both implicit operators on `Envelope<T>`
- Record equality on both types
- STJ round-trip for `Envelope` and `Envelope<string>` using `ContractsJsonContext`

**`IIntegrationEvent` tests:**
- A concrete `sealed record` implementing `IIntegrationEvent` is assignable to the interface
- `EventId` and `OccurredOn` are readable from the interface reference

**`EventEnvelope<TEvent>` tests (coordinate with or depend on P-055 test spec):**
- `Wrap` factory populates all fields correctly; `EnvelopeId` is distinct from `TEvent.EventId`
- `CorrelationId` is never null or empty when provided; defaults correctly when null
- `CausationId` is null when not provided
- `SchemaVersion` matches the value passed to `Wrap`; defaults to 1 when `DomainEventVersionAttribute` is absent
- `EventType` equals `typeof(TEvent).Name`
- Record equality
- STJ round-trip for `EventEnvelope<T>` where `T` is a concrete `IDomainEvent`-implementing record

**Cross-cutting test rules:**
- No test may use reflection-based `JsonSerializer.Serialize(obj)` without a context — all STJ tests must use source-generated contexts
- All tests must be theory-driven where boundary conditions exist (e.g., `PagedList<T>` page/pageSize guards use `[Theory]` with edge-case data)
- Tests are framework-agnostic assertions — no assumption that FluentAssertions is the only allowed assertion library, though it is the platform standard

#### Why this is needed

`04.Contracts` is a zero-infrastructure package — every test can be a true unit test with no external dependencies. This means there is no excuse for incomplete coverage. The STJ round-trip tests are particularly critical: they are the only automated verification that the `ContractsJsonContext` correctly covers all types, and that the `EventEnvelope<TEvent>` serialization does not silently fall back to reflection in production AOT builds. Without these tests, an AOT-incompatible serialization path can ship undetected and only surface at runtime in a production container.

#### Acceptance criteria
- [ ] `PagedList<T>` tests cover: `TotalPages` computation, `HasNextPage`/`HasPreviousPage` on first/last/middle/single/empty page, factory guard clauses, record equality, STJ round-trip
- [ ] `Envelope` and `Envelope<T>` tests cover: success/failure factories, implicit operators, `Error.None` guard, `null` value guard on `Ok(T)`, record equality, STJ round-trip
- [ ] `IIntegrationEvent` tests cover: interface assignability; property accessibility from interface reference
- [ ] `EventEnvelope<TEvent>` tests cover: `Wrap` factory fields, distinct `EnvelopeId`, null/non-null `CorrelationId`, `SchemaVersion` default and declared, `EventType`, record equality, STJ round-trip
- [ ] All STJ round-trip tests use source-generated contexts — zero reflection-based serialization in tests
- [ ] All guard-clause tests use `[Theory]` with boundary data sets
- [ ] `dotnet test` passes with zero failures and zero skipped tests
- [ ] Test project references only `SharedKernel.Contracts` and `SharedKernel.Testing` — no new infrastructure dependencies
---

---
### P-061 — Contracts: Docs — XML Documentation and Package README

**Status:** `●` Complete
**Work Order:** WO-012
**Domain:** 04.Contracts
**Depends on:** P-060

#### What is needed

Complete XML documentation on all public types and a package-level README that serves as the developer quick-reference for `SharedKernel.Contracts`.

**XML documentation requirements (per type):**

`PagedList<T>`: `<summary>` stating it is the cross-service paged result DTO; `<typeparam>` for `T`; `<remarks>` documenting the 1-based page convention and `Create` as the only permitted construction path; per-property `<param>` on `Create`.

`Envelope` and `Envelope<T>`: `<summary>` stating these are cross-service response wrappers (counterparts to `Result<T>` from `SharedKernel.Primitives`); `<remarks>` documenting the boundary contract — must only be constructed at service boundaries (presentation, HTTP client adapters), never returned from application layer methods; `<seealso cref="Result{T}"/>` cross-reference.

`IIntegrationEvent`: `<summary>` stating it is the public contract projection of a domain event; `<remarks>` stating implementations must be `sealed record` or `sealed class`, must be immutable DTOs, and must never carry domain logic; cross-reference to `IDomainEvent` from `03.Domain`.

`EventEnvelope<TEvent>`: `<summary>` and per-property `<remarks>` for every property (especially `EnvelopeId` vs `EventId` distinction, `SchemaVersion` sourcing, `CorrelationId` null semantics, `SourceService` intent).

`ContractsJsonContext`: `<summary>` and `<remarks>` instructing consumers to not reference this context directly but to create their own `partial JsonSerializerContext` and merge via `TypeInfoResolverChain`.

**README.md (`04.Contracts/SharedKernel.Contracts/README.md`):**
A concise developer README with five sections:
1. Purpose and what belongs here (cross-service DTOs, integration event payloads, paged results, response envelopes) — and what does NOT belong (domain logic, domain types, `Result<T>`)
2. Quick-start code examples for each surface (create a `PagedList<T>`, wrap a result in `Envelope<T>`, define an `IIntegrationEvent`, wrap a domain event in `EventEnvelope<TEvent>`)
3. The `Result<T>` vs `Envelope<T>` boundary rule — one paragraph explaining when to use each and at which layer conversion happens
4. The STJ usage pattern — how consuming services must extend `ContractsJsonContext`
5. The `EventEnvelope<TEvent>` composition pattern — how `07.Messaging` uses it as the wire format

#### Why this is needed

`04.Contracts` is the most widely referenced package in the entire platform — every microservice references it. Developers encounter this package on day 1 and must understand three non-obvious boundaries: `Result<T>` vs `Envelope<T>`, why `IIntegrationEvent` cannot carry behavior, and how `ContractsJsonContext` must be extended. These are architectural subtleties that XML doc comments alone cannot fully convey — the README provides the framing. Every new team member, every onboarding engineer, and every code reviewer will consult this package's documentation. Incomplete XML docs cause IDE tooltips to be silent exactly where developers need guidance most.

#### Acceptance criteria
- [ ] All public types in `SharedKernel.Contracts` have complete XML doc comments (`<summary>`, `<remarks>` where applicable, `<typeparam>`, `<param>`, `<seealso>` cross-references)
- [ ] `Envelope<T>` XML doc explicitly states the boundary contract and cross-references `Result<T>`
- [ ] `ContractsJsonContext` XML doc instructs consumers on context extension via `TypeInfoResolverChain`
- [ ] `IIntegrationEvent` XML doc states the sealed record/class constraint and the no-domain-logic rule
- [ ] `README.md` exists in the project folder with all five sections
- [ ] `dotnet build` generates the `.xml` documentation file with no missing-doc warnings
- [ ] All XML doc comments are grammatically correct and accurately describe the types they annotate
---

---
### P-062 — Contracts: Published — NuGet Packaging and Consumer Verification

**Status:** `●` Complete
**Work Order:** WO-012
**Domain:** 04.Contracts
**Depends on:** P-061

#### What is needed

NuGet packaging and consumer verification for `SharedKernel.Contracts` — confirming the package can be consumed correctly by a downstream microservice in a realistic usage scenario.

**NuGet metadata hardening:**
Verify the `.csproj` has complete metadata: `<PackageId>SharedKernel.Contracts</PackageId>`, `<Version>1.0.0</Version>`, `<Description>Cross-service DTOs, integration event contracts, paged results, and response envelopes for the Platform SharedKernel ecosystem.</Description>`, `<Authors>Platform Team</Authors>`, `<PackageTags>contracts;dtos;integration-events;paged-list;envelope;shared-kernel</PackageTags>`, `<PackageLicenseExpression>MIT</PackageLicenseExpression>` (or the repo's chosen license), `<RepositoryUrl>` pointing to the mono-repo. `<Nullable>enable</Nullable>` must be present. `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` is strongly recommended for a contracts package.

**Pack and verify:**
Run `dotnet pack` on `SharedKernel.Contracts.csproj` and confirm the `.nupkg` is produced in the `nupkgs/` output directory (consistent with the convention used by `SharedKernel.Domain` and `SharedKernel.Primitives`). The `.nupkg` must include the generated XML documentation file (`.xml`) alongside the DLL so NuGet consumers receive IntelliSense.

**Consumer verification project:**
A minimal `consumer-verify` console project (or the existing one if it exists for the domain) that:
1. References `SharedKernel.Contracts` as a `ProjectReference`
2. Creates a `PagedList<string>` via `PagedList<string>.Create(items, page: 1, pageSize: 10, totalCount: 25)`
3. Wraps a result in `Envelope<string>.Ok("hello")`
4. Defines a minimal concrete `IIntegrationEvent` sealed record and wraps it in an `EventEnvelope.Wrap(...)` call
5. Serializes the `EventEnvelope<T>` using a locally defined `JsonSerializerContext` that extends `ContractsJsonContext`
6. Confirms the round-trip compiles and runs without reflection-based JSON paths

This verification project must compile and run with `dotnet run`. It is not a test project — it is a compilation and smoke-test verification that the public API is usable as intended.

**`04.Contracts/state-map.md` Published milestone:**
The domain sub-state-map must be updated to reflect `Published` state with the `.nupkg` manifest entry.

#### Why this is needed

A package that can be built but not consumed correctly has no value. The consumer-verify project closes the gap between "it builds" and "it works for downstream teams." It is especially important for `04.Contracts` because the `ContractsJsonContext` extension pattern is non-obvious — if the STJ context is misconfigured, the compilation still succeeds but the runtime will fall back to reflection silently. The consumer-verify project exercises this path explicitly. The `TreatWarningsAsErrors` discipline prevents XML doc gaps and nullable annotation regressions from shipping in a contracts package used by hundreds of services.

#### Acceptance criteria
- [ ] `.csproj` has complete NuGet metadata (all fields listed above)
- [ ] `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` are present
- [ ] `dotnet pack` succeeds and produces `SharedKernel.Contracts.1.0.0.nupkg` in `nupkgs/`
- [ ] `.nupkg` includes the `.xml` documentation file
- [ ] Consumer-verify project exists; references `SharedKernel.Contracts`; exercises all five surfaces; defines a consuming `JsonSerializerContext` that extends the base context
- [ ] Consumer-verify project compiles and runs with `dotnet run` — no runtime reflection fallback
- [ ] `04.Contracts/state-map.md` Published phase milestone updated with `.nupkg` manifest
- [ ] All existing tests continue to pass — no regressions from packaging changes
---

---
### P-063 — Governance: Contracts Layer Purity Architecture Rules

**Status:** `●` Complete
**Work Order:** WO-012
**Domain:** 00.Governance
**Depends on:** P-059

#### What is needed

New architecture enforcement rules in `00.Governance/SharedKernel.ArchitectureTests` that protect the `04.Contracts` purity contract. These rules enforce the hard layering and design constraints that distinguish a gold-standard contracts package from a code-dump DTO library.

**Rule 1 — `04.Contracts` must never contain domain logic:**
A NetArchTest rule that asserts no type in any assembly under `04.Contracts` has methods with behavioral logic beyond: constructors/factories, computed properties that derive from stored state, `ToString()`, and `Equals()`/`GetHashCode()` (from record). The test detects types with non-trivial method bodies by checking method count > N (a heuristic threshold) or by checking for any method that is not a constructor, operator, or property getter. The rule is documented as a heuristic — it cannot catch all logic, but it catches accidental method additions.

**Rule 2 — No domain types on `04.Contracts` public surface:**
A NetArchTest rule that asserts no public type in `04.Contracts` assemblies has public properties or return types that are `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, or `Specification<T>` from `03.Domain`. Integration events and DTOs must be independent projections, not domain type aliases. The `IDomainEvent` reference in `EventEnvelope<TEvent>` is a permitted internal constraint (it is the generic parameter constraint), not a public property type.

**Rule 3 — `Result<T>` must not appear in `04.Contracts` public surface:**
A NetArchTest rule that asserts no property, parameter, or return type in `04.Contracts` public API is `Result<T>` or `Result` from `SharedKernel.Primitives`. `Result<T>` is intra-service; `Envelope<T>` is cross-service. They must never be conflated. Crossing a service boundary with `Result<T>` causes serialization failures in polyglot consumers and breaks the contract model.

**Rule 4 — `IIntegrationEvent` implementations must be sealed:**
A NetArchTest rule that asserts every non-abstract type in any production assembly that implements `IIntegrationEvent` is either a `sealed class` or a `record` (which is implicitly sealed in terms of further implementation). Non-sealed integration events are an inheritance trap — they allow sub-events that change the wire format without incrementing `[DomainEventVersion]`.

**Rule 5 — Microservices must not reference `SharedKernel.Domain` directly (only via `04.Contracts`):**
A documentation-level rule (enforced by `13.ServiceDefaults` or equivalent composition guidance): microservices must reference `SharedKernel.Contracts` for cross-service DTO types and must not reference `SharedKernel.Domain` unless they implement domain logic. This is not a NetArchTest rule (the mono-repo structure makes it hard to enforce across external assemblies) but is documented in the `00.Governance` brain with the architectural rationale.

**Rule documentation:** All five rules must be documented in the `00.Governance` domain brain with rationale, offending-pattern example, and compliant-pattern example.

#### Why this is needed

`04.Contracts` is the most widely referenced package in the platform. A contracts package that silently accumulates domain logic, carries `Result<T>` return types, or exposes non-sealed integration events will corrupt downstream services at the point of serialization or deserialization — typically in production, not in development. The rules here are simple, high-confidence, low-false-positive checks. Rule 3 (no `Result<T>`) is particularly important: a microservice that returns `Result<T>` in a serialized HTTP response compiles correctly but fails for any JSON client that does not know about `SharedKernel.Primitives` types. The governance rules make these violations visible in CI before they reach any downstream consumer.

#### Acceptance criteria
- [ ] NetArchTest Rule 1 exists: detects non-trivial method bodies in `04.Contracts` assemblies; includes an explicit allowlist for constructors, operators, property accessors
- [ ] NetArchTest Rule 2 exists: detects `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, or `Specification<T>` as public property/return types in `04.Contracts`
- [ ] NetArchTest Rule 3 exists: detects `Result<T>` or `Result` as any public parameter or return type in `04.Contracts`; fires with the specific type name in the error message
- [ ] NetArchTest Rule 4 exists: detects non-sealed `IIntegrationEvent` implementations in production assemblies; fires with the offending type name
- [ ] Rule 5 is documented in `00.Governance/CLAUDE.md` with rationale — not a NetArchTest rule but an architectural guideline
- [ ] All four NetArchTest rules have at least one "violating assembly" fixture test demonstrating the rule fires correctly under a violation scenario
- [ ] All rules documented in `00.Governance/CLAUDE.md` with rationale, offending-pattern example, compliant-pattern example
- [ ] Governance test suite passes with all new rules included; no false positives on the existing SharedKernel assemblies
---

---
### P-064 — Testing: Contracts Test Helpers — Fakers, Builders, and Assertion Extensions

**Status:** `○` Pending
**Work Order:** WO-012
**Domain:** 16.Testing
**Depends on:** P-059

#### What is needed

Additions to `16.Testing/SharedKernel.Testing` that give downstream microservice test projects the standard test helpers for the `04.Contracts` types — covering construction helpers, Bogus fakers, and assertion extensions.

**`PagedListBuilder<T>` — fluent test builder:**
A fluent builder for constructing `PagedList<T>` instances in tests without boilerplate. Exposes: `WithItems(IEnumerable<T> items)`, `WithPage(int page)`, `WithPageSize(int pageSize)`, `WithTotalCount(int totalCount)`, and `Build()` which calls `PagedList<T>.Create(...)`. Default values: `Page = 1`, `PageSize = 10`, `TotalCount = items.Count` (when set via `WithItems`). An `Empty<T>()` static factory returns a `PagedList<T>` with zero items, `TotalCount = 0`, `Page = 1`, `PageSize = 10`. This eliminates the repetitive construction in test setups where teams need a `PagedList<T>` but don't care about the pagination parameters.

**`EnvelopeAssertions` — extension methods on `Envelope` and `Envelope<T>`:**
Static extension methods for assertion-level checking:
- `ShouldBeSuccess(this Envelope envelope)` — asserts `IsSuccess = true`; throws with the `Error` details on failure
- `ShouldBeFailure(this Envelope envelope)` — asserts `IsSuccess = false`; throws with a message on failure
- `ShouldBeSuccess<T>(this Envelope<T> envelope)` — asserts success; returns `Value` for further chaining
- `ShouldBeFailure<T>(this Envelope<T> envelope, ErrorType? expectedType = null)` — asserts failure; optionally asserts the `ErrorType`; throws with the actual error on failure
- `ShouldHaveError(this Envelope<T> envelope, string expectedCode)` — asserts failure with a specific error code

All helpers throw `InvalidOperationException` with descriptive messages — no test framework dependency.

**`IntegrationEventFaker<TEvent>` abstract base:**
A Bogus-based abstract faker for `IIntegrationEvent` implementations. Provides `protected void RuleForEventId()` pre-wired to `f.Random.Guid()` and `protected void RuleForOccurredOn()` pre-wired to `f.Date.RecentOffset()`. Subclasses call these helpers in their constructor then add their own `RuleFor` declarations. Eliminates the boilerplate of wiring `EventId` and `OccurredOn` on every integration event faker.

**`EventEnvelopeBuilder<TEvent>` — fluent test builder for `EventEnvelope<TEvent>`:**
A fluent builder that wraps `EventEnvelope.Wrap<TEvent>(...)`. Exposes: `WithPayload(TEvent event)`, `WithSourceService(string name)`, `WithCorrelationId(string id)`, `WithCausationId(string id)`, and `Build()`. Defaults: `SourceService = "test-service"`, `CorrelationId = Guid.NewGuid().ToString("N")`, `CausationId = null`. This gives integration test setups a clean way to construct envelopes without knowing every field.

**`AddFakeContractsServices()` DI extension:**
Registers any DI-backed test doubles for the contracts domain — at this stage likely only `PagedListBuilder` and `EventEnvelopeBuilder` as transient services if needed by test fixture scaffolding. If no DI registration is needed, this extension can be deferred.

All helpers must be added to `16.Testing/SharedKernel.Testing` under a `SharedKernel.Testing.Contracts` namespace sub-group. Each helper must have its own unit tests in `SharedKernel.Testing.Tests`.

#### Why this is needed

Downstream microservice test projects will construct `PagedList<T>` instances dozens of times per test suite — one per paged query test. Without `PagedListBuilder<T>`, every test hardcodes `PagedList<string>.Create(new[] {"a"}, 1, 10, 1)` — correct but noisy. Without `EnvelopeAssertions`, every test writes `Assert.True(result.IsSuccess); Assert.Equal("expected", result.Value)` without meaningful failure messages. The `IntegrationEventFaker<TEvent>` closes the gap between `EntityFaker<TEntity, TId>` (P-035) and integration event test data — without it, teams write their own minimal fakers per event type, diverging in completeness and correctness. The `EventEnvelopeBuilder<TEvent>` is particularly valuable for messaging tests that need to simulate receiving an envelope without knowing the metadata details.

#### Acceptance criteria
- [ ] `PagedListBuilder<T>` fluent builder exists; `WithItems`, `WithPage`, `WithPageSize`, `WithTotalCount`, `Build()` work correctly; `Empty<T>()` produces a valid zero-item `PagedList<T>`
- [ ] `EnvelopeAssertions` static extensions exist: `ShouldBeSuccess`, `ShouldBeFailure` on both `Envelope` and `Envelope<T>`; `ShouldHaveError` checks error code; all throw with descriptive messages on failure; no test framework dependency
- [ ] `IntegrationEventFaker<TEvent>` abstract base exists; `RuleForEventId()` and `RuleForOccurredOn()` helpers are pre-wired; subclasses can extend cleanly
- [ ] `EventEnvelopeBuilder<TEvent>` fluent builder exists; all fields are configurable; defaults are applied for unprovided fields
- [ ] All helpers have unit tests in `SharedKernel.Testing.Tests`
- [ ] `16.Testing/SharedKernel.Testing` references `SharedKernel.Contracts` (permitted — testing may reference any layer)
- [ ] All public types carry XML doc comments
- [ ] `AddFakeCachingServices()` and `AddFakeDomainServices()` are not affected — contracts helpers are registered separately via `AddFakeContractsServices()` if DI registration is needed, or shipped as static helpers only

---
### P-065 — Persistence: Scaffold Abstractions and EfCore Packages

**Status:** `●` Complete
**Work Order:** WO-013
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

Full project scaffold for the two persistence packages in scope: `SharedKernel.Persistence.Abstractions` and `SharedKernel.Persistence.EfCore`. Both csproj files are currently empty shells — they need proper NuGet references, package metadata, and solution wiring before any implementation can begin.

**`SharedKernel.Persistence.Abstractions`:**
- References: `SharedKernel.Primitives`, `SharedKernel.Domain` (project references from `01.Core` and `03.Domain`)
- Zero ORM NuGet dependencies — this is a pure interface library
- Package metadata: id, description, version `1.0.0`

**`SharedKernel.Persistence.EfCore`:**
- References: `SharedKernel.Persistence.Abstractions` (project reference), `SharedKernel.Domain` (project reference)
- NuGet: `Microsoft.EntityFrameworkCore` 10.x, `Microsoft.EntityFrameworkCore.Relational` 10.x, `Microsoft.Extensions.DependencyInjection.Abstractions` (for DI extensions)
- Package metadata: id, description, version `1.0.0`

Both packages: `net10.0` target framework, `ImplicitUsings` enabled, `Nullable` enabled. Both test sub-project csproj files get the same treatment — test projects receive `xunit`, `xunit.runner.visualstudio`, `coverlet.collector`, `Microsoft.EntityFrameworkCore.Sqlite` (for EfCore tests), and a project reference to `SharedKernel.Testing`. The two test projects are: `SharedKernel.Persistence.Abstractions.Tests/` and `SharedKernel.Persistence.EfCore.Tests/`.

Add all four projects (two production, two test) to `Platform.SharedKernel.slnx` under the `06.Persistence` solution folder.

#### Why this is needed

Both packages are empty csproj shells with no references. No implementation can start until the dependency graph is correctly wired and the solution registers the projects. The PostgreSQL and Dapper packages are out of scope for this work order — they will be scaffolded in a future work order when those capability domains are needed.

#### Acceptance criteria
- [ ] `SharedKernel.Persistence.Abstractions.csproj` references `SharedKernel.Primitives` and `SharedKernel.Domain` as project references; zero ORM NuGet dependencies
- [ ] `SharedKernel.Persistence.EfCore.csproj` references `SharedKernel.Persistence.Abstractions` and `SharedKernel.Domain` as project references; `Microsoft.EntityFrameworkCore` 10.x and `Microsoft.Extensions.DependencyInjection.Abstractions` added
- [ ] Both test csproj files reference `SharedKernel.Testing`, xUnit packages, and their respective production project; EfCore.Tests adds `Microsoft.EntityFrameworkCore.Sqlite`
- [ ] All four projects appear in `Platform.SharedKernel.slnx` under the `06.Persistence` solution folder
- [ ] `dotnet build` passes for both production projects from a clean state
---

---
### P-066 — Persistence Abstractions: IRepository, IReadRepository, IUnitOfWork, IDbConnectionFactory, ISpecificationEvaluator

**Status:** `●` Complete
**Work Order:** WO-013
**Domain:** 06.Persistence
**Depends on:** P-065

#### What is needed

The complete public surface of `SharedKernel.Persistence.Abstractions`. This package is a pure interface library — no EF Core, no Npgsql, no Dapper. Every interface in this package is a contract that consuming microservices depend on regardless of which persistence provider they choose. The outbox pattern is fully owned by `07.Messaging` (MassTransit's Entity Framework outbox) — no outbox types belong here.

**Repository interfaces (`Repositories/`):**
- `IRepository<TAggregate, TId>` — write-side only: `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`. Constrained to `where TAggregate : IAggregateRoot<TId> where TId : notnull`. No `IQueryable<TAggregate>` exposure — all queries go through `IReadRepository`.
- `IReadRepository<TAggregate, TId>` — read-side only: `GetByIdAsync`, `GetBySpecAsync(ISpecification<TAggregate>)`, `ListAsync(ISpecification<TAggregate>)`, `CountAsync(ISpecification<TAggregate>)`, `AnyAsync(ISpecification<TAggregate>)`. Same generic constraints as write repository. Consumers inject `ReadOnlySpecification<T>` or `PagedSpecification<T>` from `03.Domain` for read-heavy paths.

**Unit of work (`UnitOfWork/`):**
- `IUnitOfWork` — single method: `SaveChangesAsync(CancellationToken ct) → Task<int>`. This is the only permitted save boundary in the system. No provider-specific concepts leak here.

**Connection factory (`Connections/`):**
- `IDbConnectionFactory` — single method: `CreateConnectionAsync(CancellationToken ct) → Task<IDbConnection>`. Returns an open connection; caller disposes. Defined here for future Dapper provider implementations; not consumed by EfCore packages.

**Specification evaluator contract (`Specifications/`):**
- `ISpecificationEvaluator<T>` — single method: `GetQuery(IQueryable<T> inputQuery, ISpecification<T> spec) → IQueryable<T>`. Lives in Abstractions so alternative evaluators (e.g., Cosmos DB) can implement the same contract without referencing EF Core.

All types carry XML doc comments.

#### Why this is needed

`SharedKernel.Persistence.Abstractions` is the contract layer that Application-layer handlers (`05.Application`) depend on for repositories and unit of work. Keeping it free of ORM dependencies means a microservice can reference it without pulling in EF Core. Outbox is explicitly excluded: MassTransit's Entity Framework outbox (`UseEntityFrameworkOutbox`) manages its own schema, persistence, and relay entirely within `07.Messaging` — introducing a competing outbox contract in `06.Persistence` would create parallel infrastructure with no clear owner. The specification evaluator contract enables the EfCore implementation and any future alternative to fulfill the same interface without coupling to each other.

#### Acceptance criteria
- [ ] `IRepository<TAggregate, TId>` defined with write-only surface; no `IQueryable<TAggregate>` member
- [ ] `IReadRepository<TAggregate, TId>` defined with all five read methods accepting `ISpecification<TAggregate>`
- [ ] `IUnitOfWork` defined with single `SaveChangesAsync` method
- [ ] `IDbConnectionFactory` defined with single `CreateConnectionAsync` method returning `Task<IDbConnection>`
- [ ] `ISpecificationEvaluator<T>` defined with `GetQuery` method
- [ ] No `OutboxMessage`, `IOutboxWriter`, or any outbox type exists in this package
- [ ] Package has zero ORM NuGet dependencies — only `SharedKernel.Primitives` and `SharedKernel.Domain` project references
- [ ] All public types carry XML doc comments
- [ ] `dotnet build SharedKernel.Persistence.Abstractions` passes clean
---

---
### P-067 — Persistence EfCore Core: SharedKernelDbContext, EntityTypeConfigurationBase, StronglyTypedIdValueConverter

**Status:** `●` Complete
**Work Order:** WO-013
**Domain:** 06.Persistence
**Depends on:** P-066

#### What is needed

The foundational EF Core types in `SharedKernel.Persistence.EfCore` that all other EfCore phases build on. This phase must be completed before interceptors (P-068) or repositories (P-069) are implemented, because both depend on `SharedKernelDbContext`.

**`SharedKernelDbContext` — abstract base context (`Context/`):**
The base `DbContext` that all downstream microservice DbContexts extend. Its responsibilities:

- Accepts `DbContextOptions` via constructor; passes to `DbContext` base
- Registers the three cross-cutting interceptors in its constructor (`AuditInterceptor`, `SoftDeleteInterceptor`, `ConcurrencyInterceptor`) — downstream subclasses cannot bypass this registration
- Overrides `SaveChangesAsync` to ensure all interceptors fire before the database commit; returns `Task<int>`
- Exposes no entity `DbSet<T>` properties — those are declared by the consuming service's own DbContext subclass
- In `OnModelCreating`, scans and applies all `IEntityTypeConfiguration<T>` implementations registered in the calling assembly (via `modelBuilder.ApplyConfigurationsFromAssembly`)
- Must not seal itself — it is designed to be extended

The outbox interceptor is not registered here. MassTransit's `UseEntityFrameworkOutbox` integration manages outbox concerns directly on the consuming service's DbContext at the `07.Messaging` layer.

**`EntityTypeConfigurationBase<TEntity, TId>` — abstract EF configuration base (`Configurations/`):**
The base class all aggregate EF configurations extend. Configures the following automatically when `base.Configure(builder)` is called:

- Primary key on `TId`
- Concurrency token (row version) for entities implementing `IHasConcurrency` — uses `IsRowVersion()` convention
- Global query filter for `ISoftDeletable` entities: `e => !e.IsDeleted` — ensures soft-deleted records are invisible to all queries by default
- Owned audit value columns for `IHasCreatedAudit`: `CreatedBy` (max-length string, not null), `CreatedOn` (DateTimeOffset, not null)
- Owned audit value columns for `IHasAudit`: additionally `ModifiedBy` (nullable string), `ModifiedOn` (nullable DateTimeOffset)
- Tenant column for `IHasTenant`: `TenantId` (Guid, not null) — index added for tenant-filtered queries

Concrete downstream configurations extend this base and call `base.Configure(builder)` first, then add their own entity-specific column and index mappings.

**`StronglyTypedIdValueConverter<TStronglyTypedId, TValue>` — EF value converter (`Conversions/`):**
A sealed generic `ValueConverter<TStronglyTypedId, TValue>` that converts between a `StronglyTypedId<TValue>` and its underlying `TValue` for EF Core column mapping. Uses the `implicit operator TValue` from `StronglyTypedId<TValue>` for the to-provider direction and a constructor call (or factory) for the from-provider direction. No reflection — the `implicit operator TValue` is a static method call, AOT-safe.

#### Why this is needed

`SharedKernelDbContext` is the single point where cross-cutting persistence concerns (audit, soft-delete, concurrency) are registered as interceptors. Centralizing this in an abstract base ensures no downstream service can accidentally omit an interceptor by forgetting to call `AddInterceptors`. The outbox concern is intentionally absent — MassTransit's Entity Framework outbox hooks into the consuming service's `DbContext` directly and manages its own interceptor registration, schema, and relay without any coordination with this base. `EntityTypeConfigurationBase` eliminates boilerplate from every entity's EF configuration. `StronglyTypedIdValueConverter` is required by every entity configuration that uses a strongly-typed ID as a primary key.

#### Acceptance criteria
- [ ] `SharedKernelDbContext` is abstract; its constructor registers exactly three interceptors (`AuditInterceptor`, `SoftDeleteInterceptor`, `ConcurrencyInterceptor`); no `OutboxInterceptor`
- [ ] `SharedKernelDbContext.OnModelCreating` calls `modelBuilder.ApplyConfigurationsFromAssembly` for the calling assembly
- [ ] `SharedKernelDbContext.SaveChangesAsync` override delegates to interceptor pipeline then base EF Core commit
- [ ] `EntityTypeConfigurationBase<TEntity, TId>` applies primary key, concurrency token (for `IHasConcurrency`), soft-delete filter (for `ISoftDeletable`), and audit columns (for `IHasCreatedAudit` / `IHasAudit`) when `base.Configure(builder)` is called
- [ ] Tenant column and index applied for `IHasTenant` entities in `EntityTypeConfigurationBase`
- [ ] `StronglyTypedIdValueConverter<TStronglyTypedId, TValue>` uses `implicit operator TValue` — no `Activator.CreateInstance`, no reflection
- [ ] All public types carry XML doc comments
- [ ] `dotnet build SharedKernel.Persistence.EfCore` passes clean
---

---
### P-068 — Persistence EfCore Interceptors: Audit, SoftDelete, Concurrency

**Status:** `●` Complete
**Work Order:** WO-013
**Domain:** 06.Persistence
**Depends on:** P-067

#### What is needed

The three `ISaveChangesInterceptor` implementations in `SharedKernel.Persistence.EfCore` that compose the cross-cutting persistence pipeline. All three are sealed classes registered by `SharedKernelDbContext`. There is no `OutboxInterceptor` in this package — outbox concerns belong entirely to `07.Messaging` (MassTransit's Entity Framework outbox).

**`AuditInterceptor` (`Interceptors/`):**

- Fires `SavingChangesAsync` / `SavingChanges`
- For `Added` entities implementing `IHasCreatedAudit`: sets `CreatedBy` and `CreatedOn` via `ChangeTracker.Entry(entity).CurrentValues[propertyName]` — never via direct property setter on the aggregate
- For `Modified` entities implementing `IHasAudit`: sets `ModifiedBy` and `ModifiedOn` the same way
- For `Deleted` entities: does not write audit fields — `SoftDeleteInterceptor` intercepts those before they reach `Deleted` state
- Requires `IUserContext` (from `12.Security.Abstractions`) to resolve the current user string — injected via constructor. Note: `06.Persistence` cannot reference `12.Security` by layering rules; `IUserContext` must be resolved at DI composition time and passed in as a scoped dependency injected into the interceptor. The interceptor declares a constructor parameter typed to `IUserContext`; DI wires it at runtime. This is standard EF interceptor DI — the interceptor is registered as a scoped service so it receives `IUserContext` per-request.
- Requires `IClock` (from `01.Core`) for `CreatedOn` / `ModifiedOn` timestamps

**`SoftDeleteInterceptor` (`Interceptors/`):**

- Fires `SavingChangesAsync` / `SavingChanges`
- For `Deleted` entities implementing `ISoftDeletable`: changes EF entity state from `Deleted` to `Modified`; sets `IsDeleted = true`, `DeletedOn = clock.UtcNow`, `DeletedBy` via EF ChangeTracker
- For non-`ISoftDeletable` entities: passes through without modification
- Requires `IUserContext` and `IClock` via constructor

**`ConcurrencyInterceptor` (`Interceptors/`):**

- Fires on `SaveChangesFailedAsync` / `SaveChangesFailed`
- When a `DbUpdateConcurrencyException` is thrown for an entity implementing `IHasConcurrency`: catches the exception and rethrows as a typed `ConcurrencyException` carrying `Error.Conflict(...)` from `SharedKernel.Primitives`
- Does not silently retry — conflict resolution is the application layer's responsibility
- Non-concurrency exceptions are not swallowed — they propagate unchanged

#### Why this is needed

These three interceptors enforce audit trails, soft deletion, and optimistic concurrency transparently at the infrastructure boundary without any domain or application code needing to know about them. Every downstream `SaveChangesAsync` call automatically gets consistent cross-cutting behaviour. The outbox concern is absent by design — MassTransit's `UseEntityFrameworkOutbox` hooks directly into the consuming service's DbContext at the `07.Messaging` composition layer, writing its outbox tables in the same transaction as `SaveChanges` without any coordination needed from this package.

#### Acceptance criteria
- [ ] `AuditInterceptor` sets `CreatedBy`/`CreatedOn` for Added `IHasCreatedAudit` entities; sets `ModifiedBy`/`ModifiedOn` for Modified `IHasAudit` entities; does so via EF ChangeTracker, never via direct property setter
- [ ] `SoftDeleteInterceptor` converts `Deleted → Modified` for `ISoftDeletable` entities; sets `IsDeleted`, `DeletedOn`, `DeletedBy`; non-soft-deletable entities pass through untouched
- [ ] `ConcurrencyInterceptor` catches `DbUpdateConcurrencyException` for `IHasConcurrency` entities and rethrows as `ConcurrencyException` carrying `Error.Conflict(...)` — does not swallow non-concurrency exceptions
- [ ] Exactly three interceptors are sealed classes registered in `SharedKernelDbContext`; no `OutboxInterceptor` exists in this package
- [ ] `IUserContext` is injected into `AuditInterceptor` and `SoftDeleteInterceptor` as a scoped DI dependency — no direct reference to `12.Security.Oidc` concrete package
- [ ] `IClock` is injected via constructor from `01.Core`
- [ ] All public types carry XML doc comments
---

---
### P-069 — Persistence EfCore Repositories, Unit of Work, and Specification Evaluator

**Status:** `●` Complete
**Work Order:** WO-013
**Domain:** 06.Persistence
**Depends on:** P-067

#### What is needed

The EF Core implementations of the repository and unit of work contracts from `SharedKernel.Persistence.Abstractions`, plus the specification evaluator that translates `ISpecification<T>` to `IQueryable<T>`.

**`EfRepository<TAggregate, TId>` (`Repositories/`):**
Abstract class implementing `IRepository<TAggregate, TId>`. Backed by `DbContext.Set<TAggregate>()`. Implements `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`. Does not expose `IQueryable<TAggregate>` to callers. Concrete downstream repositories extend this — do not register `EfRepository<T,TId>` directly in DI without a concrete subclass. Accepts `SharedKernelDbContext` via constructor injection.

**`EfReadRepository<TAggregate, TId>` (`Repositories/`):**
Abstract class implementing `IReadRepository<TAggregate, TId>`. Uses `ISpecificationEvaluator<TAggregate>` internally to apply specifications to `DbContext.Set<TAggregate>()`. Implements all five read methods: `GetByIdAsync`, `GetBySpecAsync`, `ListAsync`, `CountAsync`, `AnyAsync`. When the specification has `AsNoTracking == true`, `AsNoTracking()` is applied to the query. Accepts `SharedKernelDbContext` and `ISpecificationEvaluator<TAggregate>` via constructor.

**`EfUnitOfWork` (`UnitOfWork/`):**
Sealed class implementing `IUnitOfWork`. Single responsibility: delegates `SaveChangesAsync` to `SharedKernelDbContext.SaveChangesAsync`. Accepts `SharedKernelDbContext` via constructor. All interceptors fire automatically through the context. This is the sole permitted save boundary — application code must never call `DbContext.SaveChangesAsync` directly.

**`SpecificationEvaluator<T>` (`Specifications/`):**
Sealed class implementing `ISpecificationEvaluator<T>`. The `GetQuery` method applies the following operations to the input `IQueryable<T>` in strict order:

1. `Criteria` (Where clause) — if non-null
2. `Includes` (eager loading via `Include` / `ThenInclude`)
3. `OrderBy` / `OrderByDescending` — primary sort; last-call wins
4. `ThenBys` — secondary sorts applied in order; only applied if a primary sort is set
5. `IsDistinct` — `Distinct()` if true
6. `AsNoTracking` — `AsNoTracking()` if true
7. `Skip` / `Take` — paging; always last to ensure ordering is stable before any row-offset operation

#### Why this is needed

These types are the bridge between the domain's `ISpecification<T>` contracts and EF Core's `IQueryable<T>` pipeline. Abstracting repositories behind `IRepository` and `IReadRepository` means application-layer handlers never reference `DbContext` directly — they work exclusively with injected repository interfaces, keeping the application layer portable. `EfUnitOfWork` as the sole save boundary enforces the rule that business logic cannot accidentally persist changes by calling `SaveChanges` directly on the context. `SpecificationEvaluator` in a sealed concrete class means specification application logic is defined once, tested once, and applied consistently across every read operation in every microservice that uses EfCore persistence.

#### Acceptance criteria
- [ ] `EfRepository<TAggregate, TId>` is abstract; implements all four write methods; never exposes `IQueryable<TAggregate>`
- [ ] `EfReadRepository<TAggregate, TId>` is abstract; all five read methods work correctly; `AsNoTracking()` applied when `spec.AsNoTracking == true`
- [ ] `EfUnitOfWork` is sealed; delegates to `SharedKernelDbContext.SaveChangesAsync`; no additional logic
- [ ] `SpecificationEvaluator<T>` applies operations in the documented order; paging is provably last (unit test verifies `Skip`/`Take` appear after `OrderBy` in the generated query)
- [ ] `SpecificationEvaluator<T>` ignores `ThenBys` when no primary sort is set
- [ ] Null `Criteria` specification matches all entities (no `Where` clause added)
- [ ] All types carry XML doc comments
---

---
### P-070 — Persistence EfCore Multi-Tenancy: TenantedDbContext and Tenant Query Filter

**Status:** `●` Complete
**Work Order:** WO-013
**Domain:** 06.Persistence
**Depends on:** P-069

#### What is needed

Multi-tenant data isolation support in `SharedKernel.Persistence.EfCore`. This capability adds a tenant-filtered DbContext base and a tenant repository wrapper for services that use tenant-scoped aggregates.

**`ICurrentTenantService` (`MultiTenancy/`):**
A simple interface exposing `TenantId` (Guid?) — returns the current tenant from wherever it was resolved (HTTP header, JWT claim, etc.). This interface is defined in `SharedKernel.Persistence.EfCore` (not in Abstractions) because it is an EfCore-layer concern: the DbContext needs it to apply a global query filter. The concrete implementation lives in `13.ServiceDefaults.MultiTenancy`.

**`TenantedDbContext` (`MultiTenancy/`):**
Abstract base class extending `SharedKernelDbContext`. Overrides `OnModelCreating` to install a global query filter on all entities implementing `IHasTenant`: `e => e.TenantId == currentTenantService.TenantId` (where `TenantId` is resolved from `ICurrentTenantService` at query execution time, not at model-build time — the filter must capture the service, not a snapshot of the tenant ID). Accepts `ICurrentTenantService` via constructor alongside `DbContextOptions`. Downstream multi-tenant DbContext subclasses extend `TenantedDbContext` instead of `SharedKernelDbContext`.

**`TenantedRepository<TAggregate, TId>` (`MultiTenancy/`):**
Abstract class extending `EfRepository<TAggregate, TId>`. Adds `GetByIdForTenantAsync(TId id, Guid tenantId, CancellationToken ct) → Task<TAggregate?>` — a safety method that explicitly scopes a lookup to a specific tenant, bypassing the global filter for cross-tenant administrative operations. Standard `GetByIdAsync` routes through the global filter automatically. The global filter on `TenantedDbContext` means that `GetByIdAsync` from the base `EfRepository` is already tenant-safe for the current-tenant path.

#### Why this is needed

Multi-tenant SaaS services that use `TenantedAggregateRoot<TId>` (from `03.Domain`) must never accidentally query another tenant's data. Global query filters at the DbContext level are the most reliable mechanism for this — they apply to all queries, including those generated by `EfReadRepository` and specification evaluators, without any per-query filter annotation. Without this phase, each team must implement tenant filtering ad-hoc, and the inevitable oversight creates cross-tenant data leakage vulnerabilities.

#### Acceptance criteria
- [ ] `ICurrentTenantService` defined in `SharedKernel.Persistence.EfCore`; exposes `TenantId` as `Guid?`
- [ ] `TenantedDbContext` installs a global query filter on all `IHasTenant` entities using the runtime value of `ICurrentTenantService.TenantId` (not a startup-time snapshot)
- [ ] `TenantedRepository<TAggregate, TId>` provides `GetByIdForTenantAsync` that explicitly scopes by `tenantId`; standard `GetByIdAsync` flows through the global filter
- [ ] A unit test verifies that a query on `TenantedDbContext` without a current tenant either returns nothing or throws a configurable exception (not cross-tenant data)
- [ ] All types carry XML doc comments
---

---
### P-071 — DEFERRED: Persistence PostgreSQL Package

**Status:** `●` Complete
**Work Order:** WO-013
**Domain:** 06.Persistence

`SharedKernel.Persistence.PostgreSQL` (SnakeCaseNamingConvention, JSONB, pgvector, `UsePostgreSQL` DI extension) is out of scope for WO-013. It will be planned and implemented in a future work order once the EfCore layer (P-065 to P-070, P-073 to P-076) is complete and microservices begin integrating the persistence packages.

---

---
### P-072 — DEFERRED: Persistence Dapper Package

**Status:** `●` Complete
**Work Order:** WO-013
**Domain:** 06.Persistence

`SharedKernel.Persistence.Dapper` (NpgsqlConnectionFactory, StronglyTypedIdTypeHandler, SmartEnumTypeHandler, DapperReadService) is out of scope for WO-013. Dapper is a read-side concern; it will be scoped into a dedicated future work order alongside the PostgreSQL package once a microservice CQRS read-side need drives it.

---

---

### P-073 — Persistence DI Extensions: AddSharedKernelEfCore Builder

**Status:** `●` Complete
**Work Order:** WO-013
**Domain:** 06.Persistence
**Depends on:** P-070

#### What is needed

The DI extension methods in `SharedKernel.Persistence.EfCore` that wire up the EfCore persistence layer for consuming microservices. This phase provides a fluent builder that composes all required registrations in one call and validates the configuration at startup.

**`EfCorePersistenceBuilder` — fluent DI builder (`Extensions/`):**
A builder type returned by `AddSharedKernelEfCore<TContext>(IServiceCollection services, Action<DbContextOptionsBuilder> configureDb)` where `TContext : SharedKernelDbContext`. Provides a fluent API for optional capabilities:

- `.WithMultiTenancy()` — asserts that `TContext` extends `TenantedDbContext`; registers `ICurrentTenantService` as a **scoped placeholder** (an empty no-op implementation) so the DI graph resolves at startup — the concrete implementation is overridden by `13.ServiceDefaults.MultiTenancy` in consuming services. This placeholder exists only to prevent startup failures in services that call `.WithMultiTenancy()` before the MultiTenancy package wires its own implementation.
- `.Build()` — registers:
  - `TContext` as `DbContext` (scoped)
  - `IUnitOfWork` → `EfUnitOfWork` (scoped)
  - `ISpecificationEvaluator<T>` → `SpecificationEvaluator<T>` (singleton — stateless)
  - `AuditInterceptor`, `SoftDeleteInterceptor`, `ConcurrencyInterceptor` as scoped services (required by EF Core interceptor DI)

**Startup guard in `.Build()`:**
If `.WithMultiTenancy()` was called but `TContext` does not extend `TenantedDbContext`, `.Build()` throws `InvalidOperationException` with a clear message at startup. This prevents the common mistake of calling `.WithMultiTenancy()` while using the wrong base DbContext.

**`IUserContext` dependency resolution:**
`AuditInterceptor` and `SoftDeleteInterceptor` require `IUserContext` (from `12.Security.Abstractions`). `.Build()` registers a scoped no-op `IUserContext` placeholder (returns `"system"`) if no `IUserContext` is already registered — consuming services override it by registering their own implementation before or after calling `.Build()`. The last registration wins.

#### Why this is needed

Without a builder, consuming services must register `EfUnitOfWork`, `SpecificationEvaluator<T>`, and all three scoped interceptors individually in exactly the right order. Missing any registration causes a cryptic runtime failure on the first `SaveChangesAsync`. The builder makes the correct configuration the path of least resistance and catches misconfiguration at startup rather than at first use.

#### Acceptance criteria

- [ ] `AddSharedKernelEfCore<TContext>(services, configureDb)` returns an `EfCorePersistenceBuilder` instance
- [ ] `.Build()` registers `TContext` as `DbContext`, `IUnitOfWork → EfUnitOfWork`, `ISpecificationEvaluator<T> → SpecificationEvaluator<T>`, and all three interceptors as scoped services
- [ ] `.WithMultiTenancy()` registers the no-op `ICurrentTenantService` placeholder; `.Build()` throws if `TContext` does not extend `TenantedDbContext`
- [ ] No-op `IUserContext` placeholder registered only when no `IUserContext` is already in the container
- [ ] No outbox, Dapper, or PostgreSQL wiring in this builder — those are separate concerns
- [ ] A smoke test (no real database) verifies `.Build()` with `.WithMultiTenancy()` on a non-tenanted context throws at startup
- [ ] All public extension methods carry XML doc comments
---

---

### P-074 — Persistence Tests: EfCore Interceptors, Repository, Evaluator, Multi-Tenancy

**Status:** `●` Complete
**Work Order:** WO-013
**Domain:** 06.Persistence
**Depends on:** P-073, P-076

#### What is needed

The full test suite for both EfCore persistence packages, using SQLite for in-process integration tests. Tests span two projects nested inside their respective package folders.

**`SharedKernel.Persistence.Abstractions.Tests/` (unit tests):**

- Interface contract shape: verify all expected methods present on `IRepository`, `IReadRepository`, `IUnitOfWork`, `IDbConnectionFactory`, `ISpecificationEvaluator`
- `OutboxMessage` is absent — assert no outbox types exist in this package

**`SharedKernel.Persistence.EfCore.Tests/` (unit + SQLite integration):**

- `AuditInterceptor`: Added entity → `CreatedBy`/`CreatedOn` set; Modified entity → `ModifiedBy`/`ModifiedOn` set; Deleted non-soft-deletable entity → no audit mutation
- `SoftDeleteInterceptor`: Deleted `ISoftDeletable` entity → state changed to Modified; `IsDeleted = true`; `DeletedOn` and `DeletedBy` set; soft-deleted entity invisible via global filter after reload
- `ConcurrencyInterceptor`: `DbUpdateConcurrencyException` on `IHasConcurrency` entity → rethrown as `ConcurrencyException` with `Error.Conflict`; non-concurrency exceptions propagate unchanged
- `SpecificationEvaluator<T>`: criteria, ordering (ascending/descending), secondary sorts (ThenBys), paging, distinct, AsNoTracking — each verified independently; paging provably last (verify LINQ expression tree order); null criteria matches all entities
- `EfRepository`/`EfReadRepository`: read-write round-trip with SQLite provider; `GetBySpecAsync` with a real specification returns the correct entity; `ListAsync` with `PagedSpecification` returns the correct page
- `StronglyTypedIdValueConverter`: round-trip — write entity with strongly-typed ID, read back, assert ID value equal
- `TenantedDbContext`: global query filter isolates records by `TenantId`; a query with no current tenant returns zero rows (not cross-tenant data); `GetByIdForTenantAsync` bypasses the filter and returns the correct entity by explicit tenant scope
- `EfCorePersistenceBuilder` smoke tests: `.Build()` with `.WithMultiTenancy()` on a non-`TenantedDbContext` throws `InvalidOperationException` at startup

All tests use the `TestSharedKernelDbContext` from `SharedKernel.Testing` (P-076) with SQLite in-memory provider — no Testcontainers needed for this phase.

#### Why this is needed

The persistence layer is the most critical infrastructure boundary in the platform — incorrect interceptor behavior causes data loss (missing audits), compliance failures (missing soft-deletes), or cross-tenant data leakage. Every component must be verified against a real EF `SaveChanges` pipeline, not just isolated unit calls. SQLite covers all EF Core LINQ and interceptor behavior at zero infrastructure cost.

#### Acceptance criteria

- [ ] Both test projects build cleanly
- [ ] `AuditInterceptor` tests: Added, Modified, and non-soft-deletable Deleted scenarios all verified
- [ ] `SoftDeleteInterceptor` tests: state transition verified; soft-deleted entity invisible via global filter after reload
- [ ] `ConcurrencyInterceptor` tests: exception type, `Error.Conflict` payload, and non-concurrency passthrough all verified
- [ ] `SpecificationEvaluator` tests: all six operations (criteria, includes, orderby, thenbys, distinct, asnotracking, skip/take) verified independently; paging-last order proven
- [ ] `EfRepository`/`EfReadRepository` round-trip tests with SQLite pass
- [ ] `StronglyTypedIdValueConverter` round-trip test passes
- [ ] `TenantedDbContext` filter tests: tenant isolation and `GetByIdForTenantAsync` bypass both verified
- [ ] Builder smoke test: `.WithMultiTenancy()` on wrong context type throws at startup
- [ ] All tests use helpers from `SharedKernel.Testing`; no mocked database connections in integration tests

---

---

### P-075 — Governance: Persistence Architecture Rules

**Status:** `●` Complete
**Work Order:** WO-013
**Domain:** 00.Governance
**Depends on:** P-066

#### What is needed

New architecture enforcement rules in `00.Governance/SharedKernel.ArchitectureTests` protecting the EfCore persistence layer's key contracts. Three rules — Dapper and PostgreSQL rules are deferred alongside those packages.

**Rule 1 — IUnitOfWork is the only permitted save boundary:**
No class in any assembly other than `SharedKernel.Persistence.EfCore` may call `DbContext.SaveChanges[Async]` directly. Only `EfUnitOfWork` may reference `DbContext.SaveChangesAsync`. Services that inject `IUnitOfWork` are compliant; those that call `SaveChanges` directly on an injected `DbContext` are not.

**Rule 2 — IRepository must never expose IQueryable:**
No member of any type implementing `IRepository<T,TId>` may return `IQueryable<T>`. The write-side repository contract is narrowly scoped to mutation operations. All query surface belongs on `IReadRepository` via specifications.

**Rule 3 — No persistence references in Domain layer:**
Types in the `03.Domain` namespace must not reference any type from `Microsoft.EntityFrameworkCore`, `Npgsql`, or `SharedKernel.Persistence.*`. This enforces the hard layering rule from root `CLAUDE.md`.

#### Why this is needed

These three rules catch the most damaging persistence anti-patterns before they merge: `SaveChanges` called directly (bypasses interceptors, breaks audit and soft-delete), `IQueryable` exposed on a repository (breaks the read/write separation and couples application handlers to EF internals), and EF Core referenced inside the Domain layer (violates DDD isolation and makes domain logic impossible to test without a database).

#### Acceptance criteria

- [ ] Rule 1 exists and tested: a class calling `SaveChangesAsync` directly fails; a class using only `IUnitOfWork` passes
- [ ] Rule 2 exists and tested: a repository type with `IQueryable<T>` return fails; `IRepository<T,TId>` without `IQueryable` passes
- [ ] Rule 3 exists and tested: a domain type referencing `EntityFrameworkCore` fails; a clean domain type passes
- [ ] All three rules documented in `00.Governance/CLAUDE.md` with rationale and offending/compliant pattern examples
- [ ] All rules run cleanly against the current SharedKernel codebase without false positives

---

---

### P-076 — Testing: EfCore Test Helpers — Test DbContext, Domain Fakers, EF Extensions

**Status:** `○` Pending
**Work Order:** WO-013
**Domain:** 16.Testing
**Depends on:** P-066

#### What is needed

Extensions to `SharedKernel.Testing` that provide EfCore-specific test infrastructure consumed by P-074 and by any downstream microservice test project. No Testcontainers or PostgreSQL infrastructure is required — all tests in P-074 use SQLite in-memory.

**EfCore test DbContext base (`EfCore/`):**
A `TestSharedKernelDbContext` abstract class that extends `SharedKernelDbContext` with SQLite in-memory provider preconfigured for testing. This base:

- Wires a no-op `IUserContext` (returns fixed string `"test-user"`) so `AuditInterceptor` resolves without a real HTTP context
- Wires a deterministic `IClock` returning a fixed `DateTimeOffset.UtcNow` snapshot so interceptor timestamps are stable across test runs
- Enables `EnableSensitiveDataLogging()` for readable test diagnostics
- Exposes `EnsureCreatedAsync()` helper for test setup — no migrations needed in SQLite tests

**Persistence-aware domain fakers (`Fakers/`):**

- `AggregateRootFaker<TAggregate, TId>` — abstract Bogus `Faker<TAggregate>` base; pre-configures `CreatedBy`, `CreatedOn`, `IsDeleted = false` matching EF interceptor expectations; concrete fakers in microservice test projects extend this
- `TenantedAggregateFaker<TAggregate, TId>` — extends `AggregateRootFaker` with a populated, non-empty `TenantId` (Guid)

**EF Core test helpers (`Helpers/`):**

- `EfContextExtensions.DetachAll(DbContext)` — detaches all tracked entities, enabling a fresh load from the same in-memory database in the same test
- `EfContextExtensions.ReloadAsync<T>(DbContext, T entity)` — loads a fresh copy of an entity via a new `DbContext` instance scoped to the same provider, asserting round-trip persistence

#### Why this is needed

Every persistence test project that verifies interceptor behavior must create a `SharedKernelDbContext` subclass wired with the same interceptor pipeline as production. Without the `TestSharedKernelDbContext` base, teams must re-implement the fake `IUserContext` and `IClock` wiring in each test project, introducing subtle divergences. The fakers ensure that test aggregates arrive in a valid persisted state (with audit fields populated), eliminating false failures caused by missing required columns.

#### Acceptance criteria

- [ ] `TestSharedKernelDbContext` abstract class exists with SQLite configuration, no-op `IUserContext`, deterministic `IClock`, sensitive logging, and `EnsureCreatedAsync()`
- [ ] `AggregateRootFaker<TAggregate, TId>` abstract base exists; generates valid `CreatedBy`, `CreatedOn`, `IsDeleted = false` defaults
- [ ] `TenantedAggregateFaker<TAggregate, TId>` generates a non-empty `TenantId`
- [ ] `EfContextExtensions.DetachAll()` and `ReloadAsync<T>()` both implemented and self-tested
- [ ] No `OutboxMessageFaker`, no `OutboxAssertions`, no PostgreSQL testcontainer in this phase — those belong to future WOs
- [ ] `SharedKernel.Testing` csproj references `SharedKernel.Persistence.Abstractions` and `SharedKernel.Persistence.EfCore`
- [ ] All helpers have self-tests in `SharedKernel.Testing.Tests`

---
### P-077 — Security Abstractions: IUserContext and ITenantProvider Package

**Status:** `●` Complete
**Work Order:** WO-014
**Domain:** 12.Security
**Depends on:** None

#### What is needed

Create the `SharedKernel.Security.Abstractions` package under `12.Security/`. This package is a pure interface library — zero NuGet dependencies, references only `SharedKernel.Primitives` and `SharedKernel.Core`.

**Interfaces to define:**

`IUserContext` — exposes the identity of the currently authenticated principal. Must surface at minimum: `UserId` (string), `UserName` (string, optional), `Email` (string, optional), `Roles` (IReadOnlyList<string>), `IsAuthenticated` (bool). The `UserId` property is the primary consumer of `AuditInterceptor` and `SoftDeleteInterceptor` in `06.Persistence.EfCore`. A fallback pattern: when not authenticated, `UserId` returns a conventional constant (e.g., `"system"`) and `IsAuthenticated` returns `false` — never null, never throws.

`ITenantProvider` — exposes the current tenant identity. Surfaces: `TenantId` (Guid?). Nullable because background jobs, migrations, and system-initiated operations run without a tenant context. This is the exact contract currently defined as `ICurrentTenantService` inside `06.Persistence.EfCore.MultiTenancy`. The name `ITenantProvider` is preferred as it is generic enough to be consumed by Application, Communication, and ServiceDefaults layers — not just Persistence. The old `ICurrentTenantService` name is too EF-specific; it becomes an alias or is removed in P-078.

Both interfaces must carry full XML doc comments, including the fallback/no-context behavior contract.

No implementations ship in this package — those are in `SharedKernel.Security.Oidc`, `13.ServiceDefaults.MultiTenancy`, etc.

#### Why this is needed

Both `IUserContext` and `ICurrentTenantService` are currently duplicated/misplaced in `06.Persistence.EfCore` because the layering rules appeared to prohibit a reference from `06.Persistence` to `12.Security`. In reality the prohibition is against concrete security infrastructure (JWT parsing, OIDC tokens, Azure B2C SDKs) — not against a pure zero-dependency interface library. Moving these contracts to `12.Security.Abstractions` corrects the ownership: identity and tenancy are security concerns, not EF Core concerns. With a canonical home, these interfaces can be consumed by `05.Application` pipeline behaviors, `11.Communication` typed clients, `13.ServiceDefaults` middleware, and `06.Persistence` interceptors — all without each layer defining its own local copy.

#### Acceptance criteria

- [ ] `SharedKernel.Security.Abstractions` csproj exists in `12.Security/SharedKernel.Security.Abstractions/`; references only `SharedKernel.Primitives` and `SharedKernel.Core`; zero third-party NuGet dependencies
- [ ] `IUserContext` interface exposes `UserId`, `UserName`, `Email`, `Roles`, `IsAuthenticated` with the no-null, no-throw fallback contract documented
- [ ] `ITenantProvider` interface exposes `TenantId` (Guid?) with the nullable-means-no-tenant-context contract documented
- [ ] Both interfaces have full XML doc comments covering the fallback/system behavior
- [ ] Package is registered in the solution file under the `12.Security` solution folder
- [ ] A `ContractShapeTests` test confirms the interfaces are present, have the correct member signatures, and live in the `SharedKernel.Security.Abstractions` namespace

---
### P-078 — Persistence EfCore: Migrate IUserContext and ICurrentTenantService to Security.Abstractions

**Status:** `●` Complete
**Work Order:** WO-014
**Domain:** 06.Persistence
**Depends on:** P-077

#### What is needed

Migrate `06.Persistence.EfCore` to consume `IUserContext` and `ITenantProvider` from `SharedKernel.Security.Abstractions` (created in P-077) instead of declaring its own local copies.

**Changes required:**

1. Add a project reference from `SharedKernel.Persistence.EfCore` to `SharedKernel.Security.Abstractions`.
2. Delete `06.Persistence.EfCore/Interceptors/IUserContext.cs` — replaced by `IUserContext` from `12.Security.Abstractions`.
3. Delete `06.Persistence.EfCore/MultiTenancy/ICurrentTenantService.cs` — replaced by `ITenantProvider` from `12.Security.Abstractions`.
4. Update `AuditInterceptor` and `SoftDeleteInterceptor` to inject `IUserContext` from the `SharedKernel.Security.Abstractions` namespace. The `UserId` property is the same string accessor — no behavioral change.
5. Update `TenantedDbContext` to inject `ITenantProvider` (instead of `ICurrentTenantService`). The `TenantId` property is the same `Guid?` — no behavioral change in the global query filter.
6. Update `EfCorePersistenceBuilder.WithMultiTenancy()` to register a no-op `ITenantProvider` placeholder instead of `ICurrentTenantService`.
7. Update `EfCorePersistenceBuilder.Build()` to register a no-op `IUserContext` placeholder (returning `"system"`, `IsAuthenticated = false`) if no `IUserContext` is already registered.
8. Delete `NoOpUserContext.cs` and `NoOpCurrentTenantService.cs` — replace with new no-op impls that implement the updated interfaces from `12.Security.Abstractions`.
9. Update `06.Persistence/CLAUDE.md` to reflect the new dependency and remove the workaround documentation.
10. Update `TenantedRepository.cs` — no changes needed (it does not inject `ICurrentTenantService` directly).

The `EfCorePersistenceBuilder` DI registration shape changes only in the interface names used — the startup API (`.WithMultiTenancy()`, `.Build()`) remains unchanged for consumers.

#### Why this is needed

The local `IUserContext` and `ICurrentTenantService` declarations in `06.Persistence.EfCore` are workarounds for a perceived layering constraint. Now that `12.Security.Abstractions` is a pure interface-only package with no infrastructure dependencies, `06.Persistence.EfCore` referencing it is architecturally correct — the same way `06.Persistence.EfCore` already references `SharedKernel.Domain` (also an abstractions layer). Removing the local duplicates eliminates divergence risk: a service that consumes both `IUserContext` from Security and from EfCore currently gets two different types that look the same but are not the same contract.

#### Acceptance criteria

- [ ] `06.Persistence.EfCore.csproj` references `SharedKernel.Security.Abstractions`
- [ ] `IUserContext.cs` file is deleted from `06.Persistence.EfCore/Interceptors/`
- [ ] `ICurrentTenantService.cs` file is deleted from `06.Persistence.EfCore/MultiTenancy/`
- [ ] `AuditInterceptor` and `SoftDeleteInterceptor` compile against `IUserContext` from `SharedKernel.Security.Abstractions`
- [ ] `TenantedDbContext` compiles against `ITenantProvider` from `SharedKernel.Security.Abstractions`
- [ ] `EfCorePersistenceBuilder` registers no-op `IUserContext` and `ITenantProvider` placeholders using the types from `12.Security.Abstractions`
- [ ] All existing `SharedKernel.Persistence.EfCore.Tests` pass without modification (behavioral change is zero)
- [ ] `06.Persistence/CLAUDE.md` updated to remove the IUserContext-workaround documentation and replace it with the correct `SharedKernel.Security.Abstractions` reference documentation

---
### P-079 — Persistence Abstractions: Clean Up IDbConnectionFactory Doc + Add Projection Specification Contract

**Status:** `●` Complete
**Work Order:** WO-014
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

Two targeted changes to `SharedKernel.Persistence.Abstractions`:

**Fix 1 — Remove "Dapper-exclusive" restriction from `IDbConnectionFactory` XML doc.**
The current doc comment says *"This factory is used exclusively by Dapper read-side services."* This is an overly restrictive statement on an abstraction. `IDbConnectionFactory` returns `Task<IDbConnection>` — a BCL type. Any consumer that needs raw SQL access (ADO.NET bulk operations, custom micro-ORM, Testcontainers connection injection) can legitimately use this factory. Remove the "exclusively Dapper" language and replace it with a neutral contract description: factory for producing open database connections, caller is responsible for disposal, connection pooling is provider-managed.

**Fix 2 — Add `IProjectionSpecification<TAggregate, TResult>` contract.**
The current `ISpecification<T>` in `SharedKernel.Domain` covers filtering, ordering, paging, and includes. But there is no way to express a projection (a `Select` expression) at the abstraction level. Add `IProjectionSpecification<TAggregate, TResult>` to `SharedKernel.Persistence.Abstractions/Specifications/`. This interface extends `ISpecification<TAggregate>` with one additional member: a `Selector` expression (`Expression<Func<TAggregate, TResult>>`). This is the contract that `IReadRepository.ListProjectedAsync<TResult>` and `IReadRepository.GetBySpecProjectedAsync<TResult>` (added in P-080) depend on. The interface itself lives in Abstractions; the EF Core implementation of the selector application lives in `SpecificationEvaluator` (updated in P-080).

#### Why this is needed

`IDbConnectionFactory`'s doc comment silently locks down a legitimate abstraction — teams reading it will avoid using the factory for non-Dapper raw SQL scenarios and either bypass it or duplicate it. The doc fix aligns the abstraction with its actual semantic contract.

The projection specification gap is the single most common reason teams reach around the repository at scale. When a read-side handler needs a lightweight DTO projection from a large aggregate table, materializing full aggregates wastes memory and CPU. Without a first-class projection contract, developers will expose `IQueryable`, add a custom `DbContext` service, or implement their own `DapperReadService` even when EF Core is sufficient. The `IProjectionSpecification<TAggregate, TResult>` contract solves this within the specification pattern without breaking the IQueryable encapsulation rule.

#### Acceptance criteria

- [ ] `IDbConnectionFactory` XML doc no longer contains "exclusively by Dapper" language; the contract describes what the factory does without restricting who may use it
- [ ] `IProjectionSpecification<TAggregate, TResult>` interface exists in `SharedKernel.Persistence.Abstractions/Specifications/`; it extends `ISpecification<TAggregate>` and adds a `Selector` property of type `Expression<Func<TAggregate, TResult>>`
- [ ] Full XML doc comments on the new interface describing the contract
- [ ] `ContractShapeTests` in `SharedKernel.Persistence.Abstractions.Tests` verify the new interface shape
- [ ] Zero NuGet dependencies introduced (System.Linq.Expressions is BCL)

---
### P-080 — Persistence EfCore: Bulk Operations, Projection Reads, Paged Result, Domain Event Dispatch, and IQueryable Leak Fix

**Status:** `●` Complete
**Work Order:** WO-014
**Domain:** 06.Persistence
**Depends on:** P-079, P-081, P-095

#### What is needed

A comprehensive feature addition and correctness hardening pass on `SharedKernel.Persistence.EfCore`. Six independent capabilities, all delivered in this phase because they share the same set of implementation files and tests.

**Capability 1 — Bulk write operations on `IRepository` and `EfRepository`.**
Add `AddRangeAsync(IEnumerable<TAggregate>, CancellationToken)`, `UpdateRangeAsync(IEnumerable<TAggregate>, CancellationToken)`, and `DeleteRangeAsync(IEnumerable<TAggregate>, CancellationToken)` to `IRepository<TAggregate, TId>` in Abstractions and implement them in `EfRepository<TAggregate, TId>`. Mutations are staged (not committed) until `IUnitOfWork.SaveChangesAsync` is called — same semantics as the single-entity methods. `EfRepository` implementations use `DbContext.Set<T>().AddRangeAsync`, `UpdateRange`, and `RemoveRange` respectively.

**Capability 2 — Projection read methods on `IReadRepository` and `EfReadRepository`.**
Add two new methods to `IReadRepository<TAggregate, TId>` (in Abstractions):
- `ListProjectedAsync<TResult>(IProjectionSpecification<TAggregate, TResult> spec, CancellationToken ct)` returns `Task<IReadOnlyList<TResult>>`
- `GetBySpecProjectedAsync<TResult>(IProjectionSpecification<TAggregate, TResult> spec, CancellationToken ct)` returns `Task<TResult?>`

Update `EfReadRepository` to implement both. Update `SpecificationEvaluator<T>` to handle `IProjectionSpecification<T, TResult>`: after all criteria/ordering/paging steps, apply `.Select(spec.Selector)` and materialize as `TResult`. The selector is applied after paging to maintain the paging-last invariant.

**Capability 3 — Paged result from a single round-trip.**
Add `ListPagedAsync(ISpecification<TAggregate> spec, CancellationToken ct)` returning `Task<PagedList<TAggregate>>` to `IReadRepository` (Abstractions) where `PagedList<T>` is the type already defined in `04.Contracts`. This method issues a count query (using `spec` without Skip/Take) and a data query (using `spec` with Skip/Take) as two database round-trips under the same `DbContext` scope.

**Capability 4 — Domain event dispatch hook in `EfUnitOfWork`.**
Update `EfUnitOfWork` to accept an optional `IDomainEventDispatcher` from `SharedKernel.Domain` (`03.Domain`). After `_dbContext.SaveChangesAsync(ct)` succeeds, `EfUnitOfWork` collects all domain events from `IHasDomainEvents` tracked entities via `ChangeTracker.Entries<IHasDomainEvents>()`, calls `IDomainEventDispatcher.DispatchAsync(events, ct)`, and clears the event collection on each aggregate. The dispatcher is optional — consuming services opt in by registering an `IDomainEventDispatcher`. No reference to `05.Application` is introduced — `IDomainEventDispatcher` lives in `03.Domain` which `06.Persistence` already references.

**Capability 5 — Fix `QueryableExtensions.IgnoreSoftDeleteFilter` IQueryable leakage.**
Remove `QueryableExtensions.IgnoreSoftDeleteFilter` entirely. Add a boolean flag `IncludeDeleted` to `ISpecification<T>` in `SharedKernel.Domain`. Handle it in `SpecificationEvaluator`: when `spec.IncludeDeleted == true`, call `.IgnoreQueryFilters()` on the query before applying criteria. The `QueryableExtensions` class is deleted.

**Capability 6 — Fix `GetByIdAsync` duplication between `IRepository` and `IReadRepository`.**
Remove `GetByIdAsync` from `IReadRepository<TAggregate, TId>`. Provide a `ByIdSpecification<TAggregate, TId>` convenience specification in the Abstractions package. `EfReadRepository.GetByIdAsync` is also removed. This is a breaking change — document with migration guide in `06.Persistence/CLAUDE.md`.

#### Why this is needed

These six capabilities represent the most common points where teams across hundreds of services will bypass the repository pattern. Each gap is a known failure mode at microservice scale: bulk bypass via `DbContext.AddRange`, projection bypass via `IQueryable` exposure, paged result duplication in every handler, lost domain events without a dispatch hook, `IQueryable` leakage through the soft-delete extension, and tracked/untracked implementation divergence from `GetByIdAsync` duplication.

#### Acceptance criteria

- [ ] `IRepository<TAggregate, TId>` declares `AddRangeAsync`, `UpdateRangeAsync`, `DeleteRangeAsync`; `EfRepository` implements all three
- [ ] `IReadRepository<TAggregate, TId>` declares `ListProjectedAsync<TResult>` and `GetBySpecProjectedAsync<TResult>`; `EfReadRepository` implements both
- [ ] `IReadRepository<TAggregate, TId>` declares `ListPagedAsync` returning `PagedList<TAggregate>`; `EfReadRepository` implements it with count + data queries
- [ ] `EfUnitOfWork` accepts optional `IDomainEventDispatcher`; dispatches events post-commit; clears event collections; no-dispatcher path unchanged
- [ ] `QueryableExtensions` class deleted; `ISpecification<T>.IncludeDeleted` flag added; `SpecificationEvaluator` handles `IgnoreQueryFilters` via the flag
- [ ] `IReadRepository.GetByIdAsync` removed; `ByIdSpecification<TAggregate, TId>` convenience spec added in Abstractions; migration guide in CLAUDE.md
- [ ] All existing tests pass; new unit tests (SQLite in-memory) cover every new capability

---

### P-081 — Domain: IDomainEventDispatcher Interface

**Status:** `●` Complete
**Work Order:** WO-014
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

Add an `IDomainEventDispatcher` interface to `SharedKernel.Domain` (`03.Domain`). This is a single, minimal interface:

`IDomainEventDispatcher` — one method: `DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken ct)` returning `Task`. `IDomainEvent` is already defined in `SharedKernel.Domain` — this interface has zero dependencies outside `03.Domain`.

**Why 03.Domain and not 05.Application:** Dispatching domain events is a domain-layer concept — the interface only references `IDomainEvent` which lives in `03.Domain`. Placing the interface in `05.Application` forces `06.Persistence.EfCore` to take a dependency on the application layer just to wire the optional dispatch hook in `EfUnitOfWork`. That coupling is wrong: `06.Persistence` already references `03.Domain`, so moving the interface there eliminates the cross-layer dependency entirely. The MediatR-based implementation (`MediatRDomainEventDispatcher`, which does depend on MediatR) is a future `05.Application` concern — it is not part of this phase.

`IDomainEventDispatcher` is consumed by `EfUnitOfWork` in `06.Persistence.EfCore` (P-080) as an optional dependency. It is not registered by `EfCorePersistenceBuilder` — consuming services opt in by registering an implementation alongside `.Build()`.

#### Why this is needed

Domain events collected on aggregates during a command must be dispatched after the unit of work commits. Without a first-class interface in `03.Domain`, the persistence layer cannot express the optional dispatch hook without coupling to the application layer. Locating the interface in `03.Domain` means any layer from 03 upward (Persistence, Application, ServiceDefaults) can implement or consume it without layering violations. The MediatR-based default implementation is a separate future phase in `05.Application`.

#### Acceptance criteria

- [ ] `IDomainEventDispatcher` interface exists in `SharedKernel.Domain` with `DispatchAsync(IReadOnlyList<IDomainEvent>, CancellationToken)` returning `Task`
- [ ] Interface lives in the `SharedKernel.Domain` namespace under `03.Domain/SharedKernel.Domain/`
- [ ] Zero NuGet dependencies introduced — interface only references `IDomainEvent` which is already in the package
- [ ] Empty event list is treated as a no-op by any conforming implementation (document in XML doc)
- [ ] Handler exceptions must propagate unchanged — document in XML doc; no swallowing
- [ ] Full XML doc comments on the interface including the opt-in DI contract and no-op empty-list behaviour
- [ ] `ContractShapeTests` in `SharedKernel.Domain.Tests` verify interface shape and namespace
- [ ] `03.Domain/CLAUDE.md` updated to list `IDomainEventDispatcher` in the public surface

---
### P-082 — Persistence EfCore: Fix TenantedDbContext Reflection in OnModelCreating

**Status:** `●` Complete
**Work Order:** WO-014
**Domain:** 06.Persistence
**Depends on:** P-078

#### What is needed

Two targeted fixes to remove runtime reflection from EF Core model-building paths.

**Fix 1 — `TenantedDbContext.OnModelCreating` reflection elimination.**
The current implementation uses `GetMethod(...).MakeGenericMethod(entityType.ClrType).Invoke(this, ...)` to apply the tenant query filter for each `IHasTenant` entity. Replace this with an expression-tree-based approach that constructs the `HasQueryFilter` lambda directly without reflection on the CLR type. The approach: for each entity type implementing `IHasTenant`, build a lambda `e => e.TenantId == CurrentTenantService.TenantId` using `Expression.Parameter`, `Expression.Property` (accessing the `TenantId` property by name), `Expression.Field` or `Expression.Property` for `CurrentTenantService.TenantId`, and `Expression.Equal`. Then call `modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda)` via the non-generic `Entity(Type)` overload. No `MakeGenericMethod` or `Invoke` calls remain.

**Fix 2 — `ValueObjectOwnershipConvention` early-exit when model has no `IValueObject` properties.**
Add an early-exit at the top of `ValueObjectOwnershipConvention.Apply`: scan for any entity with at least one `IValueObject` property. If none found, return immediately without allocating the candidates list. This is an O(n) scan-once optimization, not a full rewrite. Document in `06.Persistence/CLAUDE.md` that the convention is opt-in and has O(n*m) startup cost (n entities × m properties each) — callers should be aware at large model scale.

#### Why this is needed

The reflection in `TenantedDbContext.OnModelCreating` violates the AOT-preferred guidance and adds measurable cold-start latency on K8s-native services where compiled EF Core models are used. Reflection during model finalization is especially problematic with NativeAOT builds (even if not mandatory today, the migration path should be clear). Removing reflection from the hot model-building path makes the code auditable and forward-compatible.

#### Acceptance criteria

- [ ] `TenantedDbContext.OnModelCreating` contains no `GetMethod`, `MakeGenericMethod`, or `Invoke` calls
- [ ] Tenant query filter is applied via expression trees (`Expression.Parameter`, `Expression.Property`, `Expression.Equal`, `Expression.Lambda`)
- [ ] `modelBuilder.Entity(clrType).HasQueryFilter(lambdaExpr)` called via non-generic `Entity(Type)` overload
- [ ] All existing `TenantedDbContextTests` pass; new tests added confirming the filter fires correctly for `IHasTenant` entities post-refactor
- [ ] `ValueObjectOwnershipConvention.Apply` returns immediately (no candidate list allocation) when no `IValueObject` properties are found
- [ ] `06.Persistence/CLAUDE.md` updated with reflection-elimination note and `ValueObjectOwnershipConvention` startup-cost guidance

---
### P-083 — Governance: Persistence Architecture Rules Phase 2 — Interface Migration Enforcement

**Status:** `●` Complete
**Work Order:** WO-014
**Domain:** 00.Governance
**Depends on:** P-078

#### What is needed

Extend `SharedKernel.ArchitectureTests` with additional persistence architecture enforcement rules that codify the corrections from this work order.

**New rules to add:**

1. **`IUserContext` must be declared only in `SharedKernel.Security.Abstractions`** — No type named `IUserContext` may be declared in any package other than `SharedKernel.Security.Abstractions`. Catches future re-introduction of local copies in persistence or application layers.

2. **`ITenantProvider` (and `ICurrentTenantService`) must be declared only in `SharedKernel.Security.Abstractions`** — Same rule pattern for tenant identity contracts.

3. **`IQueryable<T>` must not appear on any public method or property of `IRepository` or `IReadRepository` implementors** — Extends the existing IQueryable enforcement to cover the repository abstraction surface explicitly. Catches patterns like the deleted `IgnoreSoftDeleteFilter` if they reappear on any repository class.

4. **No `GetByIdAsync` method on any `IReadRepository` implementor** — Codifies the removal from P-080. Any class implementing `IReadRepository<,>` that also declares a `GetByIdAsync` method fails this rule. This prevents the duplication anti-pattern from being reintroduced.

#### Why this is needed

Architecture test rules are the durable enforcement layer for `state-map.md` decisions. Without automated rules, migrations like P-078 will regress as teams copy old patterns from wiki pages or service templates that predate the fix. Each rule here directly corresponds to a correctness decision made in this work order.

#### Acceptance criteria

- [ ] All four new architecture rules are implemented as `[Fact]` tests in `SharedKernel.ArchitectureTests`
- [ ] Each test carries a clear failure message pointing to the correct pattern and package
- [ ] All new tests pass against the codebase after P-078 and P-080 are complete
- [ ] `00.Governance/CLAUDE.md` updated with the four new rules under the Persistence Enforcement section

---
### P-084 — Testing: Projection, Bulk, and Paged Read Test Helpers

**Status:** `○` Pending
**Work Order:** WO-014
**Domain:** 16.Testing
**Depends on:** P-080

#### What is needed

Extend `SharedKernel.Testing` with helpers for the new repository surface introduced in P-080.

**Projection specification helpers (`EfCore/Specifications/`):**
A `ProjectionSpecificationBuilder<TAggregate, TResult>` fluent builder that allows test code to construct `IProjectionSpecification<TAggregate, TResult>` instances concisely without declaring a full concrete spec class per test. Exposes at minimum: `WithCriteria(Expression<Func<TAggregate, bool>>)`, `WithSelector(Expression<Func<TAggregate, TResult>>)`, `Build()`.

**Paged read assertion helpers (`EfCore/Assertions/`):**
`PagedListAssertions` — static assertion helpers for `PagedList<T>` (from `04.Contracts`): `ShouldHaveTotalCount(int expected)`, `ShouldHaveItems(params T[] expected)`, `ShouldBeEmpty()`. Uses FluentAssertions internally.

**Bulk operation fakers (`EfCore/Fakers/`):**
`BulkAggregateFaker<TAggregate, TId>` — generates a `List<TAggregate>` of configurable count using Bogus with all audit fields populated. Designed for seeding integration test databases with bulk `AddRangeAsync` calls.

**`IncludeDeleted` specification helper:**
A `WithDeletedSpecification<TAggregate>` wrapper that takes any `ISpecification<TAggregate>` and returns a copy with `IncludeDeleted = true`. Used in soft-delete integration tests.

#### Why this is needed

New capabilities without testing scaffolding create an adoption barrier across teams. Projection and bulk APIs need matching test helpers so teams can write deterministic, readable tests without reverting to raw `DbContext.Set<T>()` access — which bypasses the repository and gives false confidence in test coverage.

#### Acceptance criteria

- [ ] `ProjectionSpecificationBuilder<TAggregate, TResult>` exists; produces valid `IProjectionSpecification` instances; self-tested
- [ ] `PagedListAssertions` exists with the three assertion methods; self-tested against constructed `PagedList<T>` instances
- [ ] `BulkAggregateFaker<TAggregate, TId>` generates configurable count with valid audit fields; self-tested
- [ ] `WithDeletedSpecification<TAggregate>` sets `IncludeDeleted = true`; self-tested
- [ ] All helpers have XML doc comments
- [ ] `SharedKernel.Testing.Tests` self-test suite passes with all new helpers covered

---

### P-085 — Security Abstractions: Design — Finalize Interface Contracts

**Status:** `●` Complete
**Work Order:** WO-015
**Domain:** 12.Security
**Depends on:** None

#### What is needed

Finalize and lock down the full public surface of `SharedKernel.Security.Abstractions` before any scaffolding or implementation begins. This is a design-only phase — output is a verified, authoritative contract spec that the implementation phase executes against without ambiguity.

**Surface to finalize:**

`IUserContext` — the request-scoped identity contract. Properties: `UserId` (Guid), `Email` (string?), `Username` (string?), `Roles` (IReadOnlyCollection of string), `Claims` (IReadOnlyDictionary of string to string), `IsAuthenticated` (bool). Method: `HasRole(string role)` returning bool with case-insensitive semantics. Contract invariant: when `IsAuthenticated == true`, `UserId` must never be `Guid.Empty`. When `IsAuthenticated == false`, all string properties are null or empty, collections are empty, `UserId` is `Guid.Empty`.

`ITenantProvider` — the request-scoped tenancy contract. Property: `TenantId` (Guid). Returns `Guid.Empty` when no tenant claim is present — it is non-nullable by design. Callers must treat `Guid.Empty` as "no tenant context" (system-level or unauthenticated request). Domain code must never inject `ITenantProvider` — the application layer resolves `TenantId` and passes it as a primitive to aggregate constructors.

`AnonymousUserContext` — sealed class implementing `IUserContext`. Sentinel for unauthenticated requests. All string properties null, all collections empty, `UserId = Guid.Empty`, `IsAuthenticated = false`, `HasRole` always returns false. This is the fallback registered in DI so `IUserContext` is always resolvable without a real HTTP context.

`SecurityClaimTypes` — static class of `const string` fields mapping well-known claim names: `UserId` ("sub"), `TenantId` ("tenant_id"), `Email` (maps to `ClaimTypes.Email`), `Role` (maps to `ClaimTypes.Role`). These are the canonical names shared between the Abstractions package and any provider implementation — never raw string literals in consumer code.

**Design decisions to confirm:**

- `ITenantProvider.TenantId` is `Guid` (not `Guid?`) — `Guid.Empty` is the no-tenant sentinel.
- `IUserContext.UserId` is `Guid` (not `string`) — typed identity, not raw sub claim string.
- `AnonymousUserContext` is a sealed concrete class that ships in the Abstractions package (the only concrete type that does).
- `IUserContext.Claims` keyed by claim type, first value wins for multi-value claims — roles must always be accessed via `Roles`, never via `Claims`.
- `HasRole` is case-insensitive — role names may arrive with different casing from different identity providers.
- Zero NuGet dependencies — only `SharedKernel.Primitives` project reference.

#### Why this is needed

P-077 was authored before the `12.Security/CLAUDE.md` brain was written and contains a partially outdated surface (`UserId` as `string`, `TenantId` as `Guid?`, no `Claims` dictionary, no `SecurityClaimTypes`). The CLAUDE.md brain is now authoritative. This design phase locks the correct contract before any code is written, ensuring the implementation phase has zero ambiguity and the package ships with the gold-standard surface that all downstream domains (`06.Persistence`, `05.Application`, `11.Communication`, `13.ServiceDefaults`) can depend on.

#### Acceptance criteria

- [ ] `IUserContext` contract is documented: all five properties, `HasRole` signature, `IsAuthenticated`/`UserId` invariant, and scope (request-scoped, never singleton)
- [ ] `ITenantProvider` contract is documented: `TenantId` as `Guid`, `Guid.Empty` semantics, prohibition on domain injection
- [ ] `AnonymousUserContext` contract is documented: all sentinel values, `HasRole` always-false behavior, DI fallback role
- [ ] `SecurityClaimTypes` documented: four constants, their default values, extensibility note (consumers may define additional local constants)
- [ ] Design confirms `Guid`-typed `UserId` and non-nullable `TenantId` — no ambiguity for implementation
- [ ] The design is reviewed against all known consumers: `06.Persistence` interceptors (AuditInterceptor, SoftDeleteInterceptor, TenantedDbContext), `05.Application` pipeline behaviors, `11.Communication` typed clients, `13.ServiceDefaults` tenant resolution

---

### P-086 — Security Abstractions: Scaffold — Project Structure and Solution Registration

**Status:** `●` Complete
**Work Order:** WO-015
**Domain:** 12.Security
**Depends on:** P-085

#### What is needed

Create the physical project structure for `SharedKernel.Security.Abstractions` and register it in the solution. This is scaffold-only — no implementation logic, no interface bodies beyond stubs.

**Deliverables:**

1. `12.Security/SharedKernel.Security.Abstractions/SharedKernel.Security.Abstractions.csproj` — targets `net10.0`; project reference to `SharedKernel.Primitives` only; zero NuGet package references; XML doc generation enabled; pack metadata (Id, Version 1.0.0, Authors, Description, Tags).

2. Folder layout inside the project: `Abstractions/` for `IUserContext.cs` and `ITenantProvider.cs`; `Claims/` for `SecurityClaimTypes.cs`; `Fallback/` for `AnonymousUserContext.cs`.

3. Empty stub files (namespace + type declaration only, no members yet) for each of the four types.

4. `12.Security/SharedKernel.Security.Abstractions/SharedKernel.Security.Abstractions.Tests/` — test sub-project: `classlib`, `net10.0`, references `SharedKernel.Security.Abstractions` and `SharedKernel.Testing`; xUnit, FluentAssertions, coverlet as NuGet references.

5. Both projects registered in `Platform.SharedKernel.slnx` under the `12.Security` solution folder.

#### Why this is needed

A clean scaffold phase separates project wiring from logic implementation. It allows the solution to build (empty stubs compile) and CI to catch any project reference or solution registration errors before implementation begins. It also gives the implementer a predictable folder contract to work within.

#### Acceptance criteria

- [ ] `SharedKernel.Security.Abstractions.csproj` exists, builds, targets `net10.0`, references only `SharedKernel.Primitives`
- [ ] Zero NuGet dependencies beyond transitive from `SharedKernel.Primitives`
- [ ] Four stub files exist in correct folders: `IUserContext.cs`, `ITenantProvider.cs`, `SecurityClaimTypes.cs`, `AnonymousUserContext.cs`
- [ ] Test sub-project exists and is registered in solution
- [ ] `dotnet build` passes for the entire solution with the new projects present
- [ ] Both projects appear in `Platform.SharedKernel.slnx` under `12.Security`

---

### P-087 — Security Abstractions: Core — Implement All Public Contracts

**Status:** `●` Complete
**Work Order:** WO-015
**Domain:** 12.Security
**Depends on:** P-086

#### What is needed

Full implementation of all four public types in `SharedKernel.Security.Abstractions`, strictly following the contract finalized in P-085.

**`IUserContext` (interface):** Properties: `UserId` (Guid), `Email` (string?), `Username` (string?), `Roles` (IReadOnlyCollection of string), `Claims` (IReadOnlyDictionary of string to string), `IsAuthenticated` (bool). Method: `bool HasRole(string role)` using case-insensitive comparison against `Roles`. Full XML doc on all members including the `IsAuthenticated == true` implies `UserId != Guid.Empty` invariant and the scope note (request-scoped, never singleton).

**`ITenantProvider` (interface):** Property: `TenantId` (Guid) — non-nullable; `Guid.Empty` means no active tenant context. Full XML doc including the `Guid.Empty` sentinel semantics, prohibition on domain-layer injection, and the note that `06.Persistence.TenantedDbContext` is the only infrastructure component allowed to inject this interface.

**`AnonymousUserContext` (sealed class, implements `IUserContext`):**

- `UserId` = `Guid.Empty`
- `Email` = null
- `Username` = null
- `Roles` = empty read-only collection (e.g., `Array.Empty` or `ImmutableArray.Empty`)
- `Claims` = empty read-only dictionary (e.g., `ImmutableDictionary.Empty`)
- `IsAuthenticated` = false
- `HasRole(string role)` always returns false
- Sealed — no subclassing allowed
- All state is static/constant — no mutable instance state

**`SecurityClaimTypes` (static class):**

- `const string UserId = "sub"` — maps to the OIDC subject claim
- `const string TenantId = "tenant_id"` — custom claim agreed upon for multi-tenant services
- `const string Email` — mirrors the BCL `ClaimTypes.Email` value as a string constant (not a property reference, to avoid a BCL import in a const context)
- `const string Role` — mirrors the BCL `ClaimTypes.Role` value
- Full XML doc on the class and each constant explaining the canonical claim name and its source

All types must be in the `SharedKernel.Security.Abstractions` namespace (or a logical sub-namespace matching the folder, e.g., `SharedKernel.Security.Abstractions.Claims` for `SecurityClaimTypes`).

#### Why this is needed

This is the foundational package that all downstream capability domains will reference. The correctness of the interface shapes — especially the `Guid`-typed `UserId`, non-nullable `TenantId`, and the `AnonymousUserContext` sentinel — directly determines whether the `06.Persistence` migration (P-078), `05.Application` pipeline behaviors, and `13.ServiceDefaults` tenant middleware can be implemented without local workarounds. Getting this right in one pass eliminates divergence across domains.

#### Acceptance criteria

- [ ] `IUserContext` is implemented with all six members; `HasRole` uses case-insensitive string comparison
- [ ] `ITenantProvider` is implemented with `TenantId` as non-nullable `Guid`
- [ ] `AnonymousUserContext` is a sealed class; all sentinel values are correct; `HasRole` always returns false; no mutable state
- [ ] `SecurityClaimTypes` is a static class with four `const string` fields; values match the documented canonical claim names
- [ ] All types are in the correct namespace matching their folder
- [ ] All public members have XML doc comments
- [ ] `dotnet build` succeeds with zero warnings on the package

---

### P-088 — Security Abstractions: Tests — Unit Test Suite

**Status:** `●` Complete
**Work Order:** WO-015
**Domain:** 12.Security
**Depends on:** P-087

#### What is needed

Full unit test coverage for all four public types in `SharedKernel.Security.Abstractions`. Tests live in `12.Security/SharedKernel.Security.Abstractions/SharedKernel.Security.Abstractions.Tests/`.

**`AnonymousUserContext` tests:**

- `UserId` returns `Guid.Empty`
- `IsAuthenticated` returns false
- `Email` returns null
- `Username` returns null
- `Roles` returns an empty collection (not null)
- `Claims` returns an empty dictionary (not null)
- `HasRole("any-role")` returns false — with exact case, uppercase, lowercase, and mixed casing
- Instance is immutable — no property setter

**`SecurityClaimTypes` tests:**

- `UserId` constant value is `"sub"`
- `TenantId` constant value is `"tenant_id"`
- `Email` constant value matches the expected BCL claim type string value for email
- `Role` constant value matches the expected BCL claim type string value for role

**Contract shape tests (reflection-based, ensuring the interface is stable):**

- `IUserContext` has exactly the expected members: `UserId` (Guid), `Email` (string?), `Username` (string?), `Roles`, `Claims`, `IsAuthenticated` (bool), `HasRole` method
- `ITenantProvider` has exactly `TenantId` (Guid)
- `AnonymousUserContext` implements `IUserContext`
- `AnonymousUserContext` is sealed
- `SecurityClaimTypes` is a static class

**`IUserContext` + `ITenantProvider` consumer pattern tests:**

- A custom `IUserContext` test double with `IsAuthenticated = true` and a non-empty Guid satisfies the `IsAuthenticated == true implies UserId != Guid.Empty` invariant — verified by test assertions
- A custom `ITenantProvider` test double with `TenantId = Guid.Empty` is a valid state — test asserts it does not throw

#### Why this is needed

A pure interface package without a test suite leaves contract drift undetected. These tests serve as a living specification: they will catch any future refactor that accidentally changes a property type, renames a member, or breaks the `AnonymousUserContext` sentinel behavior. The contract shape tests are especially valuable because all downstream domains (Persistence, Application, Communication) depend on this surface being stable.

#### Acceptance criteria

- [ ] All `AnonymousUserContext` property and method tests pass
- [ ] All `SecurityClaimTypes` constant value tests pass
- [ ] Contract shape tests confirm correct member types and sealed/static modifiers
- [ ] Consumer pattern tests for `IUserContext` and `ITenantProvider` pass
- [ ] Test project builds and all tests pass with `dotnet test`
- [ ] No test references any concrete Oidc or infrastructure type

---

### P-089 — Security Abstractions: Docs — XML Documentation and Package README

**Status:** `●` Complete
**Work Order:** WO-015
**Domain:** 12.Security
**Depends on:** P-088

#### What is needed

Ensure every public API in `SharedKernel.Security.Abstractions` is fully documented with XML doc comments, and the package ships a README that consumer teams can use to onboard quickly.

**XML doc requirements for `IUserContext`:**

- Type-level: purpose, scope (request-scoped, never singleton), DI registration note (always resolvable — falls back to `AnonymousUserContext`), usage guidance (inject in application layer only, never in domain or repository code)
- `UserId`: `Guid` type rationale, the `IsAuthenticated == true implies not Guid.Empty` invariant
- `Email`, `Username`: nullable, may be absent depending on identity provider configuration
- `Roles`: collection, case as provided by the identity provider — use `HasRole` for safe comparison
- `Claims`: keyed by claim type, first value wins for multi-value claims; roles must always be accessed via `Roles`, never this dictionary
- `IsAuthenticated`: semantics, false means unauthenticated or no HTTP context
- `HasRole`: case-insensitive, returns false for unknown roles and when not authenticated

**XML doc requirements for `ITenantProvider`:** Type-level doc covering purpose, scope (request-scoped), `Guid.Empty` sentinel meaning, prohibition on domain injection, and the note that `06.Persistence.TenantedDbContext` is the only allowed infrastructure consumer.

**XML doc requirements for `AnonymousUserContext`:** Type-level doc covering what it is (sentinel, fallback implementation), why it exists (DI always-resolvable pattern), when to expect it (unauthenticated requests, background jobs without HTTP context), and the `sealed` rationale.

**XML doc requirements for `SecurityClaimTypes`:** Type-level doc covering what these constants are, why to use them (avoid raw string literals), and a note that consumers may define additional local constants for domain-specific claims. Each constant documents the actual claim string value, its source (OIDC spec / custom), and which identity providers use it.

**Package README (`README.md` at the project root):**

- What the package is and what it is not (no DI, no JWT parsing — those are in `SharedKernel.Security.Oidc`)
- The three patterns: inject `IUserContext` in application handlers; inject `ITenantProvider` in the application layer to resolve `TenantId` before passing to domain; use `SecurityClaimTypes` constants instead of raw strings
- `AnonymousUserContext` — when to expect it and how to handle it
- One-paragraph note on the `IsAuthenticated` guard pattern

#### Why this is needed

`SharedKernel.Security.Abstractions` is consumed by potentially every service across the platform. Without clear documentation, teams will guess at the `AnonymousUserContext` fallback behavior, incorrectly inject `ITenantProvider` into domain code, or bypass `HasRole` in favour of raw `Claims` dictionary access — all of which the design explicitly prohibits. Documentation is the enforcement mechanism for design intent in a shared library.

#### Acceptance criteria

- [ ] All public members of `IUserContext` have non-trivial XML doc comments (not just property name restatements)
- [ ] All public members of `ITenantProvider` have XML doc
- [ ] `AnonymousUserContext` type-level and member docs explain the sentinel pattern
- [ ] `SecurityClaimTypes` class and all four constants have XML doc with the actual claim string values
- [ ] `README.md` exists at `12.Security/SharedKernel.Security.Abstractions/README.md`
- [ ] README covers the three consumer patterns and the `AnonymousUserContext` handling guidance
- [ ] `dotnet build` emits zero XML doc warnings (no missing `<param>`, `<returns>`, or `<summary>` warnings)

---

### P-090 — Security Abstractions: Published — NuGet Pack and Consumer Verification

**Status:** `●` Complete
**Work Order:** WO-015
**Domain:** 12.Security
**Depends on:** P-089

#### What is needed

Pack `SharedKernel.Security.Abstractions` as a NuGet package and verify it is consumable by a downstream service referencing it as a NuGet dependency (not a project reference).

**Pack metadata (in .csproj):**

- `PackageId`: `SharedKernel.Security.Abstractions`
- `Version`: `1.0.0`
- `Authors`: platform team identity
- `Description`: "Identity and tenancy abstraction interfaces for Platform.SharedKernel. Zero NuGet dependencies. Reference this package in application and infrastructure layers; never inject IHttpContextAccessor or ClaimsPrincipal directly."
- `PackageTags`: security, identity, tenancy, abstractions, sharedkernel
- `GenerateDocumentationFile`: true (so XML doc ships with the package)
- `IncludeReadme`: README.md
- No `PrivateAssets` — this is a public abstraction package

**Pack and output:** `dotnet pack` targeting Release configuration outputs `.nupkg` to the repo's `nupkgs/` directory. Package content verified: contains the four source types, XML doc file, README.

**Consumer verification** — a minimal consumer verify project (console or classlib) that:

1. References `SharedKernel.Security.Abstractions` as a NuGet package from the `nupkgs/` local feed
2. Resolves `IUserContext` from a test DI container with `AnonymousUserContext` registered as the implementation
3. Asserts `IUserContext.IsAuthenticated == false` and `IUserContext.UserId == Guid.Empty`
4. Resolves `SecurityClaimTypes.UserId` constant and asserts value is `"sub"`
5. Confirms the package does not pull in any unexpected transitive NuGet dependencies

#### Why this is needed

Packing and doing a consumer verify is the only way to confirm that the package as shipped (not as a project reference) exposes the correct public API, ships XML docs, and carries no unintended transitive dependencies. Projects throughout the platform will take a NuGet dependency on this package — consumer verification catches mismatched namespaces, missing doc files, or accidental private-asset leaks before they reach downstream teams.

#### Acceptance criteria

- [ ] `dotnet pack` succeeds in Release configuration; `.nupkg` is in `nupkgs/`
- [ ] Package contains: four type files, XML doc file, README.md
- [ ] No unexpected NuGet dependencies beyond `SharedKernel.Primitives` in the package manifest
- [ ] Consumer verify: `IUserContext` resolves to `AnonymousUserContext` from local NuGet feed reference
- [ ] Consumer verify: `IsAuthenticated == false`, `UserId == Guid.Empty`, `HasRole("anything") == false` via `AnonymousUserContext`
- [ ] Consumer verify: `SecurityClaimTypes.UserId == "sub"`
- [ ] `12.Security/state-map.md` Package Board row for `SharedKernel.Security.Abstractions` updated to `Published`

---

---
### P-091 — Persistence EfCore: Audit Interceptor String Adapter for Guid-typed IUserContext.UserId

**Status:** `●` Complete
**Work Order:** WO-016
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

`AuditInterceptor` and `SoftDeleteInterceptor` in `SharedKernel.Persistence.EfCore` currently call `_userContext.UserId` expecting a `string`, which is the type on the local `IUserContext` declared in `EfCore/Interceptors/`. When P-078 migrates to `SharedKernel.Security.Abstractions.IUserContext`, the `UserId` property becomes `Guid` — a breaking type change. The audit columns `CreatedBy`, `ModifiedBy`, and `DeletedBy` are mapped as `string` columns in the database (configured in `EntityTypeConfigurationBase` as `HasMaxLength(256)`). These columns must continue to receive a `string` value.

The resolution requires an explicit adapter decision. The correct approach is:

**Use `IUserContext.UserId.ToString()` as the string representation for audit fields.** The `Guid` UUID value (`"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"`) is a stable, unique, human-readable identifier that fits within the `HasMaxLength(256)` constraint. `"system"` remains the fallback when `IsAuthenticated == false` or `UserId == Guid.Empty`.

Update both interceptors to produce the audit string as follows:
- When `userContext.IsAuthenticated == true` and `userContext.UserId != Guid.Empty`: use `userContext.UserId.ToString()` (lowercase hyphenated GUID string, format "D")
- When `userContext.IsAuthenticated == false` or `UserId == Guid.Empty`: use the fallback value `"system"`

The `NoOpUserContext` that ships as the DI fallback in `EfCorePersistenceBuilder.Build()` must return `UserId = Guid.Empty` and `IsAuthenticated = false` — consistent with `AnonymousUserContext` from Security.Abstractions.

Update `06.Persistence/CLAUDE.md` to document the audit string format: "Audit fields `CreatedBy`, `ModifiedBy`, and `DeletedBy` store the `UserId` as a lowercase hyphenated GUID string. When no user context is available (background jobs, migrations), the literal `\"system\"` is stored."

#### Why this is needed

When P-078 removes the local `IUserContext` (which had `string UserId`) and replaces it with `SharedKernel.Security.Abstractions.IUserContext` (which has `Guid UserId`), the interceptors will fail to compile unless the `string`→`Guid` adapter is in place. More importantly, this decision must be made **before** P-078 is dispatched — it is a behavioral contract that affects every audit record written by every downstream service. Getting this right on first implementation avoids a database migration in hundreds of services.

#### Acceptance criteria

- [ ] `AuditInterceptor` produces audit string as `userId.ToString()` (format `"D"`) when `IsAuthenticated == true && UserId != Guid.Empty`; falls back to `"system"` otherwise
- [ ] `SoftDeleteInterceptor` applies the same adapter logic for `DeletedBy`
- [ ] `NoOpUserContext` (the DI fallback) returns `UserId = Guid.Empty` and `IsAuthenticated = false` — triggers the `"system"` fallback path
- [ ] Audit string format is `"system"` or a lowercase hyphenated GUID — no other format (no uppercase, no braces, no `"N"` format)
- [ ] `EntityTypeConfigurationBase` `HasMaxLength(256)` constraint accommodates GUID string length (36 chars) — no change needed, but verified
- [ ] All existing `AuditInterceptorTests` and `SoftDeleteInterceptorTests` are updated to match the new adapter behavior
- [ ] New test: when `IsAuthenticated == false`, `CreatedBy` receives `"system"` — not an empty GUID string, not null
- [ ] New test: when `IsAuthenticated == true` with a valid `UserId`, `CreatedBy` receives the hyphenated lowercase GUID string
- [ ] `06.Persistence/CLAUDE.md` documents the audit string format and the `"system"` fallback rule
- [ ] No behavioral regression for services that supply a real `IUserContext` from `SharedKernel.Security.Oidc`

---

---
### P-092 — Persistence EfCore: ICurrentTenantService → ITenantProvider Nullability Resolution

**Status:** `●` Complete
**Work Order:** WO-016
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

`ICurrentTenantService.TenantId` in `06.Persistence.EfCore` is declared as `Guid?` (nullable). `ITenantProvider.TenantId` in `SharedKernel.Security.Abstractions` is declared as `Guid` (non-nullable; returns `Guid.Empty` as the no-tenant sentinel). When P-078 migrates `TenantedDbContext` from `ICurrentTenantService` to `ITenantProvider`, the global query filter changes from:

```
e.TenantId == CurrentTenantService.TenantId   // Guid? comparison — nullable SQL semantics
```

to:

```
e.TenantId == TenantProvider.TenantId         // Guid comparison — non-nullable SQL semantics
```

This is a behavioral difference. The nullable `Guid?` comparison generates SQL `IS NULL` or `= NULL` logic at the EF Core translation layer, which can produce unexpected filter results (entities with `TenantId = null` in the DB would be excluded regardless). The non-nullable `Guid` comparison always generates `= '00000000-0000-0000-0000-000000000000'` when no tenant is present, which would match only rows where `TenantId` is the zero GUID — a well-defined, safe sentinel.

The resolution: `TenantedDbContext` after migration must document and test the new filter behavior explicitly. The `NoOpTenantProvider` (no-op `ITenantProvider` placeholder registered by `EfCorePersistenceBuilder.WithMultiTenancy()`) must return `Guid.Empty`. The global query filter becomes `e.TenantId == Guid.Empty` when no tenant is set — which correctly returns zero rows (no entity should have `TenantId == Guid.Empty` in production). This is a safer sentinel than `null` because it avoids `IS NULL` SQL complexity.

Specifically:
1. Document in `06.Persistence/CLAUDE.md` that `TenantId == Guid.Empty` is the no-tenant filter state; no rows match; this is intentional and safe.
2. The `NoOpTenantProvider` (`NoOpCurrentTenantService` successor) must return `Guid.Empty`.
3. A new integration test must verify: when no real `ITenantProvider` is registered (only the no-op placeholder), `TenantedDbContext` queries return zero rows for `IHasTenant` entities regardless of their `TenantId` value.
4. Update `EntityTypeConfigurationBase` to add a check: if `TenantId` is stored as a required non-nullable column (which it should be per `ConfigureTenantColumn`), no database row should ever have `TenantId == Guid.Empty`. Add an assertion comment in the configuration.

This phase is a prerequisite clarification and documentation phase for P-078 — it does not change production behavior but makes the behavioral contract explicit before P-078 executes.

#### Why this is needed

`Guid?` vs `Guid` in the tenant filter is a correctness boundary. If the migration from P-078 produces a filter `e.TenantId == Guid.Empty` without this being understood and tested, the no-op placeholder silently shows zero rows to all queries — which looks like a data loss bug, not a configuration issue. Teams that forget to register a real `ITenantProvider` will see an empty database and spend hours debugging. This phase ensures the behavior is understood, tested, and documented as a design decision rather than a surprise.

#### Acceptance criteria

- [ ] `06.Persistence/CLAUDE.md` documents the `Guid.Empty` no-tenant sentinel behavior: "When no real `ITenantProvider` is registered, the no-op placeholder returns `Guid.Empty`, producing the filter `TenantId == Guid.Empty`. No production row should have `TenantId == Guid.Empty`. This is the intended safe default: unconfigured multi-tenancy returns zero rows rather than all rows."
- [ ] `NoOpTenantProvider` (successor to `NoOpCurrentTenantService`) returns `Guid.Empty` explicitly, not `default(Guid)`  — same value but explicit intent
- [ ] Integration test (SQLite): a `TenantedDbContext` with no real `ITenantProvider` (only no-op) returns zero rows for entities with any non-empty `TenantId`
- [ ] Integration test (SQLite): a `TenantedDbContext` with a real `ITenantProvider` returning `tenantId = X` returns only rows where `TenantId == X`
- [ ] `EntityTypeConfigurationBase.ConfigureTenantColumn` includes an XML doc comment stating: "`TenantId` must never be `Guid.Empty` in production rows — `Guid.Empty` is the no-tenant sentinel used by the `NoOpTenantProvider`"
- [ ] No production code behavior changes — this phase is documentation + test only (the no-op returning `Guid.Empty` is already the correct behavior)

---

---
### P-093 — Persistence Abstractions: Add ExistsAsync and GetByIdsAsync to Repository Contracts

**Status:** `●` Complete
**Work Order:** WO-016
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

Extend `IRepository<TAggregate, TId>` in `SharedKernel.Persistence.Abstractions` with two new methods, and extend `IReadRepository<TAggregate, TId>` with one new method.

**`IRepository<TAggregate, TId>` additions:**

- `ExistsAsync(TId id, CancellationToken ct)` → `Task<bool>` — returns `true` if an aggregate with the given identity exists, `false` otherwise. This is semantically distinct from `GetByIdAsync` (which fetches the full aggregate) — `ExistsAsync` issues a cheap count or existence check (`ANY` / `EXISTS` in SQL) and never materializes an entity. This is a write-side check used before commands (e.g., "does this order exist before I try to cancel it?").

**`IReadRepository<TAggregate, TId>` additions:**

- `GetByIdsAsync(IEnumerable<TId> ids, CancellationToken ct)` → `Task<IReadOnlyList<TAggregate>>` — returns all aggregates whose identity appears in `ids`. The order of results is not guaranteed to match the order of input IDs. Missing IDs produce no entry in the result (the result list may be shorter than the input). This is distinct from `ListAsync(spec)` — it is a primary-key batch lookup that can use EF Core's `Contains` / `IN (...)` SQL pattern.

**`EfRepository<TAggregate, TId>` additions:**

- Implement `ExistsAsync` using `DbContext.Set<TAggregate>().AnyAsync(e => e.Id.Equals(id), ct)` — or use EF Core's compiled query pattern for this hot-path operation if the evaluator supports it.

**`EfReadRepository<TAggregate, TId>` additions:**

- Implement `GetByIdsAsync` using `DbContext.Set<TAggregate>().Where(e => ids.Contains(e.Id)).ToListAsync(ct)`. Note: this generates an `IN (...)` clause at the SQL level. The implementation must constrain input to a reasonable size (document a recommended max of 1000 IDs); the XML doc must warn that very large `ids` collections degrade to table scans on non-indexed types.

Both methods must carry full XML doc comments explaining the semantics, the SQL translation, and performance considerations.

#### Why this is needed

`ExistsAsync` and `GetByIdsAsync` are among the top two operations that cause teams to bypass the repository pattern entirely. Without `ExistsAsync`, application-layer handlers call `GetByIdAsync` just to check if an aggregate exists, materializing the full entity unnecessarily. Without `GetByIdsAsync`, handlers either call `GetByIdAsync` in a loop (N+1 queries) or drop into `DbContext` directly to write a `Where(e => ids.Contains(e.Id))` query. Both bypasses erode the repository abstraction and create inconsistent patterns across hundreds of services. Adding these methods to the abstract contracts ensures every EF Core repository gets the implementations for free.

#### Acceptance criteria

- [ ] `IRepository<TAggregate, TId>` declares `ExistsAsync(TId id, CancellationToken ct)` returning `Task<bool>`
- [ ] `IReadRepository<TAggregate, TId>` declares `GetByIdsAsync(IEnumerable<TId> ids, CancellationToken ct)` returning `Task<IReadOnlyList<TAggregate>>`
- [ ] `EfRepository<TAggregate, TId>` implements `ExistsAsync` using `AnyAsync` — does not materialize the entity
- [ ] `EfReadRepository<TAggregate, TId>` implements `GetByIdsAsync` using `Where(e => ids.Contains(e.Id)).ToListAsync`
- [ ] XML doc on `ExistsAsync` states it issues an `EXISTS` / `ANY` check — never materializes the aggregate
- [ ] XML doc on `GetByIdsAsync` states result order is not guaranteed; missing IDs produce no entry; warns on large collections (>1000 IDs)
- [ ] All existing `EfRepositoryTests` and `EfReadRepositoryTests` continue to pass
- [ ] New unit tests (SQLite): `ExistsAsync` returns `true` for existing ID, `false` for missing ID; `GetByIdsAsync` returns only matching entities; partial match (some IDs missing) returns only found entities; empty input returns empty list
- [ ] `ContractShapeTests` in `SharedKernel.Persistence.Abstractions.Tests` verify both new interface members
- [ ] `06.Persistence/CLAUDE.md` updated: both methods added to the repository interface contracts table

---

---
### P-094 — Persistence EfCore: EfRepository.UpdateAsync Tracking Optimization

**Status:** `●` Complete
**Work Order:** WO-016
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

`EfRepository<TAggregate, TId>.UpdateAsync` currently calls `DbContext.Set<TAggregate>().Update(aggregate)` unconditionally. This is the correct behavior for **disconnected** entities (entities that were not fetched through the same `DbContext` scope — for example, entities reconstructed from a cache or a DTO). However, for **connected** entities (entities that were fetched via `GetByIdAsync` in the same scope), calling `.Update()` marks every property as `Modified`, generating a full-table-column `UPDATE` statement regardless of which properties actually changed. This causes:

1. Unnecessary write load on the database — UPDATE statements touch all columns even when only one changed.
2. Concurrency token bypass risk — some providers treat a full `.Update()` call differently from a change-tracked modification.
3. Confusion in audit interceptors — `ModifiedBy`/`ModifiedOn` set on every save even when nothing materially changed.

The fix: update `EfRepository.UpdateAsync` to check EF Core's `ChangeTracker` state before calling `.Update()`:

- If the entity is already tracked by the `DbContext` (i.e., `DbContext.Entry(aggregate).State != EntityState.Detached`), do NOT call `.Update()`. The change tracker already knows about it. Simply ensure the entity state is `Modified` if it isn't already — or leave it as-is and let EF Core detect changes via snapshot comparison.
- If the entity is **detached** (not tracked in the current scope), call `.Update(aggregate)` as today — this attaches it and marks all properties as modified (correct for disconnected scenarios).

Add an XML doc comment on `UpdateAsync` explaining both paths: "If the entity is already tracked by the current `DbContext` scope (fetched via `GetByIdAsync` in the same unit of work), change detection is automatic — calling `UpdateAsync` is a no-op in this case but is idempotent. If the entity is detached (constructed externally or loaded in a different scope), `UpdateAsync` attaches it and marks all properties as modified."

Add a protected virtual `void MarkAsModifiedIfDetached(TAggregate aggregate)` helper on `EfRepository` so concrete subclasses can call it without duplicating the detachment check.

#### Why this is needed

The unconditional `.Update()` call is a classic EF Core beginner mistake that ships in many base repository implementations. In a SharedKernel used by hundreds of services, this mistake multiplies into hundreds of full-column UPDATE statements on every aggregate mutation. At scale, this is measurable database overhead. More critically, it masks EF Core's change detection capability — a primary performance feature of EF Core that is completely wasted when `.Update()` is called unconditionally. Fixing this in the base class benefits every downstream service without requiring any changes in consuming repositories.

#### Acceptance criteria

- [ ] `EfRepository<TAggregate, TId>.UpdateAsync` checks entity tracking state before calling `.Update(aggregate)`
- [ ] If the entity is already tracked (state is not `Detached`), `.Update()` is NOT called; the method is a logical no-op (change tracking handles the rest)
- [ ] If the entity is detached, `.Update(aggregate)` is called — unchanged behavior from today
- [ ] A protected virtual `MarkAsModifiedIfDetached(TAggregate aggregate)` helper method is provided on `EfRepository`
- [ ] XML doc on `UpdateAsync` explains both the tracked and detached code paths
- [ ] New unit tests (SQLite): fetch entity via `GetByIdAsync`, mutate a property, call `UpdateAsync` — verify only the changed property generates a `Modified` column marker; verify only a targeted UPDATE is issued (not full-column); fetch entity, call `UpdateAsync` without any mutation — verify no UPDATE is issued
- [ ] Existing tests continue to pass — detached entity path is unchanged
- [ ] No changes to `IRepository<TAggregate, TId>` interface — implementation-only fix

---

---
### P-095 — Domain: Add IncludeDeleted Flag to ISpecification

**Status:** `●` Complete
**Work Order:** WO-016
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

Add a `bool IncludeDeleted { get; }` property to `ISpecification<T>` in `SharedKernel.Domain` and implement it on `Specification<T>` with a `protected void IncludeSoftDeleted()` builder method and a default of `false`.

This is a prerequisite for P-080 Capability 5, which requires `SpecificationEvaluator` to call `IgnoreQueryFilters()` on the `IQueryable` when `spec.IncludeDeleted == true` — rather than the current `QueryableExtensions.IgnoreSoftDeleteFilter` extension which leaks `IQueryable` to the caller.

The semantics:
- `IncludeDeleted = false` (default) — the global soft-delete query filter (`e => !e.IsDeleted`) applies normally. Soft-deleted records are hidden.
- `IncludeDeleted = true` — the global soft-delete filter is bypassed. Soft-deleted records are included in results.

The `Specification<T>` abstract base must default `IncludeDeleted` to `false`. A `protected void IncludeSoftDeleted()` builder method sets it to `true`. Concrete specification subclasses call `IncludeSoftDeleted()` in their constructors when they are admin/audit specifications that intentionally need to see deleted records.

XML documentation on `ISpecification<T>.IncludeDeleted` must state: "When `true`, the consuming repository must bypass the global soft-delete query filter so that soft-deleted records are included in results. Intended for admin panels, audit trails, data export, and recovery operations only. Default is `false` — soft-deleted records are hidden."

Important: `IncludeDeleted = true` bypasses ALL global query filters on the entity type (including tenant isolation filters if a `TenantedDbContext` is used), because EF Core's `IgnoreQueryFilters()` cannot selectively bypass a single filter. The XML doc must warn of this: "Setting `IncludeDeleted = true` calls `IgnoreQueryFilters()` internally, which also bypasses any tenant isolation filter. For tenant-scoped soft-delete queries, re-apply the tenant criterion manually via `AddCriteria(e => e.TenantId == tenantId)`."

Composed specifications (`AndSpecification<T>`, `OrSpecification<T>`, `NotSpecification<T>`) must propagate `IncludeDeleted`: set to `true` if either operand has `IncludeDeleted = true` (the more permissive wins, since composed specs are query-oriented).

Tests must cover: default value is `false`; `IncludeSoftDeleted()` sets to `true`; composed specs propagate `IncludeDeleted = true` when either operand is `true`; composed specs remain `false` when both operands are `false`.

Update `03.Domain/CLAUDE.md` specification system section to document `IncludeDeleted` and the filter-bypass warning.

#### Why this is needed

P-080 (Capability 5) removes `QueryableExtensions.IgnoreSoftDeleteFilter` — an extension method that directly returns `IQueryable<T>` to callers, bypassing the repository's `IQueryable` encapsulation contract. The correct replacement is the `IncludeDeleted` flag on the specification, which keeps the `IQueryable` inside the repository boundary. Without this `03.Domain` phase, P-080 cannot implement Capability 5 — it depends on the specification contract change to replace the `IQueryable` leak. This phase must therefore complete before P-080 is dispatched.

#### Acceptance criteria

- [ ] `bool IncludeDeleted { get; }` exists on `ISpecification<T>`
- [ ] `Specification<T>` defaults `IncludeDeleted` to `false`
- [ ] `protected void IncludeSoftDeleted()` builder method sets `IncludeDeleted` to `true`
- [ ] `AndSpecification<T>`, `OrSpecification<T>`, `NotSpecification<T>` propagate `IncludeDeleted = true` when either operand has it set
- [ ] XML doc warns that `IgnoreQueryFilters()` bypasses ALL query filters including tenant isolation; provides the manual workaround
- [ ] `03.Domain/CLAUDE.md` specification system section updated to document `IncludeDeleted`, `IncludeSoftDeleted()`, the bypass warning, and the multi-filter caveat
- [ ] New tests: default `false`; `IncludeSoftDeleted()` sets `true`; composed spec propagation (both cases)
- [ ] All existing `Specification<T>` tests continue to pass — additive change only
- [ ] No new NuGet dependencies — BCL only
- [ ] Package remains AOT-safe

---

---
### P-096 — Governance: Architecture Rule — IUserContext Audit String Adapter and Repository Contract Completeness

**Status:** `●` Complete
**Work Order:** WO-016
**Domain:** 00.Governance
**Depends on:** P-091, P-093

#### What is needed

Two new architecture enforcement rules in `SharedKernel.ArchitectureTests`:

**Rule 1 — Audit interceptors must not use `Guid.ToString()` raw — must go through the adapter helper.**
After P-091, the audit interceptors use a defined pattern for converting `IUserContext.UserId` (Guid) to a string. To prevent future drift (e.g., a contributor using `userId.ToString("N")` which produces no hyphens, causing inconsistent audit trail format), add an architecture test that scans `AuditInterceptor` and `SoftDeleteInterceptor` source and asserts that no `Guid.ToString("N")`, `Guid.ToString("B")`, `Guid.ToString("P")`, or `Guid.ToString("X")` format codes are used — only `ToString()` or `ToString("D")` (the default lowercase hyphenated format). This can be implemented as a naming/format convention check via Roslyn analyzer or a targeted IL scan.

**Rule 2 — All `IRepository<TAggregate, TId>` implementors must declare `ExistsAsync`.**
After P-093, `ExistsAsync` is on the `IRepository` interface. Any class that implements `IRepository<TAggregate, TId>` but does not implement `ExistsAsync` is missing a required method — the compiler catches this, but the architecture test provides a readable failure message. Use NetArchTest to assert that all types implementing `IRepository<,>` in `06.Persistence` assemblies have a method named `ExistsAsync`.

**Rule 3 — `IReadRepository` implementors must declare `GetByIdsAsync`.**
Same pattern as Rule 2 for `GetByIdsAsync` on `IReadRepository<TAggregate, TId>`.

All three rules must be documented in `00.Governance/CLAUDE.md` under the Persistence Enforcement section with rationale and examples.

#### Why this is needed

Rule 1 prevents audit trail inconsistency caused by GUID format drift — a silent bug that produces inconsistent `CreatedBy` column values (`"d3e4f5a6-..."` vs `"d3e4f5a6..."`) across services that implement custom repositories. Rules 2 and 3 provide readable build-time messages when a service's custom repository fails to implement the new batch methods — surfacing the gap earlier than a runtime `NotImplementedException`.

#### Acceptance criteria

- [ ] Rule 1 exists: architecture test asserts `AuditInterceptor` and `SoftDeleteInterceptor` do not use non-`"D"` GUID format codes; test passes on the P-091-updated interceptors; documented in `00.Governance/CLAUDE.md`
- [ ] Rule 2 exists: NetArchTest rule asserts all `IRepository<,>` implementors in `06.Persistence` have `ExistsAsync`; documented in `00.Governance/CLAUDE.md`
- [ ] Rule 3 exists: NetArchTest rule asserts all `IReadRepository<,>` implementors in `06.Persistence` have `GetByIdsAsync`; documented in `00.Governance/CLAUDE.md`
- [ ] All three rules run as `[Fact]` tests in the governance test suite; each has a descriptive failure message
- [ ] Governance test suite passes with all new rules included

---

---
### P-097 — Persistence Abstractions: Promote GetProjectedQuery to ISpecificationEvaluator Contract

**Status:** `●` Complete
**Work Order:** WO-017
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

`EfReadRepository<TAggregate, TId>` currently downcasts its injected `ISpecificationEvaluator<TAggregate>` to the concrete `SpecificationEvaluator<TAggregate>` in order to call `GetProjectedQuery`. This is a layering violation: `EfReadRepository` is supposed to depend on the `ISpecificationEvaluator<T>` abstraction from `SharedKernel.Persistence.Abstractions`, but the downcast breaks the contract — any alternative evaluator implementation (Cosmos, in-memory, Marten) will throw `InvalidCastException` at runtime.

The fix has two parts:

**Part 1 — Extend `ISpecificationEvaluator<T>` in Abstractions:**
Add a generic method `GetProjectedQuery<TResult>(IQueryable<T> inputQuery, IProjectionSpecification<T, TResult> spec) → IQueryable<TResult>` to the `ISpecificationEvaluator<T>` interface. This method already exists on the concrete `SpecificationEvaluator<T>` — it must be promoted to the interface so the abstraction is complete. The method signature is AOT-safe: it uses expression trees through `IProjectionSpecification<T, TResult>.Selector`, which is already in Abstractions.

**Part 2 — Remove the downcast in `EfReadRepository`:**
Replace the stored `SpecificationEvaluator<TAggregate> _evaluator` field with `ISpecificationEvaluator<TAggregate> _evaluator`. The projection methods (`ListProjectedAsync`, `GetBySpecProjectedAsync`) call `_evaluator.GetProjectedQuery(...)` directly on the interface — no cast needed. The constructor parameter type changes accordingly.

**Abstraction contract addition:**
The `ISpecificationEvaluator<T>` interface in Abstractions gains one method. Zero ORM dependencies are introduced — `IProjectionSpecification<T, TResult>` is already in Abstractions and uses only BCL expression tree types.

#### Why this is needed

The concrete downcast is a silent runtime bomb: any team that implements a custom `ISpecificationEvaluator<T>` for testing or alternative persistence providers will get an `InvalidCastException` the first time `ListProjectedAsync` or `GetBySpecProjectedAsync` is called. At scale across hundreds of services, this is a guaranteed failure vector. The abstraction boundary exists precisely to enable provider swapping — the downcast negates that entirely. Promoting the method to the interface closes the gap at zero cost.

#### Acceptance criteria
- [ ] `ISpecificationEvaluator<T>` in `SharedKernel.Persistence.Abstractions` declares `GetProjectedQuery<TResult>(IQueryable<T> inputQuery, IProjectionSpecification<T, TResult> spec) → IQueryable<TResult>`
- [ ] `SpecificationEvaluator<T>` in `SharedKernel.Persistence.EfCore` implements the interface method (it already has the implementation — it just needs the `ISpecificationEvaluator<T>` declaration added)
- [ ] `EfReadRepository` field changes from `SpecificationEvaluator<TAggregate>` to `ISpecificationEvaluator<TAggregate>` — the downcast is removed
- [ ] `EfReadRepository` constructor parameter type changes from `ISpecificationEvaluator<TAggregate> evaluator` (with internal downcast) to `ISpecificationEvaluator<TAggregate> evaluator` (used directly)
- [ ] All existing `EfReadRepository` tests continue to pass — no behavioral changes
- [ ] `ContractShapeTests` in `SharedKernel.Persistence.Abstractions.Tests` verify `GetProjectedQuery` is declared on the interface
- [ ] XML doc on the new interface method matches the existing doc on `SpecificationEvaluator<T>.GetProjectedQuery`
- [ ] `06.Persistence/CLAUDE.md` updated: `ISpecificationEvaluator<T>` interface contract table adds `GetProjectedQuery`
---

---
### P-098 — Persistence EfCore: Fix EfUnitOfWork Dual-Constructor DI Ambiguity

**Status:** `●` Complete
**Work Order:** WO-017
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

`EfUnitOfWork` declares two public constructors: one accepting `(SharedKernelDbContext)` and one accepting `(SharedKernelDbContext, IDomainEventDispatcher?)`. This creates DI resolution ambiguity in .NET's default service container (`Microsoft.Extensions.DependencyInjection`): when `IDomainEventDispatcher` IS registered, the container may select the shorter constructor (single parameter) and silently skip the dispatcher. The result is domain events raised during `SaveChangesAsync` are never dispatched even though a dispatcher is registered — a silent behavioral failure.

The fix: collapse to a single constructor that accepts `IDomainEventDispatcher?` as nullable. Since the DI container resolves nullable services as `null` when not registered, this is the idiomatic .NET optional-dependency pattern. The single constructor is:

```
EfUnitOfWork(SharedKernelDbContext dbContext, IDomainEventDispatcher? dispatcher = null)
```

When `IDomainEventDispatcher` is not registered in the container, DI resolves `null` for the nullable parameter and the no-dispatch path remains active. When it IS registered, DI resolves the implementation. This eliminates the constructor ambiguity entirely.

`EfCorePersistenceBuilder.Build()` requires no changes — the DI registration of `EfUnitOfWork` as `IUnitOfWork` continues to work because the single constructor is unambiguous.

Update `06.Persistence/CLAUDE.md` to clarify that `IDomainEventDispatcher` is resolved by the DI container as a nullable optional service — no special registration pattern is needed beyond registering the implementation.

#### Why this is needed

Silently missing domain event dispatch is among the most dangerous failure modes in an event-sourced or domain-event-driven architecture. Events appear to work in test environments (where a concrete dispatcher is passed to the constructor directly) but fail in production (where DI picks the wrong constructor). This is not theoretical: .NET DI uses the constructor with the most resolvable parameters, which is non-deterministic when two constructors overlap. Collapsing to one constructor with a nullable optional parameter is the idiomatic .NET DI pattern for optional dependencies.

#### Acceptance criteria
- [ ] `EfUnitOfWork` has exactly one public constructor: `(SharedKernelDbContext dbContext, IDomainEventDispatcher? dispatcher = null)`
- [ ] The two-constructor form is removed
- [ ] Existing `DomainEventDispatchTests` continue to pass — all four test cases cover: dispatcher registered, dispatcher not registered, double-dispatch prevention, dispatch failure behavior
- [ ] New DI test: register `EfUnitOfWork` in a real `ServiceCollection`; also register a concrete `IDomainEventDispatcher`; resolve `IUnitOfWork` via DI; verify that domain events ARE dispatched after `SaveChangesAsync` — confirming DI wires the dispatcher correctly via the single constructor
- [ ] New DI test: do NOT register `IDomainEventDispatcher`; resolve `IUnitOfWork`; verify `SaveChangesAsync` completes without error (null dispatcher path works)
- [ ] `06.Persistence/CLAUDE.md` `EfUnitOfWork` section updated to state the single-constructor pattern and the optional DI injection semantics
---

---
### P-099 — Persistence EfCore: Fix ExistsAsync EF.Property Shadow Access + Add ITransactionalUnitOfWork

**Status:** `●` Complete
**Work Order:** WO-017
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

This phase addresses two related persistence concerns:

**Part 1 — Fix `ExistsAsync` predicate in `EfRepository`:**

`EfRepository.ExistsAsync` uses `AnyAsync(e => EF.Property<TId>(e, "Id")!.Equals(id))`. `EF.Property<TId>(e, "Id")` is a shadow property accessor intended for EF Core internal use or genuinely shadow-mapped properties. For `AggregateRoot<TId>` subclasses — which declare `Id` as a concrete CLR property — this is incorrect. EF Core may translate it inconsistently depending on provider and whether the property is shadow or concrete.

The fix: use the same expression-tree approach as `ByIdSpecification<TAggregate, TId>` which correctly uses `Expression.Property(param, "Id")` → `Expression.Equal` → compiled lambda. `ExistsAsync` should either:
- Delegate to `AnyAsync(e => EF.Property<object>(e, "Id")!.Equals((object)id!))` — which forces a boxed comparison and is consistently translated
- Or more correctly: build a static compiled expression `Expression<Func<TAggregate, bool>>` using `Expression.Property(param, "Id")` + `Expression.Equal(idProperty, Expression.Constant(id, typeof(TId)))` and pass it to `AnyAsync`. This is the same pattern `ByIdSpecification` uses and is confirmed AOT-safe on EF Core IQueryable.

**Part 2 — Add `ITransactionalUnitOfWork` to Abstractions:**

Microservices that need to coordinate multiple repository operations within an explicit database transaction (e.g., saga compensation steps, two-phase read-then-write patterns, batch imports) currently have no way to do this via the `IUnitOfWork` abstraction. They fall back to injecting `SharedKernelDbContext` directly — a hard rule violation.

Add `ITransactionalUnitOfWork` to `SharedKernel.Persistence.Abstractions` extending `IUnitOfWork`:

```
ITransactionalUnitOfWork : IUnitOfWork
    BeginTransactionAsync(CancellationToken ct) → Task<IDbContextTransaction>
    CommitTransactionAsync(CancellationToken ct) → Task
    RollbackTransactionAsync(CancellationToken ct) → Task
```

`IDbContextTransaction` is from `Microsoft.EntityFrameworkCore` — this introduces a limited EF Core reference in Abstractions. To keep Abstractions ORM-free, the transaction handle should be represented as `IAsyncDisposable` or a custom `IPersistenceTransaction` wrapper interface that wraps `CommitAsync()` and `RollbackAsync()` without importing EF Core into Abstractions.

The transaction interface lives in Abstractions as `IPersistenceTransaction` with `CommitAsync(CancellationToken ct)`, `RollbackAsync(CancellationToken ct)`, and `IAsyncDisposable`. The `EfTransactionalUnitOfWork` in EfCore implements `ITransactionalUnitOfWork` using `DbContext.Database.BeginTransactionAsync` internally, wrapping the `IDbContextTransaction` in a `EfPersistenceTransaction` adapter.

`EfCorePersistenceBuilder.Build()` gains an optional `.WithTransactionalUnitOfWork()` fluent method that registers `ITransactionalUnitOfWork → EfTransactionalUnitOfWork` (scoped) alongside `IUnitOfWork → EfUnitOfWork`.

#### Why this is needed

`ExistsAsync` using `EF.Property` is an incorrect use of a shadow property accessor on a concrete CLR property. While it often works, it is fragile across provider versions and AOT scenarios. Fixing it to use the same expression-tree approach as `ByIdSpecification` aligns it with the documented AOT-safe pattern.

`ITransactionalUnitOfWork` closes the last forcing function for `DbContext` injection: teams needing explicit transactions. Without this abstraction, every service with a multi-step transactional saga must inject the concrete `DbContext`, which bypasses the repository pattern and couples application logic to EF Core. This is one of the most common persistence anti-patterns across microservice ecosystems.

#### Acceptance criteria
- [ ] `EfRepository.ExistsAsync` uses expression-tree `Expression.Property` + `Expression.Equal` pattern (or consistent EF Core translation approach) — `EF.Property<TId>(e, "Id")` is removed
- [ ] New unit test: `ExistsAsync` returns `true` for an existing strongly-typed ID; `false` for missing — identical behavioral contract, different implementation
- [ ] `IPersistenceTransaction` interface exists in `SharedKernel.Persistence.Abstractions` with `CommitAsync`, `RollbackAsync`, and `IAsyncDisposable`
- [ ] `ITransactionalUnitOfWork : IUnitOfWork` interface exists in `SharedKernel.Persistence.Abstractions` with `BeginTransactionAsync(CancellationToken ct) → Task<IPersistenceTransaction>`
- [ ] `EfPersistenceTransaction` sealed class in `SharedKernel.Persistence.EfCore` wraps `IDbContextTransaction` and implements `IPersistenceTransaction`
- [ ] `EfTransactionalUnitOfWork` sealed class in `SharedKernel.Persistence.EfCore` implements `ITransactionalUnitOfWork`; dispatches domain events post-commit in the same manner as `EfUnitOfWork`
- [ ] `EfCorePersistenceBuilder` gains `.WithTransactionalUnitOfWork()` fluent method; registers `ITransactionalUnitOfWork → EfTransactionalUnitOfWork` (scoped)
- [ ] Integration test (SQLite): begin transaction → add aggregate → commit → verify persisted
- [ ] Integration test (SQLite): begin transaction → add aggregate → rollback → verify NOT persisted
- [ ] `SharedKernel.Persistence.Abstractions` does NOT reference any EF Core NuGet package — `IPersistenceTransaction` uses only BCL types
- [ ] `06.Persistence/CLAUDE.md` updated: `ITransactionalUnitOfWork` and `IPersistenceTransaction` added to interface contracts; `ExistsAsync` implementation note updated
---

---
### P-100 — Persistence EfCore: Fix TenantedRepository Soft-Delete Bypass + Rename Clarity

**Status:** `●` Complete
**Work Order:** WO-017
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

`TenantedRepository<TAggregate, TId>.GetByIdForTenantAsync` calls `IgnoreQueryFilters()` which removes ALL global query filters simultaneously — both the tenant isolation filter AND the soft-delete filter. This is unintentional for the documented purpose of the method ("admin or migration code that needs to operate on data outside the current tenant scope"). Silently returning soft-deleted records from an admin tenant-bypass lookup is a data integrity risk.

**Fix — two new methods replacing the single ambiguous one:**

Remove `GetByIdForTenantAsync(TId id, Guid tenantId, ct)` or keep it with a deprecation warning, and add:

1. `GetByIdForTenantAsync(TId id, Guid tenantId, ct)` — bypasses ONLY the tenant filter; preserves the soft-delete filter. Implementation: apply `IgnoreQueryFilters()` then re-add the soft-delete filter manually via `Where(e => !EF.Property<bool>(e, "IsDeleted"))` if the entity is `ISoftDeletable`; or use a more targeted approach.

   **Note on EF Core limitation:** EF Core's `IgnoreQueryFilters()` cannot selectively bypass a single filter — it bypasses all of them. The workaround is: after `IgnoreQueryFilters()`, add back the soft-delete criteria manually as a `Where` clause: `.Where(e => !EF.Property<bool>(e, nameof(ISoftDeletable.IsDeleted)))` when `typeof(ISoftDeletable).IsAssignableFrom(typeof(TAggregate))`. This re-creates the soft-delete filter as an explicit query predicate.

2. `GetByIdForTenantIncludingDeletedAsync(TId id, Guid tenantId, ct)` — bypasses BOTH filters (current behavior, now named clearly). Reserved for recovery and audit operations.

Both methods carry XML doc comments that clearly state their filter semantics. `GetByIdForTenantAsync` must document that soft-deleted records are excluded. `GetByIdForTenantIncludingDeletedAsync` must document that both filters are bypassed and state it is only for audit/recovery.

#### Why this is needed

Silent soft-delete bypass on what appears to be a routine "get by ID for tenant" call is a correctness trap. A developer using `GetByIdForTenantAsync` in an admin panel expects to see live data for a different tenant — they do not expect to also see soft-deleted records. The name `GetByIdForTenantAsync` gives no indication of soft-delete semantics. Providing a clean `GetByIdForTenantAsync` (soft-delete preserved) and an explicit `GetByIdForTenantIncludingDeletedAsync` (both filters bypassed) makes the semantics unambiguous at the call site.

#### Acceptance criteria
- [ ] `TenantedRepository<TAggregate, TId>` has `GetByIdForTenantAsync(TId id, Guid tenantId, ct)` that bypasses the tenant filter AND preserves the soft-delete filter for `ISoftDeletable` entities
- [ ] `TenantedRepository<TAggregate, TId>` has `GetByIdForTenantIncludingDeletedAsync(TId id, Guid tenantId, ct)` that bypasses both filters (previous behavior); XML doc explicitly states both filters are bypassed; warns this is only for audit/recovery/admin use
- [ ] For non-`ISoftDeletable` entities, both methods behave identically (no soft-delete filter exists to preserve)
- [ ] Integration test (SQLite): soft-delete an entity; call `GetByIdForTenantAsync` → returns null (soft-delete excluded); call `GetByIdForTenantIncludingDeletedAsync` → returns entity
- [ ] Integration test (SQLite): entity in tenant A; query with tenant B via `GetByIdForTenantAsync` → returns null (correct tenant filter bypass applied, but still cross-tenant — wrong behavior confirmed blocked)
- [ ] Integration test (SQLite): entity in tenant A with `GetByIdForTenantAsync(id, tenantA)` returns entity; `GetByIdForTenantAsync(id, tenantB)` returns null
- [ ] The original single-method signature `GetByIdForTenantAsync(TId, Guid, CancellationToken)` continues to compile with the new soft-delete-preserving semantics (keeping the method name, changing its behavior)
- [ ] `06.Persistence/CLAUDE.md` `TenantedRepository` section updated to document both methods and their respective filter semantics
---

---
### P-101 — Persistence EfCore: Add ListPagedProjectedAsync to IReadRepository and EfReadRepository

**Status:** `●` Complete
**Work Order:** WO-017
**Domain:** 06.Persistence
**Depends on:** P-097

#### What is needed

The most common query pattern in microservices is "return a paged list of DTOs" — not "return a paged list of aggregate roots". Currently:

- `ListPagedAsync(spec)` returns `PagedList<TAggregate>` — requires in-memory mapping to DTOs after the call, defeating SQL projection benefits.
- `ListProjectedAsync(projectionSpec)` returns `IReadOnlyList<TResult>` — projects to DTOs at SQL level but provides no paging metadata (`TotalCount`, `TotalPages`, etc.).

Neither method serves the primary use case without a workaround. Teams are forced to: (a) use `ListPagedAsync` and map in memory, or (b) use `ListProjectedAsync` with manual paging and a separate `CountAsync` call — two round trips, manually wired, inconsistent across services.

Add `ListPagedProjectedAsync<TResult>` to both the interface and implementation:

**In `IReadRepository<TAggregate, TId>` (Abstractions):**

```
ListPagedProjectedAsync<TResult>(IProjectionSpecification<TAggregate, TResult> spec, CancellationToken ct)
    → Task<PagedList<TResult>>
```

The spec supplies both the pipeline (criteria, ordering, Skip/Take) and the selector. The method issues two DB round-trips: count query (Skip/Take stripped, projection NOT applied — counts the full filtered set) and data query (full spec with projection applied).

**In `EfReadRepository<TAggregate, TId>` (EfCore):**

Implementation mirrors `ListPagedAsync` but uses `GetProjectedQuery` instead of `GetQuery` for the data query. The count query uses `GetQuery` (without projection) to count the filtered aggregates before projection. Page metadata is extracted the same way as `ListPagedAsync`.

The `NoPagingWrapper<T>` private class already used in `ListPagedAsync` is reused for the count query.

#### Why this is needed

"Paged DTO list" is the single most frequent read-side pattern in CQRS microservices. Every query handler that returns paginated data to a UI or API needs this. Without it, teams either accept the in-memory mapping overhead or write inconsistent two-round-trip workarounds. Adding this one method to `IReadRepository` eliminates the entire class of "paged projection" workarounds across all services. The implementation cost is minimal — it reuses `GetProjectedQuery` (from P-097) and the existing `NoPagingWrapper` and `ExtractPageInfo` private helpers.

#### Acceptance criteria
- [ ] `IReadRepository<TAggregate, TId>` in Abstractions declares `ListPagedProjectedAsync<TResult>(IProjectionSpecification<TAggregate, TResult> spec, CancellationToken ct) → Task<PagedList<TResult>>`
- [ ] `EfReadRepository<TAggregate, TId>` implements the method: count query uses `GetQuery` (no projection, no Skip/Take) then `CountAsync`; data query uses `GetProjectedQuery` (with projection, with Skip/Take) then `ToListAsync`; both under the same `DbContext` scope
- [ ] `PagedList<TResult>.Create` is called with the projected items, page, pageSize, and totalCount
- [ ] Integration test (SQLite): 10 aggregates; `ListPagedProjectedAsync` with page 2 size 3 → 3 DTOs, `TotalCount == 10`, `Page == 2`, `PageSize == 3`
- [ ] Integration test (SQLite): empty set → `TotalCount == 0`, `Items == []`
- [ ] Integration test (SQLite): page beyond data → empty items, correct `TotalCount`
- [ ] Integration test (SQLite): verify projection is applied at SQL level — the projected result contains only the mapped fields (no full aggregate data)
- [ ] `ContractShapeTests` in Abstractions assert the new method exists on `IReadRepository`
- [ ] `06.Persistence/CLAUDE.md` updated: `IReadRepository` contract table adds `ListPagedProjectedAsync`; `EfReadRepository` section updated to document the two-round-trip behavior
- [ ] Depends on P-097 being complete (requires `GetProjectedQuery` on the interface, not a concrete cast)
---

---
### P-102 — Persistence EfCore: Rename ValueObjectOwnershipConvention to Reflect Static Utility Intent

**Status:** `●` Complete
**Work Order:** WO-017
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

`ValueObjectOwnershipConvention` is named as if it implements the EF Core `IModelFinalizingConvention` interface and auto-applies during model building — it does not. It is a static utility class with a single `Apply(ModelBuilder modelBuilder)` method that must be called manually from `OnModelCreating`. This naming mismatch misleads developers who expect it to work like `SnakeCaseNamingConvention` (which does implement `IModelFinalizingConvention` and is registered via `ConfigureConventions`).

**Option A (Preferred) — Implement as a real `IModelFinalizingConvention`:**

Rename to `ValueObjectOwnershipConvention` (keeping the name) and implement `IModelFinalizingConvention`:

```csharp
public sealed class ValueObjectOwnershipConvention : IModelFinalizingConvention
{
    void IModelFinalizingConvention.ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
```

Register it in `SharedKernelDbContext.OnConfiguring` or in `EfCorePersistenceBuilder.Build()` via `DbContextOptionsBuilder.ReplaceService<IConventionSet>` (which is the EF Core API for convention registration) or via `ConfigureConventions` override in the base context.

**Option B (Minimal, no convention system change) — Rename to clarify static intent:**

If implementing as a real convention is too invasive for this phase, rename to `ValueObjectOwnershipBuilder` and update all XML docs to explicitly state it must be called manually from `OnModelCreating`. The name change removes the false `Convention` suffix that implies auto-application.

**Option B is the correct scope for this phase** — implementing a full `IModelFinalizingConvention` would require changes to `SharedKernelDbContext.OnConfiguring` and the EF Core convention pipeline, which is a larger change. Option B is a safe, non-breaking rename that eliminates the naming confusion with zero behavioral impact.

After renaming:
- All references in test code and in `06.Persistence/CLAUDE.md` are updated
- XML doc on the class is updated to explicitly state: "Call `ValueObjectOwnershipBuilder.Apply(modelBuilder)` at the end of `OnModelCreating` after all entity configurations are applied. This is a post-processing utility, not an EF Core convention — it does not auto-apply."

#### Why this is needed

Developer confusion from the naming mismatch is guaranteed. A developer building a new multi-tenant service that uses value objects will either: (a) not call `Apply` at all because the name implies it auto-applies, resulting in missing `OwnsOne` configurations and runtime EF Core model-build errors; or (b) spend time looking for how to register it as a convention (via `ConfigureConventions`) only to discover it doesn't implement the interface. Either outcome costs developer time. The rename is a one-line fix that prevents this confusion permanently.

#### Acceptance criteria
- [ ] The class is renamed from `ValueObjectOwnershipConvention` to `ValueObjectOwnershipBuilder`
- [ ] All test code references are updated to use the new name
- [ ] `06.Persistence/CLAUDE.md` updated: all references to `ValueObjectOwnershipConvention` replaced with `ValueObjectOwnershipBuilder`; usage note added explicitly stating manual `OnModelCreating` call is required
- [ ] XML doc on the class states explicitly: "This is a static utility method, not an EF Core `IModelFinalizingConvention`. Call `ValueObjectOwnershipBuilder.Apply(modelBuilder)` manually at the end of `OnModelCreating`."
- [ ] All existing convention tests in `DomainPrimitiveConventionTests.cs` continue to pass with the new name
- [ ] `dotnet build` produces zero errors and zero warnings on the EfCore package after the rename
---

---
### P-103 — Governance: Architecture Rules for EfCore Package Hygiene

**Status:** `●` Complete
**Work Order:** WO-017
**Domain:** 00.Governance
**Depends on:** P-097, P-099

#### What is needed

Three new architecture enforcement rules in `SharedKernel.ArchitectureTests` addressing the EfCore package hygiene concerns identified in WO-017:

**Rule 1 — No concrete downcast of `ISpecificationEvaluator<T>` to implementation type:**
After P-097, `ISpecificationEvaluator<T>` in Abstractions exposes `GetProjectedQuery`. Any code in the production packages that casts `ISpecificationEvaluator<T>` to a concrete type (e.g., `(SpecificationEvaluator<T>)evaluator`) is a violation. Add a NetArchTest rule (or Roslyn analyzer check) that asserts no type in `SharedKernel.Persistence.EfCore` contains a direct cast from `ISpecificationEvaluator<>` to any concrete class. This prevents future regression to the downcast pattern.

**Rule 2 — `IUnitOfWork` implementors must have exactly one constructor:**
After P-098, `EfUnitOfWork` uses a single constructor. To prevent future contributors from adding a second constructor (recreating the DI ambiguity), add a NetArchTest rule asserting that all types in `06.Persistence` implementing `IUnitOfWork` have exactly one public constructor. This is a simple structural check that runs in milliseconds.

**Rule 3 — `ITransactionalUnitOfWork` must be the only EF Core transaction surface:**
After P-099, `ITransactionalUnitOfWork` is the abstraction-layer entry point for explicit transactions. No code in `05.Application` assemblies (application handlers) may reference `Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction` directly. Add a NetArchTest rule asserting that no type in any `05.Application` or downstream non-persistence namespace references `IDbContextTransaction` or `DbContext.Database.BeginTransactionAsync`. The boundary is `ITransactionalUnitOfWork` — application layer never sees the EF Core transaction type.

All three rules must be documented in `00.Governance/CLAUDE.md` under the Persistence Enforcement section with: rule name, rationale, offending-pattern example, compliant-pattern example, and exemptions (e.g., `06.Persistence.EfCore` itself is exempt from Rule 3).

#### Why this is needed

Rule 1 prevents regression to the concrete-downcast anti-pattern that P-097 fixes. Without this rule, a future refactor could re-introduce the downcast silently. Rule 2 prevents DI ambiguity regression — a one-line fix in `EfUnitOfWork` could be undone by a contributor who "helpfully" adds a convenience constructor. Rule 3 enforces the transaction abstraction boundary: if application handlers start injecting `IDbContextTransaction` directly, the entire persistence abstraction is undermined. These are low-cost, high-signal rules that protect the most critical architectural decisions made in WO-017.

#### Acceptance criteria
- [ ] Rule 1 exists as a `[Fact]` test asserting no `ISpecificationEvaluator<>` → concrete-type casts exist in `SharedKernel.Persistence.EfCore` assembly
- [ ] Rule 2 exists asserting all `IUnitOfWork` implementors in `06.Persistence` have exactly one public constructor; passes on `EfUnitOfWork` after P-098
- [ ] Rule 3 exists asserting no type in `05.Application` namespace pattern references `IDbContextTransaction`; exempts `06.Persistence` assemblies
- [ ] All three rules documented in `00.Governance/CLAUDE.md` with rationale, example, and exemptions
- [ ] All three rules run as `[Fact]` tests with descriptive failure messages; no false positives on the existing SharedKernel assemblies
- [ ] Governance test suite passes with all new rules included
---

---
### P-104 — Testing: EfCore Persistence Test Coverage Gaps — AsNoTracking, Transaction Scope, Paged Projection

**Status:** `○` Pending
**Work Order:** WO-017
**Domain:** 16.Testing
**Depends on:** P-097, P-099, P-101

#### What is needed

Several behavioral contracts of the EfCore persistence package lack test coverage. This phase adds the missing tests to `SharedKernel.Persistence.EfCore.Tests` and updates `SharedKernel.Testing` with reusable helpers for persistence testing.

**Gap 1 — `AsNoTracking` behavioral verification:**
No existing test verifies that when `spec.AsNoTracking == true`, the entities returned by `ListAsync`, `GetBySpecAsync`, `GetByIdsAsync`, and `ListProjectedAsync` are NOT tracked by the `DbContext` change tracker. This is a behavioral contract with real performance implications. Add tests:
- `ListAsync` with `ReadOnlySpecification<T>` (which sets `AsNoTracking = true`) → returned entities have `EntityState.Detached`
- `GetBySpecAsync` with `AsNoTracking = true` → returned entity has `EntityState.Detached`
- `ListAsync` without `AsNoTracking` → returned entities have `EntityState.Unchanged` (are tracked)
- Verify that modifying a no-tracking entity and calling `SaveChangesAsync` does NOT generate an UPDATE statement

**Gap 2 — Assembly scan `OnModelCreating` smoke test:**
The production pattern is that a downstream `SharedKernelDbContext` subclass calls `base.OnModelCreating(modelBuilder)` which scans the subclass's assembly for `IEntityTypeConfiguration<T>` implementations. The test `TestDbContext` deliberately bypasses `base.OnModelCreating` to avoid test configuration conflicts. Add a dedicated `AssemblyScanDbContext` in the tests that DOES call `base.OnModelCreating` with a clean assembly containing only one configuration — verify the configuration is correctly applied via assembly scan.

**Gap 3 — Transaction scope round-trip (after P-099):**
When `ITransactionalUnitOfWork` is implemented (P-099), add:
- Test: begin transaction → add entity → commit → entity persisted
- Test: begin transaction → add entity → rollback → entity NOT persisted
- Test: multiple repository operations within one transaction scope → all committed atomically

**Gap 4 — `ListPagedProjectedAsync` coverage (after P-101):**
The new paged projection method (P-101) needs its own test class:
- Paged DTO projection: 10 aggregates, page 2 size 3 → 3 DTOs, `TotalCount == 10`
- Empty set: `TotalCount == 0`, `Items == []`
- Page beyond data: empty items, correct `TotalCount`
- Verify projection columns only (not full aggregate round-trip overhead)

**`SharedKernel.Testing` helpers:**
Add `PersistenceTestHelpers` static class to `SharedKernel.Testing` with:
- `AssertEntityTracked<T>(DbContext ctx, T entity)` — asserts entity is in tracked state
- `AssertEntityNotTracked<T>(DbContext ctx, T entity)` — asserts entity has `EntityState.Detached`

These helpers are useful across any EfCore test project.

#### Why this is needed

`AsNoTracking` is one of the most performance-critical EF Core features and its absence from test coverage means it is untested as a behavioral contract. A regression (e.g., the `AsNoTracking` step being accidentally removed from `SpecificationEvaluator`) would not be caught until production profiling reveals unexpected change-tracking overhead. The assembly scan test closes the gap between "we test explicit configuration" and "we test the discovery path" — the primary path that production services use. The transaction and paged projection tests are required companions to the P-099 and P-101 implementations.

#### Acceptance criteria
- [ ] Four `AsNoTracking` tests added to `EfReadRepositoryTests`: detached state on `ListAsync` with `AsNoTracking`, detached state on `GetBySpecAsync`, tracked state on `ListAsync` without `AsNoTracking`, no UPDATE for modified no-tracking entity
- [ ] Assembly scan smoke test: `AssemblyScanDbContext` calls `base.OnModelCreating`; verify one `IEntityTypeConfiguration<T>` from the test assembly is applied
- [ ] Transaction scope tests (depends on P-099): begin/commit/rollback round-trips as described
- [ ] `ListPagedProjectedAsync` tests (depends on P-101): four test cases as described
- [ ] `PersistenceTestHelpers` static class added to `SharedKernel.Testing` with `AssertEntityTracked` and `AssertEntityNotTracked`
- [ ] All new tests use SQLite provider — no Testcontainers needed for EfCore behavioral tests
- [ ] All existing tests continue to pass — no regressions
- [ ] `16.Testing/CLAUDE.md` (if it exists) updated with `PersistenceTestHelpers` documentation
---

---
### P-105 — Persistence EfCore: Correctness Fixes — Double-Dispatch Bug, GetByIdsAsync Expression Tree, and AOT Annotation

**Status:** `●` Complete
**Work Order:** WO-018
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

Three correctness issues in `SharedKernel.Persistence.EfCore` that are bugs or trim-safety gaps requiring surgical fixes before the package can be declared production-ready.

**Fix 1 — Double domain-event dispatch in `EfTransactionalUnitOfWork`:**
The current `EfTransactionalUnitOfWork.SaveChangesAsync` calls `DispatchAndClearEventsAsync` immediately after each `SaveChangesAsync`. When a caller uses the transactional flow — `SaveChangesAsync` followed by `CommitAsync` — domain events are dispatched twice: once after save and again after commit. This is a correctness bug: integration event publishers (07.Messaging) and any idempotency logic will see duplicate domain events for a single save cycle.

The fix: `EfTransactionalUnitOfWork.SaveChangesAsync` must NOT call `DispatchAndClearEventsAsync`. Instead, it must only stage the save (delegate to `DbContext.SaveChangesAsync`). Domain event dispatch must happen exclusively in `EfPersistenceTransaction.CommitAsync` (after the database transaction is committed). For callers who never open an explicit transaction but still use `EfTransactionalUnitOfWork` directly as `IUnitOfWork`, a fallback dispatch after `SaveChangesAsync` is still needed — this is handled by detecting whether an active `IDbContextTransaction` is in progress. If `DbContext.Database.CurrentTransaction` is null (no explicit transaction open), `SaveChangesAsync` dispatches immediately; if a transaction is active, dispatch is deferred to `CommitAsync`. This is the standard EF Core transactional dispatch pattern.

The `EfUnitOfWork.SaveChangesAsync` (non-transactional) is NOT affected — its behavior (dispatch after save) is correct and unchanged.

Test additions required:

- `begin → SaveChangesAsync → CommitAsync` → domain events dispatched exactly once (after commit)
- `SaveChangesAsync` without an open transaction via `EfTransactionalUnitOfWork` → domain events dispatched once (after save, since no transaction active)
- `begin → SaveChangesAsync → RollbackAsync` → domain events NOT dispatched

**Fix 2 — `EfReadRepository.GetByIdsAsync` expression-tree rewrite:**
The current implementation uses `EF.Property<TId>(e, "Id")` inside a `Contains` predicate. When `TId` is a `StronglyTypedId<Guid>` (a value object with a registered `ValueConverter`), EF Core's translation of `idList.Contains(EF.Property<TId>(e, "Id"))` may fall back to client-side evaluation because the converter is applied to the column but the `Contains` argument is a list of `TId` instances. This silently loads all rows to the client and filters in memory — a full table scan for the `GetByIdsAsync` use case.

The fix: replace the `EF.Property` approach with the same expression-tree construction used in `EfRepository.ExistsAsync` — build `e => ids.Contains(e.Id)` as an expression tree using `Expression.Parameter`, `Expression.Property("Id")`, and `Expression.Call(containsMethod)`. EF Core's value converter is applied correctly to expression-tree lambda predicates because the LINQ provider resolves the converter at the property level. The resulting SQL is a proper `WHERE Id IN (...)` with the converter applied to each element.

Test additions required:

- `GetByIdsAsync` with `StronglyTypedId<Guid>` IDs — verify the SQL contains an `IN` clause (not a client-side predicate)
- All existing `GetByIdsAsync` behavioral tests continue to pass

**Fix 3 — `ValueObjectOwnershipBuilder` DynamicallyAccessedMembers annotation:**
`ValueObjectOwnershipBuilder.Apply` calls `entityType.ClrType.GetProperties(BindingFlags.Public | BindingFlags.Instance)` at model-build time. Under NativeAOT publishing, the .NET trimmer emits a trim warning (`IL2026` / `IL2075`) because the properties of the CLR entity types may be trimmed if not explicitly attributed. Since EF Core itself is not NativeAOT-safe without compiled models, this warning does not block current use — but it produces noise in AOT-enabled services and will become a compile error if NativeAOT is mandated in future.

The fix: add `[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]` to the `clrType` parameter at the point of reflection access, or apply it via the intermediate `entityType.ClrType` usage site. The CLAUDE.md AOT note for this class must be updated to document the annotation and state explicitly why it is acceptable (model-build time only — not a hot path).

No behavioral changes from this fix — it is purely an attribute annotation to satisfy the trim analyzer.

#### Why this is needed

The double-dispatch bug (Fix 1) is a correctness defect: any consuming service using `ITransactionalUnitOfWork` with domain events will silently produce duplicate events. This is invisible in unit tests (which mock the dispatcher) and only manifests in integration tests or production. At platform scale, duplicate events cause idempotency failures, duplicate message publishing, and difficult-to-diagnose audit anomalies. The `GetByIdsAsync` fix (Fix 2) is a performance defect: a silent client-side evaluation fallback turns an `O(1)` database lookup into an `O(n)` full table scan when strongly-typed IDs are used — and the entire platform uses strongly-typed IDs. The AOT annotation (Fix 3) is a quality hygiene issue: suppressing known trim warnings before they cascade into compile errors in AOT-publishing services.

#### Acceptance criteria

- [ ] `EfTransactionalUnitOfWork.SaveChangesAsync` does NOT call `DispatchAndClearEventsAsync` when a `DbContext.Database.CurrentTransaction` is active; it dispatches immediately only when no explicit transaction is open
- [ ] `EfPersistenceTransaction.CommitAsync` calls `DispatchAndClearEventsAsync` after the database commit succeeds
- [ ] Domain event dispatch test: `begin → SaveChangesAsync → CommitAsync` → events dispatched exactly once
- [ ] Domain event dispatch test: `SaveChangesAsync` (no open transaction via `EfTransactionalUnitOfWork`) → events dispatched once
- [ ] Domain event dispatch test: `begin → SaveChangesAsync → RollbackAsync` → events not dispatched
- [ ] `EfReadRepository.GetByIdsAsync` uses expression-tree `Contains` predicate — no `EF.Property` shadow accessor
- [ ] `GetByIdsAsync` with `StronglyTypedId<Guid>` entities generates a server-side `IN (...)` SQL clause (verifiable via EF Core logging or `ToQueryString()`)
- [ ] All existing `GetByIdsAsync` tests pass without regressions
- [ ] `ValueObjectOwnershipBuilder.Apply` carries `[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]` on the relevant CLR type access site
- [ ] `06.Persistence/CLAUDE.md` AOT note for `ValueObjectOwnershipBuilder` updated to document the annotation
- [ ] All existing tests green — no regressions
---

---
### P-106 — Persistence EfCore: Capability Gaps — Write-Side Spec Fetch, IDbContextFactory, Custom Interceptors, Compiled Model Hook

**Status:** `●` Complete
**Work Order:** WO-018
**Domain:** 06.Persistence
**Depends on:** P-105

#### What is needed

Four additive capabilities missing from `SharedKernel.Persistence.EfCore` that are required for production microservice scenarios.

**Capability 1 — Write-side `GetBySpecAsync` on `IRepository<T, TId>`:**
Many write-path command handlers need to fetch an aggregate for mutation by a business key (e.g., "the order whose external reference number is X") rather than by primary key. The current `IRepository<T, TId>` only provides `GetByIdAsync`, forcing teams to either inject `IReadRepository` (returning non-tracked entities, breaking update semantics) or bypass the repository abstraction entirely with raw `DbContext` access.

Add `Task<TAggregate?> GetBySpecAsync(ISpecification<TAggregate> spec, CancellationToken ct = default)` to `IRepository<T, TId>` in `SharedKernel.Persistence.Abstractions`. The EF Core implementation in `EfRepository<T, TId>` applies the specification via `ISpecificationEvaluator<T>.GetQuery` WITHOUT `AsNoTracking` — the entity is returned as tracked so that subsequent mutations are picked up by change detection. The spec's own `AsNoTracking` flag is honored (callers who explicitly set it get non-tracked; but typically write-side specs should not set `AsNoTracking`).

This method is intentionally NOT a query surface — callers must still express the fetch predicate as an `ISpecification<T>`. Raw `IQueryable` is never returned.

**Capability 2 — `IDbContextFactory<TContext>` support for background services:**
Background services, hosted services, Hangfire workers, and Temporal activities all execute outside a HTTP request scope and cannot consume a scoped `DbContext`. The standard .NET pattern is `IDbContextFactory<TContext>` (added by `AddDbContextFactory<T>`), which creates short-lived `DbContext` instances on demand.

`EfCorePersistenceBuilder` gains a new `.WithDbContextFactory()` opt-in method. When called, `Build()` additionally calls `services.AddDbContextFactory<TContext>(configureDb)` so that `IDbContextFactory<TContext>` is resolvable by background services. The factory-created contexts must respect the same interceptor registration — this requires that interceptors be registered as singletons or that the factory context options are built with the same interceptor chain. Since the three platform interceptors are scoped (they depend on scoped `IUserContext` and `IClock`), background services using the factory must be documented as resolving their own `IUserContext` scope (defaulting to the no-op placeholder) rather than inheriting an HTTP request's user context.

The CLAUDE.md must document: "Background services using `IDbContextFactory` receive a `NoOpUserContext` (userId = `Guid.Empty`) unless a custom `IUserContext` implementation is registered as a singleton. Audit fields on entities written from background services will always receive the `"system"` audit value."

**Capability 3 — Custom interceptor registration hook on `EfCorePersistenceBuilder`:**
Consuming services occasionally need to add service-specific EF Core interceptors alongside the three platform interceptors. Currently there is no clean path — they would have to override `OnConfiguring` (anti-pattern in DI contexts) or add a second `AddDbContext` registration that conflicts.

Add `.AddInterceptor<TInterceptor>()` fluent method to `EfCorePersistenceBuilder<TContext>` where `TInterceptor : class, ISaveChangesInterceptor`. When called one or more times, `Build()` registers each additional interceptor as a scoped service. These user-supplied interceptors are appended to the interceptor list alongside the three platform interceptors in `SharedKernelDbContext.OnConfiguring`. `SharedKernelDbContext` must be updated to accept `IEnumerable<ISaveChangesInterceptor> additionalInterceptors` via constructor injection (empty by default), and `OnConfiguring` must compose the platform three plus any additional interceptors.

The platform three interceptors (Audit, SoftDelete, Concurrency) always fire first — user-supplied interceptors fire after. This ordering is intentional and must be documented.

**Capability 4 — Compiled model hook on `EfCorePersistenceBuilder`:**
For services investing in NativeAOT or startup performance, EF Core compiled models eliminate model-building overhead. The compiled model is produced by `dotnet ef dbcontext optimize` and used via `optionsBuilder.UseModel(compiledModel)`.

Add `.WithCompiledModel(IModel compiledModel)` optional method to `EfCorePersistenceBuilder<TContext>`. When called, `Build()` wraps the caller-supplied `configureDb` action with an additional `UseModel(compiledModel)` call. This is a pure pass-through — the builder does not validate the compiled model; it merely ensures it is injected into the options before the context is created. XML doc must state: "Compiled models are produced via `dotnet ef dbcontext optimize`. When used, `ValueObjectOwnershipBuilder.Apply` and runtime model-building scans do not run — all mappings must be in the compiled model."

#### Why this is needed

Capability 1 (write-side spec fetch) is needed because every non-trivial write handler needs to fetch by business key, not just by PK. Without it, teams use `IReadRepository` (wrong tracking semantics) or bypass the abstraction. Capability 2 (`IDbContextFactory`) is a K8s-native requirement: background processing jobs that write to the database are ubiquitous in microservices (Hangfire, Temporal, outbox processors, event consumers) and all require factory-pattern DbContext creation. Without this, teams either use a singleton scoped context (a known EF Core anti-pattern causing concurrency issues) or write bespoke factory registration that bypasses the SharedKernel wiring. Capability 3 (custom interceptors) enables teams to add service-specific cross-cutting persistence concerns (audit bridges, query logging, telemetry) without forking the SharedKernel or using anti-patterns. Capability 4 (compiled model) is the standard EF Core optimization for latency-sensitive K8s cold-start scenarios — services that ship compiled models currently have no clean way to use them with the SharedKernel builder.

#### Acceptance criteria

- [ ] `IRepository<TAggregate, TId>` in `SharedKernel.Persistence.Abstractions` declares `GetBySpecAsync(ISpecification<TAggregate> spec, CancellationToken ct)`
- [ ] `EfRepository<TAggregate, TId>` implements `GetBySpecAsync` using `ISpecificationEvaluator<T>.GetQuery` without forcing `AsNoTracking` — spec's own flag is honored
- [ ] `GetBySpecAsync` on `EfRepository` returns a tracked entity when the spec has `AsNoTracking == false`
- [ ] `ContractShapeTests` in Abstractions verifies `IRepository<T,TId>` declares `GetBySpecAsync`
- [ ] `EfCorePersistenceBuilder` has `.WithDbContextFactory()` method; `Build()` calls `AddDbContextFactory<TContext>` when it is invoked
- [ ] Factory-created contexts use the same interceptor chain as scoped contexts; `NoOpUserContext` audit behavior is documented
- [ ] `EfCorePersistenceBuilder` has `.AddInterceptor<TInterceptor>()` generic method; multiple calls accumulate interceptors
- [ ] `SharedKernelDbContext` accepts additional interceptors via constructor; platform three always fire before user-supplied interceptors
- [ ] `EfCorePersistenceBuilder` has `.WithCompiledModel(IModel model)` method; `Build()` applies `UseModel(model)` to the context options when called
- [ ] All new builder methods return `EfCorePersistenceBuilder<TContext>` for fluent chaining
- [ ] `06.Persistence/CLAUDE.md` documents all four new capabilities with usage examples
- [ ] Tests: write-side `GetBySpecAsync` round-trip (tracked entity mutation persisted), factory-created context audit defaults, custom interceptor firing order, compiled model passthrough (smoke test with a hand-written `IModel` stub)
- [ ] All existing tests pass — no regressions
---

---
### P-107 — Persistence Abstractions: String-Based Navigation Include Support in ISpecification

**Status:** `●` Complete
**Work Order:** WO-018
**Domain:** 06.Persistence
**Depends on:** P-106

#### What is needed

`ISpecification<T>` and `SpecificationEvaluator<T>` currently support only expression-based eager-loading includes (`Expression<Func<T, object>>`). For dynamic include paths — where the navigation property path is determined at runtime, or for deep nested navigation chains like `"Orders.Items.Product"` — string-based includes are necessary. EF Core supports string-based includes via `IQueryable<T>.Include(string navigationPropertyPath)`.

**`ISpecification<T>` extension:**
Add a `IReadOnlyList<string> StringIncludes` property to `ISpecification<T>` in `SharedKernel.Domain.Specifications`. The default concrete `Specification<T>` base must expose a protected `AddStringInclude(string navigationPath)` method, with the property returning an empty read-only list by default.

This is an additive, non-breaking change — existing specifications that do not call `AddStringInclude` return an empty list and are unaffected.

**`SpecificationEvaluator<T>` update:**
The string-includes step is added to `GetQuery` between step 2 (expression includes) and step 3 (ordering) — it becomes step 2b. The evaluator applies each string in `StringIncludes` as an `IQueryable<T>.Include(string)` call. The ordering step number in documentation shifts accordingly.

`ISpecificationEvaluator<T>` contract on `GetQuery` remains unchanged (it already delegates to the spec properties) — any `ISpecificationEvaluator<T>` implementation that reads `spec.Includes` must also read `spec.StringIncludes` to be complete. The interface contract doc is updated to state this expectation.

**`Specification<T>` base update:**
`Specification<T>` gains an internal `List<string> _stringIncludes` backing field. `AddStringInclude(string path)` appends to this list. `StringIncludes` returns the list as `IReadOnlyList<string>`. Null or whitespace paths are rejected with an `ArgumentException`.

**`ReadOnlySpecification<T>` and `PagedSpecification<T>` bases:**
Both inherit `StringIncludes` from `Specification<T>` — no changes needed to them directly.

**CLAUDE.md updates:**
`06.Persistence/CLAUDE.md` specification evaluator ordering section updated to document step 2b. `03.Domain/CLAUDE.md` specification section updated to document `StringIncludes` and `AddStringInclude`.

**Test additions:**

- A specification with `AddStringInclude("NavigationProperty")` — verify the string include is applied (entity nav prop populated)
- A specification with both expression includes and string includes — verify both are applied
- `SpecificationEvaluator` ordering test: string includes applied after expression includes and before primary sort

#### Why this is needed

Complex domain models with multi-level navigation chains are common at microservice scale (e.g., `Order → OrderLines → Product → Category`). Expression-based includes hit a readability wall beyond two levels — `ThenInclude` chains become cumbersome. String-based includes provide a clean alternative for dynamic or deep paths. Without this, teams either abandon the specification pattern for complex queries (reaching directly for `IQueryable`, breaking the abstraction), or write verbose multi-level expression chains that are harder to maintain. The additive, non-breaking design means zero impact on existing specifications.

#### Acceptance criteria

- [ ] `ISpecification<T>` declares `IReadOnlyList<string> StringIncludes` property
- [ ] `Specification<T>` base implements `StringIncludes` with an internal list and `AddStringInclude(string path)` protected method
- [ ] `AddStringInclude` rejects null/whitespace with `ArgumentException`
- [ ] `SpecificationEvaluator<T>.GetQuery` applies string includes between expression includes (step 2) and primary sort (step 3)
- [ ] All existing specifications that do not call `AddStringInclude` return empty `StringIncludes` — zero behavioral impact
- [ ] Test: string include path applied → navigation property populated in result
- [ ] Test: expression include + string include both applied → both navigation properties populated
- [ ] `ContractShapeTests` in Abstractions verifies `ISpecification<T>` has `StringIncludes` property
- [ ] `06.Persistence/CLAUDE.md` evaluator ordering section updated (step 2b documented)
- [ ] `03.Domain/CLAUDE.md` updated with `StringIncludes` / `AddStringInclude` documentation
- [ ] All existing specification and evaluator tests pass — no regressions
---

---
### P-108 — Persistence PostgreSQL: SharedKernel.Persistence.PostgreSQL Package Implementation

**Status:** `●` Complete
**Work Order:** WO-018
**Domain:** 06.Persistence
**Depends on:** P-106

#### What is needed

The `SharedKernel.Persistence.PostgreSQL` package, previously deferred as P-071, must now be fully implemented. This package provides PostgreSQL-specific EF Core conventions and helpers on top of `SharedKernel.Persistence.EfCore`.

The package lives at `06.Persistence/SharedKernel.Persistence.PostgreSQL/`. It references `SharedKernel.Persistence.EfCore` and `Npgsql.EntityFrameworkCore.PostgreSQL` 10.x. It does NOT reference Dapper or connection factories — those are in the Dapper package.

**`SnakeCaseNamingConvention`:**
An `IModelFinalizingConvention` that converts all table names, column names, index names, and constraint names to `snake_case`. Applied automatically when `UsePostgreSQL()` DI extension is called. Implementation must handle `PascalCase`, `camelCase`, and already-snake-cased names idempotently.

**`UsePostgreSQL()` DI extension on `DbContextOptionsBuilder`:**
Configures the Npgsql provider with the connection string AND applies `SnakeCaseNamingConvention` via `ConfigureConventions`. Additionally enables pgvector support if `Pgvector.EntityFrameworkCore` is referenced. Single call replaces manual provider + convention wiring. This extension is called inside the `configureDb` action passed to `AddSharedKernelEfCore<TContext>`.

**JSONB support (`HasJsonbColumn` extension):**
A `JsonbEntityTypeBuilderExtension` static class with `.HasJsonbColumn<TProperty>(propertyExpression)` extension on `EntityTypeBuilder<T>`. Applies `.HasColumnType("jsonb")`. STJ serialization is configured globally by Npgsql — individual JSONB columns do not need per-column converters. A companion `[JsonbColumn]` attribute (optional, for documentation and future tooling).

**pgvector support (`HasVectorColumn` extension):**
A `VectorEntityTypeBuilderExtension` static class with `.HasVectorColumn<TProperty>(propertyExpression, int dimensions)` extension on `EntityTypeBuilder<T>`. Requires the `Pgvector.EntityFrameworkCore` NuGet package. Applies `.HasColumnType("vector({dimensions})")`. Companion `[VectorColumn(int dimensions)]` attribute.

**PostgreSQL DI registration (`AddSharedKernelPostgreSQL`):**
`services.AddSharedKernelPostgreSQL(string connectionString)` registers an `NpgsqlDataSource` (configured with STJ options) and `IDbConnectionFactory → NpgsqlConnectionFactory` (scoped). This extension is for services that also want Dapper read-side queries alongside EF Core writes — it wires the shared `NpgsqlDataSource` so both EF Core (via `UseNpgsql(dataSource)`) and Dapper (via `IDbConnectionFactory`) share the same connection pool.

**`NpgsqlConnectionFactory`:**
A sealed implementation of `IDbConnectionFactory` (from `SharedKernel.Persistence.Abstractions`) backed by the injected `NpgsqlDataSource`. `CreateConnectionAsync` calls `NpgsqlDataSource.OpenConnectionAsync()`. Caller is responsible for disposal.

Note: The `NpgsqlConnectionFactory` in the CLAUDE.md is listed under `SharedKernel.Persistence.Dapper` — this is because the Dapper package is the consumer. However, it is equally valid to place it here in the PostgreSQL package (which also needs it for the shared data source). The decision: place `NpgsqlConnectionFactory` in THIS package (PostgreSQL) and have the Dapper package reference it. The Dapper package is a pure Dapper read-service layer — it does not need its own connection factory if the PostgreSQL package provides one.

**Tests (`SharedKernel.Persistence.PostgreSQL.Tests`):**

All PostgreSQL package tests require a real PostgreSQL Testcontainer (no SQLite fallback — snake_case naming, JSONB, and pgvector are PostgreSQL-specific features):

- `SnakeCaseNamingConvention` test: verify table and column names in `DbContext.Model` are snake_case
- JSONB round-trip: insert entity with JSONB column, fetch back, verify deserialization
- pgvector round-trip: insert entity with vector column, fetch back, verify dimensions
- `NpgsqlConnectionFactory` test: `CreateConnectionAsync` returns an open `NpgsqlConnection`; connection is disposed by caller
- `AddSharedKernelPostgreSQL` smoke test: services resolve `IDbConnectionFactory` as `NpgsqlConnectionFactory`

#### Why this is needed

Without the PostgreSQL package, every microservice using PostgreSQL must manually wire `SnakeCaseNamingConvention`, JSONB column types, pgvector columns, and Npgsql connection factories — code that is identical across hundreds of services. This is exactly the SharedKernel's purpose: eliminate that repetition. The snake_case convention in particular is universally expected in PostgreSQL schemas (column names like `created_by`, not `CreatedBy`), and a missing convention means either mis-named columns or per-entity manual overrides in every configuration class. The shared `NpgsqlDataSource` pattern is critical for connection-pool efficiency: without it, EF Core and Dapper open separate connection pools, doubling the PostgreSQL connection count per service.

#### Acceptance criteria

- [ ] `SharedKernel.Persistence.PostgreSQL` project exists at `06.Persistence/SharedKernel.Persistence.PostgreSQL/`
- [ ] Project references `SharedKernel.Persistence.EfCore` and `Npgsql.EntityFrameworkCore.PostgreSQL` 10.x
- [ ] `SnakeCaseNamingConvention` implements `IModelFinalizingConvention`; correctly converts `PascalCase` to `snake_case` idempotently
- [ ] `UsePostgreSQL(DbContextOptionsBuilder, string connectionString)` extension configures Npgsql + `SnakeCaseNamingConvention`
- [ ] `HasJsonbColumn<TProperty>` extension applies `HasColumnType("jsonb")` to the specified property
- [ ] `HasVectorColumn<TProperty>` extension applies the correct pgvector column type with the specified dimensions
- [ ] `AddSharedKernelPostgreSQL(IServiceCollection, string connectionString)` registers `NpgsqlDataSource` and `IDbConnectionFactory → NpgsqlConnectionFactory`
- [ ] `NpgsqlConnectionFactory` implements `IDbConnectionFactory`; returns an open `NpgsqlConnection`; caller disposes
- [ ] Project registered in `Platform.SharedKernel.slnx` under solution folder `06.Persistence`
- [ ] Test project at `SharedKernel.Persistence.PostgreSQL.Tests/` using Testcontainers PostgreSQL
- [ ] `SnakeCaseNamingConvention` test: column names verified as `snake_case` via `DbContext.Model`
- [ ] JSONB round-trip test passes
- [ ] pgvector round-trip test passes
- [ ] `NpgsqlConnectionFactory` integration test passes
- [ ] `06.Persistence/CLAUDE.md` package table updated to reflect PostgreSQL package is now implemented (not deferred)
- [ ] All public types carry XML doc comments
---

---
### P-109 — Persistence Dapper: SharedKernel.Persistence.Dapper Package Implementation

**Status:** `●` Complete
**Work Order:** WO-018
**Domain:** 06.Persistence
**Depends on:** P-108

#### What is needed

The `SharedKernel.Persistence.Dapper` package, previously deferred as P-072, must now be fully implemented. This package provides the Dapper micro-ORM read-side layer for services that need raw SQL query performance alongside EF Core writes.

The package lives at `06.Persistence/SharedKernel.Persistence.Dapper/`. It references `SharedKernel.Persistence.Abstractions`, `SharedKernel.Persistence.PostgreSQL` (for `NpgsqlConnectionFactory`), and `Dapper` NuGet. It does NOT reference `SharedKernel.Persistence.EfCore` directly — the read-side is provider-agnostic at the repository level.

**`StronglyTypedIdTypeHandler<TStronglyTypedId, TValue>`:**
An abstract class extending `SqlMapper.TypeHandler<TStronglyTypedId>`. `SetValue` writes the underlying `TValue` via the `implicit operator TValue` (static method call — no reflection). `Parse` constructs the strongly-typed ID from the raw DB value using the `implicit operator` or factory method on `StronglyTypedId<TValue>`. Consuming services implement a one-line concrete handler per ID type (no logic — just type parameters). Registered at startup via `DapperTypeHandlers.Register()`.

**`SmartEnumTypeHandler<TEnum, TValue>`:**
An abstract class extending `SqlMapper.TypeHandler<TEnum>` constrained to `TEnum : SmartEnum<TEnum, TValue>`. `SetValue` writes the underlying `TValue`. `Parse` calls `SmartEnum<TEnum, TValue>.TryFromValue` — no reflection in the hot path. Consuming services implement a one-line concrete handler per enum type.

**`DapperTypeHandlers`:**
A static class with a `Register()` method that is idempotent (can be called multiple times without duplicate registration). `Register()` sets Dapper's `SqlMapper.AddTypeHandler` for any platform-wide handlers (e.g., `DateTimeOffset` → PostgreSQL `timestamptz` if Dapper does not handle it natively in 10.x). Individual service-specific handlers (`StronglyTypedIdTypeHandler` subclasses) are registered by consuming services in their composition root.

**`DapperReadService`:**

An abstract base class providing the three protected read methods:

- `QueryAsync<TResult>(string sql, object? parameters, CancellationToken ct)` — returns `IEnumerable<TResult>`
- `QuerySingleOrDefaultAsync<TResult>(string sql, object? parameters, CancellationToken ct)` — returns `TResult?`
- `ExecuteAsync(string sql, object? parameters, CancellationToken ct)` — returns `int` (rows affected)

All three methods open a connection via `IDbConnectionFactory.CreateConnectionAsync()`, execute the query, and dispose the connection. They use `await using` or explicit dispose to ensure connection release. SQL is caller-supplied — no query builder abstraction. Parameterized queries only — string interpolation in SQL is a hard violation (SQL injection risk). All three are `protected` — subclasses form the public API surface.

**Dapper DI extensions:**
`AddSharedKernelDapper(this IServiceCollection services)` — registers `DapperTypeHandlers.Register()` as a startup action (or calls it inline). Returns `IServiceCollection` for chaining. Note: `IDbConnectionFactory` is registered by `AddSharedKernelPostgreSQL` — the Dapper extension only wires the type handlers and any Dapper-specific configuration. Consuming services register their concrete `DapperReadService` subclasses individually in DI.

**Tests (`SharedKernel.Persistence.Dapper.Tests`):**

All Dapper package tests require a real PostgreSQL Testcontainer:

- `DapperReadService.QueryAsync` returns correct results from parameterized query
- `DapperReadService.QuerySingleOrDefaultAsync` returns the entity when found, null when not found
- `DapperReadService.ExecuteAsync` executes a DML statement and returns affected row count
- `IDbConnectionFactory.CreateConnectionAsync` called once per operation (connection disposed after each call)
- `StronglyTypedIdTypeHandler` round-trip: write entity with strongly-typed ID column, fetch back via Dapper, verify ID deserialized correctly
- `SmartEnumTypeHandler` round-trip: write entity with SmartEnum column, fetch back via Dapper, verify enum deserialized correctly

#### Why this is needed

CQRS at scale requires a fast read path. EF Core's change-tracking and identity-map overhead makes it suboptimal for high-throughput read queries that project directly to DTOs without aggregate materialization. Dapper provides raw SQL performance with typed result mapping. Without the SharedKernel Dapper package, every service implements its own connection management, its own type handlers for strongly-typed IDs and SmartEnums (all subtly different), and its own base query service — creating a proliferation of slightly-incompatible patterns. The `DapperReadService` base enforces the parameterized-queries-only rule at the platform level, making SQL injection the exceptional case rather than the default.

#### Acceptance criteria

- [ ] `SharedKernel.Persistence.Dapper` project exists at `06.Persistence/SharedKernel.Persistence.Dapper/`
- [ ] Project references `SharedKernel.Persistence.Abstractions`, `SharedKernel.Persistence.PostgreSQL`, and `Dapper`
- [ ] `StronglyTypedIdTypeHandler<TStronglyTypedId, TValue>` abstract class — `SetValue` uses `implicit operator`, `Parse` reconstructs via factory/operator; no reflection
- [ ] `SmartEnumTypeHandler<TEnum, TValue>` abstract class — `Parse` uses `SmartEnum<TEnum, TValue>.TryFromValue`; no reflection
- [ ] `DapperTypeHandlers.Register()` is idempotent
- [ ] `DapperReadService` protected methods: `QueryAsync`, `QuerySingleOrDefaultAsync`, `ExecuteAsync` — each opens and disposes connection per call
- [ ] `AddSharedKernelDapper` DI extension calls `DapperTypeHandlers.Register()` and returns `IServiceCollection`
- [ ] Project registered in `Platform.SharedKernel.slnx` under solution folder `06.Persistence`
- [ ] Test project at `SharedKernel.Persistence.Dapper.Tests/` using Testcontainers PostgreSQL, importing helpers from `16.Testing/SharedKernel.Testing`
- [ ] All six test cases from the `What is needed` section pass
- [ ] `DapperReadService` tests verify the connection is disposed after each call (not leaked)
- [ ] `06.Persistence/CLAUDE.md` package table updated to reflect Dapper package is now implemented (not deferred)
- [ ] All public types carry XML doc comments
---

---
### P-110 — Governance: TenantedDbContext Tenant-Filter Guard Architecture Rule

**Status:** `●` Complete
**Work Order:** WO-018
**Domain:** 00.Governance
**Depends on:** P-108

#### What is needed

A new architecture enforcement rule in `00.Governance/SharedKernel.ArchitectureTests` that guards against a silent multi-tenancy misconfiguration in `TenantedDbContext` subclasses.

**The problem being enforced:**
`TenantedDbContext.OnModelCreating` calls `base.OnModelCreating(modelBuilder)` which internally calls `ApplyTenantFilters(modelBuilder)`. However, when a subclass overrides `OnModelCreating` and does NOT call `base.OnModelCreating` (a common pattern when the assembly-scan path is bypassed for performance or test isolation), the tenant filter is silently not applied. The result is that ALL tenants' data is visible to ALL queries — a critical data leak, not just a correctness issue.

The `06.Persistence/CLAUDE.md` documents that subclasses bypassing `base.OnModelCreating` MUST call `ApplyTenantFilters` explicitly. But there is no enforcement mechanism — this is a documentation-only rule that will eventually be violated.

**Rule 1 — TenantedDbContext OnModelCreating override guard:**
A Roslyn analyzer (or NetArchTest rule, at domain planner's discretion) that detects `TenantedDbContext` subclasses that override `OnModelCreating` without either (a) calling `base.OnModelCreating(modelBuilder)` or (b) calling `this.ApplyTenantFilters(modelBuilder)`. If neither call is present, the analyzer emits a warning-level diagnostic `SK0201: TenantedDbContext.OnModelCreating override must call base.OnModelCreating(modelBuilder) or ApplyTenantFilters(modelBuilder) to preserve tenant isolation`.

The rule is warning-level (not error) to avoid breaking existing code during adoption. Services must treat it as an error in their CI configuration.

**Rule 2 — No direct `IgnoreQueryFilters()` call in tenanted repository implementations:**
`TenantedRepository.GetByIdForTenantAsync` and `GetByIdForTenantIncludingDeletedAsync` are the designated cross-tenant access points. Arbitrary `IgnoreQueryFilters()` calls in service-specific repository subclasses that extend `TenantedRepository` are a data-leak risk. The analyzer should warn when `IgnoreQueryFilters()` is called in any class that is NOT `TenantedRepository<,>` (or a type in `SharedKernel.Persistence.EfCore`). Diagnostic: `SK0202: Direct IgnoreQueryFilters() call outside TenantedRepository designated methods may bypass tenant isolation`.

**Rule documentation:**

Both rules documented in `00.Governance/CLAUDE.md` with:

- The rationale (silent data leak risk)
- The exemption list (TenantedRepository designated methods, test fixtures with explicit isolation setup)
- Suggested CI configuration (treat SK0201 and SK0202 as errors)

#### Why this is needed

Multi-tenancy misconfiguration is one of the highest-severity bug categories in SaaS microservices — it results in cross-tenant data exposure, a security incident, not merely a correctness issue. The current documentation-only approach is insufficient at platform scale: with hundreds of services consuming the SharedKernel, at least some will bypass `base.OnModelCreating` (for valid performance or test-isolation reasons) and forget to call `ApplyTenantFilters`. The analyzer enforces the invariant at IDE level (red squiggle), PR review level (CI), and production-readiness level — making the misconfiguration impossible to ship unnoticed. Rule 2 closes the secondary risk: a developer who knows how to use `IgnoreQueryFilters()` can accidentally bypass tenant isolation in a service repository subclass that is not the designated cross-tenant access point.

#### Acceptance criteria

- [ ] Roslyn analyzer (or equivalent NetArchTest fixture) `SK0201` emits a diagnostic when a `TenantedDbContext` subclass overrides `OnModelCreating` without calling `base.OnModelCreating` or `ApplyTenantFilters`
- [ ] `SK0201` is warning-level; CI configuration guidance documented
- [ ] `SK0202` emits a diagnostic when `IgnoreQueryFilters()` is called in a class outside `SharedKernel.Persistence.EfCore.MultiTenancy.TenantedRepository<,>` and its designated methods
- [ ] Both rules have test fixtures in `SharedKernel.ArchitectureTests` that verify the rules fire on violating code and pass on compliant code
- [ ] Exemption list documented: test fixtures, `TenantedRepository` designated methods, `SharedKernel.Persistence.EfCore` internal types
- [ ] `00.Governance/CLAUDE.md` updated with both rules, rationale, exemption list, and CI configuration guidance
- [ ] All existing governance tests pass — no regressions
---

---
### P-111 — Persistence EfCore: Encryption Options, Service Identity Options, and AuditInterceptor Service-Name Fallback

**Status:** `●` Complete
**Work Order:** WO-019
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

Two new options classes and a targeted update to `AuditInterceptor`.

**`EncryptionOptions`** — a new options POCO bound to the `IOptions` system (section name `SharedKernel:Encryption`). It carries:
- `Enabled` (`bool`, default `false`) — master on/off switch for the entire encryption subsystem. When `false`, all encrypted properties store and return plaintext; no AES operations are performed.
- `CurrentVersion` (`string`) — the version tag for newly encrypted values (e.g., `"v1"`). Must match a key in `Keys`.
- `Keys` (`Dictionary<string, string>`) — versioned key registry. Each entry maps a version string (e.g., `"v1"`, `"v2"`) to a Base64-encoded 256-bit (32-byte) AES key. The dictionary must contain at least one entry when `Enabled == true`.

`EncryptionOptions` must include startup-time validation (via the Options validation pattern already used in `01.Core/SharedKernel.Configuration`) that, when `Enabled == true`: (a) `CurrentVersion` is non-null/non-empty, (b) `CurrentVersion` exists as a key in `Keys`, (c) every key value in `Keys` decodes to exactly 32 bytes. Validation fires at `Build()` time (eager validation), not lazily on first use, so misconfiguration is caught at startup, not in a live request.

**`PersistenceServiceOptions`** — a new options POCO bound to the `IOptions` system (section name `SharedKernel:Persistence`). It carries:
- `ServiceName` (`string`, default `"system"`) — the application identity string used as the audit fallback when no authenticated user is present. This replaces the hardcoded `"system"` literal in `AuditInterceptor.ResolveUserId()`.

`PersistenceServiceOptions` must accept any non-null, non-empty string. Startup validation: `ServiceName` must be non-null and non-empty, and must fit within the audit column's `HasMaxLength(256)` constraint.

**`AuditInterceptor` update:** Inject `IOptions<PersistenceServiceOptions>` into the constructor (or inject the options value directly as a `PersistenceServiceOptions` instance resolved by the DI container). The `ResolveUserId()` method's else-branch replaces `"system"` with `options.ServiceName`. The fallback rule from P-091 is retained: when `IsAuthenticated == true && UserId != Guid.Empty`, write `userId.ToString("D")`; otherwise write `options.ServiceName`. The CLAUDE.md audit string format rule must be updated to reflect that the fallback is now configurable via `PersistenceServiceOptions.ServiceName` (with `"system"` remaining the default).

**`EfCorePersistenceBuilder` update:** Add a `.WithEncryption(Action<EncryptionOptions>?)` fluent method that (a) registers `EncryptionOptions` via `services.AddOptions<EncryptionOptions>()...Configure(action)`, (b) registers the Options validator, (c) sets a flag so `.Build()` registers `IEncryptionRotationJob` and `EncryptionRotationService` (see P-112). Also add `.WithServiceName(string serviceName)` that registers `PersistenceServiceOptions` with the given name. Both methods are optional — omitting them leaves existing behavior unchanged.

The two options classes live inside `06.Persistence/SharedKernel.Persistence.EfCore/Options/`. No new NuGet dependencies are required — `Microsoft.Extensions.Options` is already transitively available through `Microsoft.Extensions.DependencyInjection.Abstractions`.

#### Why this is needed

The hardcoded `"system"` audit fallback is a known limitation — when hundreds of services share the same SharedKernel, audit logs cannot distinguish which service (background job, migration runner, seed script) performed a change. A configurable `ServiceName` makes audit trails actionable for multi-service incident investigation. Centralizing `EncryptionOptions` in a single place with eager startup validation prevents the class of failure where encryption is enabled but the key is malformed, or the `CurrentVersion` refers to a non-existent key — both of which would cause runtime panics during a live request rather than a clean startup error.

#### Acceptance criteria
- [ ] `EncryptionOptions` POCO exists with `Enabled`, `CurrentVersion`, and `Keys` properties
- [ ] `EncryptionOptions` startup validation fires when `Enabled == true` and any invariant is violated (missing version, wrong key length)
- [ ] `PersistenceServiceOptions` POCO exists with `ServiceName` defaulting to `"system"`
- [ ] `PersistenceServiceOptions` startup validation rejects null/empty `ServiceName` and values exceeding 256 characters
- [ ] `AuditInterceptor.ResolveUserId()` returns `options.ServiceName` instead of the literal `"system"` for the unauthenticated path
- [ ] `EfCorePersistenceBuilder.WithEncryption(...)` method exists and registers the options + validator
- [ ] `EfCorePersistenceBuilder.WithServiceName(string)` method exists and registers `PersistenceServiceOptions`
- [ ] Omitting `.WithEncryption()` and `.WithServiceName()` leaves all existing behavior unchanged — no regressions
- [ ] `06.Persistence/CLAUDE.md` audit string format rule updated to document configurable `ServiceName`
---

---
### P-112 — Persistence EfCore: Encrypted Value Converter, PropertyBuilder Annotation Extension, Model Finalization Convention, and Key Rotation Infrastructure

**Status:** `●` Complete
**Work Order:** WO-019
**Domain:** 06.Persistence
**Depends on:** P-111

#### What is needed

The core encryption capability, composed of four interlocking pieces.

**1. `EncryptedValueConverter<T>` — EF Core value converter**

A generic value converter where `T` is constrained to `string` and `byte[]` (the only EF Core property types that encryption meaningfully applies to). The converter implements `ValueConverter<T, string>` with:

- **Encryption direction (to-provider):** When `EncryptionOptions.Enabled == false`, return the value as-is (plaintext pass-through). When enabled: generate a cryptographically random 12-byte nonce using `RandomNumberGenerator`; perform AES-256-GCM encryption using the key identified by `EncryptionOptions.CurrentVersion` from the `Keys` dictionary; produce a self-describing ciphertext string in the format `v{CurrentVersion}:{Base64(nonce || ciphertext || authentication-tag)}`. The version prefix is mandatory — it is the only mechanism for key rotation transparency. The `||` is byte concatenation. The Base64 encoding uses standard Base64 (`Convert.ToBase64String`) — URL-safe variants are avoided to keep the stored value simple.

- **Decryption direction (from-provider):** When `EncryptionOptions.Enabled == false`, return the stored value as-is. When the stored value does NOT start with `v` followed by a version tag and `:`, treat it as legacy plaintext and return it unchanged (graceful migration path). When the stored value has a version prefix: parse the version tag, look up the key in `EncryptionOptions.Keys`, extract the nonce (first 12 bytes) and authentication tag (last 16 bytes) from the decoded bytes, perform AES-256-GCM decryption. If the version is not found in `Keys`, throw a descriptive `EncryptionKeyNotFoundException` (a new exception type in the same package). If the authentication tag check fails (tampered or corrupted ciphertext), the `AesGcm` runtime will throw — let it propagate naturally.

- **Options access:** The converter holds an `IOptionsMonitor<EncryptionOptions>` reference. On every conversion call it reads `.CurrentValue` — this provides hot-reload semantics without requiring a restart. `IOptionsMonitor` is safe to hold in a singleton because it is itself singleton-safe.

- The converter is registered per-property by the model convention (see piece 3), not per-type — no auto-discovery via `ModelConfigurationBuilder.ConfigureConventions` because the `.Encrypt()` annotation controls opt-in.

**2. `.Encrypt(bool? enabled = true)` extension method on `PropertyBuilder<T>`**

A static extension method on `PropertyBuilder<T>` that writes the sentinel EF Core annotation `SharedKernel:Encrypt` with the boolean value `enabled` (defaulting to `true`). This method is the ONLY way consuming services interact with the encryption system from their entity configurations.

The extension accepts a nullable bool so callers can explicitly pass `false` to opt a property out, even if a future bulk-annotation approach were added. Null is treated as `true`. The extension is purely additive — it calls `builder.HasAnnotation("SharedKernel:Encrypt", enabled ?? true)` and returns the builder for chaining.

The extension lives in `06.Persistence/SharedKernel.Persistence.EfCore/Encryption/` (or a sub-folder). It must NOT live in a namespace that downstream domain layers import — it targets `PropertyBuilder<T>` which is an EF Core type, so it is naturally scoped to entity configuration files only.

**3. `EncryptionModelConvention` — `IModelFinalizingConvention`**

An EF Core model finalizing convention that runs during `OnModelCreating` (after all `IEntityTypeConfiguration` implementations have been applied) and auto-wires `EncryptedValueConverter<string>` on every property that carries the `SharedKernel:Encrypt` annotation with value `true`.

The convention iterates `modelBuilder.Model.GetEntityTypes()`, then for each entity type iterates `entityType.GetProperties()`, checking for the `SharedKernel:Encrypt` annotation. When found and the annotation value is `true`: resolve `EncryptedValueConverter<string>` from the service provider (or construct it with the `IOptionsMonitor<EncryptionOptions>`) and call `property.SetValueConverter(converter)`. When `EncryptionOptions.Enabled == false`, the convention still wires the converter — the converter itself is the pass-through gate. This means toggling `Enabled` at runtime (via `IOptionsMonitor` hot-reload) takes effect on the next read/write without requiring a model rebuild.

The convention is registered in `SharedKernelDbContext.OnModelCreating` via `modelBuilder.Conventions.Add(_ => new EncryptionModelConvention(optionsMonitor))` — it is always present; its behavior is gated by `EncryptionOptions.Enabled` inside the converter. This registration lives in `SharedKernelDbContext` so all downstream DbContext subclasses inherit it automatically.

`SharedKernelDbContext` must receive `IOptionsMonitor<EncryptionOptions>` via its constructor to pass to the convention. Since `EncryptionOptions` may not be registered in DI (when `.WithEncryption()` is not called), the constructor should accept `IOptionsMonitor<EncryptionOptions>?` as nullable/optional, and register a no-op default internally when not provided. The default `EncryptionOptions` has `Enabled = false`, making the pass-through the default behavior.

**4. `IEncryptionRotationJob` abstraction and `EncryptionRotationService` base**

`IEncryptionRotationJob` — a new interface in the EfCore package (not in Abstractions, because it references EF Core types) with:
- `RotateAsync(string fromVersion, string toVersion, CancellationToken ct) → Task<EncryptionRotationResult>`

`EncryptionRotationResult` — a result record carrying `RowsProcessed`, `RowsRotated`, `RowsFailed`, and `Errors` (a list of per-entity error descriptions).

`EncryptionRotationService` — an abstract base class implementing `IEncryptionRotationJob`. It uses `IDbContextFactory<TContext>` to open a fresh `DbContext` per batch (avoids long-lived context memory buildup). The base provides the rotation algorithm scaffold: enumerate all entity types in the model that have encrypted properties; for each entity type, load rows in configurable batches (default 500); for each row, check whether any encrypted property's stored value starts with `v{fromVersion}:`; if so, decrypt with the `fromVersion` key and re-encrypt with the `toVersion` key; save the batch. Concrete downstream implementations supply the `TContext` type parameter and can override the batch size or pre/post-batch hooks.

The rotation service reads keys directly from `IOptionsMonitor<EncryptionOptions>` — no separate key argument is passed at construction time. The `fromVersion` and `toVersion` are passed as method arguments to `RotateAsync` so a single service instance can be invoked for multiple rotation campaigns (e.g., scheduled, triggered via a management endpoint).

`EfCorePersistenceBuilder.WithEncryption(...)` (defined in P-111) sets a flag; `.Build()` registers `IEncryptionRotationJob → EncryptionRotationService` (scoped) only when the flag is set. When `WithEncryption()` is not called, `IEncryptionRotationJob` is not registered — no dead services.

**`EncryptionKeyNotFoundException`** — a new exception type extending `SharedKernelException` (from `01.Core`). Carries the unknown version string. Thrown by the converter when a ciphertext version prefix is not found in `EncryptionOptions.Keys`.

All new types live under `06.Persistence/SharedKernel.Persistence.EfCore/Encryption/`. No new NuGet package references are needed — `System.Security.Cryptography` (`AesGcm`) is part of the BCL in .NET 10. `RandomNumberGenerator` is also BCL.

#### Why this is needed

Field-level encryption at the persistence layer is the correct location for this concern because: (a) domain entities must not carry cryptographic attributes or cipher knowledge — that violates DDD purity; (b) infrastructure-level converters are the EF Core idiom for transparent value transformation; (c) `IModelFinalizingConvention` is the correct extension point for cross-cutting model mutations, as it runs after all `IEntityTypeConfiguration` implementations and has full model visibility. AES-256-GCM is chosen over AES-CBC+HMAC because GCM provides authenticated encryption in a single primitive — there is no risk of forgetting the HMAC step or misimplementing Encrypt-then-MAC. The versioned ciphertext prefix `v{version}:` is the minimum metadata needed to support transparent key rotation without a migration table. `IOptionsMonitor` is the correct live-reload primitive for a long-lived singleton converter. The `IEncryptionRotationJob` abstraction allows downstream services to trigger rotation via any surface (Hangfire job, Temporal workflow, HTTP management endpoint) without coupling the rotation logic to any particular scheduler.

#### Acceptance criteria
- [ ] `EncryptedValueConverter<string>` encrypts with AES-256-GCM and produces a `v{version}:{Base64}` ciphertext string
- [ ] Decryption correctly resolves the key by parsing the version prefix from the stored ciphertext
- [ ] Decryption of a value without a version prefix returns the value as-is (legacy plaintext pass-through)
- [ ] `EncryptionOptions.Enabled == false` causes the converter to pass values through without any AES operation
- [ ] `EncryptionKeyNotFoundException` is thrown when the ciphertext version is not found in `EncryptionOptions.Keys`
- [ ] `.Encrypt()` extension on `PropertyBuilder<T>` writes the `SharedKernel:Encrypt` annotation with value `true`
- [ ] `.Encrypt(false)` writes the annotation with value `false`
- [ ] `EncryptionModelConvention` applies `EncryptedValueConverter<string>` to every annotated property at model finalization time
- [ ] Properties without the `SharedKernel:Encrypt` annotation are not touched by the convention
- [ ] `SharedKernelDbContext` registers `EncryptionModelConvention` in `OnModelCreating` and accepts `IOptionsMonitor<EncryptionOptions>?` as an optional constructor parameter
- [ ] When `IOptionsMonitor<EncryptionOptions>` is not registered in DI, the convention defaults to `Enabled = false` pass-through
- [ ] `IEncryptionRotationJob.RotateAsync(fromVersion, toVersion, ct)` contract is defined
- [ ] `EncryptionRotationService` base class implements the batch rotation algorithm using `IDbContextFactory<TContext>`
- [ ] `EfCorePersistenceBuilder.Build()` registers `IEncryptionRotationJob → EncryptionRotationService` only when `.WithEncryption()` was called
- [ ] No new NuGet package references added to `SharedKernel.Persistence.EfCore.csproj`
- [ ] `06.Persistence/CLAUDE.md` updated with the encryption subsystem documentation
---

---
### P-113 — Persistence EfCore: Tests — Encryption Converter, Key Rotation, Service Name Audit Fallback, and Model Convention

**Status:** `●` Complete
**Work Order:** WO-019
**Domain:** 06.Persistence
**Depends on:** P-111, P-112

#### What is needed

A comprehensive test suite added to `06.Persistence/SharedKernel.Persistence.EfCore/SharedKernel.Persistence.EfCore.Tests/`. All tests use the SQLite in-memory provider unless explicitly noted. No new Testcontainers dependency is needed — SQLite covers all EF Core convention, interceptor, and converter behavior.

**`EncryptedValueConverter<string>` unit tests:**
- Encrypt round-trip: encrypt a plaintext string → ciphertext starts with `v{CurrentVersion}:`; decrypt returns original plaintext.
- Different plaintexts produce different ciphertexts (nonce randomness — no deterministic output).
- Wrong key version in ciphertext throws `EncryptionKeyNotFoundException`.
- Tampered ciphertext (flipped bit in the authentication tag) throws `AuthenticationTagMismatchException` or equivalent `CryptographicException`.
- `Enabled == false`: encrypt returns plaintext as-is; decrypt returns stored value as-is.
- Legacy plaintext (no version prefix): decrypt returns value unchanged (graceful migration).
- `IOptionsMonitor` hot-reload: change `CurrentVersion` mid-test; subsequent encryptions use the new version; decryption of old-version ciphertext still succeeds using the old key.

**`EncryptionOptions` validation tests:**
- `Enabled == false`: no validation errors regardless of `CurrentVersion` or `Keys` state.
- `Enabled == true`, `CurrentVersion` missing from `Keys`: validation fails with descriptive message.
- `Enabled == true`, key value decodes to != 32 bytes: validation fails with descriptive message.
- `Enabled == true`, all invariants met: validation passes.

**`PersistenceServiceOptions` validation tests:**
- Null `ServiceName`: validation fails.
- Empty string `ServiceName`: validation fails.
- String > 256 characters: validation fails.
- Valid string: validation passes.

**`AuditInterceptor` service name tests:**
- `IsAuthenticated == false`, `ServiceName = "order-service"` → `CreatedBy` and `ModifiedBy` receive `"order-service"`.
- `IsAuthenticated == false`, `ServiceName = "system"` (default) → `CreatedBy` receives `"system"` (backward compat).
- `IsAuthenticated == true`, valid `UserId` → `CreatedBy` receives `userId.ToString("D")` regardless of `ServiceName`.
- All existing P-091 audit tests continue to pass — no regressions.

**`EncryptionModelConvention` integration tests (SQLite):**
- Property annotated with `.Encrypt()` → value stored in DB is ciphertext (starts with `v`); value read back is original plaintext.
- Property NOT annotated with `.Encrypt()` → value stored and read back as plaintext, no conversion.
- `.Encrypt(false)` → property treated as plaintext (annotation present but disabled).
- `EncryptionOptions.Enabled == false` → annotated property stored and read as plaintext.
- Multi-property entity: some encrypted, some not; each behaves correctly and independently.
- `SpecificationEvaluator` criteria still work on non-encrypted properties when some properties are encrypted.

**Key rotation integration tests (SQLite):**
- Insert a row with `v1` key → call `RotateAsync("v1", "v2")` → stored value now starts with `v2:`; decrypt with `v2` key returns original plaintext.
- Rotation skips rows already at `toVersion` (idempotent).
- Rotation result `RowsRotated` count is accurate.
- Rotation with an unrecognized `fromVersion` processes zero rows and returns `RowsProcessed == 0`.
- `EncryptionRotationResult` carries accurate counts when some rows are at `fromVersion` and others are not.

**EfCorePersistenceBuilder wiring tests:**
- `.WithEncryption(...)` → `IEncryptionRotationJob` resolves from the DI container.
- Without `.WithEncryption()` → `IEncryptionRotationJob` does not resolve (not registered).
- `.WithServiceName("my-service")` → `AuditInterceptor` produces `"my-service"` for unauthenticated saves.
- Without `.WithServiceName()` → fallback is `"system"` (default).

#### Why this is needed

Encryption is a security-sensitive subsystem where subtle defects (nonce reuse, missing auth-tag verification, wrong key dispatch) have severe consequences. Tests at the converter level catch cipher defects; tests at the convention level verify the EF integration; tests at the rotation level verify the migration safety of the key rotation algorithm. The backward-compat (legacy plaintext) and hot-reload scenarios are critical edge cases that must be explicitly exercised, not assumed correct.

#### Acceptance criteria
- [ ] All `EncryptedValueConverter<string>` unit tests pass including round-trip, tamper detection, pass-through, and legacy plaintext path
- [ ] Hot-reload test confirms `IOptionsMonitor` key version switch is picked up without service restart
- [ ] `EncryptionOptions` validation tests cover all three failure modes
- [ ] `PersistenceServiceOptions` validation tests cover all failure modes
- [ ] `AuditInterceptor` service-name tests pass; all existing P-091 audit tests pass without modification
- [ ] Convention integration tests confirm annotated properties are stored as ciphertext and read back as plaintext
- [ ] Convention integration tests confirm unannotated properties are untouched
- [ ] Rotation tests confirm `RotateAsync` produces correct counts and the re-encrypted rows are readable
- [ ] Builder wiring tests confirm conditional registration of `IEncryptionRotationJob`
- [ ] All 203 existing tests continue to pass — no regressions
---

---
### P-114 — Governance: Architecture Rules for DB Encryption Pattern Correctness

**Status:** `●` Complete
**Work Order:** WO-019
**Domain:** 00.Governance
**Depends on:** P-112

#### What is needed

New architecture enforcement rules added to `00.Governance/SharedKernel.ArchitectureTests` that govern correct usage of the encryption subsystem. These rules prevent the most likely misuse patterns that would compromise security or violate the SharedKernel layering contract.

**Rule SK0301 — No direct AES usage in domain or application layers:**
No type in `03.Domain` or `05.Application` (or their sub-namespaces) may reference `System.Security.Cryptography.AesGcm`, `System.Security.Cryptography.Aes`, or `System.Security.Cryptography.SymmetricAlgorithm`. Encryption belongs exclusively in `06.Persistence` (via the converter) and `12.Security` (for JWT signing operations). Diagnostic: `SK0301: Direct cryptographic cipher usage detected in Domain or Application layer. Use the persistence-layer EncryptedValueConverter via the .Encrypt() configuration extension instead.`

**Rule SK0302 — No encryption attributes on domain entity types:**
No class in `03.Domain` (or packages consumed by it) may carry a custom attribute whose name contains `Encrypt` or `Encrypted` as a suffix or prefix. The `.Encrypt()` extension operates on `PropertyBuilder<T>` in EF Core configuration — it never adds attributes to entity classes. This rule catches developers who try to re-implement attribute-based encryption (the pattern the design deliberately avoids). Diagnostic: `SK0302: Encryption attributes must not be placed on domain entity types. Use the EF Core PropertyBuilder.Encrypt() extension in IEntityTypeConfiguration instead.`

**Rule SK0303 — `IEncryptionRotationJob` must not be injected in domain or application layer types:**
`IEncryptionRotationJob` is a persistence-layer concern — triggering rotation is an infrastructure operation that belongs in a hosted service, Hangfire job, Temporal activity, or management controller. It must not be injected in a MediatR handler, domain service, or any type in `03.Domain` or `05.Application`. Diagnostic: `SK0303: IEncryptionRotationJob must not be injected in Domain or Application layer types. Register rotation as a hosted service, Hangfire job, or management endpoint.`

**Rule SK0304 — `EncryptedValueConverter` must not be instantiated directly in entity configurations:**
Downstream entity configuration classes (implementing `IEntityTypeConfiguration<T>`) must use `.Encrypt()` on `PropertyBuilder<T>` — not call `new EncryptedValueConverter<T>(...)` directly and pass it to `.HasConversion(converter)`. The model convention wires the converter automatically; bypassing it produces duplicate or inconsistent converter registration. Diagnostic: `SK0304: Do not instantiate EncryptedValueConverter<T> directly in entity configurations. Use the .Encrypt() PropertyBuilder extension — EncryptionModelConvention applies the converter automatically.`

All rules must have test fixtures in `SharedKernel.ArchitectureTests` exercising both the violation case (which must fire the diagnostic) and the compliant case (which must not). Rules SK0301 and SK0302 apply at NetArchTest level (assembly reference and attribute reflection checks). Rules SK0303 and SK0304 may be Roslyn analyzer diagnostics or NetArchTest checks, whichever is more precise for the pattern.

#### Why this is needed

With hundreds of services consuming the SharedKernel, some developers will attempt to add `[Encrypted]` attributes directly to domain entities (familiar from other ORM frameworks), or inject `EncryptedValueConverter<T>` directly into entity configurations because IntelliSense surfaces it. Others may attempt to trigger key rotation from a MediatR handler (wrong layer). Rule-as-code at the governance level makes all four patterns impossible to ship undetected — they produce IDE squiggles and CI failures. This is especially important for the encryption subsystem because the misuse patterns are either security violations (wrong-layer crypto) or correctness violations (duplicate converter registration causing double-encryption).

#### Acceptance criteria
- [ ] `SK0301` fires when `AesGcm`, `Aes`, or `SymmetricAlgorithm` is referenced in `03.Domain` or `05.Application` namespace types
- [ ] `SK0301` does not fire for types in `06.Persistence` or `12.Security`
- [ ] `SK0302` fires when a class in `03.Domain` carries an attribute whose name matches `*Encrypt*` or `*Encrypted*`
- [ ] `SK0302` does not fire for non-domain types
- [ ] `SK0303` fires when `IEncryptionRotationJob` is injected (constructor or property) in a type in `03.Domain` or `05.Application`
- [ ] `SK0303` does not fire for hosted services, Hangfire jobs, Temporal activities, or controller types
- [ ] `SK0304` fires when `EncryptedValueConverter<T>` is directly instantiated in an `IEntityTypeConfiguration<T>` implementation
- [ ] `SK0304` does not fire for `EncryptionModelConvention` itself (the legitimate internal instantiation)
- [ ] All four rules have compliant-pass and violation-fire test fixtures
- [ ] `00.Governance/CLAUDE.md` updated with the four new rules, rationale, and exemption lists
- [ ] All existing governance tests pass — no regressions
---

---
### P-115 — Messaging: Scaffold Both Packages

**Status:** `●` Complete
**Work Order:** WO-020
**Domain:** 07.Messaging
**Depends on:** None

#### What is needed
Create the complete project scaffolding for both `SharedKernel.Messaging.Abstractions` and `SharedKernel.Messaging.MassTransit`. This includes `.csproj` files with correct NuGet metadata, `net10.0` target framework, `ImplicitUsings`, `Nullable` enabled, and all required package references. The Abstractions project must reference only `Microsoft.Extensions.DependencyInjection.Abstractions` — no transport packages. The MassTransit project must reference `SharedKernel.Messaging.Abstractions`, `SharedKernel.Contracts`, `MassTransit` 8.x, `MassTransit.RabbitMQ` 8.x, `MassTransit.Azure.ServiceBus.Core` 8.x, `MassTransit.EntityFrameworkCoreIntegration` 8.x, `Microsoft.EntityFrameworkCore` 10.x, and `Microsoft.Extensions.Logging.Abstractions` 10.x.

Each package must have its nested `.Tests/` project scaffolded with the standard test package set: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `coverlet.collector`, `FluentAssertions`, `NSubstitute`, and `MassTransit.Testing` 8.x. Every test project must include a `GlobalUsings.cs` with `global using Xunit;`.

Both packages and their test projects must be registered in the `Platform.SharedKernel.slnx` solution under the `07.Messaging` solution folder. The directory structure must mirror existing domain packages: source files in the package root, test files in the nested `.Tests/` subfolder.

No implementation code is placed in this phase — only project files, solution registrations, `GlobalUsings.cs`, and placeholder folder directories.

#### Why this is needed
Scaffold is always the prerequisite phase before core implementation. It ensures the solution compiles from day one, CI can run a clean build, and domain sub-agents have a correctly-wired project to write into. Separating scaffold from core prevents the common mistake of entangling structural decisions with implementation decisions.

#### Acceptance criteria
- [ ] `SharedKernel.Messaging.Abstractions/SharedKernel.Messaging.Abstractions.csproj` exists with correct TFM, nullability, implicit usings, and DI abstractions reference only
- [ ] `SharedKernel.Messaging.MassTransit/SharedKernel.Messaging.MassTransit.csproj` exists with all required package references enumerated above
- [ ] Both packages' nested `.Tests/` projects scaffold with correct test package references and `GlobalUsings.cs`
- [ ] Both packages and test projects registered in `Platform.SharedKernel.slnx` under `07.Messaging` solution folder
- [ ] `dotnet build` on the solution succeeds with zero errors and zero warnings after scaffolding
- [ ] No production source files written — stub only
---

---
### P-116 — Messaging Abstractions: Core Interfaces — IMessageBus, IEventPublisher, PublishContext, IMessagingBuilder, MessagingOptions

**Status:** `●` Complete
**Work Order:** WO-020
**Domain:** 07.Messaging
**Depends on:** P-115

#### What is needed
Implement all production types for `SharedKernel.Messaging.Abstractions`. This package is the zero-transport contract library every microservice references for DI — it must have no transport NuGet dependencies whatsoever.

**`IMessageBus`** — scoped service interface. Must expose: `PublishAsync<T>(T message, CancellationToken ct)` for fan-out publish; `PublishAsync<T>(T message, Action<PublishContext> configure, CancellationToken ct)` for explicit envelope metadata; `SendAsync<T>(T command, CancellationToken ct)` for point-to-point queue send; `RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct)` for request/response with caution-documented temporal coupling semantics.

**`IEventPublisher`** — scoped service interface. Must expose: `PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)` for CloudEvents-compliant integration event publishing; `PublishAsync<TEvent>(TEvent integrationEvent, Action<PublishContext> configure, CancellationToken ct)` for explicit envelope override. XML docs must clearly state: for in-process domain events, use `IDomainEventDispatcher` from `03.Domain` — never `IEventPublisher`.

**`PublishContext`** — sealed mutable builder class (not a record). Must expose: `CorrelationId` (`Guid?`, null means auto-populate from `Activity.Current`); `CausationId` (`Guid?`, null means omitted); `Headers` (`IReadOnlyDictionary<string, string>`); fluent `WithCorrelationId(Guid)`, `WithCausationId(Guid)`, `WithHeader(string key, string value)` methods returning `this`. Duplicate header keys overwrite silently. Header keys must be non-null, non-empty — guard with `ArgumentException`.

**`MessagingOptions`** — sealed POCO registered as DI options under section `"SharedKernel:Messaging"`. Must expose: `ServiceName` (`string`, required) used as CloudEvents source and queue routing prefix. Must be a lowercase slug. Startup validation must throw `OptionsValidationException` on null or whitespace.

**`IMessagingBuilder`** — interface with a single `Services` property (`IServiceCollection`). Returned by `AddSharedKernelMessaging()` to allow transport-specific extension methods to chain without a circular reference.

**`AddSharedKernelMessaging` extension method** — on `IServiceCollection`. Registers `MessagingOptions` via `services.AddOptions<MessagingOptions>()...Validate(...)`. Returns a `MessagingBusBuilder` instance (defined in the MassTransit package, but the builder contract `IMessagingBuilder` is declared here). Note: the extension lives in the Abstractions package so microservices can validate options without pulling in transport packages.

All types must carry complete XML doc comments. Namespace: `SharedKernel.Messaging` or sub-namespaces per folder convention.

#### Why this is needed
The Abstractions package is the most-referenced artifact across hundreds of microservices — it must be perfectly stable, have zero transport dependencies, and carry no implementation. Building it as a standalone, complete package first ensures downstream application handlers can be written and tested with NSubstitute mocks before the MassTransit concrete wiring exists. It is also the contract that makes transport-swappability possible — if a team ever needs to replace MassTransit with a different broker client, only the MassTransit package changes, not the hundreds of handler files referencing `IMessageBus`.

#### Acceptance criteria
- [ ] `IMessageBus` defined with all four methods, all XML-documented, registered as scoped
- [ ] `IEventPublisher` defined with both publish overloads, XML docs include domain-event dispatcher distinction
- [ ] `PublishContext` sealed class with three properties and three fluent methods; `WithHeader` guards against null/empty key
- [ ] `MessagingOptions.ServiceName` required; Options validation throws `OptionsValidationException` on null/whitespace
- [ ] `IMessagingBuilder` interface with `Services` property defined
- [ ] `AddSharedKernelMessaging` extension method on `IServiceCollection` registers options and returns a builder
- [ ] No transport NuGet packages referenced — only `Microsoft.Extensions.DependencyInjection.Abstractions`
- [ ] `dotnet build` clean; zero analyzer warnings
---

---
### P-117 — Messaging MassTransit: Core Bus Wiring — MassTransitMessageBus, MassTransitEventPublisher, ConsumerBase, MessagingBusBuilder, RabbitMQ Transport

**Status:** `●` Complete
**Work Order:** WO-020
**Domain:** 07.Messaging
**Depends on:** P-116

#### What is needed
Implement the core MassTransit bus wiring in `SharedKernel.Messaging.MassTransit`.

**`ConsumerBase<TMessage>`** — abstract class implementing `MassTransit.IConsumer<TMessage>`. The abstract method `ConsumeAsync(TMessage message, CancellationToken ct)` is what downstream services override. The sealed `Consume(ConsumeContext<TMessage>)` implementation (MassTransit entry point) must: propagate `CorrelationId` from `ConsumeContext` into `Activity.Current` when no active span exists; forward `ConsumeContext.CancellationToken` to `ConsumeAsync`; catch and log unhandled exceptions at Error level with `CorrelationId` context, then rethrow. `ILogger<TConsumer>` must be available as a protected property — injected via constructor. Derived classes must never override the sealed MassTransit `Consume` method.

**`MassTransitMessageBus`** — scoped concrete class implementing `IMessageBus`. Wraps MassTransit's `IPublishEndpoint` and `ISendEndpointProvider`. `PublishAsync` delegates to `IPublishEndpoint.Publish`. `SendAsync` resolves the endpoint by convention (MassTransit `KebabCaseEndpointNameFormatter` default) and delegates to `ISendEndpointProvider.GetSendEndpoint(...).Send(...)`. `RequestAsync` uses MassTransit `IRequestClient<TRequest>` — constructs a scoped request client, sends, awaits response, returns `TResponse`; never uses `CancellationToken.None`. All three MassTransit DI types are constructor-injected.

**`MassTransitEventPublisher`** — scoped concrete class implementing `IEventPublisher`. Wraps `IPublishEndpoint` and `IOptions<MessagingOptions>`. `PublishAsync<TEvent>` wraps `TEvent` in `EventEnvelope<TEvent>` (from `04.Contracts`) with all five envelope fields populated: `CorrelationId` from `Activity.Current?.TraceId` cast to `Guid` or `Guid.NewGuid()`; `CausationId` from `PublishContext.CausationId` or `Guid.Empty`; `SourceService` from `MessagingOptions.ServiceName`; `SchemaVersion` from `DomainEventVersionHelper.GetVersion(typeof(TEvent))`; `TimestampUtc` = `DateTimeOffset.UtcNow`. The envelope is published via `IPublishEndpoint.Publish<EventEnvelope<TEvent>>`.

**`MessagingBusBuilder`** — sealed class implementing `IMessagingBuilder`. Accumulates configuration state and wires MassTransit in `Build()`. Must expose: `.UseRabbitMq(string connectionString)` and `.UseRabbitMq(Action<RabbitMqBusOptions> configure)` — both configure the MassTransit RabbitMQ host; `.Build()` method that calls `services.AddMassTransit(...)`, registers `IMessageBus → MassTransitMessageBus` (scoped), `IEventPublisher → MassTransitEventPublisher` (scoped), and `IHostedService` for bus lifecycle. Build must throw `InvalidOperationException` if no transport has been configured.

**`RabbitMqBusOptions`** — sealed options class with: `Host` (AMQP URI string), `Username` (default `"guest"`), `Password` (default `"guest"`), `VirtualHost` (default `"/"`), `Prefetch` (`ushort`, default `16`), `RequestedHeartbeat` (`TimeSpan`, default `60s`). Registered under `"SharedKernel:Messaging:RabbitMq"`.

**Consumer endpoint naming** — `MessagingBusBuilder` must configure MassTransit's `KebabCaseEndpointNameFormatter` with `MessagingOptions.ServiceName` as the prefix so queues are named `{service-name}-{consumer-type}` in kebab-case.

This phase covers RabbitMQ transport only. Azure Service Bus is addressed in P-118.

#### Why this is needed
This phase builds the beating heart of the messaging system. `ConsumerBase<TMessage>` is the contract every microservice consumer will implement — its sealed `Consume` method and correlation propagation behavior are the platform-enforced quality gates that prevent silent message loss and ensure observability across hundreds of services. The CloudEvents envelope population in `MassTransitEventPublisher` is critical for cross-service traceability — CorrelationId threading from Activity.Current means distributed traces span service boundaries automatically.

#### Acceptance criteria
- [ ] `ConsumerBase<TMessage>` abstract class with sealed `Consume` entry point, `ConsumeAsync` abstract method, CorrelationId propagation, exception logging-then-rethrow behavior, and protected `ILogger` property
- [ ] `MassTransitMessageBus` implements all four `IMessageBus` methods; never uses hardcoded queue URIs
- [ ] `MassTransitEventPublisher` wraps events in `EventEnvelope<TEvent>` with all five fields populated correctly; `CorrelationId` falls back to `Guid.NewGuid()` when no active trace; `SchemaVersion` via `DomainEventVersionHelper.GetVersion`
- [ ] `RabbitMqBusOptions` sealed class with all five properties and documented defaults
- [ ] `MessagingBusBuilder.UseRabbitMq` overloads (string and Action) both configure the MassTransit RabbitMQ host correctly
- [ ] `MessagingBusBuilder.Build()` registers `IMessageBus`, `IEventPublisher`, MassTransit bus types, and `IHostedService` bus lifecycle
- [ ] `Build()` throws `InvalidOperationException` if no transport configured
- [ ] `KebabCaseEndpointNameFormatter` wired with `MessagingOptions.ServiceName` prefix
- [ ] `dotnet build` clean; no compile errors
---

---
### P-118 — Messaging MassTransit: Azure Service Bus Transport Adapter and Options Validation

**Status:** `●` Complete
**Work Order:** WO-020
**Domain:** 07.Messaging
**Depends on:** P-117

#### What is needed
Extend `MessagingBusBuilder` with the Azure Service Bus (ASB) transport adapter.

**`AzureServiceBusOptions`** — sealed options class registered under `"SharedKernel:Messaging:AzureServiceBus"`. Must expose: `ConnectionString` (`string?`, local/dev only); `FullyQualifiedNamespace` (`string?`, K8s managed identity path, e.g. `"my-ns.servicebus.windows.net"`); `MaxConcurrentCalls` (`int`, default `1`); `TransportType` (`ServiceBusTransportType` enum, default `Amqp`). Startup validation: exactly one of `ConnectionString` or `FullyQualifiedNamespace` must be set — if both or neither are set, `InvalidOperationException` is thrown. When `FullyQualifiedNamespace` is set, the transport must authenticate via `DefaultAzureCredential` (managed identity preferred in K8s workloads).

**`MessagingBusBuilder.UseAzureServiceBus`** overloads — `.UseAzureServiceBus(string connectionString)` parses and passes to MassTransit ASB host; `.UseAzureServiceBus(Action<AzureServiceBusOptions> configure)` applies options and configures the host with the correct credential (connection string or `DefaultAzureCredential` depending on which option is set).

**Transport exclusivity guard** — calling both `.UseRabbitMq(...)` and `.UseAzureServiceBus(...)` on the same builder must throw `InvalidOperationException` at the second call. Only one transport per bus instance.

**Consumer definition support** — `MessagingBusBuilder.AddConsumer<TConsumer, TDefinition>()` must wire `IConsumerDefinition<TConsumer>` into MassTransit's consumer registration, allowing per-consumer endpoint name, prefetch count, and retry override configuration. This method is in addition to the simple `AddConsumer<TConsumer>()` from P-117.

#### Why this is needed
Many teams run RabbitMQ locally and Azure Service Bus in staging/production. The ASB adapter with managed identity via `DefaultAzureCredential` is the standard K8s-to-Azure pattern — workload identity eliminates connection strings in environment variables entirely. The transport exclusivity guard prevents a misconfigured service from silently ignoring one transport, which would cause message routing failures in production.

#### Acceptance criteria
- [ ] `AzureServiceBusOptions` sealed class with `ConnectionString?`, `FullyQualifiedNamespace?`, `MaxConcurrentCalls`, `TransportType` properties
- [ ] Startup validation throws `InvalidOperationException` when both `ConnectionString` and `FullyQualifiedNamespace` are set, or when neither is set
- [ ] When `FullyQualifiedNamespace` is set, `DefaultAzureCredential` is used — no connection string in code path
- [ ] `.UseAzureServiceBus(string)` and `.UseAzureServiceBus(Action<AzureServiceBusOptions>)` both configure MassTransit ASB host correctly
- [ ] Calling both `.UseRabbitMq` and `.UseAzureServiceBus` on the same builder throws `InvalidOperationException` at the second call
- [ ] `AddConsumer<TConsumer, TDefinition>()` wires `IConsumerDefinition<TConsumer>` into MassTransit registration
- [ ] `dotnet build` clean; `MassTransit.Azure.ServiceBus.Core` reference is present in `.csproj`
---

---
### P-119 — Messaging MassTransit: Retry Pipeline and EF Core Transactional Outbox

**Status:** `●` Complete
**Work Order:** WO-020
**Domain:** 07.Messaging
**Depends on:** P-117

#### What is needed
Implement two opt-in capabilities on `MessagingBusBuilder`: the global retry pipeline and the EF Core transactional outbox.

**`RetryOptions`** — sealed options class. Must expose: `Attempts` (`int`, default `3`, total attempts including first delivery); `InitialInterval` (`TimeSpan`, default `1s`); `IntervalIncrement` (`TimeSpan`, default `1s`); `MaxInterval` (`TimeSpan`, default `30s`); `ImmediateAttempts` (`int`, default `0`). Registered under `"SharedKernel:Messaging:Retry"`.

**`MessagingBusBuilder.WithRetry(Action<RetryOptions>? configure = null)`** — applies `UseRetry` globally to all registered consumers via MassTransit's retry configurator. Null argument uses default `RetryOptions`. The retry pipeline must use incremental back-off: delay for attempt N is `InitialInterval + (N-1) * IntervalIncrement`, capped at `MaxInterval`. `ImmediateAttempts` fast-retries fire before the interval-based attempts begin. This method is optional — omitting it results in no retry policy (MassTransit default: no retry).

**`OutboxOptions`** — sealed options class. Must expose: `BatchSize` (`int`, default `100`); `QueryDelay` (`TimeSpan`, default `1s`); `DuplicateDetectionWindow` (`TimeSpan`, default `30min`). Registered under `"SharedKernel:Messaging:Outbox"`.

**`MessagingBusBuilder.WithEntityFrameworkOutbox<TDbContext>(Action<OutboxOptions>? configure = null)`** — calls `MassTransit.EntityFrameworkCoreIntegration.AddEntityFrameworkOutbox<TDbContext>()` with the configured `OutboxOptions`. The generic constraint is `where TDbContext : Microsoft.EntityFrameworkCore.DbContext`. This package must not reference `SharedKernel.Persistence.EfCore` or any `06.Persistence.*` package — the `TDbContext` type parameter constraint uses only `Microsoft.EntityFrameworkCore` directly. The outbox delivery background service is wired by MassTransit automatically. At-least-once delivery semantics apply — all consumers registered alongside an outbox must be idempotent. The consuming service owns all EF migrations for outbox tables.

The outbox setup must include clear startup documentation (XML doc on `WithEntityFrameworkOutbox`) warning: the consuming service's `DbContext` must include outbox tables (added via `dotnet ef migrations add AddMassTransitOutbox`) before starting the service. Starting without outbox tables causes the delivery worker to throw immediately at runtime.

#### Why this is needed
These two capabilities are the primary reliability levers for the messaging system. The retry pipeline is the first line of defense against transient broker failures, network blips, and downstream service unavailability. The transactional outbox solves the dual-write problem — without it, an application handler that saves a database record and then publishes a message has a window where the DB commits but the publish fails (or vice versa), causing data inconsistency. The outbox pattern makes publish atomic with the database transaction, which is the gold standard for microservice event sourcing at scale.

The no-reference-to-06.Persistence constraint is critical: `07.Messaging` must never depend on `06.Persistence`. The generic `TDbContext` parameter achieves the transactional coupling without introducing a package dependency.

#### Acceptance criteria
- [ ] `RetryOptions` sealed class with all five properties and documented defaults
- [ ] `WithRetry()` wires MassTransit `UseRetry` with incremental back-off computed from `RetryOptions`; `ImmediateAttempts` fires before interval attempts
- [ ] Omitting `WithRetry()` results in zero retry policy (verified via `TestHarness` — first failure causes fault immediately)
- [ ] `OutboxOptions` sealed class with three properties and documented defaults
- [ ] `WithEntityFrameworkOutbox<TDbContext>()` calls MassTransit `AddEntityFrameworkOutbox<TDbContext>()` with outbox options; generic constraint is `where TDbContext : DbContext` (EfCore only, no Persistence reference)
- [ ] `SharedKernel.Messaging.MassTransit.csproj` references `MassTransit.EntityFrameworkCoreIntegration` and `Microsoft.EntityFrameworkCore` only — no `SharedKernel.Persistence.*` reference
- [ ] XML doc on `WithEntityFrameworkOutbox` warns about migration requirement
- [ ] `dotnet build` clean
---

---
### P-120 — Messaging: Full Test Suite — Unit, Integration, Outbox, CloudEvents, Retry, and Guard Tests

**Status:** `●` Complete
**Work Order:** WO-020
**Domain:** 07.Messaging
**Depends on:** P-117, P-118, P-119

#### What is needed
A comprehensive test suite covering both packages. Tests live in the nested `.Tests/` projects inside each package folder. All tests use the standard test stack: xUnit, FluentAssertions, NSubstitute, `MassTransit.Testing` 8.x.

**Abstractions package tests (`SharedKernel.Messaging.Abstractions.Tests`):**
- `PublishContext` fluent builder — verify `WithCorrelationId`, `WithCausationId`, `WithHeader` produce correct state; verify `WithHeader` with null/empty key throws `ArgumentException`; verify duplicate key overwrites
- `MessagingOptions` validation — verify `OptionsValidationException` thrown when `ServiceName` is null or whitespace
- `AddSharedKernelMessaging` — verify `IMessagingBuilder` returned; verify `MessagingOptions` resolvable from DI after registration

**MassTransit package tests — unit (`SharedKernel.Messaging.MassTransit.Tests`):**
- `IMessageBus` mock tests — NSubstitute mock; application handler calls `PublishAsync<OrderPlacedEvent>` — verify called once with correct type; `SendAsync<CreateOrderCommand>` — verify called once
- `IEventPublisher` mock tests — NSubstitute mock; verify `PublishAsync<TEvent>` called with correct integration event type
- `MessagingBusBuilder` guard tests — verify `Build()` throws `InvalidOperationException` with no transport; verify both `.UseRabbitMq` + `.UseAzureServiceBus` throws at second call; verify `AzureServiceBusOptions` with both `ConnectionString` and `FullyQualifiedNamespace` set throws `InvalidOperationException`

**MassTransit package tests — TestHarness integration:**
- `ConsumerBase<TMessage>` tests — in-memory `TestHarness`; publish a message; assert `ConsumeAsync` was called with correct message; assert NSubstitute-configured dependency was invoked; verify exception propagation (NSubstitute throws from dependency — verify `harness.Consumed` does NOT show success; fault is published)
- CloudEvents envelope tests — publish via `IEventPublisher` via `TestHarness`; intercept outgoing `EventEnvelope<TEvent>`; assert `SourceService` equals `MessagingOptions.ServiceName`; assert `CorrelationId` non-empty; assert `SchemaVersion` value (via `[DomainEventVersion]` attribute if present, else `1`); assert `TimestampUtc` is recent
- Consumer endpoint convention tests — verify queue name follows `{service-name}-{consumer-type}` kebab-case via `TestHarness.GetConsumerHarness<TConsumer>().QueueAddress`
- `RequestAsync<TRequest, TResponse>` timeout test — no responder registered; cancellation token expires; verify operation throws or cancels (does not hang)

**Retry policy tests:**
- Configure `RetryOptions.Attempts = 3`; consumer throws on first 2 calls, succeeds on 3rd; assert `ConsumeAsync` called exactly 3 times via `TestHarness.Consumed`
- Configure `ImmediateAttempts = 2`; consumer always fails; assert fault after total `Attempts` exhausted

**Outbox round-trip tests (SQLite, no Testcontainers needed):**
- Wire `WithEntityFrameworkOutbox<TestDbContext>` to an in-memory SQLite EF Core context with MassTransit outbox tables (via `modelBuilder.AddInboxStateEntity()` + `modelBuilder.AddOutboxMessageEntity()` + `modelBuilder.AddOutboxStateEntity()`)
- Publish via `IEventPublisher` inside a DB transaction scope
- Assert outbox row inserted before `SaveChangesAsync` commits
- Run outbox delivery worker (advance MassTransit `TestHarness` clock or trigger delivery manually)
- Assert message delivered to consumer after DB commit; `harness.Consumed` contains the expected event

**RabbitMQ integration tests (Testcontainers, tagged `[Trait("Category", "Integration")]`):**
- Wire `UseRabbitMq` with Testcontainers RabbitMQ container connection string from `16.Testing/SharedKernel.Testing`
- Publish via `IMessageBus.PublishAsync`; consumer receives message; assert `harness.Consumed.Select<TMessage>()` contains expected message
- End-to-end delivery verified across real broker

All tests must achieve meaningful assertion coverage — no trivial pass-through tests. Every test class must have `GlobalUsings.cs` `global using Xunit;`.

#### Why this is needed
The messaging layer is the communication backbone for hundreds of microservices. A broken `ConsumerBase`, incorrect CloudEvents envelope, or malfunctioning outbox could cause silent message loss at massive scale. The test suite is the zero-regression guarantee. SQLite for outbox unit tests eliminates Testcontainers dependency for CI speed. Testcontainers RabbitMQ tests verify actual broker behavior — serialization, acknowledgment, and reconnection behavior cannot be caught by `TestHarness` alone.

#### Acceptance criteria
- [ ] All `PublishContext` behavioral tests pass — fluent builder, guard on null/empty key, duplicate overwrite
- [ ] `MessagingOptions` validation tests pass — `OptionsValidationException` on null/whitespace `ServiceName`
- [ ] `IMessageBus` and `IEventPublisher` NSubstitute mock verification tests pass
- [ ] All `MessagingBusBuilder` guard tests pass — no-transport, dual-transport, ASB options mutual-exclusion
- [ ] `ConsumerBase<TMessage>` `TestHarness` tests pass — correct message delivery, exception propagation without swallowing
- [ ] CloudEvents envelope field tests pass — all five envelope fields verified
- [ ] Consumer endpoint naming convention test passes — kebab-case with service-name prefix
- [ ] `RequestAsync` timeout test passes — no hang on missing responder
- [ ] Retry policy tests pass — 3-attempt scenario with incremental back-off; `ImmediateAttempts` fast-retry scenario
- [ ] Outbox round-trip test passes on SQLite — row inserted before commit, delivered after commit
- [ ] RabbitMQ Testcontainers integration test passes (run locally and in CI)
- [ ] All tests tagged `[Trait("Category", "Integration")]` can be skipped in unit-only CI runs
- [ ] Zero test compilation warnings; `dotnet test` exits 0
---

---
### P-121 — Messaging: XML Documentation, Package Metadata, NuGet Pack, and Consumer Verification

**Status:** `●` Complete
**Work Order:** WO-020
**Domain:** 07.Messaging
**Depends on:** P-120

#### What is needed
Finalize both packages for NuGet publication.

**XML documentation** — every public type and public member in both packages must carry XML doc comments: `<summary>`, `<param>`, `<returns>`, `<exception>`, and `<remarks>` where applicable. Special emphasis on: `IMessageBus.RequestAsync` — must include `<remarks>` warning about temporal coupling and the requirement to pass a timeout-bound `CancellationToken`; `WithEntityFrameworkOutbox` — must include `<remarks>` warning about migration prerequisite; `ConsumerBase<TMessage>.ConsumeAsync` — must include `<remarks>` stating exceptions must not be swallowed.

**NuGet metadata** — both `.csproj` files must carry: `<PackageId>`, `<Version>` (starting at `1.0.0`), `<Authors>`, `<Description>` (one-sentence capability description), `<PackageTags>`, `<PackageLicenseExpression>` (MIT), `<GenerateDocumentationFile>true</GenerateDocumentationFile>`, `<IncludeSymbols>true</IncludeSymbols>`, `<SymbolPackageFormat>snupkg</SymbolPackageFormat>`. No `<IsAotCompatible>` tag — per root `CLAUDE.md` AOT policy.

**Pack and verify** — run `dotnet pack` for both packages; verify `.nupkg` and `.snupkg` artifacts land in `nupkgs/`; verify package manifests include all expected assemblies; verify XML documentation is embedded in the `.nupkg`. Write a consumer-verify test that instantiates `AddSharedKernelMessaging` from `SharedKernel.Messaging.Abstractions` in isolation (no MassTransit reference) and verifies the DI registration completes and `IMessagingBuilder` resolves.

**`07.Messaging/CLAUDE.md` changelog update** — append a one-line entry per implementation-phase discovery, if any corrections or additions to the domain brain were made during implementation.

#### Why this is needed
NuGet metadata and XML docs are the developer experience contract for every team consuming these packages. Incomplete XML docs mean IntelliSense shows no guidance — developers make wrong decisions at call sites. The `snupkg` symbol package enables source-level debugging across service boundaries. The consumer-verify test catches packaging errors (missing types, wrong assembly references) before the packages are promoted.

#### Acceptance criteria
- [ ] Every public type and member in both packages carries XML doc `<summary>`; complex members have `<remarks>`
- [ ] `IMessageBus.RequestAsync` XML doc includes temporal-coupling and timeout-CancellationToken warning
- [ ] `WithEntityFrameworkOutbox` XML doc includes migration prerequisite warning
- [ ] `ConsumerBase<TMessage>.ConsumeAsync` XML doc includes no-swallow exception requirement
- [ ] Both `.csproj` files carry all required NuGet metadata fields including `GenerateDocumentationFile`, `IncludeSymbols`, `SymbolPackageFormat`
- [ ] `dotnet pack` produces `.nupkg` and `.snupkg` for both packages in `nupkgs/`
- [ ] Package manifests verified — assemblies and XML docs included
- [ ] Consumer-verify test passes: `AddSharedKernelMessaging` used from `SharedKernel.Messaging.Abstractions` with no transport reference; `IMessagingBuilder` resolves
- [ ] `07.Messaging/CLAUDE.md` changelog updated
---

---
### P-122 — ServiceDefaults: Messaging Health Checks — RabbitMQ and Azure Service Bus Connection Probes

**Status:** `◐` Dispatched
**Work Order:** WO-020
**Domain:** 13.ServiceDefaults
**Depends on:** P-117, P-118

#### What is needed
Add health check registrations for both supported transports to `SharedKernel.ServiceDefaults`. The health checks must be opt-in extension methods rather than unconditionally registered — services that do not use messaging should not carry a failing health check for a broker they don't connect to.

**RabbitMQ health check** — an `AddRabbitMqMessagingHealthCheck(string amqpUri)` extension on `IHealthChecksBuilder` (or a `IServiceCollection` extension) that registers the `RabbitMQ.Client` health check (from the `AspNetCore.HealthChecks.RabbitMQ` package). The health check must be tagged `"messaging"` and `"ready"` so it participates in the readiness probe. The check must verify the AMQP connection can be established — not just that the URI parses.

**Azure Service Bus health check** — an `AddAzureServiceBusMessagingHealthCheck(string connectionStringOrNamespace)` extension that registers the ASB health check (from `AspNetCore.HealthChecks.AzureServiceBus`). The check must be tagged `"messaging"` and `"ready"`. When a fully-qualified namespace is passed (no connection string), the check must use `DefaultAzureCredential`.

**Bus readiness probe** — the health check endpoints must distinguish between liveness (the process is alive) and readiness (the bus is connected and ready to consume). A service reporting Healthy on `/health/live` but Degraded on `/health/ready` must not receive traffic until the bus connection is established. The messaging health checks must only appear on the readiness path.

**Options-based registration** — the extension must accept the transport type and connection details from `MessagingOptions` and transport options (e.g., `RabbitMqBusOptions.Host`) rather than requiring callers to duplicate connection string values. Where `MessagingOptions` is registered in DI, the health check registration must be able to resolve it.

#### Why this is needed
K8s-native services depend on accurate health probes to route traffic safely. A service whose bus connection is down should be removed from the load balancer until reconnected — this is why messaging health checks belong on the readiness path, not liveness. Without this, a pod with a broken RabbitMQ connection continues receiving HTTP traffic whose handlers may publish messages that silently fail, causing data inconsistency at scale. The `13.ServiceDefaults` layer is the correct home for health check wiring because it composes all observability concerns at the host level.

#### Acceptance criteria
- [ ] `AddRabbitMqMessagingHealthCheck` extension registered; verifies real AMQP connection; tagged `"messaging"` and `"ready"`
- [ ] `AddAzureServiceBusMessagingHealthCheck` extension registered; uses `DefaultAzureCredential` when fully-qualified namespace is passed; tagged `"messaging"` and `"ready"`
- [ ] Messaging health checks appear only on `/health/ready` endpoint, not `/health/live`
- [ ] Registration accepts values from resolved `RabbitMqBusOptions` / `AzureServiceBusOptions` from DI — no connection string duplication in calling code
- [ ] `AspNetCore.HealthChecks.RabbitMQ` and `AspNetCore.HealthChecks.AzureServiceBus` package references added to `SharedKernel.ServiceDefaults`
- [ ] `dotnet build` clean; `13.ServiceDefaults/CLAUDE.md` changelog updated
---

---
### P-123 — Governance: Messaging Architecture Rules — No Raw IBus Injection, No IMessageBus Singleton, No Domain Messaging, No Hardcoded Queue URIs

**Status:** `●` Complete
**Work Order:** WO-020
**Domain:** 00.Governance
**Depends on:** P-117

#### What is needed
Add architecture enforcement rules to `SharedKernel.ArchitectureTests` covering the four primary messaging misuse patterns that cannot be caught by the compiler.

**Rule MSG0101 — No direct `IBus` / `IPublishEndpoint` / `ISendEndpointProvider` injection outside `07.Messaging`:**
Any type outside the `SharedKernel.Messaging.*` namespace (or the `07.Messaging` folder assemblies) that has a constructor or property dependency on `MassTransit.IBus`, `MassTransit.IPublishEndpoint`, or `MassTransit.ISendEndpointProvider` violates this rule. Application handlers, domain services, command/query handlers, and controllers must inject `IMessageBus` or `IEventPublisher` exclusively. Diagnostic: `MSG0101: MassTransit transport types (IBus, IPublishEndpoint, ISendEndpointProvider) must not be injected directly. Use IMessageBus or IEventPublisher from SharedKernel.Messaging.Abstractions.`

**Rule MSG0102 — `IEventPublisher` must not be called from types in `03.Domain`:**
Any type in the domain layer (implementing `Entity`, `AggregateRoot`, `ValueObject`, `DomainService`, or residing in a `*.Domain.*` namespace) that has a dependency on `IEventPublisher` violates this rule. Domain events are dispatched internally by `IDomainEventDispatcher` — integration events flow through the application layer only. Diagnostic: `MSG0102: IEventPublisher must not be used in the domain layer. Domain events are dispatched by IDomainEventDispatcher. The correct flow: domain event → IDomainEventDispatcher → application handler → IEventPublisher.`

**Rule MSG0103 — `IMessageBus` and `IEventPublisher` must not be registered as singletons:**
Any `services.AddSingleton<IMessageBus, ...>()` or `services.AddSingleton<IEventPublisher, ...>()` call violates this rule. Both interfaces must be scoped to match MassTransit's per-consume-scope lifetime model. Singleton registration breaks MassTransit's scoping and causes subtle message correlation bugs. Diagnostic: `MSG0103: IMessageBus and IEventPublisher must be registered as scoped services, not singletons.`

**Rule MSG0104 — No hardcoded queue URI strings passed to `GetSendEndpoint`:**
Any call to `ISendEndpointProvider.GetSendEndpoint(new Uri("queue:..."))` or `GetSendEndpoint(new Uri("exchange:..."))` with a string literal URI violates this rule. Endpoint addresses must be resolved by convention through MassTransit's `IEndpointNameFormatter` — hardcoded addresses break across environments. Diagnostic: `MSG0104: Do not pass hardcoded queue or exchange URI strings to GetSendEndpoint. Use convention-based endpoint resolution via IEndpointNameFormatter.`

All rules must have compliant-pass and violation-fire test fixtures in `SharedKernel.ArchitectureTests`. Rules MSG0101 and MSG0102 are NetArchTest predicate checks (assembly type inspection). MSG0103 is a startup-time check or Roslyn analyzer. MSG0104 is a Roslyn analyzer checking for literal `Uri` construction with queue/exchange scheme prefixes.

#### Why this is needed
With hundreds of microservices consuming `SharedKernel.Messaging`, developers under deadline pressure will reach for the most IntelliSense-visible type — which is often `IBus` from MassTransit directly, bypassing the abstraction entirely. Without rule-as-code enforcement, this leaks transport-specific types into application handlers, making transport swaps impossible and breaking the abstraction boundary. The singleton lifetime violation is particularly insidious — it compiles and runs correctly in development but causes race conditions and scope pollution in production under concurrent load.

#### Acceptance criteria
- [ ] `MSG0101` fires when `IBus`, `IPublishEndpoint`, or `ISendEndpointProvider` is injected in a type outside `SharedKernel.Messaging.*`; does not fire for types within that namespace
- [ ] `MSG0102` fires when `IEventPublisher` is a constructor dependency of any domain-layer type; does not fire for application-layer types
- [ ] `MSG0103` fires when `IMessageBus` or `IEventPublisher` is registered via `AddSingleton`; does not fire for `AddScoped`
- [ ] `MSG0104` fires when a string literal `new Uri("queue:...")` or `new Uri("exchange:...")` is passed to `GetSendEndpoint`; does not fire for convention-based resolution
- [ ] All four rules have compliant-pass and violation-fire test fixtures in `SharedKernel.ArchitectureTests`
- [ ] All existing governance tests pass — no regressions
- [ ] `00.Governance/CLAUDE.md` updated with the four new rules, rationale, and exemption lists
---

---
### P-124 — Testing: Messaging Test Doubles and TestHarness Factory Helpers

**Status:** `○` Pending
**Work Order:** WO-020
**Domain:** 16.Testing
**Depends on:** P-116

#### What is needed
Add messaging test infrastructure to `SharedKernel.Testing` so downstream microservice tests can wire messaging in a controlled, deterministic way without spinning up a real broker.

**`InMemoryMessageBus`** — a concrete `IMessageBus` implementation backed by an in-memory dictionary of published and sent messages. `PublishAsync<T>` stores the message and invokes any registered handlers for `T`. `SendAsync<T>` stores the command. `RequestAsync<TRequest, TResponse>` returns a configurable `TResponse` via a per-type response factory registration. Exposes `Published<T>()` and `Sent<T>()` query methods for assertion. Thread-safe. Does not require MassTransit package reference — references only `SharedKernel.Messaging.Abstractions`.

**`InMemoryEventPublisher`** — a concrete `IEventPublisher` implementation backed by an in-memory list of published integration events. `PublishAsync<TEvent>` stores the `(TEvent, PublishContext?)` tuple. Exposes `PublishedEvents<TEvent>()` returning `IReadOnlyList<TEvent>`; `PublishedCount<TEvent>()` returning `int`. Thread-safe. No MassTransit dependency.

**`MessagingTestHarnessFactory`** — a static factory class with a `CreateInMemory(Action<MessagingBusBuilder>? configure = null)` method that constructs a MassTransit `TestHarness` with in-memory transport, wires `IMessageBus` and `IEventPublisher`, and registers provided consumers. Returns an `IAsyncDisposable` wrapper (`MessagingTestContext`) that exposes the `TestHarness`, `IMessageBus`, and `IEventPublisher` instances. References `MassTransit.Testing` — this is acceptable because `16.Testing` may reference any layer.

**`RabbitMqTestContainerFactory`** — a static factory (or helper builder) that creates a Testcontainers RabbitMQ container configured for integration tests. Exposes the resulting AMQP connection string so callers can pass it to `UseRabbitMq(connectionString)`. Wraps the Testcontainers RabbitMQ image setup — callers do not need to know the container image name or port mapping details. Implements `IAsyncDisposable` to stop and remove the container on test teardown.

**`FakePublishContext`** — a test helper that pre-builds a `PublishContext` with deterministic `CorrelationId` and `CausationId` values for asserting envelope propagation in unit tests.

#### Why this is needed
Every microservice team will write application-layer unit tests that exercise command/query handlers which call `IMessageBus` or `IEventPublisher`. Without `InMemoryMessageBus` and `InMemoryEventPublisher`, teams reach for raw NSubstitute mocks — which verify calls were made but cannot assert message content or routing semantics. The in-memory implementations provide a lightweight but behaviorally-correct bus that captures the full message for assertion, enabling stronger test contracts. The `MessagingTestHarnessFactory` lifts MassTransit's TestHarness setup out of every test project that needs it — a single well-configured factory prevents thirty teams from each writing their own harness setup with slightly different configurations.

#### Acceptance criteria
- [ ] `InMemoryMessageBus` implements `IMessageBus`; `Published<T>()` and `Sent<T>()` return correct captured messages after `PublishAsync`/`SendAsync` calls; `RequestAsync` returns registered response or throws descriptive `InvalidOperationException` when no response factory registered
- [ ] `InMemoryEventPublisher` implements `IEventPublisher`; `PublishedEvents<TEvent>()` returns correct list; thread-safe under concurrent publish
- [ ] `MessagingTestHarnessFactory.CreateInMemory()` returns a `MessagingTestContext` with working `IMessageBus`, `IEventPublisher`, and `TestHarness` instances using in-memory transport
- [ ] `MessagingTestContext` is `IAsyncDisposable`; `DisposeAsync` stops and disposes the MassTransit harness cleanly
- [ ] `RabbitMqTestContainerFactory` starts a Testcontainers RabbitMQ container; exposes AMQP connection string; `IAsyncDisposable` teardown stops container
- [ ] `FakePublishContext` produces deterministic `PublishContext` with set `CorrelationId` and `CausationId`
- [ ] `InMemoryMessageBus` and `InMemoryEventPublisher` reference only `SharedKernel.Messaging.Abstractions` — no MassTransit package reference in their implementations
- [ ] `SharedKernel.Testing` test project validates all four helpers with at least one test each
- [ ] `dotnet build` clean; `16.Testing/CLAUDE.md` changelog updated

---
### P-125 — Messaging Abstractions: Circuit Breaker and Fault Consumer Contracts

**Status:** `●` Complete
**Work Order:** WO-021
**Domain:** 07.Messaging
**Depends on:** None

#### What is needed
Extend `SharedKernel.Messaging.Abstractions` with two new contracts that address the gap between retry exhaustion and structured fault handling.

**`IFaultConsumer<TMessage>` interface** — a companion base contract for consumers that need to handle dead-lettered or faulted messages. Mirrors MassTransit's `IConsumer<Fault<TMessage>>` pattern but expressed through the SharedKernel abstraction surface so consuming services never reference MassTransit directly in their fault handlers. Must carry the original faulted message payload, the `ExceptionInfo[]` array (as a platform-defined `FaultExceptionInfo` record with `ExceptionType` string and `Message` string), the `FaultId` (Guid), and the `FaultTimestamp` (DateTimeOffset). Fault consumers are registered via `MessagingBusBuilder.AddFaultConsumer<TMessage, TConsumer>()` (defined in the MassTransit package, not here — this interface is the abstraction only). The interface must be zero-dependency (no MassTransit reference in Abstractions).

**`CircuitBreakerOptions` options class** — a sealed POCO (DI options section `"SharedKernel:Messaging:CircuitBreaker"`) exposing: `TripThreshold` (int, default 5 — consecutive failures before tripping), `ActiveThreshold` (int, default 10 — active messages before circuit-breaker evaluation), `ResetInterval` (TimeSpan, default 60s — time before circuit moves from Open to HalfOpen), `TrackingPeriod` (TimeSpan, default 60s — rolling window for failure counting). These options are consumed by `MessagingBusBuilder.WithCircuitBreaker()` in the MassTransit package.

**`FaultExceptionInfo` record** — a sealed record with `ExceptionType` (string) and `Message` (string). Lives in Abstractions so fault consumers can inspect the error without MassTransit references. Referenced by `IFaultConsumer<TMessage>`.

All new types must have full XML doc comments. Zero new NuGet dependencies in `SharedKernel.Messaging.Abstractions`.

#### Why this is needed
Retry alone is insufficient resilience — it is the *first* line of defense. When retries are exhausted, the message enters MassTransit's dead-letter mechanism. Without `IFaultConsumer<TMessage>`, services either ignore dead-lettered messages (silent data loss) or reach directly into MassTransit's `IConsumer<Fault<T>>` (leaking transport types into application code). The circuit breaker options are needed here (not just in the MassTransit package) because they are configuration data that consuming services configure via DI options — the shape of that configuration is part of the abstraction contract.

#### Acceptance criteria
- [ ] `IFaultConsumer<TMessage>` interface defined in `SharedKernel.Messaging.Abstractions` with `FaultId`, `FaultTimestamp`, `FaultedMessage`, and `Exceptions` contract members
- [ ] `FaultExceptionInfo` sealed record defined with `ExceptionType` and `Message` — no MassTransit reference
- [ ] `CircuitBreakerOptions` sealed POCO defined with four properties, defaults, and `SectionName` constant; full XML docs
- [ ] Zero new NuGet package references added to `SharedKernel.Messaging.Abstractions.csproj`
- [ ] All new types carry full XML doc comments
- [ ] Unit tests added to `SharedKernel.Messaging.Abstractions.Tests` verifying `CircuitBreakerOptions` default values and `FaultExceptionInfo` record equality
- [ ] `07.Messaging/CLAUDE.md` changelog updated
---

---
### P-126 — Messaging MassTransit: Circuit Breaker Builder Method and Fault Consumer Wiring

**Status:** `●` Complete
**Work Order:** WO-021
**Domain:** 07.Messaging
**Depends on:** P-125

#### What is needed
Extend `MessagingBusBuilder` in `SharedKernel.Messaging.MassTransit` with circuit breaker configuration and fault consumer registration.

**`MessagingBusBuilder.WithCircuitBreaker(Action<CircuitBreakerOptions>? configure = null)` method** — configures MassTransit's `UseCircuitBreaker` on the bus factory. Uses `CircuitBreakerOptions` (from P-125 in Abstractions) to populate `tripThreshold`, `activeThreshold`, `resetInterval`, and `trackingPeriod`. When both `WithRetry()` and `WithCircuitBreaker()` are called, retry is applied first (inner), circuit breaker second (outer) — this ordering is the correct layering: retry first within the current breaker state, then the breaker guards against sustained failure. If called without `WithRetry()`, only the circuit breaker applies. The method returns `MessagingBusBuilder` for fluent chaining.

**`MessagingBusBuilder.AddFaultConsumer<TMessage, TFaultConsumer>()` method** — registers a consumer for `Fault<TMessage>` by adapting `TFaultConsumer : IFaultConsumer<TMessage>` to MassTransit's `IConsumer<Fault<TMessage>>`. The adapter class (`FaultConsumerAdapter<TMessage, TFaultConsumer>`) is internal to the package and translates `ConsumeContext<Fault<TMessage>>` to a platform `IFaultConsumer<TMessage>` call. The adapter must map `Fault<TMessage>.Exceptions` to `FaultExceptionInfo[]` (from P-125). The adapter uses the same `ConsumerBase<T>` error-logging and CorrelationId-propagation pattern.

**`CLAUDE.md` documentation update** — add `WithCircuitBreaker()` and `AddFaultConsumer<TMessage, TFaultConsumer>()` to the DI registration surface documentation. Add a hard violation rule: "Do not configure circuit breakers per-consumer via `IConsumerDefinition` — always use `WithCircuitBreaker()` for global policy."

**Tests** — circuit breaker unit tests using MassTransit TestHarness: configure a consumer that always fails; assert the circuit opens after `TripThreshold` consecutive failures; assert subsequent messages are rejected with `CircuitBreakerException` without invoking the consumer. Fault consumer tests: wire `AddFaultConsumer`; publish a message that causes a fault; assert the fault consumer's method was invoked with the correct `FaultId` and `FaultedMessage`.

#### Why this is needed
Without `WithCircuitBreaker()`, a permanently-failing dependency (e.g., a crashed downstream service) causes every incoming message to exhaust its retry budget, saturating thread pools and amplifying load on already-degraded dependencies. The circuit breaker pattern is the standard mitigation — it short-circuits failing messages quickly once a failure threshold is crossed, preserving system stability until the dependency recovers. The fault consumer adapter is the structured path for handling dead-lettered messages without leaking MassTransit's `Fault<T>` type into application code.

#### Acceptance criteria
- [ ] `WithCircuitBreaker(Action<CircuitBreakerOptions>?)` method added to `MessagingBusBuilder`; applies MassTransit `UseCircuitBreaker` with correct field mappings from `CircuitBreakerOptions`
- [ ] When both `WithRetry()` and `WithCircuitBreaker()` are called, retry is applied inner (first) and circuit breaker outer (second)
- [ ] `AddFaultConsumer<TMessage, TFaultConsumer>()` registers internal `FaultConsumerAdapter<TMessage, TFaultConsumer>` for `Fault<TMessage>`
- [ ] `FaultConsumerAdapter` maps `Fault<TMessage>.Exceptions` to `FaultExceptionInfo[]` correctly
- [ ] TestHarness test: consumer trips circuit after `TripThreshold` failures; subsequent messages are short-circuited
- [ ] TestHarness test: fault consumer is invoked after message fault with correct `FaultId` and `FaultedMessage`
- [ ] `07.Messaging/CLAUDE.md` updated with new builder methods and hard violation rule
- [ ] `dotnet build` clean; `dotnet test` passes
---

---
### P-127 — Messaging MassTransit: Deferred Message Scheduling via IMessageScheduler

**Status:** `●` Complete
**Work Order:** WO-021
**Domain:** 07.Messaging
**Depends on:** P-125

#### What is needed
Add deferred message delivery capability to the messaging layer through a scheduling abstraction and MassTransit integration.

**`IMessageScheduler` interface** (in `SharedKernel.Messaging.Abstractions`) — exposes two methods:
- `ScheduleAsync<T>(T message, DateTimeOffset deliverAt, CancellationToken ct) → Task<Guid>` — schedules a message for delivery at the specified UTC time; returns a schedule token (Guid) for potential cancellation
- `CancelAsync(Guid scheduleToken, CancellationToken ct) → Task` — cancels a previously scheduled message by token; no-op if already delivered

**`SchedulingOptions` sealed POCO** (in Abstractions) — `SectionName = "SharedKernel:Messaging:Scheduling"`, property: `Provider` (enum: `InMemory`, `Quartz`, `HangfireDefault InMemory`) for selecting the scheduler backend. `UseInMemoryScheduler` (bool, default true when no Quartz/Hangfire integration is present).

**`MassTransitMessageScheduler`** (in `SharedKernel.Messaging.MassTransit`) — `internal sealed` implementation of `IMessageScheduler`. Delegates to MassTransit's `IMessageScheduler` (via `MassTransit.MessageSchedulerExtensions.GetMessageScheduler(IServiceProvider)`). The token returned by `ScheduleAsync` maps to the MassTransit `ScheduledMessage.TokenId`.

**`MessagingBusBuilder.WithInMemoryScheduler()` method** — wires MassTransit's in-memory scheduler (`cfg.UseInMemoryScheduler()`). Registers `IMessageScheduler → MassTransitMessageScheduler` as scoped. Adds a documentation note that in-memory scheduler does not survive process restarts — for production use Quartz or Hangfire integration.

**`MessagingBusBuilder.WithQuartzScheduler(Action<QuartzSchedulerOptions>? configure = null)` method** — wires MassTransit's Quartz.NET scheduler integration (`MassTransit.QuartzIntegration`). `QuartzSchedulerOptions` exposes `ConnectionString` (required) and `Schema` (default `"quartz"`). Registers `IMessageScheduler → MassTransitMessageScheduler`.

#### Why this is needed
Deferred delivery is required for sagas with time-based transitions (e.g., "if payment not received within 24 hours, cancel the order"), scheduled reminders, and retry-with-significant-delay patterns that are too long for in-process retry backoff. Without `IMessageScheduler`, services either implement ad-hoc Hangfire jobs that bypass the messaging abstraction, or misuse `Task.Delay` inside consumers (which blocks threads and cannot survive service restarts). Making `IMessageScheduler` part of the messaging abstraction ensures scheduled messages are observable, cancellable, and transport-agnostic.

#### Acceptance criteria
- [ ] `IMessageScheduler` interface added to `SharedKernel.Messaging.Abstractions` with `ScheduleAsync` and `CancelAsync` methods; zero new NuGet dependencies in Abstractions
- [ ] `SchedulingOptions` sealed POCO added to Abstractions with `SectionName` and `Provider` enum
- [ ] `MassTransitMessageScheduler` internal implementation in MassTransit package delegates to MassTransit's `IMessageScheduler`
- [ ] `WithInMemoryScheduler()` method on `MessagingBusBuilder` wires in-memory scheduler and registers `IMessageScheduler`
- [ ] `WithQuartzScheduler()` method on `MessagingBusBuilder` wires Quartz integration; `MassTransit.Quartz` NuGet reference added only if this method is called (conditional registration)
- [ ] Unit test: `WithInMemoryScheduler()` + publish scheduled message via `IMessageScheduler`; assert message consumed after `deliverAt` elapsed (TestHarness with in-memory scheduler)
- [ ] Unit test: `CancelAsync` before delivery time; assert message never consumed
- [ ] `07.Messaging/CLAUDE.md` updated with scheduler abstraction and hard violation (no direct `MassTransit.IMessageScheduler` injection)
- [ ] `dotnet build` clean
---

---
### P-128 — Messaging MassTransit: Saga State Machine Base

**Status:** `●` Complete
**Work Order:** WO-021
**Domain:** 07.Messaging
**Depends on:** P-125

#### What is needed
Add saga state machine support to `SharedKernel.Messaging.MassTransit` so services can implement durable orchestration workflows without referencing MassTransit types directly in their saga definitions.

**`SagaStateBase` abstract record** — a base record type for saga state classes (i.e., the entity that holds the saga's persisted state). Must implement MassTransit's `ISagaVersion` interface (for optimistic concurrency via `Version` int) and expose: `CorrelationId` (Guid, primary key of the saga instance), `CurrentState` (string, the current state name), `CreatedAt` (DateTimeOffset), `UpdatedAt` (DateTimeOffset). The base record must be EF Core-mappable (no EF-specific attributes on this type — EF configuration lives in Persistence). Lives in `SharedKernel.Messaging.MassTransit` because it references `ISagaVersion` from MassTransit.

**`SagaStateMachineBase<TSaga>` abstract class** — derives from MassTransit's `MassTransitStateMachine<TSaga>` where `TSaga : SagaStateBase`. Provides protected helper methods for common patterns: `TransitionTo(state)`, `Finalize()`, and event correlation helpers. Must not expose MassTransit's state machine API directly — consuming services inherit from `SagaStateMachineBase<TSaga>` and declare states, events, and transitions using the protected surface. This is a thin ergonomic wrapper, not a complete abstraction — consuming services that need advanced MassTransit state-machine features must reference MassTransit directly for those specific features.

**`MessagingBusBuilder.AddSaga<TSaga>()` method** — registers a saga by type. Saga persistence defaults to in-memory repository (for development). Production persistence is configured via `AddSaga<TSaga, TDefinition>()` (companion overload accepting an `IConsumerDefinition<TSaga>`-equivalent saga definition).

**`MessagingBusBuilder.WithEntityFrameworkSagaRepository<TDbContext, TSaga>()` method** — wires MassTransit's EF Core saga repository for `TSaga` using `TDbContext`. The `TDbContext` must include the EF Core saga state mappings. No compile-time reference to `SharedKernel.Persistence.EfCore` — the `TDbContext : DbContext` generic constraint only. Documents that consuming services must add the saga state entity to their DbContext and run EF migrations.

**Tests** — TestHarness saga test: define a minimal two-state saga (Initial → Active → Final) triggered by two events; publish both events in sequence; assert saga reaches Final state.

#### Why this is needed
Event-choreographed sagas without state machine support devolve into ad-hoc flag-checking in consumers, scattered across multiple services with no central view of the workflow state. MassTransit's saga state machines provide durable, auditable orchestration — but the raw MassTransit `MassTransitStateMachine<T>` API is complex and leaks transport types into business workflow code. `SagaStateMachineBase<TSaga>` provides a thin ergonomic wrapper that guides teams toward the correct pattern while keeping the blast radius of MassTransit API changes contained to this package.

#### Acceptance criteria
- [ ] `SagaStateBase` abstract record in MassTransit package implements `ISagaVersion`; has `CorrelationId`, `CurrentState`, `CreatedAt`, `UpdatedAt`
- [ ] `SagaStateMachineBase<TSaga>` abstract class extends `MassTransitStateMachine<TSaga>`; provides protected helpers for common patterns
- [ ] `AddSaga<TSaga>()` and `AddSaga<TSaga, TDefinition>()` on `MessagingBusBuilder`; in-memory saga repository as default
- [ ] `WithEntityFrameworkSagaRepository<TDbContext, TSaga>()` on `MessagingBusBuilder`; no `SharedKernel.Persistence.*` reference introduced
- [ ] TestHarness test: two-state saga driven by two events reaches Final state; saga state is inspectable via harness
- [ ] `07.Messaging/CLAUDE.md` updated with saga section and docs
- [ ] `dotnet build` clean
---

---
### P-129 — Messaging MassTransit: Batch Consumer Base

**Status:** `●` Complete
**Work Order:** WO-021
**Domain:** 07.Messaging
**Depends on:** None

#### What is needed
Add batch consumer support to `SharedKernel.Messaging.MassTransit` for high-throughput scenarios where processing individual messages one-by-one is inefficient.

**`BatchConsumerBase<TMessage>` abstract class** — derives from MassTransit's `IConsumer<Batch<TMessage>>` interface. The base class implements `IConsumer<Batch<TMessage>>.Consume(ConsumeContext<Batch<TMessage>>)` as the sealed entry point, forwarding to `ConsumeAsync(IReadOnlyList<TMessage> messages, CancellationToken ct)` (the abstract method consuming services override). The base class performs: (1) structured logging with batch size and batch-level CorrelationId; (2) exception log-then-rethrow on failure (same pattern as `ConsumerBase<T>`); (3) extraction of the batch's cancellation token from the last message's `ConsumeContext.CancellationToken`.

**`BatchOptions` sealed POCO** (DI options section `"SharedKernel:Messaging:Batch"`) — `MessageLimit` (int, default 10 — maximum messages per batch), `TimeLimit` (TimeSpan, default 1s — maximum wait time for a batch to fill), `ConcurrencyLimit` (int, default 1 — concurrent batch deliveries per endpoint). These options are applied to individual consumer endpoints via `MessagingBusBuilder.AddBatchConsumer<TConsumer>(Action<BatchOptions>? configure = null)`.

**`MessagingBusBuilder.AddBatchConsumer<TConsumer>(Action<BatchOptions>? configure = null)` method** — registers `TConsumer` (where `TConsumer : BatchConsumerBase<TMessage>`) with MassTransit's batch endpoint configuration. Applies `BatchOptions` via `IConsumerConfigurator.Options<BatchOptions>()` and wires the batch size and time limit via MassTransit's batch configurator. Returns `MessagingBusBuilder` for fluent chaining.

**Tests** — TestHarness batch test: publish 5 messages to a batch consumer with `MessageLimit = 5`; assert `ConsumeAsync` was called once with a list of 5 messages (not 5 times with 1 message each). Time-limit test: publish 2 messages with `MessageLimit = 10` and `TimeLimit = 100ms`; assert batch is delivered after `TimeLimit` elapses with 2 messages.

#### Why this is needed
High-throughput services (e.g., event ingestion, audit logging, analytics writes) cannot process messages one-by-one within reasonable throughput budgets. Batch consumers allow amortizing database round-trips and network calls across multiple messages in a single invocation, improving throughput by an order of magnitude. Without a `BatchConsumerBase<T>`, teams either implement `IConsumer<Batch<T>>` directly (leaking MassTransit types) or process messages individually (poor throughput). The platform providing a standard base ensures consistent logging, error handling, and configuration patterns across all batch consumers.

#### Acceptance criteria
- [ ] `BatchConsumerBase<TMessage>` abstract class in MassTransit package implements `IConsumer<Batch<TMessage>>`; provides `ConsumeAsync(IReadOnlyList<TMessage>, CancellationToken)` abstract method
- [ ] `BatchOptions` sealed POCO with `MessageLimit`, `TimeLimit`, `ConcurrencyLimit` and documented defaults
- [ ] `AddBatchConsumer<TConsumer>(Action<BatchOptions>?)` on `MessagingBusBuilder` applies batch configuration
- [ ] TestHarness test: 5 messages consumed as a single batch of 5
- [ ] TestHarness test: partial batch delivered after `TimeLimit` elapses
- [ ] Exception from `ConsumeAsync` is logged and rethrown; fault is published by MassTransit
- [ ] Full XML docs on all new types
- [ ] `dotnet build` clean; `dotnet test` passes
---

---
### P-130 — Messaging MassTransit: Fix Build() Eager ServiceProvider Anti-Pattern

**Status:** `●` Complete
**Work Order:** WO-021
**Domain:** 07.Messaging
**Depends on:** None

#### What is needed
Fix the architectural anti-pattern in `MessagingBusBuilder.Build()` where `Services.BuildServiceProvider()` is called to perform eager validation of `MessagingOptions.ServiceName`.

**Current problem:** `Build()` calls `Services.BuildServiceProvider()` to resolve `IOptions<MessagingOptions>` and validate `ServiceName`. This creates a second root `IServiceProvider` (separate from the application's real root), triggers "A second operation was started on this context" warnings in EF Core, double-registers any singleton services, and silently discards any scoped service registrations made between `AddSharedKernelMessaging()` and `Build()`. This is a well-known anti-pattern in .NET DI composition.

**Correct approach:** The validation should be performed by instantiating `MessagingOptions` directly from the captured `configure` action (stored by `AddSharedKernelMessaging`) and running `MessagingOptionsValidator.Validate()` on it inline at `Build()` time — no `BuildServiceProvider()` call required. The `MessagingBusBuilder` must capture the `Action<MessagingOptions>?` delegate passed to `AddSharedKernelMessaging()` and use it to construct a `MessagingOptions` instance for validation only. The `ServiceName` is also needed at Build time for the `KebabCaseEndpointNameFormatter` prefix — this too should come from the captured delegate, not from a built container.

**Additionally:** The `MessagingBusBuilder` should store the validated `ServiceName` in a private field after `Build()` validation succeeds, so it is available for the formatter without re-building the container. If `MessagingOptions` was configured via `services.Configure<MessagingOptions>(...)` (the configuration-section binding path rather than the inline action path), the builder must fall back to a sentinel (empty string) that defers validation to the ASP.NET startup `ValidateOnStart()` call — document this path in `CLAUDE.md`.

**Tests:** Add a test verifying that calling `Build()` after `AddSharedKernelMessaging()` does not produce a "second root service provider" diagnostic and does not cause duplicate service registration side effects.

#### Why this is needed
The current `BuildServiceProvider()` call inside `Build()` is classified as a hard anti-pattern in the Microsoft DI documentation. It causes subtle bugs: singleton services created in the early `BuildServiceProvider()` are different instances from those in the application's real container; EF Core DbContexts registered by the consuming service can be constructed twice; any `.AddSingleton()` call that captures state (like a connection pool) will create two pools. For a package consumed by hundreds of services, this time-bomb needs to be defused before it causes production incidents.

#### Acceptance criteria
- [ ] `MessagingBusBuilder.Build()` no longer calls `Services.BuildServiceProvider()`
- [ ] `ServiceName` validation at `Build()` time uses the captured `Action<MessagingOptions>?` delegate directly — instantiates `MessagingOptions`, applies the delegate, calls `MessagingOptionsValidator.Validate()` — no DI container constructed
- [ ] `KebabCaseEndpointNameFormatter` prefix is populated from the captured/validated `ServiceName`
- [ ] When `MessagingOptions` was configured via `services.Configure<T>(config.GetSection(...))` (no inline action), `Build()` does not throw — defers `ServiceName` validation to startup `ValidateOnStart()` and logs a warning that naming convention prefix cannot be validated at build time
- [ ] Test verifying `Build()` does not register or resolve services from a second root provider
- [ ] Existing `MessagingBusBuilderGuardTests` all still pass
- [ ] `07.Messaging/CLAUDE.md` updated to document the correct validation approach
---

---
### P-131 — Messaging MassTransit: ISendEndpointResolver — Cross-Service Command Routing

**Status:** `●` Complete
**Work Order:** WO-021
**Domain:** 07.Messaging
**Depends on:** P-125

#### What is needed
Fix the hardcoded service-name assumption in `MassTransitMessageBus.SendAsync<T>()` and introduce a routing abstraction for cross-service command delivery.

**Current problem:** `MassTransitMessageBus.SendAsync<T>()` builds the destination queue name as `{current-service-name}-{type-name}`. This is correct for self-addressed commands (e.g., a service queuing work for itself) but wrong for cross-service commands (e.g., `order-service` sending a `ProcessPaymentCommand` to `payment-service`). The current convention assumes the queue prefix always equals the current service's name — an assumption that breaks at scale.

**`ISendEndpointResolver` interface** (in `SharedKernel.Messaging.Abstractions`) — a single-method interface: `Resolve<T>() → string` where the return value is the full queue name (e.g., `"payment-service-process-payment"`). Defaults can be provided by a convention-based implementation. Custom resolvers are registered per command type.

**`ConventionSendEndpointResolver`** (in MassTransit package) — default implementation that uses `MessagingOptions.ServiceName` as prefix and `KebabCaseEndpointNameFormatter.SanitizeName(typeof(T).Name)` as suffix. This is the current hardcoded behavior, now made explicit and overrideable.

**`MessagingBusBuilder.WithSendEndpointRoute<T>(string queueName)` method** — registers a per-command-type queue name override. Stores in a `Dictionary<Type, string>` on the builder. Passed to `MassTransitMessageBus` via constructor injection as `IReadOnlyDictionary<Type, string>`.

**`MassTransitMessageBus.SendAsync<T>()` updated behavior** — checks the per-type route dictionary first; falls back to `ConventionSendEndpointResolver` if no route registered for `T`.

**Tests** — test that `SendAsync<T>` uses the registered route when one is configured; test that it falls back to convention when no route is registered.

#### Why this is needed
Cross-service command routing is a fundamental pattern in microservice choreography. An `order-service` must be able to send a `ProcessPaymentCommand` directly to `payment-service`'s queue. The current convention (`{current-service}-{type-name}`) only works for self-addressed queues. Without `ISendEndpointResolver`, teams either hardcode queue names (violating MSG0104), use `IMessageBus.PublishAsync` where send semantics are needed (wrong routing), or inject `ISendEndpointProvider` directly (violating MSG0101). The per-type route dictionary provides an escape hatch with full visibility into the routing configuration.

#### Acceptance criteria
- [ ] `ISendEndpointResolver` interface added to `SharedKernel.Messaging.Abstractions` with `Resolve<T>() → string`; zero new NuGet dependencies
- [ ] `ConventionSendEndpointResolver` default implementation in MassTransit package using service-name prefix + kebab-case type name
- [ ] `WithSendEndpointRoute<T>(string queueName)` on `MessagingBusBuilder`; stores per-type overrides
- [ ] `MassTransitMessageBus.SendAsync<T>()` checks per-type route first; falls back to `ConventionSendEndpointResolver`
- [ ] Unit test: `SendAsync<ProcessPaymentCommand>()` with registered route `"payment-service-process-payment"` — sends to that exact queue
- [ ] Unit test: `SendAsync<OrderCreatedCommand>()` with no registered route — uses convention-based name
- [ ] `07.Messaging/CLAUDE.md` updated with routing section and hard violation rule (no hardcoded queue URIs — use `WithSendEndpointRoute`)
---

---
### P-132 — ServiceDefaults: Messaging OpenTelemetry Wiring

**Status:** `◐` Dispatched
**Work Order:** WO-021
**Domain:** 13.ServiceDefaults
**Depends on:** P-117, P-118

#### What is needed
Add `WithMessagingTelemetry()` extension to `SharedKernel.ServiceDefaults` that wires MassTransit's OpenTelemetry instrumentation into the platform's OTel `TracerProvider` and `MeterProvider`.

**`WithMessagingTelemetry()` extension on `IOpenTelemetryBuilder`** (or a compatible `IServiceCollection` extension) — calls `builder.WithTracing(t => t.AddSource("MassTransit"))` to capture MassTransit's built-in `ActivitySource` spans for publish, send, consume, and outbox delivery. Also calls `builder.WithMetrics(m => m.AddMeter("MassTransit"))` to capture MassTransit's built-in meters (message counts, consumer durations, fault rates). This wiring must be idempotent — calling it multiple times must not register duplicate instruments.

**SharedKernel custom `ActivitySource`** — registers a static `ActivitySource("SharedKernel.Messaging", "1.0.0")` in the `SharedKernel.Messaging.MassTransit` package. The `ConsumerBase<TMessage>` `Consume()` entry point creates a child `Activity` from this source named `"Consumer.Consume"` with tag `messaging.message_type = typeof(TMessage).Name`. The `MassTransitEventPublisher.PublishAsync()` creates a child `Activity` named `"EventPublisher.Publish"` with tag `messaging.event_type = typeof(TEvent).Name`. These custom activities provide SharedKernel-namespaced traces in addition to MassTransit's own tracing. `WithMessagingTelemetry()` must also register `"SharedKernel.Messaging"` as a traced source.

**Structured log enrichment** — `ConsumerBase<TMessage>.Consume()` must enrich the log scope with `messaging.destination` (the queue name from `ConsumeContext.DestinationAddress?.AbsolutePath`) and `messaging.message_type` (the message type name). These are OpenTelemetry semantic convention keys for messaging.

**`13.ServiceDefaults/CLAUDE.md` update** — document the `WithMessagingTelemetry()` extension, its placement in the OTel setup chain, and which packages must be referenced.

#### Why this is needed
Without explicit OTel wiring, MassTransit's built-in ActivitySource spans are invisible in Jaeger, Zipkin, or Azure Monitor — they exist on the wire but are not exported. `13.ServiceDefaults` is the composition layer responsible for OTel setup across all services; every new infrastructure concern must have a corresponding OTel hook registered here. The custom SharedKernel `ActivitySource` provides a platform-namespaced trace hierarchy on top of MassTransit's own telemetry, enabling platform-wide observability dashboards that can aggregate across transport types.

#### Acceptance criteria
- [ ] `WithMessagingTelemetry()` extension method added to `SharedKernel.ServiceDefaults`; wires `"MassTransit"` and `"SharedKernel.Messaging"` as traced `ActivitySource` names
- [ ] `WithMessagingTelemetry()` wires MassTransit meter `"MassTransit"` into `MeterProvider`
- [ ] Static `ActivitySource("SharedKernel.Messaging", "1.0.0")` registered in `SharedKernel.Messaging.MassTransit`; `ConsumerBase.Consume()` creates child activity with `messaging.message_type` tag
- [ ] `MassTransitEventPublisher.PublishAsync()` creates child activity with `messaging.event_type` tag
- [ ] `ConsumerBase.Consume()` log scope enriched with `messaging.destination` and `messaging.message_type`
- [ ] `WithMessagingTelemetry()` is idempotent — calling twice does not register duplicate instruments
- [ ] Unit test: `ConsumerBase.Consume()` creates an `Activity` from the SharedKernel source with the correct message type tag
- [ ] `dotnet build` clean for both `SharedKernel.ServiceDefaults` and `SharedKernel.Messaging.MassTransit`
- [ ] `13.ServiceDefaults/CLAUDE.md` changelog updated
---

---
### P-133 — Governance: Extended Messaging Architecture Rules — Fault Consumers, Scheduling, Singleton Guards

**Status:** `●` Complete
**Work Order:** WO-021
**Domain:** 00.Governance
**Depends on:** P-125, P-126, P-127, P-128

#### What is needed
Extend `SharedKernel.ArchitectureTests` with additional messaging enforcement rules for the new capabilities introduced in P-125 through P-131.

**Rule MSG0105 — `IFaultConsumer<T>` must not be used as a scoped or singleton service registration:** Fault consumers must be registered via `AddFaultConsumer<TMessage, TFaultConsumer>()` on `MessagingBusBuilder`, not via direct `services.AddScoped<IFaultConsumer<T>>()` registration. Direct registration bypasses the MassTransit adapter that translates `Fault<T>` context to the platform abstraction. Diagnostic: `MSG0105: IFaultConsumer<T> must be registered via MessagingBusBuilder.AddFaultConsumer<TMessage,TConsumer>(), not via direct DI registration.`

**Rule MSG0106 — `MassTransit.IMessageScheduler` must not be injected directly:** Any type outside `SharedKernel.Messaging.*` that has a dependency on `MassTransit.IMessageScheduler` violates this rule. Use `SharedKernel.Messaging.Abstractions.IMessageScheduler` exclusively. Diagnostic: `MSG0106: Do not inject MassTransit.IMessageScheduler directly. Use SharedKernel.Messaging.Abstractions.IMessageScheduler.`

**Rule MSG0107 — Saga state classes must extend `SagaStateBase`:** Any class implementing MassTransit's `ISaga` interface that does not derive from `SagaStateBase` violates this rule. This ensures all saga states carry the platform's standard correlation, versioning, and auditing fields. Diagnostic: `MSG0107: Saga state classes must extend SagaStateBase from SharedKernel.Messaging.MassTransit to ensure correlation ID, version, and audit field consistency.`

**Rule MSG0108 — `BatchConsumerBase<T>` must be registered via `AddBatchConsumer`:** Any subclass of `BatchConsumerBase<T>` registered via plain `AddConsumer<T>()` violates this rule — batch consumers must be registered via `AddBatchConsumer<T>()` to apply batch-specific configuration (`MessageLimit`, `TimeLimit`). Diagnostic: `MSG0108: BatchConsumerBase<T> subclasses must be registered via MessagingBusBuilder.AddBatchConsumer<T>(), not AddConsumer<T>().`

All rules must have compliant-pass and violation-fire test fixtures. Rules MSG0106 and MSG0107 are NetArchTest predicate checks. MSG0105 and MSG0108 are either Roslyn analyzers or startup-time architecture test predicates, depending on testability with NetArchTest.

#### Why this is needed
Each new messaging capability introduced in P-125 through P-131 creates a new class of misuse. Without rule enforcement, teams will bypass `AddFaultConsumer` and register fault consumers manually (breaking the adapter chain), inject `MassTransit.IMessageScheduler` directly (re-introducing transport coupling), create saga state classes without `SagaStateBase` (missing version field, causing saga persistence issues), and register batch consumers via `AddConsumer` (ignoring batch configuration, resulting in one-by-one processing). Architecture tests encode platform decisions as executable contracts that run in CI.

#### Acceptance criteria
- [ ] `MSG0105` fires when `IFaultConsumer<T>` is registered via `services.AddScoped` or `services.AddSingleton`; does not fire for `AddFaultConsumer` registrations
- [ ] `MSG0106` fires when `MassTransit.IMessageScheduler` is a constructor dependency outside `SharedKernel.Messaging.*`; does not fire for `SharedKernel.Messaging.Abstractions.IMessageScheduler`
- [ ] `MSG0107` fires when a class implements `ISaga` but does not extend `SagaStateBase`; does not fire for classes extending `SagaStateBase`
- [ ] `MSG0108` fires when a `BatchConsumerBase<T>` subclass is registered via `AddConsumer<T>()`; does not fire for `AddBatchConsumer<T>()` registrations
- [ ] All four rules have compliant-pass and violation-fire test fixtures in `SharedKernel.ArchitectureTests`
- [ ] All existing governance tests (MSG0101–MSG0104) still pass — no regressions
- [ ] `00.Governance/CLAUDE.md` updated with the four new rules
---

---
### P-134 — Messaging: Idempotency Abstraction — IIdempotencyStore and IdempotentConsumerBehavior

**Status:** `●` Complete
**Work Order:** WO-022
**Domain:** 07.Messaging
**Depends on:** None

#### What is needed

At-least-once delivery is documented throughout the messaging domain but the platform provides no standard contract for how consumers implement idempotency. Every microservice currently re-invents its own deduplication strategy, leading to inconsistency. This phase introduces two layers:

**Abstractions layer** — an `IIdempotencyStore` interface in `SharedKernel.Messaging.Abstractions` (zero transport dependencies) with two methods: `HasProcessedAsync(Guid messageId, CancellationToken ct) → Task<bool>` and `MarkProcessedAsync(Guid messageId, CancellationToken ct) → Task`. The interface is transport-agnostic — implementors can back it with Redis, SQL, or any durable store.

**MassTransit layer** — an `IdempotentConsumerBehavior<TMessage>` internal pipeline behavior in `SharedKernel.Messaging.MassTransit`. When registered, it intercepts the `Consume` pipeline for any consumer, checks `IIdempotencyStore.HasProcessedAsync` before delegating to the consumer body, and calls `MarkProcessedAsync` after successful completion. The behavior reads the message ID from `ConsumeContext.MessageId` (populated by MassTransit from the transport message ID). If the message has already been processed, the behavior short-circuits and acknowledges the message without calling `ConsumeAsync`.

**Builder method** — `WithIdempotency()` on `MessagingBusBuilder`. When called, registers the `IdempotentConsumerBehavior<TMessage>` middleware globally. A companion overload `WithIdempotency(Action<IdempotencyOptions>)` accepts `IdempotencyOptions.ExpiryWindow` (TimeSpan, default 24h) as a hint for store implementations that use time-windowed deduplication.

**Registration rule** — `IIdempotencyStore` must be registered by the consuming service before calling `WithIdempotency()`. If `IIdempotencyStore` is not registered at startup, the behavior throws `InvalidOperationException` with a clear diagnostic message. The SharedKernel does not provide an `IIdempotencyStore` implementation — the consuming service bridges to its own persistence layer (e.g., `RedisIdempotencyStore`, `EfCoreIdempotencyStore`).

**Documentation** — `07.Messaging/CLAUDE.md` must be updated with: the `IIdempotencyStore` interface contract, `IdempotencyOptions`, the hard violation rule (never implement custom deduplication in `ConsumeAsync` body — use `WithIdempotency()`), and the "What Goes Where" guidance for providing a concrete `IIdempotencyStore` implementation.

#### Why this is needed

MassTransit guarantees at-least-once delivery. Every consumer in the platform is required to be idempotent, but the current documentation treats this as advice rather than enforcing a standard pattern. Without a platform contract, teams implement ad-hoc deduplication — some use a local `HashSet<Guid>`, some query the database on every message, some skip it entirely and accept duplicate side-effects. A standard `IIdempotencyStore` interface makes idempotency a composable platform concern: teams register a store implementation once and opt into the behavior via `WithIdempotency()`. The `IdempotentConsumerBehavior<TMessage>` handles the check-then-process-then-mark lifecycle correctly at the pipeline level, which is the only reliable location for deduplication in a message-processing pipeline.

#### Acceptance criteria
- [ ] `IIdempotencyStore` interface added to `SharedKernel.Messaging.Abstractions` with `HasProcessedAsync` and `MarkProcessedAsync` methods; zero new NuGet references in Abstractions
- [ ] `IdempotencyOptions` sealed class added to Abstractions with `ExpiryWindow` (TimeSpan, default 24h) and `SectionName`
- [ ] `IdempotentConsumerBehavior<TMessage>` internal pipeline class in MassTransit package; short-circuits on duplicate message IDs; calls `MarkProcessedAsync` only on successful consumer completion
- [ ] `WithIdempotency()` and `WithIdempotency(Action<IdempotencyOptions>)` added to `MessagingBusBuilder`; throws `InvalidOperationException` at startup if `IIdempotencyStore` is not registered
- [ ] Hard violation rule documented in `07.Messaging/CLAUDE.md`: never implement custom deduplication inside `ConsumeAsync` body
- [ ] Unit test: duplicate `MessageId` → consumer body not invoked on second delivery; `MarkProcessedAsync` called exactly once
- [ ] Unit test: novel `MessageId` → consumer body invoked normally; `HasProcessedAsync` called before `ConsumeAsync`
- [ ] `dotnet build` clean; zero new NuGet dependencies added to `SharedKernel.Messaging.Abstractions`
---

---
### P-135 — Messaging: Header Propagation — IMessageHeaderPropagator and WithHeaderPropagator

**Status:** `●` Complete
**Work Order:** WO-022
**Domain:** 07.Messaging
**Depends on:** None

#### What is needed

Cross-cutting concerns such as tenant identifiers, correlation request IDs, feature flag overrides, and A/B test cohort identifiers must propagate through message headers when a message is published — without requiring every application handler to manually populate them via `PublishContext.WithHeader(...)`. This phase introduces a standard propagator contract and the builder integration to apply propagators automatically at publish time.

**`IMessageHeaderPropagator` interface** in `SharedKernel.Messaging.Abstractions` — single method `Propagate(PublishContext context)`. Implementations read values from ambient scope (e.g., `IHttpContextAccessor`, ambient `Activity.Current.Baggage`, or `IOptions<T>`) and populate `PublishContext` headers. Zero transport NuGet dependencies in the interface definition.

**`WithHeaderPropagator<T>()` builder method** on `MessagingBusBuilder` — registers `T` as an `IMessageHeaderPropagator` implementation in DI (scoped lifetime). Multiple propagators can be registered; they are applied in registration order. At publish time, `MassTransitMessageBus.PublishAsync<T>()` and `MassTransitEventPublisher.PublishAsync<TEvent>()` resolve all registered `IEnumerable<IMessageHeaderPropagator>` from the current scope and invoke `Propagate` on each before sending to the transport.

**Explicit-override precedence rule** — when a caller supplies an explicit `Action<PublishContext>` configure callback, that callback runs after propagators, meaning explicit overrides always win over propagated values. This rule must be documented in the interface XML remarks.

**Consumer header extraction** — `ConsumerBase<TMessage>.Consume()` should extract the propagated headers from `ConsumeContext.Headers` and populate the log scope with any headers whose keys match a configurable prefix (default `"x-sk-"`). This allows tenant ID and correlation headers to flow into structured logs on the consumer side without manual extraction.

**`07.Messaging/CLAUDE.md` update** — document the `IMessageHeaderPropagator` contract, precedence rules, the consumer extraction behavior, and a "What Goes Where" row: implementing a header propagator belongs in the consuming service's composition root (referencing `07.Messaging` Abstractions), not in SharedKernel.

#### Why this is needed

Tenant isolation, distributed tracing, and feature flag propagation are recurring cross-cutting concerns in microservice platforms. The current `PublishContext` mechanism requires every call site to explicitly add headers, creating systemic omissions — a developer who forgets to propagate the tenant ID header causes subtle routing failures in multi-tenant deployments. A propagator pattern at the bus level ensures these concerns are applied uniformly at every publish/send point in the platform, reducing the surface area for human error from hundreds of call sites to one registration per service.

#### Acceptance criteria
- [ ] `IMessageHeaderPropagator` interface added to `SharedKernel.Messaging.Abstractions`; single `Propagate(PublishContext context)` method; zero new NuGet dependencies
- [ ] `WithHeaderPropagator<T>()` added to `MessagingBusBuilder`; registers `T` as scoped `IMessageHeaderPropagator` in DI; multiple registrations are additive
- [ ] `MassTransitMessageBus.PublishAsync<T>()` and `MassTransitEventPublisher.PublishAsync<TEvent>()` resolve `IEnumerable<IMessageHeaderPropagator>` and invoke all in registration order before publishing
- [ ] Explicit `Action<PublishContext>` configure callback overrides propagated values (explicit-override precedence rule)
- [ ] `ConsumerBase<TMessage>.Consume()` extracts headers matching the `"x-sk-"` prefix and adds them to the structured log scope
- [ ] Unit test: two propagators registered; both headers present in published message; explicit override wins when the same key is set
- [ ] Unit test: consumer log scope contains extracted header when `"x-sk-tenant-id"` is present in `ConsumeContext.Headers`
- [ ] `07.Messaging/CLAUDE.md` updated with propagator contract, precedence rule, and consumer extraction behavior
---

---
### P-136 — Messaging: ConsumerDefinitionBase — Platform-Standard Per-Consumer Configuration

**Status:** `●` Complete
**Work Order:** WO-022
**Domain:** 07.Messaging
**Depends on:** None

#### What is needed

MassTransit's `IConsumerDefinition<TConsumer>` allows per-consumer configuration of retry policies, endpoint names, prefetch counts, and exception filters. The current platform offers no base class for this, so microservices implementing `IConsumerDefinition<TConsumer>` must configure the retry exception filter from scratch — commonly misconfiguring it by retrying validation errors that should be dead-lettered immediately.

**`ConsumerDefinitionBase<TConsumer>`** — an abstract class in `SharedKernel.Messaging.MassTransit` that implements `IConsumerDefinition<TConsumer>`. The base provides:

- A pre-wired `IRetryConfigurator` integration that excludes a configurable set of non-retryable exception types. The base includes a protected abstract `ConfigureConsumer(...)` method for subclasses to add their own endpoint/prefetch configuration without overriding retry filter wiring.
- A protected virtual `NonRetryableExceptions` property returning `IReadOnlyList<Type>` that defaults to an empty list — subclasses override it to declare which exception types should bypass retry and go directly to dead-letter. The most common entries are `ValidationException` (from `01.Core`) and `NotFoundException`.
- A protected virtual `EndpointName` property that defaults to `null` (MassTransit convention-based naming). Subclasses override it to return a specific endpoint name string.
- A protected virtual `PrefetchCount` property that defaults to `null` (MassTransit default). Subclasses override it to return an explicit prefetch count.

The class must not introduce any new NuGet references beyond those already in `SharedKernel.Messaging.MassTransit`.

**Documentation** — `07.Messaging/CLAUDE.md` updated with `ConsumerDefinitionBase<TConsumer>` contract, the `NonRetryableExceptions` virtual hook, and guidance that microservices should prefer this base over raw `IConsumerDefinition<TConsumer>`.

**"What Goes Where" row** — implementing a per-consumer definition belongs in the consuming service; `ConsumerDefinitionBase<TConsumer>` is the base to extend; the consuming service registers via `AddConsumer<TConsumer, TDefinition>()`.

#### Why this is needed

The current pattern requires microservices to implement `IConsumerDefinition<TConsumer>` from scratch. Without a base class, teams frequently omit the retry exception filter, resulting in transient infrastructure errors and business validation errors being treated identically — both get retried 3 times before dead-lettering. The `NonRetryableExceptions` hook makes the platform-standard distinction explicit: business validation failures are not transient and should not be retried. This reduces consumer configuration from ~25 lines of boilerplate to a 3-line override of `NonRetryableExceptions`.

#### Acceptance criteria
- [ ] `ConsumerDefinitionBase<TConsumer>` abstract class in `SharedKernel.Messaging.MassTransit`; implements `IConsumerDefinition<TConsumer>`
- [ ] Protected abstract `ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator, IConsumerConfigurator<TConsumer> consumerConfigurator, IBusRegistrationContext context)` — subclasses implement this for endpoint-specific configuration
- [ ] Protected virtual `NonRetryableExceptions` returns `IReadOnlyList<Type>` defaulting to empty list; retry filter excludes listed exception types
- [ ] Protected virtual `EndpointName` (string?) and `PrefetchCount` (int?) with null defaults
- [ ] When `NonRetryableExceptions` contains `ValidationException`, retry filter is wired to skip retry on those exceptions
- [ ] Unit test: subclass returns `ValidationException` in `NonRetryableExceptions`; consumer throws `ValidationException`; assert no retry attempted; fault published immediately
- [ ] Unit test: subclass returns empty `NonRetryableExceptions`; consumer throws `IOException`; assert retry attempted per global retry policy
- [ ] `07.Messaging/CLAUDE.md` updated with `ConsumerDefinitionBase<TConsumer>` contract
- [ ] Zero new NuGet references in `SharedKernel.Messaging.MassTransit`
---

---
### P-137 — Messaging: Message Schema Evolution — IMessageVersionTranslator and WithVersionTranslator

**Status:** `●` Complete
**Work Order:** WO-022
**Domain:** 07.Messaging
**Depends on:** None

#### What is needed

As services evolve, the schema of published messages changes. A v2 producer may publish `OrderPlacedEventV2` while a consumer still processes `OrderPlacedEventV1`. The platform currently has no standard pattern for this: teams either maintain backward compatibility in a single message type (making it fragile) or force all consumers to upgrade simultaneously (coupling release cycles). This phase introduces a lightweight translation layer.

**`IMessageVersionTranslator<TOld, TNew>` interface** in `SharedKernel.Messaging.Abstractions` — single method `Translate(TOld old) → TNew`. Zero transport dependencies. The interface contract is synchronous — translation is a pure projection; it must not perform I/O or call external services.

**`WithVersionTranslator<TOld, TNew, TTranslator>()` builder method** on `MessagingBusBuilder` — registers `TTranslator` (implementing `IMessageVersionTranslator<TOld, TNew>`) as a singleton in DI. Registers a MassTransit message type alias/mapping so that when MassTransit deserializes a message of type `TOld`, it is transparently translated to `TNew` before being delivered to the consumer. Uses MassTransit's `cfg.SetMessageSerializer` / `AddTranslator` hook or the custom deserialization pipeline — the exact wiring strategy is left to the implementor, but the observable behavior must be: consumer receives `TNew`; producer publishes `TOld`; no consumer code change required.

**`TranslatorRegistrationValidator` startup check** — at `Build()` time, if `WithVersionTranslator<TOld, TNew, TTranslator>()` was called but no consumer is registered for `TNew`, log a warning (not an error) indicating the translation is registered but has no consumer endpoint. This is advisory, not a hard failure — the consumer may be in a separate service.

**Documentation** — `07.Messaging/CLAUDE.md` updated with the `IMessageVersionTranslator<TOld, TNew>` contract, usage pattern, the synchronous-only constraint, and guidance that translations should be stateless pure functions.

#### Why this is needed

Long-lived microservice deployments inevitably encounter schema drift between message versions. At scale (hundreds of services), forcing synchronized deployments for every message schema change is operationally untenable. The `IMessageVersionTranslator<TOld, TNew>` pattern enables rolling upgrades: the producer publishes the new shape while old consumers remain operational through the translation layer. This is a well-established pattern in event-sourced systems (upcasting) and in messaging systems (message transformers). Without a platform standard, teams either freeze their schemas or accumulate branching conditionals in consumer code.

#### Acceptance criteria
- [ ] `IMessageVersionTranslator<TOld, TNew>` interface added to `SharedKernel.Messaging.Abstractions`; synchronous `Translate(TOld) → TNew`; zero new NuGet dependencies
- [ ] `WithVersionTranslator<TOld, TNew, TTranslator>()` added to `MessagingBusBuilder`; registers translator and wires MassTransit deserialization so `TOld` messages are delivered as `TNew` to registered consumers
- [ ] `TranslatorRegistrationValidator` logs a warning at `Build()` time when `WithVersionTranslator` is called but no consumer for `TNew` is registered
- [ ] Unit test: publish `TOld`; consumer for `TNew` receives the translated payload with correct field values
- [ ] Unit test: translator registered with no `TNew` consumer; `Build()` logs warning; no exception thrown
- [ ] `IMessageVersionTranslator<TOld, TNew>` contract and synchronous-only constraint documented in `07.Messaging/CLAUDE.md`
- [ ] Zero new NuGet dependencies in `SharedKernel.Messaging.Abstractions`
---

---
### P-138 — Testing: Messaging Test Doubles — InMemoryMessageBus, InMemoryEventPublisher, and TestHarnessFactory

**Status:** `○` Pending
**Work Order:** WO-022
**Domain:** 16.Testing
**Depends on:** None

#### What is needed

P-124 (general messaging test helpers) covers `TestHarness` factory patterns but leaves a critical gap: application-layer unit tests (handlers, domain services) need `IMessageBus` and `IEventPublisher` test doubles that require no MassTransit harness, no broker, and no DI container setup. These are not mocks (which require per-test setup via NSubstitute) but proper test implementations that record calls and allow assertion in the test body.

**`InMemoryMessageBus`** in `SharedKernel.Testing` — implements `IMessageBus`. Records all `PublishAsync<T>` and `SendAsync<T>` calls in typed collections accessible via `Published` and `Sent` properties. `RequestAsync<TRequest, TResponse>` is configurable: callers register a response handler via `SetResponseHandler<TRequest, TResponse>(Func<TRequest, TResponse>)` before the test; if no handler is registered, it throws `InvalidOperationException` with a clear message. Implements `IMessageBus` and is registered via `services.AddSingleton<IMessageBus, InMemoryMessageBus>()` — or via a convenience `AddInMemoryMessageBus(this IServiceCollection services)` extension.

**`InMemoryEventPublisher`** in `SharedKernel.Testing` — implements `IEventPublisher`. Records all `PublishAsync<TEvent>` calls. Exposes `Published` as `IReadOnlyList<object>` and a typed accessor `PublishedOf<TEvent>()` that returns `IReadOnlyList<TEvent>`. Thread-safe (multiple async calls in the same test are supported). Registered via `services.AddSingleton<IEventPublisher, InMemoryEventPublisher>()` or a companion `AddInMemoryEventPublisher` extension.

**Assertion helpers** — extension methods on `InMemoryMessageBus` and `InMemoryEventPublisher` for test assertions: `ShouldHavePublished<T>()` (asserts at least one message of type T was published, returns the first for further assertion), `ShouldHaveSent<T>()`, `ShouldHavePublishedOnce<T>()`, `ShouldNotHavePublished<T>()`. These follow FluentAssertions conventions and return the recorded message for chaining.

**`TestHarnessFactory`** — a convenience static class that configures a `MassTransit.Testing.InMemoryTestHarness` (or `ITestHarness`) with platform defaults: `KebabCaseEndpointNameFormatter`, a configurable `ServiceName`, and pre-registered `ConsumerBase<T>` subclasses. Exposes `Create(string serviceName, Action<IBusRegistrationConfigurator>? configure = null) → ITestHarness` so integration tests don't repeat harness setup boilerplate.

**References** — `SharedKernel.Testing` references `SharedKernel.Messaging.Abstractions` (already allowed by layering rules — `16.Testing` may reference any layer). No reference to `SharedKernel.Messaging.MassTransit` in `SharedKernel.Testing.csproj` for the `InMemory*` types — they reference only Abstractions. The `TestHarnessFactory` may reference `SharedKernel.Messaging.MassTransit` as a test-only dependency since `SharedKernel.Testing` is never shipped as a production dependency.

#### Why this is needed

Application-layer unit tests — the most numerous and most frequently run tests in any microservice — should not require spinning up a MassTransit `InMemoryTestHarness`. The harness introduces meaningful startup time, background threading, and infrastructure overhead inappropriate for pure unit tests. `InMemoryMessageBus` and `InMemoryEventPublisher` fill the same role as `FakeCacheService` in the caching domain: they provide zero-infrastructure, immediately assertable test doubles that make handler unit tests fast, deterministic, and readable. Without them, teams either use NSubstitute mocks (verbose) or use the full harness (slow), and often skip publisher assertions entirely.

#### Acceptance criteria
- [ ] `InMemoryMessageBus` implements `IMessageBus`; records `PublishAsync<T>` in `Published` typed collection; records `SendAsync<T>` in `Sent` typed collection; `RequestAsync<TRequest, TResponse>` throws clearly if no handler registered
- [ ] `InMemoryEventPublisher` implements `IEventPublisher`; records `PublishAsync<TEvent>` in `Published`; exposes typed `PublishedOf<TEvent>()` accessor; thread-safe
- [ ] `AddInMemoryMessageBus()` and `AddInMemoryEventPublisher()` DI extension methods on `IServiceCollection`
- [ ] Assertion helpers: `ShouldHavePublished<T>()`, `ShouldHaveSent<T>()`, `ShouldHavePublishedOnce<T>()`, `ShouldNotHavePublished<T>()` on both test doubles; return the recorded message for chaining
- [ ] `TestHarnessFactory.Create(...)` produces a configured `ITestHarness` with `KebabCaseEndpointNameFormatter` and platform defaults
- [ ] `InMemoryMessageBus` and `InMemoryEventPublisher` reference only `SharedKernel.Messaging.Abstractions`; no `SharedKernel.Messaging.MassTransit` reference for these types
- [ ] Tests for the test doubles themselves: assert `ShouldHavePublished<T>` throws `XunitException` when nothing was published; assert `ShouldHavePublishedOnce<T>` throws when published twice
- [ ] `16.Testing/CLAUDE.md` updated with the new test doubles and assertion helpers
---

---
### P-139 — Messaging: Routing Slip Activity Base — RoutingSlipActivityBase for MassTransit Courier

**Status:** `●` Complete
**Work Order:** WO-022
**Domain:** 07.Messaging
**Depends on:** P-128

#### What is needed

MassTransit Courier (routing slips) provides a lightweight distributed transaction pattern for multi-step processes where saga state persistence is not needed. A routing slip defines a sequence of activities executed across services; compensation activities run on failure. For a platform serving hundreds of microservices, providing a `RoutingSlipActivityBase<TArguments, TLog>` base class standardizes how teams implement routing slip activities: correlation propagation, structured logging, exception handling, and compensation semantics.

**`RoutingSlipActivityBase<TArguments, TLog>`** — an abstract class in `SharedKernel.Messaging.MassTransit` extending MassTransit's `IActivity<TArguments, TLog>`. Provides:
- `ExecuteAsync(TArguments arguments, CancellationToken ct) → Task<ExecutionResult>` as the abstract execute entry point. The base `Execute(ExecuteContext<TArguments>)` sealed implementation propagates `CorrelationId` from the routing slip context into `Activity.Current`, enriches the log scope with `routing_slip.tracking_number` and `routing_slip.activity_name`, and delegates to `ExecuteAsync`.
- `CompensateAsync(TLog log, CancellationToken ct) → Task<CompensationResult>` as the abstract compensation entry point. The base `Compensate(CompensateContext<TLog>)` sealed implementation applies the same correlation and logging enrichment and delegates to `CompensateAsync`.
- A protected `Complete(TLog log)` helper that returns `context.Completed(log)` — the idiomatic way to signal successful activity completion.
- A protected `Faulted(Exception ex)` helper that returns `context.Faulted(ex)` — used to signal activity failure.
- A protected `CompensationComplete()` helper for the compensation path.
- Exception catch-then-rethrow semantics matching `ConsumerBase<T>` — unhandled exceptions from `ExecuteAsync` are logged at Error level with `routing_slip.tracking_number` then rethrown.

**`AddRoutingSlipActivity<TActivity>()` builder method** on `MessagingBusBuilder` — registers the activity with MassTransit's `AddActivity<TActivity, TArguments, TLog>()`. Returns `MessagingBusBuilder` for fluent chaining.

**`IRoutingSlipBuilder` abstraction** in `SharedKernel.Messaging.Abstractions` — wraps MassTransit's `RoutingSlip` creation so application code does not reference MassTransit types directly when building a slip. Exposes: `AddActivity(string activityName, Uri executeAddress, object arguments) → IRoutingSlipBuilder`, `Build() → object` (returns the opaque routing slip; publishing is done via `IMessageBus`). The `IMessageBus` interface gets an additional method `ExecuteRoutingSlipAsync(object routingSlip, CancellationToken ct)` that accepts the opaque routing slip object and dispatches it via MassTransit.

**Documentation** — `07.Messaging/CLAUDE.md` updated with the routing slip pattern, `RoutingSlipActivityBase<TArguments, TLog>` contract, `IRoutingSlipBuilder` usage, and the hard rule: routing slips are for stateless multi-step coordination; saga state machines are for workflows requiring persistent state.

#### Why this is needed

Sagas (`SagaStateMachineBase<TSaga>`) are designed for long-running, stateful workflows. Many distributed multi-step processes in microservice platforms are stateless once triggered — for example, a four-step payment processing chain that does not need to survive a process restart. Using a saga for these cases adds unnecessary persistence overhead (EF Core table, optimistic concurrency, state machine boilerplate). Routing slips (Courier) provide exactly-once forward execution with compensation on failure, all without persisting state. Without a platform base class, teams who discover Courier implement it ad-hoc with inconsistent correlation, logging, and error handling. `RoutingSlipActivityBase<TArguments, TLog>` brings routing slips into the platform contract, identical in ergonomics to `ConsumerBase<T>` and `SagaStateMachineBase<TSaga>`.

#### Acceptance criteria
- [ ] `RoutingSlipActivityBase<TArguments, TLog>` abstract class in `SharedKernel.Messaging.MassTransit`; implements `IActivity<TArguments, TLog>`; sealed `Execute` and `Compensate` entry points
- [ ] `ExecuteAsync(TArguments, CancellationToken)` abstract method; unhandled exceptions logged at Error then rethrown
- [ ] `CompensateAsync(TLog, CancellationToken)` abstract method; same logging semantics
- [ ] Protected helpers: `Complete(TLog)`, `Faulted(Exception)`, `CompensationComplete()`
- [ ] Log scope enriched with `routing_slip.tracking_number` and `routing_slip.activity_name` in both execute and compensate paths
- [ ] `AddRoutingSlipActivity<TActivity>()` on `MessagingBusBuilder`
- [ ] `IRoutingSlipBuilder` interface added to `SharedKernel.Messaging.Abstractions` with `AddActivity` and `Build()` methods; zero transport NuGet dependencies
- [ ] `IMessageBus` extended with `ExecuteRoutingSlipAsync(object routingSlip, CancellationToken ct)` method
- [ ] `MassTransitMessageBus` implements `ExecuteRoutingSlipAsync` by publishing the routing slip via `IPublishEndpoint`
- [ ] TestHarness test: two-activity routing slip executes in sequence; assert both `ExecuteAsync` calls received correct arguments; assert compensation fires on second activity failure
- [ ] `07.Messaging/CLAUDE.md` updated with routing slip pattern, hard rule distinguishing routing slips from sagas
- [ ] `dotnet build` clean; no new compile warnings
---

---
### P-140 — Caching: Redis Connection Core — Shared Multiplexer, Health, and Resilience Foundation

**Status:** `●` Complete
**Work Order:** WO-023
**Domain:** 02.Caching
**Depends on:** None

#### What is needed

A new foundational Redis package that owns everything currently duplicated or implicitly shared across `SharedKernel.Caching.Redis`: the singleton `IConnectionMultiplexer` registration/lifecycle, `ConnectionHealthState` tracking (reconnect/failure event wiring currently embedded in `RedisChannelService`), and the Polly v8 circuit breaker resilience primitives (`RedisL2Options.CircuitBreaker`-style options and `ResiliencePipeline` registration) currently scoped only to hash/channel services.

This package provides:
- A single DI entry point that registers `IConnectionMultiplexer` as a singleton via `TryAddSingleton` (first caller wins — preserves the existing "shared multiplexer" guarantee).
- Connection health monitoring (`ConnectionHealthState`: Connected/Reconnecting/Disconnected) wired to `ConnectionRestored`/`ConnectionFailed` events, exposed for health-check consumption by any dependent package.
- A reusable Polly v8 `ResiliencePipeline` factory/options pattern that any Redis-backed capability package can opt into, replacing the currently hash/channel-specific `RedisL2Options.CircuitBreaker`.
- Zero references to FusionCache, RedLock.net, or any specific capability — this package knows about Redis connections and resilience only.

This is the dependency root for P-141 (L2 backplane), P-142 (distributed locking), P-143 (hash store), and P-144 (pub/sub) — each of those packages depends on this one for connection access instead of registering or assuming a multiplexer independently.

#### Why this is needed

The current `SharedKernel.Caching.Redis` package bundles four distinct infrastructure roles (L2 cache backplane, distributed locking, hash storage, pub/sub) behind one assembly, all silently sharing one `IConnectionMultiplexer` via `TryAddSingleton` ordering. This works today but means a microservice that only needs `IDistributedLockService` must pull in StackExchange.Redis hash-store code, channel/pub-sub code, FusionCache backplane wiring, RedLock.net, and Polly — none of which it uses. Extracting the connection/health/resilience concerns into a standalone core package is the prerequisite for splitting the remaining three capabilities into independently-referenceable packages without each one re-registering its own multiplexer or duplicating circuit-breaker plumbing. This directly enables the plug-and-play package topology requested: a worker service can reference exactly `Redis.Core` + `Redis.DistributedLocking` and nothing else.

#### Acceptance criteria
- [ ] New package created for shared Redis connection management, depending only on `SharedKernel.Caching.Abstractions` and StackExchange.Redis
- [ ] `IConnectionMultiplexer` registration extracted from `SharedKernel.Caching.Redis` into this package via `TryAddSingleton`, preserving "first caller wins" semantics
- [ ] `ConnectionHealthState` enum and connection health tracking (currently embedded in `RedisChannelService`) extracted/generalized so any dependent package can expose connection health for health checks
- [ ] Polly v8 circuit breaker options and `ResiliencePipeline` registration pattern generalized from `RedisL2Options.CircuitBreaker` into a reusable shape consumable by hash store, channel service, and distributed locking packages
- [ ] No FusionCache, RedLock.net, or capability-specific (hash/channel/lock) types present in this package
- [ ] Existing 154 Redis tests re-partitioned or updated to validate the extracted core in isolation (connection registration, health transitions, circuit breaker pipeline construction)
- [ ] `dotnet build` clean; no new compile warnings
---

---
### P-141 — Caching: Redis L2 Cache Backplane Package Refactor

**Status:** `●` Complete
**Work Order:** WO-023
**Domain:** 02.Caching
**Depends on:** P-140

#### What is needed

Refactor the existing `SharedKernel.Caching.Redis` package so that it contains **only** the FusionCache L2 distributed backplane registration (`AddRedisL2`, `RedisL2Options`, the FusionCache Redis backplane and STJ serializer wiring, Brotli compression integration). All distributed locking, hash store, and pub/sub types identified in P-142, P-143, and P-144 are removed from this package (moved, not duplicated). This package becomes a thin FusionCache-L2-specific extension depending on `SharedKernel.Caching.Abstractions`, the new Redis connection core (P-140), and the FusionCache/Brotli packages it already uses.

The public DI surface (`AddRedisL2(...)`, `AddBrotliCompression(...)`) remains functionally equivalent from the consuming microservice's perspective — `services.AddSharedKernelCaching(...).AddRedisL2(connectionString)` continues to work — but the package now sources its `IConnectionMultiplexer` from the P-140 core registration rather than registering its own.

#### Why this is needed

This is the "keep what's genuinely caching" half of the split. `AddRedisL2` is the one capability in the current `SharedKernel.Caching.Redis` package that is unambiguously a **caching** concern — it is the L2 tier of the hybrid FusionCache abstraction promised by `02.Caching`'s domain brief. Once distributed locking, hash storage, and pub/sub are extracted to their own packages (P-142–P-144), this package's identity becomes coherent: "the Redis L2 tier for `ICacheService`." Microservices that want hybrid L1/L2 caching but no distributed locks, hashes, or pub/sub now get exactly that and nothing more.

#### Acceptance criteria
- [ ] `SharedKernel.Caching.Redis` retains only `AddRedisL2`, `RedisL2Options`, FusionCache Redis backplane wiring, and Brotli compression integration
- [ ] `IConnectionMultiplexer` is sourced from the P-140 core package registration — no duplicate registration
- [ ] Distributed locking, hash store, and pub/sub types removed from this package (relocated per P-142/P-143/P-144, not deleted from the solution)
- [ ] `services.AddSharedKernelCaching(...).AddRedisL2(connectionString)` DI usage shape is unchanged for consumers
- [ ] Brotli compression rules (magic-byte detection, threshold, ArrayPool) continue to function and are covered by existing tests
- [ ] Package reference graph confirmed: `SharedKernel.Caching.Redis` → `SharedKernel.Caching.Abstractions` + Redis connection core (P-140) + FusionCache packages — no reference to distributed locking, hash store, or pub/sub packages
- [ ] Relevant subset of the existing 154 Redis tests relocated/passing against the slimmed package
- [ ] `02.Caching/CLAUDE.md` package table updated to reflect the narrowed scope of this package
- [ ] `dotnet build` clean; no new compile warnings
---

---
### P-142 — Caching: Redis Distributed Locking Package Extraction

**Status:** `●` Complete
**Work Order:** WO-023
**Domain:** 02.Caching
**Depends on:** P-140

#### What is needed

Extract `IDistributedLockService`, `IRenewableLock`, RedLock.net wiring (`RedLockDistributedLockService`, `RedLockRenewableLock`, `KeepAliveAsync`), and `AddRedisDistributedLocking` into a new standalone package dedicated to distributed mutual-exclusion locking over Redis. This package depends on `SharedKernel.Caching.Abstractions` (for the `IDistributedLockService`/`IRenewableLock` interfaces, which remain unchanged) and the Redis connection core (P-140) for its `IConnectionMultiplexer` and connection health.

The existing `AddRedisDistributedLocking(connectionString)` fluent registration shape is preserved. A microservice that needs only distributed locks (e.g., a Hangfire job coordinator or a leader-election worker) references `Abstractions` + `Redis.Core` + this package — with zero transitive dependency on FusionCache, hash-store types, or pub/sub types.

#### Why this is needed

Distributed locking is an infrastructure coordination primitive, not a caching primitive — its presence in `SharedKernel.Caching.Redis` today is purely a historical artifact of "it also uses Redis." Bundling RedLock.net into every consumer of the Redis cache package means services that just want `AddRedisL2` carry RedLock.net as a transitive dependency for no reason, and vice versa. Splitting by role gives each capability its own versioning lifecycle and dependency footprint, consistent with the platform's `.{Capability}.{Provider}` naming convention where "Provider" can be further specialized by role when one underlying technology serves multiple distinct contracts.

#### Acceptance criteria
- [ ] New package created containing `RedLockDistributedLockService`, `RedLockRenewableLock`, `KeepAliveAsync`, `RedisLockOptions`, and `AddRedisDistributedLocking`
- [ ] Package depends on `SharedKernel.Caching.Abstractions` + Redis connection core (P-140) + RedLock.net — no reference to `SharedKernel.Caching.Redis` (L2), hash store, or pub/sub packages
- [ ] `IDistributedLockService` and `IRenewableLock` interface contracts in `SharedKernel.Caching.Abstractions` are unchanged (no breaking change to the abstraction)
- [ ] `AddRedisDistributedLocking(connectionString)` fluent registration shape preserved; `IConnectionMultiplexer` sourced from P-140 core
- [ ] Renewable lock re-acquisition behavior (RedLock.net 2.3.2 has no public `ExtendAsync`) and `KeepAliveAsync` background renewal preserved with existing test coverage
- [ ] Relevant subset of the existing 154 Redis tests (acquire/timeout/release/expiry/renewal) relocated and passing against the new package
- [ ] `02.Caching/CLAUDE.md` package table updated with the new package's role and references
- [ ] `dotnet build` clean; no new compile warnings
---

---
### P-143 — Caching: Redis Hash Store Package Extraction

**Status:** `●` Complete
**Work Order:** WO-023
**Domain:** 02.Caching
**Depends on:** P-140

#### What is needed

Extract `IRedisHashService`, `RedisHashService`, `ITypedHashStore<T>`, `TypedHashStore<T>`, `AddRedisHashService`, and `AddTypedHashStore<T>` into a new standalone package dedicated to structured Redis Hash data access. This package depends on `SharedKernel.Caching.Abstractions` (for the `IRedisHashService`/`ITypedHashStore<T>` interfaces, which remain unchanged) and the Redis connection core (P-140) for its `IConnectionMultiplexer` and circuit breaker pipeline.

The existing fluent registration shapes (`AddRedisHashService()`, `AddTypedHashStore(JsonTypeInfo<T>)`) are preserved, including the existing DI guard (`AddRedisHashService` throws if `IConnectionMultiplexer` is not registered — now satisfied by P-140's registration instead of `AddRedisL2`/`AddRedisDistributedLocking`).

#### Why this is needed

`IRedisHashService` and `ITypedHashStore<T>` are a general-purpose structured-storage primitive over Redis Hashes (sessions, configuration snapshots, counters) — conceptually closer to a lightweight key-value/document store than to "caching." Today, any service using `ITypedHashStore<OrderDto>` for session storage transitively pulls in FusionCache, RedLock.net, and pub/sub code via `SharedKernel.Caching.Redis`. Extracting this gives hash-store consumers (often BFF/session-management services) a minimal, purpose-named dependency.

#### Acceptance criteria
- [ ] New package created containing `RedisHashService`, `TypedHashStore<T>` (internal sealed), `AddRedisHashService`, and `AddTypedHashStore<T>`
- [ ] Package depends on `SharedKernel.Caching.Abstractions` + Redis connection core (P-140) — no reference to `SharedKernel.Caching.Redis` (L2), distributed locking, or pub/sub packages
- [ ] `IRedisHashService` and `ITypedHashStore<T>` interface contracts in `SharedKernel.Caching.Abstractions` are unchanged (no breaking change to the abstraction)
- [ ] `AddRedisHashService()` / `AddTypedHashStore(JsonTypeInfo<T>)` fluent registration shapes preserved; startup guard now validates against P-140's `IConnectionMultiplexer` registration
- [ ] All typed methods continue to use `JsonTypeInfo<T>` — no `typeof(T)` reflection introduced
- [ ] Optional Polly circuit breaker pipeline continues to be resolved via `sp.GetService<ResiliencePipeline>()` (optional, sourced from P-140)
- [ ] Relevant subset of the existing 154 Redis tests (set/get/get-all/delete/increment, typed store round-trip, shared multiplexer) relocated and passing against the new package
- [ ] `02.Caching/CLAUDE.md` package table updated with the new package's role and references
- [ ] `dotnet build` clean; no new compile warnings
---

---
### P-144 — Caching: Redis Pub/Sub and Cache Invalidation Package Extraction

**Status:** `●` Complete
**Work Order:** WO-023
**Domain:** 02.Caching
**Depends on:** P-140

#### What is needed

Extract `IRedisChannelService`, `RedisChannelService`, `ICacheInvalidationBus`, `RedisCacheInvalidationBus`, `CacheInvalidationReceiver`, and the `CacheInvalidationMessage` wire type into a new standalone package dedicated to ephemeral Redis Pub/Sub signaling. This package depends on `SharedKernel.Caching.Abstractions` (for the interfaces and `CacheInvalidationMessage`/`CachingCoreOptions`, unchanged) and the Redis connection core (P-140) for its `IConnectionMultiplexer`, connection health (`ConnectionHealthState`, reconnect/resubscribe replay logic currently embedded in `RedisChannelService`), and circuit breaker pipeline.

The existing fluent registration shapes (`AddRedisChannelService()`, `AddRedisCacheInvalidationBus()`, `AddCacheInvalidationReceiver()`) and channel naming conventions (`sharedkernel:cache:invalidation:{service-name}` / `:broadcast`) are preserved exactly.

This package remains within `02.Caching` — it is **not** moved to `07.Messaging`. The existing XML-doc boundary ("ephemeral, no delivery guarantees, not a substitute for `07.Messaging`") is preserved and reinforced by the package name itself.

#### Why this is needed

`IRedisChannelService` and `ICacheInvalidationBus` carry an explicit, deliberate at-most-once / no-delivery-guarantee contract that is the architectural opposite of `07.Messaging`'s durable, outbox-backed, retryable contract. Moving these types into `07.Messaging` would (a) violate the layering rule that `07.Messaging` may not reference `02.Caching` types (`CacheInvalidationMessage`, `CachingCoreOptions`), and (b) create a foreseeable trap where developers assume `07.Messaging`-housed abstractions inherit its delivery guarantees. The correct fix for "this doesn't feel like caching" is not relocation across the layering boundary — it is giving this ephemeral-pub/sub capability its own clearly-named package within `02.Caching`, so a service that wants lightweight cross-instance signaling (without cache invalidation semantics, without FusionCache, without RedLock) can take a minimal, honestly-named dependency.

#### Acceptance criteria
- [ ] New package created containing `RedisChannelService`, `RedisCacheInvalidationBus`, `CacheInvalidationReceiver`
- [ ] `IRedisChannelService`, `ICacheInvalidationBus`, `CacheInvalidationMessage`, `CacheInvalidationMessageJsonContext`, `ConnectionHealthState`, and `CachingCoreOptions` remain in `SharedKernel.Caching.Abstractions` — unchanged (no breaking change to the abstraction)
- [ ] Package depends on `SharedKernel.Caching.Abstractions` + Redis connection core (P-140) — no reference to `SharedKernel.Caching.Redis` (L2), distributed locking, or hash store packages, and no reference to any `07.Messaging` package
- [ ] `AddRedisChannelService()`, `AddRedisCacheInvalidationBus()`, `AddCacheInvalidationReceiver()` fluent shapes preserved; startup guards updated to validate against P-140's `IConnectionMultiplexer`
- [ ] Connection reconnect/resubscribe replay logic (Phase 26 `Dictionary` + lock registry, `ConnectionHealthState` transitions) relocated intact
- [ ] Channel naming convention (`sharedkernel:cache:invalidation:{service-name}` / `:broadcast`) unchanged
- [ ] XML doc boundary statement ("ephemeral, no delivery guarantees, not a substitute for 07.Messaging") preserved on `IRedisChannelService` and `ICacheInvalidationBus`
- [ ] Relevant subset of the existing 154 Redis tests (pub/sub round-trip, unsubscribe, reconnect/resubscribe, invalidation receiver: key/tag/broadcast/offline) relocated and passing against the new package
- [ ] `02.Caching/CLAUDE.md` package table updated with the new package's role and references; explicit statement that this package stays in `02.Caching` and why (linking the rationale to the `07.Messaging` durability contrast)
- [ ] `dotnet build` clean; no new compile warnings
---

---
### P-145 — Governance: Architecture Rules for Redis Package Topology

**Status:** `●` Complete
**Work Order:** WO-023
**Domain:** 00.Governance

**Depends on:** P-140, P-141, P-142, P-143, P-144

#### What is needed

New NetArchTest architecture rules (and Roslyn analyzers if appropriate, following the existing `SK0xxx` numbering convention) that enforce the post-split Redis package topology in `02.Caching`:
- The Redis connection core package (P-140) must not reference any of the four capability packages (L2/P-141, locking/P-142, hash store/P-143, pub-sub/P-144).
- Each of the four capability packages may reference the connection core (P-140) and `SharedKernel.Caching.Abstractions`, but must not reference each other (no `Redis.DistributedLocking` → `Redis.HashStore`, etc.).
- The pub/sub package (P-144) must never reference any `SharedKernel.Messaging.*` package, and no `SharedKernel.Messaging.*` package may reference any `SharedKernel.Caching.*` package — codifying the Issue 3 boundary as an enforced rule, not just documentation.
- `SharedKernel.Caching.Abstractions` continues to have zero infrastructure dependencies (existing rule, re-verified against the new package set).

#### Why this is needed

A package topology refactor of this scope is only "gold standard" if it is enforced mechanically — otherwise the careful separation of concerns established in P-140–P-144 will erode the first time a developer takes a shortcut (e.g., referencing `Redis.HashStore` from `Redis.DistributedLocking` because "it's already a dependency anyway"). The platform already has precedent for this: Phase 17's "Redis and FusionCache must never reference each other" rule, and WO-021/P-133's messaging architecture rules. This phase extends that enforcement model to the new five-package Redis topology and explicitly closes the door on `07.Messaging` ever depending on `02.Caching` (or vice versa for the pub/sub package), which is the structural guarantee underpinning the Issue 3 decision.

#### Acceptance criteria
- [ ] NetArchTest rule: Redis connection core (P-140) has no project reference to any of the four capability packages
- [ ] NetArchTest rule: the four capability packages (P-141–P-144) do not reference each other (pairwise check)
- [ ] NetArchTest rule: pub/sub package (P-144) has no reference to any `SharedKernel.Messaging.*` assembly
- [ ] NetArchTest rule: no `SharedKernel.Messaging.*` package references any `SharedKernel.Caching.*` package
- [ ] NetArchTest rule: `SharedKernel.Caching.Abstractions` has zero references beyond `Microsoft.Extensions.DependencyInjection.Abstractions` (re-verified, not newly introduced)
- [ ] All new rules follow existing `SK0xxx` numbering convention; documented in `00.Governance/CLAUDE.md`
- [ ] All new and existing architecture tests pass
- [ ] `dotnet build` clean; no new compile warnings
---

---
### P-146 — Testing: Update Shared Test Doubles and References for Redis Package Split

**Status:** `○` Pending
**Work Order:** WO-023
**Domain:** 16.Testing

**Depends on:** P-141, P-142, P-143, P-144

#### What is needed

Update `SharedKernel.Testing` and any affected `.Tests` project references to align with the new five-package Redis topology from P-140–P-144. This includes:
- Verifying `FakeCacheService`, `FakeDistributedLockService`/`FakeRenewableLock`, `FakeCacheInvalidationBus`, and any hash-store fakes still satisfy their respective `SharedKernel.Caching.Abstractions` interfaces (these are interface-based fakes, so the abstraction being unchanged means minimal churn — but the package's own project references and any Testcontainers-based integration test base classes that referenced the old monolithic `SharedKernel.Caching.Redis` package must be updated to reference the correct new package(s) for what they're actually testing).
- Updating Testcontainers Redis fixture setup (if `SharedKernel.Testing` provides a shared Redis container fixture) to be usable independently by each of the four new capability test projects without requiring all four capabilities to be wired up simultaneously.

#### Why this is needed

`16.Testing` is referenced by every `.Tests` project across the platform and is explicitly exempted from the layering rules ("may reference any layer — test infrastructure only, never shipped"), but that does not mean it is exempt from staying correct. A package split of this scope, if left unaddressed in `16.Testing`, would leave shared fakes and container fixtures pointing at a package (`SharedKernel.Caching.Redis`) that no longer contains the types they were built to test, breaking test compilation across four newly-split test projects simultaneously. This phase ensures the test infrastructure split lands in lockstep with the production package split.

#### Acceptance criteria
- [ ] `FakeCacheService`, `FakeDistributedLockService`, `FakeRenewableLock`, `FakeCacheInvalidationBus`, and any hash-store fakes in `SharedKernel.Testing` confirmed to depend only on `SharedKernel.Caching.Abstractions` (no change needed if already true; corrected if not)
- [ ] Shared Testcontainers Redis fixture (if present) usable independently by each of the four new capability `.Tests` projects (P-141–P-144)
- [ ] All four new capability `.Tests` projects build and reference `SharedKernel.Testing` correctly
- [ ] No `.Tests` project references the now-removed monolithic `SharedKernel.Caching.Redis` surface for types it does not test
- [ ] `dotnet build` clean across all affected test projects; no new compile warnings
---

---
### P-147 — Persistence: Fix Encryption Key Rotation Reflection Violation and Global-Version Coupling

**Status:** `●` Complete
**Work Order:** WO-024
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

Two defects in the WO-019 encryption subsystem must be corrected before this code is trusted for production rotation:

1. **Reflection hard-violation in `EncryptionRotationService.LoadBatchAsync`.** The current implementation resolves `DbContext.Set<T>()` via `GetMethods().First(...)`, `MakeGenericMethod`, and `Invoke` — the exact pattern this domain's own hard-rule list forbids for `TenantedDbContext`, justified there only by being model-build-time/startup code. Key rotation is a long-running batch operation over potentially millions of rows and is not startup code. Replace with a non-reflective access pattern (e.g., a generic helper method invoked once per discovered entity type via a typed dispatch, or `EF.Constant`/expression-based `Set<T>()` resolution) that keeps `LoadBatchAsync` reflection-free in its hot loop.

2. **Re-encryption depends on a global mutable `EncryptionOptions.CurrentVersion`.** Today, `RotateAsync` marks properties `IsModified = true` and relies on `EncryptedValueConverter` re-encrypting with whatever `EncryptionOptions.CurrentVersion` happens to be at save time — with an explicit code comment instructing operators to set `CurrentVersion == toVersion` globally *before* calling `RotateAsync`. This means every unrelated write across the entire running service silently re-encrypts with `toVersion` for the full duration of the rotation job, and `IOptionsMonitor` hot-reload makes the blast radius worse. Redesign so the rotation job can target `toVersion` for the rows it rewrites without mutating the steady-state `CurrentVersion` used by concurrent application writes — for example, by introducing an explicit per-operation encryption-version override (an `IEncryptionKeyResolver`-style seam, or a rotation-scoped converter instance) that `EncryptionRotationService` uses directly, decoupled from `EncryptedValueConverter`'s steady-state `CurrentVersion` read.

3. **Documentation drift correction.** `CLAUDE.md` and multiple XML doc `<see cref>` references describe `EncryptedValueConverter<T>` (a generic type). The shipped type is non-generic (`EncryptedValueConverter : ValueConverter<string, string>`), which is the correct scope (string-only field encryption). Correct all doc references — in `06.Persistence/CLAUDE.md` and in-code XML docs — to the non-generic name, or formally promote the type to generic if a future `T : IConvertible` scope is desired (decide one direction and make code and docs agree).

#### Why this is needed

This domain enforces some of the strictest "no reflection in hot paths" and "expression trees only" rules in the entire SharedKernel specifically because it sets the pattern hundreds of downstream services copy. Shipping a reflection-based `MakeGenericMethod`/`Invoke` pattern in the same package that documents this as a hard violation elsewhere undermines the credibility of every other hard rule in `06.Persistence/CLAUDE.md`. The global `CurrentVersion` coupling is worse than a style issue — it is a latent production incident: any team that runs `RotateAsync` in a live multi-instance deployment will, for the duration of the job, change the encryption key used by every other write path in the fleet (if `IOptionsMonitor` propagates the config change across instances) or only the rotation-job's own instance (if it doesn't) — either way, an undocumented and surprising side effect for an "idempotent, safe" operation as currently described.

#### Acceptance criteria
- [ ] `EncryptionRotationService.LoadBatchAsync` (and any other rotation code path) contains zero `GetMethod`/`MakeGenericMethod`/`Invoke` calls
- [ ] `RotateAsync(fromVersion, toVersion, ct)` re-encrypts targeted rows with `toVersion` without requiring or causing a change to `EncryptionOptions.CurrentVersion` observed by concurrent, non-rotation writes
- [ ] A concurrent-write test proves: while `RotateAsync(v1, v2)` is in progress, a normal entity write through the same or a different DbContext instance continues to encrypt with the steady-state `CurrentVersion` (unchanged by the rotation job)
- [ ] `EncryptedValueConverter` documentation (CLAUDE.md + XML docs) matches the actual non-generic type — no `<T>`/`{T}` references to a generic that does not exist
- [ ] Existing rotation idempotency tests (rows already at `toVersion` skipped) continue to pass
- [ ] `dotnet build` clean; no new compile warnings; all existing `06.Persistence` tests green
---

---
### P-148 — Persistence: Set-Based Bulk Mutations via ExecuteUpdate/ExecuteDelete

**Status:** `●` Complete
**Work Order:** WO-024
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

A specification-driven, set-based bulk mutation capability on the write-side repository contract, backed by EF Core's `ExecuteUpdateAsync`/`ExecuteDeleteAsync`. This is distinct from the existing `UpdateRangeAsync`/`DeleteRangeAsync` (which load N tracked entities and rely on change-tracking/`SoftDeleteInterceptor`): the new capability issues a single set-based SQL statement against rows matching an `ISpecification<T>`'s criteria, without materializing entities.

Scope:
- A new method on `IRepository<TAggregate, TId>` (or a dedicated bulk-operations interface composed into it) accepting an `ISpecification<TAggregate>` for the filter and an expression-based "setter" description for `ExecuteUpdate`, returning the affected row count.
- A corresponding bulk-delete method accepting an `ISpecification<TAggregate>`.
- Clear documentation of the interceptor bypass: `ExecuteUpdate`/`ExecuteDelete` bypass `AuditInterceptor`, `SoftDeleteInterceptor`, and `ConcurrencyInterceptor` entirely (EF Core does not route these through `SaveChanges`). The contract must make this bypass explicit in its XML docs and the "what goes where" guidance — for `ISoftDeletable` aggregates, bulk-delete via this path is either disallowed or must require the caller to express the soft-delete semantics explicitly in the setter (e.g., set `IsDeleted = true` directly as part of the bulk update rather than calling bulk-delete).
- The specification pipeline reuse question must be answered: does `ISpecificationEvaluator<T>` apply its full step 0–7 pipeline (filters/includes/ordering/paging) before handing off to `ExecuteUpdate`/`ExecuteDelete`, or only criteria + IncludeDeleted (steps 0–1)? EF Core's `ExecuteUpdate`/`ExecuteDelete` do not support `Include`, `OrderBy`, or `Skip`/`Take` — the evaluator must reject or ignore those spec properties for this path with a clear contract (fail fast at runtime with an explanatory exception, documented as a hard rule).

#### Why this is needed

Every aggregate-loading bulk operation (`UpdateRangeAsync`/`DeleteRangeAsync`) requires loading N entities into memory, running them through change-tracking, and issuing N (or batched) UPDATE/DELETE statements via `SaveChanges`. For genuinely bulk operations — "archive all orders older than 90 days," "anonymize all soft-deleted customer rows," "expire all pending invitations" — this is the dominant cost in services with large tables, and EF Core has shipped `ExecuteUpdate`/`ExecuteDelete` specifically to address it with a single round-trip. Without a SharedKernel-sanctioned abstraction, every downstream team will either (a) reach for raw `DbContext.Set<T>().Where(...).ExecuteUpdateAsync(...)` directly — bypassing `IRepository` entirely and violating the "no `IQueryable<T>` exposure" rule — or (b) suffer the N-entity-load cost on large tables. A specification-driven contract keeps this inside the abstraction boundary while being explicit about the interceptor-bypass trade-off.

#### Acceptance criteria
- [ ] New bulk-update method on the write-side repository contract accepts `ISpecification<TAggregate>` (criteria/IncludeDeleted only) plus a setter expression, returns affected row count, implemented via `ExecuteUpdateAsync`
- [ ] New bulk-delete method accepts `ISpecification<TAggregate>` (criteria/IncludeDeleted only), returns affected row count, implemented via `ExecuteDeleteAsync`
- [ ] XML docs and `06.Persistence/CLAUDE.md` explicitly document that `AuditInterceptor`/`SoftDeleteInterceptor`/`ConcurrencyInterceptor` and domain event dispatch do NOT fire for either method
- [ ] Passing a specification with `Includes`, `OrderBy`/`OrderByDescending`, `ThenBys`, or `Skip`/`Take` to either method throws a documented exception at call time (fail fast, not silent ignore)
- [ ] `ISoftDeletable` aggregates: bulk-delete either throws (directing callers to bulk-update with an explicit `IsDeleted` setter) or is documented as a hard physical delete bypassing soft-delete — pick one and enforce it
- [ ] SQLite-based tests: bulk update affects only matching rows with correct new values; bulk delete removes only matching rows; spec with disallowed properties throws; row counts returned match affected rows
- [ ] `dotnet build` clean; no new compile warnings
---

---
### P-149 — Persistence: Streaming Reads via IAsyncEnumerable on IReadRepository

**Status:** `●` Complete
**Work Order:** WO-024
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

A streaming read method on `IReadRepository<TAggregate, TId>` (and its `EfReadRepository<TAggregate, TId>` implementation) returning `IAsyncEnumerable<TAggregate>` for a given `ISpecification<TAggregate>`, plus a projected variant returning `IAsyncEnumerable<TResult>` for `IProjectionSpecification<TAggregate, TResult>`. Both must:
- Apply the full `ISpecificationEvaluator<T>` pipeline (criteria, includes, ordering, distinct, AsNoTracking) — `Skip`/`Take` interaction with streaming must be documented (paging + streaming together is unusual but not forbidden; document the semantics).
- Always force `AsNoTracking` semantics for streamed results regardless of `spec.AsNoTracking` — streaming thousands of tracked entities through the change tracker is a memory leak in long-lived scopes. Document this override explicitly (it is the one place this contract deviates from "the spec's `AsNoTracking` flag is honored").
- Be backed by EF Core's native `IAsyncEnumerable<T>` query support (`AsAsyncEnumerable()`), preserving server-side streaming (no client-side buffering of the full result set).

#### Why this is needed

`ListAsync` and `ListPagedAsync` both materialize the full result set into an `IReadOnlyList<T>` / `PagedList<T>` before returning. For export jobs, report generation, ETL pipelines, and Temporal workflow activities that need to process large tables row-by-row, this forces either an unbounded in-memory list or application-layer manual chunking via repeated `PagedSpecification<T>` calls (each paying a separate count-query round trip per `ListPagedAsync`'s documented two-round-trip pattern). A first-class streaming contract gives consuming services a server-side-streamed, constant-memory iteration path that stays inside the `ISpecification<T>` abstraction boundary — consistent with how `ListProjectedAsync` and `ListPagedProjectedAsync` already extended the read contract for other access patterns (P-080/P-101).

#### Acceptance criteria
- [ ] `IReadRepository<TAggregate, TId>` gains a method returning `IAsyncEnumerable<TAggregate>` for `ISpecification<TAggregate>`
- [ ] `IReadRepository<TAggregate, TId>` gains a projected variant returning `IAsyncEnumerable<TResult>` for `IProjectionSpecification<TAggregate, TResult>`
- [ ] `EfReadRepository<TAggregate, TId>` implements both via `ISpecificationEvaluator<T>.GetQuery`/`GetProjectedQuery` + `AsAsyncEnumerable()`
- [ ] Both methods force `AsNoTracking` regardless of `spec.AsNoTracking` — documented as an explicit deviation in XML docs and `06.Persistence/CLAUDE.md`
- [ ] `Skip`/`Take` interaction with streaming documented (either honored as a row-window before streaming, or explicitly disallowed with a fail-fast exception — pick one)
- [ ] SQLite-based test: enumerate N entities via the streaming method, verify all N returned in spec order, verify entities are `EntityState.Detached`
- [ ] `ContractShapeTests` verify both new methods exist on `IReadRepository<TAggregate, TId>`
- [ ] `dotnet build` clean; no new compile warnings
---

---
### P-150 — Persistence: Database Readiness Health-Check Contract

**Status:** `●` Complete
**Work Order:** WO-024
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

A lightweight, provider-agnostic database-readiness contract that `13.ServiceDefaults` (or the consuming service directly) can register as a `Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck`. Scope is limited to `06.Persistence` providing the *capability* — wiring the actual `IHealthCheck` registration into `AddHealthChecks()` remains a `13.ServiceDefaults` concern (per the existing layering rule: "OTel, health check, or probe wiring → `13.ServiceDefaults`").

Concretely, `06.Persistence` should provide:
- A simple readiness-check method reachable from `SharedKernelDbContext` (e.g., a lightweight `CanConnectAsync`/`ExecuteRawQueryAsync("SELECT 1")`-style probe) exposed in a form `13.ServiceDefaults` can wrap in an `IHealthCheck` without `13.ServiceDefaults` taking an EF Core dependency beyond what it already has.
- For the Dapper/PostgreSQL path, an equivalent `IDbConnectionFactory`-based readiness probe (open a connection, run a trivial query, measure latency) since not every service using `SharedKernel.Persistence.Dapper` also uses EF Core.
- Both probes should report enough structured detail (latency, provider name) to populate `HealthCheckResult.Data` usefully — but `06.Persistence` itself ships no `IHealthCheck` implementation, only the underlying probe primitives, to avoid a `Microsoft.Extensions.Diagnostics.HealthChecks` NuGet dependency in this layer if one is not already present.

#### Why this is needed

`02.Caching` (`Redis.Core`, per WO-023/P-140) now ships connection-health tracking specifically so dependent packages and `13.ServiceDefaults` can build health checks on top of it. `06.Persistence` — the layer every single microservice depends on for its primary datastore — has no equivalent. Every downstream team currently either omits a DB health check, or writes raw `context.Database.CanConnectAsync()` calls directly inside their `Program.cs` with no shared latency/diagnostics shape. For a platform that is explicitly "K8s-Native" (readiness probes gate traffic routing), the absence of a sanctioned DB-readiness primitive in the persistence layer is a gap relative to the standard this domain otherwise holds itself to.

#### Acceptance criteria
- [ ] A connection-readiness probe method is available via `SharedKernelDbContext` (or an extension method on it) returning success/failure + latency, without requiring `13.ServiceDefaults`-side EF Core internals knowledge
- [ ] An equivalent `IDbConnectionFactory`-based readiness probe exists in `SharedKernel.Persistence.Dapper` or `SharedKernel.Persistence.PostgreSQL` (placement decided based on where `IDbConnectionFactory` already lives) for non-EF read-side services
- [ ] No `Microsoft.Extensions.Diagnostics.HealthChecks` NuGet dependency introduced into `06.Persistence` unless it is already a transitive dependency with zero added weight — probes return plain result types, not `HealthCheckResult`
- [ ] `06.Persistence/CLAUDE.md` documents how `13.ServiceDefaults` is expected to wrap these probes in `IHealthCheck` (cross-reference, not implementation)
- [ ] SQLite/Testcontainers-based tests: probe returns success against a live connection, returns failure with diagnostic detail against an unreachable/disposed connection
- [ ] `dotnet build` clean; no new compile warnings
---

---
### P-151 — Persistence: Idempotent Migration and Seed Runner Abstraction

**Status:** `●` Complete
**Work Order:** WO-024
**Domain:** 06.Persistence
**Depends on:** None

#### What is needed

A small, opt-in abstraction for applying EF Core migrations and running idempotent data seeding at service startup, exposed via `EfCorePersistenceBuilder`. Scope:
- A `IDataSeeder<TContext>` (or similar) marker contract — consuming services implement one or more seeders; each seeder's `SeedAsync(TContext, CancellationToken)` must be written idempotently by the implementer (the contract documents this requirement but cannot enforce it).
- A builder method (e.g., `EfCorePersistenceBuilder<TContext>.WithMigrationsOnStartup()` and/or `.WithSeeders(...)`) that registers a hosted service or startup task: applies pending migrations via `Database.MigrateAsync()`, then runs registered seeders in registration order, all within a distributed lock or advisory-lock guard so multi-replica K8s deployments don't race to apply migrations concurrently.
- Explicit non-goals documented: this is not a migration *authoring* tool (no `dotnet ef migrations add` wrapper) and not a replacement for `dotnet ef dbcontext optimize`/compiled models (P-106) — it is purely the startup orchestration of "apply what's pending, then seed."

#### Why this is needed

"Ensure DB created + apply migrations + seed reference data at startup" is one of the most universally reimplemented pieces of boilerplate across microservices, and it has a well-known multi-replica failure mode: N replicas of the same service starting simultaneously in K8s all call `Database.MigrateAsync()` concurrently, causing migration-lock contention or duplicate-seed races. A SharedKernel-sanctioned, lock-guarded startup orchestration — opt-in via the same fluent `EfCorePersistenceBuilder` every service already uses for `.WithMultiTenancy()`/`.WithEncryption()`/etc. — closes a gap that is currently left to each team to solve (or not solve) independently, with inconsistent results. Note: this phase is for the *EF Core migration* orchestration only — if the distributed-lock primitive needed for multi-replica coordination is best sourced from `02.Caching.Redis.DistributedLocking`, this phase should document that as an optional dependency (consuming service wires it in) rather than `06.Persistence` taking a hard reference to `02.Caching`.

#### Acceptance criteria
- [ ] `IDataSeeder<TContext>` (or equivalent) contract defined with clear idempotency documentation requirement
- [ ] `EfCorePersistenceBuilder<TContext>` gains an opt-in method to register migration-on-startup + ordered seeder execution as a hosted service
- [ ] Multi-replica race documented with a recommended mitigation (e.g., PostgreSQL advisory lock via `IDbConnectionFactory`, or an optional `02.Caching.Redis.DistributedLocking` integration point) — `06.Persistence` does not take a hard reference to `02.Caching`
- [ ] Omitting this call leaves current behavior unchanged (no migrations applied automatically) — fully opt-in
- [ ] SQLite-based test: seeder runs exactly once across two sequential startups when seed data already present (idempotency contract honored by a test seeder implementation); migrations applied when pending
- [ ] `06.Persistence/CLAUDE.md` documents this as startup orchestration only — explicitly not a migration-authoring tool, not a compiled-model replacement
- [ ] `dotnet build` clean; no new compile warnings
---

---
### P-152 — Domain: Generic STJ JsonConverter for StronglyTypedId<TValue>

**Status:** `●` Complete
**Work Order:** WO-024
**Domain:** 03.Domain
**Depends on:** None

#### What is needed

A generic `System.Text.Json.Serialization.JsonConverter<TStronglyTypedId>` (and an accompanying `JsonConverterFactory` for open-generic registration) for `StronglyTypedId<TValue>`-derived types, shipped in `SharedKernel.Domain`. The factory must:
- Detect any closed type deriving from `StronglyTypedId<TValue>` and produce a converter that serializes to/from the underlying `TValue` (using the existing `implicit operator TValue` and the primary-constructor `Value` property — no reflection-based member access beyond what `JsonConverterFactory.CreateConverter` already requires for type inspection).
- Support the common `TValue` shapes already implied elsewhere in the domain (`Guid`, `int`, `long`, `string`) without requiring per-ID-type converter classes in consuming services.
- Be registered via a documented `JsonSerializerOptions` extension (e.g., `options.Converters.Add(new StronglyTypedIdJsonConverterFactory())`) — `03.Domain` ships the factory but does not force global STJ configuration; consuming services opt in.

#### Why this is needed

`03.Domain/CLAUDE.md` currently documents: *"`StronglyTypedId<TValue>` does not ship a STJ `JsonConverter` — consuming services must provide their own in their serialization context."* For a single service this is a minor inconvenience; across "hundreds of services across multiple teams," each implementing the same `JsonConverter<OrderId>`, `JsonConverter<CustomerId>`, etc. boilerplate is exactly the kind of duplication a shared kernel exists to eliminate. `03.Domain` already owns the `StronglyTypedId<TValue>` base and has zero external NuGet dependencies — `System.Text.Json` is part of the BCL (`System.Text.Json` ships in the shared framework for `net10.0`), so adding this does not introduce a new external dependency or violate the "zero external NuGet dependencies" rule for `SharedKernel.Domain`. This directly serves `04.Contracts` (DTOs referencing strongly-typed IDs) and `06.Persistence`/API serialization without requiring `04.Contracts` or `06.Persistence` to own ID-specific converters.

#### Acceptance criteria
- [ ] `StronglyTypedIdJsonConverterFactory` (or equivalently named) added to `SharedKernel.Domain`, in the `StronglyTypedIds/` area
- [ ] Factory correctly handles `StronglyTypedId<Guid>`, `StronglyTypedId<int>`, `StronglyTypedId<long>`, and `StronglyTypedId<string>` derived types via a single registration
- [ ] No reflection beyond standard `JsonConverterFactory.CanConvert`/`CreateConverter` type-inspection patterns already idiomatic to STJ converter factories — documented in the AOT Compatibility section consistent with existing AOT notes (e.g., `DomainEventVersionHelper`'s precedent for acceptable startup-time reflection)
- [ ] `SharedKernel.Domain` remains zero-external-NuGet-dependency (verified: `System.Text.Json` is part of the shared framework, not an added package reference)
- [ ] Unit tests: round-trip serialize/deserialize for each supported `TValue` shape via a concrete test `StronglyTypedId` subclass; serialized JSON is the bare primitive value (not an object wrapper)
- [ ] `03.Domain/CLAUDE.md` updated — the "consuming services must provide their own" note is replaced with usage documentation for the new factory
- [ ] `dotnet build` clean; no new compile warnings
---

---
### P-153 — Governance: Architecture Rule Forbidding Reflection-Based Generic Method Invocation Outside Documented Exceptions

**Status:** `●` Complete
**Work Order:** WO-024
**Domain:** 00.Governance
**Depends on:** P-147

#### What is needed

A new NetArchTest (and/or Roslyn analyzer, following the existing `SK0xxx` numbering convention) rule that flags use of `Type.GetMethod` combined with `MakeGenericMethod`/`Invoke` in production assemblies, except where an explicit, documented exception is registered (e.g., via an `[UnsafeReflectionJustification]`-style marker attribute or an allow-list maintained in governance configuration). The rule should run across all numbered domains, not just `06.Persistence`, since the pattern this rule targets is exactly the one found and fixed in P-147.

#### Why this is needed

P-147 found that `EncryptionRotationService.LoadBatchAsync` — in the same package whose `CLAUDE.md` explicitly documents `TenantedDbContext`'s expression-tree-only approach as the gold-standard alternative to `GetMethod`/`MakeGenericMethod`/`Invoke` — shipped exactly the forbidden pattern, justified by an inline comment claiming it was "not a hot path." Documentation alone did not prevent this from shipping once. A mechanical architecture rule, scoped platform-wide (not just `06.Persistence`), converts this from a documentation convention each domain must remember to restate into an enforced platform invariant — consistent with how P-145 (WO-023) converted the `02.Caching`/`07.Messaging` mutual-exclusion documentation into an enforced NetArchTest rule. Where a genuinely justified exception exists (if any), the rule's allow-list mechanism gives teams an explicit, reviewable opt-out rather than a silent violation.

#### Acceptance criteria
- [ ] New NetArchTest rule (and/or Roslyn analyzer `SK0xxx`) detects `MakeGenericMethod` calls on a `MethodInfo` obtained via `GetMethod`/`GetMethods` reflection in production assemblies across all numbered domains
- [ ] An explicit, documented exception mechanism exists (marker attribute or governance allow-list) for any future justified case, requiring a written rationale
- [ ] The fixed `EncryptionRotationService` from P-147 passes the new rule without needing an exception
- [ ] Rule documented in `00.Governance/CLAUDE.md` with the `EncryptionRotationService` incident referenced as the motivating example
- [ ] All existing production assemblies pass the new rule (or have explicit, reviewed exceptions) — `dotnet build` and architecture test suite both clean
---

### P-154 — Communication: REST Typed HttpClient Package

**Status:** `●` Complete
**Work Order:** WO-025
**Domain:** 11.Communication
**Depends on:** None

#### What is needed

Full implementation of `SharedKernel.Communication.Rest` — the platform-standard typed HTTP client factory for outbound REST communication between microservices.

The package must deliver:

**Builder and options:** A fluent `IRestCommunicationBuilder` entry-point registered via `AddSharedKernelRestCommunication(this IServiceCollection)`. The builder exposes `AddRestClient<TClient>(name, configure)` which wires a named, typed `HttpClient` with the full resilience and propagation stack. `RestClientOptions` carries `BaseAddress`, `TimeoutSeconds`, and a nested `RestResilienceOptions` block covering retry count, exponential base delay, circuit-breaker threshold, sampling duration, and break duration — all with production-safe defaults (retry 3, base delay 500 ms, CB on after 5 failures in 30 s, break for 30 s).

**Resilience pipeline:** `Microsoft.Extensions.Http.Resilience`'s `StandardResilienceHandler` is the required wiring — no raw Polly pipelines built from scratch. The handler must be configured from `RestResilienceOptions` values. Timeout is a per-request timeout, not a global handler timeout.

**Delegation handlers:** Two transient `DelegatingHandler` implementations:

- Correlation ID handler: reads the ambient trace context (`Activity.Current`) and injects `x-correlation-id` on every outgoing request. Falls back to a new random ID when no trace is active. Must not overwrite an already-present `x-correlation-id` header set by the caller.
- Tenant ID handler: reads `IUserContext` (from `12.Security.Abstractions`) from the **request scope** via `IHttpContextAccessor`-backed resolution. Injects `x-tenant-id` when `TenantId` is non-null. Silently no-ops when `IUserContext` is not registered or `TenantId` is absent — never throws.

Both handlers must be registered as transient and must hold no cross-request state.

**ProblemDetails error deserialization:** On non-2xx responses, the package must provide an extension or base typed-client helper that deserializes the response body as `application/problem+json` into a structured error shape. Use STJ source-generated context where available; fall back to reflection-based STJ. The deserialized error must map into `SharedKernel.Primitives.Error` so consuming application code stays within the `Result<T>` monad without referencing raw HTTP status codes.

**Service discovery integration point:** `BaseAddress` on `RestClientOptions` is the only allowed way to set the base URI. When `IServiceEndpointResolver` (from the P-155 `Internal` package) is registered in DI, `AddRestClient<TClient>` should support omitting `BaseAddress` and resolving it at request time via the resolver. This integration is optional — typed clients with a static `BaseAddress` work without any service discovery registration.

**DI registration shape:** follows the example in `11.Communication/CLAUDE.md` verbatim. The builder is fluent and chainable. `AddRestClient<TClient>` returns the builder for continued chaining.

#### Why this is needed

Every microservice in the platform makes outbound HTTP calls. Without a shared, pre-wired typed-client factory, each service re-implements resilience policies, correlation ID propagation, and tenant header injection independently — resulting in: (a) inconsistent retry strategies, (b) lost trace context at service boundaries, (c) tenant ID leakage or omission, and (d) raw `HttpClient` injections that bypass Polly entirely. This package eliminates all four failure modes at source.

`Microsoft.Extensions.Http.Resilience`'s `StandardResilienceHandler` is chosen over raw Polly because it integrates with .NET 10's `IResilienceHttpClientBuilder` pipeline, composes correctly with `IHttpClientFactory`'s handler lifetime management, and aligns with Microsoft's documented guidance for typed HTTP clients — reducing platform maintenance burden when Polly or HttpClient APIs evolve.

#### Acceptance criteria

- [ ] `AddSharedKernelRestCommunication()` registers `IRestCommunicationBuilder` and both delegation handlers in the DI container
- [ ] `AddRestClient<TClient>()` produces a typed client with the `StandardResilienceHandler` configured from `RestResilienceOptions` defaults
- [ ] `RestClientOptions.TimeoutSeconds`, `RetryCount`, `RetryBaseDelayMs`, `CircuitBreakerEnabled`, `FailureThreshold`, `SamplingDurationSec`, `BreakDurationSec` are all applied when configured via `configure` callback
- [ ] `CorrelationIdDelegatingHandler` injects `x-correlation-id` from `Activity.Current?.Id`; falls back to a new GUID; does not overwrite a caller-set header
- [ ] `TenantIdDelegatingHandler` injects `x-tenant-id` from request-scoped `IUserContext.TenantId`; silently skips when context or `TenantId` is absent; resolves from request scope (not singleton)
- [ ] Non-2xx responses deserialized as `application/problem+json` produce a typed `Error` value matching the `SharedKernel.Primitives.Error` shape
- [ ] When `IServiceEndpointResolver` is registered and `BaseAddress` is omitted, the typed client resolves its base address dynamically at request time
- [ ] Raw `HttpClient` is never injected directly by this package — all clients are registered via `IHttpClientFactory`
- [ ] Package references: `01.Core`, `04.Contracts`, `12.Security.Abstractions`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Http.Resilience` — no reference to `02.Caching`, `05.Application`, `06.Persistence`, `07.Messaging`
- [ ] XML doc comments on all public types
- [ ] Test project nested inside `SharedKernel.Communication.Rest/` covering: delegation handler inject/skip/no-overwrite behavior, resilience policy fires (retry + CB), ProblemDetails deserialization to `Error`, and `AddRestClient` builder smoke test

---

### P-155 — Communication: K8s Service Discovery Package

**Status:** `●` Complete
**Work Order:** WO-025
**Domain:** 11.Communication
**Depends on:** None

#### What is needed

Full implementation of `SharedKernel.Communication.Internal` — the in-cluster service endpoint resolver for K8s-native microservice address resolution.

The package must deliver:

**Interface contract:** `IServiceEndpointResolver` with a single `ResolveAsync(string serviceName, CancellationToken ct) → ValueTask<Uri>` method. This is the only interface consuming typed clients may depend on — no concrete DNS types may leak into application code.

**K8s resolver:** `KubernetesServiceEndpointResolver` implements `IServiceEndpointResolver`. It uses `Microsoft.Extensions.ServiceDiscovery` DNS resolution under the hood. Resolution order: DNS SRV records (`_http._tcp.<service>.<namespace>.svc.<clusterDomain>`) first; fall back to A-record for headless services. Namespace defaults to `"default"`, cluster domain defaults to `"cluster.local"`, scheme defaults to `"http"` (overridable via `K8sServiceDiscoveryOptions.SchemeOverride`). The resolver must never throw for an unresolvable name in production — it returns a constructed K8s convention URI and lets the caller's HTTP/gRPC stack surface the connection error.

**Static resolver:** `StaticServiceEndpointResolver` implements `IServiceEndpointResolver` using a `Dictionary<string, Uri>` provided at registration time. Intended exclusively for local development and test environments. Registration via `AddStaticServiceDiscovery(Dictionary<string, Uri>)` must log a `Warning`-level startup message making clear this is not a production resolver. `AddStaticServiceDiscovery` must throw `InvalidOperationException` if `IServiceEndpointResolver` is already registered.

**Options:** `K8sServiceDiscoveryOptions` carries `Namespace`, `ClusterDomain`, and `SchemeOverride`. Validated via Options-pattern validator.

**DI registration:** `AddK8sServiceDiscovery(this IServiceCollection, Action<K8sServiceDiscoveryOptions>? configure)` registers `KubernetesServiceEndpointResolver` as the `IServiceEndpointResolver` singleton. `AddStaticServiceDiscovery(this IServiceCollection, Dictionary<string, Uri>)` registers `StaticServiceEndpointResolver` as a singleton.

**Integration with Rest/Grpc:** This package is a standalone dependency. `SharedKernel.Communication.Rest` and `SharedKernel.Communication.Grpc` each optionally detect `IServiceEndpointResolver` from DI to support omitting `BaseAddress`/`Address` in typed client options. The `Internal` package does not reference `Rest` or `Grpc` — dependency flows only into `Internal`.

#### Why this is needed

Without a platform-standard service discovery abstraction, typed clients hardcode service addresses from configuration or environment variables. In K8s, this produces: (a) configuration drift between environments, (b) missed headless-service scenarios where a single DNS name routes to multiple pod IPs, and (c) inability to swap resolution strategies between dev (static), staging (DNS), and production (DNS + SRV) without code changes. `IServiceEndpointResolver` provides a single injection point that allows environment-specific resolution without changing the typed client implementation.

#### Acceptance criteria

- [ ] `IServiceEndpointResolver` is defined in this package with `ResolveAsync` returning `ValueTask<Uri>`
- [ ] `AddK8sServiceDiscovery()` registers `KubernetesServiceEndpointResolver` as `IServiceEndpointResolver` singleton; `K8sServiceDiscoveryOptions` wired via Options-pattern
- [ ] `KubernetesServiceEndpointResolver.ResolveAsync` never throws for an unresolvable name — returns K8s convention URI on failure
- [ ] `AddStaticServiceDiscovery()` registers `StaticServiceEndpointResolver`; logs `Warning` at startup
- [ ] `AddStaticServiceDiscovery` throws `InvalidOperationException` if `IServiceEndpointResolver` already registered
- [ ] `StaticServiceEndpointResolver.ResolveAsync` returns the registered `Uri` for known service names; returns K8s convention URI for unknown names
- [ ] Package references: `01.Core`, `Microsoft.Extensions.ServiceDiscovery` — no reference to `02.Caching`, `05.Application`, `06.Persistence`, `07.Messaging`, `11.Communication.Rest`, or `11.Communication.Grpc`
- [ ] XML doc comments on all public types
- [ ] Test project nested inside `SharedKernel.Communication.Internal/` covering: static resolver known/unknown lookup, startup guard double-registration, warning log assertion, and Options validation

---

### P-156 — Communication: gRPC Channel Factory Package

**Status:** `●` Complete
**Work Order:** WO-025
**Domain:** 11.Communication
**Depends on:** None

#### What is needed

Full implementation of `SharedKernel.Communication.Grpc` — the platform-standard gRPC typed client factory with OTel tracing, correlation + tenant metadata injection, and Protobuf well-known type helpers.

The package must deliver:

**Builder and options:** A fluent `IGrpcCommunicationBuilder` entry-point registered via `AddSharedKernelGrpcCommunication(this IServiceCollection)`. The builder exposes `AddGrpcClient<TClient>(address, configure)` which wires a gRPC typed client with all interceptors registered globally via `AddGrpcClient<T>().AddInterceptor<T>()`. `GrpcClientOptions` carries `Address` (required), `DeadlineSeconds` (default 30), and `EnableRetry` (default true). `GrpcChannel` instances must be registered as singletons — use `Grpc.Net.ClientFactory` channel caching, not manual `GrpcChannel.ForAddress()` per-request.

**Interceptors (global, not per-call):**

- Correlation + OTel tracing interceptor: injects W3C trace context headers (`traceparent`, `tracestate`) and `x-correlation-id` gRPC metadata. Must read `Activity.Current` at the moment of the call (not at DI registration). Must not overwrite an already-present `x-correlation-id` metadata entry. Must catch all exceptions and log at `Error` level — never propagate into the gRPC call pipeline.
- Tenant ID interceptor: reads `IUserContext.TenantId` (from `12.Security.Abstractions`) and injects `x-tenant-id` gRPC metadata. Silently no-ops when context or `TenantId` is absent. Same exception-swallowing contract as the tracing interceptor.

**Protobuf well-known type helpers:** Pure static extension methods for lossless bidirectional conversion:

- `Money` Protobuf type ↔ `decimal` — allocation-minimal, no intermediate object allocations
- `Timestamp` Protobuf type ↔ `DateTimeOffset` — allocation-minimal

These helpers must be pure, static, and verifiable without a running gRPC service.

**Service discovery integration:** Same optional pattern as P-154 — when `IServiceEndpointResolver` is registered, `AddGrpcClient<TClient>` should support omitting `Address` and resolving it at channel creation time. Static `Address` continues to work without service discovery.

**TLS:** Configured at the channel level only — no per-call TLS configuration.

#### Why this is needed

gRPC is the primary high-throughput inter-service protocol for same-cluster calls in this platform. Without shared interceptors, each service team independently implements — or forgets to implement — OTel trace propagation, correlation ID injection, and tenant context forwarding. A single missed interceptor registration breaks distributed tracing for an entire call chain and loses tenant isolation at the gRPC boundary. Centralizing this in a shared package with global interceptor registration (not per-call) ensures every gRPC call the platform makes is automatically instrumented and context-propagated, with no per-service boilerplate.

`Grpc.Net.ClientFactory` is chosen for channel lifecycle management because it integrates with `IHttpClientFactory`'s named-client pattern and handles channel caching and reconnect correctly — avoiding the common mistake of creating a new `GrpcChannel` per call (expensive) or using a single static channel without proper lifecycle management.

#### Acceptance criteria

- [ ] `AddSharedKernelGrpcCommunication()` registers `IGrpcCommunicationBuilder` and both interceptors in the DI container
- [ ] `AddGrpcClient<TClient>()` wires the typed client via `Grpc.Net.ClientFactory` with channels registered as singletons; `GrpcClientOptions.DeadlineSeconds` applied as a per-call deadline
- [ ] Correlation + OTel interceptor injects `traceparent`, `tracestate`, and `x-correlation-id`; reads `Activity.Current` at call time; does not overwrite existing `x-correlation-id`; swallows its own exceptions with `Error` logging
- [ ] Tenant ID interceptor injects `x-tenant-id` from request-scoped `IUserContext.TenantId`; silently no-ops when absent; swallows exceptions with `Error` logging
- [ ] `MoneyProtoExtensions` converts `Money` Protobuf ↔ `decimal` with no intermediate allocations
- [ ] `TimestampProtoExtensions` converts `Timestamp` Protobuf ↔ `DateTimeOffset` with no intermediate allocations
- [ ] When `IServiceEndpointResolver` is registered and `Address` is omitted, the gRPC channel resolves its address at channel creation time
- [ ] Package references: `01.Core`, `04.Contracts`, `12.Security.Abstractions`, `Grpc.Net.Client`, `Grpc.Net.ClientFactory`, `OpenTelemetry.Instrumentation.GrpcNetClient` — no reference to `02.Caching`, `05.Application`, `06.Persistence`, `07.Messaging`
- [ ] XML doc comments on all public types
- [ ] Test project nested inside `SharedKernel.Communication.Grpc/` covering: interceptor metadata inject/skip/no-overwrite, interceptor exception swallowing, Protobuf helper round-trip correctness, and `AddGrpcClient` builder smoke test

---

### P-157 — Communication: GraphQL Server Convention Package

**Status:** `●` Complete
**Work Order:** WO-025
**Domain:** 11.Communication
**Depends on:** None

#### What is needed

Full implementation of `SharedKernel.Communication.GraphQL` — the platform-standard HotChocolate server-side configuration package for microservices that expose a GraphQL API.

The package must deliver:

**Entry point:** `AddSharedKernelGraphQL(this IServiceCollection, Action<GraphQLOptions>? configure) → IRequestExecutorBuilder`. This must be called before any service-specific `AddGraphQL()` / `AddTypes()` calls — it establishes the base convention all types inherit.

**Options:** `GraphQLOptions` carries: `EnableFiltering` (default true), `EnableSorting` (default true), `EnablePaging` (default true, offset paging), `MaxPageSize` (default 100, hard cap at 500), `AllowIntrospection` (default true — consuming services must gate this to `IsDevelopment()`).

**Convention wiring:** `AddSharedKernelGraphQL` applies:

- snake_case field naming convention across all types
- `SharedKernelFilterConvention` — a pre-configured `FilterConvention` that registers standard filter operations for string, numeric, and date field types using snake_case binding names that match REST API field naming
- Cursor pagination and offset pagination support, with `MaxPageSize` enforced globally (services must not increase beyond 500 without documented justification)
- Error mapping via `IErrorFilter` that translates `IError` → the same `ProblemDetails`-compatible shape used by REST responses, so GraphQL errors and REST errors have a consistent shape for API consumers

**Base input types for consuming services:** Two open generic base classes:

- `FilterBase<T>` — extends HotChocolate's `FilterInputType<T>`; consuming services override `Descriptor()` to customise field visibility and operations; enforces snake_case binding
- `SortBase<T>` — extends `SortInputType<T>`; same customisation model

Consuming services must always subclass these bases — direct registration of raw `FilterInputType<T>` or `SortInputType<T>` without the base wrapper is a platform violation (enforced by P-159 governance rule).

**Pagination helper:** `PagedResponseType<T>` — a wrapper type that presents HotChocolate paged results (both `CollectionSegment` for offset and `Connection` for cursor) with a consistent `TotalCount + Items` shape, matching the `PagedList<T>` shape used in REST responses from `06.Persistence`.

#### Why this is needed

HotChocolate's default configuration exposes raw C# PascalCase field names, allows unbounded page sizes, permits full-entity filter exposure without restriction, and produces HotChocolate-specific error shapes that diverge from the platform's REST `ProblemDetails` convention. Services that configure HotChocolate independently from scratch inevitably produce inconsistent API surfaces. This package locks in snake_case naming, bounded paging, restricted filter types, and unified error shapes as non-negotiable platform defaults, while leaving all domain-specific type registrations to each consuming service.

The `AllowIntrospection` default of `true` requires explicit opt-out in production. This is intentional — introspection is essential for development and CI schema validation, and the burden of disabling it in production is placed on the consuming service's `Program.cs` environment gate, where it is visible to reviewers.

#### Acceptance criteria

- [ ] `AddSharedKernelGraphQL()` returns an `IRequestExecutorBuilder` for continued type registration chaining
- [ ] snake_case field naming applied globally across all types registered after `AddSharedKernelGraphQL()`
- [ ] `SharedKernelFilterConvention` registered; string/numeric/date filter operations available with snake_case binding names
- [ ] `GraphQLOptions.MaxPageSize` enforced globally; attempts to request more items than `MaxPageSize` are rejected at the HotChocolate layer
- [ ] `IErrorFilter` registered; `IError` items in GraphQL responses produce a `ProblemDetails`-compatible JSON shape matching the REST error convention
- [ ] `FilterBase<T>` and `SortBase<T>` are public abstract base classes; consuming services extend them and override `Descriptor()` to configure visible fields
- [ ] `PagedResponseType<T>` presents a `TotalCount + Items` shape for both offset and cursor paged queries
- [ ] `GraphQLOptions.AllowIntrospection = false` disables GraphQL schema introspection; default is `true`
- [ ] `AddSharedKernelGraphQL` must be idempotent — calling it twice does not double-register conventions
- [ ] Package references: `01.Core`, `04.Contracts`, `HotChocolate.AspNetCore`, `HotChocolate.Data` — no reference to `02.Caching`, `05.Application`, `06.Persistence`, `07.Messaging`, `12.Security` (GraphQL tenant resolution is JWT-claim-based, done by the consuming service's security pipeline)
- [ ] XML doc comments on all public types
- [ ] Test project nested inside `SharedKernel.Communication.GraphQL/` using HotChocolate's `IRequestExecutor` test builder; covers: filter + sort + paging with configured convention, `MaxPageSize` enforcement, error mapping to ProblemDetails shape, `AllowIntrospection` flag behavior

---

### P-158 — Testing: Communication Package Test Helpers

**Status:** `○` Pending
**Work Order:** WO-025
**Domain:** 16.Testing
**Depends on:** P-154, P-155, P-156, P-157

#### What is needed

Additions to `SharedKernel.Testing` to support testing of code that depends on `11.Communication` packages.

The additions must deliver:

**HTTP delegation handler test infrastructure:**

- A `FakeHttpMessageHandler` (or equivalent test-doubles pattern) that allows unit tests to assert which headers were injected on outgoing `HttpRequestMessage` instances without making real HTTP calls. Must support configurable response fixture (status code + body) and response sequence (first call returns 503, second returns 200) for resilience policy testing.
- Extension helpers on `IServiceCollection` to register delegation handlers in test DI without a full `HttpClient` factory pipeline.

**Correlation and tenant context fakes:**

- A `FakeUserContext` (if not already present in SharedKernel.Testing) that implements `IUserContext` with configurable `TenantId`, `UserId`, and `Roles` — reusable for delegation handler tests, interceptor tests, and application-layer tests.
- A helper to set an ambient `Activity` with a specific trace ID and parent for testing correlation ID propagation.

**gRPC interceptor test infrastructure:**

- A test helper or factory that produces a `ServerCallContext`-equivalent stub for testing Grpc interceptors in isolation without a real gRPC channel. Must allow inspecting injected metadata entries after interceptor execution.

**Service discovery test doubles:**

- An `InMemoryServiceEndpointResolver` implementing `IServiceEndpointResolver` with a configurable `Dictionary<string, Uri>` — simpler than `StaticServiceEndpointResolver` (which logs warnings); intended purely for test DI registration. Registered via `AddInMemoryServiceDiscovery(Dictionary<string, Uri>)`.

**GraphQL test helpers:**

- A pre-configured `IRequestExecutorBuilder` factory helper that wires `AddSharedKernelGraphQL()` with test-safe defaults (`AllowIntrospection = true`, `MaxPageSize = 10`) for HotChocolate filter/sort/paging unit tests.

#### Why this is needed

Without shared test infrastructure for `11.Communication`, each microservice team writes their own `FakeHttpMessageHandler`, their own `IUserContext` stub, and their own gRPC metadata inspection helpers — producing dozens of subtly different test doubles across the platform. Consolidating these into `SharedKernel.Testing` ensures a single, maintained, correct set of test primitives that stay in sync with the production interfaces as they evolve.

The `InMemoryServiceEndpointResolver` is distinct from `StaticServiceEndpointResolver` (P-155) by design: the static resolver is production code that logs warnings; the in-memory resolver is a test-only double that never logs, never throws, and has no startup side effects.

#### Acceptance criteria

- [ ] `FakeHttpMessageHandler` added to `SharedKernel.Testing`; supports fixed and sequenced response fixtures; allows `RequestMessage` inspection after the call
- [ ] Extension to set up delegation handler tests via `IServiceCollection` without full `IHttpClientFactory` pipeline
- [ ] `FakeUserContext` with configurable `TenantId`, `UserId`, `Roles` added (or confirmed already present); implements `IUserContext` from `12.Security.Abstractions`
- [ ] Ambient `Activity` factory helper for trace ID / parent injection in unit tests
- [ ] gRPC `ServerCallContext` stub that allows metadata entry inspection after interceptor execution
- [ ] `InMemoryServiceEndpointResolver` implementing `IServiceEndpointResolver`; registered via `AddInMemoryServiceDiscovery(Dictionary<string, Uri>)`; no startup warnings or side effects
- [ ] GraphQL `IRequestExecutorBuilder` test factory helper wiring `AddSharedKernelGraphQL()` with test-safe defaults
- [ ] All new types in `SharedKernel.Testing` reference only production packages via their abstraction interfaces — `SharedKernel.Communication.Rest`, `.Grpc`, `.GraphQL`, `.Internal` are NOT referenced by `SharedKernel.Testing`; test helpers reference only `SharedKernel.Security.Abstractions` for `IUserContext`
- [ ] XML doc comments on all new public helpers

---

### P-159 — Governance: Architecture Rules for Communication Layer

**Status:** `●` Complete
**Work Order:** WO-025
**Domain:** 00.Governance
**Depends on:** P-154, P-155, P-156, P-157

#### What is needed

New architecture enforcement rules in `SharedKernel.ArchitectureTests` (NetArchTest) covering the `11.Communication` package family.

The rules must mechanically enforce the following invariants across all platform assemblies:

**Communication layering rules:**

- `11.Communication.*` packages must not reference `02.Caching.*`, `05.Application`, `06.Persistence.*`, `07.Messaging.*` — the layering table in root CLAUDE.md permits only `01.Core`, `04.Contracts`, and `12.Security` abstractions
- No assembly outside `11.Communication.*` may directly reference `Grpc.Net.Client`, `Grpc.Net.ClientFactory`, or `HotChocolate.*` — those libraries must be accessed only through the `SharedKernel.Communication.Grpc` and `SharedKernel.Communication.GraphQL` wrappers
- `SharedKernel.Communication.Internal` must not reference `SharedKernel.Communication.Rest`, `SharedKernel.Communication.Grpc`, or `SharedKernel.Communication.GraphQL` — the dependency flows into `Internal`, not out of it

**Raw injection guards:**

- Production assemblies (any assembly not in `*.Tests`) must not declare a constructor parameter of type `HttpClient` directly — typed clients must be injected as their named `TClient` interface, never as `HttpClient`. Enforced via a NetArchTest or Roslyn analyzer SK0xxx rule.
- Production assemblies must not reference `Grpc.Core.Interceptors.Interceptor` directly outside `11.Communication.Grpc` — interceptor implementations belong only in the Communication package.

**GraphQL convention guard:**

- Assemblies referencing `HotChocolate.Data` must not directly instantiate or inherit `FilterInputType<T>` or `SortInputType<T>` without going through `FilterBase<T>` or `SortBase<T>` from `SharedKernel.Communication.GraphQL`. This prevents accidental full-entity filter exposure and naming convention violations.

**Hardcoded URI guard:**

- Production typed clients (classes implementing a typed-client interface and depending on `HttpClient` or `GrpcChannel`) must not assign a `Uri` constructed from a string literal or `IConfiguration` value directly — `BaseAddress` or `Address` must be set only via `RestClientOptions`/`GrpcClientOptions` options objects or resolved via `IServiceEndpointResolver`. This is a documentation-enforced rule; a Roslyn analyzer can be added if mechanical enforcement is feasible without excessive false positives.

#### Why this is needed

P-145 (WO-023) demonstrated that documenting the `02.Caching`/`07.Messaging` mutual exclusion rule was insufficient — the pattern was violated in a package whose own CLAUDE.md documented it as forbidden. The same risk applies here: the `11.Communication` layering rules are clear in CLAUDE.md but nothing mechanically prevents a developer from injecting `HttpClient` directly, importing `Grpc.Net.Client` in an application assembly, or registering a raw `FilterInputType<T>`. Architecture tests convert these documentation rules into build-time failures.

The raw `HttpClient` injection guard is particularly important: `IHttpClientFactory`-managed clients participate in connection pooling, DNS refresh, and handler lifetime management; a directly-injected `HttpClient` singleton bypasses all of this and is a known production reliability issue in .NET microservices.

#### Acceptance criteria

- [ ] NetArchTest rule: `11.Communication.*` assemblies have no reference to `02.Caching.*`, `05.Application`, `06.Persistence.*`, `07.Messaging.*`
- [ ] NetArchTest rule: `SharedKernel.Communication.Internal` has no reference to `SharedKernel.Communication.Rest`, `.Grpc`, or `.GraphQL`
- [ ] NetArchTest or Roslyn analyzer rule (SK0xxx): no production assembly constructor declares a parameter of raw `HttpClient` type (exception: `DelegatingHandler` subclasses in `SharedKernel.Communication.Rest` are explicitly exempted)
- [ ] NetArchTest rule: no assembly outside `SharedKernel.Communication.Grpc` directly references `Grpc.Core.Interceptors.Interceptor` as a base class
- [ ] NetArchTest rule: no assembly referencing `HotChocolate.Data` inherits from `FilterInputType<T>` or `SortInputType<T>` directly without going through `FilterBase<T>` / `SortBase<T>` (exemption: `SharedKernel.Communication.GraphQL` itself for defining those bases)
- [ ] All rules documented in `00.Governance/CLAUDE.md` with the motivating principle and exemption mechanism for each
- [ ] `dotnet build` and full architecture test suite clean with 0 violations on current codebase
- [ ] Rules assigned SK0xxx numbers in the existing governance numbering sequence
---

---
### P-160 — Communication: REST Package — Four Correctness and Quality Fixes

**Status:** `●` Complete
**Work Order:** WO-026
**Domain:** 11.Communication
**Depends on:** None

#### What is needed

Four targeted correctness and quality fixes in `SharedKernel.Communication.Rest`:

**Fix 1 — Cached `JsonSerializerOptions` in `ProblemDetailsDeserializer` fallback path.**
The reflection-based STJ fallback inside `ProblemDetailsDeserializer.DeserializeAsync` allocates a new `JsonSerializerOptions` object on every call. `JsonSerializerOptions` initialization is expensive (internal state machine build). Promote it to a `static readonly` field. This is a hot-path allocation in production services that receive non-standard error responses.

**Fix 2 — Register `RestClientOptionsValidator` in the DI container.**
`RestClientOptionsValidator` exists in the codebase but is never registered via `services.AddSingleton<IValidateOptions<RestClientOptions>, RestClientOptionsValidator>()` in `ServiceCollectionExtensions.cs`. This means the validator never fires at startup — `TimeoutSeconds <= 0` and whitespace `BaseAddress` inputs go unchecked. Register the validator alongside the other options infrastructure.

**Fix 3 — Replace `Services.Any(...)` O(n) DI probe with a sentinel-flag pattern.**
Both `RestCommunicationBuilder.AddRestClient<TClient>` and `GrpcCommunicationBuilder.AddGrpcClient<TClient>` probe for `IServiceEndpointResolver` via `Services.Any(d => d.ServiceType == typeof(...))`. This walks the entire descriptor list on every typed-client registration. In large services this is O(n×m). The resolver-presence check should be captured at builder-construction time (when `AddSharedKernelRestCommunication()` / `AddSharedKernelGrpcCommunication()` is called) and stored as a `bool` on the builder, or use a sentinel-marker approach consistent with the GraphQL idempotency guard. The `AddStaticServiceDiscovery` guard uses the same pattern and should be cleaned up similarly.

**Fix 4 — Expose and document the `TotalRequestTimeout` calculation formula.**
`RestCommunicationBuilder` computes `TotalRequestTimeout` as `TimeoutSeconds × (RetryCount + 1) + 10`. The magic `+10` second buffer is undocumented. Add `RestResilienceOptions.TotalTimeoutBufferSec` (default: `10`, minimum: `0`) with XML documentation explaining that it accounts for jitter headroom and circuit-breaker probe time. The builder uses this value instead of the hardcoded literal. Consumers can override to `0` for tight latency budgets.

#### Why this is needed

Fix 1 directly impacts production throughput — `JsonSerializerOptions` allocation on every error response creates GC pressure at scale. Fix 2 means options-validation guarantees currently documented in code are actually silent — a `TimeoutSeconds = 0` config silently passes today. Fix 3 is a scalability anti-pattern in DI bootstrap that degrades as the number of registered clients grows. Fix 4 exposes a non-obvious production behaviour (real effective timeout is significantly higher than `TimeoutSeconds` implies) that catches operators by surprise in SLA-sensitive services.

#### Acceptance criteria
- [ ] `ProblemDetailsDeserializer` uses a `static readonly JsonSerializerOptions` field for the reflection fallback path — zero `new JsonSerializerOptions(...)` calls in `DeserializeAsync`
- [ ] `RestClientOptionsValidator` is registered via `services.AddSingleton<IValidateOptions<RestClientOptions>, RestClientOptionsValidator>()` in `AddSharedKernelRestCommunication`
- [ ] `RestCommunicationBuilder` captures resolver-presence as a `bool` at construction time, not per-client-registration (eliminates `Services.Any(...)` per-call)
- [ ] `GrpcCommunicationBuilder` captures resolver-presence at construction time similarly
- [ ] `RestResilienceOptions.TotalTimeoutBufferSec` property added (default `10`, minimum `0`); builder uses it in the `TotalRequestTimeout` formula
- [ ] XML doc on `TotalTimeoutBufferSec` explains the formula: "Added to per-attempt timeout × (RetryCount + 1) to compute TotalRequestTimeout. Provides headroom for jitter and circuit-breaker probe time."
- [ ] All existing REST tests continue to pass
- [ ] New unit tests: options validator fires on invalid `TimeoutSeconds`; options validator fires on whitespace `BaseAddress`
---

---
### P-161 — Communication: REST Package — Full-Response `ReadEnvelopeAsync<T>` and `Result<T>` / `Envelope<T>` Bridge

**Status:** `●` Complete
**Work Order:** WO-026
**Domain:** 11.Communication
**Depends on:** P-160, P-166

#### What is needed

Two related additions that complete the `Result<T>` → `Envelope<T>` boundary mapping in the REST communication layer:

**Addition 1 — `ReadEnvelopeAsync<T>` on `HttpResponseMessageExtensions`.**
The existing `EnsureSuccessOrErrorAsync<T>` checks the HTTP status and returns `Result<T>.Success(default!)` on 2xx — the caller must then separately deserialize the body. This is a two-step pattern that forces boilerplate in every typed client method. Add `ReadEnvelopeAsync<T>` that on a 2xx response deserializes the body using STJ, and returns `Envelope<T>.Ok(value)`. On non-2xx, it deserializes the ProblemDetails body via `ProblemDetailsDeserializer` and returns `Envelope<T>.Fail(error)`. This is the single-step typed-client entry point that matches the boundary responsibility rule ("the communication layer constructs `Envelope<T>` at service boundaries").

The serialization in `ReadEnvelopeAsync<T>` uses `JsonTypeInfo<T>` passed by the caller for the AOT-safe primary path, plus a `JsonSerializerOptions?` overload for the reflection-based fallback. The static `JsonSerializerOptions` from P-160 Fix 1 applies here for consistency.

**Addition 2 — `RestEnvelopeExtensions` bridge pattern removal note.**
With P-166 delivering `ToEnvelope()`/`ToResult()` in `SharedKernel.Contracts`, the `Communication.Rest` package does not need to duplicate bridge extensions. However, `ReadEnvelopeAsync<T>` on `HttpResponseMessageExtensions` is the REST-specific entry point that internally uses `Envelope<T>.Ok()` / `Envelope<T>.Fail()` directly — no bridge extension needed here.

#### Why this is needed

`ReadEnvelopeAsync<T>` completes the boundary mapping story. Every typed HTTP client method today requires: (1) call the upstream service, (2) call `EnsureSuccessOrErrorAsync<T>`, (3) if success, deserialize the body separately, (4) construct `Envelope<T>` manually. Steps 2–4 collapse into a single `ReadEnvelopeAsync<T>` call. This is the developer-experience improvement that makes the platform feel cohesive rather than piecemeal.

#### Acceptance criteria
- [ ] `HttpResponseMessageExtensions.ReadEnvelopeAsync<T>(JsonTypeInfo<T> typeInfo, CancellationToken)` added — AOT-safe primary path
- [ ] `HttpResponseMessageExtensions.ReadEnvelopeAsync<T>(JsonSerializerOptions? options, CancellationToken)` added — reflection fallback overload
- [ ] On 2xx: body deserialized into `T`; returned as `Envelope<T>.Ok(value)`; null/empty body returns `Envelope<T>.Fail(Error.Unexpected("http.empty-body", "Response body was empty or null."))`
- [ ] On non-2xx: `ProblemDetailsDeserializer.DeserializeAsync` used; returned as `Envelope<T>.Fail(error)` — consistent error mapping
- [ ] All `ReadEnvelopeAsync<T>` paths covered by unit tests using `HttpMessageHandler` test doubles with controlled response bodies (success body, empty body, ProblemDetails body, non-ProblemDetails error body)
- [ ] XML doc on all new API surface
- [ ] All existing REST tests continue to pass
---

---
### P-162 — Communication: REST Package — Fix `ServiceDiscoveryResolvingHandler` Per-Client-Name Registration Bug

**Status:** `●` Complete
**Work Order:** WO-026
**Domain:** 11.Communication
**Depends on:** P-160

#### What is needed

Fix a subtle but real production bug in `RestCommunicationBuilder.AddRestClient<TClient>` in the service-discovery path (when `BaseAddress` is omitted).

**The bug:** When two typed clients are registered without a `BaseAddress` — for example, `AddRestClient<IOrderClient>("order-service", ...)` followed by `AddRestClient<IPaymentClient>("payment-service", ...)` — both calls register a transient factory for `ServiceDiscoveryResolvingHandler` in the DI container. The second registration overwrites the first. When `IOrderClient` later resolves its `ServiceDiscoveryResolvingHandler`, it gets the handler registered for "payment-service" (the last one registered), not "order-service". Both clients silently route to the wrong service.

**The fix:** `ServiceDiscoveryResolvingHandler` must not be registered as a shared singleton or transient `ServiceDiscoveryResolvingHandler` DI type when multiple clients need distinct service names. The correct fix is to use `builder.AddHttpMessageHandler(sp => new ServiceDiscoveryResolvingHandler(sp.GetRequiredService<IServiceEndpointResolver>(), capturedName))` as an inline factory on the `IHttpClientBuilder` — bypassing DI type registration entirely. Each typed client's message handler pipeline gets its own closure-captured service name. This is a non-breaking fix — the external API shape does not change.

Additionally, add `RestClientOptions.ServiceName` as an optional override (`string?`): when set, the resolver uses `ServiceName` for DNS lookup instead of the `name` parameter passed to `AddRestClient<TClient>`. This separates the typed client's logical registration name from its DNS service name.

#### Why this is needed

Any microservice that registers two or more typed clients using service-discovery (without explicit `BaseAddress`) will route at least one of them to the wrong DNS name. This is a production routing failure with no error at startup — requests succeed but reach the wrong service. The fix is straightforward and non-breaking since the external API shape does not change.

#### Acceptance criteria
- [ ] Registering two or more typed clients without `BaseAddress` in the same service correctly routes each client to its own service name
- [ ] `ServiceDiscoveryResolvingHandler` is no longer registered as a shared DI type — each client gets its own instance via inline `AddHttpMessageHandler` factory closure
- [ ] `RestClientOptions.ServiceName` property added (nullable `string?`, default `null`); when set, used as the DNS lookup key; when null, the `name` parameter is used
- [ ] XML doc on `ServiceName` explains: "Overrides the `name` parameter for DNS lookup via IServiceEndpointResolver. Use when the typed client's logical registration name differs from its DNS service name."
- [ ] Unit test: two typed clients registered without `BaseAddress` with different service names; assert each resolves its own service endpoint (using `MockServiceEndpointResolver` from P-168)
- [ ] Unit test: `RestClientOptions.ServiceName` override correctly routes to the overridden DNS name
- [ ] All existing REST tests continue to pass
---

---
### P-163 — Communication: gRPC Package — Extract `GrpcMetadataHelper`, Fix Address Parameter, Remove Dead 04.Contracts Reference

**Status:** `●` Complete
**Work Order:** WO-026
**Domain:** 11.Communication
**Depends on:** None

#### What is needed

Three improvements in `SharedKernel.Communication.Grpc`:

**Improvement 1 — Extract `GrpcMetadataHelper` internal static class.**
`CorrelationTracingInterceptor` and `TenantIdInterceptor` both contain identical implementations of `HasMetadataEntry(Metadata, string)` and `CloneAndAdd(Metadata, string, string)`. These two utilities should live in a single `internal static class GrpcMetadataHelper` within the package, and both interceptors should delegate to it. This eliminates duplication and provides a single place to optimize or harden Metadata manipulation logic (e.g., span-based lookup for high-throughput scenarios in the future).

**Improvement 2 — Make `AddGrpcClient<TClient>` address parameter optional.**
`IGrpcCommunicationBuilder.AddGrpcClient<TClient>(string address, ...)` declares `address` as a required `string`. But the implementation treats it as optional (allows empty string when `IServiceEndpointResolver` is present). The interface is dishonest. Change the signature to `string? address = null` on both the interface and implementation. Callers using service discovery can omit the address cleanly without passing an empty string. The validation error message when neither address nor resolver is present remains unchanged.

**Improvement 3 — Remove dead `04.Contracts` project reference from `.Grpc` csproj.**
`SharedKernel.Communication.Grpc.csproj` references `SharedKernel.Contracts`. Inspecting all production `.cs` files in the package, no type from `SharedKernel.Contracts` is used anywhere — gRPC uses Protobuf-generated types directly; `Envelope<T>`, `PagedList<T>`, and `EventEnvelope<T>` do not appear in any file. This dead reference adds unnecessary transitive dependency weight to every downstream consumer. Remove it. The gRPC package only needs `SharedKernel.Primitives`, `SharedKernel.Security.Abstractions`, and the gRPC NuGet packages.

#### Why this is needed

The metadata helper duplication (Finding 5) means any bug in `HasMetadataEntry` must be fixed in two places independently. For a platform package consumed by hundreds of services, duplication in cross-cutting interceptors is a maintenance liability. The address parameter dishonesty (Finding 8) forces callers using service discovery to pass `""` — developer-hostile and not self-documenting. The dead `04.Contracts` reference (Finding 2/3) adds unnecessary package weight and can trigger false positives in future governance dependency checks.

#### Acceptance criteria
- [ ] `GrpcMetadataHelper` internal static class added with `HasMetadataEntry(Metadata, string): bool` and `CloneAndAdd(Metadata, string, string): Metadata` methods
- [ ] `CorrelationTracingInterceptor` delegates to `GrpcMetadataHelper` — no local `HasMetadataEntry` or `CloneAndAdd`
- [ ] `TenantIdInterceptor` delegates to `GrpcMetadataHelper` — no local `HasMetadataEntry` or `CloneAndAdd`
- [ ] `IGrpcCommunicationBuilder.AddGrpcClient<TClient>` signature: `string? address = null`
- [ ] `GrpcCommunicationBuilder` implementation updated — existing validation logic (throw when neither address nor resolver present) unchanged
- [ ] `SharedKernel.Contracts` project reference removed from `SharedKernel.Communication.Grpc.csproj`
- [ ] All existing gRPC tests pass
- [ ] New unit test: `GrpcMetadataHelper.HasMetadataEntry` — case-insensitive match, no false positive on absent key, no false negative on present key; `GrpcMetadataHelper.CloneAndAdd` — new metadata instance, original preserved, new entry present
---

---
### P-164 — Communication: Internal Package — TTL-Based Endpoint Resolution Cache in `KubernetesServiceEndpointResolver`

**Status:** `●` Complete
**Work Order:** WO-026
**Domain:** 11.Communication
**Depends on:** None

#### What is needed

Add a TTL-based, thread-safe in-memory endpoint cache to `KubernetesServiceEndpointResolver`.

Currently, `ResolveAsync` performs live DNS SRV + A-record lookups on every call. For REST clients using the `ServiceDiscoveryResolvingHandler` path (resolving at request time), this means a DNS lookup on every outbound HTTP request — significant DNS amplification at scale.

The cache design:
- Keyed by `serviceName` (case-insensitive, `StringComparer.OrdinalIgnoreCase`)
- Value: a struct holding the resolved `Uri` and `DateTimeOffset ExpiresAt`
- TTL configurable via `K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds` (int, default `30`, `0` = disabled)
- Thread-safe: use `ConcurrentDictionary<string, CachedEntry>` with TTL-check on read
- On cache hit (entry not expired): return cached `Uri` — no DNS I/O
- On cache miss or TTL expiry with a successful DNS lookup: update cache with new `Uri` and new expiry; return new `Uri`
- On DNS lookup failure with a stale cached entry (post-expiry): log `LogLevel.Warning` and return stale `Uri` (stale-while-revalidate — prefer known-good stale over unknown fallback)
- On DNS lookup failure with no cache entry at all: fall through to existing K8s convention URI fallback
- When `EndpointCacheTtlSeconds = 0`: no reads or writes to cache; exact existing behavior preserved

`StaticServiceEndpointResolver` does not need caching — its map is already in-memory and static.

#### Why this is needed

Without caching, REST typed clients using service-discovery resolution trigger DNS lookups per request. In a microservice at 500 req/s making 3 downstream calls each, this is 1,500 DNS requests per second — unnecessary load on K8s DNS infrastructure (CoreDNS). K8s pod endpoints are stable within a deployment window; a 30-second TTL eliminates 99%+ of DNS traffic under normal operation while reacting to pod IP changes within one TTL window. The stale-while-revalidate pattern prevents cache expiry from becoming a cascading failure when the DNS server is transiently unavailable.

#### Acceptance criteria
- [ ] `K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds` property added (int, default `30`)
- [ ] `K8sServiceDiscoveryOptionsValidator` updated to reject negative `EndpointCacheTtlSeconds`
- [ ] `KubernetesServiceEndpointResolver` uses `ConcurrentDictionary` for cache storage; cache struct holds `Uri` and `DateTimeOffset ExpiresAt`
- [ ] Cache hit (not expired): returns cached `Uri` without calling `resolver.GetEndpointsAsync`
- [ ] Cache miss / expired with successful DNS: updates cache and returns new `Uri`
- [ ] Cache miss / expired with failed DNS + stale entry: logs `Warning` and returns stale `Uri`
- [ ] Cache miss / expired with failed DNS + no prior entry: falls through to K8s convention URI (existing behavior)
- [ ] `EndpointCacheTtlSeconds = 0`: cache bypassed entirely; all lookups are live
- [ ] Unit tests cover all four cache scenarios; `EndpointCacheTtlSeconds = 0` bypass; negative value rejected by validator
---

---
### P-165 — Communication: GraphQL Package — `PagedResponseType<T>.FromPagedList` Bridge Factory

**Status:** `●` Complete
**Work Order:** WO-026
**Domain:** 11.Communication
**Depends on:** None

#### What is needed

Add a `PagedResponseType<T>.FromPagedList(PagedList<T> pagedList)` static factory method to `SharedKernel.Communication.GraphQL.Pagination.PagedResponseType<T>`.

Currently, `PagedResponseType<T>` provides `FromPage(IPage)` (HC offset paging), `FromConnection(Connection<T>)` (HC cursor paging), and `From(IReadOnlyList<T>, int)` (manual assembly). However, the persistence layer and application handlers return `PagedList<T>` from `SharedKernel.Contracts`. A GraphQL resolver receiving a `PagedList<T>` must manually unpack `.Items` and `.TotalCount` before calling `From(items, totalCount)`. The `FromPagedList` factory collapses this to a single call.

The CLAUDE.md states "field names match `PagedList<T>` from `04.Contracts` for API shape consistency" — this factory makes that stated consistency executable, not just documented. The `SharedKernel.Communication.GraphQL` package already references `SharedKernel.Contracts` — this addition makes that reference purposeful rather than dead weight.

Factory signature: `public static PagedResponseType<T> FromPagedList(PagedList<T> pagedList)` — maps `pagedList.Items` to `Items` and `pagedList.TotalCount` to `TotalCount`.

#### Why this is needed

Without `FromPagedList`, every GraphQL resolver that receives a `PagedList<T>` from the application layer must manually unpack `.Items` and `.TotalCount`. This is repetitive boilerplate, exposes the internal shape of `PagedList<T>` to each resolver, and will silently drift if either type evolves. The bridge factory seals the shape contract and makes the "same structure" claim between `PagedList<T>` and `PagedResponseType<T>` structurally enforced rather than merely documented.

#### Acceptance criteria
- [ ] `PagedResponseType<T>.FromPagedList(PagedList<T> pagedList)` static factory method added
- [ ] Factory returns `new PagedResponseType<T> { Items = pagedList.Items, TotalCount = pagedList.TotalCount }`
- [ ] `ArgumentNullException.ThrowIfNull(pagedList)` guard included
- [ ] XML doc comment explains relationship to `PagedList<T>` and when to use `FromPagedList` vs `FromPage` / `FromConnection` vs `From`
- [ ] `SharedKernel.Communication.GraphQL.csproj` comment updated to note that the `SharedKernel.Contracts` reference is used by `FromPagedList`
- [ ] Unit test: `FromPagedList` with a populated `PagedList<string>` produces correct `Items` and `TotalCount`; null input throws `ArgumentNullException`
- [ ] All existing GraphQL tests continue to pass
---

---
### P-166 — Contracts: `Result<T>` ↔ `Envelope<T>` Mapping Extension Methods

**Status:** `●` Complete
**Work Order:** WO-026
**Domain:** 04.Contracts
**Depends on:** None

#### What is needed

Add a `ResultEnvelopeExtensions` static class to `SharedKernel.Contracts` providing mapping extension methods between `Result<T>` / `Result` (from `SharedKernel.Primitives`) and `Envelope<T>` / `Envelope` (from `SharedKernel.Contracts`).

These extensions enable the documented boundary-mapping rule ("the communication layer maps `Result<T>` to `Envelope<T>` at service boundaries") without ad-hoc inline boilerplate in every typed client, controller action, or gRPC server handler.

**Extensions needed:**
- `ToEnvelope<T>(this Result<T> result) → Envelope<T>` — success maps to `Envelope<T>.Ok(result.Value)`; failure maps to `Envelope<T>.Fail(result.Error)`
- `ToEnvelope(this Result result) → Envelope` — non-generic variant for void operations
- `ToResult<T>(this Envelope<T> envelope) → Result<T>` — maps `Envelope<T>` back to `Result<T>`; useful in client adapters that receive a deserialized `Envelope<T>` and need to re-enter the railway pipeline
- `ToResult(this Envelope envelope) → Result` — non-generic variant

These extensions belong in `SharedKernel.Contracts` because that package already references both `SharedKernel.Primitives` (for `Error`, `Result<T>`) and owns `Envelope<T>`. No other package in the allowed layering graph can hold this bridge without introducing a forbidden reference. Placing them in `11.Communication.Rest` would force any non-HTTP boundary (e.g., `14.Presentation`, gRPC server handlers) to reference a HTTP-specific package for this fundamental conversion.

Namespace: `SharedKernel.Contracts.Mapping` for discoverability.

#### Why this is needed

Every service boundary that converts `Result<T>` to `Envelope<T>` today writes inline `if (result.IsSuccess) Envelope<T>.Ok(result.Value!) else Envelope<T>.Fail(result.Error!)`. This pattern is duplicated across every typed HTTP client method, controller action, and gRPC server handler — potentially hundreds of sites across the ecosystem. Any evolution of `Result<T>` or `Envelope<T>` (e.g., adding `Error.Description`) requires touching every mapping site. A platform-standard bridge collapses all of these to `result.ToEnvelope()`.

#### Acceptance criteria
- [ ] `SharedKernel.Contracts.Mapping.ResultEnvelopeExtensions` static class added
- [ ] `ToEnvelope<T>(this Result<T>)` extension: success → `Envelope<T>.Ok(result.Value)`; failure → `Envelope<T>.Fail(result.Error)`
- [ ] `ToEnvelope(this Result)` non-generic extension: success → `Envelope.Ok()`; failure → `Envelope.Fail(result.Error)`
- [ ] `ToResult<T>(this Envelope<T>)` extension: `IsSuccess` → `Result<T>.Success(envelope.Value!)`; `!IsSuccess` → `Result<T>.Failure(envelope.Error!)`
- [ ] `ToResult(this Envelope)` non-generic extension: analogous mapping
- [ ] All four extensions are pure — no allocations beyond the output type; no side effects; no logging
- [ ] `SharedKernel.Contracts` acquires no new NuGet dependencies from this addition
- [ ] XML doc on all extension methods with usage examples in the summary
- [ ] Unit tests: round-trip `Result<T>.Success` → `ToEnvelope` → `ToResult` preserves value; round-trip `Result<T>.Failure` → `ToEnvelope` → `ToResult` preserves error; non-generic variants tested analogously; `Envelope<T>.Ok` → `ToResult` → success; `Envelope<T>.Fail` → `ToResult` → failure preserving error
---

---
### P-167 — Governance: Architecture Rules for WO-026 Communication Quality Improvements

**Status:** `●` Complete
**Work Order:** WO-026
**Domain:** 00.Governance
**Depends on:** P-163, P-165, P-166

#### What is needed

Extend `SharedKernel.ArchitectureTests` and `00.Governance/CLAUDE.md` to lock in the WO-026 architectural decisions and prevent regression:

**Rule 1 — `SharedKernel.Communication.Grpc` must not reference `SharedKernel.Contracts`.**
P-163 removes this dead reference. This NetArchTest rule locks that state permanently — if the reference is accidentally re-added, the architecture test suite fails immediately.

**Rule 2 — Governance CLAUDE.md `What Goes Where` additions.**
Add table entries for:
- "`Result<T>` → `Envelope<T>` boundary mapping extension" → `04.Contracts/SharedKernel.Contracts` via `ResultEnvelopeExtensions.ToEnvelope()` / `ToResult()`
- "`PagedResponseType<T>` from a `PagedList<T>` source" → `11.Communication.GraphQL` via `PagedResponseType<T>.FromPagedList(pagedList)`
- "Endpoint resolution cache TTL" → `11.Communication.Internal` via `K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds`
- "Multiple typed REST clients using service discovery" → `11.Communication.Rest` via inline factory pattern (not shared `ServiceDiscoveryResolvingHandler` DI type)

**Rule 3 — Document inline `Result<T>` → `Envelope<T>` mapping as a platform violation.**
Add to `00.Governance/CLAUDE.md` and to a governance convention note: "Inline `if (result.IsSuccess) Envelope<T>.Ok(...) else Envelope<T>.Fail(...)` at service boundaries is a platform violation — use `result.ToEnvelope()` from `SharedKernel.Contracts.Mapping`. The mechanical enforcement Roslyn rule is tracked as future SK0xxx."

#### Why this is needed

P-145 (WO-023) and P-159 (WO-025) demonstrated that documentation-only rules regress. The Grpc/Contracts dead reference is a concrete example of drift that cannot be detected by reading code. The `What Goes Where` additions are the standard WO closeout step that encodes architectural decisions for future developers who were not present for this review.

#### Acceptance criteria
- [ ] NetArchTest rule: `SharedKernel.Communication.Grpc` assembly has no dependency on `SharedKernel.Contracts` assembly; rule documented with P-163 rationale
- [ ] Four `What Goes Where` entries added to root `CLAUDE.md` for the patterns established in WO-026
- [ ] Platform violation documented for inline `Result<T>` → `Envelope<T>` mapping; future SK0xxx analyzer noted in backlog
- [ ] Full architecture test suite passes with 0 violations on the WO-026 codebase
- [ ] `00.Governance/CLAUDE.md` changelog entry added for WO-026
---

---
### P-168 — Testing: Communication Package Test Infrastructure Consolidation

**Status:** `○` Pending
**Work Order:** WO-026
**Domain:** 16.Testing
**Depends on:** P-160, P-162

#### What is needed

Add three reusable Communication test helpers to `SharedKernel.Testing`, consolidating ad-hoc test doubles currently duplicated across `SharedKernel.Communication.Rest.Tests` and `SharedKernel.Communication.Grpc.Tests`:

**Helper 1 — `MockServiceEndpointResolver`.**
An in-memory `IServiceEndpointResolver` test double that maps service names to `Uri` values. Exposes: `Configure(string serviceName, Uri uri)` for per-service setup; `GetResolvedNames()` returning all service names resolved so far (for assertion); `ResolveAsync` never throws. Supports configuring a per-service exception to simulate resolver failures. Enables REST and gRPC typed-client tests to inject a controllable resolver without a live DNS or K8s cluster.

**Helper 2 — `FakeHttpContextAccessor`.**
A simple `IHttpContextAccessor` test double that holds a fixed `HttpContext` (or null) with a configurable `TenantId` on its backing `ITenantProvider`. Used by `TenantIdDelegatingHandler` and `TenantIdInterceptor` tests to inject controlled tenant context without a real ASP.NET Core host. Currently implemented ad-hoc in multiple test files — consolidate into `SharedKernel.Testing`.

**Helper 3 — `HttpClientHandlerTestFactory`.**
A factory that creates a pre-wired `DelegatingHandler` chain without a full `ServiceCollection`. Exposes: `WithInnerHandler(HttpMessageHandler)` (the test double at the bottom of the chain), `WithCorrelationIdHandler()`, `WithTenantIdHandler(Guid? tenantId)`. Returns the outermost handler for direct use in `HttpClient` construction in tests. This eliminates the `ServiceCollection`-based wiring boilerplate in handler unit tests.

**Scope constraint:** `SharedKernel.Testing` must not reference `SharedKernel.Communication.Rest`, `.Grpc`, `.GraphQL` as project references. `MockServiceEndpointResolver` may reference `SharedKernel.Communication.Internal` for `IServiceEndpointResolver`. `FakeHttpContextAccessor` references `Microsoft.AspNetCore.Http` and `SharedKernel.Security.Abstractions`. `HttpClientHandlerTestFactory` references `Microsoft.Extensions.Http` only.

#### Why this is needed

The existing test files implement their own `FakeHttpContext`, `FakeTenantProvider`, and resolver variants. This duplication was appropriate during initial package implementation but should be consolidated now that all four Communication packages are stable. Shared test doubles reduce per-test boilerplate, enforce consistent test isolation patterns, and make test failures easier to diagnose by eliminating per-project variation in test setup code.

#### Acceptance criteria
- [ ] `MockServiceEndpointResolver` added to `SharedKernel.Testing`; implements `IServiceEndpointResolver`; `Configure`, `GetResolvedNames`, `ResolveAsync` methods present; never throws; supports failure injection per service name
- [ ] `FakeHttpContextAccessor` added to `SharedKernel.Testing`; implements `IHttpContextAccessor`; configurable `TenantId`; null `HttpContext` supported
- [ ] `HttpClientHandlerTestFactory` added to `SharedKernel.Testing`; creates handler chain without `ServiceCollection`; supports `WithInnerHandler`, `WithCorrelationIdHandler`, `WithTenantIdHandler` fluent configuration
- [ ] `SharedKernel.Testing` does not gain project references to `SharedKernel.Communication.Rest`, `.Grpc`, or `.GraphQL`
- [ ] Ad-hoc duplicates in `SharedKernel.Communication.Rest.Tests` and `SharedKernel.Communication.Grpc.Tests` removed in favour of the new shared helpers
- [ ] All new helpers carry XML doc comments
- [ ] Unit tests for the helpers themselves: `MockServiceEndpointResolver` — configured service returns correct Uri; unknown service does not throw; failure injection propagates; `FakeHttpContextAccessor` — null HttpContext returns null; `HttpClientHandlerTestFactory` — handler chain executes in correct order
---

---
### P-169 — ServiceDefaults: Scaffold — Project Structure and Solution Registration

**Status:** `◐` Dispatched
**Work Order:** WO-027
**Domain:** 13.ServiceDefaults
**Depends on:** None

#### What is needed

Stand up real project structure for both packages so every subsequent phase has assemblies to build against. `SharedKernel.ServiceDefaults` gains project references to `SharedKernel.Primitives` (01.Core), `SharedKernel.Caching.Abstractions` (02.Caching), `SharedKernel.Persistence.Abstractions` (06.Persistence), `SharedKernel.Messaging.Abstractions` (07.Messaging), and the package references for `OpenTelemetry.*`, `Microsoft.Extensions.Diagnostics.HealthChecks`, and `Microsoft.Extensions.Hosting`. `SharedKernel.MultiTenancy` gains project references to `SharedKernel.Security.Abstractions` (12.Security), `SharedKernel.Persistence.Abstractions` (06.Persistence), and a package reference for `Microsoft.AspNetCore.Http.Abstractions`. Both gain nested `.Tests` projects referencing `SharedKernel.Testing` (16.Testing), following the standard test-project nesting rule.

Folder structure inside each package mirrors the public-surface grouping already documented in `13.ServiceDefaults/CLAUDE.md` (`Extensions/`, `HealthChecks/`, `Telemetry/`, `Probes/` for `SharedKernel.ServiceDefaults`; `Resolution/`, `Middleware/`, `Extensions/` for `SharedKernel.MultiTenancy`) — exact file names remain the domain planner's call.

#### Why this is needed

Both `.csproj` files currently contain only `TargetFramework`/`Nullable`/`ImplicitUsings` — zero project or package references. None of the three already-queued phases (P-010, P-122, P-132) or the brain's documented public surface can be implemented without real references to the abstractions they wrap. Every other domain in this platform began with an explicit Scaffold phase before Core implementation; ServiceDefaults skipped straight to a brain write-up last session and needs this foundational step before code can land.

#### Acceptance criteria
- [ ] `SharedKernel.ServiceDefaults.csproj` references `SharedKernel.Primitives`, `SharedKernel.Caching.Abstractions`, `SharedKernel.Persistence.Abstractions`, `SharedKernel.Messaging.Abstractions`, `OpenTelemetry.Extensions.Hosting`, `Microsoft.Extensions.Diagnostics.HealthChecks`
- [ ] `SharedKernel.MultiTenancy.csproj` references `SharedKernel.Security.Abstractions`, `SharedKernel.Persistence.Abstractions`, `Microsoft.AspNetCore.Http.Abstractions`
- [ ] `SharedKernel.ServiceDefaults.Tests` and `SharedKernel.MultiTenancy.Tests` projects created, nested inside their respective package folders, referencing `SharedKernel.Testing`
- [ ] Both projects registered in `Platform.SharedKernel.slnx` under the `13.ServiceDefaults` solution folder
- [ ] `dotnet build` clean across both new project trees with zero implementation code (empty namespaces compile)
- [ ] `13.ServiceDefaults/CLAUDE.md` and `13.ServiceDefaults/state-map.md` changelog updated
---

---
### P-170 — ServiceDefaults: Core — AddServiceDefaults, Liveness/Readiness Split, StartupGate, OpenTelemetry Wiring

**Status:** `◐` Dispatched
**Work Order:** WO-027
**Domain:** 13.ServiceDefaults
**Depends on:** P-169

#### What is needed

The foundational composition entry points exactly as already specified in `13.ServiceDefaults/CLAUDE.md`'s Interface Contracts section — this phase is the implementation of that existing design, not a new design pass:

- `AddServiceDefaults(this IHostApplicationBuilder)` — the mandatory first call in every microservice's `Program.cs`. Wires OpenTelemetry (tracing, metrics, logging via OTLP exporter using the standard `OTEL_EXPORTER_OTLP_ENDPOINT`/`_PROTOCOL` env vars — no SharedKernel-specific config keys) and the base health check infrastructure.
- `AddSharedKernelHealthChecks(this IServiceCollection)` plus the two endpoint mappings (`/health/live`, `/health/ready`) with the hard tag split: `"live"` checks process-alive only and must never depend on an external system; `"ready"` checks gate load-balancer routing and may depend on DB/cache/broker connectivity.
- `AddSharedKernelTelemetry(this IHostApplicationBuilder, string serviceName)` — `ResourceBuilder` with service name + assembly version; ASP.NET Core, HttpClient, and (conditionally, when EF Core is referenced) EF Core instrumentation into `TracerProvider`; runtime + ASP.NET Core instrumentation into `MeterProvider`.
- `StartupGate` (singleton, volatile-backed `IsReady`/`MarkReady()`) and `StartupGateHealthCheck` (tagged `"ready"`, registered automatically and unconditionally by `AddServiceDefaults()` — the only health check that is not opt-in, since it carries no dependency-specific coupling).

This phase does **not** include any dependency-specific health check (Redis, DB, RabbitMQ, ASB) — those remain scoped to P-010 and P-122, which depend on this phase landing first.

#### Why this is needed

This is the load-bearing foundation every other ServiceDefaults phase (P-010, P-122, P-132) and every microservice's `Program.cs` builds on top of. The liveness/readiness tag split is, per the domain's own brain, "the central design invariant" of this package — getting it right here, once, in the base infrastructure prevents every downstream health check phase from having to re-litigate the distinction. Without this phase landing first, P-010 and P-122's `IHealthChecksBuilder` extension methods have no `AddSharedKernelHealthChecks()` base to extend.

#### Acceptance criteria
- [ ] `AddServiceDefaults()` wires OTLP-exporting tracing, metrics, and logging; registers base health check infrastructure; is documented as the mandatory first call in `Program.cs`
- [ ] `/health/live` endpoint reports only process-alive signal — verified by a test that registers a deliberately-failing `"ready"`-tagged check and confirms `/health/live` still reports Healthy
- [ ] `/health/ready` endpoint reports the aggregate of all `"ready"`-tagged checks, independent of `"live"`-tagged ones
- [ ] `StartupGate.IsReady` defaults to `false`; `MarkReady()` is idempotent (callable multiple times without toggling state back or throwing)
- [ ] `StartupGateHealthCheck` reports Unhealthy before `MarkReady()`, Healthy after; tagged `"ready"`; registered automatically by `AddServiceDefaults()` with zero additional configuration
- [ ] `AddSharedKernelTelemetry()` is exposed independently of `AddServiceDefaults()` for services needing a custom `serviceName`
- [ ] No dependency-specific (Redis/DB/RabbitMQ/ASB) health check is registered by this phase
- [ ] `13.ServiceDefaults/CLAUDE.md` changelog and `13.ServiceDefaults/state-map.md` Phase: Core tasks updated
---

---
### P-171 — ServiceDefaults: MultiTenancy Core — Resolution Strategies, AmbientTenantProvider, TenantResolutionMiddleware

**Status:** `◐` Dispatched
**Work Order:** WO-027
**Domain:** 13.ServiceDefaults
**Depends on:** P-169

#### What is needed

The `SharedKernel.MultiTenancy` public surface exactly as specified in `13.ServiceDefaults/CLAUDE.md`: `ITenantResolutionStrategy` (returns `Task<Guid?>`, null meaning "this strategy does not apply, try the next one" — never throws for an absent tenant signal); the three concrete strategies `HeaderTenantResolutionStrategy` (configurable header name, default `X-Tenant-Id`), `ClaimTenantResolutionStrategy` (thin delegating adapter to `SharedKernel.Security.Oidc.OidcTenantProvider` — must not reimplement claim parsing), and `DatabaseTenantResolutionStrategy` (parameterized tenant-directory lookup via `IDbConnectionFactory`, keyed by request host/subdomain); `TenantResolutionOptions` (Options-pattern POCO, default `StrategyOrder = ["Header", "Claim", "Database"]`); `AmbientTenantProvider` (scoped `ITenantProvider` implementation, `TenantId` defaults to `Guid.Empty`, private setter); `TenantResolutionMiddleware` (runs the configured strategy order, sets `AmbientTenantProvider.TenantId` from the first non-null result, must run after `UseAuthentication()`); and `AddSharedKernelMultiTenancy(IServiceCollection, Action<TenantResolutionOptions>?)` for DI registration.

#### Why this is needed

Multi-tenant SaaS services across the platform currently have no shared, composable tenant-resolution story — each team would otherwise hand-roll header parsing, claim extraction, or tenant-directory lookups independently, with no consistent fallback ordering or fail-safe-to-empty-tenant behavior. This package gives every multi-tenant service a single `AddSharedKernelMultiTenancy()` call with pluggable, ordered resolution strategies, while explicitly avoiding duplication of claim-parsing logic that already lives correctly in `12.Security.Oidc`.

#### Acceptance criteria
- [ ] `ITenantResolutionStrategy.TryResolveAsync` never throws for a not-applicable request; returns `null` to signal "try next strategy"
- [ ] `HeaderTenantResolutionStrategy`: present + parseable header → resolved Guid; absent or malformed header → `null` (never throws)
- [ ] `ClaimTenantResolutionStrategy` delegates to `OidcTenantProvider` for all claim parsing — zero duplicated claim-name string literals outside `SecurityClaimTypes`
- [ ] `DatabaseTenantResolutionStrategy` uses parameterized queries exclusively — no string-interpolated or concatenated SQL with request-derived values
- [ ] `TenantResolutionOptions.StrategyOrder` defaults to `["Header", "Claim", "Database"]`; omitting a strategy name from the order means it is never invoked
- [ ] `AmbientTenantProvider.TenantId` defaults to `Guid.Empty`; setter is private; set exactly once per request by `TenantResolutionMiddleware`
- [ ] `TenantResolutionMiddleware`: first non-null strategy result wins; zero resolving strategies leaves `TenantId` at `Guid.Empty` without throwing; documented as required to run after `UseAuthentication()`
- [ ] `AddSharedKernelMultiTenancy()` registers `TenantResolutionOptions` via the Options pattern, `AmbientTenantProvider` as scoped `ITenantProvider`, and the configured strategy set
- [ ] `13.ServiceDefaults/CLAUDE.md` changelog and `13.ServiceDefaults/state-map.md` Phase: Core tasks updated
---

---
### P-172 — Messaging: SharedKernel.Messaging ActivitySource and Consume/Publish Instrumentation

**Status:** `◐` Dispatched
**Work Order:** WO-027
**Domain:** 07.Messaging
**Depends on:** None

#### What is needed

A static `ActivitySource("SharedKernel.Messaging", "1.0.0")` added to `SharedKernel.Messaging.MassTransit`. `ConsumerBase<TMessage>.Consume()` starts a child `Activity` named `"Consumer.Consume"` from this source, tagged `messaging.message_type = typeof(TMessage).Name`, and enriches its structured log scope with `messaging.destination` (from `ConsumeContext.DestinationAddress?.AbsolutePath`) and `messaging.message_type`. `MassTransitEventPublisher.PublishAsync()` starts a child `Activity` named `"EventPublisher.Publish"` from the same source, tagged `messaging.event_type = typeof(TEvent).Name`.

#### Why this is needed

This was originally assumed to already exist by the pending P-132 ServiceDefaults phase (which only intended to wire an *existing* source into the host's `TracerProvider`/`MeterProvider`). It does not exist — there is no `ActivitySource` anywhere in `07.Messaging` today. Creating custom instrumentation is a `07.Messaging`-owned concern (the domain that owns `ConsumerBase`/`MassTransitEventPublisher`), not a `13.ServiceDefaults` concern — `13.ServiceDefaults` only wires already-existing sources into the host, per its own brain's explicit rule ("`13.ServiceDefaults` never creates an `ActivitySource` or custom meter on behalf of another domain"). Splitting this out corrects a cross-domain phase violation that was latent in P-132 and unblocks it with a true dependency instead of a false assumption.

#### Acceptance criteria
- [ ] Static `ActivitySource("SharedKernel.Messaging", "1.0.0")` defined once in `SharedKernel.Messaging.MassTransit`
- [ ] `ConsumerBase<TMessage>.Consume()` starts a child `Activity` named `"Consumer.Consume"` tagged with `messaging.message_type`
- [ ] `MassTransitEventPublisher.PublishAsync()` starts a child `Activity` named `"EventPublisher.Publish"` tagged with `messaging.event_type`
- [ ] `ConsumerBase<TMessage>.Consume()` log scope enriched with `messaging.destination` and `messaging.message_type`
- [ ] Unit test: `Consume()` produces an `Activity` from the `"SharedKernel.Messaging"` source with the correct tag value
- [ ] Unit test: `PublishAsync()` produces an `Activity` from the `"SharedKernel.Messaging"` source with the correct tag value
- [ ] `dotnet build` clean; `07.Messaging/CLAUDE.md` changelog updated documenting the new `ActivitySource` and its consumption by `13.ServiceDefaults.WithMessagingTelemetry()` (P-132)
---

---
### P-173 — Governance: ServiceDefaults Liveness/Readiness and Composition-Root Layering Rules

**Status:** `◐` Dispatched
**Work Order:** WO-027
**Domain:** 00.Governance
**Depends on:** P-170

#### What is needed

Two new architecture enforcement rules added to `00.Governance/SharedKernel.ArchitectureTests`:

**Rule 1 — Liveness/readiness tag integrity.** A NetArchTest-backed (or reflection-over-`HealthCheckRegistration`-backed) rule asserting that no `IHealthCheck` registered by any `Add*HealthCheck`/`Add*ReadinessCheck` extension in `SharedKernel.ServiceDefaults` ever carries both the `"live"` and `"ready"` tags simultaneously, and that every dependency-specific check (Redis, database, RabbitMQ, Azure Service Bus, cache) carries `"ready"` and never `"live"`. This makes the domain's own stated "central design invariant" mechanically enforced rather than relying on code review discipline.

**Rule 2 — Composition-root exclusivity restated for ServiceDefaults' full provider set.** `P-009`'s `SharedKernelLayeringRules` Rule 1 currently only covers `02.Caching` concrete providers. Extend (or add a sibling rule alongside) it so that no production assembly other than `SharedKernel.ServiceDefaults`/`SharedKernel.MultiTenancy` themselves may reference concrete provider packages from `06.Persistence` (`.EfCore`, `.PostgreSQL`, `.Dapper`), `07.Messaging` (`.MassTransit`), or `12.Security` (`.Oidc`) — mirroring the existing Caching rule so the "13.ServiceDefaults is the only composition root permitted to reference concrete providers" exception, already documented as a hard rule in `13.ServiceDefaults/CLAUDE.md`, is mechanically enforced for every provider family it covers, not just Redis.

#### Why this is needed

`13.ServiceDefaults/CLAUDE.md` already states this composition-root exception as a hard rule and explicitly claims it is "mechanically enforced by `00.Governance`'s `SharedKernelLayeringRules`" — but that rule (P-009) was scoped only to caching providers when it was written for WO-003, before `13.ServiceDefaults` existed as a real package. The claim in the brain is currently aspirational, not actual, for the persistence/messaging/security provider families. Closing this gap before `13.ServiceDefaults` ships its first NuGet package prevents the same silent-coupling drift this platform has already paid down once in caching (P-009) and once in Redis topology (P-145) — `05.Application` or any domain/contract package quietly taking a dependency on `SharedKernel.Persistence.EfCore` or `SharedKernel.Messaging.MassTransit` directly, bypassing abstractions, with nothing to catch it.

#### Acceptance criteria
- [ ] NetArchTest (or equivalent reflection-based) rule fails when any registered `IHealthCheck` in `SharedKernel.ServiceDefaults`'s own extension methods carries both `"live"` and `"ready"` tags
- [ ] Rule fails when a dependency-specific check (Redis/DB/RabbitMQ/ASB/cache) is registered without the `"ready"` tag, or with the `"live"` tag
- [ ] `SharedKernelLayeringRules` extended so no production assembly other than `SharedKernel.ServiceDefaults`/`SharedKernel.MultiTenancy` and the concrete provider packages themselves references `SharedKernel.Persistence.EfCore`, `.PostgreSQL`, `.Dapper`, `SharedKernel.Messaging.MassTransit`, or `SharedKernel.Security.Oidc`
- [ ] Both rules documented in `00.Governance/CLAUDE.md` with rationale and the exemption list (the composition-root packages themselves)
- [ ] Full governance test suite passes with both new rules included
---

---
### P-174 — Testing: ServiceDefaults Test Doubles — Tenant Resolution and Health Check Assertions

**Status:** `○` Pending
**Work Order:** WO-027
**Domain:** 16.Testing
**Depends on:** P-170, P-171

#### What is needed

Three reusable test helpers added to `SharedKernel.Testing`:

**Helper 1 — `StaticTenantProvider`.** A trivial `ITenantProvider` test double constructed with a fixed `Guid` (or `Guid.Empty` for the no-tenant case). Used by any downstream domain's tests (Persistence, Application, Communication) that need a deterministic tenant context without standing up `AmbientTenantProvider` + middleware + HTTP context.

**Helper 2 — `FakeTenantResolutionStrategy`.** A configurable `ITenantResolutionStrategy` test double — constructed with either a fixed `Guid?` result or a delegate, so `TenantResolutionMiddleware`/`TenantResolutionOptions.StrategyOrder` behavior can be unit tested without real HTTP headers, claims, or a database.

**Helper 3 — `HealthCheckAssertionExtensions`.** Assertion helpers over `IHealthChecksBuilder`/`HealthCheckRegistration` for verifying tag composition in unit tests without booting a `WebApplicationFactory` — e.g. `ShouldBeTaggedReady(this HealthCheckRegistration)`, `ShouldNotBeTaggedLive(this HealthCheckRegistration)`. These give consuming-service test suites (and `13.ServiceDefaults`'s own test suite) a shared, readable way to assert the liveness/readiness invariant without hand-rolling tag-list assertions per test.

**Scope constraint:** `SharedKernel.Testing` must not take a project reference to `SharedKernel.ServiceDefaults` or `SharedKernel.MultiTenancy` themselves — `StaticTenantProvider`/`FakeTenantResolutionStrategy` reference only `SharedKernel.Security.Abstractions`; `HealthCheckAssertionExtensions` references only `Microsoft.Extensions.Diagnostics.HealthChecks`.

#### Why this is needed

Every other infrastructure domain in this platform (Caching, Messaging, Domain, Contracts) shipped a `16.Testing` phase alongside its core implementation so that both its own test suite and every downstream consumer's test suite have a sanctioned, shared test double rather than ad-hoc per-project fakes. `13.ServiceDefaults` is no exception, and is in fact higher-leverage here: `ITenantProvider` and tenant-aware health check tags are consumed by nearly every other domain's test suite (Persistence multi-tenancy tests, Application pipeline behavior tests), so a shared `StaticTenantProvider` prevents the same N-times-duplicated fake this platform already had to consolidate once for Communication (P-168).

#### Acceptance criteria
- [ ] `StaticTenantProvider` added to `SharedKernel.Testing`; implements `ITenantProvider`; constructed with a fixed `Guid`
- [ ] `FakeTenantResolutionStrategy` added; implements `ITenantResolutionStrategy`-shaped contract (or a structurally compatible delegate-based double if `ITenantResolutionStrategy` itself is not referenced to avoid a project reference to `SharedKernel.MultiTenancy`); configurable fixed result or delegate
- [ ] `HealthCheckAssertionExtensions` added with `ShouldBeTaggedReady`/`ShouldNotBeTaggedLive` (or equivalently named) assertion helpers over `HealthCheckRegistration`
- [ ] `SharedKernel.Testing` does not gain a project reference to `SharedKernel.ServiceDefaults` or `SharedKernel.MultiTenancy`
- [ ] All new helpers carry XML doc comments
- [ ] Unit tests for the helpers themselves: `StaticTenantProvider` returns the configured Guid; `FakeTenantResolutionStrategy` returns configured result or delegate output; `HealthCheckAssertionExtensions` correctly passes/fails against tagged and untagged `HealthCheckRegistration` fixtures
---
