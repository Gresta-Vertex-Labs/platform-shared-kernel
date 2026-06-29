# 05.Application — State Map

> **What this file is:** Phase and task tracker for all work within `05.Application`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.05.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
| --- | --- |
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition | Root Backlog ID |
| --- | --- | --- | --- |
| `SK.05.Design` | Design | All tasks in Phase: Design are `●` | P-214 (WO-035) |
| `SK.05.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` | P-215 (WO-035) |
| `SK.05.Core` | Core | All tasks in Phase: Core are `●` | P-216, P-217 (WO-035) |
| `SK.05.Tests` | Tests | All tasks in Phase: Tests are `●` | P-218 (WO-035) |
| `SK.05.Docs` | Docs | All tasks in Phase: Docs are `●` | P-219 (WO-035) |
| `SK.05.Published` | Published | All tasks in Phase: Published are `●` | P-219 (WO-035) |

> Root `state-map.md` P-015 (WO-004) — the `ICacheableQuery<TResponse>`/`CachingBehavior` design — is carried forward **verbatim, no redesign** into P-214/P-217 below. WO-035 (P-214–P-219) is the full gold-standard build-out for this domain: command/query vocabulary, the domain-event-to-MediatR bridge, and the seven-step pipeline behavior suite (the original five — Validation/Logging/Metrics/Transaction/Caching — plus two new: Authorization and Idempotency).

---

## Active Work

_Nothing in progress — all 6 phases complete. 05.Application domain fully published._

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| Example blocked task | SK.05.Core | Waiting on upstream decision |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Application` | Published | `●` | NuGet metadata confirmed complete; `dotnet pack` produces `.nupkg`+`.snupkg` with 0 warnings; consumer-verify harness proves full `AddMediatR`+`AddSharedKernelApplication()`+`AddDomainEventHandler<,>` DI chain resolves and executes end to end (command, query, domain-event dispatch) with zero exceptions. Domain complete. |
| `SharedKernel.Application.Behaviors` | Published | `●` | NuGet metadata confirmed complete; `dotnet pack` produces `.nupkg`+`.snupkg` with 0 warnings; consumer-verify harness proves the full seven-behavior `ApplicationBehaviorsBuilder` pipeline resolves and executes end to end (authorized+idempotent command with transaction commit; cacheable query with cache-hit short-circuit) with zero exceptions; all four missing-dependency `Build()` guards (Transaction/Caching/Authorization/Idempotency) confirmed throwing `InvalidOperationException`. Domain complete. |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
| --- | --- | --- | --- |
| `SK.05.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`Result`, `Result<T>`, `Error`) | Available |
| `SK.05.Scaffold` | `01.Core` | `SharedKernel.Core` ProjectReference (`ValidationException`, `Result<T>` railway extensions) | Available |
| `SK.05.Core` | `03.Domain` | `SharedKernel.Domain` ProjectReference (`IDomainEvent`, `IDomainEventDispatcher`) — `MediatRDomainEventDispatcher` fulfils the forward reference recorded in `03.Domain/CLAUDE.md` (P-081) | Available |
| `SK.05.Core` (Behaviors only) | `02.Caching` | `SharedKernel.Caching.Abstractions` ProjectReference (`ICacheService`, `CachePolicy`, `ICacheKeyProvider`) for `CachingBehavior`/`ICacheableQuery<TResponse>` (root `state-map.md` P-015, WO-004) | Available |
| `SK.05.Core` (Behaviors only) | — | `IUnitOfWork` bridge to `06.Persistence.Abstractions.IUnitOfWork` — a composition-root DI registration in the consuming service, never a project reference (`06.Persistence` may reference `05.Application`; never the reverse) | N/A — by design, see `CLAUDE.md` |
| `SK.05.Core` (Behaviors only) | — | `IAuthorizationContext` bridge to `12.Security.Abstractions.IUserContext`/`ITenantProvider` — a composition-root DI registration in the consuming service, never a project reference (`05.Application` layering ceiling is `01–04`; `12.Security` is never referenced) | N/A — by design, see `CLAUDE.md` (P-214) |
| `SK.05.Core` (Behaviors only) | — | Idempotency local seam — deliberately NOT a reference to `07.Messaging.Abstractions.IIdempotencyStore`; this domain owns its own minimal seam interface, mirroring the `IUnitOfWork` precedent exactly | N/A — by design, see `CLAUDE.md` (P-214) |

---

## Phase: Design <!-- phase-key: SK.05.Design -->

> Finalize all interface shapes for the `ICommand`/`IQuery<TResponse>` base contracts, the MediatR-based `IDomainEventDispatcher` bridge, and the full seven-step pipeline behavior suite (Validation, Logging, Metrics, Transaction, Caching, Authorization, Idempotency) before implementation begins. Maps to root `state-map.md` P-214 (WO-035).

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| D-01 | Lock `ICommandBase`/`ICommand`/`ICommand<TResponse>`/`IQuery<TResponse>` contracts exactly as documented in `CLAUDE.md` | WO-035 | SharedKernel.Application | `●` |
| D-02 | Lock `ICommandHandler<>`/`ICommandHandler<,>`/`IQueryHandler<,>` handler-alias contracts (pure `IRequestHandler<,>` aliases) | WO-035 | SharedKernel.Application | `●` |
| D-03 | Lock `IDomainEventHandler<TDomainEvent>`/`DomainEventNotification<TDomainEvent>`/`MediatRDomainEventDispatcher` contracts, fulfilling `03.Domain` P-081 forward reference | WO-035 | SharedKernel.Application | `●` |
| D-04 | Lock `ValidationBehavior<,>`/`LoggingBehavior<,>`/`MetricsBehavior<,>`/`TransactionBehavior<,>` contracts exactly as documented | WO-035 | SharedKernel.Application.Behaviors | `●` |
| D-05 | Carry forward `ICacheableQuery<TResponse>`/`CachingBehavior<,>` contract verbatim from root P-015 (WO-004) — no redesign | WO-035 | SharedKernel.Application.Behaviors | `●` |
| D-06 | Design new `IAuthorizationContext` local seam + `IAuthorizeRequest` marker + `AuthorizationBehavior<,>` — zero `12.Security` reference; short-circuits with `Result.Failure(Error.Unauthorized(...))`, never throws | WO-035 | SharedKernel.Application.Behaviors | `●` |
| D-07 | Design new idempotency local seam (`IIdempotencyKeyProvider`-shaped, mirroring `07.Messaging.Abstractions.IIdempotencyStore`'s `HasProcessedAsync`/`MarkProcessedAsync` shape as this domain's own interface) + `IIdempotentRequest` marker + `IdempotentCommandBehavior<,>` — constrained to `ICommandBase`; zero `07.Messaging` reference | WO-035 | SharedKernel.Application.Behaviors | `●` |
| D-08 | Revise canonical pipeline order to seven steps (Logging → Metrics → Validation → Authorization → Caching → Idempotency → Transaction) with positional rationale documented for every step | WO-035 | SharedKernel.Application.Behaviors | `●` |
| D-09 | Design `ApplicationBehaviorsBuilder.AddAuthorizationBehavior()` / `.AddIdempotencyBehavior()` extensions, each with a `Build()`-time `InvalidOperationException` missing-dependency guard mirroring Transaction/Caching | WO-035 | SharedKernel.Application.Behaviors | `●` |
| D-10 | Refresh `05.Application/CLAUDE.md` to reflect D-01..D-09 as the single source of truth before Scaffold begins | WO-035 | Both | `●` |

---

## Phase: Scaffold <!-- phase-key: SK.05.Scaffold -->

> Wire up `.csproj` NuGet references (`MediatR`, `FluentValidation`), intra-domain and cross-domain project references, folder structure, solution registration, and empty test stubs — no logic yet. Maps to root `state-map.md` P-215 (WO-035).

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| S-01 | Reference `MediatR` 12.4.x and `SharedKernel.Primitives`/`SharedKernel.Core`/`SharedKernel.Domain` in `SharedKernel.Application.csproj` — nothing else | WO-035 | SharedKernel.Application | `●` |
| S-02 | Reference `SharedKernel.Application`, `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Caching.Abstractions`, `MediatR`, `FluentValidation` 11.x in `SharedKernel.Application.Behaviors.csproj` — nothing else | WO-035 | SharedKernel.Application.Behaviors | `●` |
| S-03 | Create folder structure `Messaging/`, `DomainEvents/`, `Extensions/` in `SharedKernel.Application` | WO-035 | SharedKernel.Application | `●` |
| S-04 | Create folder structure `Validation/`, `Logging/`, `Metrics/`, `Transaction/`, `Caching/`, `Authorization/`, `Idempotency/`, `Extensions/` in `SharedKernel.Application.Behaviors` (new `Authorization/` and `Idempotency/` folders) | WO-035 | SharedKernel.Application.Behaviors | `●` |
| S-05 | Confirm both packages registered as solution folders under `05.Application` in `Platform.SharedKernel.slnx` | WO-035 | Both | `●` |
| S-06 | Create empty nested test stub `SharedKernel.Application.Tests` — references `SharedKernel.Testing` + standard test package pins, no test logic | WO-035 | SharedKernel.Application | `●` |
| S-07 | Create empty nested test stub `SharedKernel.Application.Behaviors.Tests` — references `SharedKernel.Testing` + standard test package pins, no test logic | WO-035 | SharedKernel.Application.Behaviors | `●` |
| S-08 | `dotnet build` succeeds for both packages and both test stubs with zero warnings; confirm zero project reference to `06.Persistence`, `07.Messaging`, or `12.Security` anywhere | WO-035 | Both | `●` |

---

## Phase: Core <!-- phase-key: SK.05.Core -->

> Implement all production types: `ICommand`/`ICommand<TResponse>`/`IQuery<TResponse>`, `ICommandHandler<>`/`ICommandHandler<,>`/`IQueryHandler<,>`, `IDomainEventHandler<TDomainEvent>`/`DomainEventNotification<TDomainEvent>`/`MediatRDomainEventDispatcher`, and the full seven-step pipeline behavior suite with their DI builders. Maps to root `state-map.md` P-216 (vocabulary + bridge) and P-217 (behaviors suite), both WO-035.

**Core (Contracts) — `SharedKernel.Application`, maps to P-216:**

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| C-01 | Implement `ICommandBase`, `ICommand`, `ICommand<TResponse>`, `IQuery<TResponse>` exactly to documented contract shapes | WO-035 | SharedKernel.Application | `●` |
| C-02 | Implement `ICommandHandler<>`, `ICommandHandler<,>`, `IQueryHandler<,>` as pure `IRequestHandler<,>` aliases | WO-035 | SharedKernel.Application | `●` |
| C-03 | Implement `IDomainEventHandler<TDomainEvent>` | WO-035 | SharedKernel.Application | `●` |
| C-04 | Implement `DomainEventNotification<TDomainEvent>` (sealed record, `INotification`) | WO-035 | SharedKernel.Application | `●` |
| C-05 | Implement internal `DomainEventNotificationHandler<TDomainEvent>` adapter | WO-035 | SharedKernel.Application | `●` |
| C-06 | Implement `MediatRDomainEventDispatcher` (single ctor `IPublisher`; cached closed-generic dispatch via static `ConcurrentDictionary<Type, Delegate>`, built once per concrete event `Type` — the documented `MakeGenericMethod` exception) | WO-035 | SharedKernel.Application | `●` |
| C-07 | Implement `AddSharedKernelApplication(IServiceCollection)` — registers `IDomainEventDispatcher → MediatRDomainEventDispatcher` (scoped); no `AddMediatR` call | WO-035 | SharedKernel.Application | `●` |
| C-08 | Implement `AddDomainEventHandler<TDomainEvent,THandler>(IServiceCollection)` — closed-generic registration, one call per event type, zero assembly scanning | WO-035 | SharedKernel.Application | `●` |

**Core (Behaviors) — `SharedKernel.Application.Behaviors`, maps to P-217:**

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| C-09 | Implement `ValidationBehavior<,>` — aggregates `IValidator<TRequest>` failures, throws `ValidationException` without calling `next()`, zero-validators no-op | WO-035 | SharedKernel.Application.Behaviors | `●` |
| C-10 | Implement `LoggingBehavior<,>` — Information at start/success with elapsed time, Error + rethrow on exception, no payload logging | WO-035 | SharedKernel.Application.Behaviors | `●` |
| C-11 | Implement `ApplicationDiagnostics` (static `Meter`/`Histogram<double>`) + `MetricsBehavior<,>` — exactly one measurement per request via try/finally | WO-035 | SharedKernel.Application.Behaviors | `●` |
| C-12 | Implement `IUnitOfWork` (local) + `TransactionBehavior<,>` — constrained to `ICommandBase`; calls `next()` then `SaveChangesAsync`, no surrounding try/catch, never inspects `Result` success/failure | WO-035 | SharedKernel.Application.Behaviors | `●` |
| C-13 | Implement `ICacheableQuery<TResponse>` + `CachingBehavior<,>` exactly per root P-015 — `GetOrSetAsync` only; registered after Validation | WO-035 | SharedKernel.Application.Behaviors | `●` |
| C-14 | Implement `IAuthorizationContext` local seam + `IAuthorizeRequest` marker + `AuthorizationBehavior<,>` — zero `12.Security` reference; short-circuits with `Result.Failure(Error.Unauthorized(...))`, never throws; no-marker requests skip the behavior (DI-level, not a runtime branch) | WO-035 | SharedKernel.Application.Behaviors | `●` |
| C-15 | Implement idempotency local seam + `IIdempotentRequest` marker + `IdempotentCommandBehavior<,>` — constrained to `ICommandBase`; zero `07.Messaging` reference; duplicate key short-circuits without invoking `next()` twice | WO-035 | SharedKernel.Application.Behaviors | `●` |
| C-16 | Implement `ApplicationBehaviorsBuilder` — seven `.AddXBehavior()` methods + `.Build()`; registers only opted-in behaviors in the fixed seven-step canonical order regardless of call order; never calls `AddMediatR` | WO-035 | SharedKernel.Application.Behaviors | `●` |
| C-17 | Implement all four documented missing-dependency `Build()` guards (Transaction → `IUnitOfWork`, Caching → `ICacheService`, Authorization → `IAuthorizationContext`, Idempotency → local seam) throwing `InvalidOperationException` with actionable messages | WO-035 | SharedKernel.Application.Behaviors | `●` |

---

## Phase: Tests <!-- phase-key: SK.05.Tests -->

> Unit tests for command/query contract shape, `MediatRDomainEventDispatcher` dispatch semantics, and each pipeline behavior in isolation (validation short-circuit, logging, metrics, transaction commit-after-success, caching stampede protection, authorization short-circuit, idempotency duplicate-suppression). Maps to root `state-map.md` P-218 (WO-035).

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| T-01 | Contract-shape tests for `ICommand`/`ICommand<T>`/`IQuery<T>`/`ICommandHandler<>`/`IQueryHandler<,>` | WO-035 | SharedKernel.Application | `●` |
| T-02 | `MediatRDomainEventDispatcher` dispatch tests — empty-list no-op, single event, multi-type-event (each published as its own closed `DomainEventNotification<T>`), handler-exception propagation | WO-035 | SharedKernel.Application | `●` |
| T-03 | `AddDomainEventHandler<,>` DI resolution test — handler and notification adapter both resolvable after one call | WO-035 | SharedKernel.Application | `●` |
| T-04 | `ValidationBehavior` tests — zero/one-failing/mixed validators | WO-035 | SharedKernel.Application.Behaviors | `●` |
| T-05 | `LoggingBehavior` tests — success and fault paths | WO-035 | SharedKernel.Application.Behaviors | `●` |
| T-06 | `MetricsBehavior` tests — exactly-one-measurement, recorded even on throw | WO-035 | SharedKernel.Application.Behaviors | `●` |
| T-07 | `TransactionBehavior` tests — `SaveChangesAsync` call count, query bypass as a DI-contract test | WO-035 | SharedKernel.Application.Behaviors | `●` |
| T-08 | `CachingBehavior` tests — miss/hit/stampede/command-never-satisfies-marker | WO-035 | SharedKernel.Application.Behaviors | `●` |
| T-09 | `AuthorizationBehavior` tests — unauthorized short-circuit (handler never invoked, `Result.Failure` with `ErrorType.Unauthorized`), authorized pass-through, no-marker skip (DI-contract test) | WO-035 | SharedKernel.Application.Behaviors | `●` |
| T-10 | `IdempotentCommandBehavior` tests — first call invokes handler and records key, duplicate key short-circuits without a second handler invocation, query types never resolve the behavior into their pipeline | WO-035 | SharedKernel.Application.Behaviors | `●` |
| T-11 | Pin standard test package set (`xunit` 2.9.3, `xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.13.0, `coverlet.collector` 6.0.4, `FluentAssertions` 8.4.0, `NSubstitute` 5.3.0); `GlobalUsings.cs` with `global using Xunit;` in both test projects | WO-035 | Both | `●` |
| T-12 | Full test suite green — zero failures, zero skips | WO-035 | Both | `●` |

---

## Phase: Docs <!-- phase-key: SK.05.Docs -->

> Ensure all public types carry XML doc comments. Update `CLAUDE.md` with any implementation-phase discoveries. Write `README.md` for each package. Maps to root `state-map.md` P-219 (WO-035, Docs half).

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| DO-01 | Confirm 100% XML doc coverage on all public types in `SharedKernel.Application` | WO-035 | SharedKernel.Application | `●` |
| DO-02 | Confirm 100% XML doc coverage on all public types in `SharedKernel.Application.Behaviors`, including `AuthorizationBehavior`/`IdempotentCommandBehavior` and their seams/markers | WO-035 | SharedKernel.Application.Behaviors | `●` |
| DO-03 | Write/update `README.md` for `SharedKernel.Application` | WO-035 | SharedKernel.Application | `●` |
| DO-04 | Write/update `README.md` for `SharedKernel.Application.Behaviors` — full seven-behavior `ApplicationBehaviorsBuilder` DI registration example, plus the Authorization/Idempotency local-seam bridging pattern at the composition root | WO-035 | SharedKernel.Application.Behaviors | `●` |

---

## Phase: Published <!-- phase-key: SK.05.Published -->

> Set NuGet metadata, pack, verify manifests, and publish to the internal feed. Maps to root `state-map.md` P-219 (WO-035, Published half).

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| P-01 | Set NuGet package metadata (`PackageId`, version, description, license, repository URL) on `SharedKernel.Application.csproj` | WO-035 | SharedKernel.Application | `●` |
| P-02 | Set NuGet package metadata on `SharedKernel.Application.Behaviors.csproj` | WO-035 | SharedKernel.Application.Behaviors | `●` |
| P-03 | `dotnet pack` both packages — `.nupkg` + `.snupkg`, zero warnings | WO-035 | Both | `●` |
| P-04 | Run/extend consumer-verify harness — prove full DI registration chain resolves with zero exceptions | WO-035 | Both | `●` |
| P-05 | Consumer-verify harness — prove all four missing-dependency `Build()` guards (Transaction/Caching/Authorization/Idempotency) throw `InvalidOperationException` as documented | WO-035 | Both | `●` |
| P-06 | Update this file's Package Board and root `state-map.md` Domain Summary Board to reflect the completed domain | WO-035 | Both | `●` |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
| --- | --- | :---: | :---: | :---: | :---: |
| `SK.05.Design` | Design | 10 | 10 | 0 | `●` |
| `SK.05.Scaffold` | Scaffold | 8 | 8 | 0 | `●` |
| `SK.05.Core` | Core | 17 | 17 | 0 | `●` |
| `SK.05.Tests` | Tests | 12 | 12 | 0 | `●` |
| `SK.05.Docs` | Docs | 4 | 4 | 0 | `●` |
| `SK.05.Published` | Published | 6 | 6 | 0 | `●` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-06-29] Sub state-map initialized — phase key registry, 6 phases scaffolded at `○`, no tasks yet; package board with 2 packages at not-started; Overall Progress table added (all phases 0 total / `○`)
- [2026-06-29] WO-035 dispatched (root P-214–P-219): 57 tasks added across all 6 phases (D-01..D-10, S-01..S-08, C-01..C-17, T-01..T-12, DO-01..DO-04, P-01..P-06) — locks the command/query vocabulary, the domain-event-to-MediatR bridge fulfilling `03.Domain` P-081, and the full seven-step pipeline behavior suite: the original five (Validation/Logging/Metrics/Transaction/Caching — Caching carrying forward root P-015/WO-004 verbatim, no redesign) plus two new behaviors, Authorization (`IAuthorizationContext` local seam + `IAuthorizeRequest` marker, zero `12.Security` reference) and Idempotency (local seam mirroring `07.Messaging.Abstractions.IIdempotencyStore`'s shape, `IIdempotentRequest` marker, constrained to `ICommandBase` only, zero `07.Messaging` reference); revised canonical pipeline order to Logging → Metrics → Validation → Authorization → Caching → Idempotency → Transaction; Phase Key Registry Root Backlog ID column populated (P-214 through P-219); Package Board states moved from `○` to `◐`; Overall Progress counts populated
- [2026-06-29] D-01 → ● in SK.05.Design — all ten Design tasks verified against CLAUDE.md, design fully locked, no gaps found (state-map-phase)
- [2026-06-29] S-01..S-08 → ● in SK.05.Scaffold — NuGet/project refs wired (MediatR 12.4.1, FluentValidation 11.11.0, SharedKernel.Primitives/Core/Domain/Caching.Abstractions), folder structure created, both packages confirmed in slnx, nested test stubs created with standard pins + GlobalUsings.cs, zero-warning build verified, zero forbidden references confirmed (state-map-phase)
- [2026-06-29] C-01..C-17 → ● in SK.05.Core — full command/query vocabulary, domain-event-to-MediatR bridge (`MediatRDomainEventDispatcher` with cached `MakeGenericMethod` dispatch, the one documented reflection exception), and all seven pipeline behaviors (Validation/Logging/Metrics/Transaction/Caching/Authorization/Idempotency) plus `ApplicationBehaviorsBuilder` with four missing-dependency guards implemented; `AuthorizationBehavior`/`IdempotentCommandBehavior` construct `Result`/`Result<T>` failure responses via `FailureResponseFactory`; both packages build with 0 warnings/0 errors; zero forbidden references confirmed (application-phase-implementer)
- [2026-06-29] `FailureResponseFactory` revised before Tests phase — replaced `dynamic`/`Microsoft.CSharp` with a cached `Expression.Lambda(...).Compile()` per closed `TResponse` type (static `ConcurrentDictionary<Type, Func<Error, object>>`); `Microsoft.CSharp` package reference removed from `SharedKernel.Application.Behaviors.csproj`; this is now the second documented `MakeGenericMethod`/reflection-prohibition exception (built via `Expression` compilation, not `dynamic`), mirroring `MediatRDomainEventDispatcher`; rebuilt clean, 0 warnings/0 errors; CLAUDE.md Technology Stack/AOT sections updated to match (design review)
- [2026-06-29] T-01..T-12 → ● in SK.05.Tests — 18 `SharedKernel.Application.Tests` (contract-shape, `MediatRDomainEventDispatcher` dispatch semantics via a hand-written recording `IPublisher` double, `AddDomainEventHandler<,>`/`AddSharedKernelApplication` DI resolution) + 32 `SharedKernel.Application.Behaviors.Tests` (one class per behavior using a real `ServiceCollection`+`AddMediatR` pipeline; stampede protection proven via a purpose-built semaphore-backed `ICacheService` double since `16.Testing`'s `FakeCacheService` deliberately does not enforce concurrency; `ApplicationBehaviorsBuilder` guard + fixed-order tests) — 50/50 passing, 0 failures, 0 skips; standard test package pins and `GlobalUsings.cs` confirmed already correct from Scaffold (state-map-phase)
- [2026-06-29] DO-01..DO-04 → ● in SK.05.Docs — enabled `GenerateDocumentationFile`/`TreatWarningsAsErrors` plus full NuGet metadata on both csproj files (mirroring 06.Persistence/07.Messaging convention); fixed one broken `cref` in `DomainEventNotificationHandler` and added `<inheritdoc/>` to all seven behaviors' `Handle()` overrides to clear CS1591; both packages build 0 warnings/0 errors; `README.md` written for both packages (quick-start DI, full seven-behavior `ApplicationBehaviorsBuilder` example, local-seam bridging pattern); 50/50 tests still passing (application-phase-implementer)
- [2026-06-29] P-01..P-06 → ● in SK.05.Published — confirmed both csproj already carried complete, matching NuGet metadata from the Docs phase (no changes needed); added `ConsumerVerifyTests.cs` to both nested test projects mirroring `07.Messaging`'s pattern: `SharedKernel.Application.Tests` proves the real `AddMediatR`+`AddSharedKernelApplication()`+`AddDomainEventHandler<,>` chain resolves and executes a command, a query, and a domain-event dispatch end to end with zero exceptions (4 new tests, 22 total); `SharedKernel.Application.Behaviors.Tests` proves the full seven-behavior `ApplicationBehaviorsBuilder` pipeline (Logging→Metrics→Validation→Authorization→Caching→Idempotency→Transaction) resolves and executes an authorized+idempotent command (handler invoked once, idempotency key marked, `IUnitOfWork.SaveChangesAsync` committed) and a cacheable query (second call served from cache) end to end, plus re-confirms all four missing-dependency `Build()` guards throw `InvalidOperationException` (7 new tests, 39 total); discovered `LoggingBehavior<,>` requires `ILogger<TRequest>` resolvable for the full pipeline to build — harness registers `services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))`; both packages `dotnet pack` clean (0 warnings) producing `.nupkg`+`.snupkg`; 61/61 tests green; Package Board and root Domain Summary Board updated — 05.Application domain fully complete (application-phase-implementer)
