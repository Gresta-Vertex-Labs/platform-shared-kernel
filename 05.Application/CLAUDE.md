# 05.Application — Kernel Mediator Contracts, Request Pipeline & MediatR Adapter

> **Audience:** maintainers and AI agents changing code in this folder.
> **Consumers** read each package's own `README.md`; the folder overview is [`README.md`](README.md).
> This brain holds only what the source does not make obvious: rules, traps, couplings and decisions.
> Historical work-order narrative (WO-035 through WO-080) lives in [`CLAUDE.history.md`](CLAUDE.history.md);
> the phase history, including WO-086, is in [`state-map.md`](state-map.md).

## What This Domain Is

The CQRS layer: a kernel-owned request/handler/pipeline vocabulary with **no MediatR in it**, a request
pipeline of opt-in cross-cutting behaviors composed in a fixed order, a native domain-event dispatcher, and
one adapter that uses MediatR as the transport behind `ISender`.

**Philosophy:** the kernel owns the abstraction (`IRequest<T>`, `IRequestHandler<,>`, `ISender`,
`IPipelineBehavior<,>`); MediatR is a replaceable transport behind it. Handlers return `Result`/`Result<T>`
exclusively; there is no response envelope. Every infrastructure-facing behavior depends on a contract from a
Foundation or Abstractions package (`SharedKernel.Execution`, `SharedKernel.Idempotency.Abstractions`,
`SharedKernel.Caching.Abstractions`), never on the infrastructure that implements it.

**Hard rules**

1. `SharedKernel.Application` is **Abstractions tier** and references only `SharedKernel.Primitives`,
   `SharedKernel.Domain` and `SharedKernel.Caching.Abstractions` (for `CachePolicy` on `ICacheableQuery`). No
   MediatR, no FluentValidation, no `Microsoft.Extensions.*` implementation package. Application-layer projects
   of a service reference only this package.
2. **MediatR is referenced only by `SharedKernel.Application.Mediator.MediatR`** (locked by
   `00.Governance`'s `DependencyGraphRulesTests.MediatR_IsReferencedOnlyByTheMediatorAdapter`). It is pinned to
   `12.4.x`, the last MIT-licensed major; never bump it.
3. `SharedKernel.Application.Pipeline` (Host tier) adds only `SharedKernel.Application`,
   `SharedKernel.Execution`, `SharedKernel.Primitives`, `SharedKernel.Core` (since P-579, for `error.ToException()` when a
   streaming query is denied), `SharedKernel.Idempotency.Abstractions` and first-party
   `Microsoft.Extensions.*` packages. It must **never** reference a cache, Polly, hosting, FluentValidation or a
   mediator (`ApplicationPipelineRules.PipelineNeverReferencesCachingPollyOrHosting`,
   `BehaviorsNeverReferenceConcreteInfrastructure`). The `SharedKernel.Caching.Abstractions` reference for the
   behaviors belongs exclusively to `SharedKernel.Application.Pipeline.Caching`.
4. No response envelope, ever. `14.Presentation`'s `ResultHttpExtensions` maps a failure to RFC 9457
   ProblemDetails; `11.Communication`'s REST client maps it back with `ReadResultAsync<T>`.
5. The caller, transaction and audit contracts (`IRequestContext`, `IUnitOfWork`, `IAuditTrailWriter`) live in
   `01.Core/SharedKernel.Execution`, and the idempotency store in `18.Idempotency/SharedKernel.Idempotency.Abstractions`.
   This domain consumes them and never redeclares them (`UnitOfWorkSeamRules.SharedContractsAreNotRedeclared`).
6. AOT and trimming are **not** constraints here (user ruling, 2026-09-15). Reflection is used where it is the
   clearest code — see "Reflection use" — and cached once per closed type.
7. In `AddSharedKernelApplication` the first-registered behavior of a stage is outermost, so its code
   **after** `next()` runs last. `CommandScopeBehavior` is registered first in the command stage so its
   post-commit callbacks observe `TransactionBehavior`'s commit (and `AuditingBehavior`'s and
   `IdempotencyBehavior`'s post-`next()` code) as already complete. `RequestPipeline<,>` wraps behaviors in
   resolution order: first resolved = outermost.

---

## Packages

| Package | Tier | Role | References |
| --- | --- | --- | --- |
| `SharedKernel.Application` | Abstractions | The kernel mediator contracts (`IRequest<T>`, `IRequestHandler<,>`, `ISender`, `IPipelineBehavior<,>`, `RequestHandlerContinuation<T>`, stream counterparts), CQRS vocabulary (`ICommand`/`ICommand<T>`/`IQuery<T>`/`IStreamQuery<T>` + handler aliases), `IRequestValidator<T>`, `IDomainEventHandler<T>`, and `[RequirePermission]` (`Authorization`, always enforced) and every request marker (`IIdempotentRequest`, `IAuditableRequest<T>`, `ILoggableRequest<T>`, `ICacheableQuery<T>`, `IInvalidatesCache`, `CacheScope`, `CacheKeyRef`, `ICommandScope`) | `SharedKernel.Primitives`, `SharedKernel.Domain`, `SharedKernel.Caching.Abstractions` |
| `SharedKernel.Application.Pipeline` | Host | The one registration call `AddSharedKernelApplication(assemblies, app => app.With…())` (handler/validator/domain-event-handler scan, host-start seam check) with `ApplicationPipelineBuilder`/`PipelineStage`; `RequestPipeline<,>`/`StreamRequestPipeline<,>`; the internal behaviors (Tracing, Logging, Metrics, Authorization + stream authorization, Validation, Idempotency, Transaction, Auditing in two halves) and the native `DomainEventDispatcher` | `SharedKernel.Application`, `SharedKernel.Execution`, `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Idempotency.Abstractions`, `Microsoft.Extensions.{DependencyInjection.Abstractions, Logging(.Abstractions), Options(.DataAnnotations), Diagnostics}` |
| `SharedKernel.Application.Pipeline.Caching` | Host | `CachingBehavior<,>` (query stage) and `CacheInvalidationBehavior<,>` (command stage), added by `app.WithCaching()` | `SharedKernel.Application.Pipeline`, `SharedKernel.Caching.Abstractions` |
| `SharedKernel.Application.Mediator.MediatR` | Host | `app.UseMediatR()`: MediatR as the transport behind `ISender` (one route per scanned request type) | `SharedKernel.Application`, `SharedKernel.Application.Pipeline`, `MediatR` 12.4.1 |

`SharedKernel.Application.ConsumerVerify` (not packable) references all four packed packages by
`PackageReference`, plus `SharedKernel.Idempotency.Abstractions` and `SharedKernel.Validation.FluentValidation`,
and runs a command end to end (commit + post-commit callback, business failure, validation, forbidden, idempotent
replay/reuse) and asserts the Pipeline package carries no mediator or infrastructure dependency.

Every test project is nested inside the package it tests. Test doubles come from `16.Testing`'s Testing-tier
packages (`SharedKernel.Testing`, `SharedKernel.Application.Testing`, `SharedKernel.Idempotency.Testing`,
`SharedKernel.Caching.Testing`) or live inside the test project.

---

## Technology Stack

| Concern | Choice |
| --- | --- |
| Mediator transport | `MediatR` pinned to `12.4.x`, only in `.Mediator.MediatR` |
| Validation | The kernel `IRequestValidator<TRequest>` port; validators run **sequentially** (a validator may hold a scoped `DbContext`). FluentValidation joins through `01.Core/SharedKernel.Validation.FluentValidation`'s `AddFluentValidationRequestValidators()` |
| Tracing | `System.Diagnostics.ActivitySource` named `SharedKernel.Application` — a process-lifetime static instrument, the one sanctioned static-state exception |
| Metrics | `System.Diagnostics.Metrics.Meter`, same name, a DI singleton from `IMeterFactory` — `Histogram<double>` `sharedkernel.application.request.duration`, unit `s` |
| Serialization | Reflection-based `System.Text.Json`; `Idempotency/IdempotencyResponseSerializer` adds hand-written converters for `Result`/`Result<T>` |
| Logging | `[LoggerMessage]`, EventIds `5100`–`5199` (`.Pipeline`) and `5200`–`5299` (`.Pipeline.Caching`) inside `LoggingEventIdRanges.Application` |

---

## DI Registration

One call (P-563 A4, application model merged from main in P-579), in `SharedKernel.Application.Pipeline`:

```csharp
services.AddSharedKernelApplication(typeof(PlaceOrderHandler).Assembly, app => app
    .UseMediatR()                                   // SharedKernel.Application.Mediator.MediatR — the ISender
    .WithIdempotency(o => o.RetentionWindow = TimeSpan.FromHours(24)) // needs IIdempotencyStore (Request) + IRequestContext
    .WithTransactions()                             // needs IUnitOfWork
    .WithAuditing()                                 // needs IAuditTrailWriter; registers both auditing halves
    .WithCaching()                                  // SharedKernel.Application.Pipeline.Caching; needs ICacheService + ITenantCacheKeyProvider
    .WithBehavior(typeof(MyBehavior<,>), PipelineStage.Command, typeof(IMyDependency)));

services.AddFluentValidationRequestValidators(typeof(PlaceOrderHandler).Assembly); // IValidator<T> -> IRequestValidator<T>

// The contracts the behaviors consume, implemented by infrastructure directly — before or after the call above:
services.AddSharedKernelRequestContext();                                  // IRequestContext (ServiceDefaults.Security)
builder.AddSharedKernelPostgres<OrderDbContext>("orders", p => p.UseAuditTrail()); // IUnitOfWork, IAuditTrailWriter
services.AddRedisIdempotency(p => p.ForRequests());                        // IIdempotencyStore for IdempotencyPurpose.Request
services.AddSharedKernelCaching(o => o.ServiceName = "orders");           // ICacheService, ITenantCacheKeyProvider
```

- **Always registered:** every `IRequestHandler<,>`/`IStreamQueryHandler<,>` (transient), `IRequestValidator<>` and
  `IDomainEventHandler<>` (scoped) of the assemblies, public or internal; `DomainEventDispatcher` as
  `IDomainEventDispatcher`; `ICommandScope`; `RequestPipeline<,>`/`StreamRequestPipeline<,>`; and the Tracing,
  Logging, Metrics, **Authorization** (plus its stream twin) and Validation behaviors. `AddDomainEventHandler<TEvent,
  THandler>()` registers a handler from another assembly.
- **Opt-ins** (`With…`) land in the canonical order whatever order they are called in; `WithBehavior(type, stage,
  requiredServices)` is how `WithCaching()` wires its two behaviors without `.Pipeline` referencing a cache.
- **Seams are checked when the host starts** (`ValidateOnStart` on an internal options type), never at registration:
  one `OptionsValidationException` names every missing service and what needs it. `ISender` is always required, so a
  host that forgot `UseMediatR()` fails at start. A request type of the scanned assemblies carrying
  `[RequirePermission]` makes `IRequestContext` required. A plain `ServiceProvider` without a host runs no check
  (tests call `IStartupValidator.Validate()` — `ApplicationPipelineTestHarness` does).
- **Calling it twice throws** — pass every assembly and every option to the one call.
- The full persistence composition, compiled and run by a test, is `06.Persistence/README.md`.

---

## Reflection use

Not a design constraint (user ruling, 2026-09-15). Every instance is cached once per closed type:

| Site | What | Why |
| --- | --- | --- |
| `.Pipeline/Shared/FailureResponse.cs` | `TResponse.GetMethod("Failure", ...)` + `CreateDelegate`, cached per closed `TResponse` | A short-circuiting behavior builds a failed `Result`/`Result<T>` from the open `TResponse` |
| `.Pipeline/Shared/ResponseOutcome.cs` | `TResponse.GetProperty("Error")` + `CreateDelegate`, cached per closed `TResponse` | Reads the `Error` off any failed response |
| `.Pipeline/DomainEvents/DomainEventDispatcher.cs` | `MakeGenericType` of a private closed invoker per runtime event type, cached in a static `ConcurrentDictionary` | The concrete event type is only known per element at runtime |
| `.Pipeline/Idempotency/IdempotencyResponseSerializer.cs` | Hand-written `JsonConverter<Result>`/`JsonConverter<Result<T>>` + factory | `Result` types have private constructors and get-only properties |
| `.Pipeline/ApplicationServiceCollectionExtensions.cs` | Assembly scan at **registration** only (handlers, validators, domain-event handlers, `[RequirePermission]` request types) | Registration discovers the service's types; nothing is scanned per send |
| `.Pipeline/Authorization/AuthorizationBehavior.cs` | `GetCustomAttributes<RequirePermissionAttribute>` once per closed request type (`PermissionRequirements<TRequest>`) | The attribute is the declaration; reading it per call would be waste |
| `.Mediator.MediatR/MediatRServiceCollectionExtensions.cs` | Assembly scan + `MakeGenericType` at **registration** only (`UseMediatR()`); per request type a keyed `RequestDispatcher<TRequest, TResponse>` singleton | A send then resolves the dispatcher by request type and makes no reflective call |

---

## Interface Contracts

### `SharedKernel.Application`

| Namespace | Types |
| --- | --- |
| `Messaging` | `IRequest<TResponse>` (marker); `IRequestHandler<TRequest,TResponse>.Handle(request, ct)`; `ISender` (`Send<TResponse>(IRequest<TResponse>, ct)`, `CreateStream<TResponse>(IStreamQuery<TResponse>, ct)`); `IPipelineBehavior<TRequest,TResponse>.Handle(request, RequestHandlerContinuation<TResponse> next, ct)` — `next` takes no arguments (MediatR-shaped, so `await next()` call sites are unchanged); `ICommandBase`/`IQueryBase` zero-member markers; `ICommand : ICommandBase, IRequest<Result>`; `ICommand<T> : ICommandBase, IRequest<Result<T>>`; `IQuery<T> : IQueryBase, IRequest<Result<T>>`; `ICommandHandler<>`, `ICommandHandler<,>`, `IQueryHandler<,>` — pure `IRequestHandler<,>` aliases |
| `Streaming` | `IStreamQuery<T>`, `IStreamQueryHandler<TQuery,T>` (returns `IAsyncEnumerable<T>`, items not wrapped in `Result`), `IStreamPipelineBehavior<,>` + `StreamHandlerContinuation<T>`. `.Pipeline` registers one stream behavior, `StreamAuthorizationBehavior` (`[RequirePermission]` on a streaming query); `StreamRequestPipeline<,>` runs it and any a service registers |
| `Validation` | `IRequestValidator<TRequest>.ValidateAsync(request, ct)` → `ValueTask<IReadOnlyList<Error>>` (empty = valid; field path in `Error.MessageArguments[ErrorArgumentNames.PropertyPath]`, never the rejected value) |
| `DomainEvents` | `IDomainEventHandler<in TDomainEvent>.Handle(event, ct)` against the raw `03.Domain` event |
| `Authorization` | `RequirePermissionAttribute` — `[RequirePermission("a", "b")]` on a command, query or stream query: the values of one attribute are alternatives, several attributes all apply (`AllowMultiple`, `Inherited`); a blank or empty list throws when the attribute is constructed |
| `Idempotency` | `IIdempotentRequest` (`IdempotencyKey` — reserved per tenant and caller, optional `Fingerprint`) |
| `Auditing` | `IAuditableRequest<TResponse>` (`Action`, `ResourceType`, `ResourceId`, `BeforeSnapshot`, `GetAfterSnapshot`) |
| `Logging` | `ILoggableRequest<TResponse>` (`LoggableRequestFields`, `GetLoggableResponseFields`) |
| `Caching` | `ICacheableQuery` (non-generic base: `CacheKey`, `CachePolicy`, `Scope`, `RefreshCache`; an internal member makes it unimplementable outside the assembly) and `ICacheableQuery<TValue>` (also an `IQuery<TValue>`; `ShouldCache`); `IInvalidatesCache` (`CacheKeysToInvalidate` as `CacheKeyRef`, `CacheTagsToInvalidate`, `Scope`); `CacheKeyRef.For<TQuery>(key)`; `CacheScope` (`Tenant` = 0, `User`, `Global`) |
| `Commands` | `ICommandScope` (`IsActive`, `IsNested`, `OnCompleted(callback)`) |

### `SharedKernel.Application.Pipeline`

Public (namespace `SharedKernel.Application.Pipeline`): `ApplicationServiceCollectionExtensions` —
`AddSharedKernelApplication(params Assembly[])`, `(Assembly, Action<ApplicationPipelineBuilder>)`,
`(Assembly[], Action<ApplicationPipelineBuilder>)` and `AddDomainEventHandler<TDomainEvent,THandler>()`;
`ApplicationPipelineBuilder` (`Services`, `Assemblies`, `WithIdempotency(Action<IdempotencyBehaviorOptions>?)`,
`WithTransactions()`, `WithAuditing()`, `WithBehavior(type, stage, params requiredServices)`); `PipelineStage`
(`Observability`, `Authorization`, `Validation`, `Query`, `Command`); `ApplicationLoggingOptions`
(`SlowRequestThreshold`, 500 ms); `IdempotencyBehaviorOptions` (`LeaseDuration` 30 s, `RetentionWindow` 24 h, lease <
retention); `RequestPipeline<TRequest,TResponse>.HandleAsync(request, ct)` (resolves the handler and every
`IPipelineBehavior<TRequest,TResponse>`, first = outermost) and `StreamRequestPipeline<,>.Handle` — public because a
mediator adapter (and a test) resolves them.

Internal (P-563 A5; `InternalsVisibleTo` the test project only): every behavior — `TracingBehavior<,>`,
`LoggingBehavior<,>`, `MetricsBehavior<,>`, `AuthorizationBehavior<,>`, `StreamAuthorizationBehavior<,>`,
`ValidationBehavior<,>` (over `IEnumerable<IRequestValidator<TRequest>>`), `CommandScopeBehavior<,>`,
`IdempotencyBehavior<,>`, `AuditingBehavior<,>`, `TransactionBehavior<,>`, `AuditingCommitBehavior<,>` — and
`DomainEventDispatcher` (serial, events in list order, handlers in registration order, exact runtime type only, an
exception propagates and stops dispatch, no handler = skipped), `IdempotencyKeyScope`, `PipelineRequirements`.

`AddSharedKernelApplication` throws `ArgumentException` for no (or a `null`) assembly and `InvalidOperationException`
for a second call or for two different handlers of one request type (a hand-registered handler — factory, instance or
the same type — is kept). `WithBehavior` throws `ArgumentException` for a type that is not an open generic
`IPipelineBehavior<,>` and ignores a duplicate.

### `SharedKernel.Application.Pipeline.Caching`

`app.WithCaching()` (`CachingPipelineExtensions`, namespace `SharedKernel.Application.Pipeline.Caching`) — registers
the `CachingBehaviorsMetrics` singleton and adds `CachingBehavior<,>` (query stage) and `CacheInvalidationBehavior<,>`
(command stage), both requiring `ICacheService` and `ITenantCacheKeyProvider` (checked at host start). Both behaviors
are internal; `IRequestContext` is optional (without one, only `Global` scope can be served).

### `SharedKernel.Application.Mediator.MediatR`

`app.UseMediatR()` (extension on `ApplicationPipelineBuilder`) — adds MediatR once (scanning only this adapter's
assembly) and `ISender` → internal `MediatRSender` (transient, so a nested send stays in the caller's scope). For every
non-abstract, non-generic class of `app.Assemblies` implementing `IRequestHandler<,>` or `IStreamQueryHandler<,>` it
registers a MediatR handler for an internal `RequestEnvelope<TRequest,TResponse>` that runs `RequestPipeline<,>`, and a
keyed `RequestDispatcher` singleton. The handlers themselves are registered by `AddSharedKernelApplication`. Sending a
request with no discovered handler throws `InvalidOperationException` naming the type. MediatR's own pipeline stays
empty; a class implementing MediatR's `IRequestHandler`/`INotificationHandler` is not discovered.

---

## Keys are namespaced by query type and scope

The cache key is built through `ITenantCacheKeyProvider` (`02.Caching`), never string interpolation:

```text
Global   {service}:{QueryType}:{CacheKey}
Tenant   {service}:@{tenant}:{QueryType}:{CacheKey}
User     {service}:@{tenant}:{QueryType}:{CacheKey}:u:{userId}     (no tenant: {service}:{QueryType}:{CacheKey}:u:{userId})
```

`{tenant}` is `IRequestContext.TenantId` (`TenantId`, lowercase GUID). `ICacheableQuery.CacheKey` is the
query's identity **within its own namespace**. The `{QueryType}` segment exists because an entry holds the
bare `TValue` JSON with no type discriminator. `00.Governance`'s SK0041 reports two cacheable queries sharing a
simple type name. A command names the owning query through `CacheKeyRef.For<TQuery>(key)`, so a renamed query
is a compile error at the command. Tags are scoped with `CacheKeyFormat.BuildTenantTag` and policies with
`CachePolicy.ForTenant`, so `ITenantCacheService.RemoveTenantAsync` also removes a tenant's cached queries.

**Scopes fail closed.** `CacheScope.Tenant` is the zero value. A `Tenant` query with no tenant, or a `User`
query with no authenticated caller, skips the cache, runs the handler and logs a Warning (EventId 5201) — never
a wider key. A command's `Scope` must match the queries it invalidates.

---

## Pipeline: stages and canonical order

Fixed regardless of `.Add…()` call order. Outermost first:

```text
Observability stage   1. TracingBehavior
                       2. LoggingBehavior
                       3. MetricsBehavior
                       [custom Observability-stage behaviors]
Authorization stage   4. AuthorizationBehavior
                       [custom Authorization-stage behaviors]
Validation stage      5. ValidationBehavior
                       [custom Validation-stage behaviors]
Query stage            [custom Query-stage behaviors — e.g. CachingBehavior]
Command stage          CommandScopeBehavior (first, whenever the command stage is active)
                        IdempotencyBehavior
                        AuditingBehavior          (outer half: records Failed after rollback)
                        TransactionBehavior       (runs the rest inside IUnitOfWork.ExecuteInTransactionAsync)
                        AuditingCommitBehavior    (inner half: queues Succeeded on OnBeforeCommit)
                        [custom Command-stage behaviors — e.g. CacheInvalidationBehavior]
                       handler
```

Authorization runs **before** Validation deliberately. `CommandScopeBehavior` is registered whenever any
command-stage behavior is active; `ICommandScope` is always registered.

### Each behavior's contract

| Behavior | Success | `Result` failure | Thrown exception |
| --- | --- | --- | --- |
| `TracingBehavior` | tags `request.type`, `request.kind` | span `Error`, description = error code, tags `error.type`/`error.code` | span `Error`, `AddException`, tag `error.type` = exception full name, rethrows |
| `LoggingBehavior` | `Information` (or `Warning` over `SlowRequestThreshold`) | `Warning` with error type/code | `Error` with the exception, rethrows |
| `MetricsBehavior` | `outcome="success"` | `outcome="failure"`, `error.type` = `Error.Type` | `outcome="exception"`, `error.type` = exception full name, rethrows |
| `AuthorizationBehavior` | calls `next()` (a request without `[RequirePermission]` is not checked and `IRequestContext` is not resolved) | before `next()`: unauthenticated → `Error.Unauthorized(ErrorCodes.Unauthorized.Default)`; an attribute none of whose values the caller holds → `Error.Forbidden(ErrorCodes.Forbidden.InsufficientPermission)` | throws `InvalidOperationException` only when the request declares a permission and no `IRequestContext` is registered (the start check normally reports it first). `StreamAuthorizationBehavior` throws the denial's `error.ToException()` when enumeration starts |
| `ValidationBehavior` | calls `next()` when every validator returns no error | before `next()`: `Error.Validation(errors)` aggregating every validator's errors in `Error.Details` | never throws itself |
| `CommandScopeBehavior` | runs queued `OnCompleted` callbacks (outermost frame only) after `next()` | discards the frame's callbacks | discards the frame's callbacks |
| `IdempotencyBehavior` | key = `IdempotencyKeyScope.Create(requestContext, rawKey)`; `CompleteAsync(Request, key, token, response, RetentionWindow)`; `false` logs a Warning (5120) and still returns | `ReleaseAsync` | `ReleaseAsync`, rethrows |
| `TransactionBehavior` | the rest runs inside `ExecuteInTransactionAsync` (outermost command; may replay — handlers must be re-runnable; callbacks queued by a discarded attempt are dropped); saves, runs `OnBeforeCommit`, commits | rollback; a joined failure marks the outer transaction rollback-only | rollback, propagates |
| `AuditingBehavior` + `AuditingCommitBehavior` | inner half queues `Succeeded` on `OnBeforeCommit` (written directly when no transaction is active) | outer half, after rollback: `Failed` with `ErrorCode`; a `RecordAsync` failure propagates | outer half: `Failed` with the exception type's full name (including a failed commit); a `RecordAsync` failure is logged (5130) and the **original** exception rethrows |
| `CachingBehavior` | handler runs inside `GetOrSetAsync` (once per key across concurrent callers); the `TValue` is cached | returned to that caller only, never cached (`SkipCaching`) | propagates, nothing cached; fail-safe may serve an expired entry |
| `CacheInvalidationBehavior` | registers an `OnCompleted` callback that evicts after commit; keys/tags are built and validated **before** the handler | nothing registered | nothing registered |

A nested command (`ICommandScope.IsNested`) makes `IdempotencyBehavior` call `next()` directly.
`TransactionBehavior` joins an active transaction (a nested failure marks it rollback-only) and calls `next()`
directly only when nested with no active transaction — only the **outermost** command owns the idempotency key
and commits. A nested audited command queues its `Succeeded` entry on the outer transaction.

---

## The nested-command guard and `ICommandScope`

`CommandScopeBehavior<,>` is the sole owner of frames (a stack of callback lists): every dispatch pushes one,
nested ones included. A nested command's callbacks are **merged into the parent frame** on success and run once,
after the outermost command succeeds; a failed or faulted command discards its frame. `OnCompleted` throws
`InvalidOperationException` when no command is active. The kernel `ISender` resolves in the caller's DI scope
(`MediatRSender` is transient), so one `ICommandScope` observes the whole nesting depth. A callback that throws
after the commit is logged at `Error` (5110) and does **not** change the response.

---

## The idempotency contract

`IdempotencyBehavior` uses the `IIdempotencyStore` (`SharedKernel.Idempotency.Abstractions`) registered for
`IdempotencyPurpose.Request` (`[FromKeyedServices(IdempotencyPurpose.Request)]`) and the caller's `IRequestContext`
(`WithIdempotency()` requires both at host start). The behavior passes lease and retention on every call, so the store
needs no retention settings; the store additionally partitions by the ambient tenant.

- **Keys are reserved per tenant and caller** (P-562 X3, merged from main in P-579). The store never sees the raw key:
  `IdempotencyKeyScope.Create` hands it a SHA-256 digest (64 lowercase hex chars) of a frozen layout label, the
  tenant, the actor kind, the subject (`UserId`), the client and the impersonator, and the raw key — each field
  length-prefixed, absent ≠ empty. The session id is left out so a retry after a fresh sign-in replays. **Anonymous
  callers of a tenant share one scope** (only the fingerprint separates them): keys must be unguessable. The layout is a
  stored format — change it only with a new label.
- `TryBeginAsync(Request, scopedKey, fingerprint, LeaseDuration)`: `Started` (with a token) runs the handler;
  `InProgress` → `Error.Conflict(ErrorCodes.Idempotency.InProgress)`; `Completed` → the stored response is
  deserialized and replayed; `FingerprintMismatch` → `Error.Conflict(ErrorCodes.Idempotency.KeyReused)`. An empty key
  → `Error.Validation(ErrorCodes.Idempotency.KeyRequired)` (`idempotency.key_required`, was `key_missing`). The codes
  live in `01.Core`'s `ErrorCodes.Idempotency` (P-563 A6), shared with `14.Presentation`'s `Idempotency-Key` header
  checks, so no drift test exists.
- The fingerprint is `IIdempotentRequest.Fingerprint` when set (trimmed), otherwise a lowercase-hex SHA-256 of the
  request's JSON; a serialization failure becomes an `InvalidOperationException` naming the command type.
- `CompleteAsync`/`ReleaseAsync` return `false` — never throw — when the token no longer owns the reservation.
- The key is never echoed in an error message.

## Authorization is declared on the use case

`[RequirePermission]` (P-563 A2, merged from main in P-579) replaces `IAuthorizeRequest`/`PermissionMatch`. The
behavior is **always registered** — there is no opt-in to forget — and runs on every path a request is sent from
(HTTP, messages, jobs, workflows; streams through `StreamAuthorizationBehavior`). A request without the attribute is
not checked (the old "empty permission list denies" rule went with `IAuthorizeRequest`). Values of one attribute are
alternatives, several attributes all apply. Denial never names the missing permission. An endpoint that sends a
command does not repeat the command's permissions; `14.Presentation`'s `[RequireEndpointPermission]` is for what has no
command (hubs, gRPC methods, endpoints that send nothing). `SK0040` requires a request carrying `[RequirePermission]`
or `IIdempotentRequest` to return `Result`/`Result<T>`.

---

## Contracts consumed

| Contract | Package | Implemented by |
| --- | --- | --- |
| `IRequestContext` (`SharedKernel.Execution.Context`) | `SharedKernel.Execution` | `SharedKernel.ServiceDefaults.Security`'s `AddSharedKernelRequestContext()`; `SystemRequestContext`/`AnonymousRequestContext`/`PropagatedRequestContext` for non-HTTP callers |
| `IUnitOfWork` (`SharedKernel.Execution.Transactions`) | `SharedKernel.Execution` | `06.Persistence.EfCore` (`EfUnitOfWork`) |
| `IAuditTrailWriter` (`SharedKernel.Execution.Auditing`) | `SharedKernel.Execution` | `06.Persistence.EfCore.Auditing` |
| `IIdempotencyStore` (purpose `Request`) | `SharedKernel.Idempotency.Abstractions` | `18.Idempotency` (`AddRedisIdempotency`, `AddEfCoreIdempotency`) |
| `ICacheService`, `ITenantCacheKeyProvider` | `SharedKernel.Caching.Abstractions` | `02.Caching.FusionCache` (`AddSharedKernelCaching`) |
| `IDomainEventDispatcher` (implemented here) | `SharedKernel.Domain` | `DomainEventDispatcher`; called by `06.Persistence`'s `SharedKernelDbContext.SaveChangesAsync` before the physical save |

---

## `CachingBehavior` caches the value, not the `Result`

`CachingBehavior<,>` stores the `TValue` of a successful `Result<TValue>` and rebuilds `Result<TValue>.Success(value)`
on a hit. It is constrained to the non-generic `ICacheableQuery`, whose internal `GetOrSetAsync<TResponse>` is
implemented by the generic interface where `TValue` is known — no reflection. The handler runs inside the context
overload of `ICacheService.GetOrSetAsync<TValue>`; a failed `Result` is captured in the factory closure and
skipped with `SkipCaching()`. **Load-bearing:** a skipped value is not handed to concurrent waiters, so each waiter
runs the handler itself and keeps its own failure — pinned by `CachingBehaviorConcurrencyTests` against a real
FusionCache (factory once on success, twice on the skip path), and mirrored by the per-key gate in
`SharedKernel.Caching.Testing`'s `FakeCacheService`. Caching the value keeps `Result` free of serialization
concerns; `TValue` must round-trip through `System.Text.Json` (and be in the cache `SerializerContext` when one is set).

---

## Decision Records

| Decision | Chosen | Rejected | Accepted cost |
| --- | --- | --- | --- |
| Mediator abstraction (WO-086, P-567) | Kernel-owned `IRequest`/`IRequestHandler`/`ISender`/`IPipelineBehavior` in the MediatR-free `SharedKernel.Application`; MediatR only as a transport in `.Mediator.MediatR`, whose MediatR pipeline stays empty | Inheriting MediatR's interfaces (the pre-WO-086 design) | Every application project is MediatR-free and replacing MediatR is one new `ISender`; one envelope type per request inside the adapter |
| Continuation shape | Argument-less `RequestHandlerContinuation<T>` (`await next()`) | A continuation taking the request/token | Behavior bodies are unchanged from MediatR's shape |
| Domain events | Native `DomainEventDispatcher` resolving `IDomainEventHandler<T>` | Publishing through MediatR notifications | No wrapper type; handlers match the exact runtime type only |
| Validation port | `IRequestValidator<T>` returning `Error`s; FluentValidation bridged in `01.Core` | FluentValidation inside the pipeline | The pipeline carries no validation library; SK0015 was deleted |
| Idempotency store | Purpose-keyed `IIdempotencyStore` from `SharedKernel.Idempotency.Abstractions` shared with messaging (P-568) | A request-only store owned here | Persisted formats changed; the behavior owns lease and retention |
| `CachingBehavior` cache-miss path | Context overload of `GetOrSetAsync`; failures `SkipCaching()`; policy forced to `WithoutEagerRefresh()` and `WithFactoryTimeouts(null, null)` | `TryGetAsync`/`SetAsync` | No eager refresh or factory timeout for cached queries (both would run the handler after the scope is disposed) |
| Tenant cache scoping | Keys through `ITenantCacheKeyProvider.BuildTenantKey(TenantId, …)`; tags `BuildTenantTag`; `ForTenant` | `ITenantCacheService` (takes `(entity, id)` and an explicit tenant) | `.Pipeline.Caching` consumes the plain `ICacheService` with the same tenant format |
| Pipeline extensibility | Five-value `PipelineStage` + `WithBehavior(type, stage, requiredServices)` | An unordered registration list | A sibling package slots into a precise stage |
| Short-circuit response | `FailureResponse.Create<TResponse>` (cached delegate) | A second failure shape | One mechanism for Authorization, Validation, Idempotency |
| `ApplicationLoggingOptions`/`IdempotencyBehaviorOptions` validation | DataAnnotations + `ValidateOnStart()` | `SharedKernel.Configuration`'s `AddValidatedOptions` | No extra project reference; invalid values fail at startup |
| Registration (P-563 A4, merged from main in P-579) | One `AddSharedKernelApplication(assemblies, app => …)` in `.Pipeline`; the mediator plugs in as a builder extension (`UseMediatR()`), like the messaging transports (`UseRabbitMq()`); seams checked by `ValidateOnStart`; a second call throws | WO-086's `AddSharedKernelMediatR` + `AddSharedKernelApplicationBehaviors()…Build()` (two calls, ordering traps, `Build()` throwing for a seam registered later); main's `AddSharedKernelApplication` inside a MediatR-bound `SharedKernel.Application` (A1/A3, rejected by the owner for WO-086) | FluentValidation stays a second line (`AddFluentValidationRequestValidators(asm)`), because the pipeline takes no validator library; a host that forgets the mediator fails at start (`ISender` is always required) |
| Authorization declaration (P-563 A2, merged from main in P-579) | `[RequirePermission]` on the use case, always enforced, `IRequestContext` resolved only for marked requests | `IAuthorizeRequest`/`PermissionMatch` (an opt-in behavior could be forgotten; an endpoint and a command declared the same permission twice) | A marker-free request is unchecked by design — a request that needs a check must say so |
| Behavior visibility (P-563 A5, merged from main in P-579) | Behaviors and `DomainEventDispatcher` internal; the public surface is the registration call, its builder, the options and `RequestPipeline<,>` | Public behavior classes | Tests reach them through `InternalsVisibleTo`; `PipelineOrderAssertion` inspects registrations by type name |
| Idempotency per caller (P-562 X3, merged from main in P-579) | The behavior scopes the key to tenant + caller before the store sees it | Tenant-only keys (one caller could replay another's response) | Reservations stored before the upgrade are not found |

Removed before first publish (P-544) and not coming back: fire-and-forget dispatch, `ResilienceBehavior`,
parallel domain-event dispatch, `DualApprovalBehavior`, a response envelope. See `CLAUDE.history.md`.

---

## Cross-Domain Couplings

| If you change… | Also check |
| --- | --- |
| `IRequest`/`IRequestHandler`/`ISender`/`IPipelineBehavior` shapes | `.Mediator.MediatR` envelopes and dispatchers; `17.Workflows`' `CommandActivity<>`; `19.Scheduling`'s `ScheduledCommandJob<>`; `16.Testing/SharedKernel.Application.Testing`'s `ApplicationPipelineTestHarness`; `samples/OrderApi` |
| `IRequestValidator<T>` | `01.Core/SharedKernel.Validation.FluentValidation`'s bridge; `14.Presentation`'s field-keyed ProblemDetails |
| `IdempotencyBehavior`'s use of `IIdempotencyStore` | `18.Idempotency` stores; `16.Testing/SharedKernel.Idempotency.Testing`'s `FakeIdempotencyStore` |
| `ICacheableQuery<TValue>`/`IInvalidatesCache`/key or tag scoping | `02.Caching.Abstractions`' `CacheKeyFormat`/`ITenantCacheKeyProvider`/`CachePolicy.ForTenant`; SK0017/SK0018/SK0041 |
| `IDomainEventHandler<T>`/`DomainEventDispatcher` | `06.Persistence`'s save pipeline; `03.Domain`'s `IDomainEventDispatcher` |
| Stage order in `ApplicationPipelineBuilder.Register` | `SharedKernel.Application.Pipeline.Tests/PipelineOrderTests`; `00.Governance`'s `ApplicationPipelineRules` |
| Any public API | `PublicAPI.Unshipped.txt` of the package and its `README.md` |

---

## Test Rules

- Prove pipeline order and cross-behavior interaction through a real `ServiceCollection` +
  `AddSharedKernelApplication` + `RequestPipeline<,>` (or `ISender` via `app.UseMediatR()`) — never a
  hand-rolled continuation standing in for the whole pipeline.
- Every row in "Each behavior's contract" has a test for its success, failure and (where applicable) exception path.
- The nested-command guard has dedicated coverage: merge on success, discard on failure/exception, `OnCompleted`
  throwing when no command is active.
- Idempotency covers `Started`/`InProgress`/`Completed`/`FingerprintMismatch`, release on failure and on exception.
- `WithBehavior` stage ordering, the host-start seam check (every missing service named, `[RequirePermission]` demanding
  `IRequestContext`, `ISender` always demanded) and the double-call guard have dedicated tests.
- Authorization covers: unmarked request unchecked without resolving `IRequestContext`, 401 for an anonymous caller,
  403 per attribute, alternatives within one attribute, several attributes, inheritance, the stream variant.
- Idempotency covers per-caller scoping (two callers with one key each execute; the same caller replays; anonymous
  callers of a tenant share a scope) and the key layout.
- `.Mediator.MediatR` tests cover discovery (commands, queries, streams, domain-event handlers), a hand-registered
  handler kept, duplicate handlers rejected, and a missing handler's error.
- `.Pipeline.Caching` concurrency is proven against a real FusionCache, not only the fake.

---

## Changelog

Entries before 2026-09-15 are in [`CLAUDE.history.md`](CLAUDE.history.md); later history, including WO-086
(P-564, P-565, P-567, P-568, P-571) and P-579 (main's P-563 application model and P-562 X3 merged onto WO-086;
design record with the supersession note in [`docs/p563/design.md`](docs/p563/design.md)), is recorded in [`state-map.md`](state-map.md) and the root `CLAUDE.changelog.md`.
