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
| `SK.05.Design` | Design | All WO-036 design tasks are `●` | P-220, P-221, P-222, P-223, P-224 (WO-036) |
| `SK.05.Scaffold` | Scaffold | All WO-036 scaffold tasks are `●` | P-220–P-224 (WO-036) |
| `SK.05.Core` | Core | All WO-036 core tasks are `●` | P-220–P-224 (WO-036) |
| `SK.05.Tests` | Tests | All WO-036 test tasks are `●` | P-220–P-224 (WO-036) |
| `SK.05.Docs` | Docs | All WO-036 docs tasks are `●` | P-220–P-224 (WO-036) |
| `SK.05.Published` | Published | All WO-036 published tasks are `●` | P-220–P-224 (WO-036) |

> Root `state-map.md` P-015 (WO-004) — the `ICacheableQuery<TResponse>`/`CachingBehavior` design — is carried forward **verbatim, no redesign** into P-214/P-217 below. WO-035 (P-214–P-219) is the full gold-standard build-out for this domain: command/query vocabulary, the domain-event-to-MediatR bridge, and the seven-step pipeline behavior suite (the original five — Validation/Logging/Metrics/Transaction/Caching — plus two new: Authorization and Idempotency).
>
> WO-036 (P-220–P-224) extends the published seven-step pipeline with: distributed tracing parity (`ApplicationDiagnostics.ActivitySource` + a new `TracingBehavior<,>`), a streaming query vocabulary (`IStreamQuery<TResponse>`/`IStreamQueryHandler<,>` over MediatR's `IStreamRequest<TResponse>`, additive-only, no behavior coverage), an opt-in resilience behavior (`ResilienceBehavior<,>` gated by a new `IRetryableRequest` marker to resolve the retry-after-partial-commit hazard), a reusable pipeline test harness (`SharedKernel.Application.Behaviors.Tests`-internal only), and the write-side counterpart to `CachingBehavior` (`CacheInvalidationBehavior<,>` + `IInvalidatesCache` marker). The pipeline grows from seven to **ten** named slots (Logging → Metrics → Tracing → Validation → Authorization → Caching → Resilience → Idempotency → Transaction → CacheInvalidation). WO-036 reuses the same six phase keys as WO-035 — both work orders' tasks coexist under each phase key in this file.

---

## Active Work

WO-036 (P-220–P-224) Design, Scaffold, and Core phases complete (D-11..D-25, S-09..S-15, C-18..C-29 verified ●). Tests phase partially complete (T-13..T-16 ●; T-17 harness-refactor of the seven WO-035 behavior test files and T-18 full-suite confirmation remain ○).

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
| `SharedKernel.Application` | Design (WO-036) | `◐` | WO-035 scope (vocabulary + domain-event bridge) Published/complete. WO-036 adds the streaming query vocabulary (`IStreamQuery<TResponse>`/`IStreamQueryHandler<,>`) here — additive only, design dispatched, not yet implemented. |
| `SharedKernel.Application.Behaviors` | Design (WO-036) | `◐` | WO-035 scope (seven-behavior pipeline) Published/complete. WO-036 adds `TracingBehavior<,>` (+ `ApplicationDiagnostics.ActivitySource`), `ResilienceBehavior<,>` (+ `IRetryableRequest`), `CacheInvalidationBehavior<,>` (+ `IInvalidatesCache`), and an internal test-only pipeline harness — design dispatched, not yet implemented. Pipeline order grows from seven to ten named slots. |

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
| `SK.05.Core` (WO-036) | — | `System.Diagnostics.ActivitySource`/`Activity` (BCL) for `ApplicationDiagnostics` + `TracingBehavior<,>` — zero new NuGet dependency, mirrors `07.Messaging.MassTransit.Diagnostics.MessagingDiagnostics.ActivitySource` shape exactly | N/A — BCL only, see `CLAUDE.md` (P-220) |
| `SK.05.Core` (WO-036) | `MediatR` (already referenced) | `IStreamRequest<TResponse>`/`IStreamRequestHandler<TRequest,TResponse>` — already part of the pinned `MediatR` 12.4.1 package; no new NuGet dependency | Available — already referenced (P-221) |
| `SK.05.Core` (Behaviors only, WO-036) | — | Polly v8 resilience pipeline (`Microsoft.Extensions.Resilience`/`Polly.Core`) for `ResilienceBehavior<,>` — new NuGet dependency to `SharedKernel.Application.Behaviors`; verify whether already transitive via any existing reference before adding fresh (decision recorded at Design time, confirmed at Scaffold) | To be confirmed at Scaffold (P-222) |
| `SK.05.Core` (Behaviors only, WO-036) | `02.Caching` | `SharedKernel.Caching.Abstractions.ICacheService.RemoveAsync`/`RemoveByTagAsync` for `CacheInvalidationBehavior<,>` — same `SharedKernel.Caching.Abstractions` reference `CachingBehavior` already established, no new project reference | Available — already referenced (P-224) |

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
| D-11 | Design `ApplicationDiagnostics.ActivitySource` (static readonly, name `"SharedKernel.Application"`, versioned consistently with the existing `Meter`) — mirrors `07.Messaging.MassTransit.Diagnostics.MessagingDiagnostics.ActivitySource` exactly | WO-036 | SharedKernel.Application.Behaviors | `●` |
| D-12 | Design `TracingBehavior<TRequest,TResponse>` as a distinct sealed behavior (not folded into `MetricsBehavior`) — starts an activity via `ApplicationDiagnostics.ActivitySource.StartActivity(...)` before `next()`, disposes after, tags `request.name`; positioned immediately adjacent to `MetricsBehavior` | WO-036 | SharedKernel.Application.Behaviors | `●` |
| D-13 | Decide and document `TracingBehavior` exact position in the canonical order (immediately after `MetricsBehavior`, both occupying the outermost-but-inside-Logging band) | WO-036 | SharedKernel.Application.Behaviors | `●` |
| D-14 | Design `IStreamQuery<TResponse>` marker over MediatR's `IStreamRequest<TResponse>`, named consistently with `IQuery<TResponse>` | WO-036 | SharedKernel.Application | `●` |
| D-15 | Design `IStreamQueryHandler<TQuery,TResponse>` handler-alias over `IStreamRequestHandler<TRequest,TResponse>`, named consistently with `IQueryHandler<TQuery,TResponse>` | WO-036 | SharedKernel.Application | `●` |
| D-16 | Lock the `Result<T>`-wrapping decision for streamed items: **no wrapping** — `IStreamQuery<TResponse>` yields raw `TResponse` per item via `IAsyncEnumerable<TResponse>`; errors terminate the stream via thrown exception (standard `IAsyncEnumerable` semantics), a documented, deliberate deviation from this domain's `Result`-everywhere convention | WO-036 | SharedKernel.Application | `●` |
| D-17 | Document explicitly that none of the ten pipeline behaviors apply to `IStreamQuery<TResponse>` today — `ValidationBehavior`'s `TRequest : IRequest<TResponse>` constraint does not match `IStreamRequest<TResponse>`; behavior coverage for streaming is an explicit future decision, never silently assumed | WO-036 | SharedKernel.Application | `●` |
| D-18 | Design `IRetryableRequest` marker interface (mirrors `IAuthorizeRequest`/`IIdempotentRequest` shape) — a request opts in only if provably safe to retry; can be implemented by queries (idempotent by definition) or by commands that also implement `IIdempotentRequest` | WO-036 | SharedKernel.Application.Behaviors | `●` |
| D-19 | Design `ResilienceBehavior<TRequest,TResponse>` constrained to `IRetryableRequest` — retry-with-backoff via an externally-registered Polly v8 `ResiliencePipeline`/`ResiliencePipelineProvider`; document whether circuit-breaking is in scope (deferred — retry-with-backoff only this phase) | WO-036 | SharedKernel.Application.Behaviors | `●` |
| D-20 | Document the retry-after-partial-commit hazard resolution explicitly: `ResilienceBehavior` is positioned **outside** (wrapping) `IdempotentCommandBehavior`/`TransactionBehavior` in the pipeline, so a command must additionally implement `IIdempotentRequest` to safely combine retry with mutation — a command implementing `IRetryableRequest` without `IIdempotentRequest` is a documented misuse, not mechanically prevented (no compile-time enforcement across two independent marker interfaces) | WO-036 | SharedKernel.Application.Behaviors | `●` |
| D-21 | Design `ApplicationBehaviorsBuilder.AddResilienceBehavior(ResiliencePipeline pipeline)` (or provider-based overload) with a `Build()`-time `InvalidOperationException` guard if no resilience pipeline is supplied/registered, mirroring the existing four-guard convention | WO-036 | SharedKernel.Application.Behaviors | `●` |
| D-22 | Design `IInvalidatesCache` marker interface (command declares cache key(s)/tag(s) to evict on success), named consistently with `ICacheableQuery<TResponse>` | WO-036 | SharedKernel.Application.Behaviors | `●` |
| D-23 | Design `CacheInvalidationBehavior<TRequest,TResponse>` constrained to `ICommandBase` + `IInvalidatesCache` — calls `ICacheService.RemoveAsync`/`RemoveByTagAsync` only after `next()` succeeds, never on failure/exception; positioned immediately after `TransactionBehavior` (innermost, after commit) so eviction only follows a confirmed commit | WO-036 | SharedKernel.Application.Behaviors | `●` |
| D-24 | Design `ApplicationBehaviorsBuilder.AddCacheInvalidationBehavior()` reusing the existing `ICacheService` missing-dependency guard already written for `.AddCachingBehavior()` (no duplicated guard logic); design the reusable pipeline test harness shape (`SharedKernel.Application.Behaviors.Tests`-internal, depends on D-11..D-21 existing first) | WO-036 | Both | `●` |
| D-25 | Lock the final revised ten-named-slot canonical order — Logging → Metrics → Tracing → Validation → Authorization → Caching → Resilience → Idempotency → Transaction → CacheInvalidation — with full positional rationale for every new step relative to its neighbors (Resilience wraps Idempotency+Transaction so a retry re-runs the full duplicate-check-then-commit unit, not just the commit; CacheInvalidation sits innermost, after Transaction, so eviction only follows a confirmed commit) | WO-036 | SharedKernel.Application.Behaviors | `●` |

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
| S-09 | Confirm/verify whether a Polly v8 resilience package (`Microsoft.Extensions.Resilience` or `Polly.Core`) is already a transitive dependency of `SharedKernel.Application.Behaviors` via any existing reference; if not, add the minimal fresh NuGet reference and document the version pin choice | WO-036 | SharedKernel.Application.Behaviors | `●` |
| S-10 | Create `Tracing/` folder in `SharedKernel.Application.Behaviors` (alongside existing `Validation/`, `Logging/`, `Metrics/`, `Transaction/`, `Caching/`, `Authorization/`, `Idempotency/`) | WO-036 | SharedKernel.Application.Behaviors | `●` |
| S-11 | Create `Streaming/` folder in `SharedKernel.Application` (alongside existing `Messaging/`, `DomainEvents/`, `Extensions/`) | WO-036 | SharedKernel.Application | `●` |
| S-12 | Create `Resilience/` folder in `SharedKernel.Application.Behaviors` | WO-036 | SharedKernel.Application.Behaviors | `●` |
| S-13 | Create `CacheInvalidation/` folder in `SharedKernel.Application.Behaviors` | WO-036 | SharedKernel.Application.Behaviors | `●` |
| S-14 | Create `TestHarness/` (or equivalent internal-only) folder in `SharedKernel.Application.Behaviors.Tests` for the new reusable pipeline test harness — never packaged, test-assembly-internal only | WO-036 | SharedKernel.Application.Behaviors | `●` |
| S-15 | `dotnet build` succeeds for both packages and both test projects with zero warnings after the new folders/references; confirm zero new project reference to `06.Persistence`, `07.Messaging`, or `12.Security` anywhere | WO-036 | Both | `●` |

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

**Core (WO-036 additions) — maps to P-220–P-224:**

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| C-18 | Implement `ApplicationDiagnostics.ActivitySource` (static readonly, `"SharedKernel.Application"`, versioned to match the existing `Meter`) alongside the existing `Meter`/`Histogram<double>` | WO-036 | SharedKernel.Application.Behaviors | `●` |
| C-19 | Implement `TracingBehavior<TRequest,TResponse>` — `using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(...)` before `next()`, tags `request.name = typeof(TRequest).Name`, disposed after `next()` regardless of success/`Result.Failure`/thrown exception | WO-036 | SharedKernel.Application.Behaviors | `●` |
| C-20 | Implement `IStreamQuery<TResponse>` over `IStreamRequest<TResponse>` exactly per D-14/D-16 (no `Result<T>` wrapping) | WO-036 | SharedKernel.Application | `●` |
| C-21 | Implement `IStreamQueryHandler<TQuery,TResponse>` over `IStreamRequestHandler<TRequest,TResponse>` exactly per D-15 | WO-036 | SharedKernel.Application | `●` |
| C-22 | Implement `IRetryableRequest` marker interface | WO-036 | SharedKernel.Application.Behaviors | `●` |
| C-23 | Implement `ResilienceBehavior<TRequest,TResponse>` — constrained to `IRetryableRequest`; retry-with-backoff via injected Polly v8 `ResiliencePipelineProvider`/`ResiliencePipeline`; positioned wrapping `IdempotentCommandBehavior`/`TransactionBehavior` per D-20 | WO-036 | SharedKernel.Application.Behaviors | `●` |
| C-24 | Implement `ApplicationBehaviorsBuilder.AddResilienceBehavior(...)` + `Build()`-time missing-pipeline guard | WO-036 | SharedKernel.Application.Behaviors | `●` |
| C-25 | Implement `IInvalidatesCache` marker interface (command supplies its own cache key(s)/tag(s) to evict, mirroring `ICacheableQuery<TResponse>.CacheKey`'s self-supplied pattern) | WO-036 | SharedKernel.Application.Behaviors | `●` |
| C-26 | Implement `CacheInvalidationBehavior<TRequest,TResponse>` — constrained to `ICommandBase` + `IInvalidatesCache`; calls `ICacheService.RemoveAsync`/`RemoveByTagAsync` only after `next()` succeeds, never on failure or thrown exception | WO-036 | SharedKernel.Application.Behaviors | `●` |
| C-27 | Implement `ApplicationBehaviorsBuilder.AddCacheInvalidationBehavior()` reusing the existing `ICacheService` `Build()`-time guard (no duplicated guard logic) | WO-036 | SharedKernel.Application.Behaviors | `●` |
| C-28 | Update `ApplicationBehaviorsBuilder.Build()` fixed-order registration to the revised nine-step canonical order — Logging → Metrics → Tracing → Validation → Authorization → Caching → Resilience → Idempotency → Transaction → CacheInvalidation (ten named slots; Caching vs. {Resilience, Idempotency, Transaction, CacheInvalidation} remain mutually exclusive by request shape exactly as the prior five/seven were) regardless of `.AddXBehavior()` call order — see `CLAUDE.md` for full positional rationale | WO-036 | SharedKernel.Application.Behaviors | `●` |
| C-29 | Implement the reusable pipeline test harness in `SharedKernel.Application.Behaviors.Tests` — wires a real `ServiceCollection` + `AddMediatR` + a caller-chosen subset of behaviors via `ApplicationBehaviorsBuilder`; supports asserting response shape, thrown exceptions, recorded metrics, and recorded spans (via `ActivityListener`) | WO-036 | SharedKernel.Application.Behaviors | `●` |

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
| T-13 | `TracingBehavior` tests via `ActivityListener` (matching `07.Messaging`'s `OTelInstrumentationTests` pattern) — span recorded on success, on `Result.Failure`, and on thrown exception; span tagged with `request.name` | WO-036 | SharedKernel.Application.Behaviors | `●` |
| T-14 | Streaming vocabulary contract-shape tests — `IStreamQuery<TResponse>`/`IStreamQueryHandler<,>` compile and resolve through MediatR's `IStreamMediator`/`ISender.CreateStream` exactly as a hand-written `IStreamRequestHandler<,>` would | WO-036 | SharedKernel.Application | `●` |
| T-15 | `ResilienceBehavior` tests — transient failure followed by success is retried and ultimately succeeds; a non-`IRetryableRequest` type never resolves this behavior into its pipeline (DI-contract test); exhausted retries surface per the documented design decision | WO-036 | SharedKernel.Application.Behaviors | `●` |
| T-16 | `CacheInvalidationBehavior` tests — successful command triggers exactly one `RemoveAsync`/`RemoveByTagAsync` call per declared key/tag; failed/`Result.Failure` command never calls removal; thrown exception never calls removal; a query type can never satisfy `IInvalidatesCache` (compile-time/contract-shape assertion) | WO-036 | SharedKernel.Application.Behaviors | `●` |
| T-17 | Refactor the existing seven WO-035 behavior test files plus the three new WO-036 behavior test files (Tracing/Resilience/CacheInvalidation) to use the reusable pipeline test harness (built in C-29) where doing so does not reduce test clarity — document per-file any deliberate exception | WO-036 | SharedKernel.Application.Behaviors | `○` |
| T-18 | Full test suite green across both packages — zero failures, zero skips, including the new harness-based tests | WO-036 | Both | `○` |

---

## Phase: Docs <!-- phase-key: SK.05.Docs -->

> Ensure all public types carry XML doc comments. Update `CLAUDE.md` with any implementation-phase discoveries. Write `README.md` for each package. Maps to root `state-map.md` P-219 (WO-035, Docs half).

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| DO-01 | Confirm 100% XML doc coverage on all public types in `SharedKernel.Application` | WO-035 | SharedKernel.Application | `●` |
| DO-02 | Confirm 100% XML doc coverage on all public types in `SharedKernel.Application.Behaviors`, including `AuthorizationBehavior`/`IdempotentCommandBehavior` and their seams/markers | WO-035 | SharedKernel.Application.Behaviors | `●` |
| DO-03 | Write/update `README.md` for `SharedKernel.Application` | WO-035 | SharedKernel.Application | `●` |
| DO-04 | Write/update `README.md` for `SharedKernel.Application.Behaviors` — full seven-behavior `ApplicationBehaviorsBuilder` DI registration example, plus the Authorization/Idempotency local-seam bridging pattern at the composition root | WO-035 | SharedKernel.Application.Behaviors | `●` |
| DO-05 | Confirm 100% XML doc coverage on all new public types (`ApplicationDiagnostics.ActivitySource`, `TracingBehavior`, `IStreamQuery<TResponse>`, `IStreamQueryHandler<,>`, `IRetryableRequest`, `ResilienceBehavior`, `IInvalidatesCache`, `CacheInvalidationBehavior`, new `ApplicationBehaviorsBuilder` methods) | WO-036 | Both | `○` |
| DO-06 | Update `SharedKernel.Application` `README.md` with the streaming query vocabulary usage example | WO-036 | SharedKernel.Application | `○` |
| DO-07 | Update `SharedKernel.Application.Behaviors` `README.md` — full ten-named-slot `ApplicationBehaviorsBuilder` DI registration example, the `IRetryableRequest`+`IIdempotentRequest` combined-safety guidance, and the read/write caching pairing (`ICacheableQuery<TResponse>` + `IInvalidatesCache`) | WO-036 | SharedKernel.Application.Behaviors | `○` |
| DO-08 | Document the reusable pipeline test harness in the Test Rules section, pointing future behavior tests at it explicitly | WO-036 | SharedKernel.Application.Behaviors | `○` |

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
| P-07 | Confirm NuGet package metadata still accurate on both csproj files after WO-036 additions (version bump if this is a breaking/additive public-surface change) | WO-036 | Both | `○` |
| P-08 | `dotnet pack` both packages — `.nupkg` + `.snupkg`, zero warnings | WO-036 | Both | `○` |
| P-09 | Extend consumer-verify harness — prove the new ten-named-slot pipeline resolves end to end (a retryable+idempotent command with eventual success after a transient failure, transaction commit, then cache invalidation; a traced query producing a recorded span) | WO-036 | Both | `○` |
| P-10 | Consumer-verify harness — prove the new `Build()`-time guards (Resilience missing-pipeline, CacheInvalidation reusing the Caching guard) throw `InvalidOperationException` as documented | WO-036 | Both | `○` |
| P-11 | Update this file's Package Board and root `state-map.md` Domain Summary Board to reflect WO-036 completion | WO-036 | Both | `○` |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
| --- | --- | :---: | :---: | :---: | :---: |
| `SK.05.Design` | Design | 25 | 25 | 0 | `●` |
| `SK.05.Scaffold` | Scaffold | 15 | 15 | 0 | `●` |
| `SK.05.Core` | Core | 29 | 29 | 0 | `●` |
| `SK.05.Tests` | Tests | 18 | 16 | 2 | `◐` |
| `SK.05.Docs` | Docs | 8 | 4 | 4 | `◐` |
| `SK.05.Published` | Published | 11 | 6 | 5 | `◐` |

> WO-035 (P-214–P-219) is fully `●` complete and published. WO-036 (P-220–P-224) task rows above are `○` pending — the counts in this table now reflect both work orders combined per phase key.

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
- [2026-06-30] WO-036 dispatched (root P-220–P-224): 49 tasks added across all 6 phases (D-11..D-25, S-09..S-15, C-18..C-29, T-13..T-18, DO-05..DO-08, P-07..P-11), reusing the existing six phase keys (WO-035's tasks remain `●` complete; WO-036's tasks start `○`). Five new capabilities designed: (1) distributed tracing parity — `ApplicationDiagnostics.ActivitySource` (BCL, zero new dependency) + new distinct `TracingBehavior<,>` positioned immediately after `MetricsBehavior`, mirroring `07.Messaging.MassTransit.Diagnostics.MessagingDiagnostics.ActivitySource`'s exact shape; (2) streaming query vocabulary — `IStreamQuery<TResponse>`/`IStreamQueryHandler<,>` over MediatR's already-pinned `IStreamRequest<TResponse>`/`IStreamRequestHandler<,>`, zero new NuGet dependency, explicit locked decision of **no `Result<T>` wrapping** for streamed items (raw `TResponse` per item via `IAsyncEnumerable`, errors terminate the stream via thrown exception) and explicit statement that none of the pipeline behaviors apply to this shape without a future deliberate extension; (3) resilience behavior — new `IRetryableRequest` marker (mirrors `IAuthorizeRequest`/`IIdempotentRequest`) + `ResilienceBehavior<,>` backed by an externally-registered Polly v8 resilience pipeline, resolving the retry-after-partial-commit hazard by positioning Resilience to wrap Idempotency+Transaction (so a retry re-runs the full duplicate-check-then-commit unit) and documenting that `IRetryableRequest` on a command without `IIdempotentRequest` is a documented misuse, not mechanically prevented; (4) reusable pipeline test harness — internal to `SharedKernel.Application.Behaviors.Tests` only, never packaged, depends on Tracing (P-220) and Resilience (P-222) existing first per the phase's own stated dependency; (5) write-side cache invalidation — new `IInvalidatesCache` marker (mirrors `ICacheableQuery<TResponse>`'s self-supplied-key pattern) + `CacheInvalidationBehavior<,>` constrained to `ICommandBase`, calling `ICacheService.RemoveAsync`/`RemoveByTagAsync` only after `next()` succeeds, positioned innermost (after `TransactionBehavior`) so eviction only follows a confirmed commit, reusing `CachingBehavior`'s existing `ICacheService` `Build()`-time guard rather than duplicating it. Canonical pipeline order revised from seven to a ten-named-slot order: Logging → Metrics → Tracing → Validation → Authorization → Caching → Resilience → Idempotency → Transaction → CacheInvalidation (Caching remains mutually exclusive with the {Resilience, Idempotency, Transaction, CacheInvalidation} command-only band, exactly as the prior five/seven-step design). Phase Key Registry rows added mapping WO-036 to the same six phase keys as WO-035; Package Board moved both packages to Design/`◐`; Cross-Domain Dependencies gained four new rows (BCL ActivitySource, already-referenced MediatR streaming types, a to-be-confirmed Polly v8 reference, already-referenced ICacheService); Overall Progress counts updated to reflect WO-035+WO-036 combined per phase key (application-arch-planner)
- [2026-06-30] D-11..D-25 → ● in SK.05.Design — all 15 WO-036 Design tasks verified against CLAUDE.md: ApplicationDiagnostics.ActivitySource, TracingBehavior shape/position, IStreamQuery/IStreamQueryHandler streaming vocabulary (no-Result-wrapping locked, zero behavior coverage documented), IRetryableRequest + ResilienceBehavior (circuit-breaking explicitly deferred, retry-after-partial-commit hazard resolved via positioning), IInvalidatesCache + CacheInvalidationBehavior, AddCacheInvalidationBehavior guard reuse + reusable test harness shape, and the final ten-named-slot canonical order with full step-by-step positional rationale — all fully and consistently documented, no gaps, no code changes needed. Found and fixed one inconsistency in this file itself: three places said the pipeline grows from seven to "nine" steps; corrected to "ten named slots" to match CLAUDE.md's consistent ten-slot documentation throughout. SK.05.Design now 25/25 ● complete (state-map-phase)
- [2026-06-30] S-09..S-15 → ● in SK.05.Scaffold — confirmed via repo-wide search that no Polly package was already referenced anywhere in the platform; added `Polly.Core` 8.7.0 (latest stable, minimal package — no full `Polly` meta-package) as a fresh direct NuGet reference to `SharedKernel.Application.Behaviors.csproj`; created five new empty folders following the WO-035 `.gitkeep` convention — `Tracing/` and `Resilience/` and `CacheInvalidation/` in `SharedKernel.Application.Behaviors`, `Streaming/` in `SharedKernel.Application`, `TestHarness/` in `SharedKernel.Application.Behaviors.Tests`; all four projects (`SharedKernel.Application`, `SharedKernel.Application.Behaviors`, `SharedKernel.Application.Tests`, `SharedKernel.Application.Behaviors.Tests`) build 0 warnings/0 errors in Release (only the pre-existing, inherited NU1903 SQLitePCLRaw advisory remains on both test projects, unchanged from WO-035); confirmed zero `ProjectReference` to `06.Persistence`/`07.Messaging`/`12.Security` in either production csproj. SK.05.Scaffold now 15/15 ● complete (state-map-phase)
- [2026-06-30] C-18..C-29 → ● in SK.05.Core — all twelve WO-036 Core tasks implemented: `ApplicationDiagnostics.ActivitySource` added alongside the existing `Meter`; `TracingBehavior<,>` (Tracing/); `IStreamQuery<TResponse>`/`IStreamQueryHandler<,>` (Streaming/, SharedKernel.Application); `IRetryableRequest` marker + `ResilienceBehavior<,>` resolving a Polly `ResiliencePipelineProvider<string>` keyed by `typeof(TRequest).Name` with fallback to a `DefaultPipelineKey` constant (Resilience/); `IInvalidatesCache` marker + `CacheInvalidationBehavior<,>`, which gates eviction on "next() did not throw" exactly like `TransactionBehavior` — not `Result.IsSuccess`/`IsFailure` — per CLAUDE.md's explicit design (CacheInvalidation/); `ApplicationBehaviorsBuilder` extended with `.AddTracingBehavior()`, `.AddResilienceBehavior()` (with a `ResiliencePipelineProvider<string>` missing-dependency guard), and `.AddCacheInvalidationBehavior()` (reusing the existing `ICacheService` guard, not duplicating it); `Build()` now registers the fixed ten-slot canonical order (Logging → Metrics → Tracing → Validation → Authorization → Caching → Resilience → Idempotency → Transaction → CacheInvalidation) regardless of call order; reusable `PipelineTestHarness` added to `SharedKernel.Application.Behaviors.Tests/TestHarness/` (real `ServiceCollection` + `AddMediatR` + `ApplicationBehaviorsBuilder` wiring, with `ActivityListener` span capture and `MeterListener` measurement capture). Both production packages build 0 warnings/0 errors in Release; zero new forbidden references confirmed. T-13..T-16 → ● in SK.05.Tests ahead of a dedicated Tests-phase dispatch (TracingBehavior span tests, streaming contract-shape tests, ResilienceBehavior retry/exhaustion tests, CacheInvalidationBehavior tests) — all new behaviors needed accompanying tests to prove correctness as they were built; a test-writing bug was caught and fixed: the originally-written `CacheInvalidationBehavior` failure test asserted eviction is suppressed on `Result.Failure`, which contradicts CLAUDE.md's explicit "NEVER calls removal on a thrown exception" (not on `Result.Failure`) design — test corrected to assert invalidation still occurs on `Result.Failure`, only a thrown exception suppresses it. T-17 (refactor the seven WO-035 behavior test files onto the new harness) and T-18 (full-suite confirmation) remain ○. 91 tests passing total (38 in SharedKernel.Application.Tests + 53 in SharedKernel.Application.Behaviors.Tests), 0 failures. SK.05.Core now 29/29 ● complete (state-map-phase)
