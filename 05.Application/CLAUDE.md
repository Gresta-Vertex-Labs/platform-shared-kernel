# 05.Application — Application Layer

## What This Domain Is

The MediatR-based CQRS plumbing layer. Every command, query, domain-event handler, and cross-cutting pipeline concern (validation, logging, metrics, transactions, caching, authorization, idempotency) in a downstream microservice derives from the contracts defined here. This domain may reference `01.Core`, `02.Caching` (abstractions only), `03.Domain`, and `04.Contracts` — it must never reference `06.Persistence`, `07.Messaging`, `12.Security`, or any concrete infrastructure package.

> **Local-seam bridging is the load-bearing pattern in this domain.** `TransactionBehavior` (`IUnitOfWork`), `AuthorizationBehavior` (`IAuthorizationContext`), and `IdempotentCommandBehavior` (`IIdempotencyKeyStore`) each define a minimal interface owned by this package, never a direct reference to the "real" infrastructure (`06.Persistence`, `12.Security`, `07.Messaging` respectively). The consuming service bridges each local seam to its real implementation at the composition root. This is the same pattern applied three times, not three different patterns.

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
| `SharedKernel.Application` | `ICommand`, `ICommand<TResponse>`, `IQuery<TResponse>`, `ICommandHandler<>`, `ICommandHandler<,>`, `IQueryHandler<,>`, `IDomainEventHandler<TDomainEvent>`, `DomainEventNotification<TDomainEvent>`, `MediatRDomainEventDispatcher` — the MediatR vocabulary and the domain-event-to-MediatR bridge | `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Domain`, `MediatR` |
| `SharedKernel.Application.Behaviors` | `ValidationBehavior<,>`, `LoggingBehavior<,>`, `MetricsBehavior<,>`, `TransactionBehavior<,>` + `IUnitOfWork`, `CachingBehavior<,>` + `ICacheableQuery<TResponse>`, `AuthorizationBehavior<,>` + `IAuthorizationContext`/`IAuthorizeRequest`, `IdempotentCommandBehavior<,>` + idempotency seam/`IIdempotentRequest` — opt-in MediatR pipeline behaviors (seven total) | `SharedKernel.Application`, `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Caching.Abstractions`, `MediatR`, `FluentValidation` |

Both packages target `net10.0`, `ImplicitUsings` enabled, `Nullable` enabled. Test sub-folders live inside each project folder (never in a top-level `tests/`). Both production `.csproj` files set `GenerateDocumentationFile=true` and `TreatWarningsAsErrors=true` (matching `06.Persistence`/`07.Messaging` convention) so a missing XML doc comment on any public member (CS1591) or an unresolved `<see cref="..."/>` (CS1574) fails the build — doc coverage is enforced mechanically, not just audited once at Docs phase.

**Microservices always reference `SharedKernel.Application`. `SharedKernel.Application.Behaviors` is opt-in** — reference it only when adopting one or more platform behaviors; a service that hand-rolls its own cross-cutting concerns can depend on `SharedKernel.Application` alone.

---

## Technology Stack

| Concern | Technology | Version |
| --- | --- | --- |
| Mediator | `MediatR` | 12.4.x (last MIT-licensed major — see version-pin callout above) |
| Validation | `FluentValidation` | 11.x |
| Metrics | `System.Diagnostics.Metrics` (BCL) | `net10.0` |
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
    NOTE: Same platform-standard static-instrument pattern already approved for 07.Messaging's
          MessagingDiagnostics.ActivitySource — a static readonly Meter/Histogram carries no mutable
          business state and is the BCL-sanctioned process-lifetime diagnostics shape (same class as a
          static ILogger category name). This is the only sanctioned static state in this domain.
          13.ServiceDefaults (future work, out of scope here) registers the "SharedKernel.Application"
          meter name with the host's MeterProvider — this domain never reaches into 13.ServiceDefaults.

MetricsBehavior<TRequest, TResponse>  (sealed class, implements IPipelineBehavior<TRequest, TResponse>)
    where TRequest : IRequest<TResponse>
    NOTE: Records ApplicationDiagnostics.RequestDuration exactly once per request, tagged with
          request.name = typeof(TRequest).Name, via a try/finally around next() so the measurement is
          recorded whether the inner pipeline succeeds, returns a Result.Failure, or throws.
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
    .AddTransactionBehavior()
        — Build() throws InvalidOperationException if IUnitOfWork is not registered in IServiceCollection
          when this was called — mirrors 07.Messaging's WithIdempotency() missing-store guard.
    .AddCachingBehavior()
        — Build() throws InvalidOperationException if SharedKernel.Caching.Abstractions.ICacheService is
          not registered when this was called.
    .AddAuthorizationBehavior()
        — Build() throws InvalidOperationException if IAuthorizationContext is not registered in
          IServiceCollection when this was called — same missing-dependency guard shape as Transaction/Caching.
    .AddIdempotencyBehavior()
        — Build() throws InvalidOperationException if IIdempotencyKeyStore is not registered in
          IServiceCollection when this was called.
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

```text
1. LoggingBehavior        ← outermost; measures/logs the full pipeline, including validation/auth failures
2. MetricsBehavior        ← records sharedkernel.application.request.duration regardless of outcome
3. ValidationBehavior     ← throws ValidationException before any handler, auth check, or cache work happens
4. AuthorizationBehavior  ← TRequest : IAuthorizeRequest (commands AND queries); rejects before cache/mutation
5. CachingBehavior        ← queries only (TRequest : ICacheableQuery<TResponse>)
6. IdempotentCommandBehavior  ← commands only (TRequest : ICommandBase, IIdempotentRequest); innermost-but-one
7. TransactionBehavior    ← commands only (TRequest : ICommandBase); innermost — wraps handler + commit
```

**Positional rationale, step by step:**

1. **Logging outermost** — every request, including ones that fail validation or authorization, must produce a start/end log line; logging must never be skipped by an inner short-circuit.
2. **Metrics second** — the duration histogram must capture the full pipeline cost (validation, auth, cache, commit), not just the handler; recorded via try/finally regardless of how the request terminates.
3. **Validation third** — malformed input is rejected before spending a permission check, a cache lookup, an idempotency-store round trip, or a transaction — the cheapest, most foundational rejection happens first.
4. **Authorization fourth** — runs after validation (don't spend a permission check on garbage input) and before everything that follows: never let an unauthorized request reach a cache lookup (step 5), an idempotency check (step 6), or a mutation (step 7).
5. **Caching fifth** — queries only; applies after authorization so an unauthorized query never populates or reads the cache. Mutually exclusive with steps 6–7 at the request-type level (`ICacheableQuery<TResponse>` and `ICommandBase` are never implemented by the same request).
6. **Idempotency sixth** — commands only; runs immediately before the commit boundary so a duplicate submission is detected and rejected before `TransactionBehavior`'s `SaveChangesAsync` can run a second time for the same logical operation. Positioned after authorization (don't burn an idempotency-store round trip on an unauthorized request) and immediately outside Transaction (the innermost pair must stay adjacent — duplicate-detection has to happen right before commit, not several steps earlier where another behavior could still short-circuit in between).
7. **Transaction innermost** — wraps the handler call and the commit; by the time a command reaches this step it has already passed validation, authorization, and (if applicable) the idempotency check, so `SaveChangesAsync` only ever runs for a genuinely new, authorized, validated command.

Steps 5, 6, and 7 are mutually exclusive at the request-type level in pairs (`ICacheableQuery<TResponse>` vs. `ICommandBase`), so a single request only ever actually passes through one of {5} or {6, 7} — the full seven-step order is documented for completeness across the whole request universe, not because any one request traverses all seven.

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
- Reordering the seven-step canonical pipeline (Logging → Metrics → Validation → Authorization → Caching → Idempotency → Transaction) without updating the documented positional rationale for every affected step.

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
services
    .AddSharedKernelApplicationBehaviors()
    .AddLoggingBehavior()
    .AddMetricsBehavior()
    .AddValidationBehavior()
    .AddAuthorizationBehavior()    // requires IAuthorizationContext registered
    .AddCachingBehavior()          // requires SharedKernel.Caching.Abstractions.ICacheService registered
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

- `ICommandBase`, `ICommand`, `ICommand<TResponse>`, `IQuery<TResponse>`, `ICommandHandler<>`, `ICommandHandler<,>`, `IQueryHandler<,>`, `IDomainEventHandler<TDomainEvent>`, `IUnitOfWork`, `ICacheableQuery<TResponse>`, `IAuthorizationContext`, `IAuthorizeRequest`, `IIdempotencyKeyStore`, `IIdempotentRequest` are interfaces — AOT-safe by definition.
- `DomainEventNotification<TDomainEvent>` is a closed generic sealed record at every call site — AOT-safe.
- `MediatRDomainEventDispatcher`'s per-event-type dispatch is the one documented `MakeGenericMethod` exception in this domain — cached once per concrete `Type` in a static `ConcurrentDictionary<Type, Delegate>`, not a per-call reflection cost. Identical, already-approved shape to `07.Messaging`'s `MassTransitEventPublisher` bridge.
- `AddDomainEventHandler<TDomainEvent, THandler>()` is a closed generic at the call site — the consuming service supplies both type arguments at compile time; zero reflection.
- `ValidationBehavior<,>`, `LoggingBehavior<,>`, `MetricsBehavior<,>`, `TransactionBehavior<,>`, `CachingBehavior<,>`, `AuthorizationBehavior<,>`, `IdempotentCommandBehavior<,>` are closed generic sealed classes; MediatR resolves the open generic `IPipelineBehavior<,>` registration to a closed generic per request type via ordinary DI generics at runtime, not `MakeGenericType` per call.
- `ApplicationDiagnostics.Meter` / `Histogram<double>` are BCL `System.Diagnostics.Metrics` — fully AOT-safe, no reflection.
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
