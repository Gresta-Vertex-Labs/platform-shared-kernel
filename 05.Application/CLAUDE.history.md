# 05.Application — History

> **This file is a historical archive, not a maintainer reference.** For current rules read
> [`CLAUDE.md`](CLAUDE.md). It holds two superseded brains, newest first:
>
> 1. **The P-544–P-562 brain** (2026-09-15 to 2026-09-24): four packages — `SharedKernel.Application`,
>    `SharedKernel.Application.Abstractions`, `SharedKernel.Application.Behaviors` and
>    `SharedKernel.Application.Behaviors.Caching` — composed with `AddSharedKernelApplicationBehaviors()…Build()`,
>    authorization declared with `IAuthorizeRequest`/`PermissionMatch`, idempotency codes on `IdempotencyErrorCodes`.
>    P-563 (2026-09-24) merged the behaviors into `SharedKernel.Application`, renamed the caching package to
>    `SharedKernel.Application.Caching`, replaced the builder with one `AddSharedKernelApplication(assembly, app => …)`
>    call checked at host start, moved authorization to `[RequirePermission]` on the use case, and moved the idempotency
>    codes to `01.Core`'s `ErrorCodes.Idempotency`. Its design record is [`docs/p563/design.md`](docs/p563/design.md).
> 2. **The pre-P-544 brain** (WO-035 through WO-080), below it, unedited.
>
> Every type these archives describe that no longer exists is gone, not deprecated.

---
# 05.Application — the P-544–P-562 brain (superseded by P-563)

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

---

# 05.Application — the pre-P-544 brain (WO-035 through WO-080)

> **This file is a historical archive, not a maintainer reference.** It is the `05.Application/CLAUDE.md`
> that existed immediately before the 2026-09-15 pre-publish redesign (root work item P-544, this
> domain's own `SK.05.P544` phase). Nothing had been published to a feed at that point, so the redesign
> was a breaking rewrite by explicit user ruling — see the current `CLAUDE.md`'s "What we removed and
> why" table for the short version.
>
> Every type this file describes that no longer exists in the current source is **gone, not deprecated**:
> `IFireAndForgetCommand`/`ChannelFireAndForgetDispatcher`/`FireAndForgetBackgroundConsumer`/
> `FireAndForgetGuardBehavior`/`FireAndForgetOptions`/`FireAndForgetDispatchContext`, `ResilienceBehavior`/
> `IRetryableRequest`, every `Stream*Behavior` (`StreamLoggingBehavior`, `StreamMetricsBehavior`,
> `StreamTracingBehavior`, `StreamValidationBehavior`, `StreamAuthorizationBehavior`),
> `MediatRDomainEventDispatcherOptions`/`ParallelDispatch`, `DualApprovalBehavior`/`IRequiresDualApproval`/
> `IDualApprovalStore`/`IAuthorizationContextIdentity`, and the old `IAuthorizationContext`/
> `IIdempotencyKeyStore`/`IIdempotencyResponseStore` seam shapes (replaced by `IRequestContext` and
> `IRequestIdempotencyStore`). Read this file only to understand what a work order (WO-035 through WO-080)
> historically did — never as a description of the code on disk today.
>
> For current, authoritative rules read [`CLAUDE.md`](CLAUDE.md). This file is kept, unedited from its
> pre-redesign state, purely so the reasoning behind since-removed capabilities (why fire-and-forget
> existed, why dual approval was shaped the way it was, the original seven/ten/eleven/twelve-step pipeline
> evolution) is not lost.

---

# 05.Application — Application Layer

## What This Domain Is

The MediatR-based CQRS plumbing layer. Every command, query, streaming query, domain-event handler, and cross-cutting pipeline concern (validation, logging, metrics, tracing, transactions, caching, cache invalidation, authorization, idempotency, resilience, dual-control/maker-checker approval, auditing) in a downstream microservice derives from the contracts defined here. This domain may reference `01.Core`, `02.Caching` (abstractions only), `03.Domain`, and `04.Contracts` — it must never reference `06.Persistence`, `07.Messaging`, `12.Security`, or any concrete infrastructure package.

> **Local-seam bridging is the load-bearing pattern in this domain.** `TransactionBehavior` (`IUnitOfWork`), `AuthorizationBehavior` (`IAuthorizationContext`), `IdempotentCommandBehavior` (`IIdempotencyKeyStore`), `DualApprovalBehavior` (`IDualApprovalStore`, plus `IAuthorizationContextIdentity` as an additive sibling capability on the existing `IAuthorizationContext` seam, shipped WO-058), and `AuditingBehavior` (`IAuditTrailWriter`, a local seam deliberately smaller than `06.Persistence.Abstractions`'s richer P-456 contract of the same name, shipped WO-071) each define a minimal interface owned by this package, never a direct reference to the "real" infrastructure (`06.Persistence`, `12.Security`, `07.Messaging` respectively). The consuming service bridges each local seam to its real implementation at the composition root. This is the same pattern applied **five** times, not five different patterns.
>
> **WO-071 status (2026-09-04): all six phases — Design/Scaffold/Core/Tests/Docs — `●` complete; Published in progress.** A new opt-in `AuditingBehavior<TRequest,TResponse>` gated by `IAuditableRequest<TResponse>` (a marker mirroring `ILoggableRequest<TResponse>`'s exact self-supplied-field shape — immediate `Action`/`ResourceType`/`ResourceId`/`BeforeSnapshot` properties plus a `GetAfterSnapshot(TResponse response)` method, opaque caller-pre-serialized strings only) shipped exactly per the locked design. Feeds `06.Persistence`'s append-only, hash-chained audit trail via a NEW local `IAuditTrailWriter`/`AuditEntry` seam — the fifth local-seam-bridging instance in this domain, deliberately smaller than the real `06.Persistence.Abstractions` contract of the same name (no `Id`/`ActorId`/`TenantId`/`OccurredOn`/`CorrelationId`/hash-chain fields — all resolved by the REAL writer, never by this package or the caller). When a command also implements `IRequiresDualApproval` (WO-058), `AuditingBehavior` links the two via a plain `is`-pattern check on `ApprovalKey` — zero coupling to `IDualApprovalStore` itself. Canonical pipeline order grows from eleven to **twelve** named slots, inserting `AuditingBehavior` as the new step 10 (between `IdempotentCommandBehavior` and `TransactionBehavior`, renumbered to 11; `CacheInvalidationBehavior` renumbered to 12) — positioned so the audit write completes BEFORE `TransactionBehavior`'s own commit executes ("just inside Transaction"), the temporal mirror-image of `CacheInvalidationBehavior`'s post-commit-only positioning.
>
> **Implementation-phase discovery (2026-09-04): the PHYSICAL DI registration order needed to realize "just inside Transaction" is the OPPOSITE of what the canonical step numbering (10, then 11) would naively suggest.** MediatR wraps `IPipelineBehavior<,>` instances so the *first-registered* behavior is outermost — its post-`next()` code runs *last*, after every later-registered (more-inner) behavior's post-`next()` code has already run. This was verified empirically (not merely reasoned about) during the Core phase: `AuditingBehavior` must be registered in `ApplicationBehaviorsBuilder.Build()` AFTER (physically inner to) `TransactionBehavior`, not before, for `IAuditTrailWriter.RecordAsync` to observably precede `IUnitOfWork.SaveChangesAsync`. **A second, independent finding surfaced by the same empirical method that same session: `CacheInvalidationBehavior`'s existing shipped registration order (`Transaction` before `CacheInvalidation`) did NOT actually achieve its own documented "evicts only after a confirmed commit" invariant** — a real DI-registered pipeline probe showed `ICacheService.RemoveAsync` firing BEFORE `IUnitOfWork.SaveChangesAsync`, the reverse of what `CacheInvalidationBehavior.cs`'s XML docs (WO-036) claim. This was a genuine, previously-undetected defect in already-shipped WO-036 code, discovered as a byproduct of P-458's temporal-order testing methodology, left unfixed at the time (out of P-458's own scope) and flagged for a follow-up work order.
>
> **WO-080 status (2026-09-04, same-day follow-up): fixed, all six phases contributed to (Core/Tests/Docs) `●` complete.** The flagged defect above is now closed: `CacheInvalidationBehavior<,>` is registered in `Build()` BEFORE (physically OUTER to) `TransactionBehavior<,>` — not after, as WO-036's original registration order had it — so its post-`next()` eviction call is observed strictly AFTER `TransactionBehavior`'s (and, inside that, `AuditingBehavior`'s) post-`next()` code has fully unwound. `AuditingBehavior`'s own registration AFTER `TransactionBehavior` (correct since WO-071 above) is untouched. Proven by a new real composed-pipeline test, `CacheInvalidationTransactionOrderingTests`, mirroring `AuditingTransactionOrderingTests`'s exact shape — and confirmed genuinely non-vacuous: the fix was temporarily reverted and the new tests were confirmed to FAIL against the pre-fix ordering before being restored. See "Pipeline composition (canonical order)" below for the corrected DI-registration-order-to-onion-order relationship, now stated explicitly for both inverted pairs (this was the root cause of the defect class surfacing twice). See `05.Application/state-map.md` for the full task breakdown (C-88, T-79, T-80, DO-31).
>
> **WO-058 status (2026-08-17): all six phases — Design/Scaffold/Core/Tests/Docs/Published — `●` complete.** `01.Core` shipped `Error.Forbidden(...)` (P-384/WO-059) — `ErrorType.Forbidden`/`Error.Forbidden(string code, string message)`, mirroring `BusinessRule`'s exact shape — unblocking `IAuthorizationContextIdentity`/`DualApprovalBehavior` (C-78), `AddDualApprovalBehavior()`'s two-dependency `Build()`-time guard (C-79), the pipeline-order insertion at canonical position 6 (C-80), and final build verification (C-81), all implemented and shipped exactly per the locked design — no `Error.Unauthorized` workaround was ever used. `DualApprovalBehavior`/`AddDualApprovalBehavior()` test coverage shipped 2026-08-14: `DualApprovalContractShapeTests`, `DualApprovalBehaviorTests` (all four required scenarios — no-approval short-circuit, distinct-identity pass-through, self-approval short-circuit for both `Result`/`Result<T>`, query-type exclusion — plus the shipped `InvalidOperationException` misconfiguration path), the two-dependency `Build()`-time guard tests, and a new eleven-step `PipelineOrderAssertion` regression test. `SharedKernel.Application.Behaviors/README.md` gained the eleven-slot pipeline table and a full maker-checker worked example (Docs, 2026-08-17). Published (2026-08-17): the consumer-verify harness gained a full end-to-end maker-checker proof (unapproved/self-approval both fail `ErrorType.Forbidden`, a distinct-approver-recorded dispatch succeeds) via NSubstitute doubles for `IAuthorizationContext`/`IDualApprovalStore` — a `16.Testing` in-memory `IDualApprovalStore` fake is also required by root Phase Backlog P-380's own acceptance criteria but remains out of this domain's jurisdiction to build and is still blocked (`16.Testing/state-map.md` `C-117`/`C-118`), so P-380 itself stays `◐` Dispatched at the root even though this domain's own scope is fully shipped. Both packages repacked at `1.1.0`; `SharedKernel.Application.Behaviors.Tests` 161/161 passing.
>
> **WO-036 status: all six phases `●` complete (Published closed 2026-08-17).** `TracingBehavior`, the streaming query vocabulary (`IStreamQuery<TResponse>`/`IStreamQueryHandler<,>`), `ResilienceBehavior`, `CacheInvalidationBehavior`, and the reusable pipeline test harness are all shipped, packed (`1.1.0`), and consumer-verified end to end (a retryable+idempotent command retries a transient failure then commits its transaction and invalidates its cache key; a traced query records exactly one `ActivitySource` span).
>
> **WO-038 status: all six phases `●` complete (Published closed 2026-08-17, shared with WO-036's Published rows).** P-231 (bug fixes), P-232 (contract evolution), P-233 (parallel dispatch + fire-and-forget), and P-234 (streaming pipeline behaviors) are all fully implemented and tested. The `AddSharedKernelApplication(configure)` parallel-dispatch overload is now also consumer-verified end to end through the real DI chain, and the fire-and-forget dispatch path is consumer-verified to actually execute a handler via the real `FireAndForgetBackgroundConsumer`.
>
> **WO-039 status: all six phases `●` complete (Published closed 2026-08-17).** A post-ship review found two confirmed defects in already-"Complete" WO-038 code, both now fixed: (1) `FailureResponseFactory`'s `ResultOfTDispatcher<TResponse>` reworked onto `01.Core` P-236's `IFailureFactory<TSelf>` — the interface-discovery/inner-type-unwrapping reflection (`GetInterfaces()`/`GetGenericArguments()`/`MakeGenericType()`/`GetMethod()`/`Invoke()`) is gone; one `MakeGenericMethod`+`CreateDelegate` call remains, disclosed as a second dispatch structurally identical to `MediatRDomainEventDispatcher`'s already-approved one (see "Constructing a generic failure response" below); (2) `FireAndForgetGuardBehavior<,>`'s self-blocking bug is fixed via an internal `AsyncLocal<bool>`-backed trusted-dispatch marker (`FireAndForgetDispatchContext`) — `AddFireAndForgetDispatch()` is now safe to adopt as documented. `MetricsBehavior<,>` now carries an `outcome` tag (P-239, via a new shared `ResponseOutcomeClassifier` also consumed by `LoggingBehavior`); opt-in idempotency response replay shipped (`IIdempotencyResponseStore`, P-242); `ApplicationBehaviorsBuilder.AddDefaultBehaviors()` onboarding preset shipped (P-243). **P-241 real-assembly wiring is now fully shipped, all 4 rule groups**: `ApplicationPipelineRules.BehaviorsNeverReferenceConcreteInfrastructure`/`.NoExistingBehaviorMatchesStreamRequestConstraint`/`.NoHandRolledRetryLoopOutsideResilienceBehavior`, `PipelineOrderAssertion.AssertRegistrationOrder`, `MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag`, and (as of 2026-07-06) `ReflectionGuardRules.NoMakeGenericMethodReflection` all pass against the real `SharedKernel.Application`/`SharedKernel.Application.Behaviors` assemblies. `00.Governance` P-240 shipped its `ReflectionExemptionRegistry` entry for `MediatRDomainEventDispatcher`, unblocking T-40/T-44; before marking them complete, this domain empirically verified (not merely inferred) that `SharedKernel.Application.Behaviors`' own `MakeGenericMethod` call site (`ResultOfTDispatcher<TResponse>.BuildFactory`) needs no second registry entry today — it is excluded from NetArchTest's real-assembly scan by the `.AreNotAbstract()` filter, since a C# `static class` compiles to IL `abstract sealed` (a second, independently-discovered vacuous-pass mechanism alongside the already-documented closure-type invisibility gap — see `00.Governance/CLAUDE.md`'s SK0012 notes). See `05.Application/state-map.md` for the full task breakdown.
>
> **WO-040 status: all six phases `●` complete (Published closed 2026-08-17).** A single, self-contained gap-closure capability: opt-in structured request/response payload logging via `ILoggableRequest<TResponse>` (`Logging/ILoggableRequest.cs`, `SharedKernel.Application.Behaviors`), extending `LoggingBehavior<,>` **in place** — no new pipeline slot, no change to the canonical pipeline order. Closes the gap `LoggingBehavior`'s own documentation has flagged as deliberately deferred since WO-035. Follows the same self-supplied-marker precedent already proven twice in this package (`ICacheableQuery<TResponse>.CacheKey`, `IInvalidatesCache.CacheKeysToInvalidate`) — the request instance supplies its own redacted/loggable field set for both request and response, never a reflection-based property walk. Zero new NuGet dependency (`ILogger.BeginScope`, BCL), zero new reflection. Now consumer-verified end to end: a request implementing `ILoggableRequest<TResponse>` logs both request- and response-side structured fields via `ILogger.BeginScope` through the real DI-registered pipeline.
>
> **WO-041 status: all six phases `●` complete (Published closed 2026-08-17) — a mechanical `[LoggerMessage]` authoring-style retrofit, not a behavioral or contract change.** A prior session's claim that "Design was locked" was stale/incorrect — direct inspection of `05.Application/state-map.md`'s task table found D-66..D-71 still genuinely `○` at the start of this session. Both cross-domain blockers were re-verified shipped (`01.Core` `SK.01.P249` 4/4 `●`; `00.Governance` `SK.00.LoggingStandardEnforcement` 11/11 `●`) and the retrofit was carried through in full. Every direct `ILogger` extension-method call and hand-written `LoggerMessage.Define<>()` delegate in `SharedKernel.Application.Behaviors` — `LoggingBehavior<,>` (four call sites), `FireAndForgetBackgroundConsumer` (one), `ChannelFireAndForgetDispatcher` (one), and `StreamLoggingBehavior<,>` (four, previously ad hoc `EventId(1..4, "Name")` outside any reserved range) — is now converted to the source-generated `[LoggerMessage]` partial-method pattern with explicit `EventId`s drawn from this domain's reserved block in `01.Core`'s `SharedKernel.Primitives.Logging.LoggingEventIdRanges` registry (P-249): `SharedKernel.Application` = `5000`-`5099` (currently unused — zero log call sites exist in that package), `SharedKernel.Application.Behaviors` = `5100`-`5199` (the second package in this domain's declaration order). See "Logging EventId Allocation" below for the full table. Message templates, levels, and named placeholders are unchanged; the `ILoggableRequest<TResponse>`-driven `BeginScope` opt-in redaction mechanism (WO-040, P-246) is confirmed preserved byte-for-byte (regression-tested, T-60). New `Shared/ApplicationBehaviorsLoggingEventIds.cs` (ten `const int` fields) added. A genuine, **pre-existing** (not introduced by this retrofit) latent quirk was discovered in `ChannelFireAndForgetDispatcher`'s real DI wiring: the `DropAndLog` policy's `BoundedChannelFullMode.DropOldest` channel construction makes `TryWrite` never return `false` on a full channel (it drops-and-succeeds instead), so the full-channel `LogChannelFull` warning is unreachable through the builder's exact wiring as shipped — flagged here as a candidate follow-up functional-bug work order, out of scope for this authoring-only retrofit; the T-62 test exercises the log call site directly via an overridden channel singleton rather than silently working around or fixing the underlying wiring. See `05.Application/state-map.md` for the full task breakdown.
>
> **Published (2026-08-17): both packages repacked at `1.1.0` — a single coordinated additive-only minor bump** folding every public-surface delta accumulated across WO-036/038/039/040/041/058 since the original `1.0.0` publish (mirrors the `SharedKernel.Primitives` `1.1.0` precedent). `dotnet pack` clean, zero warnings; no internal NuGet feed is configured in this environment, so no `dotnet nuget push` was attempted. The consumer-verify harness grew from 178 to 190 total tests across both packages (`SharedKernel.Application.Tests` 29/29, `SharedKernel.Application.Behaviors.Tests` 161/161) — every canonical pipeline slot this domain ships is now proven end to end through a real `ServiceCollection` + `AddMediatR` + `ApplicationBehaviorsBuilder` chain, never a hand-rolled `RequestHandlerDelegate<TResponse>` mock. All seven work orders (WO-035/036/038/039/040/041/058) are now `●` complete through Published — this domain's own scope is fully shipped end to end.

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
| `SharedKernel.Application.Behaviors` | `ValidationBehavior<,>`, `LoggingBehavior<,>`, `MetricsBehavior<,>` (+ `outcome` tag via `ResponseOutcomeClassifier`, WO-039), `TracingBehavior<,>`, `TransactionBehavior<,>` + `IUnitOfWork`, `CachingBehavior<,>` + `ICacheableQuery<TResponse>`, `CacheInvalidationBehavior<,>` + `IInvalidatesCache` + `InvalidateOnlyOnSuccess` opt-in flag (WO-038), `AuthorizationBehavior<,>` + `IAuthorizationContext` (multi-requirement, WO-038) / `IAuthorizeRequest` (AllOf/AnyOf, WO-038), `IdempotentCommandBehavior<,>` + idempotency seam/`IIdempotentRequest` + `IIdempotencyResponseStore` opt-in replay seam (WO-039), `ResilienceBehavior<,>` + `IRetryableRequest`, `FireAndForgetGuardBehavior<,>` + `IFireAndForgetDispatcher` + `FireAndForgetOptions` + `FireAndForgetBackgroundConsumer` + `FireAndForgetDispatchContext` trusted-dispatch marker (WO-038; self-blocking bug fixed WO-039 P-238), streaming behaviors: `StreamLoggingBehavior<,>`, `StreamMetricsBehavior<,>`, `StreamTracingBehavior<,>`, `StreamValidationBehavior<,>`, `StreamAuthorizationBehavior<,>` (WO-038, `IStreamPipelineBehavior<,>`), `ApplicationBehaviorsBuilder.AddDefaultBehaviors()` onboarding preset (WO-039), `ILoggableRequest<TResponse>` opt-in structured payload logging marker (WO-040, shipped), `DualApprovalBehavior<,>` + `IRequiresDualApproval` + `IDualApprovalStore` + `IAuthorizationContextIdentity` dual-control/maker-checker seam (WO-058, shipped), `AuditingBehavior<,>` + `IAuditableRequest<TResponse>` + `IAuditTrailWriter` + `AuditEntry` local audit-writer seam (WO-071, shipped) — opt-in MediatR pipeline behaviors (twelve unary + five streaming) | `SharedKernel.Application`, `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Caching.Abstractions`, `MediatR`, `FluentValidation`, Polly v8 resilience pipeline (see Technology Stack) |

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
          Handlers return Result<T> exclusively — never an HTTP or wire response shape. The HTTP boundary
          maps the Result to a response (RFC 9457 ProblemDetails on failure) via 14.Presentation's
          ResultHttpExtensions; 04.Contracts has no response-wrapper type.
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
          see the STRUCTURED PAYLOAD LOGGING note below for the opt-in mechanism (WO-040, shipped).
          SHARED CLASSIFICATION HELPER (WO-039, P-239, shipped): the IHasSuccessFlag pattern-match
          driving the Information/Warning decision above is delegated to Shared/ResponseOutcomeClassifier.cs
          (internal, IsSuccess<TResponse>/Classify<TResponse>), the same helper MetricsBehavior's outcome tag
          uses (see Metrics section below) — a pure refactor, zero behavior change, so the two behaviors'
          success/failure classification can never silently diverge.
          STRUCTURED PAYLOAD LOGGING (WO-040, P-246, shipped 2026-07-08 — see state-map.md C-66..C-68):
          when TRequest implements ILoggableRequest<TResponse>
          (below), the start log entry additionally opens an ILogger.BeginScope(request.LoggableRequestFields)
          scope (skipped when the returned dictionary is null/empty), and the completion log entry
          additionally opens ILogger.BeginScope(loggable.GetLoggableResponseFields(response)) — called ONLY
          when next() returns normally, never on a thrown exception (there is no response to project; the
          request-side scope still applies to the entry log and to the Error-level exception log).
          ILogger.BeginScope(IReadOnlyDictionary<string,object?>) is the same BCL-native structured-logging-
          scope convention ASP.NET Core's own HttpLoggingMiddleware uses — zero new NuGet dependency. This is
          a PURE ADDITIVE branch (`is ILoggableRequest<TResponse>` pattern-match) around the existing log
          calls — a TRequest that does not implement the marker produces byte-for-byte identical logging
          behavior to today, no new scope/tag/level/message-template change of any kind. Not an eleventh
          pipeline slot and not a second logging pathway — LoggingBehavior<,> remains the single place
          request/response payload data is ever written to a log, deliberately extended in place rather than
          duplicated.

ILoggableRequest<TResponse>  : IRequest<TResponse>   (WO-040, P-246, shipped 2026-07-08 — Logging/ILoggableRequest.cs)
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

**LOGGING AUTHORING RETROFIT (WO-041, P-253, shipped 2026-07-10):** `LoggingBehavior<TRequest,TResponse>`'s
four log statements (`Handling {RequestName}`, `Handled {RequestName} in {ElapsedMilliseconds}ms`, `Handled
{RequestName} with failure in {ElapsedMilliseconds}ms`, `Handling {RequestName} failed after
{ElapsedMilliseconds}ms`) move from direct `ILogger.LogInformation/LogWarning/LogError(...)` extension-method
calls to `[LoggerMessage]`-attributed static partial methods with explicit `EventId`s `5100`-`5103` (see
"Logging EventId Allocation" below) — the class gains the `partial` modifier (compatible with its existing
primary constructor and generic parameters). This is an authoring-mechanism change only: message templates,
named placeholders, log levels, the `ResponseOutcomeClassifier`-driven Information/Warning branch, and the
`ILoggableRequest<TResponse>` `BeginScope` opt-in mechanism above are all unchanged, byte-for-byte.

### Logging EventId Allocation (`WO-041`, P-253, shipped 2026-07-10)

```text
This domain's reserved EventId block (01.Core's SharedKernel.Primitives.Logging.LoggingEventIdRanges.Application,
P-249) is 5000-5999, sub-divided into one 100-wide sub-block per package in this domain's declaration order
(see Packages table above):

    SharedKernel.Application            5000-5099   (currently unused — zero ILogger call sites in this package)
    SharedKernel.Application.Behaviors  5100-5199   (eleven allocated EventIds as of the SK0030 fix below, three sub-ranges)

  Logging/LoggingBehavior.cs            5100-5109
    5100  LogHandling            Information  "Handling {RequestName}"
    5101  LogHandledSuccess      Information  "Handled {RequestName} in {ElapsedMilliseconds}ms"
    5102  LogHandledFailure      Warning      "Handled {RequestName} with failure in {ElapsedMilliseconds}ms"
    5103  LogHandlingFailed      Error        "Handling {RequestName} failed after {ElapsedMilliseconds}ms"

  FireAndForget/                        5110-5119
    5110  LogChannelFull         Warning      ChannelFireAndForgetDispatcher — "Fire-and-forget channel is
                                              full (capacity={Capacity}). Command {CommandType} was dropped
                                              and will not be executed."
    5111  LogCommandFaulted      Error        FireAndForgetBackgroundConsumer — "Fire-and-forget command
                                              {CommandType} faulted and its result was discarded."
    5112  LogCommandFailed       Warning      FireAndForgetBackgroundConsumer — "Fire-and-forget command
                                              {CommandType} completed with a failure result ({ErrorCode})
                                              and the result was discarded." (SK0030 real-source-audit fix,
                                              WO-049 P-299 candidate follow-up — closes the silent-failure
                                              observability gap for a handler-returned Result.Failure,
                                              distinct from the thrown-exception path logged at 5111)

  Streaming/StreamLoggingBehavior.cs    5120-5129   (renumbered off the pre-retrofit ad hoc EventId(1..4, "Name"),
                                                      which carried no reserved-range guarantee)
    5120  LogStreamStarted       Information  "Streaming {RequestName} started."
    5121  LogFirstItem           Debug        "Streaming {RequestName} produced first item in {ElapsedMilliseconds}ms."
    5122  LogStreamCompleted     Information  "Streaming {RequestName} completed in {ElapsedMilliseconds}ms."
    5123  LogStreamFaulted       Warning      "Streaming {RequestName} faulted after {ElapsedMilliseconds}ms."

Implemented as SharedKernel.Application.Behaviors/Shared/ApplicationBehaviorsLoggingEventIds.cs — eleven
const int fields (ten from the WO-041 retrofit plus 5112 from the SK0030 fix below), each computed as
LoggingEventIdRanges.Application + offset (never a bare numeric literal disconnected from the registry).
5104-5109, 5113-5119, and 5124-5129 remain unallocated headroom within each sub-range for future log
statements in the same file without renumbering anything already shipped.
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

IAuthorizationContextIdentity  (interface — WO-058, shipped; optional capability, NOT a modification to IAuthorizationContext)
    .GetCurrentIdentityAsync(CancellationToken ct)                              → Task<string>
    NOTE: An IAuthorizationContext implementation MAY additionally implement this interface to expose the
          identity of the currently-executing caller. Added as a NEW, SEPARATE interface rather than a
          breaking modification to the already-published (WO-035, v1.x) IAuthorizationContext contract —
          mirrors IIdempotencyResponseStore's exact precedent as a sibling capability layered onto
          IIdempotencyKeyStore (WO-039, P-242), not a fresh pattern. DualApprovalBehavior (see DualApproval
          section below) detects this via an `is IAuthorizationContextIdentity` pattern-match on the
          injected IAuthorizationContext — a standard .NET optional-capability-interface check, not
          reflection, the same class of check IdempotentCommandBehavior already uses for
          IIdempotencyResponseStore. AuthorizationBehavior itself never needs or uses this interface; it
          exists solely to support DualApprovalBehavior's self-approval prevention. The returned identity
          MUST be the SAME opaque identity string the composition-root bridge uses to identify the current
          approver when later recording an approval via IDualApprovalStore.RecordApprovalAsync — keeping
          identity representation consistent across both sides of the maker-checker check is the consuming
          service's responsibility; this package never interprets, normalizes, or compares identity strings
          beyond ordinal equality. IMPLEMENTATION DETAIL (shipped, not originally pinned down at Design time):
          when the registered IAuthorizationContext does NOT additionally implement this interface,
          DualApprovalBehavior throws InvalidOperationException — a composition-root misconfiguration, not a
          foreseeable business outcome, so it does not use the Result.Failure(Error.Forbidden(...)) channel.
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

#### Dual-Control / Maker-Checker Approval (`DualApproval/`) — WO-058, shipped 2026-08-14

> Maker-checker/four-eyes controls — one identity initiates a privileged action, a distinct second identity must
> approve it before it executes — are a baseline requirement (SOX, banking regulation, PCI-DSS) for high-value
> financial operations: large payment approval, credit-limit changes, signing-key rotation, production
> configuration changes. This is a pipeline-behavior authorization gate structurally identical to the already-
> shipped `AuthorizationBehavior`, not a `12.Security` capability — `12.Security`'s `IUserContext` already
> carries everything needed to identify an approver; no new `12.Security` type is required.
>
> **Cross-domain blocker cleared 2026-08-14:** `DualApprovalBehavior`'s short-circuit requires `Error.Forbidden(...)`
> (`01.Core/SharedKernel.Primitives.Errors.Error`/`ErrorType`) — `01.Core` shipped it as P-384/WO-059 (one new
> `ErrorType` enum member + one new static factory method, exactly mirroring how `BusinessRule` was added),
> verified by direct inspection of `01.Core/SharedKernel.Primitives/Errors/Error.cs`/`ErrorType.cs`;
> `SharedKernel.Primitives` was repacked `1.1.0`. `IRequiresDualApproval`, `IDualApprovalStore`,
> `IAuthorizationContextIdentity`, and `DualApprovalBehavior<TRequest,TResponse>` are all implemented and shipped
> against the real factory — `Error.Unauthorized(...)` was never substituted as an interim measure, per this
> section's original design instruction (`Unauthorized` means "not permitted to attempt this at all";
> dual-control's rejection means "permitted, but a second, distinct approver has not yet signed off on this
> specific instance" — a materially different, HTTP-403-shaped semantic).

```text
IRequiresDualApproval  (interface — marker)
    .ApprovalKey                                                    → string
    NOTE: Marker a COMMAND implements to require a second, distinct approving identity before its handler
          runs. Self-supplied, mirroring IIdempotentRequest.IdempotencyKey's exact pattern — the command
          instance alone computes its own key (e.g. a deterministic identifier tying this specific pending
          change to its approval record; often, but not required to be, the same value as the command's own
          IdempotencyKey if it also implements IIdempotentRequest). Never implemented by a query —
          IQuery<TResponse> never implements ICommandBase, and dual-control is a mutation-gating concern by
          definition, mirroring IIdempotentRequest's commands-only scope exactly.

IDualApprovalStore  (interface — local seam, owned by this package)
    .TryGetApprovalAsync(string approvalKey, CancellationToken ct)             → Task<string?>
    .RecordApprovalAsync(string approvalKey, string approverIdentity, CancellationToken ct)   → Task
    NOTE: Mirrors IIdempotencyKeyStore's bridge shape exactly — this package ships only the interface, no
          implementation; the consuming service provides one backed by whatever durable store it prefers
          (a dedicated approvals table, a distributed cache key, etc.) and registers it at the composition
          root. TryGetApprovalAsync returns the recorded approver's identity, or null if no approval has
          been recorded yet for this key — the identity itself (not just a bool) is required so
          DualApprovalBehavior can compare it against the current initiator's identity for self-approval
          prevention. RecordApprovalAsync is called ONLY by the consuming service's OWN separate
          approval-recording workflow (e.g. a distinct "ApproveChangeCommand" an approver dispatches
          through their own request) — DualApprovalBehavior itself NEVER calls RecordApprovalAsync; it only
          ever reads. Zero reference to 06.Persistence/07.Messaging/12.Security from this package — same
          layering discipline as every other local seam in this domain.

DualApprovalBehavior<TRequest, TResponse>  (sealed class, implements IPipelineBehavior<TRequest, TResponse>)
    where TRequest : ICommandBase, IRequiresDualApproval, IRequest<TResponse>
    NOTE: Constrained to ICommandBase ONLY — mirrors TransactionBehavior/IdempotentCommandBehavior's exact
          constraint shape; never applies to queries (a DI-level fact). Resolves the current caller's
          identity via the injected IAuthorizationContext's IAuthorizationContextIdentity capability (see
          Authorization section above), then calls
          IDualApprovalStore.TryGetApprovalAsync(request.ApprovalKey, ct):
            - No recorded approval (null) → short-circuits WITHOUT calling next(), returns
              Result.Failure(Error.Forbidden(...)) — "awaiting a second approver."
            - Recorded approval whose identity equals the resolved initiator identity (SELF-APPROVAL) →
              short-circuits WITHOUT calling next(), returns Result.Failure(Error.Forbidden(...)) — this
              check is evaluated unconditionally whenever a record exists, so self-approval can never slip
              through merely because a non-null record is present. STRUCTURALLY IMPOSSIBLE, not merely
              discouraged: there is no code path in this behavior that calls next() when the two identities
              match, regardless of how the record was created.
            - Recorded approval from a DISTINCT identity → calls next(), returns its result unchanged.
          NEVER throws for the awaiting-approval or self-approval cases — Result.Failure(Error.Forbidden(...))
          is a foreseeable, expected outcome, mirroring AuthorizationBehavior's never-throw contract; the one
          deliberate behavior short-circuit reserved for a thrown exception remains ValidationException.
          Does NOT clear, consume, or expire the approval record — record lifecycle (one-time-use
          invalidation, expiry, re-approval-on-command-change) is the consuming service's own
          approval-recording workflow's responsibility; this behavior only ever reads.
          Reuses Shared/FailureResponseFactory.cs for the generic TResponse failure construction — the same
          mechanism AuthorizationBehavior/IdempotentCommandBehavior already use, never a third
          implementation of "construct an arbitrary Result/Result<T> failure from an Error when TResponse is
          generic."
          Runs sixth in the canonical pipeline — see Pipeline Composition below.
```

#### Auditing (`Auditing/`) — WO-071, shipped

> Feeds `06.Persistence`'s append-only, hash-chained audit trail (`IAuditTrailWriter`/`AuditEntry`/`AuditRecord`/`IAuditActorContext`, `SharedKernel.Persistence.Abstractions`, P-456/P-457). This is the domain's **fifth** local-seam-bridging instance, following the exact `IUnitOfWork`/`IAuthorizationContext`/`IIdempotencyKeyStore`/`IDualApprovalStore` precedent: `05.Application`'s layering ceiling is `01–04`, which does NOT include `06.Persistence.Abstractions`, so `AuditingBehavior` can never reference the real, richer P-456 `IAuditTrailWriter`/`AuditEntry` directly. A NEW, deliberately smaller local seam of the SAME NAME (`IAuditTrailWriter`, `AuditEntry`) is defined in this package instead — the composition-root bridge, a consuming-service concern, maps the local shape onto the real one once `06.Persistence` ships its own implementation. Audit records are NEVER written automatically off `SaveChanges`/`AuditInterceptor` — always an explicit, opt-in act via `IAuditableRequest<TResponse>`, mirroring the platform-wide "consequential side effects are always explicit" convention and `06.Persistence`'s own already-declined "hidden audit write" design.

```text
IAuditableRequest<TResponse>  (interface — marker + self-supplied snapshot surface)
    .Action                                                          → string
    .ResourceType                                                    → string
    .ResourceId                                                      → string
    .BeforeSnapshot                                                  → string?
    .GetAfterSnapshot(TResponse response)                             → string?
    NOTE: Mirrors ILoggableRequest<TResponse>'s exact shape — Action/ResourceType/ResourceId/BeforeSnapshot are
          immediate properties, known at request-construction time; GetAfterSnapshot(response) is a method,
          invoked only after next() returns NORMALLY (never on a thrown exception — there is no response to
          project, same convention ILoggableRequest.GetLoggableResponseFields already established). All values
          are OPAQUE, caller-pre-serialized strings — AuditingBehavior/IAuditTrailWriter never parse or diff
          them, mirroring 06.Persistence's own P-456 "opaque snapshot" rule and IIdempotencyResponseStore's
          "store persists what it's handed" precedent. Never a reflection-based property walk over an
          arbitrary TRequest/TResponse.

IAuditTrailWriter  (interface — local seam, owned by this package, SAME NAME as the real 06.Persistence.Abstractions contract)
    .RecordAsync(AuditEntry entry, CancellationToken ct = default)   → Task
    NOTE: Deliberately smaller than 06.Persistence.Abstractions's IAuditTrailWriter (which returns
          Task<AuditRecord> and internally resolves actor/tenant identity, timestamp, and hash-chain linkage)
          — this local seam never needs those; the composition-root bridge adapter maps AuditEntry (below)
          onto the real, richer contract. Same-name-different-namespace precedent, mirroring IUnitOfWork's
          existing bridge shape exactly — never a compiled reference to 06.Persistence.

AuditEntry  (sealed record — local seam, owned by this package)
    .Action                                                          → string
    .ResourceType                                                    → string
    .ResourceId                                                      → string
    .BeforeSnapshot                                                  → string?
    .AfterSnapshot                                                   → string?
    .ApprovalId                                                      → string?
    NOTE: Deliberately omits Id/ActorId/TenantId/OccurredOn/CorrelationId/RecordHash/PreviousRecordHash —
          every one of those is resolved by the REAL 06.Persistence writer implementation (P-457), never by
          this package or by the caller. ApprovalId is populated by AuditingBehavior itself, not by the
          request (see below) — it is the ONE field this local entry carries beyond what IAuditableRequest
          supplies directly.

AuditingBehavior<TRequest, TResponse>  (sealed class, implements IPipelineBehavior<TRequest, TResponse>)
    where TRequest : ICommandBase, IAuditableRequest<TResponse>, IRequest<TResponse>
    NOTE: Constrained to ICommandBase ONLY — mirrors TransactionBehavior/IdempotentCommandBehavior/
          DualApprovalBehavior's exact constraint shape; a query type must never satisfy
          IAuditableRequest<TResponse>'s applicability (a DI-level fact, no runtime `is`-check needed — the
          .NET DI container's generic-constraint-aware resolution naturally excludes any closed TRequest that
          does not satisfy the constraint, the same mechanism every other commands-only behavior here relies
          on). Calls next() FIRST to obtain the response, then unconditionally calls
          IAuditTrailWriter.RecordAsync(...) for BOTH a Result.Success AND a Result.Failure outcome —
          auditing a rejected high-risk attempt is itself often the compliance-relevant event, not just a
          successful one — but NEVER when next() throws (mirrors ILoggableRequest<TResponse>'s established
          convention: there is no response object to project). DOES NOT catch an exception thrown by
          IAuditTrailWriter.RecordAsync itself — it propagates unchanged and blocks the rest of the pipeline
          (FAILS CLOSED), consistent with this domain's "log/audit failures are never silently swallowed"
          convention; a failed audit write means TransactionBehavior (which wraps this behavior — see
          Pipeline Composition below) never reaches its own commit either.
          DUAL-APPROVAL LINKAGE: detects `request is IRequiresDualApproval dual` via a plain `is`-pattern
          (zero coupling to IDualApprovalStore itself, zero new project reference) and passes
          `dual.ApprovalKey` as AuditEntry.ApprovalId; a request implementing only IAuditableRequest<TResponse>
          (no dual-approval) passes ApprovalId = null.
          AUDIT-WRITE / BUSINESS-COMMIT DECOUPLING (documented, disclosed, not an oversight): since
          06.Persistence's real EfAuditTrailWriter (P-457) is SELF-CONTAINED and calls its own
          SaveChangesAsync independently of the caller's business IUnitOfWork, the recorded audit entry
          reflects the outcome the handler COMPUTED, not a guarantee the business mutation itself later
          persisted — matching 06.Persistence's own already-locked design decision, not a gap introduced here.
          Runs TENTH in the canonical pipeline, immediately inside TransactionBehavior — see Pipeline
          Composition below.
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
          LOGGING RETROFIT (WO-041, P-253, shipped 2026-07-10): the implementation,
          ChannelFireAndForgetDispatcher, logs a Warning ("channel is full, command dropped") when
          DropAndLog rejects an enqueue attempt — this is now a [LoggerMessage]-attributed static
          partial method (EventId 5110 — see "Logging EventId Allocation" above); the class carries
          partial; message text/level unchanged.

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
          LOGGING RETROFIT (WO-041, P-253, shipped 2026-07-10): the Error-level fault log is now a
          [LoggerMessage]-attributed static partial method (EventId 5111 — see "Logging EventId Allocation"
          above); the class carries partial; message text/level unchanged.
          FAILURE-OBSERVABILITY FIX (SK0030, WO-049 P-299 candidate follow-up, shipped): the returned
          Result is now captured (was previously a bare-statement-discarded `await sender.Send(...)`,
          the exact SK0030 ResultOutcomeDiscardedAnalyzer fire condition). When result.IsFailure, a new
          [LoggerMessage]-attributed static partial method logs at Warning (EventId 5112) with the
          command's type name and result.Error.Code, then the result is still discarded — the
          fire-and-forget contract is unchanged, only the silent-failure observability gap is closed.
          This is deliberately distinct from the EventId-5111 Error-level fault log: a thrown exception
          is an unanticipated fault, while Result.Failure is an expected, foreseeable outcome the
          handler evaluated and returned normally. The existing exception path (catch (Exception ex) →
          LogCommandFaulted) is completely unchanged by this fix.

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
          LOGGING RETROFIT (WO-041, P-253, shipped 2026-07-10): the four previously hand-written
          LoggerMessage.Define<>() static delegate fields (formerly ad hoc EventId(1..4, "Name"), outside
          any reserved range) have been replaced with four [LoggerMessage]-attributed static partial
          methods carrying the identical message templates/levels but renumbered into this domain's
          reserved range: EventId 5120 (StreamStarted), 5121 (LogFirstItem), 5122 (StreamCompleted), 5123
          (StreamFaulted) — see "Logging EventId Allocation" above. The class carries partial.

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
    .AddDualApprovalBehavior()  — WO-058, shipped 2026-08-14 (see DualApproval section above)
        — This domain's FIRST TWO-dependency Build()-time guard: throws InvalidOperationException naming
          IAuthorizationContext if it is not registered, and a distinct InvalidOperationException naming
          IDualApprovalStore if that is not registered — both are required for DualApprovalBehavior to
          function (identity resolution and approval-record lookup respectively).
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

> **WO-036 and WO-038 Core phases complete (C-18..C-50 ●).** The ten-named-slot unary order was fully implemented and shipped. **WO-058 (shipped 2026-08-14) revised this to ELEVEN named slots — `DualApprovalBehavior` inserted as step 6.** **WO-071 (shipped 2026-09-04) revises this further to TWELVE named slots — `AuditingBehavior` inserted as the new step 10, between `IdempotentCommandBehavior` (unchanged at step 9) and `TransactionBehavior` (renumbered from 10 to 11); `CacheInvalidationBehavior` renumbered from 11 to 12.** WO-038 also adds `FireAndForgetGuardBehavior<,>` (for fire-and-forget dispatch) and five `IStreamPipelineBehavior<,>` streaming behaviors — neither alters the canonical unary order. **WO-080/P-488 (shipped 2026-09-04) fixed a confirmed defect in the physical `Build()` registration order:** step 12 (CacheInvalidation) had been physically registered AFTER step 11 (Transaction) — i.e. physically INNER to it — which made eviction run BEFORE the commit, the exact opposite of this section's own step-12 rationale. The registration order is now corrected so CacheInvalidation is physically registered BEFORE Transaction (OUTER to it); see the next paragraph and step 12's rationale below, both updated to describe the corrected, shipped behavior.
>
> **The step list below is the TEMPORAL execution order, not necessarily the literal `ApplicationBehaviorsBuilder.Build()` registration-list order.** MediatR wraps `IPipelineBehavior<,>` instances so the first-registered behavior is outermost — its post-`next()` code runs LAST, after every later-registered (more-inner) behavior's post-`next()` code has already run. Two pairs of behaviors deliberately invert relative to the canonical numbering above, because each one's meaningful side effect happens AFTER `next()` returns:
> - Step 10 (Auditing) vs. step 11 (Transaction): the audit write must complete BEFORE the commit, so `AuditingBehavior` is physically registered AFTER `TransactionBehavior` — i.e. physically INNER to it — in `Build()`'s implementation (verified empirically, `state-map.md` T-77, `AuditingTransactionOrderingTests`).
> - Step 11 (Transaction) vs. step 12 (CacheInvalidation): eviction must observably follow the commit, so `CacheInvalidationBehavior` is physically registered BEFORE `TransactionBehavior` — i.e. physically OUTER to it — in `Build()`'s implementation (verified empirically, `CacheInvalidationTransactionOrderingTests`, WO-080/P-488).
>
> Never assume registration-list order equals execution order for a behavior whose meaningful side effect happens after `next()` returns — verify empirically via a real composed-pipeline dispatch test, the same way both pairs above were proven.

```text
1.  LoggingBehavior            ← outermost; logs the full pipeline, including validation/auth/approval/audit/resilience failures
2.  MetricsBehavior             ← records sharedkernel.application.request.duration regardless of outcome
3.  TracingBehavior             ← WO-036; starts/disposes the request-traversal Activity regardless of outcome
4.  ValidationBehavior          ← throws ValidationException before any handler, auth, approval, cache, or retry work happens
5.  AuthorizationBehavior       ← TRequest : IAuthorizeRequest (commands AND queries); rejects before approval/cache/mutation
6.  DualApprovalBehavior        ← WO-058, shipped; commands only (TRequest : ICommandBase, IRequiresDualApproval); rejects before cache/mutation
7.  CachingBehavior              ← queries only (TRequest : ICacheableQuery<TResponse>)
8.  ResilienceBehavior          ← WO-036; commands only in practice (TRequest : IRetryableRequest); wraps Idempotency+Auditing+Transaction
9.  IdempotentCommandBehavior   ← commands only (TRequest : ICommandBase, IIdempotentRequest)
10. AuditingBehavior            ← WO-071, shipped; commands only (TRequest : ICommandBase, IAuditableRequest<TResponse>); writes just inside Transaction, before its commit
11. TransactionBehavior         ← commands only (TRequest : ICommandBase); wraps handler + commit
12. CacheInvalidationBehavior   ← WO-036; commands only (TRequest : ICommandBase, IInvalidatesCache); innermost — after commit
```

**Positional rationale, step by step:**

1. **Logging outermost** — every request, including ones that fail validation, authorization, dual-approval, or exhaust all resilience retries, must produce a start/end log line; logging must never be skipped by an inner short-circuit.
2. **Metrics second** — the duration histogram must capture the full pipeline cost (validation, auth, approval, cache, retries, commit, invalidation), not just the handler; recorded via try/finally regardless of how the request terminates.
3. **Tracing third (WO-036)** — immediately adjacent to Metrics because both occupy the same "measure/observe the whole pipeline" band; positioned just inside Metrics so the trace span and the duration measurement bracket nearly the same scope, while Logging remains the true outermost layer (a span failing to start must still be logged). A distinct behavior from Metrics, not folded into it — one measures, one traces; single responsibility.
4. **Validation fourth** — malformed input is rejected before spending a permission check, an approval-record lookup, a cache lookup, a retry budget, an idempotency-store round trip, an audit write, or a transaction — the cheapest, most foundational rejection happens first.
5. **Authorization fifth** — runs after validation (don't spend a permission check on garbage input) and before everything that follows: never let an unauthorized request reach a dual-approval lookup (step 6), a cache lookup (step 7), consume a retry budget (step 8), an idempotency check (step 9), an audit write (step 10), a mutation (step 11), or an invalidation (step 12).
6. **DualApproval sixth (WO-058, shipped)** — commands only; runs immediately after Authorization and before everything mutation-adjacent that follows: never let an unapproved command reach a retry budget (step 8), an idempotency check (step 9), an audit write (step 10), a mutation (step 11), or an invalidation (step 12). Distinct from Authorization: Authorization answers "is this identity permitted to attempt this kind of action at all" (a static permission/policy question); DualApproval answers "has a second, distinct identity signed off on this exact pending instance of the action" (a per-instance maker-checker gate). The two are orthogonal and independently opt-in — a command may implement one, the other, both, or neither. Positioned before Caching/Resilience/Idempotency/Auditing/Transaction/CacheInvalidation for the same reason Authorization is: an unapproved command must never touch a retry budget, an idempotency-store round trip, an audit write, a mutation, or a cache-invalidation call.
7. **Caching seventh** — queries only; applies after authorization so an unauthorized query never populates or reads the cache. Mutually exclusive with step 6 and steps 8–12 at the request-type level (`ICacheableQuery<TResponse>` is never implemented by a command, and `ICommandBase` is never implemented by `IQuery<TResponse>`) — DualApproval (step 6, commands-only) and Caching (step 7, queries-only) join the same mutual-exclusion partition as {8, 9, 10, 11, 12}.
8. **Resilience eighth (WO-036)** — commands only in practice (queries that want retry use `IRetryableRequest` too, but queries already exited the command-only band at step 7's mutual exclusion; a retryable *query* would sit here in place of step 7 if it also needs caching+retry composition, an edge case not yet exercised). Positioned to WRAP Idempotency (step 9), Auditing (step 10), and Transaction (step 11) — this is the resolution to the retry-after-partial-commit hazard: a retry re-runs the FULL duplicate-check-then-audit-then-commit unit on each attempt, never a bare second commit attempt that bypasses idempotency detection or double-records an audit entry. Positioned after Authorization/DualApproval (don't burn a retry budget on an unauthorized or unapproved request) and after Caching (a cache hit never needs to retry anything).
9. **Idempotency ninth** — commands only; runs immediately before the audit-and-commit boundary so a duplicate submission is detected and rejected before an audit entry is recorded a second time or `TransactionBehavior`'s `SaveChangesAsync` can run a second time for the same logical operation. Sits *inside* Resilience precisely so that a Polly-driven retry attempt re-checks idempotency on every attempt rather than skipping the check after the first.
10. **Auditing tenth (WO-071, shipped)** — commands only (`TRequest : ICommandBase, IAuditableRequest<TResponse>`); runs immediately after Idempotency and immediately before Transaction, so the audit record captures the exact response the transaction is about to commit — "just inside Transaction, after the result is known but before/alongside commit." This is the temporal MIRROR-IMAGE of how CacheInvalidation (step 12) is positioned to run strictly AFTER a confirmed commit: Auditing's write must complete BEFORE Transaction's own commit executes, realized by registering `AuditingBehavior` CLOSER to the actual handler than `TransactionBehavior` in the real DI chain (the same inverted-registration-order technique already required to make `CacheInvalidationBehavior`'s post-commit-only execution work, applied in the opposite temporal direction). Records both a `Result.Success` and a `Result.Failure` outcome — never only success — but never on a thrown exception. If `IAuditTrailWriter.RecordAsync` itself throws, the exception propagates and blocks Transaction's commit (fails closed) — see the Auditing Interface Contracts section above for the full rationale, including the disclosed audit-write/business-commit decoupling property inherited from `06.Persistence`'s own design.
11. **Transaction eleventh** — wraps the handler call and the commit; by the time a command reaches this step it has already passed validation, authorization, dual-approval (if applicable), (if applicable) resilience retry composition, the idempotency check, and (if applicable) the audit write, so `SaveChangesAsync` only ever runs for a genuinely new, authorized, approved, validated, non-duplicate, audited command.
12. **CacheInvalidation twelfth, LAST TEMPORALLY (WO-036; registration order fixed WO-080/P-488)** — its eviction call runs only after `TransactionBehavior` returns without throwing, i.e. only after a CONFIRMED commit; evicting before commit risks invalidating a cache entry for a mutation that ultimately rolled back. This is the last thing that runs in the entire request pipeline — nothing observable happens after it. **Do not read "twelfth"/"last" as "physically registered last" or "DI-innermost"** — the opposite is true: `CacheInvalidationBehavior` is physically registered BEFORE `TransactionBehavior` (onion-OUTER to it), which is precisely what makes its post-`next()` eviction code run LAST, after `TransactionBehavior`'s (and, inside that, `AuditingBehavior`'s) post-`next()` code has already completed. See the "DI-registration-order-to-onion-order relationship" note above step 1 of this list.

Steps {6, 8, 9, 10, 11, 12} (commands-only) and step 7 (queries-only) are mutually exclusive at the request-type level (`ICacheableQuery<TResponse>` vs. `ICommandBase`), so a single request only ever actually passes through one of {7} or {6, 8, 9, 10, 11, 12} — the full twelve-step order is documented for completeness across the whole request universe, not because any one request traverses all twelve. Within {6, 8, 9, 10, 11, 12}, each sub-step (other than the always-on-for-any-`ICommandBase` Transaction) is itself independently opt-in via its own marker (`IRequiresDualApproval`, `IRetryableRequest`, `IIdempotentRequest`, `IAuditableRequest<TResponse>`, `IInvalidatesCache`) — a command implementing none of them still passes through Transaction alone.

### Constructing a generic failure response (`AuthorizationBehavior`/`IdempotentCommandBehavior`)

Both `AuthorizationBehavior<TRequest,TResponse>` and `IdempotentCommandBehavior<TRequest,TResponse>` must short-circuit with a failed response of type `TResponse`, where `TResponse` is statically only known to be `IRequest<TResponse>`'s response type — at runtime it is either the non-generic `Result` (struct) or a closed `Result<T>` (sealed class).

**Current shipped implementation (as of WO-039 P-237, Core phase complete 2026-07-06):** `Shared/FailureResponseFactory.cs` (`internal static class FailureResponseFactory`) solves this with `TResponse Create<TResponse>(Error error)`. It special-cases `TResponse == typeof(Result)` directly (zero reflection, a straight cast). For the `Result<T>` case, it delegates to `ResultOfTDispatcher<TResponse>`, which dispatches through `01.Core`'s `IFailureFactory<TSelf>` (`static abstract TSelf Failure(Error error)`, `where TSelf : IFailureFactory<TSelf>`) — `Result<T>` implements `IFailureFactory<Result<T>>` via its pre-existing `Failure(Error)` static factory (no new member); the non-generic `Result` struct deliberately does not implement it. `IFailureFactory<TSelf>`'s self-referential (CRTP) shape means `TResponse` itself is the type to invoke — unlike the superseded `IResultOfT<T>` approach, no inner value type needs to be located, so `Type.GetInterfaces()`, `Type.GetGenericArguments()`, `Type.MakeGenericType()`, `Type.GetMethod()`, and `MethodBase.Invoke()` are all gone from `BuildFactory()`.

**The one remaining, irreducible constraint:** a C# `static abstract` interface member can only be invoked through a generic type parameter *itself* constrained to that interface at the invoking method. `FailureResponseFactory.Create<TResponse>` is called by `AuthorizationBehavior<TRequest,TResponse>`/`IdempotentCommandBehavior<TRequest,TResponse>` with `TResponse` deliberately unconstrained (it may be the non-generic `Result`, which does not implement `IFailureFactory<TSelf>`) — those two behaviors keep their current generic shape unchanged (a hard requirement of this phase), so the constraint cannot be propagated to the call site without either reflection or a call-chain restructure that was explicitly out of scope. `ResultOfTDispatcher<TResponse>` resolves this the same way `MediatRDomainEventDispatcher` resolves the structurally identical "invoke by runtime-only-known closed type through a generic API" problem: a single `MethodInfo.MakeGenericMethod` call, performed once per closed `TResponse` type (cached forever in a static field — the CLR's per-closed-generic-type instantiation guarantee, not a `ConcurrentDictionary`, though the shape is otherwise identical), bound to a real delegate via `MethodInfo.CreateDelegate` so every call thereafter is a direct delegate invocation, never a per-call `MethodBase.Invoke()`. The `MethodInfo` for the constrained bridge method is captured at type-load time via a typed delegate instantiation over a private witness type (never a string-based `Type.GetMethod(name)` lookup), mirroring `MediatRDomainEventDispatcher`'s own compile-time method-reference capture (WO-038, P-231).

This makes `ResultOfTDispatcher<TResponse>` the domain's **second** `MakeGenericMethod`-based dispatch, structurally identical in shape and safety to `MediatRDomainEventDispatcher`'s already-approved one (build-once-per-Type, delegate-cached, never a per-call `Invoke`) — not a new class of hidden reflection. P-237's acceptance criterion was to eliminate `GetInterfaces`/`GetGenericArguments`/`MakeGenericType`/`GetMethod`/`Invoke`, all of which are gone; the `MakeGenericMethod`/`CreateDelegate` pair is retained because full elimination would require restructuring `AuthorizationBehavior`/`IdempotentCommandBehavior`'s own generic signatures, which this phase's design (D-37) explicitly ruled out of scope. This is flagged here as a disclosed, deliberate trade-off, not a silently-reintroduced defect — a future phase may revisit whether `00.Governance`'s `ReflectionExemptionRegistry` (P-240) should carry a second entry for this call site alongside `MediatRDomainEventDispatcher`'s.

Do not replace the current implementation with `dynamic` or raw string-based reflection lookups as a "quick fix" — that is exactly the class of hidden regression P-237 exists to close for good. Do not duplicate this pattern elsewhere without updating this note — `FailureResponseFactory` is the single chosen answer to "construct an arbitrary `Result`/`Result<T>` failure from an `Error` when `TResponse` is generic," reused by both behaviors.

### Hard violations (never do these)

- Returning an HTTP or wire response shape (`IResult`, `ProblemDetails`, a success/error wrapper DTO) from any command/query handler — handlers return `Result`/`Result<T>` exclusively; the endpoint maps it through `14.Presentation`'s `ResultHttpExtensions`.
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
- **(WO-080, P-488, fixed 2026-09-04)** Registering `CacheInvalidationBehavior` in `ApplicationBehaviorsBuilder.Build()` AFTER (physically inner to) `TransactionBehavior` — this is exactly the confirmed defect this phase fixed: doing so makes eviction run BEFORE the commit, contradicting the previous bullet's invariant despite that invariant already being documented at the time. `CacheInvalidationBehavior` must always be registered BEFORE (physically outer to) `TransactionBehavior`; the fix is proven by a real composed-pipeline test (`CacheInvalidationTransactionOrderingTests`), not merely by inspection, and is mechanically locked by `00.Governance` (P-489).
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
- **(WO-041, P-253)** A direct `ILogger.LogInformation/LogWarning/LogError/LogDebug/LogTrace/LogCritical(...)` extension-method call, `ILogger.Log(...)` interface call, or a hand-written `LoggerMessage.Define<>()` static delegate anywhere in `SharedKernel.Application.Behaviors` production source — every log statement in this package must be authored via the `[LoggerMessage]` source-generated partial-method pattern, matching the platform-wide root `CLAUDE.md` Logging Conventions mandate (WO-041) and mechanically enforced by `00.Governance`'s SK0020/SK0021 (P-250).
- **(WO-041, P-253)** An `EventId` literal disconnected from `SharedKernel.Primitives.Logging.LoggingEventIdRanges.Application` (this domain's reserved `5000`-`5999` block, sub-divided `5000`-`5099`/`5100`-`5199` per package) — every `[LoggerMessage(EventId = ...)]` value in this domain must be expressed as `ApplicationBehaviorsLoggingEventIds.SomeField` (itself computed from the registry), never an ad hoc numeric literal in the style of `StreamLoggingBehavior`'s pre-retrofit `EventId(1, "StreamStarted")`.
- **(WO-041, P-253)** Changing `LoggingBehavior<,>`'s message templates, log levels, the `ResponseOutcomeClassifier`-driven Information/Warning branch, or the `ILoggableRequest<TResponse>`-driven `BeginScope` opt-in mechanism (WO-040, P-246) under cover of "just doing the `[LoggerMessage]` retrofit" — this phase is an authoring-mechanism change only; any behavioral change must be its own, separately-justified phase.
- **(WO-058, shipped)** Constraining `DualApprovalBehavior` to anything other than `ICommandBase, IRequiresDualApproval` — dual-control is commands-only, mirroring `TransactionBehavior`/`IdempotentCommandBehavior`'s exact constraint shape; a query type must never satisfy `IRequiresDualApproval`'s applicability.
- **(WO-058, shipped)** `DualApprovalBehavior` calling `next()` without first confirming BOTH "an approval record exists" AND "the recorded approver's identity differs from the resolved initiator's identity" — self-approval must be structurally impossible, not merely discouraged; short-circuit both conditions with `Result.Failure(Error.Forbidden(...))`, never throw.
- **(WO-058, shipped)** Modifying the already-published, shipped `IAuthorizationContext` interface directly to add identity resolution — use the new sibling `IAuthorizationContextIdentity` optional-capability interface instead (the `IIdempotencyResponseStore` precedent), so existing `IAuthorizationContext` implementations in downstream services do not break.
- **(WO-058, shipped)** Substituting `Error.Unauthorized(...)` for `Error.Forbidden(...)` in `DualApprovalBehavior` "until `01.Core` ships the real factory" — wait for the actual `01.Core` phase to land; do not silently repurpose a semantically different `ErrorType`, and do not ship a version that would need a later behavioral rename.
- **(WO-058, shipped)** `IDualApprovalStore.RecordApprovalAsync` being called from within `DualApprovalBehavior` itself — this behavior only ever reads approval state; recording an approval is the consuming service's own separate approval-workflow responsibility (a distinct command/admin action the approver dispatches).
- **(WO-071, shipped)** Referencing `SharedKernel.Persistence.Abstractions.IAuditTrailWriter`/`AuditEntry`/`AuditRecord`/`IAuditActorContext` directly from `AuditingBehavior` or anywhere else in this domain — `05.Application`'s layering ceiling is `01–04`; `06.Persistence` is never referenced. Use the local `IAuditTrailWriter`/`AuditEntry` seam; the consuming service bridges it to the real `06.Persistence` implementation at the composition root.
- **(WO-071, shipped)** Constraining `AuditingBehavior<TRequest,TResponse>` to anything other than `ICommandBase, IAuditableRequest<TResponse>` — auditing is commands-only, mirroring `TransactionBehavior`/`IdempotentCommandBehavior`/`DualApprovalBehavior`'s exact constraint shape; a query type must never satisfy `IAuditableRequest<TResponse>`'s applicability.
- **(WO-071, shipped)** Feeding the audit trail automatically off `SaveChanges`/the existing `AuditInterceptor`, or writing an audit record from any code path other than the explicit `IAuditableRequest<TResponse>` opt-in — a consequential side effect this platform treats as always explicit, never a hidden effect of persistence machinery (mirrors `06.Persistence`'s own already-declined "hidden audit write" design for the same capability).
- **(WO-071, shipped)** `AuditingBehavior` catching and swallowing an exception thrown by `IAuditTrailWriter.RecordAsync` — a failed audit write must propagate and block `TransactionBehavior`'s commit (fail closed), never be logged-and-ignored the way an optional/best-effort side effect would be.
- **(WO-071, shipped)** `AuditingBehavior` calling `IAuditTrailWriter.RecordAsync` only on a `Result.Success` outcome — a business-rejected (`Result.Failure`) high-risk attempt must be recorded too; only a thrown exception (no response to project) skips the audit write.
- **(WO-071, shipped)** `AuditingBehavior` calling `IDualApprovalStore` directly to resolve the linked approval — the dual-approval linkage is a plain `is IRequiresDualApproval` pattern check on the request's own `ApprovalKey`, never a second dependency on the `IDualApprovalStore` seam.
- **(WO-071, shipped)** A reflection-based property walk over `TRequest`/`TResponse` to derive `Action`/`ResourceType`/`ResourceId`/`BeforeSnapshot`/`AfterSnapshot` — the request itself must supply these via `IAuditableRequest<TResponse>`'s self-supplied field surface, mirroring `ILoggableRequest<TResponse>`'s established convention exactly.

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
    // WO-058, shipped 2026-08-14:
    .AddDualApprovalBehavior()     // requires IAuthorizationContext (with IAuthorizationContextIdentity) AND IDualApprovalStore registered
    .AddCachingBehavior()          // requires SharedKernel.Caching.Abstractions.ICacheService registered
    .AddCacheInvalidationBehavior()// reuses the ICacheService guard above
    .AddResilienceBehavior()       // requires a ResiliencePipelineProvider<string> registered
    .AddIdempotencyBehavior()      // requires IIdempotencyKeyStore registered
    // WO-071, shipped:
    .AddAuditingBehavior()         // requires SharedKernel.Application.Behaviors.IAuditTrailWriter registered
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

// WO-058, shipped 2026-08-14.
// UserContextAuthorizationAdapter (above) additionally implements IAuthorizationContextIdentity so
// DualApprovalBehavior can resolve "who is calling right now" from the SAME bridge AuthorizationBehavior
// already uses — no second IAuthorizationContext registration.
// public sealed class UserContextAuthorizationAdapter : IAuthorizationContext, IAuthorizationContextIdentity
// {
//     public Task<string> GetCurrentIdentityAsync(CancellationToken ct) => Task.FromResult(_userContext.UserId);
//     // ... IAuthorizationContext members unchanged ...
// }

// Approval store — composition root provides the implementation; never a 06.Persistence/07.Messaging/
// 12.Security reference from SharedKernel.Application.Behaviors itself
services.AddScoped<SharedKernel.Application.Behaviors.IDualApprovalStore, SqlDualApprovalStore>();

// WO-071, shipped.
// Bridging this package's local IAuditTrailWriter to 06.Persistence's real, richer IAuditTrailWriter —
// composition root only, never inside SharedKernel.Application.Behaviors itself. This bridge is the ONE
// place either domain's real AuditEntry type is referenced by name.
services.AddScoped<SharedKernel.Application.Behaviors.IAuditTrailWriter>(sp =>
    new PersistenceAuditTrailWriterAdapter(
        sp.GetRequiredService<SharedKernel.Persistence.Abstractions.IAuditTrailWriter>()));

// public sealed class PersistenceAuditTrailWriterAdapter(
//     SharedKernel.Persistence.Abstractions.IAuditTrailWriter realWriter)
//     : SharedKernel.Application.Behaviors.IAuditTrailWriter
// {
//     public async Task RecordAsync(
//         SharedKernel.Application.Behaviors.AuditEntry entry, CancellationToken ct = default)
//     {
//         // Actor/tenant identity, OccurredOn, and hash-chain linkage are all resolved internally by the
//         // real writer via its own IAuditActorContext/IClock — this adapter only maps the smaller local
//         // shape onto the richer one. The Task<AuditRecord> the real writer returns is discarded here;
//         // AuditingBehavior's local seam never needs the persisted record back.
//         await realWriter.RecordAsync(new SharedKernel.Persistence.Abstractions.AuditEntry(
//             entry.Action, entry.ResourceType, entry.ResourceId,
//             entry.BeforeSnapshot, entry.AfterSnapshot, CorrelationId: null, entry.ApprovalId), ct);
//     }
// }

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

    // OrderPlacedIntegrationEvent: an [IntegrationEvent("orders.order-placed")] IIntegrationEvent (04.Contracts)
    // carrying primitives only; reusing the domain event's Id as EventId lets a retried publish deduplicate.
    public Task Handle(OrderPlacedDomainEvent domainEvent, CancellationToken ct)
        => _eventPublisher.PublishAsync(
            new OrderPlacedIntegrationEvent(domainEvent.Id, domainEvent.OccurredOn, domainEvent.Payload.OrderId), ct);
}

// (WO-058, shipped 2026-08-14) Dual-control/maker-checker
// worked example. A high-risk command opts in via IRequiresDualApproval:
public sealed record RotateSigningKeyCommand(Guid KeyId) : ICommand, IRequiresDualApproval
{
    public string ApprovalKey => $"rotate-signing-key:{KeyId}";
}

// A SEPARATE admin/approval command an approver (a DISTINCT identity from the initiator) dispatches — its
// handler is the ONLY place IDualApprovalStore.RecordApprovalAsync is ever called; DualApprovalBehavior
// itself only ever reads:
public sealed record ApproveKeyRotationCommand(Guid KeyId) : ICommand
{
    public string ApprovalKey => $"rotate-signing-key:{KeyId}";
}

public sealed class ApproveKeyRotationCommandHandler(
    IDualApprovalStore store, IAuthorizationContextIdentity identity) : ICommandHandler<ApproveKeyRotationCommand>
{
    public async Task<Result> Handle(ApproveKeyRotationCommand request, CancellationToken ct)
    {
        var approverId = await identity.GetCurrentIdentityAsync(ct);
        await store.RecordApprovalAsync(request.ApprovalKey, approverId, ct);
        return Result.Success();
    }
}

// The retry-after-approval flow:
//   1. Alice dispatches RotateSigningKeyCommand -> no approval recorded yet ->
//      Result.Failure(Error.Forbidden(...)) -- "awaiting a second approver." Handler never invoked.
//   2. Bob (a DISTINCT identity) dispatches ApproveKeyRotationCommand for the same KeyId ->
//      IDualApprovalStore.RecordApprovalAsync("rotate-signing-key:{KeyId}", "bob", ct).
//   3. Alice dispatches RotateSigningKeyCommand a SECOND time (same command/key) -> approval record found,
//      recorded identity "bob" != initiator identity "alice" -> next() is called -> handler executes.
//   4. If Alice had instead recorded her OWN approval in step 2 (self-approval), step 3 would STILL
//      short-circuit with Result.Failure(Error.Forbidden(...)) -- self-approval is structurally impossible.

// (WO-071, shipped) Auditing worked example. A simple audited command —
// no dual-approval — records exactly one entry, ApprovalId always null:
public sealed record UpdateCustomerAddressCommand(Guid CustomerId, string NewAddress, string OldAddressSnapshot)
    : ICommand, IAuditableRequest<Result>
{
    public string Action => "customer.address.update";
    public string ResourceType => "Customer";
    public string ResourceId => CustomerId.ToString("D");
    public string? BeforeSnapshot => OldAddressSnapshot; // caller pre-serializes; this package never parses it
    public string? GetAfterSnapshot(Result response) => response.IsSuccess ? NewAddress : null;
}

// A command combining BOTH capabilities — the recorded audit entry's ApprovalId is automatically populated
// from IRequiresDualApproval.ApprovalKey, linking the two without either interface referencing the other:
public sealed record RotateSigningKeyCommand(Guid KeyId)
    : ICommand, IRequiresDualApproval, IAuditableRequest<Result>
{
    public string ApprovalKey => $"rotate-signing-key:{KeyId}";
    public string Action => "signing-key.rotate";
    public string ResourceType => "SigningKey";
    public string ResourceId => KeyId.ToString("D");
    public string? BeforeSnapshot => null; // no meaningful "before" state for a key rotation
    public string? GetAfterSnapshot(Result response) => response.IsSuccess ? "rotated" : "rejected";
}
// The recorded AuditEntry for a successful dispatch of RotateSigningKeyCommand carries
// ApprovalId == "rotate-signing-key:{KeyId}" — AuditingBehavior populated it automatically via the
// `is IRequiresDualApproval` check, never a manual field the command author has to remember to set.
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
- **(WO-041, P-253, shipped 2026-07-10)** `[LoggerMessage]`-attributed static partial methods (`LoggingBehavior<,>`, `FireAndForgetBackgroundConsumer`, `ChannelFireAndForgetDispatcher`, `StreamLoggingBehavior<,>`) are compiler-generated at build time via the `Microsoft.Extensions.Logging.Abstractions` source generator — zero runtime reflection, fully AOT-safe, replacing `StreamLoggingBehavior`'s prior hand-written `LoggerMessage.Define<>()` static delegate fields (themselves already reflection-free, but hand-maintained and unnumbered against any reserved range) with an equivalently zero-reflection, source-generated equivalent. `ApplicationBehaviorsLoggingEventIds`'s `const int` fields are compile-time constants, directly usable as `[LoggerMessage(EventId = ...)]` arguments (which themselves require a compile-time constant expression) — no reflection, no runtime computation.
- **(WO-039, P-237, Core complete 2026-07-06)** `FailureResponseFactory`'s `ResultOfTDispatcher<TResponse>.BuildFactory()` (`SharedKernel.Application.Behaviors/Shared/FailureResponseFactory.cs`) no longer performs `Type.GetInterfaces()`, `Type.GetGenericArguments()`, `Type.MakeGenericType()`, `Type.GetMethod()`, or `MethodBase.Invoke()` — it dispatches through `01.Core`'s `IFailureFactory<TResponse>` (CRTP, self-referential), so `TResponse` itself is the type to invoke, with no inner value type to locate. One `MethodInfo.MakeGenericMethod` call remains, performed once per closed `TResponse` type (cached in a static field) and bound to a real delegate via `MethodInfo.CreateDelegate` — never a per-call `MethodBase.Invoke()`. This is a disclosed, deliberate second dispatch of the same shape as `MediatRDomainEventDispatcher`'s already-approved one, not an open AOT/trimming hazard: the `MethodInfo` is captured at type-load time via a compile-time typed-delegate instantiation (never a string-based `Type.GetMethod(name)` lookup), so there is no string-keyed member lookup for a trimmer to break. See "Constructing a generic failure response" above for the full rationale and the residual-scope trade-off (`AuthorizationBehavior`/`IdempotentCommandBehavior`'s generic shapes were kept unchanged per this phase's design, D-37).
- **(WO-071, shipped)** `IAuditableRequest<TResponse>`, `IAuditTrailWriter`, and `AuditEntry` are plain interfaces/a sealed record with BCL-typed (`string`/`string?`) properties — AOT-safe by definition, identical posture to `ILoggableRequest<TResponse>`. `AuditingBehavior<TRequest,TResponse>` is a closed generic sealed class resolved by MediatR's ordinary DI-generics `IPipelineBehavior<,>` mechanism — no `MakeGenericType`/`MakeGenericMethod`, no reflection of any kind. The dual-approval linkage (`is IRequiresDualApproval` pattern match) is a plain C# type-pattern check — zero reflection.

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
- **(WO-041, P-253, shipped 2026-07-10)** Logging-authoring retrofit tests: existing `LoggingBehavior`/`StreamLoggingBehavior`/`FireAndForget*` test suites pass unmodified in observable behavior (log level, message content, `ElapsedMilliseconds` value, `BeginScope` attachment) after the `[LoggerMessage]` conversion — one non-behavioral fix was required: NSubstitute's `ILogger<T>` mock defaults `IsEnabled()` to `false`, which silently no-ops `[LoggerMessage]`-generated partial methods; fixed via `logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true)` at each affected call site (`LoggingBehaviorTests`, `LoggingBehaviorFailureLevelTests`, `LoggingBehaviorLoggableRequestTests`); new tests assert the correct `EventId` on every retrofitted log call (`LoggingBehavior` 5100-5103, `FireAndForget` 5110-5111, `StreamLoggingBehavior` 5120-5123); a Mono.Cecil IL-shape assertion (mirroring T-33's `FailureResponseFactoryReflectionShapeTests.cs` technique, via the existing test-project-only `00.Governance/SharedKernel.ArchitectureTests` reference) proves zero hand-authored `Call`/`Callvirt` IL instructions in `SharedKernel.Application.Behaviors.dll` resolve to `LoggerExtensions.Log*`/`ILogger.Log`/`LoggerMessage.Define*` — correctly excluding the `[LoggerMessage]` source generator's own `[GeneratedCodeAttribute]`-marked output (including its `.cctor` delegate-cache field-assignment pairing, confirmed via Mono.Cecil attribute inspection to legitimately call `LoggerMessage.Define<>` as generator-internal machinery); a real-assembly test invokes `00.Governance`'s `LoggingEventIdIntegrityAssertion` against this domain's real `SharedKernel.Application`/`SharedKernel.Application.Behaviors` assemblies, now that `01.Core` P-249 and `00.Governance` P-250 have both shipped — passes. `SharedKernel.Application.Behaviors.Tests` 130/130 passing.
- **(WO-040, P-246, shipped)** `ILoggableRequest<TResponse>` opt-in payload logging tests (`LoggingBehavior` test file, extended): a request implementing the marker attaches `LoggableRequestFields` to the entry-log scope; `GetLoggableResponseFields(response)` is attached to the completion-log scope on both a success and a `Result.Failure` outcome (structured fields compose correctly with the existing Information/Warning level decision); `GetLoggableResponseFields` is never invoked and no response-fields scope is entered on a thrown exception; a null/empty field-set result enters no scope (zero-overhead path); a request NOT implementing the marker produces a byte-for-byte identical log call sequence to the pre-WO-040 baseline — a dedicated regression test, not just code review, mechanically enforcing the additive-only guarantee.
- **(WO-058, unblocked 2026-08-14 — `DualApprovalBehavior`/`IAuthorizationContextIdentity` shipped, tests not yet written)** `DualApprovalBehavior` tests: no recorded approval → handler never invoked, `Result.Failure` with `ErrorType.Forbidden` returned (asserted for both `Result` and `Result<T>` response shapes); recorded approval from a DISTINCT identity → `next()` invoked and its result returned unchanged; recorded approval from the SAME identity as the initiator (self-approval) → handler never invoked, `Result.Failure` with `ErrorType.Forbidden` returned even though a record exists; a query type can never satisfy `IRequiresDualApproval` — a compile-time/contract-shape assertion only, mirroring `IIdempotentRequest`'s existing query-exclusion test; `AddDualApprovalBehavior()` `Build()`-time guard tests for both dependencies (`IAuthorizationContext`, `IDualApprovalStore`) missing individually and together. Now actionable — tracked as `SK.05.Tests` T-69..T-73, a separate phase key from `SK.05.Core` (now `●`) — see `05.Application/state-map.md`.
- **(WO-071, shipped)** `AuditingBehavior` tests: a command implementing `IAuditableRequest<TResponse>` alone (no dual-approval) calls `IAuditTrailWriter.RecordAsync` exactly once with `ApprovalId = null`, for both a `Result.Success` and a `Result.Failure` outcome; a command additionally implementing `IRequiresDualApproval` produces a recorded entry whose `ApprovalId` equals `ApprovalKey`; a command NOT implementing `IAuditableRequest<TResponse>` never resolves `AuditingBehavior` into its pipeline at all — a DI-contract test against a spy `IAuditTrailWriter` receiving zero calls; `IAuditTrailWriter.RecordAsync` is never called when `next()` throws; an exception thrown by `RecordAsync` itself propagates unchanged, never caught/swallowed; `AddAuditingBehavior()` `Build()`-time guard test for the missing `IAuditTrailWriter` dependency; a registration-order regression test proving `IAuditTrailWriter.RecordAsync` is observably called BEFORE `IUnitOfWork.SaveChangesAsync` for the same request (a genuine temporal proof, not merely a registration-list position assertion) — tracked as `SK.05.Tests` T-74..T-78, see `05.Application/state-map.md`.
- **Standard test package set:** `xunit` 2.9.3, `xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.13.0, `coverlet.collector` 6.0.4, `FluentAssertions` 8.10.0 (bumped from 8.4.0 in WO-039 P-241/S-17 — `00.Governance/SharedKernel.ArchitectureTests` pins `FluentAssertions` 8.10.0, and NuGet's `NU1605` package-downgrade check errors on a lower pin transitively required by a `ProjectReference`; both test projects must stay at or above whatever `SharedKernel.ArchitectureTests` requires), `NSubstitute` 5.3.0 — same pins already used across `06.Persistence`/`07.Messaging` unless a documented reason requires otherwise.
- **Test-project-only `SharedKernel.ArchitectureTests` reference (WO-039 P-241, S-17):** both `SharedKernel.Application.Tests.csproj` and `SharedKernel.Application.Behaviors.Tests.csproj` carry a `ProjectReference` to `00.Governance/SharedKernel.ArchitectureTests` — never from either production `.csproj`. The reference is scaffolded ahead of P-241's Core phase (which is blocked on `00.Governance` P-240 plus this domain's own P-237/P-239 Core phases) so the real-assembly rule invocations can be written the moment those blockers clear.
- **GlobalUsings.cs required** — every test project must include a `GlobalUsings.cs` containing `global using Xunit;`. `ImplicitUsings` does not auto-import xUnit attributes.

---

## Changelog

> Maintained by the application domain agent. One line per significant change.

- [2026-08-14] WO-058 Core phase partially shipped (C-76/C-77, 2/6) — implemented `IRequiresDualApproval` and `IDualApprovalStore` in `SharedKernel.Application.Behaviors/DualApproval/`, exactly per the already-locked D-72/D-73 design; both are plain interfaces with no dependency on `01.Core`'s still-missing `Error.Forbidden(...)`. Re-verified C-78 (`IAuthorizationContextIdentity` + `DualApprovalBehavior<,>`) is genuinely `⚑` blocked by direct inspection of `01.Core/SharedKernel.Primitives/Errors/Error.cs`/`ErrorType.cs` (still only `Unexpected`/`Validation`/`NotFound`/`Conflict`/`Unauthorized`/`BusinessRule`); C-79 (`AddDualApprovalBehavior()`) and C-80 (pipeline-order insertion) are transitively gated on `DualApprovalBehavior<,>` existing, and C-81 (build verification) is explicitly scoped to "once C-76..C-80 land" — all three correctly left untouched. Both packages rebuild 0 warnings/0 errors in Release; full regression `SharedKernel.Application.Tests` 28/28 + `SharedKernel.Application.Behaviors.Tests` 132/132 passing, unchanged (no new tests — `DualApprovalBehavior` tests remain blocked on C-78). Top-of-file WO-058 status callout updated to "Core `◐` partial (2/6)". `05.Application/state-map.md` `SK.05.Core` now 77/81 `◐` — not promoted, `state-map-phase` not invoked. The `01.Core` `Error.Forbidden(...)` blocker still needs its own companion `core-arch-planner` dispatch (application-phase-implementer)
- [2026-08-14] WO-058 Scaffold phase closed (S-20..S-22, 22/22) — created only the empty `DualApproval/` folder in `SharedKernel.Application.Behaviors`, matching every prior Scaffold session's folders-only convention (Resilience/CacheInvalidation/Streaming/TestHarness precedent). `IRequiresDualApproval`/`IDualApprovalStore`/`IAuthorizationContextIdentity`/`DualApprovalBehavior` deliberately left unwritten — their real implementation is Core's job (C-76/C-77/C-78), and C-78 remains genuinely `⚑` blocked on `01.Core` shipping `Error.Forbidden(...)`. Confirmed via `dotnet build` that both production packages build 0 warnings/0 errors and carry zero forbidden `ProjectReference`s (`06.Persistence`/`07.Messaging`/`12.Security`); full regression 28/28 + 132/132 tests passing, no code changes. Top-of-file WO-058 status callout corrected from the stale "Design dispatched, 0/9" to reflect Design and Scaffold both complete (application-phase-implementer)
- [2026-08-13] WO-058 (root P-380) dispatched — a new opt-in dual-control/maker-checker pipeline behavior, `DualApprovalBehavior<TRequest,TResponse>`, gated by a new `IRequiresDualApproval` marker (structurally identical to `IAuthorizeRequest`/`IIdempotentRequest`), backed by a new local seam `IDualApprovalStore` (mirrors `IIdempotencyKeyStore`'s bridge shape) for high-value commands requiring a second, distinct approving identity. Self-approval prevention solved WITHOUT a breaking change to the already-published `IAuthorizationContext`: a new sibling optional-capability interface, `IAuthorizationContextIdentity`, added instead — mirrors the `IIdempotencyResponseStore`/`IIdempotencyKeyStore` precedent exactly (WO-039, P-242). Canonical pipeline order revised from ten to eleven named slots, inserting `DualApprovalBehavior` as step 6 (immediately after Authorization, before Caching); steps 7-10 renumbered to 8-11. `ApplicationBehaviorsBuilder.AddDualApprovalBehavior()` designed as this domain's first two-dependency `Build()`-time guard. **Genuine, disclosed cross-domain blocker:** `DualApprovalBehavior`'s short-circuit needs `Error.Forbidden(...)`, confirmed absent from `01.Core` today (verified by direct inspection of `01.Core/SharedKernel.Primitives/Errors/Error.cs`/`ErrorType.cs`) — flagged for a companion `01.Core` phase, explicitly NOT worked around by reusing `Error.Unauthorized(...)` (a materially different semantic). A `16.Testing` in-memory `IDualApprovalStore` fake is required by this phase's own acceptance criteria but is out of this domain's jurisdiction — flagged as a companion `testing-arch-planner` phase. Local-seam bridging callout updated from "three times" to "four times"; Packages table, Pipeline Composition, Hard Violations, DI Registration (with a worked maker-checker retry example), and Test Rules all updated (application-arch-planner)
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
- [2026-07-08] WO-040 Design phase locked (D-59..D-65 → `●`, `SK.05.Design` phase key now 65/65 `●` across all five work orders). Verified the `ILoggableRequest<TResponse>` contract and the extended `LoggingBehavior<,>` NOTE already present in this file (written by the prior `application-arch-planner` dispatch) unambiguously cover all six design points: the marker shape (D-59), the in-place-extension/no-eleventh-slot placement decision with rationale (D-60, also already present as Hard Violations entry 905), the byte-for-byte-identical-when-not-opted-in guarantee (D-61), the `ILogger.BeginScope` attachment mechanism — now additionally noting it mirrors ASP.NET Core's own `HttpLoggingMiddleware` convention (D-62), the exception-path rule that `GetLoggableResponseFields` is never invoked when `next()` throws while the request-side scope still applies (D-63), and the Hard Violations entry prohibiting payload logging by any means other than the marker (D-64, already present as entries 904-907). No content gaps found; no redesign performed. Removed all remaining "design-only"/"not yet implemented" qualifiers on WO-040 content now that Design is locked, replacing them with "design locked YYYY-MM-DD, Core pending" — matching the phrasing convention `05.Application/state-map.md` already used for WO-039 P-241's Design→Core handoff ("Design (D-46..D-48) locked 2026-07-03; Core/Tests execution blocked on..."). Updated: top-of-file WO-040 status callout, Packages table row, `LoggingBehavior`/`ILoggableRequest<TResponse>` NOTE blocks, and the WO-040 Test Rules entry. Scaffold/Core/Tests/Docs/Published phases for WO-040 remain untouched and unimplemented — this session is Design-only, no C# code written (application-phase-implementer)
- [2026-07-09] WO-041 (P-253) dispatched — design locked, Core/Tests pending. Every direct `ILogger` extension-method call and hand-written `LoggerMessage.Define<>()` delegate in `SharedKernel.Application.Behaviors` (`LoggingBehavior<,>` — four call sites; `FireAndForgetBackgroundConsumer` — one; `ChannelFireAndForgetDispatcher` — one; `StreamLoggingBehavior<,>` — four, currently ad hoc `EventId(1..4, "Name")` outside any reserved range; confirmed by direct source inspection to be the complete, exhaustive set — `SharedKernel.Application` has zero `ILogger` usage) is converted to `[LoggerMessage]`-attributed static partial methods with explicit `EventId`s inside this domain's reserved `01.Core` `LoggingEventIdRanges.Application` range (5000-5999), sub-divided per this domain's package declaration order: `SharedKernel.Application` = 5000-5099 (unused), `SharedKernel.Application.Behaviors` = 5100-5199 (`LoggingBehavior` 5100-5103, `FireAndForget` 5110-5111, `StreamLoggingBehavior` 5120-5123). New top-of-file status callout; new "Logging EventId Allocation" subsection with the full ten-entry table; retrofit addenda added to the `LoggingBehavior`/`IFireAndForgetDispatcher`/`StreamLoggingBehavior` NOTE blocks; three new Hard Violations entries (no direct `ILogger` calls/hand-written `Define`, no ad hoc `EventId` literals, no behavioral drift under cover of the retrofit); Test Rules and AOT Compatibility sections extended. Message templates, levels, and named placeholders are unchanged; the `ILoggableRequest<TResponse>` `BeginScope` opt-in mechanism (WO-040, P-246) is explicitly out of scope and preserved byte-for-byte. `05.Application/state-map.md` carries the full 22-task breakdown across all six phases; Core/Tests genuinely blocked on `01.Core` P-249 and `00.Governance` P-250, both `○` Pending as of this dispatch (application-arch-planner)
- [2026-07-08] WO-040 Core phase shipped (C-66..C-68 → `●`, `SK.05.Core` phase key now 68/68 `●`). Implemented `ILoggableRequest<TResponse>` (`Logging/ILoggableRequest.cs`, `SharedKernel.Application.Behaviors`) and extended `LoggingBehavior<TRequest,TResponse>` with the additive `is ILoggableRequest<TResponse> loggable` branch: `BeginScope(loggable.LoggableRequestFields)` wraps the entry log and the fault-path `Error` log (skipped when null/empty), `BeginScope(loggable.GetLoggableResponseFields(response))` wraps the completion log only on a normal (non-throwing) return. No new pipeline slot, no reflection, zero new NuGet dependency. Both packages build 0 warnings/0 errors; `SharedKernel.Application.Behaviors.Tests` 115/115 passing unchanged, confirming byte-for-byte-identical logging for requests not implementing the marker. Replaced "design locked, Core pending" qualifiers in the Logging section and Packages table with "shipped 2026-07-08". Dedicated regression tests (T-53..T-59) remain for the Tests phase (application-phase-implementer, sync-brain)
- [2026-07-10] **CORRECTION + full WO-041 (P-253) implementation.** The 2026-07-09 "Design locked" claim above was stale/incorrect — direct inspection of `05.Application/state-map.md`'s task table at the start of this session found D-66..D-71 still genuinely `○`; both cross-domain blockers were re-verified as actually shipped (`01.Core` `SK.01.P249` 4/4 `●`; `00.Governance` `SK.00.LoggingStandardEnforcement` 11/11 `●`) before proceeding. Carried WO-041 through Design → Scaffold → Core → Tests in one session (`SK.05.Design/Scaffold/Core/Tests` now 71/19/74/66, all `●`). Implemented `Shared/ApplicationBehaviorsLoggingEventIds.cs` (ten `const int` fields); converted `LoggingBehavior<,>`, `FireAndForgetBackgroundConsumer`, `ChannelFireAndForgetDispatcher`, and `StreamLoggingBehavior<,>` to `partial` with `[LoggerMessage]`-attributed static partial methods replacing every direct `ILogger` call and the four hand-written `LoggerMessage.Define<>()` delegates; both packages build 0 warnings/0 errors. Existing `LoggingBehavior*`/`StreamLoggingBehaviorTests` suites required one non-behavioral fix (NSubstitute's `ILogger<T>` mock defaults `IsEnabled()` to `false`, silently no-oping `[LoggerMessage]`-generated methods — fixed via `logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true)`). New tests: `LoggingBehaviorEventIdTests.cs`, `FireAndForgetLoggingEventIdTests.cs` (uses an open-generic `RecordingLogger<>` + shared static sink since `ChannelFireAndForgetDispatcher` is `internal`), `Governance/LoggingAuthoringStyleShapeTests.cs` (Mono.Cecil IL-shape assertion, correctly excluding the `[LoggerMessage]` generator's own `[GeneratedCodeAttribute]`-marked output), and `Governance/LoggingEventIdIntegrityRealAssemblyTests.cs` (first real-assembly pass of `00.Governance`'s `LoggingEventIdIntegrityAssertion`). Discovered and worked around (in the test only, not fixed in production — out of scope for an authoring-only retrofit) a pre-existing latent quirk: `ChannelFireAndForgetDispatcher`'s `DropAndLog` policy uses `BoundedChannelFullMode.DropOldest`, under which `TryWrite` never returns `false` on a full channel, making the `LogChannelFull` warning branch unreachable through the builder's exact real wiring. `SharedKernel.Application.Behaviors.Tests` now 130/130 passing (up from 121). All prior stale "design locked, Core pending" qualifiers for WO-041 replaced with "shipped 2026-07-10" throughout this file. Docs (DO-22..DO-24) and Published (P-20..P-22) remain unimplemented (application-phase-implementer)
- [2026-07-28] **SK0030 real-source-audit fix — `FireAndForgetBackgroundConsumer` silent-failure observability gap closed.** `00.Governance`'s new `ResultOutcomeDiscardedAnalyzer` (WO-049, P-299) real-source audit flagged `FireAndForgetBackgroundConsumer.cs` line 57 (`await sender.Send(command, stoppingToken).ConfigureAwait(false);`) as its exact fire condition: the awaited `Result` (implementing `IHasSuccessFlag`) was discarded as a bare statement. The operational defect behind the mechanical finding: the surrounding `try/catch` only observed thrown exceptions (logged via the pre-existing `LogCommandFaulted`, EventId 5111) — a handler returning `Result.Failure(...)` (a deliberate business-rule failure, not an exception) vanished with zero telemetry. Fixed per the "log failures, then discard" decision: the returned `Result` is now captured; on `IsFailure`, a new `[LoggerMessage]`-attributed static partial method (`LogCommandFailed`, EventId **5112** — `ApplicationBehaviorsLoggingEventIds.LogCommandFailed = LoggingEventIdRanges.Application + 112`, the next available slot in the `FireAndForget/` 5110-5119 sub-range) logs at `Warning` with the command's type name and `result.Error.Code`, then the result is still discarded — fire-and-forget contract unchanged, only observability added. The existing `catch (Exception ex)` → `LogCommandFaulted` (5111, Error) path is untouched. Targeted, scope-disciplined fix: no other behavior, the exception path, the fire-and-forget public contract, or DI wiring were touched. Two new tests added to `FireAndForgetLoggingEventIdTests.cs`: `FireAndForgetBackgroundConsumer_HandlerReturnsFailure_LogsWarningWithEventId5112` and the regression `FireAndForgetBackgroundConsumer_HandlerReturnsSuccess_DoesNotLogEventId5112` (both drive the real, documented `AddFireAndForgetDispatch()` wiring end-to-end via the existing open-generic `RecordingLogger<>`/`SharedRecordingSink` pattern, since `ChannelFireAndForgetDispatcher`'s consumer resolves `FireAndForgetBackgroundConsumer` — a public type — but the dispatcher itself stays internal). Both packages build 0 warnings/0 errors under `TreatWarningsAsErrors`/`GenerateDocumentationFile`; `SharedKernel.Application.Behaviors.Tests` 132/132 passing (up from 130) — one unrelated pre-existing flaky `ActivityListener`-based test (`MetricsBehaviorKeyUniquenessTests.TracingBehavior_RequestNameTag_UsesFullNameNotShortName`) failed once under full-suite parallel execution and passed cleanly both in isolation and on a full-suite re-run, confirmed unrelated to this change and left untouched per scope discipline. `SK0030`'s `05.Application` candidate follow-up is now closed (application-phase-implementer)
- [2026-08-14] WO-058/`SK.05.Core` unblocked and shipped (C-78..C-81, 81/81 `●`) — `01.Core`'s `Error.Forbidden(...)` (P-384/WO-059) verified shipped; `IAuthorizationContextIdentity`, `DualApprovalBehavior<,>`, `AddDualApprovalBehavior()`'s two-dependency guard, and the eleven-slot pipeline order all implemented per the locked design. Every stale "WO-058, design-only"/"Core BLOCKED on 01.Core" qualifier swept to "shipped 2026-08-14" throughout the file (top-of-file callouts, DualApproval section header/blocker callout, Packages table, builder DI extension, pipeline composition table + rationale, five Hard Violations entries, DI Registration worked example, Test Rules entry) — no `Error.Unauthorized(...)` substitution was ever used. `SK.05.Tests` (T-69..T-73) now unblocked and actionable but not yet implemented, tracked in `05.Application/state-map.md` (application-phase-implementer, sync-brain)
- [2026-08-17] `SK.05.Published` closed (23/23) — all seven work orders (WO-035/036/038/039/040/041/058) now `●` complete through Published; this domain's own scope is fully shipped end to end. Both packages version-bumped `1.0.0` → `1.1.0` (single coordinated additive-only minor bump) and repacked clean, zero warnings (no internal feed configured — `dotnet nuget push` not attempted). Consumer-verify harness substantially extended — `SharedKernel.Application.Behaviors.Tests` 150 → 161 (11 new end-to-end DI-chain tests: Resilience+Idempotency+Transaction+CacheInvalidation, a traced-query `ActivitySource` span, the Resilience/CacheInvalidation `Build()` guards, fire-and-forget dispatch via the real background consumer, idempotency response replay, `AddDefaultBehaviors()`, `ILoggableRequest<TResponse>`, and the full maker-checker flow), `SharedKernel.Application.Tests` 28 → 29 (the `AddSharedKernelApplication(configure)` parallel-dispatch overload). One reusable pitfall discovered and worth remembering: NSubstitute/Castle DynamicProxy cannot generate an `ILogger<T>` proxy over a `private`-nested generic type argument — test types used as `ILogger<T>`'s `T` in a consumer-verify test must be `public`. All six top-of-file WO status callouts (WO-036/038/039/040/041/058) corrected from stale "Docs/Published pending" framing to reflect full completion; a new Published-phase paragraph added after the WO-041 callout summarizing the version bump and final test counts. Root Phase Backlog **P-380 (WO-058) stays `◐` Dispatched at the root, not closed** — its acceptance criteria still require the `16.Testing` in-memory `IDualApprovalStore` fake, re-verified this session as still genuinely blocked (`16.Testing/state-map.md` `C-117`/`C-118` both `⚑`, no `.cs` file exists) — out of this domain's jurisdiction. Root `CLAUDE.md` not touched — no new package, DI convention, layering rule, or approved technology was introduced this session (application-phase-implementer, sync-brain)
- [2026-08-26] WO-071 (root P-458) dispatched — Design phase, `○` all six phases queued, zero code written. New opt-in `AuditingBehavior<TRequest,TResponse>` gated by a new `IAuditableRequest<TResponse>` marker, mirroring `ILoggableRequest<TResponse>`'s exact self-supplied-field-surface shape (immediate `Action`/`ResourceType`/`ResourceId`/`BeforeSnapshot` properties plus `GetAfterSnapshot(TResponse response)`, opaque strings only, never a reflection-based property walk). Read `06.Persistence/CLAUDE.md`'s newly design-locked P-456 `IAuditTrailWriter`/`AuditEntry`/`IAuditActorContext` shape (planned this same session by `persistence-arch-planner`) before designing against it. **Central design question resolved: local-seam bridging, the FIFTH instance of this domain's established pattern** (`IUnitOfWork`, `IAuthorizationContext`, `IIdempotencyKeyStore`, `IDualApprovalStore`, now `IAuditTrailWriter`) — root `CLAUDE.md` confirms `05.Application → may reference 01–04` only, which does not include `06.Persistence.Abstractions`, so a direct reference to the real, richer P-456 contract was never legally available; a new local `IAuditTrailWriter`/`AuditEntry` seam, deliberately smaller than the real contract (no `Id`/`ActorId`/`TenantId`/`OccurredOn`/`CorrelationId`/hash-chain fields — all resolved by the REAL writer, never by this package or the caller), is the correct answer, matching the pattern-memory playbook exactly. Audit records remain strictly opt-in via the marker — never fed automatically off `SaveChanges`/`AuditInterceptor`, mirroring `06.Persistence`'s own already-declined "hidden audit write" design. Dual-approval linkage (WO-058's `IRequiresDualApproval`) composed via a plain `is`-pattern check on `ApprovalKey` — zero coupling to `IDualApprovalStore`, no scope expansion into that seam's own implementation. Canonical pipeline order grows from eleven to **twelve** named slots, inserting `AuditingBehavior` as the new step 10 (between `IdempotentCommandBehavior` and `TransactionBehavior`, renumbered to 11; `CacheInvalidationBehavior` renumbered to 12) — positioned so the audit write completes BEFORE `TransactionBehavior`'s own commit executes ("just inside Transaction"), the temporal mirror-image of how `CacheInvalidationBehavior` runs strictly AFTER a confirmed commit, realized via the same inverted-registration-order technique applied in the opposite direction. **A genuinely new finding for this domain: UNLIKE every prior local-seam addition, this one carries NO cross-domain Core-phase blocker** — the local seam is fully self-contained and does not need `06.Persistence`'s real P-456/P-457 implementation (design-locked, implementation pending) to exist for this domain's own Core phase to proceed; only the eventual composition-root bridge needs it. Top-of-file summary, local-seam-bridging callout ("four times" → "five times"), Packages table, a new Auditing Interface Contracts subsection (mirroring the DualApproval section's shape), the twelve-step Pipeline Composition table + full positional rationale, seven new Hard Violations entries, DI Registration (composition-root bridge adapter + two worked examples — a plain audited command and a dual-approval-linked one), AOT Compatibility, and Test Rules all updated. `05.Application/state-map.md` gained 28 new tasks (D-81..D-89, S-23, C-82..C-87, T-74..T-78, DO-28..DO-30, P-27..P-30), Phase Key Registry gained 6 new WO-071 rows, Package Board updated, Cross-Domain Dependencies gained 4 new rows (including one explicitly marked informational-only, not a genuine blocker), Overall Progress counts updated to 89D/23S/87C/78T/30DO/27P, all six phase keys reopened from `●` to `◐`. Downstream awareness only, not acted on: root Phase Backlog P-459 (`16.Testing` audit-trail fakes) depends on this phase AND `06.Persistence`'s P-457 (application-arch-planner)
- [2026-09-04] WO-071 (root P-458) implemented and shipped end to end — Scaffold/Core/Tests/Docs `●` complete (Published in progress). `Auditing/IAuditableRequest.cs`, `Auditing/AuditEntry.cs`, `Auditing/IAuditTrailWriter.cs`, `Auditing/AuditingBehavior.cs` implemented exactly per the locked D-81..D-88 design in `SharedKernel.Application.Behaviors`; `ApplicationBehaviorsBuilder.AddAuditingBehavior()` + single-dependency `IAuditTrailWriter` `Build()`-time guard added. **One implementation-only refinement to the design, discovered empirically via a real DI-registered pipeline dispatch, not by inspection:** `AuditingBehavior` must be registered in `Build()`'s implementation AFTER `TransactionBehavior` (not before, as D-86's prose describing "step 10 then step 11" might suggest at a literal reading) for `IAuditTrailWriter.RecordAsync` to observably precede `IUnitOfWork.SaveChangesAsync` — MediatR wraps `IPipelineBehavior<,>` so the first-registered behavior is outermost and its post-`next()` code runs last; this was proven both by an initial failing test and the corrected passing one (`AuditingTransactionOrderingTests`, T-77). No documented interface shape, constraint, or behavior semantic changed — only the internal registration-list position, which was already flagged in the design as needing "the inverted-registration-order technique." **A second, independent finding surfaced by the same empirical method, OUT OF SCOPE for this phase and left unfixed:** `CacheInvalidationBehavior`'s existing shipped registration order (`Transaction` before `CacheInvalidation` in `Build()`) does NOT actually achieve its own documented "evicts only after a confirmed commit" invariant — a real pipeline probe showed `ICacheService.RemoveAsync` firing BEFORE `IUnitOfWork.SaveChangesAsync`. This is a genuine, previously-undetected defect in already-shipped WO-036 code; flagged in the top-of-file callout and this entry for a follow-up work order, not silently fixed here. Tests: `AuditContractShapeTests`, `AuditingBehaviorTests` (all five T-75 scenarios), `AuditingTransactionOrderingTests` (T-77's genuine temporal proof), `ApplicationBehaviorsBuilderTests` guard tests (T-76) + updated fixed-order permutation test (now asserting the twelve-behavior physical order with `TransactionBehavior` before `AuditingBehavior`), and three new `ConsumerVerifyTests` end-to-end scenarios (P-29: plain audited command, dual-approval-linked audited command, non-audited command triggers zero seam calls). `SharedKernel.Application.Behaviors.Tests` 161 → 183 passing, zero failures. `.csproj` `Description`/`PackageTags` updated (P-27); `README.md` gained the twelve-slot table, the physical-vs-temporal-order callout, the full Auditing usage section, the `IAuditTrailWriter` composition-root bridge recipe, and a `CacheInvalidationBehavior`-parity streaming-exclusion row (DO-29). This file's Pipeline Composition/Hard Violations/DI Registration/AOT/Test Rules sections and every scattered "WO-071, `○` design-locked[, not yet implemented]" marker swept to "shipped" (DO-30). `05.Application/state-map.md`: D-81..D-89/S-23/C-82..C-87/T-74..T-78/DO-28..DO-30 marked `●`; P-27..P-29 marked `●`, P-30 (this update) in progress. Root Phase Backlog P-459 (`16.Testing` audit-trail fakes) is now unblocked on this domain's side — the seam it needs to fake is `SharedKernel.Application.Behaviors.Auditing.IAuditTrailWriter` (`RecordAsync(AuditEntry entry, CancellationToken ct = default) → Task`) and `AuditEntry` (`Action`/`ResourceType`/`ResourceId`/`BeforeSnapshot`/`AfterSnapshot`/`ApprovalId`, all `string`/`string?`) — both now real, shipped types in `SharedKernel.Application.Behaviors.dll` (application-phase-implementer)
- [2026-09-15] Contracts redesign: handler rules no longer reference the removed `Envelope`/`Envelope<T>` — handlers return `Result`/`Result<T>` and HTTP failures are RFC 9457 ProblemDetails via `14.Presentation`'s `ResultHttpExtensions` — and the domain-event handler example now maps to an `[IntegrationEvent]`-attributed `IIntegrationEvent` (coordinator)
