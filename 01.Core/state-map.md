# 01.Core — State Map

> **What this file is:** Phase and task tracker for all work within `01.Core`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.01.{Phase}` to propagate that milestone to the root state-map.

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
| `SK.01.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.01.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.01.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.01.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.01.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.01.Published` | Published | All tasks in Phase: Published are `●` |

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement Result<T> type | SK.01.Core | SharedKernel.Primitives | ◐ |
-->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| C-10 FeatureManagement adapter | SK.01.Core | Waiting for Microsoft.FeatureManagement AOT verdict |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
|---------|--------------|:-----:|-------|
| `SharedKernel.Primitives` | — | `○` | Zero external dependencies |
| `SharedKernel.Core` | — | `○` | References Primitives |
| `SharedKernel.Configuration` | — | `○` | References Primitives |
| `SharedKernel.FeatureManagement` | — | `○` | References Primitives |

---

## Phase: Design <!-- phase-key: SK.01.Design -->

> Finalize all type shapes, interface contracts, and DI extension signatures before any implementation begins.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-01 | Define `Result<T>` and `Result` type shape (sealed class, implicit operators, access rules) | SharedKernel.Primitives | `○` |
| D-02 | Define `Error` sealed record shape, `ErrorType` enum variants, and factory method signatures | SharedKernel.Primitives | `○` |
| D-03 | Define `IClock` interface and `SystemClock` implementation contract | SharedKernel.Primitives | `○` |
| D-04 | Define `SmartEnum<TEnum, TValue>` abstract base shape — factory methods, List, AOT lookup strategy | SharedKernel.Primitives | `○` |
| D-05 | Define base exception hierarchy (SharedKernelException, DomainException, ValidationException, NotFoundException, ConflictException, UnauthorizedException) | SharedKernel.Core | `○` |
| D-06 | Define `Result<T>` railway extension method signatures (.Map, .MapError, .Bind, .Match, .Tap, async overloads) and void `Match` on non-generic `Result` | SharedKernel.Core | `○` |
| D-07 | Define BCL extension method surface (string, IEnumerable<T>, DateTimeOffset, Guid) | SharedKernel.Core | `○` |
| D-08 | Define `AddValidatedOptions<TOptions>` DI extension signature and startup-validation contract | SharedKernel.Configuration | `○` |
| D-09 | Define `IFeatureManager` interface and `FeatureDefinition` record shape | SharedKernel.FeatureManagement | `○` |
| D-10 | Confirm `Microsoft.FeatureManagement` NuGet version and AOT compatibility status | SharedKernel.FeatureManagement | `○` |
| D-11 | Define `ValidationResult` and `ValidationResult<T>` sealed record shapes — multi-error pair, distinct from `Result<T>` | SharedKernel.Primitives | `○` |
| D-12 | Define `ErrorCodes` static class structure — nested static category classes, well-known string constants | SharedKernel.Primitives | `○` |

---

## Phase: Scaffold <!-- phase-key: SK.01.Scaffold -->

> Wire up .csproj NuGet references, intra-domain project references, folder structure, solution registration, and empty test stubs — no logic yet.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| S-01 | Add `Microsoft.Extensions.Options.DataAnnotations` NuGet ref to `SharedKernel.Configuration.csproj` | SharedKernel.Configuration | `○` |
| S-02 | Add `Microsoft.FeatureManagement` NuGet ref to `SharedKernel.FeatureManagement.csproj` | SharedKernel.FeatureManagement | `○` |
| S-03 | Add `SharedKernel.Primitives` project reference to `SharedKernel.Core.csproj` | SharedKernel.Core | `○` |
| S-04 | Add `SharedKernel.Primitives` project reference to `SharedKernel.Configuration.csproj` | SharedKernel.Configuration | `○` |
| S-05 | Add `SharedKernel.Primitives` project reference to `SharedKernel.FeatureManagement.csproj` | SharedKernel.FeatureManagement | `○` |
| S-06 | Create folder structure (`Results/`, `Errors/`, `Clocks/`, `Enums/`) in `SharedKernel.Primitives` | SharedKernel.Primitives | `○` |
| S-07 | Create folder structure (`Exceptions/`, `Extensions/`) in `SharedKernel.Core` | SharedKernel.Core | `○` |
| S-08 | Create folder structure (`Options/`, `Extensions/`) in `SharedKernel.Configuration` | SharedKernel.Configuration | `○` |
| S-09 | Create folder structure (`Abstractions/`, `Extensions/`) in `SharedKernel.FeatureManagement` | SharedKernel.FeatureManagement | `○` |
| S-10 | Register all four projects in `Platform.SharedKernel.slnx` under solution folder `01.Core` | All | `○` |
| S-11 | Stub empty `.Tests` projects with xUnit package reference for all four packages | All | `○` |

---

## Phase: Core <!-- phase-key: SK.01.Core -->

> Full implementation of all types, interfaces, extensions, and DI registrations.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| C-01 | Implement `Result<T>` (sealed class) and `Result` (non-generic) with Success/Failure factories and implicit operators | SharedKernel.Primitives | `○` |
| C-02 | Implement `Error` sealed record with `ErrorType` enum and all factory methods | SharedKernel.Primitives | `○` |
| C-03 | Implement `IClock` interface and `SystemClock` (wraps `DateTimeOffset.UtcNow`) | SharedKernel.Primitives | `○` |
| C-04 | Implement `SmartEnum<TEnum, TValue>` abstract base with AOT-safe static list and value lookup | SharedKernel.Primitives | `○` |
| C-05 | Implement `services.AddClock()` DI extension wiring `SystemClock` as singleton | SharedKernel.Primitives | `○` |
| C-06 | Implement base exception hierarchy (all derive from `SharedKernelException`, carry `Error` payload; `ValidationException` accepts `IReadOnlyList<Error>`) | SharedKernel.Core | `○` |
| C-07 | Implement `Result<T>` railway extension methods (.Map, .MapError, .Bind, .Match, .Tap) and void `Match` on non-generic `Result` | SharedKernel.Core | `○` |
| C-08 | Implement async `Task<Result<T>>` railway extension overloads — avoid unnecessary state machines on outer extension | SharedKernel.Core | `○` |
| C-09 | Implement BCL extensions: string (ToSnakeCase, ToCamelCase, ToPascalCase, IsNullOrWhiteSpace), IEnumerable<T> (ToBatches, IsNullOrEmpty, WhereNotNull), DateTimeOffset (ToUnixMilliseconds, StartOfDay, EndOfDay), Guid (IsEmpty) | SharedKernel.Core | `○` |
| C-10 | Implement `AddValidatedOptions<TOptions>` DI extension with `.ValidateDataAnnotations().ValidateOnStart()` | SharedKernel.Configuration | `○` |
| C-11 | Implement `IFeatureManager` abstraction interface and `FeatureDefinition` sealed record | SharedKernel.FeatureManagement | `○` |
| C-12 | Implement `MicrosoftFeatureManagerAdapter` wrapping `Microsoft.FeatureManagement.IFeatureManager` | SharedKernel.FeatureManagement | `○` |
| C-13 | Implement `AddSharedKernelFeatureManagement` DI extension | SharedKernel.FeatureManagement | `○` |
| C-14 | Implement `ValidationResult` (non-generic, `IsValid` + `IReadOnlyList<Error>`) and `ValidationResult<T>` (adds `Value`) sealed records | SharedKernel.Primitives | `○` |
| C-15 | Implement `ErrorCodes` static class with nested category constants (e.g., `ErrorCodes.Validation.Required`, `ErrorCodes.NotFound.Default`) | SharedKernel.Primitives | `○` |

---

## Phase: Tests <!-- phase-key: SK.01.Tests -->

> Unit test coverage for all packages. No integration tests needed — this domain has no external dependencies.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| T-01 | Unit: `Result<T>` — success path, failure path, implicit operators, accessor throws on wrong state | SharedKernel.Primitives.Tests | `○` |
| T-02 | Unit: `Error` — factory methods, record equality, ErrorType discrimination, `Error.None` sentinel | SharedKernel.Primitives.Tests | `○` |
| T-03 | Unit: `SmartEnum` — FromValue hit, FromValue miss (throws), TryFromValue, FromName, List completeness | SharedKernel.Primitives.Tests | `○` |
| T-04 | Unit: `IClock` / `SystemClock` — returns current UTC; verify `FakeClock` usable in tests | SharedKernel.Primitives.Tests | `○` |
| T-05 | Unit: Base exceptions — carry correct `Error`, message propagates, hierarchy verified | SharedKernel.Core.Tests | `○` |
| T-06 | Unit: Railway extensions — Map, MapError, Bind, Match, Tap chains over success and failure paths; void Match on non-generic Result; async variants | SharedKernel.Core.Tests | `○` |
| T-07 | Unit: BCL extensions — string conversions, IEnumerable batching and nullability, DateTimeOffset helpers, Guid.IsEmpty | SharedKernel.Core.Tests | `○` |
| T-08 | Unit: `AddValidatedOptions` — valid config registers without throw; invalid config throws at `IHost.StartAsync()` | SharedKernel.Configuration.Tests | `○` |
| T-09 | Unit: `IFeatureManager` adapter — enabled flag returns true, disabled returns false, context-aware variant | SharedKernel.FeatureManagement.Tests | `○` |
| T-10 | Unit: `ValidationResult` / `ValidationResult<T>` — multi-error collection, `IsValid` semantics, generic `Value` access, distinction from `Result<T>` | SharedKernel.Primitives.Tests | `○` |

---

## Phase: Docs <!-- phase-key: SK.01.Docs -->

> XML doc comments on all public APIs, README with usage examples.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| DO-01 | XML doc all public types and interfaces across all four packages | All | `○` |
| DO-02 | Write `01.Core/README.md` with usage examples for Result, Error, IClock, SmartEnum | All | `○` |
| DO-03 | Document `Result<T>` railway pattern and error-propagation guide with code samples | SharedKernel.Primitives, SharedKernel.Core | `○` |
| DO-04 | Document `AddValidatedOptions` startup-validation pattern with annotated example | SharedKernel.Configuration | `○` |

---

## Phase: Published <!-- phase-key: SK.01.Published -->

> NuGet packaging metadata, pack, publish, and consumer verification.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| P-01 | Add NuGet metadata to all four `.csproj` files (authors, description, version, license) | All | `○` |
| P-02 | Pack and publish `SharedKernel.Primitives` to feed | SharedKernel.Primitives | `○` |
| P-03 | Pack and publish `SharedKernel.Core` to feed | SharedKernel.Core | `○` |
| P-04 | Pack and publish `SharedKernel.Configuration` to feed | SharedKernel.Configuration | `○` |
| P-05 | Pack and publish `SharedKernel.FeatureManagement` to feed | SharedKernel.FeatureManagement | `○` |
| P-06 | Verify dependency graph in a consumer test project (Primitives → Core → Configuration chain) | All | `○` |

---

## Cross-Domain Dependencies

_No active cross-domain dependencies. `01.Core` references nothing._

<!--
Format when active:
| This Phase Key | Needs From Domain | What | Status |
|---------------|------------------|------|--------|
-->

---

## Overall Progress

> Counts updated whenever a task state changes. Total tasks: 54.

| Phase Key | Phase | Total | ● Done | State |
|-----------|-------|:-----:|:------:|:-----:|
| `SK.01.Design` | Design | 12 | 0 | `○` |
| `SK.01.Scaffold` | Scaffold | 11 | 0 | `○` |
| `SK.01.Core` | Core | 15 | 0 | `○` |
| `SK.01.Tests` | Tests | 10 | 0 | `○` |
| `SK.01.Docs` | Docs | 4 | 0 | `○` |
| `SK.01.Published` | Published | 6 | 0 | `○` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-05-14] Sub state-map initialized — phase key registry, all 6 phases scaffolded at ○ (49 tasks total)
- [2026-05-14] P-001 and P-002 processed — added D-11, D-12 (ValidationResult pair and ErrorCodes design tasks), C-14, C-15 (implementation tasks), T-10 (ValidationResult tests); updated D-06 and C-07 to reflect MapError and void Match on non-generic Result; total now 54 tasks
