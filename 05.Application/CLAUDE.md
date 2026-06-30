# 05.Application — Application Layer

## What This Domain Is

The MediatR-based CQRS plumbing layer. Every command, query, streaming query, domain-event handler, and cross-cutting pipeline concern (validation, logging, metrics, tracing, transactions, caching, cache invalidation, authorization, idempotency, resilience) in a downstream microservice derives from the contracts defined here. This domain may reference `01.Core`, `02.Caching` (abstractions only), `03.Domain`, and `04.Contracts` — it must never reference `06.Persistence`, `07.Messaging`, `12.Security`, or any concrete infrastructure package.

> **Local-seam bridging is the load-bearing pattern in this domain.** `TransactionBehavior` (`IUnitOfWork`), `AuthorizationBehavior` (`IAuthorizationContext`), and `IdempotentCommandBehavior` (`IIdempotencyKeyStore`) each define a minimal interface owned by this package, never a direct reference to the "real" infrastructure (`06.Persistence`, `12.Security`, `07.Messaging` respectively). The consuming service bridges each local seam to its real implementation at the composition root. This is the same pattern applied three times, not three different patterns.
>
> **WO-036 status (2026-06-30): design-only.** `TracingBehavior`, the streaming query vocabulary (`IStreamQuery<TResponse>`/`IStreamQueryHandler<,>`), `ResilienceBehavior`, `CacheInvalidationBehavior`, and the reusable pipeline test harness are documented below as the target design but are **not yet implemented** — see `05.Application/state-map.md` Design phase (D-11..D-25) for current status before treating any WO-036 type as available in code.

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
| `SharedKernel.Application` | `ICommand`, `ICommand<TResponse>`, `IQuery<TResponse>`, `ICommandHandler<>`, `ICommandHandler<,>`, `IQueryHandler<,>`, `IStreamQuery<TResponse>`, `IStreamQueryHandler<,>`, `IDomainEventHandler<TDomainEvent>`, `DomainEventNotification<TDomainEvent>`, `MediatRDomainEventDispatcher` — the MediatR vocabulary (including the streaming-query vocabulary, WO-036) and the domain-event-to-MediatR bridge | `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Domain`, `MediatR` |
| `SharedKernel.Application.Behaviors` | `ValidationBehavior<,>`, `LoggingBehavior<,>`, `MetricsBehavior<,>`, `TracingBehavior<,>` (WO-036), `TransactionBehavior<,>` + `IUnitOfWork`, `CachingBehavior<,>` + `ICacheableQuery<TResponse>`, `CacheInvalidationBehavior<,>` + `IInvalidatesCache` (WO-036), `AuthorizationBehavior<,>` + `IAuthorizationContext`/`IAuthorizeRequest`, `IdempotentCommandBehavior<,>` + idempotency seam/`IIdempotentRequest`, `ResilienceBehavior<,>` + `IRetryableRequest` (WO-036) — opt-in MediatR pipeline behaviors (ten total once WO-036 lands) | `SharedKernel.Application`, `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Caching.Abstractions`, `MediatR`, `FluentValidation`, Polly v8 resilience pipeline (WO-036, see Technology Stack) |

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

#### Streaming query vocabulary (`Streaming/`) — WO-036, design-only

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

NO PIPELINE BEHAVIOR COVERAGE (explicit, not an oversight): none of this domain's ten pipeline behaviors
apply to IStreamQuery<TResponse> as of WO-036. ValidationBehavior<TRequest,TResponse>'s constraint
(TRequest : IRequest<TResponse>) does not match IStreamRequest<TResponse> — MediatR treats unary and
streaming requests as two separate generic hierarchies with no shared base. Logging/Metrics/Tracing/
Caching/Authorization/etc. would each need a SEPARATE streaming-shaped sibling behavior
(IPipelineBehavior<,> has no streaming equivalent in MediatR 12.4.x; that is MediatR's own
IStreamPipelineBehavior<,>, a distinct interface this domain has not yet adopted). Extending any behavior
to streaming is an explicit, deliberate FUTURE phase — never silently assumed to already work just because
the unary behavior exists. A service consuming IStreamQuery<TResponse> today gets zero cross-cutting
coverage (no logging, no metrics, no validation) unless and until that future phase ships.
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

#### DI extensions for `SharedKernel.Application` (`Extensions/`)

```text
AddSharedKernelApplication(IServiceCollection services)            → IServiceCollection
    — Registers IDomainEventDispatcher → MediatRDomainEventDispatcher (scoped).
    — Does NOT call services.AddMediatR(...) — the consuming service owns MediatR registration and
      assembly scanning (RegisterServicesFromAssembly). This extension only adds the dispatcher bridge.

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
    NOTE: Injects ILogger<TRequest>. Logs Information at start ("Handling {RequestName}") and at successful
          completion ("Handled {RequestName} in {ElapsedMilliseconds}ms"), timed via
          Stopwatch.GetTimestamp/GetElapsedTime (no Stopwatch allocation). On exception: logs Error with
          the exception and elapsed time, then rethrows unchanged — never swallows, mirroring
          07.Messaging's ConsumerBase.Consume log-then-rethrow contract.
          Does NOT log request or response payloads by default (PII risk in command/query parameters) —
          opt-in payload logging is a future capability, not part of this phase.
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
          13.ServiceDefaults (future work, out of scope here) registers the "SharedKernel.Application"
          meter/source name with the host's MeterProvider/TracerProvider — this domain never reaches into
          13.ServiceDefaults. ActivitySource is named and versioned identically to Meter (same string
          "SharedKernel.Application", same "1.0.0") so both instruments share one logical diagnostics
          identity — not two different platform names for the same package.

MetricsBehavior<TRequest, TResponse>  (sealed class, implements IPipelineBehavior<TRequest, TResponse>)
    where TRequest : IRequest<TResponse>
    NOTE: Records ApplicationDiagnostics.RequestDuration exactly once per request, tagged with
          request.name = typeof(TRequest).Name, via a try/finally around next() so the measurement is
          recorded whether the inner pipeline succeeds, returns a Result.Failure, or throws.
```

#### Tracing (`Tracing/`) — WO-036, design-only

```text
TracingBehavior<TRequest, TResponse>  (sealed class, implements IPipelineBehavior<TRequest, TResponse>)
    where TRequest : IRequest<TResponse>
    NOTE: A DISTINCT behavior from MetricsBehavior, not folded into it — single-responsibility (one behavior
          measures, one behavior traces), even though both occupy the same outermost-but-inside-Logging band
          and both bracket next() in a try/finally-equivalent shape. Mirrors 07.Messaging's
          ConsumerBase.Consume/MassTransitEventPublisher.Publish shape exactly:
              using var activity = ApplicationDiagnostics.ActivitySource.StartActivity("Request.Handle");
              activity?.SetTag("request.name", typeof(TRequest).Name);
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
            (a) a future 06.Persistence work order has EfUnitOfWork additionally implement this interface
                directly, or
            (b) the composition root registers a one-line scoped adapter delegating to
                SharedKernel.Persistence.Abstractions.IUnitOfWork (see DI Registration below).

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

#### Cache Invalidation (`CacheInvalidation/`) — WO-036, design-only; the write-side counterpart to Caching above

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
          constraint shape — never applies to queries (a DI-level fact). Calls next() first; only on success
          (the inner pipeline returned without throwing — same "did not fault" signal TransactionBehavior
          uses, NOT an inspection of Result.IsSuccess/IsFailure, for the identical reason TransactionBehavior
          does not inspect it: a Result.Failure is a valid, deliberate handler outcome, and whatever the
          handler already staged or didn't stage is the handler's own decision) does it call
          ICacheService.RemoveAsync(key, ct) once per declared CacheKeysToInvalidate entry (and/or
          RemoveByTagAsync per declared tag). NEVER calls removal on a thrown exception — a faulted handler
          mutated nothing (or its mutation never committed, since this behavior runs after
          TransactionBehavior — see Pipeline Composition below), so there is nothing to evict. Zero direct
          reference to SharedKernel.Caching.FusionCache/SharedKernel.Caching.Redis or any concrete provider —
          SharedKernel.Caching.Abstractions only, the exact same reference shape CachingBehavior already
          established. Positioned innermost, immediately after TransactionBehavior — eviction must follow a
          CONFIRMED commit, never a speculative one; running invalidation before the transaction commits
          risks evicting a cache entry for a mutation that ultimately rolled back.
```

#### Authorization (`Authorization/`)

```text
IAuthorizationContext  (interface — local seam, owned by this package)
    .IsAuthorizedAsync(string requirement, CancellationToken ct)     → Task<bool>
    NOTE: This is NOT SharedKernel.Security.Abstractions.IUserContext, and this package carries no project
          reference to 12.Security — the layering ceiling for 05.Application is 01–04. Exposes only the
          minimal shape AuthorizationBehavior needs ("does the current caller satisfy this requirement").
          The consuming service bridges this seam to its real IUserContext/ITenantProvider at the
          composition root — the EXACT same bridging pattern TransactionBehavior's local IUnitOfWork
          already established for 06.Persistence.Abstractions.IUnitOfWork. requirement is an opaque string
          (permission/policy name) whose meaning is entirely owned by the consuming service's bridge
          implementation — this package never interprets it.

IAuthorizeRequest  (interface)
    .Requirement                                                    → string
    NOTE: Marker a command or query implements to declare it requires an authorization check before its
          handler runs. The requirement string is whatever minimal shape (permission/policy name) the
          consuming service's IAuthorizationContext bridge understands. Requests that do NOT implement this
          marker skip AuthorizationBehavior entirely — a DI/runtime fact (the behavior never resolves into
          that request's pipeline), not a config flag or an if-check inside the behavior. Can be implemented
          by both commands and queries — unlike Transaction/Idempotency, authorization is not commands-only;
          queries can require permission checks too (e.g. "view another tenant's data").

AuthorizationBehavior<TRequest, TResponse>  (sealed class, implements IPipelineBehavior<TRequest, TResponse>)
    where TRequest : IAuthorizeRequest, IRequest<TResponse>
    NOTE: Calls IAuthorizationContext.IsAuthorizedAsync(request.Requirement, ct). On false, short-circuits
          WITHOUT calling next() and returns Result.Failure(Error.Unauthorized(...)) — for both Result and
          Result<T> response shapes — NEVER throws. This is consistent with this domain's existing rule that
          exceptions are reserved for ValidationException (the one deliberate behavior short-circuit) and
          genuinely unexpected faults; an unauthorized caller is an expected, foreseeable outcome. On true,
          calls next() and returns its result unchanged. Runs after ValidationBehavior (reject malformed
          input before spending a permission check) and before CachingBehavior/TransactionBehavior (never
          let an unauthorized request reach a cache lookup or a mutation).
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
          Calls IIdempotencyKeyStore.HasProcessedAsync(request.IdempotencyKey, ct) first. On true (duplicate),
          short-circuits WITHOUT calling next() a second time and returns Result.Failure(Error.Conflict(...))
          — the documented behavior when the prior result is not retrievable (this seam intentionally does
          NOT cache/replay the original response payload; that is a deliberate scope boundary, not an
          oversight — replaying a stored Result<T> payload would require a second, generic-serialization
          concern this behavior does not take on). On false, calls next(), then on success calls
          MarkProcessedAsync(request.IdempotencyKey, ct) so the key is recorded only after a successful
          handler run — a faulted handler must be safely retryable with the same key. Runs innermost,
          immediately wrapping the handler-and-commit boundary — positioned after CachingBehavior (queries
          never reach this behavior anyway) and immediately before TransactionBehavior, so a duplicate is
          rejected before TransactionBehavior's SaveChangesAsync would run a second time for the same
          logical operation.
```

#### Resilience (`Resilience/`) — WO-036, design-only

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

#### DI extensions for `SharedKernel.Application.Behaviors` (`Extensions/`)

```text
AddSharedKernelApplicationBehaviors(IServiceCollection services)   → ApplicationBehaviorsBuilder

ApplicationBehaviorsBuilder
    .AddValidationBehavior()
        — Registers ValidationBehavior<,> against the open generic IPipelineBehavior<,>.
        — Requires IValidator<T> implementations registered separately (FluentValidation's own
          AddValidatorsFromAssembly or explicit registration) — optional at the per-request-type level;
          omitted entirely, every request simply skips validation (no-op, not an error).
    .AddLoggingBehavior()
    .AddMetricsBehavior()
    .AddTracingBehavior()                                          — WO-036, design-only
        — No missing-dependency guard needed: ApplicationDiagnostics.ActivitySource is always available
          (BCL static instance), exactly like AddMetricsBehavior()/AddLoggingBehavior() carry no guard.
    .AddTransactionBehavior()
        — Build() throws InvalidOperationException if IUnitOfWork is not registered in IServiceCollection
          when this was called — mirrors 07.Messaging's WithIdempotency() missing-store guard.
    .AddCachingBehavior()
        — Build() throws InvalidOperationException if SharedKernel.Caching.Abstractions.ICacheService is
          not registered when this was called.
    .AddCacheInvalidationBehavior()                                — WO-036, design-only
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
    .AddResilienceBehavior()                                       — WO-036, design-only
        — Build() throws InvalidOperationException if no ResiliencePipelineProvider/ResiliencePipeline is
          registered/supplied when this was called — same missing-dependency guard shape as the other four.
    .Build()                                                       → IServiceCollection
        — Registers only the behaviors that were opted into, ALWAYS in the fixed pipeline order below
          regardless of .AddXBehavior() call order — mirrors 06.Persistence's "platform three interceptors
          always fire first" precedent (non-negotiable ordering, not configurable).
        — Must NOT call services.AddMediatR() — the consuming service already registers MediatR; this
          builder only appends behaviors via services.AddTransient(typeof(IPipelineBehavior<,>), ...).
```

---

## Implementation Rules

### Pipeline composition (canonical order)

> **WO-036 status: design-only.** The ten-named-slot order below is the target design recorded for implementation; `TracingBehavior`, `ResilienceBehavior`, and `CacheInvalidationBehavior` are not yet built (see `05.Application/state-map.md` Core phase C-18..C-29). Until WO-036's Core phase lands, the pipeline actually registered by `ApplicationBehaviorsBuilder.Build()` remains the seven-step WO-035 order (Logging → Metrics → Validation → Authorization → Caching → Idempotency → Transaction).

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

Both `AuthorizationBehavior<TRequest,TResponse>` and `IdempotentCommandBehavior<TRequest,TResponse>` must short-circuit with a failed response of type `TResponse`, where `TResponse` is statically only known to be `IRequest<TResponse>`'s response type — at runtime it is either the non-generic `Result` (struct) or a closed `Result<T>` (sealed class), and there is no shared interface linking the two (adding one to `SharedKernel.Primitives` is out of scope for this domain). `Shared/FailureResponseFactory.cs` (`internal static class FailureResponseFactory`) solves this with `TResponse Create<TResponse>(Error error)`: it special-cases `TResponse == typeof(Result)` directly, and for the `Result<T>` case builds a small `Expression` tree per distinct closed `TResponse` type that performs the implicit `Error → Result<T>` conversion, compiles it once into a `Func<Error,object>`, and caches it in a static `ConcurrentDictionary<Type,Func<Error,object>>` keyed by `TResponse`. This is the **same documented, justified exception** to the platform-wide `MakeGenericMethod`/reflection prohibition already used by `MediatRDomainEventDispatcher` — "build once per concrete `Type`, cache forever, invoke directly thereafter" — except built via `Expression` compilation (the governance rule's own recommended alternative to reflection) rather than `MakeGenericMethod`. It deliberately does **not** use `dynamic`/`Microsoft.CSharp` — that approach was tried and rejected because it pulls in a new NuGet dependency this domain has no other reason to carry and is not AOT/trim-safe. Do not replace this with `dynamic` or raw reflection, and do not duplicate this pattern elsewhere without updating this note — `FailureResponseFactory` is the single chosen answer to "construct an arbitrary `Result`/`Result<T>` failure from an `Error` when `TResponse` is generic," reused by both behaviors.

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
- Any static mutable state. **Exception:** the static `readonly Meter` / `Histogram<double>` pair in `ApplicationDiagnostics` — the same platform-standard diagnostics-instrument pattern already approved for `07.Messaging.MessagingDiagnostics.ActivitySource`. Do not add further ad-hoc static fields under cover of this exception.
- Adding a project reference from `SharedKernel.Application` to `SharedKernel.Caching.Abstractions` — that reference belongs to `SharedKernel.Application.Behaviors` only (where `ICacheableQuery<TResponse>` / `CachingBehavior` live), so services with no caching needs never pull it in transitively through the base package.
- Throwing `NotImplementedException` or a raw `Exception` for an expected/foreseeable failure inside a handler — return `Result.Failure(Error.X(...))` (`NotFound`, `Conflict`, `BusinessRule`, `Unauthorized`, etc.). Exceptions are reserved for `ValidationException` (the one deliberate behavior short-circuit) and genuinely unexpected faults.
- Adding a second public constructor to `MediatRDomainEventDispatcher` — exactly one constructor accepting `IPublisher`, mirroring `EfUnitOfWork`'s single-constructor rule in `06.Persistence` (avoids the same class of DI resolution ambiguity).
- Injecting `SharedKernel.Security.Abstractions.IUserContext` or `ITenantProvider` directly into `AuthorizationBehavior` or anywhere else in this domain — `05.Application`'s layering ceiling is `01–04`; `12.Security` is never referenced. Use the local `IAuthorizationContext` seam; the consuming service bridges it to the real `IUserContext`/`ITenantProvider` at the composition root.
- `AuthorizationBehavior` throwing instead of short-circuiting with `Result.Failure(Error.Unauthorized(...))` — an unauthorized caller is an expected, foreseeable outcome, not a fault; exceptions stay reserved for `ValidationException` and genuinely unexpected faults.
- Referencing `SharedKernel.Messaging.Abstractions.IIdempotencyStore` directly from `IdempotentCommandBehavior` or anywhere else in this domain — use the local idempotency seam (`IIdempotencyKeyStore`); `07.Messaging` is never referenced by `05.Application`.
- Constraining `IdempotentCommandBehavior<TRequest,TResponse>` to anything other than `ICommandBase` — idempotency is commands-only, mirroring `TransactionBehavior`'s exact constraint; a query type must never satisfy `IIdempotentRequest`'s applicability.
- `IdempotentCommandBehavior` calling `next()` more than once for the same request, or calling `MarkProcessedAsync` before `next()` succeeds — a faulted handler must remain safely retryable with the same idempotency key.
- Reordering the canonical pipeline (currently the seven-step WO-035 order; the ten-named-slot WO-036 order once implemented) without updating the documented positional rationale for every affected step.
- **(WO-036, design-only)** Hand-rolling a retry/backoff loop inside a handler or behavior now that `ResilienceBehavior` exists as the platform alternative — this extends the existing `System.Random`/`DateTime.UtcNow` prohibition above to retry/backoff specifically; use `IRetryableRequest` + `ApplicationBehaviorsBuilder.AddResilienceBehavior(...)` instead.
- **(WO-036, design-only)** A command implementing `IRetryableRequest` without also implementing `IIdempotentRequest` — this is the documented, accepted gap in the retry-after-partial-commit hazard resolution (not mechanically enforceable across two independent marker interfaces); code review must catch this, the compiler will not.
- **(WO-036, design-only)** Folding `TracingBehavior` into `MetricsBehavior` (or vice versa) — they are deliberately separate, single-responsibility behaviors occupying adjacent pipeline positions, not one merged behavior.
- **(WO-036, design-only)** `CacheInvalidationBehavior` calling `ICacheService.RemoveAsync`/`RemoveByTagAsync` before `TransactionBehavior` has confirmed a commit, or on a thrown exception/`Result.Failure` — invalidation must follow a confirmed commit only; evicting speculatively risks invalidating a cache entry for a mutation that never actually persisted.
- **(WO-036, design-only)** A query type implementing `IInvalidatesCache`, or a command type implementing `ICacheableQuery<TResponse>` — the read/write caching marker pair remains strictly query-only / command-only respectively, exactly like every other shape-constrained marker in this domain.
- **(WO-036, design-only)** Wrapping a streamed `IStreamQuery<TResponse>` item in `Result<TResponse>` "for consistency" with the rest of the domain — this is a deliberate, documented deviation; do not silently retrofit the `Result` railway onto the streaming vocabulary.
- **(WO-036, design-only)** Assuming any of the ten pipeline behaviors apply to `IStreamQuery<TResponse>` without a separate, deliberate extension phase — `ValidationBehavior`'s constraint does not match `IStreamRequest<TResponse>` today, and none of the others have been extended either.

---

## DI Registration (expected shape)

```csharp
// Composition root (consuming service) — MediatR registration is NOT this package's responsibility
services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
services.AddValidatorsFromAssemblyContaining<Program>();   // FluentValidation's own scanning, not ours

// Domain-event-to-MediatR bridge
services.AddSharedKernelApplication();   // IDomainEventDispatcher -> MediatRDomainEventDispatcher (scoped)
services.AddDomainEventHandler<OrderPlacedDomainEvent, OrderPlacedDomainEventHandler>();

// Opt-in pipeline behaviors — fixed execution order regardless of call order (see Pipeline Composition)
// WO-036 additions (AddTracingBehavior/AddResilienceBehavior/AddCacheInvalidationBehavior) are
// design-only as of 2026-06-30 — not yet implemented; shown here for the target end-state shape.
services
    .AddSharedKernelApplicationBehaviors()
    .AddLoggingBehavior()
    .AddMetricsBehavior()
    .AddTracingBehavior()          // WO-036, design-only — no missing-dependency guard (BCL ActivitySource)
    .AddValidationBehavior()
    .AddAuthorizationBehavior()    // requires IAuthorizationContext registered
    .AddCachingBehavior()          // requires SharedKernel.Caching.Abstractions.ICacheService registered
    .AddCacheInvalidationBehavior()// WO-036, design-only — reuses the ICacheService guard above
    .AddResilienceBehavior()       // WO-036, design-only — requires a ResiliencePipelineProvider registered
    .AddIdempotencyBehavior()      // requires IIdempotencyKeyStore registered
    .AddTransactionBehavior()      // requires SharedKernel.Application.Behaviors.IUnitOfWork registered
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

// An authorized, idempotent command
public sealed record PlaceOrderCommand(string IdempotencyKey, Guid CustomerId, decimal Total)
    : ICommand<Guid>, IAuthorizeRequest, IIdempotentRequest
{
    public string Requirement => "orders:create";
}

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
- `MediatRDomainEventDispatcher`'s per-event-type dispatch is the one documented `MakeGenericMethod` exception in this domain — cached once per concrete `Type` in a static `ConcurrentDictionary<Type, Delegate>`, not a per-call reflection cost. Identical, already-approved shape to `07.Messaging`'s `MassTransitEventPublisher` bridge.
- `AddDomainEventHandler<TDomainEvent, THandler>()` is a closed generic at the call site — the consuming service supplies both type arguments at compile time; zero reflection.
- `ValidationBehavior<,>`, `LoggingBehavior<,>`, `MetricsBehavior<,>`, `TransactionBehavior<,>`, `CachingBehavior<,>`, `AuthorizationBehavior<,>`, `IdempotentCommandBehavior<,>`, and (WO-036, design-only) `TracingBehavior<,>`, `ResilienceBehavior<,>`, `CacheInvalidationBehavior<,>` are closed generic sealed classes; MediatR resolves the open generic `IPipelineBehavior<,>` registration to a closed generic per request type via ordinary DI generics at runtime, not `MakeGenericType` per call.
- `ApplicationDiagnostics.Meter` / `Histogram<double>` are BCL `System.Diagnostics.Metrics` — fully AOT-safe, no reflection.
- **(WO-036, design-only)** `ApplicationDiagnostics.ActivitySource` is BCL `System.Diagnostics.ActivitySource` — fully AOT-safe, no reflection, identical AOT posture to the existing `Meter`. `TracingBehavior`'s `using var activity = ActivitySource.StartActivity(...)` is the same zero-reflection BCL call already proven by `07.Messaging`'s `ConsumerBase.Consume`/`MassTransitEventPublisher.Publish`.
- **(WO-036, design-only)** `IStreamQuery<TResponse>`/`IStreamQueryHandler<,>` are pure interface aliases over MediatR's own `IStreamRequest<TResponse>`/`IStreamRequestHandler<,>` — zero reflection beyond whatever AOT posture MediatR's `IStreamMediator`/`ISender.CreateStream` already carries (verify on each MediatR major upgrade, same caveat as the existing unary `IRequestHandler<,>` resolution).
- **(WO-036, design-only)** `ResilienceBehavior<,>`'s Polly v8 resilience pipeline execution (`ResiliencePipeline.ExecuteAsync(...)`) is Polly v8's own delegate-based, reflection-free execution model — no `MakeGenericMethod`, no `Activator.CreateInstance`. Verify Polly v8's own AOT compatibility status on first integration (Scaffold phase, `state-map.md` S-09) and on each major upgrade thereafter.
- `MediatR`'s own assembly-scanning registration (`AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...))`) is reflection-based and is the **consuming service's** responsibility, performed once at startup — not part of this package, not a hot path. Verify MediatR's own AOT compatibility status on each major upgrade.
- `FluentValidation`'s `IValidator<T>` resolution uses ordinary DI generics — no reflection at the `ValidationBehavior` call site. FluentValidation's own rule-building DSL may use expression trees internally; verify on each major upgrade.
- No `Activator.CreateInstance`, `Assembly.Load`, or dynamic reflection anywhere else in this domain's hot paths.
- `FailureResponseFactory`'s per-`TResponse` `Expression.Lambda(...).Compile()` is the second documented, justified exception to the platform-wide `MakeGenericMethod`/reflection prohibition in this domain (the first being `MediatRDomainEventDispatcher`) — built once per distinct closed `TResponse` type and cached in a static `ConcurrentDictionary<Type, Func<Error, object>>`, never `dynamic`/`Microsoft.CSharp`. `Expression.Compile()` itself has the same Native AOT caveat as any other runtime-codegen path (the interpreter fallback engages without a JIT); since `TResponse` is closed at every call site (`Result` or a closed `Result<T>`) and the cache is populated lazily on first use per type, this is a narrow, well-understood AOT caveat — not a `dynamic`/DLR dependency. Services targeting Native AOT that opt into `.AddAuthorizationBehavior()`/`.AddIdempotencyBehavior()` should verify this path at publish time; services that only use the other five behaviors are unaffected.

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
- **Standard test package set:** `xunit` 2.9.3, `xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.13.0, `coverlet.collector` 6.0.4, `FluentAssertions` 8.4.0, `NSubstitute` 5.3.0 — same pins already used across `06.Persistence`/`07.Messaging` unless a documented reason requires otherwise.
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
