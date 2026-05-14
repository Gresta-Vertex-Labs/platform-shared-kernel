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
| [01.Core](01.Core/state-map.md) | Design | Define Result/Error/ValidationResult/IClock/SmartEnum type shapes in SharedKernel.Primitives with railway extensions and BCL utilities in SharedKernel.Core |

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
| 00 | [Governance](00.Governance/state-map.md) | — | `○` | — | — |
| 01 | [Core](01.Core/state-map.md) | Design | `◐` | — | Define Result/Error/ValidationResult/IClock/SmartEnum type shapes in SharedKernel.Primitives with railway extensions and BCL utilities in SharedKernel.Core |
| 02 | [Caching](02.Caching/state-map.md) | — | `○` | — | — |
| 03 | [Domain](03.Domain/state-map.md) | — | `○` | — | — |
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
| ● Published | 0 |
| ● Docs | 0 |
| ● Tests | 0 |
| ● Core | 0 |
| ● Scaffold | 0 |
| ● Design | 0 |
| ◐ In Progress | 1 |
| ⚑ Blocked | 0 |
| ○ Not Started | 17 |

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

**Status:** `◐` Dispatched
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

**Status:** `◐` Dispatched
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

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} ({domain(s) affected}) — {trigger}`.

- [2026-05-14] Root state-map template created — `/sync-brain`
- [2026-05-14] P-001, P-002 written for WO-001 — 01.Core SharedKernel.Primitives and SharedKernel.Core design — arch-lead
- [2026-05-14] 01.Core → Design (◐) — Define Result/Error/ValidationResult/IClock/SmartEnum and railway extensions (state-map-phase)
- [2026-05-14] Phase(s) P-001, P-002 dispatched to core-arch-planner for 01.Core (dispatch-phase)
