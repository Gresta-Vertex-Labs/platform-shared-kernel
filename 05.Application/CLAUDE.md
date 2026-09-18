# 05.Application — MediatR CQRS Vocabulary, Domain-Event Bridge & Pipeline Behaviors

> **Audience:** maintainers and AI agents changing code in this folder.
> **Consumers** read each package's own `README.md`; the folder overview is [`README.md`](README.md).
> This brain holds only what the source does not make obvious: rules, traps, couplings and decisions.
> It never repeats the README. Historical work-order narrative (WO-035 through WO-080, describing
> capabilities since removed by the 2026-09-15 redesign) lives in [`CLAUDE.history.md`](CLAUDE.history.md)
> — read it only to understand *why* a since-removed capability once existed, never as a description of
> the code on disk today.

## What This Domain Is

The MediatR-based CQRS plumbing layer: command/query vocabulary, a request-context seam, the
domain-event-to-MediatR bridge, and opt-in cross-cutting pipeline behaviors composed in a fixed order.

**Philosophy:** MediatR's own `IRequest<TResponse>`/`IRequestHandler<,>`/`IPipelineBehavior<,>` interfaces
*are* the abstraction — this domain adds platform vocabulary on top, never a second mediator underneath.
Handlers return `Result`/`Result<T>` exclusively; there is no response envelope. Every infrastructure-facing
behavior depends on a *minimal seam interface this domain owns*, never a direct reference to the real
infrastructure package — the consuming service bridges each seam to its real implementation at its own
composition root.

**Hard rules**

1. `SharedKernel.Application` references only `SharedKernel.Primitives`, `SharedKernel.Domain`, and
   `MediatR`. Never `02.Caching`, `06.Persistence`, `07.Messaging`, `12.Security`, or any concrete
   infrastructure package.
2. `SharedKernel.Application.Behaviors` adds only `SharedKernel.Application`, `MediatR`,
   `FluentValidation`, and the `Microsoft.Extensions.*` abstractions/diagnostics packages it needs. It
   must **never** reference `SharedKernel.Caching.Abstractions` — that reference belongs exclusively to
   the sibling `SharedKernel.Application.Behaviors.Caching`.
3. Neither package calls `services.AddMediatR(...)`. The consuming service owns MediatR registration and
   assembly scanning; these packages only append vocabulary and `IPipelineBehavior<,>` registrations.
4. No response envelope, ever. Handlers return `Result`/`Result<T>`; `14.Presentation`'s
   `ResultHttpExtensions` maps a failure to RFC 9457 ProblemDetails at the HTTP boundary;
   `11.Communication`'s REST client maps it back with `ReadResultAsync<T>`.
5. AOT and trimming are **not** constraints here (user ruling, 2026-09-15). Reflection is used where it
   is the clearest code — see "Reflection use" below — never guarded behind `[RequiresUnreferencedCode]`
   gymnastics.
6. Every local seam (`IRequestContext`, `IUnitOfWork`, `IRequestIdempotencyStore`, `IAuditTrailWriter`) is
   a minimal interface *owned by this domain*. Adding a project reference from either package to the real
   infrastructure package that seam bridges to — to "simplify" the bridge — is a hard violation.
7. Registration order in `ApplicationBehaviorsBuilder.Build()` is *not* the same thing as onion-wrap
   execution order for post-`next()` code: the first-registered behavior in a stage is outermost, so its
   code *after* `next()` returns runs **last**, after every later-registered (more-inner) behavior in
   that stage has already unwound. `CommandScopeBehavior` is registered first among command-stage
   behaviors specifically so its callback-running code observes `TransactionBehavior`'s commit (and
   `AuditingBehavior`'s and `IdempotencyBehavior`'s post-`next()` code) as already complete.

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Application` | CQRS vocabulary (`ICommand`/`ICommand<TResponse>`/`IQuery<TResponse>`/`IStreamQuery<TResponse>`), the handler-alias interfaces, the `IRequestContext` seam, the domain-event-to-MediatR bridge | `SharedKernel.Primitives`, `SharedKernel.Domain`, `MediatR` |
| `SharedKernel.Application.Behaviors` | Eight pipeline behaviors (Tracing, Logging, Metrics, Authorization, Validation, Idempotency, Transaction, Auditing) plus `ApplicationBehaviorsBuilder`/`PipelineStage` | `SharedKernel.Application`, `SharedKernel.Primitives`, `MediatR`, `FluentValidation` |
| `SharedKernel.Application.Behaviors.Caching` | `CachingBehavior<,>` + `ICacheableQuery<TValue>`, `CacheInvalidationBehavior<,>` + `IInvalidatesCache` — the **only** package in this domain permitted a `SharedKernel.Caching.Abstractions` reference | `SharedKernel.Application.Behaviors`, `SharedKernel.Caching.Abstractions` |

Every test project is nested inside the package it tests and references **only that package** — never
`16.Testing` or `00.Governance`'s `SharedKernel.ArchitectureTests` (both packages currently depend on
this domain and are migrated onto the redesigned contracts separately; see `state-map.md`'s `SK.05.P544`
phase). Local test doubles live inside each test project.

**Microservices always reference `SharedKernel.Application`.** `SharedKernel.Application.Behaviors` and
`.Behaviors.Caching` are opt-in — reference them only when adopting platform pipeline behaviors.

---

## Technology Stack

| Concern | Choice |
| --- | --- |
| Mediator | `MediatR` pinned to `12.4.x` — the last MIT-licensed major (v13 introduced a commercial license); a deliberate platform ceiling, never bumped incidentally |
| Validation | `FluentValidation`, validators run **sequentially** (never `Task.WhenAll` — a validator may hold a non-concurrency-safe scoped resource such as a `DbContext`) |
| Tracing | `System.Diagnostics.ActivitySource` named `SharedKernel.Application` — a process-lifetime static instrument, the one sanctioned static-state exception (mirrors `07.Messaging.MassTransit`'s `MessagingDiagnostics.ActivitySource`) |
| Metrics | `System.Diagnostics.Metrics.Meter`, same name, resolved as a DI singleton from `IMeterFactory` (not a static field) — `Histogram<double>` `sharedkernel.application.request.duration`, unit `s` |
| Serialization | Reflection-based `System.Text.Json`; `Idempotency/IdempotencyResponseSerializer` adds two hand-written `JsonConverter`s for `Result`/`Result<T>` (see "Reflection use") |
| Logging | `[LoggerMessage]` source-generated partial methods, `EventId`s `5100`-`5199` (`Shared/ApplicationBehaviorsLoggingEventIds.cs`) — see `01.Core`'s `LoggingEventIdRanges.Application` registry |

---

## DI Registration

```csharp
services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
services.AddValidatorsFromAssemblyContaining<Program>();

services.AddSharedKernelApplication();                                   // domain-event-to-MediatR bridge
services.AddDomainEventHandler<OrderPlacedDomainEvent, Handler>();

// Local-seam bridges — implement each against real infrastructure at the composition root.
services.AddScoped<IRequestContext, HttpRequestContext>();               // -> 12.Security.Abstractions
services.AddScoped<IUnitOfWork, EfUnitOfWorkAdapter>();                  // -> 06.Persistence.Abstractions
services.AddScoped<IRequestIdempotencyStore, RedisIdempotencyStore>();   // -> 18.Idempotency, or your own
services.AddScoped<IAuditTrailWriter, PersistenceAuditTrailWriterAdapter>(); // -> 06.Persistence.Abstractions
services.AddSharedKernelCaching(o => o.ServiceName = "orders");         // 02.Caching.FusionCache — registers ICacheService

services.AddSharedKernelApplicationBehaviors()
    .AddTracingBehavior()
    .AddLoggingBehavior()
    .AddMetricsBehavior()
    .AddAuthorizationBehavior()      // requires IRequestContext
    .AddValidationBehavior()
    .AddCachingBehaviors()           // SharedKernel.Application.Behaviors.Caching — Query + Command stage
    .AddIdempotencyBehavior()        // requires IRequestIdempotencyStore
    .AddTransactionBehavior()        // requires IUnitOfWork
    .AddAuditingBehavior()           // requires IAuditTrailWriter
    .Build();
```

`AddDefaultBehaviors()` = `AddTracingBehavior().AddLoggingBehavior().AddMetricsBehavior().AddValidationBehavior()`
— the only four behaviors with **zero** `Build()`-time missing-dependency guard, provably equivalent to
calling the four individually. Every infrastructure-gated behavior (Authorization, Idempotency,
Transaction, Auditing, Caching, and any custom behavior registered via `AddBehavior`) stays a deliberate,
individual opt-in.

A sibling package or consuming service extends the pipeline via `AddBehavior(openGenericType, stage,
requiredServices)` — this is exactly how `AddCachingBehaviors()` wires `CachingBehavior<,>`/
`CacheInvalidationBehavior<,>` into the shared pipeline without `SharedKernel.Application.Behaviors` ever
referencing `SharedKernel.Caching.Abstractions`.

---

## AOT Compatibility

Not a design constraint (user ruling, 2026-09-15). **Reflection use, every instance cached once per
closed type/`Type` and never re-resolved per call:**

| Site | What | Why |
| --- | --- | --- |
| `Shared/FailureResponse.cs` | `TResponse.GetMethod("Failure", ...)` + `CreateDelegate`, cached per closed `TResponse` in a generic nested class | Behaviors short-circuit with a failed response whose concrete shape (`Result` vs. a closed `Result<T>`) is only known through the open `TResponse` parameter |
| `Shared/ResponseOutcome.cs` | `TResponse.GetProperty("Error")` + `CreateDelegate`, cached per closed `TResponse` | Reads the `Error` off any `IHasSuccessFlag`-failure response without a hardcoded `Result<T>` cast |
| `DomainEvents/MediatRDomainEventDispatcher.cs` | `typeof(DomainEventNotification<>).MakeGenericType(eventType)` to find the closed constructor, then an `Expression` tree calling it, compiled once via `Compile()` and cached per runtime event `Type` in a static `ConcurrentDictionary<Type, Func<IDomainEvent, INotification>>` | The concrete domain-event type is only known per-element at runtime; compiling once (rather than `Activator.CreateInstance` per call) trades a one-time build cost for a plain delegate invocation on every later dispatch of that type — the same documented exception class as `07.Messaging`'s `MassTransitEventPublisher` |
| `Idempotency/IdempotencyResponseSerializer.cs` | Two hand-written `JsonConverter<Result>`/`JsonConverter<Result<T>>` plus a `JsonConverterFactory` resolving the open-generic case | `Result`/`Result<T>` (`01.Core`) have only `private` constructors and get-only properties, so neither round-trips through STJ's default reflection contract |

No other reflection appears in either package. `ApplicationBehaviorsBuilder.AddBehavior`'s validation
(`Type.IsGenericTypeDefinition`, `GetInterfaces()`) runs once, at `Build()` time, not per request.

---

## Interface Contracts

### `SharedKernel.Application`

| Namespace | Types |
| --- | --- |
| `Messaging` | `ICommandBase` (zero-member marker); `ICommand : ICommandBase, IRequest<Result>`; `ICommand<TResponse> : ICommandBase, IRequest<Result<TResponse>>`; `IQueryBase` (zero-member marker); `IQuery<TResponse> : IQueryBase, IRequest<Result<TResponse>>`; `ICommandHandler<TCommand>`, `ICommandHandler<TCommand,TResponse>`, `IQueryHandler<TQuery,TResponse>` — pure `IRequestHandler<,>` aliases |
| `Streaming` | `IStreamQuery<TResponse> : IStreamRequest<TResponse>`; `IStreamQueryHandler<TQuery,TResponse> : IStreamRequestHandler<TQuery,TResponse>` — raw per-item payloads, **not** wrapped in `Result<T>`; no pipeline behavior applies (MediatR treats unary and streaming requests as disjoint generic hierarchies) |
| `Context` | `IRequestContext` — `IsAuthenticated`, `UserId` (`string?`), `TenantId` (`Guid?`), `HasPermissionAsync(permission, ct)`; `SystemRequestContext` — always authenticated, caller-supplied identity (default `"system"`) and explicit permission set, for `17.Workflows`/`19.Scheduling` dispatch with no HTTP request behind it; `AnonymousRequestContext` — unauthenticated, no user/tenant/permissions, singleton `.Instance` |
| `DomainEvents` | `IDomainEventHandler<in TDomainEvent>`; `DomainEventNotification<TDomainEvent>` (`INotification` wrapper); internal `DomainEventNotificationHandler<TDomainEvent>`; `MediatRDomainEventDispatcher` (serial-only `IDomainEventDispatcher`) |
| `Extensions` | `AddSharedKernelApplication()` (no configure overload); `AddDomainEventHandler<TDomainEvent,THandler>()` |

### `SharedKernel.Application.Behaviors`

| Namespace | Types |
| --- | --- |
| `Tracing` | `TracingBehavior<,>`; internal `ApplicationDiagnostics.ActivitySource` |
| `Logging` | `LoggingBehavior<,>`; `ApplicationLoggingOptions` (`SlowRequestThreshold`, default 500ms); `ILoggableRequest<TResponse>` (opt-in `LoggableRequestFields`/`GetLoggableResponseFields`) |
| `Metrics` | `MetricsBehavior<,>` (internal); internal `ApplicationMetrics` singleton |
| `Authorization` | `AuthorizationBehavior<,>`; `IAuthorizeRequest` (`RequiredPermissions`, `PermissionMatch` defaulting to `All`); `PermissionMatch` enum (`All`/`Any`) |
| `Validation` | `ValidationBehavior<,>` |
| `Commands` | `ICommandScope` (`IsActive`, `IsNested`, `OnCompleted`); internal `CommandScope`; internal `CommandScopeBehavior<,>` |
| `Idempotency` | `IIdempotentRequest` (`IdempotencyKey`, `Fingerprint` — optional, defaults to `null`); `IRequestIdempotencyStore` (`TryBeginAsync`/`CompleteAsync`/`ReleaseAsync`); `IdempotencyBeginResult`, `IdempotencyBeginStatus`; `IdempotencyBehavior<,>`; internal `IdempotencyResponseSerializer`, `RequestFingerprint` |
| `Transaction` | `IUnitOfWork` (`SaveChangesAsync`); `TransactionBehavior<,>` |
| `Auditing` | `IAuditableRequest<TResponse>` (`Action`, `ResourceType`, `ResourceId`, `BeforeSnapshot`, `GetAfterSnapshot`); `IAuditTrailWriter` (`RecordAsync`); `AuditEntry` (record, includes `Succeeded`/`ErrorCode`); `AuditingBehavior<,>` |
| `Extensions` | `ApplicationBehaviorsBuilder` (`.AddXBehavior()` methods, `AddBehavior`, `AddDefaultBehaviors`, `Build`); `ApplicationBehaviorsServiceCollectionExtensions.AddSharedKernelApplicationBehaviors()`; `PipelineStage` enum |
| `Shared` (internal) | `FailureResponse`, `ResponseOutcome`, `RequestKind`, `ApplicationBehaviorsLoggingEventIds` |

### `SharedKernel.Application.Behaviors.Caching`

| Namespace | Types |
| --- | --- |
| `Caching` | `ICacheableQuery<TValue>` (`CacheKey`, `CachePolicy`; also an `IQuery<TValue>`); `CachingBehavior<,>`, which caches the `TValue` of a successful `Result<TValue>` |
| `CacheInvalidation` | `IInvalidatesCache` (`CacheKeysToInvalidate`, `CacheTagsToInvalidate` defaulting to empty); `CacheInvalidationBehavior<,>` |
| `Extensions` | `CachingBehaviorsExtensions.AddCachingBehaviors(ApplicationBehaviorsBuilder)` |

---

## Pipeline: stages and canonical order

Fixed regardless of `.AddXBehavior()`/`AddBehavior` call order. Outermost first:

```text
Observability stage   1. TracingBehavior
                       2. LoggingBehavior
                       3. MetricsBehavior
                       [custom Observability-stage behaviors]
Authorization stage   4. AuthorizationBehavior
                       [custom Authorization-stage behaviors]
Validation stage      5. ValidationBehavior
                       [custom Validation-stage behaviors]
Query stage            [custom Query-stage behaviors — e.g. CachingBehavior; no built-in of its own]
Command stage          CommandScopeBehavior (always first when the command stage is active)
                        IdempotencyBehavior
                        TransactionBehavior
                        AuditingBehavior
                        [custom Command-stage behaviors — e.g. CacheInvalidationBehavior]
                       handler
```

Authorization runs **before** Validation deliberately — an unauthorized caller must never learn a
request's validation rules, and a validator may itself hit the database. `CommandScopeBehavior` is
registered automatically by `Build()` whenever any command-stage behavior (idempotency, transaction,
auditing, or a custom `PipelineStage.Command` entry) is active; `ICommandScope` itself is *always*
registered so a handler may inject it regardless.

### Each behavior's contract

| Behavior | Success | `Result`/`Result<T>` failure | Thrown exception |
| --- | --- | --- | --- |
| `TracingBehavior` | tags only (`request.type`, `request.kind`) | span `Error`, description = error code, tags `error.type`/`error.code` | span `Error`, `Activity.AddException`, tag `error.type` = exception full name, rethrows |
| `LoggingBehavior` | `Information` (or `Warning` if over `SlowRequestThreshold`) | `Warning`, names error type/code | `Error` with the exception, rethrows |
| `MetricsBehavior` | records `outcome="success"` | records `outcome="failure"`, `error.type` = `Error.Type` | records `outcome="exception"`, `error.type` = exception full name, rethrows (via `finally`) |
| `AuthorizationBehavior` | calls `next()` | short-circuits **before** `next()`: unauthenticated → `Error.Unauthorized`; empty `RequiredPermissions` → `Error.Forbidden("authorization.no_permissions_declared")`; permission denied → `Error.Forbidden(ErrorCodes.Forbidden.InsufficientPermission)` | never throws itself |
| `ValidationBehavior` | calls `next()` when zero failures | short-circuits before `next()` with `Error.Validation(errors)`: each child coded by `failure.ErrorCode` with the field path in `MessageArguments[PropertyPath]` | never throws itself |
| `CommandScopeBehavior` | runs queued `OnCompleted` callbacks (outermost frame only) after `next()` returns | discards the frame's callbacks | discards the frame's callbacks (depth reset in `finally` either way) |
| `IdempotencyBehavior` | `CompleteAsync` (persists the serialized response, passing back the reservation token; a `false` result logs a `Warning` but still returns the response) | `ReleaseAsync` (never `CompleteAsync` — a failure must remain retryable) | `ReleaseAsync`, then rethrows |
| `TransactionBehavior` | `IUnitOfWork.SaveChangesAsync` (outermost command only) | no commit | no commit — the exception propagates before this behavior's post-`next()` code runs |
| `AuditingBehavior` | `RecordAsync` with `Succeeded=true`, `AfterSnapshot` computed | `RecordAsync` with `Succeeded=false`, `ErrorCode` = the response's `Error.Code`, `AfterSnapshot=null` | `RecordAsync` with `Succeeded=false`, `ErrorCode` = the exception type's full name, `AfterSnapshot=null`; a `RecordAsync` failure here is logged (`LogAuditWriteFailedDuringException`, EventId 5130), not propagated — the **original** exception always rethrows |
| `CachingBehavior` (Query stage, `.Caching`) | runs the handler inside `GetOrSetAsync` (once per key across concurrent callers) and caches the response | returned to every waiting caller, never cached (`CacheFactoryContext.SkipCaching`) | propagates, nothing cached; the query policy's fail-safe may serve an expired entry instead |
| `CacheInvalidationBehavior` (Command stage, `.Caching`) | registers an `ICommandScope.OnCompleted` callback that evicts after commit (`RemoveAsync` per key, `RemoveByTagAsync` per tag); keys and tags are scoped and validated **before** the handler runs, so an invalid key fails the command instead of the post-commit callback | nothing registered | nothing registered |

A nested command (`ICommandScope.IsNested`) makes `IdempotencyBehavior` and `TransactionBehavior` call
`next()` directly, skipping their own reservation/commit logic entirely — only the **outermost** command
in a DI scope owns the idempotency key and the transaction.

---

## The nested-command guard and `ICommandScope`

`ICommandScope` (`Commands/`) tracks, per DI scope, whether a command is executing (`IsActive`) and
whether the current command was sent from inside another command's handler (`IsNested`), and lets a
handler or behavior queue work to run once the **outermost** command succeeds (`OnCompleted`).

`CommandScopeBehavior<,>` is the sole owner of entering/exiting frames (a `Stack<List<Func<CancellationToken,
Task>>>` internally): `Enter()` pushes a frame on every dispatch, including nested ones; `Exit(succeeded)`
pops it. A nested command's callbacks, on success, are **merged into the parent frame** rather than run —
they still only ever run once, after the outermost command succeeds. A failed or faulted command (nested
or outermost) discards its frame's callbacks outright. `OnCompleted` throws `InvalidOperationException`
if called when no command is active in the scope.

Because MediatR dispatches a nested `ISender.Send()` call through the *same* DI scope as the outer
command, one `ICommandScope` instance observes the entire nesting depth for one logical command
execution — this is what lets `IdempotencyBehavior`/`TransactionBehavior` detect `IsNested` correctly.

A callback that throws during the outermost command's post-success run is logged at `Error`
(`ApplicationBehaviorsLoggingEventIds.LogCommandScopeCallbackFailed`) and does **not** change the
response — the work it was meant to follow up on is already committed.

---

## The idempotency store contract

`IRequestIdempotencyStore` (`Idempotency/`) is a minimal seam, deliberately **not**
`07.Messaging.Abstractions.IIdempotencyStore` (a different, consumer-side deduplication contract — `18.Idempotency`
implements both in one file for a service that needs both).

- `TryBeginAsync(key, requestFingerprint, ct)` atomically reserves a key and records the request's
  fingerprint (lowercase-hex SHA-256 of the request's default JSON serialization). A winning call
  (`Started`) carries a `ReservationToken` — an opaque string the caller must pass back to
  `CompleteAsync`/`ReleaseAsync`; every other status leaves it `null`.
- A key that is reserved but not yet completed returns `InProgress` — the behavior fails with
  `Error.Conflict("idempotency.in_progress")` without calling `next()`.
- A key already completed with the **same** fingerprint returns `Completed` with the stored response —
  deserialized and returned directly, replaying the original outcome, `next()` never called.
- A key that exists (in-flight or completed) against a **different** fingerprint returns
  `FingerprintMismatch` — `Error.Conflict("idempotency.key_reused")`.
- A reservation that is never completed or released expires after a store-defined in-flight TTL, so a
  crashed process can never permanently wedge a key.
- `CompleteAsync(key, reservationToken, serializedResponse, ct)` and `ReleaseAsync(key, reservationToken, ct)`
  both return `bool`: `true` when `reservationToken` still owned the reservation and the mutation
  applied, `false` — never a thrown exception — when the reservation was already lost (expired and
  reclaimed by another caller, already completed/released, or a foreign token). This is what makes a
  store implementation genuinely stateless: it never has to remember which caller won which
  reservation itself, because the caller proves ownership on every subsequent call. A `false` from
  `CompleteAsync` after a successful handler run does not fail the request — the work already
  happened, so `IdempotencyBehavior` still returns the response, only logging a `Warning`
  (`ApplicationBehaviorsLoggingEventIds.LogCompleteReservationLost`, EventId 5120). A `false` from
  `ReleaseAsync` needs no log — the reservation already being gone is exactly the outcome a release
  call wants.
- The idempotency key itself is never echoed in any error message returned to the caller.

---

## The authorization fail-closed rule

`AuthorizationBehavior<,>` treats an **empty** `IAuthorizeRequest.RequiredPermissions` as a
misconfiguration, not "no check needed": it returns `Error.Forbidden("authorization.no_permissions_declared")`
rather than allowing the request through. A request that genuinely needs no permission check must not
implement `IAuthorizeRequest` at all — implementing it with nothing declared is refused, never silently
allowed. `PermissionMatch.All` short-circuits on the first missing permission; `PermissionMatch.Any`
short-circuits on the first held permission. Denial never echoes which permission was missing.

---

## Local seams

| Seam | Package | Bridges to |
| --- | --- | --- |
| `Context.IRequestContext` | `SharedKernel.Application` | `12.Security.Abstractions.IUserContext`/`ITenantProvider` |
| `Transaction.IUnitOfWork` | `SharedKernel.Application.Behaviors` | `06.Persistence.Abstractions.IUnitOfWork` (`06.Persistence` may reference `05.Application`, so `EfUnitOfWork` may implement this directly instead of needing an adapter) |
| `Idempotency.IRequestIdempotencyStore` | `SharedKernel.Application.Behaviors` | `18.Idempotency`, or a consumer-supplied store |
| `Auditing.IAuditTrailWriter` | `SharedKernel.Application.Behaviors` | `06.Persistence.Abstractions.IAuditTrailWriter` (a richer contract — the bridge adapter discards the persisted record this local seam never needs back) |

Each is a *minimal* interface, deliberately smaller than the real contract it bridges to — resolving
identity, tenancy, timestamps, and hash-chain linkage is entirely the real implementation's job, never
this domain's. This is the same pattern applied four times, not four different patterns.

---

## What we removed and why

The 2026-09-15 pre-publish redesign (root P-544) removed the following from the pre-existing, unpublished
surface. Nothing had shipped to a feed, so every removal was free — no `[Obsolete]`, no shim, no
migration path.

| Removed | Why |
| --- | --- |
| Fire-and-forget (`IFireAndForgetCommand`, `ChannelFireAndForgetDispatcher`, `FireAndForgetBackgroundConsumer`, `FireAndForgetGuardBehavior`, `FireAndForgetOptions`, `FireAndForgetDispatchContext`) | User ruling. The guard/trusted-dispatch machinery existed only to police a self-inflicted footgun; a consuming service that wants fire-and-forget dispatch can queue its own background work without this domain policing it |
| `ResilienceBehavior<,>` + `IRetryableRequest` | Drops the Polly dependency entirely. Retry policy belongs to the infrastructure call a handler makes (e.g. a typed HTTP client's own resilience pipeline in `11.Communication`), not a blind command-level retry that risks re-running a side effect |
| Parallel domain-event dispatch (`MediatRDomainEventDispatcherOptions`/`ParallelDispatch`) | `MediatRDomainEventDispatcher` is serial-only now. Parallel dispatch complicated ordering guarantees and error aggregation for no proven need, and was a documented, mechanically-unenforced misuse trap for ordered events |
| Every streaming pipeline behavior (`Stream*Behavior`, `IStreamPipelineBehavior<,>` registrations) | MediatR treats unary and streaming requests as two disjoint generic hierarchies with no shared base — each was a hand-duplicated copy of its unary sibling. `IStreamQuery<TResponse>`/`IStreamQueryHandler<,>` vocabulary itself stays; it still has zero behavior coverage, by design |
| `DualApprovalBehavior<,>` + `IRequiresDualApproval` + `IDualApprovalStore` + `IAuthorizationContextIdentity` | User ruling. Maker-checker approval is an application-specific workflow better composed by the consuming service itself, not a platform-wide pipeline primitive |
| A response envelope | Never existed here — handlers have always returned `Result`/`Result<T>` only. Noted for completeness: `04.Contracts`'s independent `Envelope<T>` was removed the same day, for the same reason |

The old `IAuthorizationContext`/`IIdempotencyKeyStore`/`IIdempotencyResponseStore` seam shapes were
likewise replaced outright by `IRequestContext` and `IRequestIdempotencyStore` — not layered alongside
them.

---

## `CachingBehavior` caches the value, not the `Result`

`CachingBehavior<,>` (`SharedKernel.Application.Behaviors.Caching`) stores the `TValue` of a successful
`Result<TValue>` and rebuilds `Result<TValue>.Success(value)` on a hit (P-549). The query declares
`ICacheableQuery<TValue>`, which is also an `IQuery<TValue>`. The behavior is constrained to the non-generic
`ICacheableQuery` base, whose internal `GetOrSetAsync<TResponse>` is implemented by the generic interface, where
`TValue` is known, so the typed cache call needs no reflection. A consumer cannot implement the non-generic base
alone: its internal member is inaccessible outside the assembly.

The handler still runs inside the context overload of `ICacheService.GetOrSetAsync<TValue>`. A failed `Result` is
captured in the factory, skipped with `CacheFactoryContext.SkipCaching()` and returned by that call only; a query
waiting on the same key finds nothing cached and runs the handler itself.

Why: `Result`/`Result<T>` have private constructors and `Value`/`Error` properties that throw in the opposite
state, so reflection-based `System.Text.Json` cannot even **write** a cached `Result<T>` — verified, the
FusionCache serializer throws `FusionCacheSerializationException` on the L2 write. Caching the value keeps
`01.Core`'s `Result` free of serialization concerns. `CachingBehaviorDistributedCacheTests` proves the round
trip with two FusionCache instances sharing one distributed cache.

Rule: `TValue` must round-trip through `System.Text.Json`, and a service with a cache `SerializerContext` adds
`TValue` to it.

---

## Decision Records

| Decision | Chosen | Rejected | Accepted cost |
| --- | --- | --- | --- |
| `CachingBehavior` cache-miss path | `ICacheService.GetOrSetAsync` context overload; a failed `Result` calls `SkipCaching()`; the policy is forced to `WithoutEagerRefresh()` and `WithFactoryTimeouts(null, null)` (P-547) | A `TryGetAsync`/`SetAsync` pair (no stampede protection); keeping eager refresh or factory timeouts | Neither eager refresh nor a soft/hard factory timeout is available to a cached query: both let FusionCache run the handler in the background after the request's DI scope is disposed. Fail-safe and every other policy setting are kept |
| Tenant cache-key/tag scoping | The internal `CacheScope` helper over `CacheKeyFormat`: key `@{tenant}:{key}` (`BuildTenantTag`, tenant as `Guid` `"D"`), policy `ForTenant(tenant)`, tags `@{tenant}:{tag}`; without a tenant the key is used as is and one starting with `@` throws `ArgumentException` (P-547) | `02.Caching.Abstractions`'s `ITenantCacheService`/`ITenantCacheKeyProvider` (they take `(entity, id)`, not a free-form query key) | `.Caching` stays a consumer of the plain `ICacheService`, yet uses the same escaped tenant format, so no tenant can read another tenant's or a global entry, and `ITenantCacheService.RemoveTenantAsync` also removes the tenant's cached query results |
| Pipeline extensibility | A five-value `PipelineStage` enum + `AddBehavior(openGenericType, stage, requiredServices)` | An unordered, purely additive registration list | A sibling package (`.Caching`) can slot precisely between Validation and the command stage without `SharedKernel.Application.Behaviors` knowing it exists |
| Idempotency store naming | `IRequestIdempotencyStore` | Reusing `07.Messaging.Abstractions.IIdempotencyStore`'s name | Avoids a same-name collision — `18.Idempotency` implements both contracts in one file |
| AOT/trimming | Not a constraint (user ruling) | Reflection-free everywhere, at the cost of ceremony | Reflection used at the four documented sites, each cached once per closed type |
| Response construction on short-circuit | `FailureResponse.Create<TResponse>` — a cached, reflection-resolved `Failure(Error)` delegate | A second, parallel non-`Result` failure shape | Every short-circuiting behavior (Authorization, Validation, Idempotency) shares one mechanism |
| `ApplicationLoggingOptions` startup validation | `[Range]` DataAnnotation on `SlowRequestThreshold` + `.AddOptions<T>().ValidateDataAnnotations().ValidateOnStart()` (`Microsoft.Extensions.Options.DataAnnotations`) | `01.Core/SharedKernel.Configuration`'s `AddValidatedOptions<TOptions>`/`ISectionBoundOptions` | Adopting `SharedKernel.Configuration` would add a `01.Core` project reference this package doesn't otherwise need; `Microsoft.Extensions.Options.DataAnnotations` is a plain `Microsoft.Extensions.*` NuGet package, already within this package's allowed reference set. `ValidateOnStart()` registers an `IStartupValidator` a real host invokes at startup — an invalid threshold fails before the first request, not at `LoggingBehavior`'s first invocation |

---

## Cross-Domain Couplings

Changes here that silently break another layer. Check the right column before merging.

| If you change… | Also check |
| --- | --- |
| `Transaction.IUnitOfWork`'s shape | `06.Persistence`'s `EfUnitOfWork` bridge |
| `Idempotency.IRequestIdempotencyStore`'s contract | `18.Idempotency`'s store implementations; `16.Testing`'s fake (both migrated onto the stateless `reservationToken`/`bool`-returning shape, same day as `SK.05.P544`) |
| `Auditing.IAuditTrailWriter`/`AuditEntry`'s shape | `06.Persistence`'s real `IAuditTrailWriter` bridge adapter; `16.Testing`'s fake |
| `Context.IRequestContext`'s shape | Every consuming service's composition-root bridge; `12.Security.Abstractions.IUserContext`/`ITenantProvider` mapping |
| `ICacheableQuery<TValue>`/`IInvalidatesCache`/tag scoping | `02.Caching.Abstractions`'s `ICacheService`/`CachePolicy`/`CacheFactoryContext`/`CacheKeyFormat` tenant format |
| `ApplicationBehaviorsBuilder`'s registration order | `00.Governance`'s pipeline-order architecture test (mid-migration as of `SK.05.P544`) |
| Any public API | `PublicAPI.Unshipped.txt` in the affected package, that package's own `README.md` |

---

## Test Rules

- Every test project references **only** the package it tests — never `16.Testing` or `00.Governance`'s
  `SharedKernel.ArchitectureTests`. Local test doubles (a hand-rolled `IUnitOfWork`, `IRequestContext`,
  etc.) live in the test project itself.
- Prove pipeline order and cross-behavior interaction through a real `ServiceCollection` + `AddMediatR` +
  `ApplicationBehaviorsBuilder` composed dispatch — never a hand-rolled `RequestHandlerDelegate<TResponse>`
  mock standing in for the whole pipeline.
- Every row in "Each behavior's contract" above has at least one test for its success, failure, and
  (where applicable) exception path.
- The nested-command guard has dedicated coverage: a nested command's callbacks merging into the parent
  frame on success, discarding on failure/exception, and `OnCompleted` throwing when no command is active.
- Idempotency coverage includes `Started`/`InProgress`/`Completed`/`FingerprintMismatch`, release-on-failure,
  and release-on-exception.
- `AddBehavior`'s stage ordering and required-service `Build()`-time guard both have dedicated tests.

---

## Changelog

> Entries before 2026-09-15 describe a since-redesigned surface and were moved to
> [`CLAUDE.history.md`](CLAUDE.history.md) — it was 1,567 lines of work-order narrative (WO-035 through
> WO-080) describing types this redesign removed or replaced (fire-and-forget, resilience, streaming
> behaviors, parallel domain-event dispatch, dual approval, the old `IAuthorizationContext`/
> `IIdempotencyKeyStore`/`IIdempotencyResponseStore` seams), not current behaviour.

- [2026-09-15] **Root P-544 — pre-publish redesign (this domain's `SK.05.P544` phase).** Nothing in
  `05.Application` had reached a feed, so this was a breaking rewrite by user ruling, not an additive
  release. Package split: `SharedKernel.Application` (vocabulary + `IRequestContext` + domain-event
  bridge), `SharedKernel.Application.Behaviors` (eight core behaviors), and a new
  `SharedKernel.Application.Behaviors.Caching` (the caching pair, published after `02.Caching`; it caches the query value, see above). Removed fire-and-forget, resilience, every streaming behavior, parallel
  domain-event dispatch, and dual approval outright. Redesigned the idempotency contract
  (`IRequestIdempotencyStore`), added `IQueryBase`, added the nested-command guard (`ICommandScope`/
  `CommandScopeBehavior`), made authorization fail closed on an empty permission declaration, and made
  every short-circuit path return a `Result` failure — never throw. AOT/trimming ruled a non-constraint;
  XML docs describe current behaviour only, with no WO/P history. Cross-domain migration (`18.Idempotency`,
  `16.Testing`, `00.Governance`, `13.ServiceDefaults`, `06.Persistence`, `17.Workflows`, `19.Scheduling`,
  samples) dispatched separately, in progress as of this entry (coordinator)

- [2026-09-16] **Pre-publish hardening pass (P-544 follow-up).** `ApplicationBehaviorsBuilder.Build()`
  now refuses a second call on the same instance (`InvalidOperationException`) instead of silently
  double-registering every opted-in behavior; `AddBehavior` rejects an undefined `PipelineStage` value
  with `ArgumentOutOfRangeException` instead of a bare `KeyNotFoundException`. `ApplicationLoggingOptions.SlowRequestThreshold`
  is now validated (`> TimeSpan.Zero`) via `[Range]` + `.ValidateDataAnnotations().ValidateOnStart()`
  — see the Decision Records entry for why this uses `Microsoft.Extensions.Options.DataAnnotations`
  rather than `01.Core/SharedKernel.Configuration`. `MediatRDomainEventDispatcher`'s per-event-type
  notification factory is now a compiled `Expression` tree instead of `Activator.CreateInstance` per
  dispatch — same cache, same documented reflection site, cheaper on every dispatch after the first for
  a given event type. `IIdempotentRequest` gained an optional `Fingerprint` (defaults to `null`); when
  supplied (trimmed, non-empty) `IdempotencyBehavior` uses it verbatim instead of hashing the request's
  JSON serialization — see the interface's own XML docs for why a command carrying a client timestamp
  or a fresh id per attempt needs this. `RequestFingerprint.Compute`'s fallback path now wraps a
  `JsonException`/`NotSupportedException` from serialization in a clear `InvalidOperationException`
  naming the command type, instead of letting the raw STJ exception escape. Two new `IRequestContext`
  implementations shipped in `SharedKernel.Application.Context`: `SystemRequestContext` (authenticated,
  caller-supplied identity and explicit permission set — never "all permissions") for `17.Workflows`/
  `19.Scheduling` dispatch, and the stateless singleton `AnonymousRequestContext`. `AuditingBehavior`
  now records a fault entry (`Succeeded=false`, `ErrorCode` = the exception type's full name,
  `AfterSnapshot=null`) when the handler throws, then rethrows the original exception unchanged; if
  `IAuditTrailWriter.RecordAsync` itself throws while recording that fault entry, the failure is logged
  (`ApplicationBehaviorsLoggingEventIds.LogAuditWriteFailedDuringException`, EventId 5130) and the
  **original** handler exception is still the one that propagates — the pre-existing fail-closed rule
  (an audit-write failure blocks the pipeline) applies only to the success/`Result.Failure` path, never
  to the exception path, where it would otherwise hide the real fault behind the writer's own exception.
  130 tests passing across the three packages (+31 from this pass)

- [2026-09-15, same day] **Follow-up: stateless `IRequestIdempotencyStore`.** `CompleteAsync`/
  `ReleaseAsync` now take an explicit `reservationToken` (from the new
  `IdempotencyBeginResult.ReservationToken`, non-null exactly when `Status == Started`) and return
  `Task<bool>` instead of `Task` — `true` when the token still owned the reservation and the
  mutation applied, `false` when it did not (never a thrown exception). This removed the need for
  `18.Idempotency`'s two providers to keep any per-instance reservation-tracking state at all.
  `IdempotencyBehavior<TRequest,TResponse>` gained an `ILogger<IdempotencyBehavior<TRequest,TResponse>>`
  constructor parameter and logs a `Warning` (new EventId 5120,
  `ApplicationBehaviorsLoggingEventIds.LogCompleteReservationLost`) when `CompleteAsync` returns
  `false` after a successful handler run — the response is still returned; a `false` from
  `ReleaseAsync` logs nothing. Also fixed same session: `ApplicationBehaviorsBuilder.Build()` now
  calls `services.AddLogging()` unconditionally (idempotent) — `CommandScopeBehavior` and
  `IdempotencyBehavior` both constructor-inject `ILogger<T>`, and a bare `ServiceCollection` with no
  prior `AddLogging()` call would otherwise fail to resolve them at first dispatch instead of at this
  deterministic `Build()` call
