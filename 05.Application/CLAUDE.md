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

1. `SharedKernel.Application.Abstractions` references only `SharedKernel.Primitives` — no MediatR, no ORM —
   because `06.Persistence` and `13.ServiceDefaults` implement its contracts. `SharedKernel.Application` references
   only `SharedKernel.Primitives`, `SharedKernel.Domain`, `SharedKernel.Application.Abstractions` and `MediatR`. Never `02.Caching`, `06.Persistence`, `07.Messaging`, `12.Security`, or any concrete
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
6. The shared contracts (`IRequestContext`, `IUnitOfWork`, `IAuditTrailWriter`, in `SharedKernel.Application.Abstractions`)
   and the local seam `IRequestIdempotencyStore` are *owned by this domain*; infrastructure implements them
   (`06.Persistence` directly, `13.ServiceDefaults.Security` for `IRequestContext`, `18.Idempotency` for the store).
   A reference from any package here to the infrastructure that implements them is a hard violation, and
   redeclaring one of the shared contracts elsewhere is refused by `00.Governance`'s `UnitOfWorkSeamRules` (P-558).
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
| `SharedKernel.Application.Abstractions` | **New in P-558.** The contracts the pipeline and persistence share, MediatR-free: `IRequestContext` (+ `ActorKind`, `SystemRequestContext`, `AnonymousRequestContext`), the one `IUnitOfWork` (+ `CommitOutcomeUnknownException`, `TransactionRolledBackException`), `IAuditTrailWriter`/`AuditEntry`/`AuditOutcome` | `SharedKernel.Primitives` |
| `SharedKernel.Application` | CQRS vocabulary (`ICommand`/`ICommand<TResponse>`/`IQuery<TResponse>`/`IStreamQuery<TResponse>`), the handler-alias interfaces, the domain-event-to-MediatR bridge; `[TypeForwardedTo]` for the three context types that moved to `.Abstractions` | `SharedKernel.Primitives`, `SharedKernel.Domain`, `SharedKernel.Application.Abstractions`, `MediatR` |
| `SharedKernel.Application.Behaviors` | Eight pipeline behaviors (Tracing, Logging, Metrics, Authorization, Validation, Idempotency, Transaction, Auditing — auditing in two halves) plus `ApplicationBehaviorsBuilder`/`PipelineStage` | `SharedKernel.Application`, `SharedKernel.Application.Abstractions`, `SharedKernel.Primitives`, `MediatR`, `FluentValidation` |
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

// The shared contracts are implemented by infrastructure directly — no adapters (P-558).
services.AddSharedKernelRequestContext();                                // 13.ServiceDefaults.Security -> IRequestContext
builder.AddSharedKernelPostgres<OrderDbContext>("orders", p => p.UseAuditTrail()); // 06 -> IUnitOfWork, IAuditTrailWriter
services.AddScoped<IRequestIdempotencyStore, RedisIdempotencyStore>();   // -> 18.Idempotency, or your own
services.AddSharedKernelCaching(o => o.ServiceName = "orders");         // 02.Caching.FusionCache — registers ICacheService

services.AddSharedKernelApplicationBehaviors()
    .AddTracingBehavior()
    .AddLoggingBehavior()
    .AddMetricsBehavior()
    .AddAuthorizationBehavior()      // requires IRequestContext
    .AddValidationBehavior()
    .AddCachingBehaviors()           // SharedKernel.Application.Behaviors.Caching — Query + Command stage
    .AddIdempotencyBehavior()        // requires IRequestIdempotencyStore + IRequestContext (keys are scoped per tenant and caller)
    .AddTransactionBehavior()        // requires IUnitOfWork
    .AddAuditingBehavior()           // requires IAuditTrailWriter; registers both auditing halves
    .Build();
```

The full persistence composition, compiled and run by a test, is `06.Persistence/README.md` ("A multi-tenant
service in 10 minutes").

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

### `SharedKernel.Application.Abstractions` (P-558)

| Namespace | Types |
| --- | --- |
| `Context` | `IRequestContext` — `IsAuthenticated`, `UserId`, `TenantId` (`Guid?`), `HasPermissionAsync`, plus default-implemented `ActorKind` (`User`/`Service`/`System`/`Anonymous`; an unauthenticated caller is `Anonymous`), `ClientId`, `SessionId`, `ImpersonatorId`; `SystemRequestContext` (authenticated, `System`); `AnonymousRequestContext` (`Anonymous`, singleton `.Instance`) |
| `Transactions` | `IUnitOfWork` — `SaveChangesAsync`, `IsTransactionActive`, `ExecuteInTransactionAsync(op[, isolationLevel])`/`<TResult>`, `OnBeforeCommit(callback)`. Contract: the operation may run more than once (retry); a returned failed `Result` rolls back; a call inside an active transaction joins it and a joined failure makes it rollback-only (`TransactionRolledBackException` for an outer success); an ambiguous commit throws `CommitOutcomeUnknownException` and is never replayed; callbacks run after the last save, before commit, and are discarded with a retried attempt. No `BeginTransactionAsync` |
| `Auditing` | `IAuditTrailWriter.RecordAsync(AuditEntry)`; `AuditEntry` (init record: `Action`, `ResourceType`, `ResourceId`, `Outcome`, optional snapshots, `ErrorCode`, `ApprovalId`, `IdempotencyKey` — never actor/tenant/time, the writer resolves those); `AuditOutcome` |

Implemented by `06.Persistence` (`IUnitOfWork`: `EfUnitOfWork`, one transaction per DI scope; `IAuditTrailWriter`:
the audit ledger) and `13.ServiceDefaults.Security` (`IRequestContext` over `12.Security`). The P-557
`ITransactionalUnitOfWork`/`IPersistenceTransaction`/`Behaviors.Auditing.IAuditTrailWriter`/`AuditEntry(bool Succeeded)`
seams and every bridge adapter were deleted.

### `SharedKernel.Application`

| Namespace | Types |
| --- | --- |
| `Messaging` | `ICommandBase` (zero-member marker); `ICommand : ICommandBase, IRequest<Result>`; `ICommand<TResponse> : ICommandBase, IRequest<Result<TResponse>>`; `IQueryBase` (zero-member marker); `IQuery<TResponse> : IQueryBase, IRequest<Result<TResponse>>`; `ICommandHandler<TCommand>`, `ICommandHandler<TCommand,TResponse>`, `IQueryHandler<TQuery,TResponse>` — pure `IRequestHandler<,>` aliases |
| `Streaming` | `IStreamQuery<TResponse> : IStreamRequest<TResponse>`; `IStreamQueryHandler<TQuery,TResponse> : IStreamRequestHandler<TQuery,TResponse>` — raw per-item payloads, **not** wrapped in `Result<T>`; no pipeline behavior applies (MediatR treats unary and streaming requests as disjoint generic hierarchies) |
| `Context` | Type-forwarded to `SharedKernel.Application.Abstractions` (same namespace): `IRequestContext`, `SystemRequestContext` (for `17.Workflows`/`19.Scheduling` dispatch with no HTTP request behind it), `AnonymousRequestContext` |
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
| `Idempotency` | `IIdempotentRequest` (`IdempotencyKey`, `Fingerprint` — optional, defaults to `null`); `IRequestIdempotencyStore` (`TryBeginAsync`/`CompleteAsync`/`ReleaseAsync`); `IdempotencyBeginResult`, `IdempotencyBeginStatus`; `IdempotencyErrorCodes` (`KeyRequired`, `InProgress`, `KeyReused`); `IdempotencyBehavior<,>` (ctor `store, requestContext, commandScope, logger`); internal `IdempotencyKeyScope` (the per-tenant-and-caller key digest, see "The idempotency store contract"), `IdempotencyResponseSerializer`, `RequestFingerprint` |
| `Transaction` | `TransactionBehavior<,>` (over `Abstractions`' `IUnitOfWork`) |
| `Auditing` | `IAuditableRequest<TResponse>` (`Action`, `ResourceType`, `ResourceId`, `BeforeSnapshot`, `GetAfterSnapshot`); `AuditingBehavior<,>` (outer half) and internal `AuditingCommitBehavior<,>` (inner half), both registered by `AddAuditingBehavior()`; internal `AuditEntries` builder |
| `Extensions` | `ApplicationBehaviorsBuilder` (`.AddXBehavior()` methods, `AddBehavior`, `AddDefaultBehaviors`, `Build`); `ApplicationBehaviorsServiceCollectionExtensions.AddSharedKernelApplicationBehaviors()`; `PipelineStage` enum |
| `Shared` (internal) | `FailureResponse`, `ResponseOutcome`, `RequestKind`, `ApplicationBehaviorsLoggingEventIds` |

### `SharedKernel.Application.Behaviors.Caching`

| Namespace | Types |
| --- | --- |
| `Caching` | `ICacheableQuery<TValue>` (`CacheKey`, `CachePolicy`, `Scope`, `RefreshCache`, `ShouldCache`; also an `IQuery<TValue>`); `CacheScope` (`Tenant` = 0, `User`, `Global`); internal `CachingBehavior<,>`, which caches the `TValue` of a successful `Result<TValue>` |
| `CacheInvalidation` | `IInvalidatesCache` (`CacheKeysToInvalidate` as `CacheKeyRef`, `CacheTagsToInvalidate` defaulting to empty, `Scope`); `CacheKeyRef.For<TQuery>(key)`; internal `CacheInvalidationBehavior<,>` |
| `Extensions` | `CachingBehaviorsExtensions.AddCachingBehaviors(ApplicationBehaviorsBuilder)` |
| `Shared` (internal) | `CachingBehaviorsLoggingEventIds` (5200-5299), `CachingBehaviorsMetrics` |

**Both behavior types are internal**, matching every behavior in `.Behaviors`. The package's contract is the
marker interfaces, `CacheScope`, `CacheKeyRef` and `AddCachingBehaviors()`; the behaviors are registered by
that extension and resolved by MediatR, never constructed by a consumer. The test project reaches them through
`InternalsVisibleTo`, the repo's dominant convention (43 projects).

### Keys are namespaced by query type and scope

The key is built through the registered `ITenantCacheKeyProvider`, never string interpolation:

```text
Global   {service}:{QueryType}:{CacheKey}
Tenant   {service}:@{tenant}:{QueryType}:{CacheKey}
User     {service}:@{tenant}:{QueryType}:{CacheKey}:u:{userId}
```

`ICacheableQuery.CacheKey` is therefore the query's identity **within its own namespace**, not a whole key.
The `{QueryType}` segment exists because an entry holds the bare `TValue` as JSON with no type discriminator:
without it, two queries choosing the same `CacheKey` share an entry and `System.Text.Json` deserializes one
payload into the other's type on a best-effort basis, with no error. `00.Governance`'s SK0041 reports the one
way that namespace can still collapse — two cacheable queries sharing a simple type name.

Because the key carries the query type, a command cannot name a whole key. `IInvalidatesCache` names the
owning query instead, through `CacheKeyRef.For<TQuery>(key)`, so a renamed query is a compile error at the
command rather than a silently-missed eviction.

### Scopes fail closed

`CacheScope.Tenant` is the zero value, so `default(CacheScope)` and both interface defaults land on the
fail-closed option. A `Tenant` query with no resolved tenant, or a `User` query with no authenticated caller,
skips the cache entirely, runs the handler and logs at Warning — it never falls back to a wider key. A
fallback would let every request whose tenant resolution failed share one entry, across tenants. Widening is
always an explicit `CacheScope.Global`.

A command's `Scope` must match the queries it invalidates, or it evicts a key those queries never wrote.

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
                        AuditingBehavior          (outer half: records Failed after rollback)
                        TransactionBehavior       (runs the rest inside IUnitOfWork.ExecuteInTransactionAsync)
                        AuditingCommitBehavior    (inner half: queues Succeeded on OnBeforeCommit)
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
| `IdempotencyBehavior` | reserves `IdempotencyKeyScope.Create(requestContext, key)` — never the raw key — then `CompleteAsync` (persists the serialized response, passing back the reservation token; a `false` result logs a `Warning` but still returns the response); a blank key short-circuits with `Error.Validation("idempotency.key_required")` | `ReleaseAsync` (never `CompleteAsync` — a failure must remain retryable) | `ReleaseAsync`, then rethrows |
| `TransactionBehavior` | runs the rest of the pipeline and the handler inside `IUnitOfWork.ExecuteInTransactionAsync` (outermost command; the strategy may replay it — handlers must be re-runnable; `OnCompleted` callbacks queued by a discarded attempt are dropped); saves, runs `OnBeforeCommit` callbacks, commits | rollback, nothing committed; a joined command's failure marks the outer transaction rollback-only | rollback, the exception propagates |
| `AuditingBehavior` (outer, outside the transaction) + `AuditingCommitBehavior` (inner, inside it) | inner half queues `RecordAsync(Outcome = Succeeded, AfterSnapshot)` on `OnBeforeCommit` (written in the business transaction, commits with it; written directly when no transaction is active) | outer half, after rollback: `RecordAsync(Outcome = Failed, ErrorCode = Error.Code)` on the writer's own connection; a `RecordAsync` failure propagates | outer half, after rollback: `Failed` with `ErrorCode` = the exception type's full name — including a failure of the commit itself; a `RecordAsync` failure here is logged (EventId 5130), the **original** exception rethrows |
| `CachingBehavior` (Query stage, `.Caching`) | runs the handler inside `GetOrSetAsync` (once per key across concurrent callers) and caches the response | returned to every waiting caller, never cached (`CacheFactoryContext.SkipCaching`) | propagates, nothing cached; the query policy's fail-safe may serve an expired entry instead |
| `CacheInvalidationBehavior` (Command stage, `.Caching`) | registers an `ICommandScope.OnCompleted` callback that evicts after commit (`RemoveAsync` per key, `RemoveByTagAsync` per tag); keys and tags are scoped and validated **before** the handler runs, so an invalid key fails the command instead of the post-commit callback | nothing registered | nothing registered |

A nested command (`ICommandScope.IsNested`) makes `IdempotencyBehavior` call `next()` directly. `TransactionBehavior`
joins an active transaction through `ExecuteInTransactionAsync` (so a nested failure marks it rollback-only)
and calls `next()` directly only when nested with no active transaction — only the **outermost** command in
a DI scope owns the idempotency key and commits. A nested audited command queues its `Succeeded` entry on the
outer transaction; if the outer command fails, that entry rolls back and no separate `Failed` entry is
written for the nested command.

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
  `Error.Conflict(IdempotencyErrorCodes.InProgress)` (`idempotency.in_progress`) without calling `next()`.
- A key already completed with the **same** fingerprint returns `Completed` with the stored response —
  deserialized and returned directly, replaying the original outcome, `next()` never called.
- A key that exists (in-flight or completed) against a **different** fingerprint returns
  `FingerprintMismatch` — `Error.Conflict(IdempotencyErrorCodes.KeyReused)` (`idempotency.key_reused`).
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

### Keys are reserved per tenant and caller (P-562 X3)

A client-chosen key is not a secret. When reservations were shared by every caller of a tenant, a caller who learned
another's key and sent the same body was handed that caller's stored response, server-generated data included. So
`IdempotencyBehavior` never passes the raw key: `IdempotencyKeyScope.Create(IRequestContext, key)` hands the store a
SHA-256 digest, 64 lowercase hex characters.

- **Scope fields, in order:** layout label `SharedKernel.Application.Behaviors.Idempotency.KeyScope.v1`, `TenantId`
  (`"D"`), `ActorKind` (numeric), `UserId`, `ClientId`, `ImpersonatorId`, the raw key. Each field is `0x00` when
  absent, else `0x01` + big-endian int32 UTF-16 length + UTF-16LE code units. The encoding is injective (every field
  delimits itself, absent differs from `""`, code units survive unpaired surrogates), so plain concatenation's
  separator-smuggling collisions cannot happen; SHA-256 makes digest collisions infeasible.
- **`SessionId` is excluded on purpose.** It changes on re-login; including it would turn a retry after a fresh sign-in
  into a second execution.
- **Fixed length** fits every store (`18`'s EF Core `key` column is 512; a raw key longer than that now works) and keeps
  the raw key and the caller's identifiers out of the store. The fingerprint check is unchanged.
- **Anonymous callers share one scope per tenant** (so do callers with a kind but no identifiers, e.g. `12.Security`'s
  `SystemUserContext` mapped with a null subject). Only the fingerprint separates them: the same key + same fingerprint
  replays another anonymous sender's response; a different fingerprint gets `idempotency.key_reused` (which still
  reveals the key is in use). Documented residual risk — mitigated by random keys and by not returning sender-only data
  from anonymous commands. Pinned by `Handle_AnonymousCallersOfOneTenant_*` tests.
- **`AddIdempotencyBehavior()` requires `IRequestContext` at `Build()`.** Falling back to anonymous would silently put
  every caller back into one shared scope. A host with no identity registers `AnonymousRequestContext.Instance` or a
  `SystemRequestContext` on purpose. (A service whose only registration is `06.Persistence`'s anonymous fallback passes
  the guard but gets no per-caller separation — register `AddSharedKernelRequestContext()`.)
- **The layout is a stored format.** Changing a field, the order or the encoding orphans every stored reservation (a
  retry spanning the deploy runs again). A new layout needs a new label and an operational note. A golden-vector test
  (`Handle_ScopedKey_MatchesThePublishedV1Layout`, value computed outside .NET) pins it.
- **Blank key → `Error.Validation(IdempotencyErrorCodes.KeyRequired)`** (`idempotency.key_required`, renamed from
  `idempotency.key_missing` to match `14.Presentation`). The three codes are the public constants of
  `SharedKernel.Application.Behaviors.Idempotency.IdempotencyErrorCodes` (`KeyRequired`, `InProgress`, `KeyReused`);
  clients and dashboards branch on them, so they never change. `00.Governance`'s `PresentationIdempotencyCodesTests`
  pins `KeyRequired` to `14.Presentation`'s `PresentationErrorCodes.IdempotencyKeyRequired`.

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
| `Context.IRequestContext` | `SharedKernel.Application.Abstractions` | `13.ServiceDefaults.Security`'s `AddSharedKernelRequestContext()` (over `12.Security`'s `IUserContext`/`ITenantProvider`); `06.Persistence` uses the same instance for audit columns, tenant filters and the ledger |
| `Transactions.IUnitOfWork` | `SharedKernel.Application.Abstractions` | `06.Persistence.EfCore`'s `EfUnitOfWork` implements it directly (no adapter) |
| `Idempotency.IRequestIdempotencyStore` | `SharedKernel.Application.Behaviors` | `18.Idempotency`, or a consumer-supplied store |
| `Auditing.IAuditTrailWriter` | `SharedKernel.Application.Abstractions` | `06.Persistence.EfCore.Auditing` implements it directly (no adapter) |

Each is a *minimal* interface — resolving identity, tenancy, timestamps and chain linkage is entirely the
implementation's job, never this domain's. Three of them moved into `SharedKernel.Application.Abstractions` in P-558
so that persistence can implement them without referencing MediatR; before that, each existed twice (here and in
`06.Persistence`) with bridge adapters in `13.ServiceDefaults.Persistence` — all deleted.

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

**That last sentence is a load-bearing contract, not a description.** The failure travels out of the factory in
a local captured by the factory closure, which is only correct because a skipped value is not handed to
concurrent waiters — were it broadcast, every waiter would come out holding a default value with no failure in
its own closure, and the behavior would return `Result.Success(default)` where a failure was correct.
`CacheFactoryContext`'s own documentation used to state the opposite (that the factory value always reaches
every concurrent caller); it was measured against a real FusionCache, corrected in
`02.Caching.Abstractions`, and is now pinned by `CachingBehaviorConcurrencyTests` against a real cache — one
test asserting the factory runs **once** on the success path, another asserting it runs **twice** on the skip
path and each caller gets its own failure. `SharedKernel.Testing`'s `FakeCacheService` was given the same
per-key gate and post-gate re-check for the same reason: the fake previously ran the factory on every
concurrent miss, which makes both paths look identical and cannot fail on a regression in either.

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
| Idempotency reservation scope (P-562 X3) | The behavior scopes the key: SHA-256 over tenant + actor kind + subject + client + impersonator + raw key, always hashed; `IRequestContext` required at `Build()` | Tenant-only scoping (replays another caller's response); passing structured scope to the store (breaks the published store contract and both `18` providers); hashing only past a length limit (two key shapes); including `SessionId` (a retry after re-login would re-execute); an anonymous fallback when no context is registered | Stored keys are opaque digests; reservations made by 1.0.0-alpha.0.1171 or earlier are never found again after the upgrade; anonymous callers of a tenant still share a scope guarded only by the fingerprint |
| AOT/trimming | Not a constraint (user ruling) | Reflection-free everywhere, at the cost of ceremony | Reflection used at the four documented sites, each cached once per closed type |
| Response construction on short-circuit | `FailureResponse.Create<TResponse>` — a cached, reflection-resolved `Failure(Error)` delegate | A second, parallel non-`Result` failure shape | Every short-circuiting behavior (Authorization, Validation, Idempotency) shares one mechanism |
| `ApplicationLoggingOptions` startup validation | `[Range]` DataAnnotation on `SlowRequestThreshold` + `.AddOptions<T>().ValidateDataAnnotations().ValidateOnStart()` (`Microsoft.Extensions.Options.DataAnnotations`) | `01.Core/SharedKernel.Configuration`'s `AddValidatedOptions<TOptions>`/`ISectionBoundOptions` | Adopting `SharedKernel.Configuration` would add a `01.Core` project reference this package doesn't otherwise need; `Microsoft.Extensions.Options.DataAnnotations` is a plain `Microsoft.Extensions.*` NuGet package, already within this package's allowed reference set. `ValidateOnStart()` registers an `IStartupValidator` a real host invokes at startup — an invalid threshold fails before the first request, not at `LoggingBehavior`'s first invocation |

---

## Cross-Domain Couplings

Changes here that silently break another layer. Check the right column before merging.

| If you change… | Also check |
| --- | --- |
| `Transactions.IUnitOfWork`'s shape or contract (retry, rollback-only, `OnBeforeCommit`) | `06.Persistence`'s `EfUnitOfWork`/`UnitOfWorkCoordinator`; both `FakeUnitOfWork`s (`16.Testing/SharedKernel.Persistence.Testing`, `Behaviors.Tests/Support`) |
| `Idempotency.IRequestIdempotencyStore`'s contract | `18.Idempotency`'s store implementations; `16.Testing`'s fake (both migrated onto the stateless `reservationToken`/`bool`-returning shape, same day as `SK.05.P544`) |
| `IdempotencyKeyScope`'s layout (fields, order, encoding, label) or its 64-character output | Every reservation already stored in `18.Idempotency`'s Redis/PostgreSQL stores becomes unreachable on deploy (write an operational note); `18`'s EF Core `key` column (512) must still fit; the golden-vector test; `16.Testing`'s harness self-tests assert the 64-hex shape |
| `IdempotencyErrorCodes`' values | `14.Presentation`'s `PresentationErrorCodes.IdempotencyKeyRequired` (pinned by `00.Governance`'s `PresentationIdempotencyCodesTests`); clients and dashboards that branch on the three codes |
| `Auditing.IAuditTrailWriter`/`AuditEntry`'s shape | `06.Persistence.EfCore.Auditing`'s writer; `16.Testing`'s fakes |
| `Context.IRequestContext`'s shape | `13.ServiceDefaults.Security`'s `SecurityRequestContext`; `06.Persistence` (actor, tenant, ledger identity); `16.Testing`'s `TestRequestContext` |
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
  and release-on-exception, plus per-caller scoping: two users of one tenant never replay each other; the same user
  replays (also from a new session); anonymous callers replay on the same body and conflict on another; service,
  system and user actors, tenants, clients and impersonators scope apart; the encoding's collision cases; the golden
  vector.
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
- [2026-09-21] P-558: `SharedKernel.Application.Abstractions` added (shared IRequestContext/IUnitOfWork/IAuditTrailWriter); TransactionBehavior runs the handler via ExecuteInTransactionAsync, auditing split in two halves, rollback-only joins (agent)
- [2026-09-24] P-562 X3 (owner-approved security fix): idempotency keys are reserved per tenant **and caller**. `IdempotencyBehavior` takes `IRequestContext` (breaking ctor change) and hands the store a SHA-256 digest of tenant, actor kind, subject, client, impersonator and raw key (internal `IdempotencyKeyScope`, layout v1); `AddIdempotencyBehavior()` requires `IRequestContext` at `Build()`; `idempotency.key_missing` → `idempotency.key_required`. Anonymous callers keep sharing one scope per tenant (documented residual risk). No `18.Idempotency` code change (64 chars fits the 512 `key` column); stored reservations from 1.0.0-alpha.0.1171 or earlier are not found after the upgrade. Republish `SharedKernel.Application.Behaviors` (agent)
