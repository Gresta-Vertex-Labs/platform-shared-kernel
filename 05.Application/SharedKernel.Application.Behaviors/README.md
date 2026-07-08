# SharedKernel.Application.Behaviors

Opt-in MediatR pipeline behaviors for Platform.SharedKernel microservices: Logging, Metrics, Tracing, Validation, Authorization, Caching, Resilience, Idempotency, Transaction, and Cache Invalidation for unary requests — plus five parallel behaviors for the streaming query pipeline, a fire-and-forget dispatcher, and a zero-prerequisite onboarding preset. Everything is composed in a fixed canonical order via `ApplicationBehaviorsBuilder`. References `SharedKernel.Application`, `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Caching.Abstractions`, `MediatR`, `FluentValidation`, and a Polly v8 resilience pipeline — never `06.Persistence`, `07.Messaging`, or `12.Security`.

This package ships **no MediatR registration of its own** — the consuming service already registers MediatR; `ApplicationBehaviorsBuilder` only appends behaviors to the already-registered pipeline.

## Canonical pipeline order (non-negotiable, ten named slots)

```text
1.  LoggingBehavior            ← outermost; logs the full pipeline, including validation/auth/resilience failures
2.  MetricsBehavior             ← records sharedkernel.application.request.duration, tagged with an outcome
3.  TracingBehavior             ← starts/disposes the request-traversal Activity regardless of outcome
4.  ValidationBehavior          ← throws ValidationException before any handler, auth, cache, or retry work happens
5.  AuthorizationBehavior       ← commands AND queries (IAuthorizeRequest)
6.  CachingBehavior              ← queries only (ICacheableQuery<TResponse>)
7.  ResilienceBehavior          ← commands only in practice (IRetryableRequest); wraps Idempotency + Transaction
8.  IdempotentCommandBehavior   ← commands only (ICommandBase, IIdempotentRequest)
9.  TransactionBehavior         ← commands only (ICommandBase); wraps handler + commit
10. CacheInvalidationBehavior   ← commands only (ICommandBase, IInvalidatesCache); innermost — after commit
```

`ApplicationBehaviorsBuilder.Build()` always registers behaviors in this order, regardless of the order `.AddXBehavior()` was called in. Steps 6 and {7, 8, 9, 10} are mutually exclusive at the request-type level — a query never satisfies `ICommandBase`, and a command never satisfies `ICacheableQuery<TResponse>` — so a single request only ever actually traverses one of those two bands.

## The local-seam bridging pattern

`TransactionBehavior` (`IUnitOfWork`), `AuthorizationBehavior` (`IAuthorizationContext`), and `IdempotentCommandBehavior` (`IIdempotencyKeyStore`) each define a **minimal interface owned by this package** — never a direct reference to the "real" infrastructure (`06.Persistence`, `12.Security`, `07.Messaging` respectively, none of which this package may reference). The consuming service bridges each local seam to its real implementation at the composition root. This is the same pattern applied three times, not three different patterns.

## Install

```xml
<ProjectReference Include="..\SharedKernel.Application.Behaviors\SharedKernel.Application.Behaviors.csproj" />
```

Or, once published, reference the NuGet package `SharedKernel.Application.Behaviors`.

## Quick Start — the zero-prerequisite preset

For a new service with no infrastructure bridges wired up yet, `AddDefaultBehaviors()` registers exactly the four behaviors that carry no `Build()`-time missing-dependency guard (Logging, Metrics, Tracing, Validation):

```csharp
services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
services.AddValidatorsFromAssemblyContaining<Program>();

services
    .AddSharedKernelApplicationBehaviors()
    .AddDefaultBehaviors()
    .Build();
```

This is provably equivalent to calling `.AddLoggingBehavior().AddMetricsBehavior().AddTracingBehavior().AddValidationBehavior()` individually — it is a convenience preset, not a different code path. Every other behavior below requires its own registered local-seam/infrastructure bridge and remains a deliberate, individual opt-in; none may ever be folded into this preset.

## Quick Start — full ten-named-slot registration

```csharp
services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
services.AddValidatorsFromAssemblyContaining<Program>();   // FluentValidation's own scanning, not ours

services.AddSharedKernelApplication();   // 05.Application's domain-event bridge

// Opt-in pipeline behaviors — fixed execution order regardless of call order
services
    .AddSharedKernelApplicationBehaviors()
    .AddLoggingBehavior()
    .AddMetricsBehavior()
    .AddTracingBehavior()           // no missing-dependency guard (BCL ActivitySource)
    .AddValidationBehavior()
    .AddAuthorizationBehavior()     // requires IAuthorizationContext registered (see below)
    .AddCachingBehavior()           // requires SharedKernel.Caching.Abstractions.ICacheService registered
    .AddCacheInvalidationBehavior() // reuses the ICacheService guard above
    .AddResilienceBehavior()        // requires a ResiliencePipelineProvider registered
    .AddIdempotencyBehavior()       // requires IIdempotencyKeyStore registered (see below)
    .AddTransactionBehavior()       // requires SharedKernel.Application.Behaviors.IUnitOfWork registered (see below)
    .AddFireAndForgetDispatch(opts => opts.Capacity = 500)  // opt-in; see Fire-and-forget dispatch below
    .AddStreamingBehaviors()        // opt-in; see Streaming behaviors below
    .Build();
```

`Build()` throws `InvalidOperationException` at registration time if `.AddTransactionBehavior()`, `.AddCachingBehavior()`/`.AddCacheInvalidationBehavior()`, `.AddAuthorizationBehavior()`, `.AddIdempotencyBehavior()`, or `.AddResilienceBehavior()` was called without its required dependency already registered in `IServiceCollection`.

## Bridging the local seams at the composition root

```csharp
// IUnitOfWork — bridge this package's minimal interface to 06.Persistence's concrete IUnitOfWork.
// Never reference 06.Persistence directly from inside SharedKernel.Application.Behaviors itself.
services.AddScoped<SharedKernel.Application.Behaviors.IUnitOfWork>(sp =>
    new EfUnitOfWorkAdapter(sp.GetRequiredService<SharedKernel.Persistence.Abstractions.IUnitOfWork>()));

// IAuthorizationContext — bridge to 12.Security's real IUserContext/ITenantProvider.
services.AddScoped<SharedKernel.Application.Behaviors.IAuthorizationContext>(sp =>
    new UserContextAuthorizationAdapter(sp.GetRequiredService<SharedKernel.Security.Abstractions.IUserContext>()));

// IIdempotencyKeyStore — the consuming service supplies its own implementation
// (e.g. backed by the same distributed store 07.Messaging's IIdempotencyStore uses,
// or a dedicated table/cache key). Never a 07.Messaging reference from this package.
// Additionally implementing IIdempotencyResponseStore opts the store in to response replay (see below).
services.AddScoped<SharedKernel.Application.Behaviors.IIdempotencyKeyStore, RedisIdempotencyKeyStore>();
```

## Declaring requests that opt into a behavior

```csharp
// A cacheable query — the read-side half of the caching pair
public sealed record GetOrderByIdQuery(Guid OrderId)
    : IQuery<OrderDto>, ICacheableQuery<Result<OrderDto>>
{
    public CachePolicy CachePolicy => CachePolicy.Default;
    public string CacheKey => $"orders:{OrderId:D}";
}

// A command implementing IAuthorizeRequest's multi-requirement (AllOf/AnyOf) shape and IIdempotentRequest
public sealed record PlaceOrderCommand(string IdempotencyKey, Guid CustomerId, decimal Total)
    : ICommand<Guid>, IAuthorizeRequest, IIdempotentRequest
{
    // AllOf: caller must have BOTH "orders:create" AND "orders:write" to proceed.
    public IReadOnlyCollection<string> AllOfRequirements => ["orders:create", "orders:write"];
    // AnyOf: at least one of these must pass (OR composition) — empty here, so it is a no-op.
    public IReadOnlyCollection<string> AnyOfRequirements => [];
}
```

Requests that do not implement `ICacheableQuery<TResponse>` / `IAuthorizeRequest` / `IIdempotentRequest` / etc. simply never resolve the corresponding behavior into their pipeline — this is a DI-level fact, not a runtime branch.

## Read/write caching pairing — `ICacheableQuery<TResponse>` + `IInvalidatesCache`

`ICacheableQuery<TResponse>` (queries) and `IInvalidatesCache` (commands) are the two halves of one caching story. A command that mutates the same data a cached query reads should invalidate that query's cache key on success:

```csharp
public sealed record UpdateOrderTotalCommand(Guid OrderId, decimal NewTotal)
    : ICommand, IInvalidatesCache
{
    // Same key format GetOrderByIdQuery.CacheKey above computes for the same OrderId.
    public IReadOnlyCollection<string> CacheKeysToInvalidate => [$"orders:{OrderId:D}"];
}
```

`CacheInvalidationBehavior` runs innermost, only after `TransactionBehavior` confirms a commit — by default it evicts on any non-throwing outcome (including `Result.Failure`, mirroring `TransactionBehavior`'s "don't inspect `Result.IsSuccess`" stance); set `InvalidateOnlyOnSuccess = true` on its options to additionally gate eviction on `IHasSuccessFlag.IsSuccess`. A type can never implement both `ICacheableQuery<TResponse>` and `IInvalidatesCache` — queries don't mutate, commands aren't cached.

## Combining retry with mutation safely — `IRetryableRequest` + `IIdempotentRequest`

`ResilienceBehavior` wraps `IdempotentCommandBehavior` and `TransactionBehavior` in the pipeline specifically so a Polly-driven retry re-runs the *full* duplicate-check-then-commit unit on every attempt, never a bare second commit. This resolves the retry-after-partial-commit hazard — but only if the command declares both markers:

```csharp
public sealed record UpdateOrderTotalCommand(string IdempotencyKey, Guid OrderId, decimal NewTotal)
    : ICommand, IIdempotentRequest, IRetryableRequest, IInvalidatesCache
{
    public IReadOnlyCollection<string> CacheKeysToInvalidate => [$"orders:{OrderId:D}"];
}
```

A command implementing `IRetryableRequest` **without** also implementing `IIdempotentRequest` is a documented misuse — there is no compile-time way to enforce "interface A implies interface B" across two independent marker interfaces in C#, so code review must catch this; the compiler will not. Queries may implement `IRetryableRequest` alone (a read is always safe to retry).

## Fire-and-forget dispatch

```csharp
services
    .AddSharedKernelApplicationBehaviors()
    .AddLoggingBehavior()
    // ...
    .AddFireAndForgetDispatch(opts =>
    {
        opts.Capacity = 1000;                                   // bounded channel capacity (default 1000)
        opts.RejectionPolicy = FireAndForgetRejectionPolicy.DropAndLog;  // or .Block
    })
    .Build();
```

This registers `IFireAndForgetDispatcher` (backed by a bounded `Channel<IFireAndForgetCommand>`), the `FireAndForgetBackgroundConsumer` hosted service that dequeues and executes commands via a fresh `IServiceScope` per item, and `FireAndForgetGuardBehavior<,>`, which rejects any direct `ISender.Send(IFireAndForgetCommand)` call with a descriptive `InvalidOperationException` — always dispatch via `IFireAndForgetDispatcher.EnqueueAsync(...)` instead (declared in `SharedKernel.Application` — see that package's README).

## Streaming behaviors

`IStreamQuery<TResponse>` (declared in `SharedKernel.Application`) uses MediatR's separate `IStreamRequest<TResponse>` hierarchy, so none of the ten unary behaviors above apply to it. Five dedicated `IStreamPipelineBehavior<,>` implementations cover the streaming path instead:

```csharp
services
    .AddSharedKernelApplicationBehaviors()
    .AddStreamingBehaviors()   // registers Logging -> Metrics -> Tracing -> Validation -> Authorization, in that order
    .Build();
```

`AddStreamingBehaviors()` is independent of `AddDefaultBehaviors()`/the unary `.AddXBehavior()` calls — call it alongside them if a service needs both unary and streaming request handling. It guards on `IAuthorizationContext` being registered if the streaming Authorization behavior is to function against `IAuthorizeRequest`-marked stream queries.

### Behaviors deliberately not offered for streaming

| Unary behavior | Why it does not apply to `IStreamQuery<TResponse>` |
| --- | --- |
| `TransactionBehavior` | Constrained to `ICommandBase`; streaming queries are read-only by contract and never implement it. |
| `CachingBehavior` | Materialising an `IAsyncEnumerable<TResponse>` to cache it defeats the constant-memory streaming guarantee. |
| `CacheInvalidationBehavior` | Constrained to `ICommandBase`; there is no streaming command shape to invalidate a cache from. |
| `IdempotentCommandBehavior` | Constrained to `ICommandBase`; duplicate-submission protection has no meaning for a read-only stream. |
| `ResilienceBehavior` | Retrying a partially-consumed stream has undefined semantics — the stream position cannot be rewound. |

## Idempotency response replay (opt-in)

A store implementing `IIdempotencyKeyStore` may additionally implement `IIdempotencyResponseStore` to opt in to replaying the original response on a genuine duplicate submission, instead of always returning a fresh `Error.Conflict`:

```csharp
public sealed class RedisIdempotencyKeyStore : IIdempotencyKeyStore, IIdempotencyResponseStore
{
    // HasProcessedAsync / MarkProcessedAsync — required, as before.
    // TryGetStoredResponseAsync / StoreResponseAsync — additive; enables replay.
}
```

`IdempotentCommandBehavior` detects the extra capability via a plain `is IIdempotencyResponseStore` check on the already-injected `IIdempotencyKeyStore` instance — no second DI registration needed. When replay is supported and a stored response exists for the duplicate key, the *original* outcome (success or failure) is returned verbatim. A store implementing only `IIdempotencyKeyStore` continues to behave exactly as before (`Error.Conflict` on every duplicate) — this is purely additive and backward-compatible.

## Opt-in structured request/response payload logging — `ILoggableRequest<TResponse>`

`LoggingBehavior<,>` never logs request or response payloads by default — command/query parameters routinely carry PII, and reflecting over arbitrary properties to redact them is exactly the kind of platform-wide reflection this domain forbids. A request that needs specific, hand-picked fields in its log lines opts in explicitly by implementing `ILoggableRequest<TResponse>`:

```csharp
public sealed record PlaceOrderCommand(Guid CustomerId, string CreditCardNumber, decimal Total)
    : ICommand<Guid>, ILoggableRequest<Result<Guid>>
{
    // Only the fields YOU decide are safe to log — never the whole request.
    public IReadOnlyDictionary<string, object?> LoggableRequestFields => new Dictionary<string, object?>
    {
        ["CustomerId"] = CustomerId,
        ["Total"] = Total,
        // CreditCardNumber is deliberately omitted — never log secrets, PII, or credentials here.
    };

    public IReadOnlyDictionary<string, object?>? GetLoggableResponseFields(Result<Guid> response) =>
        response.IsSuccess
            ? new Dictionary<string, object?> { ["OrderId"] = response.Value }
            : null; // opt out of response-side logging on failure — nothing useful to attach here
}
```

`LoggingBehavior<,>` attaches `LoggableRequestFields` to the entry-log line (and the fault-path `Error` log, if the handler throws) via `ILogger.BeginScope`, and attaches `GetLoggableResponseFields(response)` to the completion-log line — but only when `next()` returns normally, never on a thrown exception. Both are skipped entirely when the returned dictionary is null or empty, so an opted-in request with nothing to say for a given call costs nothing extra.

> **Warning — never include PII, secrets, or credentials in the returned field set.** `LoggableRequestFields`/`GetLoggableResponseFields` are logged verbatim to whatever sink `ILogger<TRequest>` is wired to (console, file, a centralized log aggregator). Passwords, tokens, card numbers, government IDs, and full free-text user input must never appear in these dictionaries — log only identifiers (`OrderId`, `CustomerId`) and coarse-grained outcome fields (`Total`, `Status`). A request that does not implement `ILoggableRequest<TResponse>` is unaffected — its logging behavior is byte-for-byte identical to a platform without this capability.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [05.Application/CLAUDE.md](../CLAUDE.md) for the full interface contracts, hard violations, the reusable pipeline test harness, and AOT notes.
