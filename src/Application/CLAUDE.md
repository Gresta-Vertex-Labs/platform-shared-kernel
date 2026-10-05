# 05.Application — Domain Brain

> The CQRS layer: a kernel-owned request/handler/pipeline vocabulary with **no MediatR in it**
> (`SharedKernel.Application`), a request pipeline of cross-cutting behaviors composed in a fixed order with a native
> domain-event dispatcher (`.Pipeline`), query caching and post-commit eviction (`.Pipeline.Caching`), and one adapter
> that uses MediatR as the transport behind `ISender` (`.Mediator.MediatR`). Handlers return `Result`/`Result<T>`
> exclusively. This domain does **not** own the caller, transaction or audit contracts (`IRequestContext`,
> `IUnitOfWork`, `IAuditTrailWriter` — `01.Core`'s `SharedKernel.Execution`), the idempotency store
> (`18.Idempotency`), the cache (`02.Caching`), validation libraries (`01.Core`'s `SharedKernel.Validation.FluentValidation`)
> or any response envelope (there is none). Consumers read each package's `README.md` and the folder `README.md`.

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Application` | Abstractions | Kernel mediator contracts (`IRequest<T>`, `IRequestHandler<,>`, `ISender`, `IPipelineBehavior<,>`, `RequestHandlerContinuation<T>`, stream counterparts), CQRS vocabulary (`ICommand`/`ICommand<T>`/`IQuery<T>`/`IStreamQuery<T>` + handler aliases), `IRequestValidator<T>`, `IDomainEventHandler<T>`, `[RequirePermission]`, and every marker (`IIdempotentRequest`, `IAuditableRequest<T>`, `ILoggableRequest<T>`, `ICacheableQuery<T>`, `IInvalidatesCache`, `CacheScope`, `CacheKeyRef`, `ICommandScope`). References `SharedKernel.Primitives`, `SharedKernel.Domain`, `SharedKernel.Caching.Abstractions` only |
| `SharedKernel.Application.Pipeline` | Host | The one registration call `AddSharedKernelApplication(assemblies, app => …)`, `ApplicationPipelineBuilder`/`PipelineStage`, `RequestPipeline<,>`/`StreamRequestPipeline<,>`, the internal behaviors and `DomainEventDispatcher` |
| `SharedKernel.Application.Pipeline.Caching` | Host | `app.WithCaching()` — internal `CachingBehavior<,>` (query stage) and `CacheInvalidationBehavior<,>` (command stage) |
| `SharedKernel.Application.Mediator.MediatR` | Host | `app.UseMediatR()` — MediatR 12.4.1 as the transport behind `ISender` |

`SharedKernel.Application.ConsumerVerify` (not packable, not in the solution) references the packed `Application`,
`.Pipeline` and `.Mediator.MediatR` packages (plus `Idempotency.Abstractions` and `Validation.FluentValidation`) by
`PackageReference` pinned with `-p:SharedKernelPackageVersion=…`, and runs a command end to end against them.

## Public Entry Points

```csharp
services.AddSharedKernelApplication(typeof(PlaceOrderHandler).Assembly, app => app
    .UseMediatR()                                        // .Mediator.MediatR — the ISender (always required)
    .WithIdempotency(o => o.RetentionWindow = TimeSpan.FromHours(24)) // IIdempotencyStore (Request) + IRequestContext
    .WithTransactions()                                  // IUnitOfWork
    .WithAuditing()                                      // IAuditTrailWriter; registers both auditing halves
    .WithCaching()                                       // .Pipeline.Caching; ICacheService + ITenantCacheKeyProvider
    .WithBehavior(typeof(MyBehavior<,>), PipelineStage.Command, typeof(IMyDependency)));
services.AddFluentValidationRequestValidators(typeof(PlaceOrderHandler).Assembly); // optional FluentValidation bridge
```

- **`SharedKernel.Application.Pipeline`** — `AddSharedKernelApplication(params Assembly[])`, `(Assembly, Action<ApplicationPipelineBuilder>)`, `(Assembly[], Action<ApplicationPipelineBuilder>)`; `AddDomainEventHandler<TDomainEvent, THandler>()` for a handler outside the scanned assemblies. `ApplicationPipelineBuilder` (`Services`, `Assemblies`, `WithIdempotency(Action<IdempotencyBehaviorOptions>?)`, `WithTransactions()`, `WithAuditing()`, `WithBehavior(type, stage, params requiredServices)`). `PipelineStage` (`Observability`, `Authorization`, `Validation`, `Query`, `Command`). Options: `ApplicationLoggingOptions.SlowRequestThreshold` (500 ms), `IdempotencyBehaviorOptions` (`LeaseDuration` 30 s, `RetentionWindow` 24 h, lease < retention) — DataAnnotations + `ValidateOnStart`, no configuration section. `RequestPipeline<TRequest,TResponse>.HandleAsync` and `StreamRequestPipeline<,>` are public because a mediator adapter (and tests) resolve them.
- **Always registered**: every `IRequestHandler<,>`/`IStreamQueryHandler<,>` (transient), `IRequestValidator<>` and `IDomainEventHandler<>` (scoped) of the assemblies, public or internal; `DomainEventDispatcher` as `IDomainEventDispatcher`; `ICommandScope`; Tracing, Logging, Metrics, Authorization (+ stream twin) and Validation behaviors.
- **`SharedKernel.Application.Pipeline.Caching`** — `app.WithCaching()` (`CachingPipelineExtensions`).
- **`SharedKernel.Application.Mediator.MediatR`** — `app.UseMediatR()`: `ISender` → internal `MediatRSender` (transient, so a nested send stays in the caller's scope); per scanned request type an internal `RequestEnvelope<TRequest,TResponse>` MediatR handler that runs `RequestPipeline<,>` and a keyed `RequestDispatcher` singleton.
- Application code injects `ISender` (`Send`, `CreateStream`) and `ICommandScope` (`IsActive`, `IsNested`, `OnCompleted`).

## Rules & Invariants

1. `SharedKernel.Application` stays MediatR-free, FluentValidation-free and free of `Microsoft.Extensions.*` implementation packages. A service's Application project references only it.
2. **MediatR is referenced only by `SharedKernel.Application.Mediator.MediatR`** (`DependencyGraphRulesTests.MediatR_IsReferencedOnlyByTheMediatorAdapter`) and is **pinned to 12.4.1, the last MIT-licensed release. Never bump it** without a recorded licensing decision. MediatR's own pipeline stays empty; MediatR `IRequestHandler`/`INotificationHandler` implementations are not discovered.
3. `.Pipeline` references only `SharedKernel.Application`, `.Execution`, `.Primitives`, `.Core`, `.Idempotency.Abstractions` and first-party `Microsoft.Extensions.*` — never a cache, Polly, hosting, FluentValidation or a mediator (`ApplicationPipelineRules`). The `Caching.Abstractions` dependency of the behaviors belongs only to `.Pipeline.Caching`.
4. Never redeclare `IRequestContext`, `IUnitOfWork`, `IAuditTrailWriter` or `IIdempotencyStore` here (`UnitOfWorkSeamRules.SharedContractsAreNotRedeclared`).
5. **No response envelope, ever.** `14.Presentation` maps a failed `Result` to RFC 9457; `11.Communication`'s REST client maps it back.
6. **One registration call.** A second `AddSharedKernelApplication` throws; so do no/`null` assemblies and two different handlers for one request type (a hand-registered handler is kept). `WithBehavior` rejects a type that is not an open generic `IPipelineBehavior<,>` and ignores duplicates.
7. **Seams are checked when the host starts** (`ValidateOnStart` on an internal options type), never at registration — one `OptionsValidationException` names every missing service. `ISender` is always required; a scanned request carrying `[RequirePermission]` makes `IRequestContext` required. A bare `ServiceProvider` runs no check (tests call `IStartupValidator.Validate()`).
8. **Canonical order is fixed**, whatever order `With…` is called in. Outermost first: Tracing → Logging → Metrics → [custom Observability] → Authorization → [custom] → Validation → [custom] → Query stage (e.g. `CachingBehavior`) → Command stage: `CommandScopeBehavior` → `IdempotencyBehavior` → `AuditingBehavior` (outer half) → `TransactionBehavior` → `AuditingCommitBehavior` (inner half) → [custom, e.g. `CacheInvalidationBehavior`] → handler. Authorization runs **before** Validation deliberately.
9. First-registered behavior of a stage is outermost, so its code after `next()` runs last; `CommandScopeBehavior` is first so post-commit callbacks see the commit complete. `RequestPipeline<,>` wraps behaviors in resolution order.
10. **Authorization is always on.** `[RequirePermission]` values in one attribute are alternatives; several attributes all apply. Unauthenticated → `Error.Unauthorized(ErrorCodes.Unauthorized.Default)`; missing permission → `Error.Forbidden(ErrorCodes.Forbidden.InsufficientPermission)`; the denial never names the permission. An unmarked request is not checked and `IRequestContext` is not resolved. `StreamAuthorizationBehavior` throws `error.ToException()` when enumeration starts.
11. **Validation is always on.** Validators run **sequentially** (one may hold a scoped `DbContext`); errors aggregate into `Error.Validation(errors)`; the field path goes in `Error.MessageArguments[ErrorArgumentNames.PropertyPath]`, never the rejected value.
12. **Only the outermost command commits and owns the idempotency key.** A nested command (`ICommandScope.IsNested`) skips idempotency; `TransactionBehavior` joins an active transaction (a nested failure marks it rollback-only). Handlers must be **re-runnable** — the unit of work may replay the delegate; callbacks queued by a discarded attempt are dropped.
13. `ICommandScope`: every dispatch pushes a frame; a nested command's callbacks merge into the parent on success and run once after the outermost command succeeds; failure discards the frame. `OnCompleted` outside a command throws. A callback that throws post-commit is logged (5110) and does not change the response.
14. **Idempotency keys are reserved per tenant and caller.** `IdempotencyKeyScope.Create` hands the store a SHA-256 digest (64 lowercase hex) over a frozen layout label, tenant, actor kind, subject, client, impersonator and raw key — each length-prefixed, absent ≠ empty; the session id is excluded. **The layout is a stored format — change it only with a new label.** Anonymous callers of a tenant share one scope, so keys must be unguessable.
15. Idempotency outcomes: `InProgress` → `Error.Conflict(ErrorCodes.Idempotency.InProgress)`; `Completed` → stored response replayed; `FingerprintMismatch` → `Error.Conflict(ErrorCodes.Idempotency.KeyReused)`; empty key → `Error.Validation(ErrorCodes.Idempotency.KeyRequired)`. Failure or exception → `ReleaseAsync`. Fingerprint = `IIdempotentRequest.Fingerprint` (trimmed) or SHA-256 of the request JSON. Never echo the key in an error.
16. **Auditing**: `Succeeded` is queued on `OnBeforeCommit` (inside the transaction); `Failed` is written after rollback. When recording a fault entry itself throws, log 5130 and rethrow the **original** exception.
17. **Cache scopes fail closed.** `CacheScope.Tenant` is the zero value. A `Tenant` query without a tenant or a `User` query without a caller skips the cache, runs the handler and logs 5201 — never a wider key. A command's `Scope` must match the queries it invalidates.
18. **Cache keys** go through `ITenantCacheKeyProvider`, never interpolation: `{service}:{QueryType}:{CacheKey}` (Global), `{service}:@{tenant}:{QueryType}:{CacheKey}` (Tenant), `…:u:{userId}` (User). Tags via `CacheKeyFormat.BuildTenantTag`, policies `CachePolicy.ForTenant`, so `RemoveTenantAsync` drops a tenant's cached queries. Commands name the query through `CacheKeyRef.For<TQuery>(key)`.
19. **`CachingBehavior` caches the `TValue`, not the `Result`**, via the context overload of `ICacheService.GetOrSetAsync`; a failed `Result` is `SkipCaching()`'d and never handed to concurrent waiters (each waiter runs the handler itself — pinned by `CachingBehaviorConcurrencyTests` against real FusionCache). Policy forced to `WithoutEagerRefresh()` and no factory timeouts (both would run the handler after the scope is disposed). `TValue` must round-trip through System.Text.Json.
20. `CacheInvalidationBehavior` builds and validates keys/tags **before** the handler and evicts in an `OnCompleted` callback (post-commit).
21. `DomainEventDispatcher` is serial: events in list order, handlers in registration order, exact runtime type only; an exception stops dispatch; no handler = skipped. External side effects go behind the outbox or `ICommandScope.OnCompleted`.
22. **Reflection** is allowed where clearest (AOT/trimming are not constraints here) but always cached once per closed type or done only at registration: `FailureResponse`/`ResponseOutcome` delegates, the dispatcher's per-event-type invoker, `RequirePermission` lookup (`PermissionRequirements<TRequest>`), the registration-time assembly scans. Nothing reflective per send.
23. Behaviors and `DomainEventDispatcher` are **internal** (`InternalsVisibleTo` the test project only); the public surface is the registration call, builder, options and pipelines. Every public change updates `PublicAPI.Unshipped.txt` and the package README.
24. `SK0040` requires a request with `[RequirePermission]` or `IIdempotentRequest` to return `Result`/`Result<T>`; `SK0041` flags two cacheable queries sharing a simple type name.

## Decisions

| Decision | Why |
| --- | --- |
| Kernel-owned `IRequest`/`ISender`/`IPipelineBehavior`; MediatR only as transport | Application projects stay MediatR-free; replacing MediatR is one new `ISender` adapter |
| Argument-less `RequestHandlerContinuation<T>` (`await next()`) | Behavior bodies keep MediatR's familiar shape |
| One `AddSharedKernelApplication` with builder opt-ins; mediator plugs in as `UseMediatR()` | No ordering traps; a seam registered after the call is still accepted; mirrors messaging's `UseRabbitMq()` |
| `[RequirePermission]` on the use case, always enforced | An opt-in authorization behavior could be forgotten; endpoints no longer repeat command permissions |
| Validation port `IRequestValidator<T>`; FluentValidation bridged in `01.Core` | The pipeline carries no validation library |
| Native `DomainEventDispatcher` over `IDomainEventHandler<T>` | No MediatR notification wrapper type |
| Purpose-keyed `IIdempotencyStore` shared with messaging | One store contract; the behavior owns lease and retention |
| Idempotency scoped per tenant + caller | Tenant-only keys let one caller replay another's response |
| Cache the value, not `Result` | Keeps `Result` free of serialization concerns; failures never cached |
| Declined: fire-and-forget dispatch, a resilience behavior, parallel domain-event dispatch, a generic dual-approval behavior, a response envelope | Retries belong to the unit of work and outbound clients; ordering and transactions need serial dispatch; approval is a domain aggregate |
| Options validated with DataAnnotations + `ValidateOnStart`, not `AddValidatedOptions` | Behavior options have no configuration section; no extra project reference |

## Logging

Block **5000–5999** (`LoggingEventIdRanges.Application`), 100-wide sub-blocks by package order; constants in internal `ApplicationBehaviorsLoggingEventIds` / `CachingBehaviorsLoggingEventIds`.

| Range | Package | Events |
| --- | --- | --- |
| 5000–5099 | `SharedKernel.Application` | none (no logging) |
| 5100–5199 | `.Pipeline` | 5100 handling (Debug) · 5101 handled · 5102 handled slow (Warning) · 5103 handled with failure · 5104 handling threw · 5110 `OnCompleted` callback threw · 5120 idempotency reservation lost at complete · 5130 audit write failed during exception |
| 5200–5299 | `.Pipeline.Caching` | 5200 query cache outcome · 5201 query scope unavailable · 5210 invalidation completed · 5211 eviction entry failed · 5212 invalidation scope unavailable |
| 5300–5399 | `.Mediator.MediatR` | none |

Tracing: `ActivitySource` `SharedKernel.Application` (process-lifetime static — the sanctioned exception). Metrics: `Meter` `SharedKernel.Application` from `IMeterFactory`, histogram `sharedkernel.application.request.duration` (unit `s`, `outcome` = success/failure/exception).

## Cross-Domain Couplings

| Contract | Owner | Seam |
| --- | --- | --- |
| `IRequestContext` | `01.Core` `SharedKernel.Execution` | Implemented by `13.ServiceDefaults`' `AddSharedKernelRequestContext()`; non-HTTP callers use `SystemRequestContext`/`AnonymousRequestContext`/`PropagatedRequestContext` |
| `IUnitOfWork`, `IAuditTrailWriter` | `SharedKernel.Execution` | Implemented by `06.Persistence` (`Persistence.EfCore`, `.EfCore.Auditing`) |
| `IIdempotencyStore` (`IdempotencyPurpose.Request`, keyed) | `18.Idempotency` | `AddRedisIdempotency` / `AddEfCoreIdempotency`; error codes in `01.Core`'s `ErrorCodes.Idempotency`, shared with `14.Presentation`'s header checks |
| `ICacheService`, `ITenantCacheKeyProvider`, `CachePolicy`, `CacheKeyFormat` | `02.Caching` Abstractions | Implemented by `Caching.FusionCache` (`AddSharedKernelCaching`) |
| `IDomainEventDispatcher` | `03.Domain` | Implemented here; called by `06.Persistence`'s `SharedKernelDbContext.SaveChangesAsync` before the physical save |
| `IRequestValidator<T>` | here | Bridged from FluentValidation by `01.Core`'s `AddFluentValidationRequestValidators` |
| `ISender` | here | Used by `17.Workflows`' `CommandActivity<>`, `19.Scheduling`'s `ScheduledCommandJob<>`, `14.Presentation` endpoints, `samples/OrderApi` |

Changing a request/handler/sender/behavior shape → also check `.Mediator.MediatR` envelopes, `CommandActivity<>`, `ScheduledCommandJob<>`, `16.Testing`'s `ApplicationPipelineTestHarness`. Changing stage order → `PipelineOrderTests` and `00.Governance`'s `ApplicationPipelineRules`.

## Testing

- Nested `*.Tests` projects for all four packages, all in the **Unit** lane.
- Prove order and cross-behavior interaction through a real `ServiceCollection` + `AddSharedKernelApplication` + `RequestPipeline<,>` (or `ISender` via `UseMediatR()`), never a hand-rolled continuation.
- Every behavior's success, `Result`-failure and exception path is tested; nested-command merge/discard; idempotency `Started`/`InProgress`/`Completed`/`FingerprintMismatch`, per-caller scoping and the key layout; authorization (unmarked unchecked, 401, 403, alternatives, multiple attributes, inheritance, stream); host-start seam check and double-call guard; `PipelineOrderAssertion` inspects registrations by type name.
- `.Mediator.MediatR`: discovery of commands/queries/streams/domain-event handlers, hand-registered handler kept, duplicates rejected, missing handler error.
- `.Pipeline.Caching` concurrency is proven against a real FusionCache, not only the fake.
- Consumer helpers (`16.Testing`): `SharedKernel.Application.Testing` (`ApplicationPipelineTestHarness.Build()` runs the kernel pipeline without a mediator), `SharedKernel.Idempotency.Testing` (`FakeIdempotencyStore`), `SharedKernel.Caching.Testing` (`FakeCacheService`, with the same per-key skip semantics), `SharedKernel.Persistence.Testing` (`AddFakeUnitOfWork()` with `TransientFailures` proves a handler re-runnable), `SharedKernel.Testing` (`TestRequestContext`).

## Known Limitations

- Idempotency reservations stored under an earlier key layout are not found after a layout-label change.
- Cached queries get no eager refresh and no factory timeout (both would outlive the request scope).
- A request type without `[RequirePermission]` is unchecked by design — a use case that needs a check must declare it.
- Anonymous callers of one tenant share an idempotency scope; only the fingerprint separates them.
- `ConsumerVerify` is not wired into the solution or CI; run it by hand against a packed feed.
