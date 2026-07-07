# 05.Application — Application Layer

## What This Domain Is

The MediatR-based CQRS plumbing layer. Every command, query, streaming query, domain-event handler, and cross-cutting pipeline concern (validation, logging, metrics, tracing, transactions, caching, cache invalidation, authorization, idempotency, resilience) in a downstream microservice derives from the contracts defined here. This domain may reference `01.Core`, `02.Caching` (abstractions only), `03.Domain`, and `04.Contracts` — it must never reference `06.Persistence`, `07.Messaging`, `12.Security`, or any concrete infrastructure package.

> **Local-seam bridging is the load-bearing pattern in this domain.** `TransactionBehavior` (`IUnitOfWork`), `AuthorizationBehavior` (`IAuthorizationContext`), and `IdempotentCommandBehavior` (`IIdempotencyKeyStore`) each define a minimal interface owned by this package, never a direct reference to the "real" infrastructure (`06.Persistence`, `12.Security`, `07.Messaging` respectively). The consuming service bridges each local seam to its real implementation at the composition root. This is the same pattern applied three times, not three different patterns.
>
> **WO-036 status (2026-06-30): fully implemented.** `TracingBehavior`, the streaming query vocabulary (`IStreamQuery<TResponse>`/`IStreamQueryHandler<,>`), `ResilienceBehavior`, `CacheInvalidationBehavior`, the reusable pipeline test harness, and all WO-036 tests (T-13..T-18) are `●` complete. Docs/Published phases remain pending — see `05.Application/state-map.md`.
>
> **WO-038 status (2026-07-02): Core and Tests complete.** P-231 (bug fixes), P-232 (contract evolution), P-233 (parallel dispatch + fire-and-forget), and P-234 (streaming pipeline behaviors) are all fully implemented (C-30..C-50 ●) and tested (T-19..T-32 ●, 120 tests passing). Docs/Published phases remain pending — see `05.Application/state-map.md`.
>
> **WO-039 status (2026-07-06): Core and Tests both complete (C-51..C-65 ●, T-33..T-52 ●, 52/52), Docs/Published pending.** A post-ship review found two confirmed defects in already-"Complete" WO-038 code, both now fixed: (1) `FailureResponseFactory`'s `ResultOfTDispatcher<TResponse>` reworked onto `01.Core` P-236's `IFailureFactory<TSelf>` — the interface-discovery/inner-type-unwrapping reflection (`GetInterfaces()`/`GetGenericArguments()`/`MakeGenericType()`/`GetMethod()`/`Invoke()`) is gone; one `MakeGenericMethod`+`CreateDelegate` call remains, disclosed as a second dispatch structurally identical to `MediatRDomainEventDispatcher`'s already-approved one (see "Constructing a generic failure response" below); (2) `FireAndForgetGuardBehavior<,>`'s self-blocking bug is fixed via an internal `AsyncLocal<bool>`-backed trusted-dispatch marker (`FireAndForgetDispatchContext`) — `AddFireAndForgetDispatch()` is now safe to adopt as documented. `MetricsBehavior<,>` now carries an `outcome` tag (P-239, via a new shared `ResponseOutcomeClassifier` also consumed by `LoggingBehavior`); opt-in idempotency response replay shipped (`IIdempotencyResponseStore`, P-242); `ApplicationBehaviorsBuilder.AddDefaultBehaviors()` onboarding preset shipped (P-243). **P-241 real-assembly wiring is now fully shipped, all 4 rule groups**: `ApplicationPipelineRules.BehaviorsNeverReferenceConcreteInfrastructure`/`.NoExistingBehaviorMatchesStreamRequestConstraint`/`.NoHandRolledRetryLoopOutsideResilienceBehavior`, `PipelineOrderAssertion.AssertRegistrationOrder`, `MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag`, and (as of 2026-07-06) `ReflectionGuardRules.NoMakeGenericMethodReflection` all pass against the real `SharedKernel.Application`/`SharedKernel.Application.Behaviors` assemblies. `00.Governance` P-240 shipped its `ReflectionExemptionRegistry` entry for `MediatRDomainEventDispatcher`, unblocking T-40/T-44; before marking them complete, this domain empirically verified (not merely inferred) that `SharedKernel.Application.Behaviors`' own `MakeGenericMethod` call site (`ResultOfTDispatcher<TResponse>.BuildFactory`) needs no second registry entry today — it is excluded from NetArchTest's real-assembly scan by the `.AreNotAbstract()` filter, since a C# `static class` compiles to IL `abstract sealed` (a second, independently-discovered vacuous-pass mechanism alongside the already-documented closure-type invisibility gap — see `00.Governance/CLAUDE.md`'s SK0012 notes). 143/143 tests passing (28 + 115). See `05.Application/state-map.md` for the full task breakdown.
>
> **WO-040 status (2026-07-07): Design phase dispatched, not yet implemented.** A single, self-contained gap-closure capability: opt-in structured request/response payload logging via a new `ILoggableRequest<TResponse>` marker interface (`Logging/`, `SharedKernel.Application.Behaviors`), extending `LoggingBehavior<,>` **in place** — no new pipeline slot, no change to the canonical ten-step order. Closes the gap `LoggingBehavior`'s own documentation has flagged as deliberately deferred since WO-035. Follows the same self-supplied-marker precedent already proven twice in this package (`ICacheableQuery<TResponse>.CacheKey`, `IInvalidatesCache.CacheKeysToInvalidate`) — the request instance supplies its own redacted/loggable field set for both request and response, never a reflection-based property walk. Zero new NuGet dependency (`ILogger.BeginScope`, BCL), zero new reflection. See the Logging section below and `05.Application/state-map.md` D-59..D-65 for the full design.

Philosophy: **MediatR-native. Result-returning. Behavior-composable. Zero infrastructure leakage.**

> **MediatR is a locked-in choice, not a swappable provider.** Unlike `06.Persistence` (EF Core behind `IRepository`) or `07.Messaging` (MassTransit behind `IMessageBus`), there is no `.Abstractions` + `.{Provider}` split here — MediatR's own `IRequest<TResponse>` / `IRequestHandler<TRequest,TResponse>` / `IPipelineBehavior<TRequest,TResponse>` interfaces **are** the abstraction. This package adds platform vocabulary (`ICommand`, `IQuery<TResponse>`, `Result<T>`-returning handler shapes) on top of MediatR, not a second mediator abstraction underneath it.
>
> **`IUnitOfWork` scope.** `SharedKernel.Application.Behaviors` defines its own minimal `IUnitOfWork` (a single `SaveChangesAsync` seam) — **distinct from `SharedKernel.Persistence.Abstractions.IUnitOfWork`** (`06.Persistence`). `05.Application` can never reference `06.Persistence` (layering runs the other direction), so `TransactionBehavior` depends on this local interface; the consuming service bridges it to its real persistence `IUnitOfWork` at the composition root (06.Persistence is permitted to reference 05.Application, so a future persistence work order may also have `EfUnitOfWork` implement this interface directly). Adding a reference from this domain to `06.Persistence.*` to "simplify" this is a hard violation.
>
> **MediatR version pin.** Target `MediatR` 12.4.x as a deliberate ceiling. Version 13 introduced a commercial license (Lucky Penny Software) for organizations above a revenue threshold. Because this package is the mediator dependency for every downstream microservice, staying on the last MIT-licensed major avoids forcing a licensing decision onto every consumer as a side effect of a routine SharedKernel upgrade. Bumping past 12.x is a separate, explicit, platform-wide decision — never an incidental one.

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Application` | `ICommand`, `ICommand<TResponse>`, `IQuery<TResponse>`, `ICommandHandler<>`, `ICommandHandler<,>`, `IQueryHandler<,>`, `IStreamQuery<TResponse>`, `IStreamQueryHandler<,>`, `IDomainEventHandler<TDomainEvent>`, `DomainEventNotification<TDomainEvent>`, `MediatRDomainEventDispatcher` + `MediatRDomainEventDispatcherOptions` (WO-038 parallel dispatch), `IFireAndForgetCommand` (WO-038) — the MediatR vocabulary (including the streaming-query vocabulary, WO-036) and the domain-event-to-MediatR bridge | `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Domain`, `MediatR` |
| `SharedKernel.Application.Behaviors` | `ValidationBehavior<,>`, `LoggingBehavior<,>`, `MetricsBehavior<,>` (+ `outcome` tag via `ResponseOutcomeClassifier`, WO-039), `TracingBehavior<,>`, `TransactionBehavior<,>` + `IUnitOfWork`, `CachingBehavior<,>` + `ICacheableQuery<TResponse>`, `CacheInvalidationBehavior<,>` + `IInvalidatesCache` + `InvalidateOnlyOnSuccess` opt-in flag (WO-038), `AuthorizationBehavior<,>` + `IAuthorizationContext` (multi-requirement, WO-038) / `IAuthorizeRequest` (AllOf/AnyOf, WO-038), `IdempotentCommandBehavior<,>` + idempotency seam/`IIdempotentRequest` + `IIdempotencyResponseStore` opt-in replay seam (WO-039), `ResilienceBehavior<,>` + `IRetryableRequest`, `FireAndForgetGuardBehavior<,>` + `IFireAndForgetDispatcher` + `FireAndForgetOptions` + `FireAndForgetBackgroundConsumer` + `FireAndForgetDispatchContext` trusted-dispatch marker (WO-038; self-blocking bug fixed WO-039 P-238), streaming behaviors: `StreamLoggingBehavior<,>`, `StreamMetricsBehavior<,>`, `StreamTracingBehavior<,>`, `StreamValidationBehavior<,>`, `StreamAuthorizationBehavior<,>` (WO-038, `IStreamPipelineBehavior<,>`), `ApplicationBehaviorsBuilder.AddDefaultBehaviors()` onboarding preset (WO-039), `ILoggableRequest<TResponse>` opt-in structured payload logging marker (WO-040, design-only) — opt-in MediatR pipeline behaviors (ten unary + five streaming) | `SharedKernel.Application`, `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Caching.Abstractions`, `MediatR`, `FluentValidation`, Polly v8 resilience pipeline (see Technology Stack) |

Both packages target `net10.0`, `ImplicitUsings` enabled, `Nullable` enabled. Test sub-folders live inside each project folder (never in a top-level `tests/`). Both production `.csproj` files set `GenerateDocumentationFile=true` and `TreatWarningsAsErrors=true` (matching `06.Persistence`/`07.Messaging` convention) so a missing XML doc comment on any public member (CS1591) or an unresolved `<see cref="..."/>` (CS1574) fails the build — doc coverage is enforced mechanically, not just audited once at Docs phase.

**Microservices always reference `SharedKernel.Application`. `SharedKernel.Application.Behaviors` is opt-in** — reference it only when adopting one or more platform behaviors; a service that hand-rolls its own cross-cutting concerns can depend on `SharedKernel.Application` alone.

---

## Technology Stack

| Concern | Technology | Version |
| --- | --- | --- |
| Mediator | `MediatR` | 12.4.x (last MIT-licensed major — see version-pin callout above) |
| Streaming mediator | `MediatR` `IStreamRequest<TResponse>`/`IStreamRequestHandler<,>`/`ISender.CreateStream` | Same 12.4.x pin — already part of the dependency, no separate version concern (WO-036) |
| Validation | `FluentValidation` | 11.x |
| Metrics | `System.Diagnostics.Metrics` (BCL) | `net10.0` |
| Tracing | `System.Diagnostics.ActivitySource`/`Activity` (BCL) | `net10.0` — zero new NuGet dependency, same BCL namespace as `Metrics` (WO-036) |
| Resilience | Polly v8 resilience pipeline (`Microsoft.Extensions.Resilience` or `Polly.Core` — exact package confirmed at Scaffold, see `state-map.md` S-09) | Pinned to the same major Polly v8 already proven in `11.Communication`'s typed HTTP clients (WO-036) |
| Logging | `Microsoft.Extensions.Logging.Abstractions` | 10.x |
| DI | `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.x |
| Functional core | `Result<T>`, `Error` from `SharedKernel.Primitives`; `ValidationException` from `SharedKernel.Core` | — |
| Generic failure-response construction | `System.Linq.Expressions` (BCL) | `net10.0` — `SharedKernel.Application.Behaviors` only; see `FailureResponseFactory` rule below |

---

## Interface Contracts

### `SharedKernel.Application` — public surface

#### Command / query vocabulary (`Messaging/`)

```text
ICommandBase  (zero-member marker interface)
    NOTE: Pure generic-constraint marker — implemented by both ICommand and ICommand<TResponse> so that
          TransactionBehavior<TRequest,TResponse> (05.Application.Behaviors) can constrain on "any command
          shape" without needing two separate behavior implementations. Never implemented by IQuery<TResponse>.

ICommand  : ICommandBase, IRequest<Result>
    NOTE: Void-returning command — MediatR's IRequest<Result> binds the handler's response type to the
          non-generic Result (01.Core/SharedKernel.Primitives), not void, so callers can branch on
          IsSuccess/IsFailure without an exception.

ICommand<TResponse>  : ICommandBase, IRequest<Result<TResponse>>
    NOTE: Value-returning command — handler returns Result<TResponse>. TResponse is the unwrapped payload
          type (e.g. Guid for a "create" command returning a new ID) — never wrap TResponse in Result
          yourself when declaring the command.

IQuery<TResponse>  : IRequest<Result<TResponse>>
    NOTE: Does NOT implement ICommandBase — TransactionBehavior never applies to queries. Queries that want
          automatic caching additionally implement ICacheableQuery<TResponse> (05.Application.Behaviors;
          see Caching/ below) — typically as ICacheableQuery<Result<TResponse>>, matching the same
          IRequest<Result<TResponse>> contract IQuery<TResponse> already implements.

ICommandHandler<TCommand>  : IRequestHandler<TCommand, Result>
    where TCommand : ICommand

ICommandHandler<TCommand, TResponse>  : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>

IQueryHandler<TQuery, TResponse>  : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>
    NOTE: All three handler interfaces are pure aliases over MediatR's IRequestHandler<,> — zero additional
          members. They exist so handler classes self-document their CQRS role in the class declaration
          (public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, Guid>) instead
          of the less informative IRequestHandler<PlaceOrderCommand, Result<Guid>>.
          Handlers must never return Envelope / Envelope<T> (04.Contracts) — Envelope is a presentation-
          boundary type only (WO-026 P-166/167); this layer returns Result<T> exclusively.
```

#### Streaming query vocabulary (`Streaming/`) — WO-036, implemented

```text
IStreamQuery<TResponse>  : IStreamRequest<TResponse>
    NOTE: Platform-vocabulary counterpart to IQuery<TResponse>, but for streaming reads — built directly on
          MediatR's own IStreamRequest<TResponse> (already part of the pinned MediatR 12.4.x dependency, zero
          new NuGet package). Named consistently with IQuery<TResponse> so a query class self-documents intent.
          DELIBERATE, DOCUMENTED DEVIATION FROM THE RESULT<T> RAILWAY: TResponse here is the RAW per-item
          payload type — IStreamQuery<TResponse> does NOT wrap items in Result<TResponse>, neither per-item
          nor as a terminal wrapper. Streaming semantics (IAsyncEnumerable<T>) differ fundamentally from the
          single-response Result<T> railway used everywhere else in this domain: a stream's natural error
          channel is a thrown exception that terminates enumeration (standard IAsyncEnumerable semantics),
          not a per-item success/failure union. Wrapping every item in Result<TResponse> would force every
          consumer (GraphQL subscription resolvers, SignalR streamed responses, cursor exports) to unwrap on
          every iteration for no benefit, and a single terminal Result<IAsyncEnumerable<T>> cannot represent
          "the stream started fine but item 4000 failed" — exactly the case that matters. This inconsistency
          with the rest of the domain is intentional and explicit, not an oversight.

IStreamQueryHandler<TQuery, TResponse>  : IStreamRequestHandler<TQuery, TResponse>
    where TQuery : IStreamQuery<TResponse>
    NOTE: Pure alias over MediatR's IStreamRequestHandler<,> — zero additional members, exactly mirroring how
          IQueryHandler<,> aliases IRequestHandler<,>. A handler implements
          IAsyncEnumerable<TResponse> Handle(TQuery request, CancellationToken ct) and self-documents its
          streaming-query role (public sealed class ExportOrdersStreamQueryHandler :
          IStreamQueryHandler<ExportOrdersStreamQuery, OrderRow>) instead of the less informative
          IStreamRequestHandler<ExportOrdersStreamQuery, OrderRow>.

STREAMING PIPELINE BEHAVIOR COVERAGE (WO-038, P-234, implemented): five IStreamPipelineBehavior<,>
implementations now cover the streaming path — StreamLoggingBehavior, StreamMetricsBehavior,
StreamTracingBehavior, StreamValidationBehavior, and StreamAuthorizationBehavior — registered via
ApplicationBehaviorsBuilder.AddStreamingBehaviors(). Transaction, Caching, CacheInvalidation,
Idempotency, and Resilience remain explicitly excluded (read-only-by-contract, stream-materialisation
unsound, retry-after-partial-consumption undefined — see Streaming Pipeline Behaviors section below).
The five unary behaviors (Validation, etc.) do NOT apply to IStreamQuery<TResponse> — MediatR treats
unary and streaming requests as two separate generic hierarchies (IRequest<> vs IStreamRequest<>);
this is why the five separate streaming behaviors were needed.
```

#### Domain event bridge (`DomainEvents/`)

```text
IDomainEventHandler<TDomainEvent>  where TDomainEvent : IDomainEvent
    .Handle(TDomainEvent domainEvent, CancellationToken ct)        → Task
    NOTE: TDomainEvent is the raw domain event (03.Domain) — not a MediatR notification. Consuming services
          implement this interface directly; they never implement MediatR's INotificationHandler<> for
          domain events. If a handler needs to cross the service boundary, it must inject
          SharedKernel.Messaging.Abstractions.IEventPublisher (07.Messaging) and publish an integration
          event — this package has no reference to 07.Messaging itself; that wiring happens in the
          consuming service's own handler implementation (see DI Registration below).

DomainEventNotification<TDomainEvent>  (sealed record, implements INotification)  where TDomainEvent : IDomainEvent
    .DomainEvent                                                   → TDomainEvent
    NOTE: The wrapper that makes a plain IDomainEvent publishable through MediatR's IPublisher. IDomainEvent
          itself (03.Domain) cannot implement MediatR's INotification — 03.Domain has zero NuGet
          dependencies and must stay that way. This record is the seam that absorbs the MediatR dependency
          on behalf of the domain layer.

DomainEventNotificationHandler<TDomainEvent>  (internal sealed class, implements INotificationHandler<DomainEventNotification<TDomainEvent>>)
    where TDomainEvent : IDomainEvent
    NOTE: Adapter only — unwraps notification.DomainEvent and forwards to the registered
          IDomainEventHandler<TDomainEvent>. Never registered directly by consuming code; always registered
          via AddDomainEventHandler<TDomainEvent, THandler>() below. Internal — not part of the public
          surface consuming services author against.

MediatRDomainEventDispatcher  (sealed class, implements IDomainEventDispatcher from SharedKernel.Domain)
    constructor: MediatRDomainEventDispatcher(IPublisher publisher)
    .DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken ct)   → Task
    NOTE: Fulfils the forward reference in 03.Domain/CLAUDE.md ("The MediatR-based implementation
          (MediatRDomainEventDispatcher) is a future 05.Application phase" — P-081). Honors the
          IDomainEventDispatcher contract exactly: an empty events list is a no-op; handler exceptions
          propagate unchanged (never caught/logged-and-swallowed here).
          RUNTIME-TYPE DISPATCH (documented exception): events is IReadOnlyList<IDomainEvent> — the
          concrete event type is only known at runtime per element. DispatchAsync resolves a cached
          closed-generic publish delegate from a static ConcurrentDictionary<Type, Delegate>, built once
          per concrete event Type via MethodInfo.MakeGenericMethod the first time that Type is seen, then
          invoked directly on every subsequent dispatch of the same Type. This is the SAME documented,
          justified exception to the platform-wide MakeGenericMethod/Invoke prohibition already used by
          07.Messaging's MassTransitEventPublisher for the identical "publish-by-runtime-type through a
          generic API" problem (see 07.Messaging/CLAUDE.md "MassTransitEventPublisher constraint bridge")
          — not a new, ad-hoc exception. The cached delegate constructs DomainEventNotification<TConcrete>
          and calls IPublisher.Publish(notification, ct).
```

#### Parallel dispatch options (`DomainEvents/`) — WO-038, P-233

```text
MediatRDomainEventDispatcherOptions  (options class)
    .ParallelDispatch                                               → bool  (default false)
    NOTE: When false (default), DispatchAsync publishes events serially in list order — each
          IPublisher.Publish awaited before the next begins. Serial is the safe default: events sharing
          an ordering dependency (e.g. domain-event A must be observed before domain-event B) are
          correctly sequenced.
          When true, DispatchAsync publishes all events concurrently via Task.WhenAll. All events are
          dispatched even if early ones fault — exceptions from concurrent dispatches are collected and
          rethrown as an AggregateException after all dispatches complete (not after the first fault).
          DOCUMENTED CONSTRAINT: parallel dispatch is only valid for independently-observable events with
          no ordering dependency between them. Opting in with ordered events is a documented misuse — not
          mechanically prevented (no ordering declaration on IDomainEvent), so code review must catch it.
          Configured via AddSharedKernelApplication(Action<MediatRDomainEventDispatcherOptions>? configure)
          at the composition root — the parameterless overload retains the serial default.
```

#### DI extensions for `SharedKernel.Application` (`Extensions/`)

```text
AddSharedKernelApplication(IServiceCollection services)            → IServiceCollection
    — Registers IDomainEventDispatcher → MediatRDomainEventDispatcher (scoped) with default serial
      dispatch (MediatRDomainEventDispatcherOptions.ParallelDispatch = false).
    — Does NOT call services.AddMediatR(...) — the consuming service owns MediatR registration and
      assembly scanning (RegisterServicesFromAssembly). This extension only adds the dispatcher bridge.

AddSharedKernelApplication(IServiceCollection services, Action<MediatRDomainEventDispatcherOptions>? configure)
                                                                   → IServiceCollection   (WO-038, P-233)
    — Overload that accepts an optional options delegate, e.g.:
        services.AddSharedKernelApplication(opts => opts.ParallelDispatch = true);
    — When configure is null or omitted, behavior is identical to the parameterless overload (serial
      dispatch, same default). Never breaks existing call sites.

AddDomainEventHandler<TDomainEvent, THandler>(IServiceCollection services)   → IServiceCollection
    where TDomainEvent : IDomainEvent
    where THandler : class, IDomainEventHandler<TDomainEvent>
    — Registers THandler as IDomainEventHandler<TDomainEvent> (scoped).
    — Registers the internal DomainEventNotificationHandler<TDomainEvent> as
      INotificationHandler<DomainEventNotification<TDomainEvent>> (scoped) so MediatR's IPublisher can
      resolve it.
    — One call per domain event type. No assembly scanning, no MakeGenericType at registration time —
      both type arguments are supplied by the caller as ordinary closed generics.
```

---

### `SharedKernel.Application.Behaviors` — public surface

#### Validation (`Validation/`)

```text
ValidationBehavior<TRequest, TResponse>  (sealed class, implements IPipelineBehavior<TRequest, TResponse>)
    where TRequest : IRequest<TResponse>
    NOTE: Injects IEnumerable<IValidator<TRequest>> (FluentValidation). Runs all registered validators
          (Task.WhenAll), aggregates every failure across every validator into IReadOnlyList<Error> via
          Error.Validation(failure.PropertyName, failure.ErrorMessage), then — if the aggregate is
          non-empty — throws SharedKernel.Core's ValidationException(errors) WITHOUT calling next(). This
          is the platform's existing ValidationResult→exception bridge (01.Core), not a new mechanism;
          14.Presentation.WebApi's global exception handler converts it into a single multi-error
          ProblemDetails response. When zero validators are registered for TRequest, next() is called
          immediately — IEnumerable<IValidator<TRequest>> is empty (never null) by DI convention, so this
          is a zero-cost no-op, not a missing-registration error.
          Applies to BOTH commands and queries — TRequest is constrained only to IRequest<TResponse>.
```

#### Logging (`Logging/`)

```text
LoggingBehavior<TRequest, TResponse>  (sealed class, implements IPipelineBehavior<TRequest, TResponse>)
    where TRequest : IRequest<TResponse>
    NOTE: Injects ILogger<TRequest>. Logs Information at start ("Handling {RequestName}"), timed via
          Stopwatch.GetTimestamp/GetElapsedTime (no Stopwatch allocation).
          Post-handler log level is response-aware (WO-038, P-232, depends on P-230/IHasSuccessFlag):
            - Response is IHasSuccessFlag with IsSuccess == false → logs at Warning ("Handled {RequestName}
              with failure in {ElapsedMilliseconds}ms") so operations teams can alert on failure rates
              without sifting through Information noise.
            - Response is IHasSuccessFlag with IsSuccess == true, or response does not implement
              IHasSuccessFlag → logs at Information ("Handled {RequestName} in {ElapsedMilliseconds}ms").
          On exception: logs Error with the exception and elapsed time, then rethrows unchanged — never
          swallows, mirroring 07.Messaging's ConsumerBase.Consume log-then-rethrow contract.
          Does NOT log request or response payloads by default (PII risk in command/query parameters) —
          see the STRUCTURED PAYLOAD LOGGING note below for the opt-in mechanism (WO-040, design-only).
          SHARED CLASSIFICATION HELPER (WO-039, P-239, shipped): the IHasSuccessFlag pattern-match
          driving the Information/Warning decision above is delegated to Shared/ResponseOutcomeClassifier.cs
          (internal, IsSuccess<TResponse>/Classify<TResponse>), the same helper MetricsBehavior's outcome tag
          uses (see Metrics section below) — a pure refactor, zero behavior change, so the two behaviors'
          success/failure classification can never silently diverge.
          STRUCTURED PAYLOAD LOGGING (WO-040, P-246, design-only — not yet implemented, see state-map.md
          D-59..D-65/C-66..C-68): when TRequest implements ILoggableRequest<TResponse> (below), the start
          log entry additionally opens an ILogger.BeginScope(request.LoggableRequestFields) scope (skipped
          when the returned dictionary is null/empty), and the completion log entry additionally opens
          ILogger.BeginScope(loggable.GetLoggableResponseFields(response)) — called ONLY when next() returns
          normally, never on a thrown exception (there is no response to project; the request-side scope
          still applies to the entry log and to the Error-level exception log). This is a PURE ADDITIVE
          branch (`is ILoggableRequest<TResponse>` pattern-match) around the existing log calls — a TRequest
          that does not implement the marker produces byte-for-byte identical logging behavior to today, no
          new scope/tag/level/message-template change of any kind. Not an eleventh pipeline slot and not a
          second logging pathway — LoggingBehavior<,> remains the single place request/response payload data
          is ever written to a log, deliberately extended in place rather than duplicated.

ILoggableRequest<TResponse>  : IRequest<TResponse>   (WO-040, P-246, design-only — not yet implemented)
    .LoggableRequestFields                                        → IReadOnlyDictionary<string, object?>
    NOTE: Self-supplied, exactly mirroring ICacheableQuery<TResponse>.CacheKey's and
          IInvalidatesCache.CacheKeysToInvalidate's precedent: the request instance alone decides which of
          its own fields are safe to log (e.g. an OrderId, never a CreditCardNumber) — LoggingBehavior<,>
          never reflects over TRequest's properties to discover this set. An empty dictionary is valid (opts
          in to the marker but has nothing to say for a given call); no dictionary is ever synthesized by
          the behavior itself.

    .GetLoggableResponseFields(TResponse response)                → IReadOnlyDictionary<string, object?>?
    NOTE: Optional, may return null/empty to opt out of response-side logging while still logging the
          request side. Invoked by LoggingBehavior<,> AFTER next() returns normally — never on a thrown
          exception. TResponse is part of the interface itself (mirroring ICacheableQuery<TResponse>'s
          shape) so a request type is typically declared as ILoggableRequest<Result<TResponse>>, matching
          the exact IRequest<Result<TResponse>> contract ICommand<TResponse>/IQuery<TResponse> already
          implement — no reflection walk over TResponse's own properties either; the request instance
          projects only the subset of the ACTUAL response object it received back that it judges safe to
          log (e.g. a newly-created Guid, never a full entity payload).
```

#### Metrics (`Metrics/`)

```text
ApplicationDiagnostics  (internal static class)
    .Meter                                                         → Meter  (static readonly; "SharedKernel.Application", "1.0.0")
    .RequestDuration                                                → Histogram<double>  ("sharedkernel.application.request.duration", unit "ms")
    .ActivitySource                                                 → ActivitySource  (static readonly; "SharedKernel.Application", "1.0.0") — WO-036, design-only
    NOTE: Same platform-standard static-instrument pattern already approved for 07.Messaging's
          MessagingDiagnostics.ActivitySource — a static readonly Meter/Histogram/ActivitySource carries no
          mutable business state and is the BCL-sanctioned process-lifetime diagnostics shape (same class as a
          static ILogger category name). This is the only sanctioned static state in this domain.
          CLOSED (P-247, 13.ServiceDefaults): consumed by 13.ServiceDefaults.WithApplicationTelemetry(),
          which wires "SharedKernel.Application" into the host's MeterProvider/TracerProvider via
          .AddMeter(...)/.AddSource(...) — 13.ServiceDefaults never constructs this Meter/ActivitySource
          itself; it only registers the already-existing "SharedKernel.Application" name pair with the
          host's MeterProvider/TracerProvider, mirroring 07.Messaging's MessagingDiagnostics.ActivitySource
          / WithMessagingTelemetry() (P-132/P-172) precedent exactly. This domain still never reaches into
          13.ServiceDefaults itself (layering runs the other direction) — WithApplicationTelemetry() is
          13.ServiceDefaults' own composition-root wiring, not a dependency this package takes on.
          ActivitySource is named and versioned identically to Meter (same string "SharedKernel.Application",
          same "1.0.0") so both instruments share one logical diagnostics identity — not two different
          platform names for the same package.

MetricsBehavior<TRequest, TResponse>  (sealed class, implements IPipelineBehavior<TRequest, TResponse>)
    where TRequest : IRequest<TResponse>
    NOTE: Records ApplicationDiagnostics.RequestDuration exactly once per request, tagged with
          request.name = typeof(TRequest).FullName ?? typeof(TRequest).Name (WO-038, P-231 — prevents
          key collisions when two assemblies define a request type with the same short name), via a
          try/finally around next() so the measurement is recorded whether the inner pipeline succeeds,
          returns a Result.Failure, or throws. Same FullName convention applies to LoggingBehavior and
          TracingBehavior tag construction.
          OUTCOME TAG (WO-039, P-239, shipped): every Record(...) call additionally carries an outcome
          tag — "success", "failure" (response is IHasSuccessFlag with IsSuccess == false), or
          "exception" (captured before rethrow, via a pessimistic-default-then-overwrite pattern) —
          mirroring StreamMetricsBehavior's existing "streamed"/"faulted" outcome tag on the streaming
          side. Classification is computed via Shared/ResponseOutcomeClassifier.cs, the same shared
          internal helper LoggingBehavior uses, so the two behaviors' IHasSuccessFlag taxonomy never
          silently diverges.
```

#### Tracing (`Tracing/`) — WO-036, implemented

```text
TracingBehavior<TRequest, TResponse>  (sealed class, implements IPipelineBehavior<TRequest, TResponse>)
    where TRequest : IRequest<TResponse>
    NOTE: A DISTINCT behavior from MetricsBehavior, not folded into it — single-responsibility (one behavior
          measures, one behavior traces), even though both occupy the same outermost-but-inside-Logging band
          and both bracket next() in a try/finally-equivalent shape. Mirrors 07.Messaging's
          ConsumerBase.Consume/MassTransitEventPublisher.Publish shape exactly:
              using var activity = ApplicationDiagnostics.ActivitySource.StartActivity("Request.Handle");
              activity?.SetTag("request.name", typeof(TRequest).FullName ?? typeof(TRequest).Name);
          The activity is started before next() and disposed (via `using`) after, regardless of success,
          Result.Failure, or thrown exception — `using var` guarantees disposal even when next() throws, no
          explicit try/finally needed (StartActivity already returns null if no listener is registered, and
          a null `Activity?` makes `activity?.SetTag` and disposal both safe no-ops — zero allocation cost
          when tracing is not being collected). Participates in the ambient Activity.Current trace context
          exactly as StartActivity already does by BCL default — no custom propagation logic, no manual
          parent-id wiring. Positioned immediately after MetricsBehavior (see Pipeline Composition below).
```

#### Transactions (`Transaction/`)

```text
IUnitOfWork  (interface)
    .SaveChangesAsync(CancellationToken ct)                       → Task<int>
    NOTE: See the "IUnitOfWork scope" callout above — this is NOT
          SharedKernel.Persistence.Abstractions.IUnitOfWork. This package ships only the interface; no
          implementation. Bridging options for the consuming service (both legal under the layering rules
          since 06.Persistence may reference 05.Application):
            (a) SHIPPED (P-228): EfUnitOfWork in 06.Persistence.EfCore additionally implements this
                interface directly. Register via EfCorePersistenceBuilder.WithApplicationTransactionBehavior()
                — both IUnitOfWork interfaces resolve the SAME scoped EfUnitOfWork instance per DI scope.
                No hand-written adapter needed. This is the recommended path.
            (b) The composition root registers a one-line scoped adapter delegating to
                SharedKernel.Persistence.Abstractions.IUnitOfWork (see DI Registration below).
                Use this fallback when not using EfUnitOfWork (e.g., custom persistence provider).

TransactionBehavior<TRequest, TResponse>  (sealed class, implements IPipelineBehavior<TRequest, TResponse>)
    where TRequest : ICommandBase, IRequest<TResponse>
    NOTE: Applies to commands only — ICommandBase is implemented by ICommand/ICommand<TResponse>, never by
          IQuery<TResponse>, so this behavior is simply absent from a query's resolved pipeline (a DI-level
          fact, not a runtime branch). Calls next() then IUnitOfWork.SaveChangesAsync(ct), in that order,
          with no surrounding try/catch: if next() throws, SaveChangesAsync is never reached, so a faulted
          handler can never commit partial state. Deliberately does NOT inspect whether the returned
          Result/Result<T> is success or failure — a Result.Failure is a valid, deliberate handler outcome
          (e.g. "validation passed but the business rule failed before any mutation"), not a fault; whether
          to stage a mutation before returning failure is the handler's own decision. This keeps the
          behavior fully generic over TResponse with zero reflection and no dependency on a shared Result
          marker interface.
```

#### Caching (`Caching/`) — carries forward root `state-map.md` P-015 (WO-004, pending dispatch)

```text
ICacheableQuery<TResponse>  : IRequest<TResponse>
    .CachePolicy                                                  → CachePolicy   (SharedKernel.Caching.Abstractions)
    .CacheKey                                                     → string
    NOTE: Zero-member-beyond-these-two marker; queries implement it to opt in to automatic cache wrapping.
          The query instance supplies its own pre-computed CacheKey because it alone holds the
          discriminating parameters (e.g. an entity ID). ICacheKeyProvider (Caching.Abstractions) is
          available as a DI service for queries that want the platform key format, but calling it is the
          query's responsibility, not the behavior's — this keeps the behavior itself simple. Never
          implemented by a command type; typically declared as ICacheableQuery<Result<TPayload>> so it
          resolves to the exact same IRequest<Result<TPayload>> contract as IQuery<TPayload>.

CachingBehavior<TRequest, TResponse>  (sealed class, implements IPipelineBehavior<TRequest, TResponse>)
    where TRequest : ICacheableQuery<TResponse>
    NOTE: Calls ICacheService.GetOrSetAsync(request.CacheKey, factory, request.CachePolicy, ct) — never
          GetAsync followed by SetAsync — so FusionCache's stampede protection guarantees the factory runs
          exactly once per key under concurrent load (02.Caching contract). ICacheService.GetOrSetAsync's
          factory parameter is Func<CancellationToken, ValueTask<TResponse>> (post Phase-21 breaking
          change in 02.Caching) while MediatR's next() returns Task<TResponse> — the behavior adapts via
          `cacheCt => new ValueTask<TResponse>(next())`, never blocking with .Result/.Wait().
          Must be registered after ValidationBehavior in the pipeline — validation must reject before a
          cache lookup occurs, so invalid requests are never cached (root-level design constraint, see
          Pipeline Composition below).
```

#### Cache Invalidation (`CacheInvalidation/`) — WO-036, implemented; the write-side counterpart to Caching above

```text
IInvalidatesCache  (interface)
    .CacheKeysToInvalidate                                          → IReadOnlyCollection<string>
    NOTE: Marker a COMMAND implements to declare the cache key(s) it renders stale on success, mirroring
          ICacheableQuery<TResponse>.CacheKey's exact self-supplied pattern — the command instance alone
          holds the discriminating parameters (e.g. the entity ID it just mutated), so it computes and
          supplies its own key list; the behavior never derives keys itself. Tag-based eviction
          (IReadOnlyCollection<string> CacheTagsToInvalidate, calling ICacheService.RemoveByTagAsync) is
          available as an optional second property for commands that invalidate a whole tag rather than
          enumerable individual keys — both are supported, neither is required if the other is supplied.
          Never implemented by a query type — this is the write-side half of the read/write caching pair;
          ICacheableQuery<TResponse> remains the read-side half. A type implementing both interfaces
          simultaneously is a contradiction (queries don't mutate, commands aren't cached) and is rejected
          by this domain's existing rule that ICacheableQuery<TResponse> is queries-only.

CacheInvalidationBehavior<TRequest, TResponse>  (sealed class, implements IPipelineBehavior<TRequest, TResponse>)
    where TRequest : ICommandBase, IInvalidatesCache, IRequest<TResponse>
    NOTE: Constrained to ICommandBase ONLY, mirroring TransactionBehavior/IdempotentCommandBehavior's exact
          constraint shape — never applies to queries (a DI-level fact). Calls next() first; only on a
          non-faulted outcome (the inner pipeline returned without throwing — same "did not fault" signal
          TransactionBehavior uses) does it call ICacheService.RemoveAsync(key, ct) once per declared
          CacheKeysToInvalidate entry (and/or RemoveByTagAsync per declared tag).
          DEFAULT STANCE — ALWAYS INVALIDATE ON NON-THROW (documented, not an oversight): a Result.Failure
          from the handler STILL triggers cache eviction in the default mode. This is deliberate: the
          behavior mirrors TransactionBehavior's "do not inspect Result.IsSuccess/IsFailure" principle —
          a Result.Failure is a valid, deliberate handler outcome, and whether to stage a mutation before
          returning failure is the handler's own decision. Operations teams reading the code must not assume
          a Result.Failure prevents eviction; it does not by default.
          OPT-IN STRICT MODE (WO-038, P-231): set InvalidateOnlyOnSuccess = true on the behavior's options
          to additionally gate eviction on IHasSuccessFlag.IsSuccess (requires P-230 IHasSuccessFlag
          available in TResponse). Default is false (always-invalidate-on-non-throw) for backward
          compatibility. This flag does NOT change the thrown-exception path — NEVER calls removal on a
          thrown exception regardless of the flag, since a faulted handler mutated nothing (or its mutation
          never committed, since this behavior runs after TransactionBehavior).
          Zero direct reference to SharedKernel.Caching.FusionCache/SharedKernel.Caching.Redis or any
          concrete provider — SharedKernel.Caching.Abstractions only. Positioned innermost, immediately
          after TransactionBehavior — eviction must follow a CONFIRMED commit, never a speculative one.
```

#### Authorization (`Authorization/`)

```text
IAuthorizationContext  (interface — local seam, owned by this package)
    .IsAuthorizedAsync(string requirement, CancellationToken ct)                   → Task<bool>
    .AllOf(IEnumerable<string> requirements, CancellationToken ct)                 → Task<bool>  (WO-038, P-232)
    .AnyOf(IEnumerable<string> requirements, CancellationToken ct)                 → Task<bool>  (WO-038, P-232)
    NOTE: This is NOT SharedKernel.Security.Abstractions.IUserContext, and this package carries no project
          reference to 12.Security — the layering ceiling for 05.Application is 01–04. Exposes the minimal
          shape AuthorizationBehavior needs.
          MULTI-REQUIREMENT EVOLUTION (WO-038, P-232): AllOf evaluates every requirement and returns true
          only if ALL pass — short-circuits on the first failure. AnyOf evaluates requirements in order and
          returns true as soon as ONE passes — short-circuits on the first success. IsAuthorizedAsync
          remains as the single-requirement convenience (equivalent to AllOf([ requirement ])). Breaking
          change from the original P-217 single-method contract — acceptable since these types are new and
          no downstream consumer has yet adopted them.
          The consuming service bridges this seam to its real IUserContext/ITenantProvider at the
          composition root — the EXACT same bridging pattern TransactionBehavior's local IUnitOfWork
          already established for 06.Persistence.Abstractions.IUnitOfWork. Requirement strings are opaque
          (permission/policy names) whose meaning is entirely owned by the consuming service's bridge
          implementation — this package never interprets them.

IAuthorizeRequest  (interface — WO-038, P-232 evolution)
    .AllOfRequirements                                              → IReadOnlyCollection<string>
    .AnyOfRequirements                                              → IReadOnlyCollection<string>
    .Requirement                                                    → string  (convenience; equivalent to AllOfRequirements = [ Requirement ])
    NOTE: Marker a command or query implements to declare it requires an authorization check before its
          handler runs. MULTI-REQUIREMENT SHAPES (WO-038): AllOfRequirements declares a set where ALL must
          pass (AND composition); AnyOfRequirements declares a set where AT LEAST ONE must pass (OR
          composition). Both collections default to empty — only the non-empty collection(s) are evaluated.
          The original single Requirement string remains as a convenience property (a default-interface-
          member or explicit implementation equivalent to AllOfRequirements = [ Requirement ]). Requests
          that do NOT implement this marker skip AuthorizationBehavior entirely — a DI/runtime fact, not a
          config flag. Can be implemented by both commands and queries — unlike Transaction/Idempotency,
          authorization is not commands-only; queries can require permission checks too.

AuthorizationBehavior<TRequest, TResponse>  (sealed class, implements IPipelineBehavior<TRequest, TResponse>)
    where TRequest : IAuthorizeRequest, IRequest<TResponse>
    NOTE: Evaluates IAuthorizationContext.AllOf(request.AllOfRequirements, ct) first (short-circuit on
          first failure), then IAuthorizationContext.AnyOf(request.AnyOfRequirements, ct) (short-circuit
          on first pass). Both are no-ops on empty collections. On any failure, short-circuits WITHOUT
          calling next() and returns Result.Failure(Error.Unauthorized(...)) — for both Result and Result<T>
          response shapes — NEVER throws. This is consistent with this domain's existing rule that
          exceptions are reserved for ValidationException (the one deliberate behavior short-circuit) and
          genuinely unexpected faults; an unauthorized caller is an expected, foreseeable outcome. On full
          pass, calls next() and returns its result unchanged. Runs after ValidationBehavior and before
          CachingBehavior/TransactionBehavior.
```

#### Idempotency (`Idempotency/`)

```text
IIdempotencyKeyStore  (interface — local seam, owned by this package)
    .HasProcessedAsync(string idempotencyKey, CancellationToken ct)            → Task<bool>
    .MarkProcessedAsync(string idempotencyKey, CancellationToken ct)           → Task
    NOTE: Mirrors the HasProcessedAsync/MarkProcessedAsync shape already proven by
          07.Messaging.Abstractions.IIdempotencyStore — but this is this domain's OWN interface, never a
          direct reference to 07.Messaging (05.Application's layering ceiling is 01–04). The consuming
          service provides the implementation (typically backed by the same distributed store
          07.Messaging's IIdempotencyStore uses, or a dedicated table/cache key) and registers it at the
          composition root. This package ships only the interface — no implementation, exactly like the
          IUnitOfWork precedent.
          FAIL-AND-CONSUME-KEY INVARIANT (WO-038, P-231 — documented, not an oversight):
          MarkProcessedAsync is called after next() even when the handler returns Result.Failure (a
          business-rule failure, not a thrown exception). This means a failed command consumes its
          idempotency key permanently: the client MUST use a new idempotency key to retry after a
          business-rule failure. A thrown exception (fault) does NOT consume the key — MarkProcessedAsync
          is only reached when next() returns normally. This asymmetry is intentional:
            - Fault (exception) → key not consumed, retry with same key is safe.
            - Failure (Result.Failure) → key consumed, retry requires a new key.
          Rationale: a Result.Failure represents a deliberate, foreseeable business outcome that reached
          the handler and was evaluated; retrying with the same key after a business-rule rejection would
          bypass the duplicate-submission guard rather than correct the underlying business condition.

IIdempotencyResponseStore  (interface — local seam, owned by this package; OPTIONAL/additive; WO-039, P-242, shipped)
    .TryGetStoredResponseAsync(string idempotencyKey, CancellationToken ct)     → Task<string?>
    .StoreResponseAsync(string idempotencyKey, string serializedResponse, CancellationToken ct)  → Task
    NOTE: A store MAY implement this interface ALONGSIDE its existing IIdempotencyKeyStore
          implementation to opt in to response replay — the platform's answer to the "client retried
          after an ambiguous network outcome" case idempotency keys exist for, so a duplicate submission
          can return the ORIGINAL outcome instead of Error.Conflict. Purely additive: a store implementing
          ONLY IIdempotencyKeyStore continues to compile and behave exactly as today, unchanged.
          IdempotentCommandBehavior detects this capability via a plain `is IIdempotencyResponseStore`
          runtime pattern-match on the injected IIdempotencyKeyStore instance (a standard .NET
          optional-capability-interface check, not reflection — the same class of check as
          IAsyncDisposable/IDisposable detection). Serialization uses System.Text.Json (in-box on
          net10.0, no new NuGet dependency); the store persists whatever string it is handed and never
          interprets it — this package owns the (de)serialization, the store owns only persistence.

IIdempotentRequest  (interface)
    .IdempotencyKey                                                 → string
    NOTE: Marker a COMMAND implements to opt in to duplicate-submission protection (double-click, client
          retry). Never implemented by a query — queries are already idempotent by definition (a query
          that needs caching uses ICacheableQuery<TResponse> instead, an orthogonal concern). The key is
          supplied by the command instance itself (e.g. a client-generated idempotency token, or a
          deterministic hash of the command's discriminating fields) — this package never generates keys.

IdempotentCommandBehavior<TRequest, TResponse>  (sealed class, implements IPipelineBehavior<TRequest, TResponse>)
    where TRequest : ICommandBase, IIdempotentRequest, IRequest<TResponse>
    NOTE: Constrained to ICommandBase ONLY — mirrors TransactionBehavior's commands-only constraint exactly;
          never applies to queries (a DI-level fact, since IQuery<TResponse> never implements ICommandBase).
          Calls IIdempotencyKeyStore.HasProcessedAsync(request.IdempotencyKey, ct) first. On true (duplicate):
          REPLAY PATH (WO-039, P-242, shipped) — if the injected store ALSO implements
          IIdempotencyResponseStore, calls TryGetStoredResponseAsync and, when a stored value exists,
          deserializes and returns it directly, replaying the ORIGINAL outcome (success or failure)
          exactly as it occurred, never a fresh Error.Conflict. When the store does not support replay,
          or supports it but no stored value exists for this key (e.g. a pre-replay-adoption key),
          short-circuits WITHOUT calling next() a second time and returns Result.Failure(Error.Conflict(...))
          — the documented, still-default behavior (this seam's original P-217 scope boundary — never
          cache/replay the response — is now an explicit, additive opt-in rather than a permanent limit;
          replaying requires a second, generic-serialization concern this behavior did not originally
          take on, and now does, only when the store opts in). On false, calls next(), then on success
          (including Result.Failure — see the fail-and-consume-key invariant above) calls
          MarkProcessedAsync(request.IdempotencyKey, ct) so the key is recorded only after a successful
          handler run, and — when replay is supported — ALSO calls StoreResponseAsync with the
          serialized response (WO-039, P-242, shipped) — a faulted handler must remain safely
          retryable with the same key, so neither call happens on a thrown exception. Runs innermost,
          immediately wrapping the handler-and-commit boundary — positioned after CachingBehavior (queries
          never reach this behavior anyway) and immediately before TransactionBehavior, so a duplicate is
          rejected before TransactionBehavior's SaveChangesAsync would run a second time for the same
          logical operation.
```

#### Resilience (`Resilience/`) — WO-036, implemented

```text
IRetryableRequest  (interface)
    NOTE: Zero-member marker, named consistently with IAuthorizeRequest/IIdempotentRequest. A request opts
          in to retry-with-backoff only if it is PROVABLY SAFE TO RETRY. Can be implemented by:
            - a query (queries are idempotent by definition — re-running a read is always safe), or
            - a command that ALSO implements IIdempotentRequest (retry-safety for a mutation is borrowed
              from idempotency: if a duplicate submission is already detected and rejected by
              IdempotentCommandBehavior, a retried attempt of the same logical command is equally safe).
          THE RETRY-AFTER-PARTIAL-COMMIT HAZARD, RESOLVED: a command implementing IRetryableRequest WITHOUT
          also implementing IIdempotentRequest is a DOCUMENTED MISUSE, not mechanically prevented — there is
          no compile-time way to require "interface A implies interface B" across two independent marker
          interfaces in C#. This is a deliberate, accepted gap (documented here and in the Hard Violations
          section below), not an oversight. The hazard itself is resolved by WHERE ResilienceBehavior sits in
          the pipeline (see below): it wraps IdempotentCommandBehavior + TransactionBehavior, so a retry
          re-runs the FULL duplicate-check-then-commit unit, never just a bare second commit attempt.

ResilienceBehavior<TRequest, TResponse>  (sealed class, implements IPipelineBehavior<TRequest, TResponse>)
    where TRequest : IRetryableRequest, IRequest<TResponse>
    NOTE: Executes next() through an injected Polly v8 resilience pipeline (ResiliencePipelineProvider/
          ResiliencePipeline, resolved by a per-request-type or named-pipeline key — exact resolution
          mechanism finalized at Core implementation, see state-map.md C-23) implementing retry-with-backoff.
          CIRCUIT-BREAKING IS OUT OF SCOPE FOR THIS PHASE — retry-with-backoff only; a circuit-breaker stage
          is a deferred, separately-justified follow-up (this domain's in-process pipeline is not a network
          boundary the way 11.Communication's typed HTTP clients are, so the case for a breaker here is
          weaker and not assumed). Exhausted retries surface as either a thrown exception (if the inner
          pipeline itself only ever throws) or propagate the final Result.Failure unchanged — this behavior
          does not invent a new failure shape; whatever the wrapped pipeline produces on its last attempt is
          what callers see. Configurable per-request-type via the IRetryableRequest marker, never globally
          fixed — a request type that does not implement IRetryableRequest never resolves this behavior into
          its pipeline (a DI-level fact, not a runtime branch), exactly like every other marker-gated
          behavior in this domain.
```

#### Fire-and-Forget Dispatch (`FireAndForget/`) — WO-038, P-233, implemented; self-blocking bug fixed (WO-039, P-238, Core complete 2026-07-06)

> **Fixed: internal dispatch is no longer self-blocked.** `FireAndForgetGuardBehavior<,>`, registered globally by
> `AddFireAndForgetDispatch()`, previously could not distinguish `FireAndForgetBackgroundConsumer`'s own internal
> `ISender.Send` dispatch from an external caller's misuse — both traversed the identical MediatR pipeline against
> the identical service provider, so the guard intercepted and rejected the consumer's own dispatch attempt every
> time, and no fire-and-forget command ever actually executed. The fix: `FireAndForgetDispatchContext`, an
> internal, non-public ambient trusted-dispatch marker (`AsyncLocal<bool>`-backed, no public surface, so it cannot
> be set or spoofed by consuming-service code), which `FireAndForgetBackgroundConsumer` sets only around its own
> internal dispatch call (via a disposable scope, reset on any exit path including handler exception);
> `FireAndForgetGuardBehavior<,>` calls `next()` when the marker is set (letting the rest of the registered
> pipeline — Logging/Metrics/Validation/etc. — still run for the internally-dispatched command) and throws its
> existing, unchanged `InvalidOperationException` otherwise. The external-misuse guard itself is never weakened —
> a caller's direct `ISender.Send(IFireAndForgetCommand)` is still rejected. `AddFireAndForgetDispatch()` is now
> safe to adopt as documented.

```text
IFireAndForgetCommand  (interface, extends ICommand — the void-returning, no-TResponse command shape)
    NOTE: Marker that identifies a command as intended for enqueue-and-forget dispatch via
          IFireAndForgetDispatcher, NOT direct ISender.Send(). A FireAndForgetGuardBehavior<,>
          constrained to IFireAndForgetCommand intercepts any ISender.Send call and throws
          InvalidOperationException with a descriptive message directing the caller to
          IFireAndForgetDispatcher.EnqueueAsync. Lives in SharedKernel.Application (not .Behaviors)
          since it is part of the command vocabulary, not a behavior.

IFireAndForgetDispatcher  (interface — in SharedKernel.Application.Behaviors)
    .EnqueueAsync(IFireAndForgetCommand command, CancellationToken ct)  → ValueTask
    NOTE: Enqueues the command onto a bounded System.Threading.Channels.Channel(IFireAndForgetCommand)
          and returns immediately (the caller does NOT await handler completion). The BackgroundService
          consumer dequeues and executes handlers via IServiceScopeFactory (one scope per command,
          matching ASP.NET Core's scoped-service-per-request pattern). Never blocks the caller's
          request-processing thread.

FireAndForgetOptions  (options class)
    .Capacity                                                           → int  (default 1000)
    .RejectionPolicy                                                    → FireAndForgetRejectionPolicy  (DropAndLog / Block)
    NOTE: Configures the bounded channel. DropAndLog silently drops the command and emits a Warning log
          when the channel is full. Block waits for capacity (can block the caller — use only when the
          caller's thread can afford to wait). Default is DropAndLog.

FireAndForgetBackgroundConsumer  (sealed class, extends BackgroundService)
    NOTE: Reads IFireAndForgetCommand instances from the Channel, resolves a new IServiceScope per
          command, resolves the appropriate IRequestHandler, executes the handler, and catches any
          exception logging at Error level without stopping the consumer loop or the host. Never re-throws
          from the consumer loop — an uncaught exception in a handler must not crash the hosted service.
          FIXED (WO-039, P-238): the internal ISender.Send call is wrapped in
          FireAndForgetDispatchContext.EnterTrustedDispatch()'s disposable scope (set immediately before,
          cleared immediately after via using/try-finally, guaranteed even on handler exception) so
          FireAndForgetGuardBehavior<,> permits it through instead of rejecting it.

FireAndForgetDispatchContext  (internal static class, AsyncLocal<bool>-backed)
    .IsTrusted                                                          → bool  (internal, get-only)
    .EnterTrustedDispatch()                                             → IDisposable
    NOTE: The ambient trusted-dispatch marker (WO-039, P-238). Internal, non-public — cannot be set or
          observed by consuming-service code, so it cannot be used to spoof trusted dispatch and defeat
          FireAndForgetGuardBehavior<,>'s external-misuse protection. Only FireAndForgetBackgroundConsumer
          calls EnterTrustedDispatch(), around its own internal ISender.Send call.

FireAndForgetGuardBehavior<TRequest,TResponse>  (sealed class, IPipelineBehavior<TRequest,TResponse>)
    where TRequest : IFireAndForgetCommand, IRequest<TResponse>
    NOTE: Checks FireAndForgetDispatchContext.IsTrusted before throwing (WO-039, P-238 fix). When set — only
          ever true inside FireAndForgetBackgroundConsumer's own dispatch call — calls next() instead of
          throwing, permitting the rest of the registered pipeline to run. When absent, throws
          InvalidOperationException without calling next() — including for any external caller's direct
          ISender.Send(IFireAndForgetCommand) call, unchanged. Registered as the outermost behavior for
          IFireAndForgetCommand requests so external misuse gets an immediate, actionable error. Message:
          "IFireAndForgetCommand implementations must be dispatched via IFireAndForgetDispatcher.EnqueueAsync,
          not ISender.Send. Register the fire-and-forget dispatcher with
          ApplicationBehaviorsBuilder.AddFireAndForgetDispatch()."
```

#### Streaming Pipeline Behaviors (`StreamingBehaviors/`) — WO-038, P-234, implemented; depends on P-232

```text
NON-APPLICABLE BEHAVIORS (explicit, not an oversight): Transaction, Caching, CacheInvalidation,
Idempotency, and Resilience are NOT applicable to IStreamQuery<TResponse>:
  - Transaction/Idempotency/CacheInvalidation are constrained to ICommandBase; streaming queries are
    read-only by contract and never implement ICommandBase.
  - Caching IAsyncEnumerable<TResponse> is architecturally unsound: materialising the stream to cache it
    defeats the constant-memory guarantee and removes the streaming benefit entirely.
  - Resilience retry on a partially-consumed stream has undefined semantics: the stream position cannot
    be rewound after the consumer has already received and processed items.
These exclusions are documented here AND in the XML docs of each non-implemented streaming interface.

StreamLoggingBehavior<TRequest,TResponse>  (sealed class, IStreamPipelineBehavior<TRequest,TResponse>)
    where TRequest : IStreamRequest<TResponse>
    NOTE: Logs request entry at Information; first-item latency at Debug; stream-completion at Information;
          stream-fault (handler throws) at Warning. Uses ILogger<TRequest>.

StreamMetricsBehavior<TRequest,TResponse>  (sealed class, IStreamPipelineBehavior<TRequest,TResponse>)
    NOTE: Emits ApplicationDiagnostics.RequestDuration from stream-open to stream-close/fault, tagged with
          request.name = typeof(TRequest).FullName ?? typeof(TRequest).Name and outcome = "streamed" /
          "faulted". Exactly one measurement per stream lifetime regardless of item count.

StreamTracingBehavior<TRequest,TResponse>  (sealed class, IStreamPipelineBehavior<TRequest,TResponse>)
    NOTE: Creates an Activity span via ApplicationDiagnostics.ActivitySource.StartActivity("StreamRequest.Handle")
          covering stream-open to stream-close/fault; tags request.name = typeof(TRequest).FullName ?? ...;
          same StartActivity/null-safe pattern as TracingBehavior<,>.

StreamValidationBehavior<TRequest,TResponse>  (sealed class, IStreamPipelineBehavior<TRequest,TResponse>)
    NOTE: Runs IEnumerable<IValidator<TRequest>> validation ONCE at stream-open before the first item is
          yielded; throws ValidationException (same bridge as ValidationBehavior<,>) without opening the
          stream if validation fails; no per-item validation; zero validators is a no-op.

StreamAuthorizationBehavior<TRequest,TResponse>  (sealed class, IStreamPipelineBehavior<TRequest,TResponse>)
    where TRequest : IAuthorizeRequest, IStreamRequest<TResponse>
    NOTE: Evaluates IAuthorizationContext.AllOf/AnyOf (P-232 multi-requirement shape) ONCE at stream-open;
          throws an authorization exception without opening the stream if the check fails. IStreamQuery<T>
          types NOT implementing IAuthorizeRequest never resolve this behavior (DI-level, not a branch).

STREAMING BEHAVIOR ORDER (when AddStreamingBehaviors() is called):
  Logging → Metrics → Tracing → Validation → Authorization
  (same positional logic as the unary pipeline's first five steps)
```

#### DI extensions for `SharedKernel.Application.Behaviors` (`Extensions/`)

```text
AddSharedKernelApplicationBehaviors(IServiceCollection services)   → ApplicationBehaviorsBuilder

ApplicationBehaviorsBuilder
    .AddDefaultBehaviors()                                          — WO-039, P-243, shipped
        — Registers exactly Logging, Metrics, Tracing, and Validation by delegating to their four
          individual .AddXBehavior() methods below — provably equivalent, not a reimplementation.
        — The ONLY four behaviors eligible for this preset: each carries zero Build()-time
          missing-dependency guard. No other behavior may ever be added to this preset.
        — Composes correctly with any individual .AddXBehavior() call for the same behavior — the
          underlying opt-in tracking is idempotent per behavior type, so calling both never double-
          registers and never changes the fixed canonical order.
        — Never throws InvalidOperationException from Build() on its own.
    .AddValidationBehavior()
        — Registers ValidationBehavior<,> against the open generic IPipelineBehavior<,>.
        — Requires IValidator<T> implementations registered separately (FluentValidation's own
          AddValidatorsFromAssembly or explicit registration) — optional at the per-request-type level;
          omitted entirely, every request simply skips validation (no-op, not an error).
    .AddLoggingBehavior()
    .AddMetricsBehavior()
    .AddTracingBehavior()
        — No missing-dependency guard needed: ApplicationDiagnostics.ActivitySource is always available
          (BCL static instance), exactly like AddMetricsBehavior()/AddLoggingBehavior() carry no guard.
    .AddTransactionBehavior()
        — Build() throws InvalidOperationException if IUnitOfWork is not registered in IServiceCollection
          when this was called — mirrors 07.Messaging's WithIdempotency() missing-store guard.
    .AddCachingBehavior()
        — Build() throws InvalidOperationException if SharedKernel.Caching.Abstractions.ICacheService is
          not registered when this was called.
    .AddCacheInvalidationBehavior()
        — Build() reuses the EXACT SAME ICacheService missing-dependency check already written for
          AddCachingBehavior() — not a duplicated guard. Calling only AddCacheInvalidationBehavior() without
          AddCachingBehavior() still requires ICacheService to be registered; the check is keyed on the
          dependency (ICacheService), not on which .AddXBehavior() call requested it.
    .AddAuthorizationBehavior()
        — Build() throws InvalidOperationException if IAuthorizationContext is not registered in
          IServiceCollection when this was called — same missing-dependency guard shape as Transaction/Caching.
    .AddIdempotencyBehavior()
        — Build() throws InvalidOperationException if IIdempotencyKeyStore is not registered in
          IServiceCollection when this was called.
    .AddResilienceBehavior()
        — Build() throws InvalidOperationException if no ResiliencePipelineProvider/ResiliencePipeline is
          registered/supplied when this was called — same missing-dependency guard shape as the other four.
    .AddFireAndForgetDispatch(Action<FireAndForgetOptions>? configure = null)  — WO-038, P-233, implemented; self-blocking bug fixed WO-039, P-238
        — Registers IFireAndForgetDispatcher → ChannelFireAndForgetDispatcher (singleton; the Channel
          is process-lifetime state).
        — Registers FireAndForgetBackgroundConsumer as a hosted service (IHostedService).
        — Registers FireAndForgetGuardBehavior<,> against the open generic IPipelineBehavior<,> so
          any ISender.Send(IFireAndForgetCommand) is caught and rejected with a descriptive error.
        — NOT registered unless this method is called — entirely opt-in.
    .AddStreamingBehaviors()                                        — WO-038, P-234, implemented
        — Registers StreamLoggingBehavior<,>, StreamMetricsBehavior<,>, StreamTracingBehavior<,>,
          StreamValidationBehavior<,>, StreamAuthorizationBehavior<,> against the open generic
          IStreamPipelineBehavior<,> in the canonical streaming order (Logging → Metrics → Tracing →
          Validation → Authorization).
        — Build() guard: if called, verifies IAuthorizationContext is registered (for the streaming
          Authorization behavior).
        — DISTINCT from AddBehaviors() — calling neither is valid; calling both registers both unary and
          streaming behaviors; calling only one registers only that path.
    .Build()                                                       → IServiceCollection
        — Registers only the behaviors that were opted into, ALWAYS in the fixed pipeline order below
          regardless of .AddXBehavior() call order — mirrors 06.Persistence's "platform three interceptors
          always fire first" precedent (non-negotiable ordering, not configurable).
        — Must NOT call services.AddMediatR() — the consuming service already registers MediatR; this
          builder only appends behaviors via services.AddTransient(typeof(IPipelineBehavior<,>), ...) and
          services.AddTransient(typeof(IStreamPipelineBehavior<,>), ...).
```

---

## Implementation Rules

### Pipeline composition (canonical order)

> **WO-036 and WO-038 Core phases complete (C-18..C-50 ●).** The ten-named-slot unary order below is fully implemented. WO-038 adds `FireAndForgetGuardBehavior<,>` (for fire-and-forget dispatch) and five `IStreamPipelineBehavior<,>` streaming behaviors — none alter the canonical unary order.

```text
1.  LoggingBehavior            ← outermost; logs the full pipeline, including validation/auth/resilience failures
2.  MetricsBehavior             ← records sharedkernel.application.request.duration regardless of outcome
3.  TracingBehavior             ← WO-036; starts/disposes the request-traversal Activity regardless of outcome
4.  ValidationBehavior          ← throws ValidationException before any handler, auth, cache, or retry work happens
5.  AuthorizationBehavior       ← TRequest : IAuthorizeRequest (commands AND queries); rejects before cache/mutation
6.  CachingBehavior              ← queries only (TRequest : ICacheableQuery<TResponse>)
7.  ResilienceBehavior          ← WO-036; commands only in practice (TRequest : IRetryableRequest); wraps Idempotency+Transaction
8.  IdempotentCommandBehavior   ← commands only (TRequest : ICommandBase, IIdempotentRequest); innermost-but-two
9.  TransactionBehavior         ← commands only (TRequest : ICommandBase); wraps handler + commit
10. CacheInvalidationBehavior   ← WO-036; commands only (TRequest : ICommandBase, IInvalidatesCache); innermost — after commit
```

**Positional rationale, step by step:**

1. **Logging outermost** — every request, including ones that fail validation, authorization, or exhaust all resilience retries, must produce a start/end log line; logging must never be skipped by an inner short-circuit.
2. **Metrics second** — the duration histogram must capture the full pipeline cost (validation, auth, cache, retries, commit, invalidation), not just the handler; recorded via try/finally regardless of how the request terminates.
3. **Tracing third (WO-036)** — immediately adjacent to Metrics because both occupy the same "measure/observe the whole pipeline" band; positioned just inside Metrics so the trace span and the duration measurement bracket nearly the same scope, while Logging remains the true outermost layer (a span failing to start must still be logged). A distinct behavior from Metrics, not folded into it — one measures, one traces; single responsibility.
4. **Validation fourth** — malformed input is rejected before spending a permission check, a cache lookup, a retry budget, an idempotency-store round trip, or a transaction — the cheapest, most foundational rejection happens first.
5. **Authorization fifth** — runs after validation (don't spend a permission check on garbage input) and before everything that follows: never let an unauthorized request reach a cache lookup (step 6), consume a retry budget (step 7), an idempotency check (step 8), a mutation (step 9), or an invalidation (step 10).
6. **Caching sixth** — queries only; applies after authorization so an unauthorized query never populates or reads the cache. Mutually exclusive with steps 7–10 at the request-type level (`ICacheableQuery<TResponse>` is never implemented by a command, and `ICommandBase` is never implemented by `IQuery<TResponse>`).
7. **Resilience seventh (WO-036)** — commands only in practice (queries that want retry use `IRetryableRequest` too, but queries already exited the command-only band at step 6's mutual exclusion; a retryable *query* would sit here in place of step 6 if it also needs caching+retry composition, an edge case not yet exercised). Positioned to WRAP Idempotency (step 8) and Transaction (step 9) — this is the resolution to the retry-after-partial-commit hazard: a retry re-runs the FULL duplicate-check-then-commit unit on each attempt, never a bare second commit attempt that bypasses idempotency detection. Positioned after Authorization (don't burn a retry budget on an unauthorized request) and after Caching (a cache hit never needs to retry anything).
8. **Idempotency eighth** — commands only; runs immediately before the commit boundary so a duplicate submission is detected and rejected before `TransactionBehavior`'s `SaveChangesAsync` can run a second time for the same logical operation. Now sits *inside* Resilience precisely so that a Polly-driven retry attempt re-checks idempotency on every attempt rather than skipping the check after the first.
9. **Transaction ninth** — wraps the handler call and the commit; by the time a command reaches this step it has already passed validation, authorization, (if applicable) resilience retry composition, and the idempotency check, so `SaveChangesAsync` only ever runs for a genuinely new, authorized, validated, non-duplicate command.
10. **CacheInvalidation tenth, innermost (WO-036)** — runs only after `TransactionBehavior` returns without throwing, i.e. only after a CONFIRMED commit; evicting before commit risks invalidating a cache entry for a mutation that ultimately rolled back. This is the absolute innermost position in the entire pipeline — nothing runs after it.

Steps 6 and {7, 8, 9, 10} are mutually exclusive at the request-type level (`ICacheableQuery<TResponse>` vs. `ICommandBase`), so a single request only ever actually passes through one of {6} or {7, 8, 9, 10} — the full ten-step order is documented for completeness across the whole request universe, not because any one request traverses all ten. Within {7, 8, 9, 10}, each sub-step is itself independently opt-in via its own marker (`IRetryableRequest`, `IIdempotentRequest`, always-on for any `ICommandBase` for Transaction, `IInvalidatesCache`) — a command implementing none of `IRetryableRequest`/`IIdempotentRequest`/`IInvalidatesCache` still passes through Transaction alone.

### Constructing a generic failure response (`AuthorizationBehavior`/`IdempotentCommandBehavior`)

Both `AuthorizationBehavior<TRequest,TResponse>` and `IdempotentCommandBehavior<TRequest,TResponse>` must short-circuit with a failed response of type `TResponse`, where `TResponse` is statically only known to be `IRequest<TResponse>`'s response type — at runtime it is either the non-generic `Result` (struct) or a closed `Result<T>` (sealed class).

**Current shipped implementation (as of WO-039 P-237, Core phase complete 2026-07-06):** `Shared/FailureResponseFactory.cs` (`internal static class FailureResponseFactory`) solves this with `TResponse Create<TResponse>(Error error)`. It special-cases `TResponse == typeof(Result)` directly (zero reflection, a straight cast). For the `Result<T>` case, it delegates to `ResultOfTDispatcher<TResponse>`, which dispatches through `01.Core`'s `IFailureFactory<TSelf>` (`static abstract TSelf Failure(Error error)`, `where TSelf : IFailureFactory<TSelf>`) — `Result<T>` implements `IFailureFactory<Result<T>>` via its pre-existing `Failure(Error)` static factory (no new member); the non-generic `Result` struct deliberately does not implement it. `IFailureFactory<TSelf>`'s self-referential (CRTP) shape means `TResponse` itself is the type to invoke — unlike the superseded `IResultOfT<T>` approach, no inner value type needs to be located, so `Type.GetInterfaces()`, `Type.GetGenericArguments()`, `Type.MakeGenericType()`, `Type.GetMethod()`, and `MethodBase.Invoke()` are all gone from `BuildFactory()`.

**The one remaining, irreducible constraint:** a C# `static abstract` interface member can only be invoked through a generic type parameter *itself* constrained to that interface at the invoking method. `FailureResponseFactory.Create<TResponse>` is called by `AuthorizationBehavior<TRequest,TResponse>`/`IdempotentCommandBehavior<TRequest,TResponse>` with `TResponse` deliberately unconstrained (it may be the non-generic `Result`, which does not implement `IFailureFactory<TSelf>`) — those two behaviors keep their current generic shape unchanged (a hard requirement of this phase), so the constraint cannot be propagated to the call site without either reflection or a call-chain restructure that was explicitly out of scope. `ResultOfTDispatcher<TResponse>` resolves this the same way `MediatRDomainEventDispatcher` resolves the structurally identical "invoke by runtime-only-known closed type through a generic API" problem: a single `MethodInfo.MakeGenericMethod` call, performed once per closed `TResponse` type (cached forever in a static field — the CLR's per-closed-generic-type instantiation guarantee, not a `ConcurrentDictionary`, though the shape is otherwise identical), bound to a real delegate via `MethodInfo.CreateDelegate` so every call thereafter is a direct delegate invocation, never a per-call `MethodBase.Invoke()`. The `MethodInfo` for the constrained bridge method is captured at type-load time via a typed delegate instantiation over a private witness type (never a string-based `Type.GetMethod(name)` lookup), mirroring `MediatRDomainEventDispatcher`'s own compile-time method-reference capture (WO-038, P-231).

This makes `ResultOfTDispatcher<TResponse>` the domain's **second** `MakeGenericMethod`-based dispatch, structurally identical in shape and safety to `MediatRDomainEventDispatcher`'s already-approved one (build-once-per-Type, delegate-cached, never a per-call `Invoke`) — not a new class of hidden reflection. P-237's acceptance criterion was to eliminate `GetInterfaces`/`GetGenericArguments`/`MakeGenericType`/`GetMethod`/`Invoke`, all of which are gone; the `MakeGenericMethod`/`CreateDelegate` pair is retained because full elimination would require restructuring `AuthorizationBehavior`/`IdempotentCommandBehavior`'s own generic signatures, which this phase's design (D-37) explicitly ruled out of scope. This is flagged here as a disclosed, deliberate trade-off, not a silently-reintroduced defect — a future phase may revisit whether `00.Governance`'s `ReflectionExemptionRegistry` (P-240) should carry a second entry for this call site alongside `MediatRDomainEventDispatcher`'s.

Do not replace the current implementation with `dynamic` or raw string-based reflection lookups as a "quick fix" — that is exactly the class of hidden regression P-237 exists to close for good. Do not duplicate this pattern elsewhere without updating this note — `FailureResponseFactory` is the single chosen answer to "construct an arbitrary `Result`/`Result<T>` failure from an `Error` when `TResponse` is generic," reused by both behaviors.

### Hard violations (never do these)

- Returning `Envelope` or `Envelope<T>` (`04.Contracts`) from any command/query handler — handlers return `Result`/`Result<T>` exclusively; `Envelope` is a presentation-boundary type only (WO-026 P-166/167).
- Catching `ValidationException` inside a handler body to convert it into a `Result.Failure` — the exception must propagate to the presentation layer's single global exception handler; swallowing it here creates a second, inconsistent validation-error response shape.
- Calling `ISender.Send()` / `IMediator.Send()` from within a command or query handler to invoke another handler — handler-to-handler mediator chains create a hidden, hard-to-trace call graph. Compose logic via domain services or direct constructor-injected dependencies instead.
- Injecting `SharedKernel.Persistence.Abstractions.IUnitOfWork`, `IRepository<,>`, `IReadRepository<,>`, or any other `06.Persistence` type directly into a type in this domain — `05.Application` must never reference `06.Persistence` (layering). Repositories are injected into handlers by the consuming service's own composition, not resolved through this package.
- An `IDomainEventHandler<TEvent>` implementation publishing an integration event via anything other than `SharedKernel.Messaging.Abstractions.IEventPublisher` — domain events are dispatched internally by `IDomainEventDispatcher`; the only sanctioned crossing into `07.Messaging` is `IEventPublisher`, called from inside the consuming service's own handler implementation (this package has no compile-time reference to `07.Messaging`).
- Registering `IDomainEventHandler<TEvent>` implementations via assembly scanning or reflection — always use `AddDomainEventHandler<TDomainEvent, THandler>()`, a closed-generic, reflection-free registration, one call per event type.
- Calling `services.AddMediatR(...)` from inside `AddSharedKernelApplication()` or `AddSharedKernelApplicationBehaviors()` — the consuming service owns MediatR registration and assembly scanning; this domain only appends to an already-registered pipeline.
- A query type implementing `ICacheableQuery<TResponse>` and a behavior or handler calling `ICacheService.GetAsync` followed by `SetAsync` instead of the single `GetOrSetAsync` call — stampede protection is only guaranteed through the atomic factory call.
- A command type implementing `ICacheableQuery<TResponse>` — caching is queries-only by design.
- Using `System.Random`, raw `DateTime.UtcNow`/`DateTime.Now`, or hand-rolled retry/backoff loops inside any behavior or handler in this domain — use `IClock` (`01.Core`) for time and `Result<T>`'s railway extensions (`.Bind`, `.Map`, `.Tap`, `.MapError`) for composition.
- Any static mutable state. **Exceptions (exactly two, both narrow and specifically justified — do not add a third under cover of either):** (1) the static `readonly Meter` / `Histogram<double>` pair in `ApplicationDiagnostics` — the same platform-standard diagnostics-instrument pattern already approved for `07.Messaging.MessagingDiagnostics.ActivitySource`; (2) **(WO-039, P-238, shipped)** the internal, non-public `AsyncLocal<bool>`-backed trusted-dispatch marker (`FireAndForgetDispatchContext`) behind `FireAndForgetGuardBehavior<,>`'s fix — `AsyncLocal<T>`'s backing field is static-declared but its VALUE is per-async-flow, not shared global state (the same ambient-context shape as BCL's own `Activity.Current`/`ExecutionContext`), carries zero business data, is never part of the public API surface, and cannot be set or observed by consuming-service code. Do not add further ad-hoc static fields — including further `AsyncLocal<T>` instances — under cover of either exception.
- Adding a project reference from `SharedKernel.Application` to `SharedKernel.Caching.Abstractions` — that reference belongs to `SharedKernel.Application.Behaviors` only (where `ICacheableQuery<TResponse>` / `CachingBehavior` live), so services with no caching needs never pull it in transitively through the base package.
- Throwing `NotImplementedException` or a raw `Exception` for an expected/foreseeable failure inside a handler — return `Result.Failure(Error.X(...))` (`NotFound`, `Conflict`, `BusinessRule`, `Unauthorized`, etc.). Exceptions are reserved for `ValidationException` (the one deliberate behavior short-circuit) and genuinely unexpected faults.
- Adding a second public constructor to `MediatRDomainEventDispatcher` — exactly one constructor accepting `IPublisher`, mirroring `EfUnitOfWork`'s single-constructor rule in `06.Persistence` (avoids the same class of DI resolution ambiguity).
- Injecting `SharedKernel.Security.Abstractions.IUserContext` or `ITenantProvider` directly into `AuthorizationBehavior` or anywhere else in this domain — `05.Application`'s layering ceiling is `01–04`; `12.Security` is never referenced. Use the local `IAuthorizationContext` seam; the consuming service bridges it to the real `IUserContext`/`ITenantProvider` at the composition root.
- `AuthorizationBehavior` throwing instead of short-circuiting with `Result.Failure(Error.Unauthorized(...))` — an unauthorized caller is an expected, foreseeable outcome, not a fault; exceptions stay reserved for `ValidationException` and genuinely unexpected faults.
- Referencing `SharedKernel.Messaging.Abstractions.IIdempotencyStore` directly from `IdempotentCommandBehavior` or anywhere else in this domain — use the local idempotency seam (`IIdempotencyKeyStore`); `07.Messaging` is never referenced by `05.Application`.
- Constraining `IdempotentCommandBehavior<TRequest,TResponse>` to anything other than `ICommandBase` — idempotency is commands-only, mirroring `TransactionBehavior`'s exact constraint; a query type must never satisfy `IIdempotentRequest`'s applicability.
- `IdempotentCommandBehavior` calling `next()` more than once for the same request, or calling `MarkProcessedAsync` before `next()` succeeds — a faulted handler must remain safely retryable with the same idempotency key.
- Reordering the canonical pipeline (currently the seven-step WO-035 order; the ten-named-slot WO-036 order once implemented) without updating the documented positional rationale for every affected step.
- **(WO-036)** Hand-rolling a retry/backoff loop inside a handler or behavior now that `ResilienceBehavior` exists as the platform alternative — this extends the existing `System.Random`/`DateTime.UtcNow` prohibition above to retry/backoff specifically; use `IRetryableRequest` + `ApplicationBehaviorsBuilder.AddResilienceBehavior(...)` instead.
- **(WO-036)** A command implementing `IRetryableRequest` without also implementing `IIdempotentRequest` — this is the documented, accepted gap in the retry-after-partial-commit hazard resolution (not mechanically enforceable across two independent marker interfaces); code review must catch this, the compiler will not.
- **(WO-036)** Folding `TracingBehavior` into `MetricsBehavior` (or vice versa) — they are deliberately separate, single-responsibility behaviors occupying adjacent pipeline positions, not one merged behavior.
- **(WO-036)** `CacheInvalidationBehavior` calling `ICacheService.RemoveAsync`/`RemoveByTagAsync` before `TransactionBehavior` has confirmed a commit, or on a thrown exception/`Result.Failure` — invalidation must follow a confirmed commit only; evicting speculatively risks invalidating a cache entry for a mutation that never actually persisted.
- **(WO-036)** A query type implementing `IInvalidatesCache`, or a command type implementing `ICacheableQuery<TResponse>` — the read/write caching marker pair remains strictly query-only / command-only respectively, exactly like every other shape-constrained marker in this domain.
- **(WO-036)** Wrapping a streamed `IStreamQuery<TResponse>` item in `Result<TResponse>` "for consistency" with the rest of the domain — this is a deliberate, documented deviation; do not silently retrofit the `Result` railway onto the streaming vocabulary.
- **(WO-036)** Assuming any of the ten unary pipeline behaviors apply to `IStreamQuery<TResponse>` via IPipelineBehavior<,> — they do not; use the five streaming behaviors (StreamLogging/Metrics/Tracing/Validation/Authorization via AddStreamingBehaviors()) for IStreamPipelineBehavior<,> coverage; Transaction/Caching/CacheInvalidation/Idempotency/Resilience remain explicitly non-applicable to streaming.
- **(WO-038, P-231)** Using `typeof(TRequest).Name` (short name) for metric/log/trace tag construction — always use `typeof(TRequest).FullName ?? typeof(TRequest).Name` to prevent key collisions when two assemblies in the same host define a request type with the same short name; applies to `MetricsBehavior`, `LoggingBehavior`, `TracingBehavior`, and all WO-038 streaming behavior counterparts.
- **(WO-038, P-231)** Calling `.AsTask()` on the `ValueTask<TResponse>` returned by `ICacheService.GetOrSetAsync` — `await` the `ValueTask` directly to avoid unconditional `Task` wrapper allocation on L1 synchronous cache hits; `.AsTask()` is a correctness-safe but allocation-wasteful anti-pattern on the hot cache path.
- **(WO-038, P-231)** Registering `ResilienceBehavior<,>` against a closed generic `ResiliencePipeline<TResponse>` — register and resolve against the non-generic `ResiliencePipeline` keyed by request type name (or caller-supplied policy name); a per-`TResponse`-type registration silently falls back to no-op when the key does not match the exact closed type, which defeats the behavior with no warning.
- **(WO-038, P-231)** Using `GetMethod(...)!` nullable-suppressed `MethodInfo` resolution in `MediatRDomainEventDispatcher` — the `Publish<T>` method reference must be captured at type-load time via a compile-time delegate binding (e.g. extract `MethodInfo` from a typed delegate instantiation), not deferred via `GetMethod` to the first dispatch call where a rename would produce a silent `NullReferenceException`.
- **(WO-038, P-232)** Implementing `IAuthorizeRequest` using only the deprecated single-string `Requirement` property for new code — use `AllOfRequirements` / `AnyOfRequirements` (the multi-requirement shape); the single-string form remains as a convenience default-interface-member equivalent, not as the primary design surface.
- **(WO-038, P-233)** Dispatching an `IFireAndForgetCommand` via `ISender.Send()` — always use `IFireAndForgetDispatcher.EnqueueAsync()`. `FireAndForgetGuardBehavior<,>` will throw `InvalidOperationException` at runtime if `Send()` is attempted; fix it at the call site, not by removing the guard.
- **(WO-038, P-233)** Registering `FireAndForgetBackgroundConsumer` manually without calling `ApplicationBehaviorsBuilder.AddFireAndForgetDispatch()` — the hosted service, dispatcher, and guard behavior are a unit; registering any one without the others creates a partial, broken configuration; always use `AddFireAndForgetDispatch()`.
- **(WO-038, P-234)** Registering any of the five streaming behaviors (`StreamLoggingBehavior<,>`, `StreamMetricsBehavior<,>`, `StreamTracingBehavior<,>`, `StreamValidationBehavior<,>`, `StreamAuthorizationBehavior<,>`) individually against `IStreamPipelineBehavior<,>` — always use `AddStreamingBehaviors()` on `ApplicationBehaviorsBuilder` to guarantee correct canonical streaming order.
- **(WO-038, P-234)** Adding Transaction, Caching, CacheInvalidation, Idempotency, or Resilience behaviors to the streaming path (against `IStreamPipelineBehavior<,>`) — these behaviors are explicitly documented as not applicable to `IStreamQuery<TResponse>` (read-only contract, stream-materialization unsound, retry-after-partial-consumption undefined); see the "NON-APPLICABLE BEHAVIORS" note in the Streaming Pipeline Behaviors section above.
- **(WO-038, P-234)** Per-item validation inside a streaming behavior — `StreamValidationBehavior<,>` validates the request ONCE at stream-open, never per yielded item; per-item validation would defeat constant-memory streaming guarantees.
- **(WO-039, P-237, fixed 2026-07-06)** Reintroducing `GetInterfaces()`/`GetGenericArguments()`/`MakeGenericType()`/`GetMethod()`/`Invoke()` (or any other `System.Reflection` API beyond the one disclosed `MakeGenericMethod`+`CreateDelegate` pair already documented in "Constructing a generic failure response" above) into `FailureResponseFactory`/`ResultOfTDispatcher` under cover of "it's cached once per Type anyway" — that reasoning is exactly what produced the confirmed gap P-237 exists to close; a per-Type cache does not make string-based reflective method lookup+invoke AOT-safe.
- **(WO-039, P-237)** Changing `AuthorizationBehavior<TRequest,TResponse>`'s or `IdempotentCommandBehavior<TRequest,TResponse>`'s own generic parameter list/shape to work around the reflection-elimination problem — the fix is confined to `FailureResponseFactory`'s internals; a solution that requires touching those two behaviors' public shape must be escalated to arch-lead first, not shipped unilaterally.
- **(WO-039, P-238, fixed 2026-07-06)** Wrapping `FireAndForgetDispatchContext.EnterTrustedDispatch()`'s scope around anything other than the single internal `ISender.Send` call in `FireAndForgetBackgroundConsumer` — widening the trusted window would let unrelated code inside that scope escape `FireAndForgetGuardBehavior<,>`'s protection for other, unrelated fire-and-forget commands dispatched incidentally within it.
- **(WO-039, P-238)** Exposing the trusted-dispatch marker (or any part of its mechanism) as public API, or setting it from anywhere other than `FireAndForgetBackgroundConsumer`'s own internal dispatch call — doing so would let an external caller spoof trusted dispatch and defeat `FireAndForgetGuardBehavior<,>`'s external-misuse protection entirely.
- **(WO-039, P-239)** Letting `MetricsBehavior`'s and `LoggingBehavior`'s success/failure classification logic diverge — both must consume the same shared internal outcome-classification helper; hand-rolling a second `IHasSuccessFlag` pattern-match in either behavior is a hard violation of this domain's "single chosen answer, reused by both behaviors" precedent.
- **(WO-039, P-242)** Inventing a second, ad hoc serialization mechanism for idempotency response replay instead of `System.Text.Json` — reuse the platform's existing serialization stance; this package owns the (de)serialization, the store owns only persistence of the resulting string.
- **(WO-039, P-242)** A store implementing `IIdempotencyResponseStore` without also implementing `IIdempotencyKeyStore` — response replay is additive to key-tracking, never a replacement for it; `IdempotentCommandBehavior` always calls `HasProcessedAsync`/`MarkProcessedAsync` first regardless of replay support.
- **(WO-039, P-243)** Adding any behavior other than Logging, Metrics, Tracing, or Validation to `AddDefaultBehaviors()` — the preset's entire justification is "carries zero `Build()`-time missing-dependency guard"; every other behavior requires a registered local-seam/infrastructure bridge and must remain a deliberate, individual opt-in.
- **(WO-039, P-243)** `AddDefaultBehaviors()` reimplementing behavior registration instead of delegating to the four existing `.AddXBehavior()` methods, or producing a duplicate registration when combined with an individual `.AddXBehavior()` call for the same behavior.
- **(WO-040, P-246)** Logging a request or response payload — or any subset of its fields — by any means other than the `ILoggableRequest<TResponse>` opt-in mechanism: no direct `logger.Log...`/`LogInformation(...)` call inside a handler passing request/response properties, no `ToString()` override on a command/query type consumed by a log call, and no reflection-based property walk over `TRequest`/`TResponse` introduced as an alternative or a "quick fix." The request/query type itself must supply the exact field set via `LoggableRequestFields`/`GetLoggableResponseFields`; `LoggingBehavior<,>` is the single sanctioned place payload data is ever written to a log.
- **(WO-040, P-246)** Adding a new pipeline slot or a second behavior for payload logging — `ILoggableRequest<TResponse>` support is an additive branch inside the existing `LoggingBehavior<,>`, never a competing logging pathway and never a change to the canonical pipeline order.
- **(WO-040, P-246)** Calling `GetLoggableResponseFields(response)` when `next()` has thrown — there is no response to project; only the request-side `LoggableRequestFields` scope (if opted in) applies to the entry log and the exception log on the fault path.
- **(WO-040, P-246)** A `TRequest` not implementing `ILoggableRequest<TResponse>` producing any observable difference in `LoggingBehavior<,>`'s log output compared to before this capability existed — the opt-in branch must be strictly additive, verified by a dedicated regression test (`state-map.md` T-57), not merely asserted by code review.

---

## DI Registration (expected shape)

```csharp
// Composition root (consuming service) — MediatR registration is NOT this package's responsibility
services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
services.AddValidatorsFromAssemblyContaining<Program>();   // FluentValidation's own scanning, not ours

// Domain-event-to-MediatR bridge
services.AddSharedKernelApplication();   // IDomainEventDispatcher -> MediatRDomainEventDispatcher (scoped)
services.AddDomainEventHandler<OrderPlacedDomainEvent, OrderPlacedDomainEventHandler>();

// Quick-start preset (WO-039, P-243, shipped) — the zero-prerequisite subset only (Logging, Metrics,
// Tracing, Validation). Provably equivalent to the four individual calls it replaces; every other behavior
// still requires its own registered local-seam/infrastructure bridge and remains a deliberate opt-in below.
services
    .AddSharedKernelApplicationBehaviors()
    .AddDefaultBehaviors()
    .Build();

// Full opt-in pipeline behaviors — fixed execution order regardless of call order (see Pipeline Composition)
services
    .AddSharedKernelApplicationBehaviors()
    .AddLoggingBehavior()
    .AddMetricsBehavior()
    .AddTracingBehavior()          // no missing-dependency guard (BCL ActivitySource)
    .AddValidationBehavior()
    .AddAuthorizationBehavior()    // requires IAuthorizationContext registered
    .AddCachingBehavior()          // requires SharedKernel.Caching.Abstractions.ICacheService registered
    .AddCacheInvalidationBehavior()// reuses the ICacheService guard above
    .AddResilienceBehavior()       // requires a ResiliencePipelineProvider<string> registered
    .AddIdempotencyBehavior()      // requires IIdempotencyKeyStore registered
    .AddTransactionBehavior()      // requires SharedKernel.Application.Behaviors.IUnitOfWork registered
    // WO-038, design-only:
    .AddFireAndForgetDispatch(opts => opts.Capacity = 500)   // opt-in; registers dispatcher + hosted service + guard behavior
    .AddStreamingBehaviors()       // opt-in; registers 5 IStreamPipelineBehavior<,> implementations
    .Build();

// Bridging this package's IUnitOfWork to 06.Persistence's concrete IUnitOfWork — composition root only,
// never inside SharedKernel.Application/.Behaviors itself
services.AddScoped<SharedKernel.Application.Behaviors.IUnitOfWork>(sp =>
    new EfUnitOfWorkAdapter(sp.GetRequiredService<SharedKernel.Persistence.Abstractions.IUnitOfWork>()));

// Bridging this package's IAuthorizationContext to 12.Security's real IUserContext/ITenantProvider —
// composition root only, never inside SharedKernel.Application.Behaviors itself
services.AddScoped<SharedKernel.Application.Behaviors.IAuthorizationContext>(sp =>
    new UserContextAuthorizationAdapter(sp.GetRequiredService<SharedKernel.Security.Abstractions.IUserContext>()));

// Idempotency key store — composition root provides the implementation (e.g. backed by the same
// distributed store used elsewhere, or a dedicated table/cache key); never a 07.Messaging reference
services.AddScoped<SharedKernel.Application.Behaviors.IIdempotencyKeyStore, RedisIdempotencyKeyStore>();

// Opt-in response replay (WO-039, P-242, shipped) — RedisIdempotencyKeyStore additionally implements
// IIdempotencyResponseStore; IdempotentCommandBehavior detects this via an `is` check on the SAME resolved
// instance above, no second DI registration needed. A store that implements ONLY IIdempotencyKeyStore is
// still fully valid and unaffected.
// public sealed class RedisIdempotencyKeyStore : IIdempotencyKeyStore, IIdempotencyResponseStore { ... }

// A command
public sealed record PlaceOrderCommand(Guid CustomerId, decimal Total) : ICommand<Guid>;

public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand request, CancellationToken ct)
    {
        // Stage mutations via injected repositories only — TransactionBehavior calls
        // IUnitOfWork.SaveChangesAsync after this handler returns successfully.
        return Result<Guid>.Success(orderId);
    }
}

// A cacheable query
public sealed record GetOrderByIdQuery(Guid OrderId)
    : IQuery<OrderDto>, ICacheableQuery<Result<OrderDto>>
{
    public CachePolicy CachePolicy => CachePolicy.Default;
    public string CacheKey => $"orders:{OrderId:D}";
}

public sealed class GetOrderByIdQueryHandler : IQueryHandler<GetOrderByIdQuery, OrderDto>
{
    public Task<Result<OrderDto>> Handle(GetOrderByIdQuery request, CancellationToken ct) { /* ... */ }
}

// An authorized, idempotent command — multi-requirement shape (WO-038, P-232, design-only)
// (the single-string Requirement convenience form remains available as a default-interface-member)
public sealed record PlaceOrderCommand(string IdempotencyKey, Guid CustomerId, decimal Total)
    : ICommand<Guid>, IAuthorizeRequest, IIdempotentRequest
{
    // AllOf: caller must have BOTH "orders:create" AND "orders:write" to proceed
    public IReadOnlyCollection<string> AllOfRequirements => ["orders:create", "orders:write"];
    public IReadOnlyCollection<string> AnyOfRequirements => [];
}

// A fire-and-forget command (WO-038, P-233, design-only)
// Never dispatched via ISender.Send() — always via IFireAndForgetDispatcher.EnqueueAsync()
public sealed record SendWelcomeEmailCommand(Guid UserId) : IFireAndForgetCommand;

public sealed class SendWelcomeEmailCommandHandler : ICommandHandler<SendWelcomeEmailCommand>
{
    public async Task<Result> Handle(SendWelcomeEmailCommand request, CancellationToken ct)
    {
        // Background execution — caller has already received its HTTP response
        return Result.Success();
    }
}

// Enqueueing a fire-and-forget command from an application service
public sealed class UserRegisteredDomainEventHandler : IDomainEventHandler<UserRegisteredDomainEvent>
{
    private readonly IFireAndForgetDispatcher _dispatcher;
    public UserRegisteredDomainEventHandler(IFireAndForgetDispatcher dispatcher)
        => _dispatcher = dispatcher;

    public ValueTask Handle(UserRegisteredDomainEvent domainEvent, CancellationToken ct)
        => _dispatcher.EnqueueAsync(new SendWelcomeEmailCommand(domainEvent.UserId), ct);
}

// Parallel domain event dispatch opt-in (WO-038, P-233, design-only)
services.AddSharedKernelApplication(opts => opts.ParallelDispatch = true);

// (WO-036, design-only) A retryable, idempotent command that also invalidates a cache entry on success —
// IRetryableRequest is paired with IIdempotentRequest on the SAME command, resolving the retry-after-
// partial-commit hazard (see Implementation Rules); IInvalidatesCache evicts the now-stale cached read.
public sealed record UpdateOrderTotalCommand(string IdempotencyKey, Guid OrderId, decimal NewTotal)
    : ICommand, IIdempotentRequest, IRetryableRequest, IInvalidatesCache
{
    public IReadOnlyCollection<string> CacheKeysToInvalidate => [$"orders:{OrderId:D}"];
}

// (WO-036, design-only) A streaming query — no Result<T> wrapping; errors terminate the stream via a
// thrown exception. None of the ten pipeline behaviors apply to this shape (see Streaming vocabulary above).
public sealed record ExportOrdersStreamQuery(DateOnly From, DateOnly To) : IStreamQuery<OrderRow>;

public sealed class ExportOrdersStreamQueryHandler : IStreamQueryHandler<ExportOrdersStreamQuery, OrderRow>
{
    public async IAsyncEnumerable<OrderRow> Handle(
        ExportOrdersStreamQuery request, [EnumeratorCancellation] CancellationToken ct)
    {
        // yield return rows one at a time — constant-memory streaming, no Result<T> wrapper per item.
    }
}

// A domain event handler (consuming service) — the seam into integration events
public sealed class OrderPlacedDomainEventHandler : IDomainEventHandler<OrderPlacedDomainEvent>
{
    private readonly IEventPublisher _eventPublisher; // SharedKernel.Messaging.Abstractions (07.Messaging)

    public OrderPlacedDomainEventHandler(IEventPublisher eventPublisher) => _eventPublisher = eventPublisher;

    public Task Handle(OrderPlacedDomainEvent domainEvent, CancellationToken ct)
        => _eventPublisher.PublishAsync(
            new OrderPlacedIntegrationEvent(domainEvent.Payload.OrderId), ct);
}
```

`SharedKernel.Application` and `SharedKernel.Application.Behaviors` ship **no MediatR registration of their own** — only the dispatcher bridge and the opt-in behavior builder.

---

## AOT Compatibility

- `ICommandBase`, `ICommand`, `ICommand<TResponse>`, `IQuery<TResponse>`, `ICommandHandler<>`, `ICommandHandler<,>`, `IQueryHandler<,>`, `IDomainEventHandler<TDomainEvent>`, `IUnitOfWork`, `ICacheableQuery<TResponse>`, `IAuthorizationContext`, `IAuthorizeRequest`, `IIdempotencyKeyStore`, `IIdempotentRequest`, and (WO-036, design-only) `IStreamQuery<TResponse>`, `IStreamQueryHandler<,>`, `IRetryableRequest`, `IInvalidatesCache` are interfaces — AOT-safe by definition.
- `DomainEventNotification<TDomainEvent>` is a closed generic sealed record at every call site — AOT-safe.
- `MediatRDomainEventDispatcher`'s per-event-type dispatch is the one documented `MakeGenericMethod` exception in this domain — cached once per concrete `Type` in a static `ConcurrentDictionary<Type, Delegate>`, not a per-call reflection cost. Identical, already-approved shape to `07.Messaging`'s `MassTransitEventPublisher` bridge. The `Publish<T>` `MethodInfo` must be captured at type-load time via a compile-time delegate binding (WO-038, P-231) rather than a nullable-suppressed `GetMethod(...)!` call deferred to first use.
- `AddDomainEventHandler<TDomainEvent, THandler>()` is a closed generic at the call site — the consuming service supplies both type arguments at compile time; zero reflection.
- `ValidationBehavior<,>`, `LoggingBehavior<,>`, `MetricsBehavior<,>`, `TransactionBehavior<,>`, `CachingBehavior<,>`, `AuthorizationBehavior<,>`, `IdempotentCommandBehavior<,>`, and (WO-036, design-only) `TracingBehavior<,>`, `ResilienceBehavior<,>`, `CacheInvalidationBehavior<,>` are closed generic sealed classes; MediatR resolves the open generic `IPipelineBehavior<,>` registration to a closed generic per request type via ordinary DI generics at runtime, not `MakeGenericType` per call.
- `ApplicationDiagnostics.Meter` / `Histogram<double>` are BCL `System.Diagnostics.Metrics` — fully AOT-safe, no reflection.
- **(WO-036, design-only)** `ApplicationDiagnostics.ActivitySource` is BCL `System.Diagnostics.ActivitySource` — fully AOT-safe, no reflection, identical AOT posture to the existing `Meter`. `TracingBehavior`'s `using var activity = ActivitySource.StartActivity(...)` is the same zero-reflection BCL call already proven by `07.Messaging`'s `ConsumerBase.Consume`/`MassTransitEventPublisher.Publish`.
- **(WO-036, design-only)** `IStreamQuery<TResponse>`/`IStreamQueryHandler<,>` are pure interface aliases over MediatR's own `IStreamRequest<TResponse>`/`IStreamRequestHandler<,>` — zero reflection beyond whatever AOT posture MediatR's `IStreamMediator`/`ISender.CreateStream` already carries (verify on each MediatR major upgrade, same caveat as the existing unary `IRequestHandler<,>` resolution).
- **(WO-036, design-only)** `ResilienceBehavior<,>`'s Polly v8 resilience pipeline execution (`ResiliencePipeline.ExecuteAsync(...)`) is Polly v8's own delegate-based, reflection-free execution model — no `MakeGenericMethod`, no `Activator.CreateInstance`. Verify Polly v8's own AOT compatibility status on first integration (Scaffold phase, `state-map.md` S-09) and on each major upgrade thereafter.
- `MediatR`'s own assembly-scanning registration (`AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...))`) is reflection-based and is the **consuming service's** responsibility, performed once at startup — not part of this package, not a hot path. Verify MediatR's own AOT compatibility status on each major upgrade.
- **(WO-038, P-233)** `System.Threading.Channels.Channel<T>` is BCL and fully AOT-safe — no reflection, no `Activator.CreateInstance`. `FireAndForgetBackgroundConsumer`'s handler resolution uses `IServiceScopeFactory`/`IServiceProvider.GetRequiredService<T>` (DI generics, not reflection); same AOT posture as any other DI-resolved scoped service.
- **(WO-038, P-234)** `IStreamPipelineBehavior<TRequest,TResponse>` is an interface already part of the pinned `MediatR` 12.4.1 package — same AOT posture as `IPipelineBehavior<,>`. The five streaming behaviors are closed-generic sealed classes; MediatR's streaming pipeline resolves them via ordinary DI generics, same reflection-free path as the unary behaviors. Per-request-type resolution via `IStreamMediator`/`ISender.CreateStream` carries the same "verify on each MediatR major upgrade" caveat as the existing unary path.
- `FluentValidation`'s `IValidator<T>` resolution uses ordinary DI generics — no reflection at the `ValidationBehavior` call site. FluentValidation's own rule-building DSL may use expression trees internally; verify on each major upgrade.
- No `Activator.CreateInstance`, `Assembly.Load`, or dynamic reflection anywhere else in this domain's hot paths.
- **(WO-039, P-237, Core complete 2026-07-06)** `FailureResponseFactory`'s `ResultOfTDispatcher<TResponse>.BuildFactory()` (`SharedKernel.Application.Behaviors/Shared/FailureResponseFactory.cs`) no longer performs `Type.GetInterfaces()`, `Type.GetGenericArguments()`, `Type.MakeGenericType()`, `Type.GetMethod()`, or `MethodBase.Invoke()` — it dispatches through `01.Core`'s `IFailureFactory<TResponse>` (CRTP, self-referential), so `TResponse` itself is the type to invoke, with no inner value type to locate. One `MethodInfo.MakeGenericMethod` call remains, performed once per closed `TResponse` type (cached in a static field) and bound to a real delegate via `MethodInfo.CreateDelegate` — never a per-call `MethodBase.Invoke()`. This is a disclosed, deliberate second dispatch of the same shape as `MediatRDomainEventDispatcher`'s already-approved one, not an open AOT/trimming hazard: the `MethodInfo` is captured at type-load time via a compile-time typed-delegate instantiation (never a string-based `Type.GetMethod(name)` lookup), so there is no string-keyed member lookup for a trimmer to break. See "Constructing a generic failure response" above for the full rationale and the residual-scope trade-off (`AuthorizationBehavior`/`IdempotentCommandBehavior`'s generic shapes were kept unchanged per this phase's design, D-37).

---

## Test Rules

- Unit tests for each package live in the nested `.Tests/` folder inside that package's folder.
- `SharedKernel.Application.Tests/` — contract-shape tests for `ICommand`/`ICommand<T>`/`IQuery<T>`/`ICommandHandler<>`/`IQueryHandler<,>`; `MediatRDomainEventDispatcher` dispatch tests (empty list is a no-op; single event; multiple events of different concrete types in one call — both published as their own closed `DomainEventNotification<T>`; handler exception during `IPublisher.Publish` propagates unchanged); `AddDomainEventHandler<,>` DI resolution test (handler and notification adapter both resolvable after one call).
- `SharedKernel.Application.Behaviors.Tests/` — one test class per behavior, preferring a minimal real `ServiceCollection` + `AddMediatR` + the behavior under test over hand-rolled `RequestHandlerDelegate<TResponse>` mocks wherever practical.
- `ValidationBehavior` tests: zero registered validators → handler invoked; one failing validator → `ValidationException` thrown with the correct `Errors`, handler never invoked; multiple validators with mixed pass/fail → aggregated `Errors` from every failing validator only.
- `LoggingBehavior` tests: successful request logs start + success at `Information`; faulted request logs start + `Error` with the exception, then rethrows unchanged.
- `MetricsBehavior` tests: histogram records exactly one measurement per request tagged with the request type name; measurement is recorded even when the inner handler throws.
- `TransactionBehavior` tests: a command request calls `IUnitOfWork.SaveChangesAsync` exactly once, after the handler returns; handler throws → `SaveChangesAsync` is never called; a query request (no `ICommandBase`) never resolves this behavior into its pipeline at all — a DI contract test, not a runtime branch.
- `CachingBehavior` tests: cache miss → factory (`next()`) invoked once, result cached; cache hit → factory not invoked; concurrent requests for the same key → factory invoked exactly once (stampede protection, asserted against a real `ICacheService` test double); a command type can never satisfy `ICacheableQuery<TResponse>` — a compile-time/contract-shape assertion only, since no runtime case is reachable.
- `AuthorizationBehavior` tests: `IAuthorizationContext.IsAuthorizedAsync` returns `false` → handler never invoked, `Result.Failure` with `ErrorType.Unauthorized` returned (asserted for both `Result` and `Result<T>` response shapes); returns `true` → `next()` invoked and its result returned unchanged; a request not implementing `IAuthorizeRequest` never resolves this behavior into its pipeline — a DI-contract test, not a runtime branch.
- `IdempotentCommandBehavior` tests: first call with a new key → `next()` invoked once, `MarkProcessedAsync` called only after `next()` succeeds; duplicate key (`HasProcessedAsync` → `true`) → `next()` never invoked a second time, `Result.Failure` with `ErrorType.Conflict` returned; handler throws → `MarkProcessedAsync` never called (key remains unmarked, safely retryable); a query type (no `ICommandBase`) never resolves this behavior into its pipeline at all — a DI contract test.
- **(WO-036, design-only)** `TracingBehavior` tests: use an `ActivityListener` (matching `07.Messaging`'s `OTelInstrumentationTests` pattern) to assert exactly one span is recorded per request on success, on `Result.Failure`, and on thrown exception; span carries the `request.name` tag.
- **(WO-036, design-only)** Streaming vocabulary contract-shape tests: `IStreamQuery<TResponse>`/`IStreamQueryHandler<,>` compile and resolve through MediatR's `IStreamMediator`/`ISender.CreateStream` exactly as a hand-written `IStreamRequestHandler<,>` would; no behavior test needed since zero behaviors apply to this shape (a documented fact, not something to test for absence).
- **(WO-036, design-only)** `ResilienceBehavior` tests: a transient failure followed by success is retried and ultimately succeeds; a request type not implementing `IRetryableRequest` never resolves this behavior into its pipeline — a DI-contract test, not a runtime branch; exhausted retries surface per the documented design decision (thrown exception or propagated `Result.Failure`, whichever the wrapped pipeline's last attempt produced).
- **(WO-036, design-only)** `CacheInvalidationBehavior` tests: successful command triggers exactly one `RemoveAsync`/`RemoveByTagAsync` call per declared key/tag; failed (`Result.Failure`) command never calls removal; thrown exception never calls removal; a query type can never satisfy `IInvalidatesCache` — a compile-time/contract-shape assertion only.
- **(WO-036, design-only) Reusable pipeline test harness** — `SharedKernel.Application.Behaviors.Tests`-internal only (never packaged, never referenced by `16.Testing` or production code); wires a real `ServiceCollection` + `AddMediatR` + a caller-chosen subset of behaviors via `ApplicationBehaviorsBuilder`, exposing a minimal fluent surface to send a request and assert on: final response shape (success/failure), thrown exceptions, recorded `ApplicationDiagnostics` metrics, and recorded `ActivitySource` spans (via `ActivityListener`). This formalizes the "preferring a minimal real `ServiceCollection`" guidance above into one reusable harness instead of each behavior test file re-deriving the same wiring; `TracingBehavior`/`ResilienceBehavior` tests use it from the start, and the seven WO-035 behavior test files are refactored to use it where doing so does not reduce test clarity (documented per file if any test is deliberately left as-is).
- **(WO-038, P-231)** `IdempotentCommandBehavior` fail-and-consume-key test: dispatch a command where the handler returns `Result.Failure`; assert `MarkProcessedAsync` IS called; dispatch the SAME key again; assert `next()` is NOT invoked the second time (short-circuit with `Result.Failure(ErrorType.Conflict)`) — confirms the fail-and-consume invariant.
- **(WO-038, P-231)** `TransactionBehavior` cancellation test: assert `OperationCanceledException` from `next()` prevents `SaveChangesAsync` from being called and propagates unchanged.
- **(WO-038, P-231)** Key-uniqueness test: assert `typeof(TRequest).FullName` (not `.Name`) is used for metric/log/trace tag values for a request type in a named namespace.
- **(WO-038, P-232)** `LoggingBehavior` level tests: `Result.Success` → `Information`; `Result.Failure` → `Warning`; non-`IHasSuccessFlag` response → `Information`; exception → `Error` + rethrow.
- **(WO-038, P-232)** `AuthorizationBehavior` multi-requirement tests: single-requirement pass/fail, AllOf-all-pass, AllOf-first-fails, AnyOf-first-passes, AnyOf-all-fail — six scenarios, all covering correct short-circuit behavior.
- **(WO-038, P-233)** `MediatRDomainEventDispatcher` parallel dispatch tests: serial order preserved (default), parallel all-succeed (all dispatched concurrently), parallel some-fail (all events dispatched, all exceptions collected in `AggregateException` before rethrow).
- **(WO-038, P-233)** Fire-and-forget tests: immediate return on `EnqueueAsync`; background consumer executes handler without blocking caller; handler exception logged at `Error`, consumer continues; full channel + `DropAndLog` drops command without exception to caller; `AddFireAndForgetDispatch()` not called → `IFireAndForgetDispatcher` not registered.
- **(WO-038, P-233)** `FireAndForgetGuardBehavior` rejection test: `ISender.Send(fireAndForgetCommand)` throws `InvalidOperationException` with the documented message; `next()` is never called.
- **(WO-038, P-234)** Streaming behavior tests (one class per behavior): `StreamLoggingBehavior` — entry/completion/first-item/fault logging at correct levels; `StreamMetricsBehavior` — one `RequestDuration` measurement per stream with `outcome` tag; `StreamTracingBehavior` — one span per stream, tagged with `FullName`, safe when no `ActivityListener` registered; `StreamValidationBehavior` — validation once at open, fails before open, passes through; `StreamAuthorizationBehavior` — AllOf/AnyOf multi-requirement evaluation on streaming requests, DI-contract test for non-`IAuthorizeRequest` types.
- **(WO-039, P-237, shipped 2026-07-06)** `FailureResponseFactory` reflection-elimination test (`Shared/FailureResponseFactoryReflectionShapeTests.cs`): an explicit Mono.Cecil IL-shape assertion — loaded via the test-project-only `SharedKernel.ArchitectureTests` reference (S-17), reusing its `Call`/`Callvirt`/`MethodReference` IL-walk technique directly rather than via `ReflectionGuardRules` (which scans a whole assembly and would also trip on the unrelated, already-disclosed `MediatRDomainEventDispatcher` exception in a different assembly) — proving `FailureResponseFactory`/`ResultOfTDispatcher<TResponse>` contain none of `GetInterfaces`/`GetGenericArguments`/`MakeGenericType`/`GetMethod`/`GetMethods`/`MethodBase.Invoke`, while confirming the one disclosed `MakeGenericMethod`+`CreateDelegate` pair IS present. **Gotcha for future IL-shape tests:** a bare `MethodReference.Name == "Invoke"` match is too broad — it also matches an ordinary `Func<Error,TResponse>` delegate invocation (`Factory(error)` compiles to a `Callvirt` on `Func<,>.Invoke`), which is NOT reflection. Distinguish by checking `MethodReference.DeclaringType.FullName` is `System.Reflection.MethodBase`/`MethodInfo` before treating an `Invoke` call as forbidden. Plus three behavioral tests exercising `Create<TResponse>` via the real `AuthorizationBehavior` pipeline for `Result`, `Result<string>`, and `Result<int>`; existing `AuthorizationBehavior`/`IdempotentCommandBehavior` tests (including `AuthorizationBehaviorTests.Handle_Unauthorized_GenericResultOfT_HandlerNeverInvokedAndReturnsUnauthorizedFailure`, which exercises the fixed `ResultOfTDispatcher<TResponse>` path directly) confirmed passing unmodified in intent.
- **(WO-039, P-238, shipped)** Fire-and-forget end-to-end test: `FireAndForgetDispatcherTests.BackgroundConsumer_ExecutesEnqueuedCommand_ViaRealDocumentedWiring` wires `AddFireAndForgetDispatch()` exactly per the documented DI shape, starts the real `FireAndForgetBackgroundConsumer` hosted service, dispatches via the real `IFireAndForgetDispatcher.EnqueueAsync`, and asserts the handler actually executed — closes the exact gap the prior `BuildConsumerDirectProvider` workaround left open (that workaround is retired); `GuardBehavior_RejectsDirectSenderSend_EvenAfterTrustedDispatchFix` confirms `FireAndForgetGuardBehavior` still rejects a caller's direct `ISender.Send` with the existing message.
- **(WO-039, P-239, shipped)** `MetricsBehavior` outcome tag tests: success → `"success"`; `Result.Failure` → `"failure"`; thrown exception → `"exception"`; `LoggingBehavior` tests still pass unmodified in intent after the shared outcome-classification-helper refactor.
- **(WO-039, P-241, all 4 of 4 rule groups shipped 2026-07-06, fifth session)** Real-assembly architecture tests, in `Governance/RealAssemblyArchitectureRulesTests.cs` (`SharedKernel.Application.Behaviors.Tests`): `ApplicationPipelineRules.BehaviorsNeverReferenceConcreteInfrastructure`/`.NoExistingBehaviorMatchesStreamRequestConstraint`/`.NoHandRolledRetryLoopOutsideResilienceBehavior`, `PipelineOrderAssertion.AssertRegistrationOrder` (asserted against a real `IServiceCollection` produced by `ApplicationBehaviorsBuilder.Build()` with all ten unary behaviors opted in, plus a companion negative test proving the helper itself detects a wrong order), `MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag`, and — as of `00.Governance` P-240 shipping (5/5 tasks, `ReflectionExemptionRegistry.AllowList`'s first real entry: `MediatRDomainEventDispatcher`'s compiler-generated closure) — `ReflectionGuardRules.NoMakeGenericMethodReflection` all now run and PASS against the REAL `SharedKernel.Application`/`SharedKernel.Application.Behaviors` assemblies (not fixtures) via the test-project-only `ProjectReference` to `00.Governance/SharedKernel.ArchitectureTests` (S-17) — the first time these already-built `00.Governance` rules ran against anything but contrived fixtures. **The suspected second-exemption-entry need for `ResultOfTDispatcher<TResponse>`'s own `MakeGenericMethod` call site (see "Constructing a generic failure response" above) did NOT materialize**: empirically verified passing with zero additional registry entries, because `ResultOfTDispatcher<TResponse>` is an `internal static class` — which compiles to IL `abstract sealed` — and `ReflectionGuardRules.NoMakeGenericMethodReflection` applies NetArchTest's `.That().AreNotAbstract()` filter before scanning, excluding it from the scan entirely. This is a second, independently-discovered vacuous-pass mechanism, the same class of gap as the already-documented closure-type invisibility that makes the `MediatRDomainEventDispatcher` check a non-end-to-end pass too (see `00.Governance/CLAUDE.md`'s SK0012 registry notes) — flagged here for completeness, not silently glossed over. **Jurisdiction note:** closing the companion follow-up notes this discovery leaves in `00.Governance/CLAUDE.md` and updating the root `state-map.md` Domain Summary Board's `00.Governance` row are `00.Governance`-side documentation updates, out of this domain's jurisdiction — this domain's own docs (this file, this note) are the complete extent of what P-241/DO-15 requires here.
- **(WO-039, P-242, shipped)** Idempotency response replay tests: a store implementing ONLY `IIdempotencyKeyStore` still returns `Error.Conflict` on a duplicate, unchanged; a store also implementing `IIdempotencyResponseStore` replays the original success payload after a successful first attempt, and the original stored failure (not a fresh `Error.Conflict`) after a `Result.Failure` first attempt; a replay-capable store with no stored value for an already-marked-processed key falls back to `Error.Conflict`.
- **(WO-039, P-243, shipped)** `AddDefaultBehaviors()` tests: registration output is equivalent to calling the four individual `.AddXBehavior()` methods; combining the preset with an individual call for the same behavior produces no duplicate registration and preserves canonical order; the preset alone never throws `InvalidOperationException` from `Build()`.
- **(WO-040, P-246, design-only)** `ILoggableRequest<TResponse>` opt-in payload logging tests (`LoggingBehavior` test file, extended): a request implementing the marker attaches `LoggableRequestFields` to the entry-log scope; `GetLoggableResponseFields(response)` is attached to the completion-log scope on both a success and a `Result.Failure` outcome (structured fields compose correctly with the existing Information/Warning level decision); `GetLoggableResponseFields` is never invoked and no response-fields scope is entered on a thrown exception; a null/empty field-set result enters no scope (zero-overhead path); a request NOT implementing the marker produces a byte-for-byte identical log call sequence to the pre-WO-040 baseline — a dedicated regression test, not just code review, mechanically enforcing the additive-only guarantee.
- **Standard test package set:** `xunit` 2.9.3, `xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.13.0, `coverlet.collector` 6.0.4, `FluentAssertions` 8.10.0 (bumped from 8.4.0 in WO-039 P-241/S-17 — `00.Governance/SharedKernel.ArchitectureTests` pins `FluentAssertions` 8.10.0, and NuGet's `NU1605` package-downgrade check errors on a lower pin transitively required by a `ProjectReference`; both test projects must stay at or above whatever `SharedKernel.ArchitectureTests` requires), `NSubstitute` 5.3.0 — same pins already used across `06.Persistence`/`07.Messaging` unless a documented reason requires otherwise.
- **Test-project-only `SharedKernel.ArchitectureTests` reference (WO-039 P-241, S-17):** both `SharedKernel.Application.Tests.csproj` and `SharedKernel.Application.Behaviors.Tests.csproj` carry a `ProjectReference` to `00.Governance/SharedKernel.ArchitectureTests` — never from either production `.csproj`. The reference is scaffolded ahead of P-241's Core phase (which is blocked on `00.Governance` P-240 plus this domain's own P-237/P-239 Core phases) so the real-assembly rule invocations can be written the moment those blockers clear.
- **GlobalUsings.cs required** — every test project must include a `GlobalUsings.cs` containing `global using Xunit;`. `ImplicitUsings` does not auto-import xUnit attributes.

---

## Changelog

> Maintained by the application domain agent. One line per significant change.

- [2026-06-29] Domain brain initialized — packages, interfaces, MediatR command/query contracts (`ICommand`/`ICommand<TResponse>`/`IQuery<TResponse>`), the domain-event-to-MediatR bridge (`IDomainEventHandler<TDomainEvent>`/`DomainEventNotification<TDomainEvent>`/`MediatRDomainEventDispatcher`, fulfilling 03.Domain's P-081 forward reference), five pipeline behaviors (Validation/Logging/Metrics/Transaction/Caching), canonical pipeline composition order, hard violations, DI registration shape, AOT notes, test rules; `CachingBehavior`/`ICacheableQuery<TResponse>` design carried forward verbatim from root `state-map.md` P-015 (WO-004, pending dispatch — not yet implemented); `IUnitOfWork` deliberately kept distinct from `06.Persistence.Abstractions.IUnitOfWork` to respect the layering direction; MediatR pinned to 12.4.x ahead of v13's commercial license
- [2026-06-29] WO-035 (root P-214–P-219) dispatched — two new opt-in pipeline behaviors designed and documented: `AuthorizationBehavior<,>` (+ local `IAuthorizationContext` seam + `IAuthorizeRequest` marker, zero `12.Security` reference, applies to commands AND queries) and `IdempotentCommandBehavior<,>` (+ local `IIdempotencyKeyStore` seam mirroring `07.Messaging.Abstractions.IIdempotencyStore`'s shape + `IIdempotentRequest` marker, constrained to `ICommandBase` only, zero `07.Messaging` reference); canonical pipeline order revised from five to seven steps (Logging → Metrics → Validation → Authorization → Caching → Idempotency → Transaction) with full positional rationale documented per step; `ApplicationBehaviorsBuilder` gains `.AddAuthorizationBehavior()`/`.AddIdempotencyBehavior()` with `Build()`-time missing-dependency guards mirroring Transaction/Caching; new top-level callout documents the "local-seam bridging" pattern as a single repeated pattern (IUnitOfWork/IAuthorizationContext/IIdempotencyKeyStore), not three unrelated ones; this design is not yet implemented — implementation tracked in `05.Application/state-map.md` Scaffold/Core/Tests/Docs/Published phases
- [2026-06-29] SK.05.Scaffold complete — both `.csproj` files wired: `SharedKernel.Application` → `MediatR` pinned to exact `12.4.1` + `ProjectReference` to `SharedKernel.Primitives`/`SharedKernel.Core`/`SharedKernel.Domain`; `SharedKernel.Application.Behaviors` → `MediatR 12.4.1` + `FluentValidation` pinned to exact `11.11.0` + `ProjectReference` to `SharedKernel.Application`/`SharedKernel.Primitives`/`SharedKernel.Core`/`SharedKernel.Caching.Abstractions`; folder structure created in both packages exactly per the Interface Contracts section headers; both nested test stub projects (`SharedKernel.Application.Tests`, `SharedKernel.Application.Behaviors.Tests`) created with the standard pin set (xunit 2.9.3, xunit.runner.visualstudio 2.8.2, Microsoft.NET.Test.Sdk 17.13.0, coverlet.collector 6.0.4, FluentAssertions 8.4.0, NSubstitute 5.3.0) plus a `ProjectReference` to `16.Testing/SharedKernel.Testing` and a `GlobalUsings.cs` with `global using Xunit;`; empty folders tracked via `.gitkeep` (no existing repo convention found, so this is the first instance — future domains creating empty scaffold folders should follow the same pattern); all four projects build with zero compiler warnings/errors in Release (the NU1903 SQLitePCLRaw advisory on the two test projects is a pre-existing transitive warning inherited from `SharedKernel.Testing` itself, identical to every other domain's test project that references it — not introduced by this phase); confirmed zero project reference to `06.Persistence`, `07.Messaging`, or `12.Security` anywhere in either package or test stub (application-phase-implementer)
- [2026-06-29] SK.05.Core complete (C-01..C-17) — full command/query vocabulary, domain-event-to-MediatR bridge (`MediatRDomainEventDispatcher` using the documented `MakeGenericMethod`-cached-delegate exception), and all seven pipeline behaviors with `ApplicationBehaviorsBuilder` implemented; new `Shared/FailureResponseFactory.cs` (`internal static class`) added to `SharedKernel.Application.Behaviors` — solves "construct a `Result`/`Result<T>` failure from `Error` when `TResponse` is generic" (needed by `AuthorizationBehavior`/`IdempotentCommandBehavior`); both packages build with 0 warnings/0 errors; zero forbidden references confirmed (application-phase-implementer)
- [2026-06-29] `FailureResponseFactory` revised — replaced the initial `dynamic`/`Microsoft.CSharp` implementation with a cached `Expression.Lambda(...).Compile()` per closed `TResponse` type (static `ConcurrentDictionary<Type, Func<Error, object>>`), removing the new `Microsoft.CSharp` package reference entirely. This is the second documented, justified exception to the platform-wide `MakeGenericMethod`/reflection prohibition (the first being `MediatRDomainEventDispatcher`), built via `Expression` compilation — the governance rule's own recommended alternative — rather than `dynamic`, which was rejected for pulling in an unnecessary dependency and being non-AOT-safe; Technology Stack table and AOT Compatibility section updated to match (root cause: design review before proceeding to the Tests phase)
- [2026-06-29] SK.05.Docs complete (DO-01..DO-04) — enabled `GenerateDocumentationFile`/`TreatWarningsAsErrors` plus full NuGet metadata on both csproj files; fixed one CS1574 broken `cref` in `DomainEventNotificationHandler`; added `<inheritdoc/>` to all seven behaviors' `Handle()` overrides to clear CS1591; both packages build 0 warnings/0 errors under the new enforcement; `README.md` written for both packages; 50/50 tests still passing (application-phase-implementer)
- [2026-06-30] WO-036 (root P-220–P-224) dispatched — design-only, not yet implemented (see `state-map.md` D-11..D-25). Five new capabilities documented: (1) **Tracing parity** — `ApplicationDiagnostics.ActivitySource` (BCL, same name/version as the existing `Meter`) + a new, distinct `TracingBehavior<,>` positioned immediately after `MetricsBehavior`, mirroring `07.Messaging.MassTransit.Diagnostics.MessagingDiagnostics.ActivitySource`'s exact static-instrument shape and `ConsumerBase.Consume`'s `using var activity = ActivitySource.StartActivity(...)` pattern; (2) **Streaming query vocabulary** — `IStreamQuery<TResponse>`/`IStreamQueryHandler<,>` over MediatR's already-pinned `IStreamRequest<TResponse>`/`IStreamRequestHandler<,>` (zero new NuGet dependency); locked decision: **no `Result<T>` wrapping** for streamed items (raw `TResponse` per item via `IAsyncEnumerable`, errors terminate the stream via thrown exception) — an explicit, documented deviation from this domain's `Result`-everywhere convention; explicit statement that none of the ten pipeline behaviors apply to this shape without a future deliberate extension; (3) **Resilience behavior** — new `IRetryableRequest` marker (mirrors `IAuthorizeRequest`/`IIdempotentRequest`) + `ResilienceBehavior<,>` backed by an externally-registered Polly v8 resilience pipeline (retry-with-backoff only this phase, circuit-breaking explicitly deferred); resolves the retry-after-partial-commit hazard by positioning Resilience to WRAP `IdempotentCommandBehavior` + `TransactionBehavior` (a retry re-runs the full duplicate-check-then-commit unit, never a bare second commit), with the `IRetryableRequest`-without-`IIdempotentRequest` gap documented as an accepted, non-compile-time-enforceable misuse; (4) **Reusable pipeline test harness** — internal to `SharedKernel.Application.Behaviors.Tests` only, never packaged, depends on Tracing and Resilience existing first; (5) **Write-side cache invalidation** — new `IInvalidatesCache` marker (mirrors `ICacheableQuery<TResponse>`'s self-supplied-key pattern) + `CacheInvalidationBehavior<,>` constrained to `ICommandBase`, calling `ICacheService.RemoveAsync`/`RemoveByTagAsync` only after `next()` succeeds, positioned innermost (after `TransactionBehavior`) so eviction only follows a confirmed commit, reusing `CachingBehavior`'s existing `ICacheService` `Build()`-time guard. Canonical pipeline order revised from seven to a ten-named-slot order: Logging → Metrics → Tracing → Validation → Authorization → Caching → Resilience → Idempotency → Transaction → CacheInvalidation, with full positional rationale for every new step; nine new Hard Violations added; AOT Compatibility and Test Rules sections extended for all five new capabilities; DI Registration example extended with the target end-state shape and three new usage examples (retryable+idempotent+invalidating command, streaming query) — all marked design-only pending Scaffold/Core implementation (application-arch-planner)
- [2026-07-01] WO-038 (P-231–P-234) dispatched — design-only, not yet implemented (see `state-map.md` D-26..D-35, C-30..C-50). Four phases: (1) **P-231 Bug fixes** — 8 self-contained correctness fixes: `IIdempotencyKeyStore.MarkProcessedAsync` fail-and-consume-key invariant XML doc; `ValidationBehavior` double-enumeration guard removal; `CachingBehavior` direct `await` on `ValueTask` (eliminates `.AsTask()` allocation); `ResilienceBehavior` non-generic `ResiliencePipeline` registration (prevents silent no-op on type-key mismatch); `MediatRDomainEventDispatcher` compile-time `Publish<T>` method capture (eliminates nullable-suppressed `GetMethod(...)!`); `CacheInvalidationBehavior` `InvalidateOnlyOnSuccess` opt-in flag with explicit default-always-invalidate XML docs; `TransactionBehavior` `OperationCanceledException` test; `typeof(TRequest).FullName ?? .Name` for all metric/log/trace key construction. (2) **P-232 Contract evolution** (depends on P-230): `LoggingBehavior` logs at `Warning` on `Result.Failure` via `IHasSuccessFlag`; `FailureResponseFactory` eliminates `Expression.Compile()` via `IResultOfT<T>` constraint removing `[RequiresUnreferencedCode]`; `IAuthorizationContext`/`IAuthorizeRequest` multi-requirement evolution — `AllOf(IEnumerable<string>)`/`AnyOf(IEnumerable<string>)` alongside single-string convenience. (3) **P-233 New capabilities**: `MediatRDomainEventDispatcherOptions.ParallelDispatch` opt-in (Task.WhenAll, AggregateException collection, serial default unchanged); `IFireAndForgetCommand` marker (extends `ICommand`, SharedKernel.Application) + `IFireAndForgetDispatcher`/`ChannelFireAndForgetDispatcher` + `FireAndForgetBackgroundConsumer` (BCL `Channel<T>`, configurable bounded capacity, DropAndLog/Block rejection policies) + `FireAndForgetGuardBehavior<,>` (rejects `ISender.Send` with actionable exception), all registered via `ApplicationBehaviorsBuilder.AddFireAndForgetDispatch()`. (4) **P-234 Streaming pipeline** (depends on P-232): five `IStreamPipelineBehavior<,>` implementations (StreamLogging/StreamMetrics/StreamTracing/StreamValidation/StreamAuthorization) registered via new `AddStreamingBehaviors()` builder method; non-applicable behaviors (Transaction/Caching/CacheInvalidation/Idempotency/Resilience) explicitly documented with rationale. Packages table updated; eleven new Hard Violations; AOT notes for Channel/IStreamPipelineBehavior; Test Rules extended with new WO-038 scenarios; DI Registration updated with new builder methods and usage examples (application-arch-planner)
- [2026-07-02] SK.05.Tests ● complete (T-17..T-32) — all 32 test tasks done; 28+92=120 tests passing (0 failed); "design-only" labels removed from Tracing/CacheInvalidation/Resilience/FireAndForget/StreamingBehaviors sections; WO-036 and WO-038 Hard Violations updated to remove design-only qualifiers; streaming behavior coverage note rewritten to reflect WO-038 P-234's five IStreamPipelineBehavior<,> implementations now shipped; root state-map P-231..P-234 → ● Complete (sync-brain)
- [2026-07-03] WO-039 dispatched (root P-237, P-238, P-239, P-241, P-242, P-243), design-only — a gap-closure work order, not a new capability tier. Two confirmed defects corrected in this file's OWN description of already-"Complete" WO-038 code, ahead of their actual fixes: (1) **"Constructing a generic failure response"** rewritten — the section previously described a "Target implementation (WO-038, P-232)" using a direct `IResultOfT<T>` generic constraint that was NEVER actually shipped; the real code (`ResultOfTDispatcher<TResponse>.BuildFactory()`) performs `GetInterfaces()`/`GetGenericArguments()`/`MakeGenericType()`/`GetMethod()`/`Invoke()` at runtime, cached once per closed `TResponse` type but genuinely reflective — this is now documented accurately as a confirmed, open gap (not a second sanctioned exception), with WO-039 P-237's planned fix (consuming `01.Core` P-236's new `IFailureFactory<TSelf>` CRTP contract, blocked until P-236 ships) described alongside it; the AOT Compatibility section's parallel paragraph corrected to match. (2) **Fire-and-Forget Dispatch section** gained a KNOWN BUG callout — `FireAndForgetGuardBehavior<,>` self-blocks `FireAndForgetBackgroundConsumer`'s own internal dispatch (cannot distinguish trusted internal dispatch from external misuse), making the documented `AddFireAndForgetDispatch()` wiring produce a fire-and-forget feature that silently never executes any command; consuming services should not adopt it until P-238 ships the planned fix (an internal, unspoofable `AsyncLocal<bool>`-backed trusted-dispatch marker). Four new capabilities documented, all design-only: `MetricsBehavior<,>` gains an `outcome` tag (P-239, mirrors `StreamMetricsBehavior`'s existing tag, backed by a new shared outcome-classification helper also consumed by `LoggingBehavior`); four already-built `00.Governance` architecture-test rule groups will be wired against this domain's real assemblies for the first time via a new test-project-only `SharedKernel.ArchitectureTests` reference (P-241, blocked on P-237 + P-239 + a companion `00.Governance` phase); `IIdempotencyResponseStore` adds purely-additive, opt-in idempotency response replay so a duplicate submission can replay the original outcome instead of always returning `Error.Conflict` (P-242, `System.Text.Json`-backed, existing `IIdempotencyKeyStore`-only stores fully unaffected); `ApplicationBehaviorsBuilder.AddDefaultBehaviors()` adds a one-line onboarding preset for the four behaviors carrying zero `Build()`-time missing-dependency guard (Logging/Metrics/Tracing/Validation), provably equivalent to and composable with the existing individual calls (P-243). Packages table, DI Registration section (preset example + response-store bridging example), Hard Violations (10 new entries), Test Rules (7 new entries), and top-of-file status callout all updated; `05.Application/state-map.md` carries the full 70-task breakdown (application-arch-planner)
- [2026-07-03] WO-039 Design phase fully locked (D-46..D-48 → `●`, second session this date) — P-241's three remaining Design tasks completed as forward-looking planning: D-46 locks the `SharedKernel.Application.Tests`/`SharedKernel.Application.Behaviors.Tests` → `00.Governance/SharedKernel.ArchitectureTests` `ProjectReference` as test-project-only; D-47 locks the invocation plan (which rule-group method targets which real assembly) while explicitly flagging that `ReflectionGuardRules.NoMakeGenericMethodReflection` and `MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag` are expected to fail when actually run until `00.Governance` P-240 and this domain's own P-239 Core phase ship, respectively; D-48 formalizes the P-237/P-239/P-240 convergence rule. Re-verified `00.Governance` P-240 is at 0/5 tasks (not 1/5 as a prior session's note mis-stated) — P-241's Core/Tests/Docs phases remain genuinely blocked; only the Design/plan is now locked. `05.Application/state-map.md`'s `SK.05.Design` phase key is now 58/58 `●` (application-phase-implementer)
- [2026-07-03] SK.05.Scaffold ● complete (S-17, last remaining task, WO-039 P-241) — confirmed `00.Governance/SharedKernel.ArchitectureTests` exists as a buildable, referenceable project; added a test-project-only `ProjectReference` to it in both `SharedKernel.Application.Tests.csproj` and `SharedKernel.Application.Behaviors.Tests.csproj` per D-46; bumped `FluentAssertions` from 8.4.0 to 8.10.0 in both test projects to resolve an `NU1605` package-downgrade error caused by `SharedKernel.ArchitectureTests`'s own 8.10.0 pin; `dotnet build` confirms 0 errors across both test projects and both production `.csproj` files, and confirms (via reference scan) zero leakage of the `00.Governance` reference into either production `.csproj`. `05.Application/state-map.md`'s `SK.05.Scaffold` phase key is now 17/17 `●` across all four work orders (WO-035/036/038/039); Core phase for WO-039 (P-237/P-238/P-239/P-242/P-243) can now begin, though P-241's own Core phase remains blocked on `00.Governance` P-240 plus this domain's P-237/P-239 Core phases (application-phase-implementer)
- [2026-07-03] WO-039 Design phase locked (D-36..D-45, D-49..D-58 → `●`; D-46..D-48 remain `○`, genuinely blocked). Verified `01.Core` P-236 shipped `IFailureFactory<TSelf>` (`SK.01.P236` fully `●` in `01.Core/state-map.md`) — P-237's blocker is cleared. Rewrote "Constructing a generic failure response": removed the "current shipped (reflective)" vs. "planned fix, blocked" framing now that the blocker is gone, replaced with a single locked rework direction (`IFailureFactory<TResponse>` invoked directly, `GetInterfaces()`/`GetGenericArguments()` eliminated by construction, `MakeGenericType()`/`GetMethod()`/`Invoke()` to be eliminated at Core time; `AuthorizationBehavior`/`IdempotentCommandBehavior` generic shapes unchanged; escalate to arch-lead if that proves false) — ready for Core-phase implementation, no remaining design blocker. Corrected the same blocker language in the AOT Compatibility section and the top-of-file WO-039 status callout. Verified `00.Governance` P-240 is NOT complete (`SK.00.DomainEventDispatcherReflectionExemption`: only `D-58` is `●`; `C-95`/`T-169`/`T-170`/`DO-30` remain `○`) — P-241 (D-46..D-48) is correctly left undesigned pending that convergence; the section's blocked-status framing is unchanged since the blocker itself is unchanged. P-238/P-239/P-242/P-243 design content (already fully written in a prior session) verified accurate against the real `ApplicationBehaviorsBuilder.cs` — no corrections needed; marked `●` (application-phase-implementer)
- [2026-07-06] SK.05.Tests ◐ 47/52 (WO-039 T-33..T-39, T-45..T-52 → ●; T-40..T-44 remain ○/blocked) — new test files: `Shared/FailureResponseFactoryReflectionShapeTests.cs` (Mono.Cecil IL-shape assertion + 3 behavioral tests, T-33/T-34), `Idempotency/IdempotentCommandBehaviorReplayTests.cs` (T-46/T-47/T-48, plus a `Result<T>` round-trip sanity test); `Metrics/MetricsBehaviorTests.cs` extended with `outcome` tag assertions (T-38); `Extensions/ApplicationBehaviorsBuilderTests.cs` extended with three `AddDefaultBehaviors()` tests (T-50/T-51/T-52). T-35/T-36/T-37/T-39/T-45 confirmed already satisfied by existing tests shipped in the prior Core-phase session — no new files needed for those. Re-verified `00.Governance` `SK.00.DomainEventDispatcherReflectionExemption` still 0/5 — T-40..T-44 (P-241) left unattempted per phase-spec instruction. Test Rules section's P-237 entry rewritten from "pending" to "shipped," documenting the delegate-Invoke-vs-reflection-Invoke IL-shape gotcha discovered while writing the test. 107/107 `SharedKernel.Application.Behaviors.Tests` passing, 28/28 `SharedKernel.Application.Tests` passing (application-phase-implementer)
- [2026-07-06] SK.05.Core ● complete (65/65, WO-039 C-51..C-65) — CLAUDE.md brought current with the actually-shipped code. Two bugs found and fixed during implementation, neither present in the locked design: (1) `ResultOfTDispatcher<TResponse>`'s static field declaration order (`Factory` declared before the `InvokeFailureMethod` it depends on) caused a `NullReferenceException`/`TypeInitializationException` on first use — fixed by reordering; (2) a test-file argument-binding bug in the rewritten `FireAndForgetDispatcherTests` (a lambda intended for `configureOptions` bound positionally to `configureHandlers` instead) — fixed by naming the argument. One `MakeGenericMethod`+`CreateDelegate` call remains in `FailureResponseFactory` (verified irreducible via a standalone CS0314 compile test), disclosed as a second dispatch of `MediatRDomainEventDispatcher`'s already-approved shape — an open item for arch-lead to decide. 121/121 tests passing (28 + 93). Root `CLAUDE.md` not touched — internal implementation detail, not a new package/layering/technology decision (application-phase-implementer, sync-brain)
- [2026-07-06] SK.05.Tests → 50/52 (WO-039 P-241, third pass) — `ApplicationPipelineRules`/`PipelineOrderAssertion`/`MetricsInstrumentationRules` real-assembly invocations shipped in new `Governance/RealAssemblyArchitectureRulesTests.cs`, confirmed independent of the `00.Governance` P-240 blocker by reading the rule implementations directly; only `ReflectionGuardRules.NoMakeGenericMethodReflection` (T-40) and the full-suite gate (T-44) remain blocked on P-240 (still 0/5). Root `CLAUDE.md` not touched — internal test-coverage detail (application-phase-implementer, sync-brain)
- [2026-07-07] WO-040 (root P-246) dispatched — design-only, not yet implemented (see `state-map.md` D-59..D-65). A single, self-contained gap-closure capability: opt-in structured request/response payload logging via a new `ILoggableRequest<TResponse>` marker interface (`Logging/`, `SharedKernel.Application.Behaviors`) — `LoggableRequestFields` (self-supplied dictionary, mirroring `ICacheableQuery<TResponse>.CacheKey`/`IInvalidatesCache.CacheKeysToInvalidate`'s exact self-supplied-field precedent) plus `GetLoggableResponseFields(TResponse response)` (invoked only after `next()` returns normally, never on a thrown exception). Placement decision locked: extend the existing `LoggingBehavior<,>` **in place** via an additive `is ILoggableRequest<TResponse>` branch attaching `ILogger.BeginScope(...)` around the entry/completion log lines — deliberately NOT an eleventh pipeline slot or a second logging pathway, so the canonical ten-step order is unchanged and a request that does not opt in produces byte-for-byte identical logging behavior to today (to be mechanically enforced by a dedicated regression test, T-57, not just code review). Zero new NuGet dependency (`ILogger.BeginScope` is BCL), zero new reflection. Closes the exact gap `LoggingBehavior`'s own documentation has flagged as deliberately deferred since WO-035 ("opt-in payload logging is a future capability, not part of this phase"). Packages table, LoggingBehavior interface contract section, Hard Violations (4 new entries), Test Rules (1 new entry), and top-of-file status callout all updated; `05.Application/state-map.md` carries the full 22-task breakdown across all six phases (application-arch-planner)
- [2026-07-07] Doc-consistency closure (companion to `13.ServiceDefaults` P-247, no code change here): the `ApplicationDiagnostics.ActivitySource` forward-reference callout in the Metrics section rewritten from "13.ServiceDefaults (future work, out of scope here) registers..." to "CLOSED (P-247, 13.ServiceDefaults)" — `13.ServiceDefaults.WithApplicationTelemetry()` now wires the `"SharedKernel.Application"` Meter/ActivitySource pair into the host's OTel `MeterProvider`/`TracerProvider`, mirroring `07.Messaging`'s `MessagingDiagnostics.ActivitySource`/`WithMessagingTelemetry()` (P-132/P-172) precedent exactly. This domain's layering is unchanged — it still never references `13.ServiceDefaults` (application-arch-planner)
