# SharedKernel.Application.Behaviors

Opt-in MediatR pipeline behaviors for Platform.SharedKernel microservices: Logging, Metrics, Tracing, Validation, Authorization, Caching, Resilience, Idempotency, Transaction, and Cache Invalidation for unary requests — plus five parallel behaviors for the streaming query pipeline, a fire-and-forget dispatcher, and a zero-prerequisite onboarding preset. Everything is composed in a fixed canonical order via `ApplicationBehaviorsBuilder`. References `SharedKernel.Application`, `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Caching.Abstractions`, `MediatR`, `FluentValidation`, and a Polly v8 resilience pipeline — never `06.Persistence`, `07.Messaging`, or `12.Security`.

This package ships **no MediatR registration of its own** — the consuming service already registers MediatR; `ApplicationBehaviorsBuilder` only appends behaviors to the already-registered pipeline.

## Canonical pipeline order (non-negotiable, twelve named slots)

```text
1.  LoggingBehavior            ← outermost; logs the full pipeline, including validation/auth/approval/resilience failures
2.  MetricsBehavior             ← records sharedkernel.application.request.duration, tagged with an outcome
3.  TracingBehavior             ← starts/disposes the request-traversal Activity regardless of outcome
4.  ValidationBehavior          ← throws ValidationException before any handler, auth, approval, cache, or retry work happens
5.  AuthorizationBehavior       ← commands AND queries (IAuthorizeRequest)
6.  DualApprovalBehavior        ← commands only (ICommandBase, IRequiresDualApproval); rejects before cache/mutation
7.  CachingBehavior              ← queries only (ICacheableQuery<TResponse>)
8.  ResilienceBehavior          ← commands only in practice (IRetryableRequest); wraps Idempotency + Auditing + Transaction
9.  IdempotentCommandBehavior   ← commands only (ICommandBase, IIdempotentRequest)
10. AuditingBehavior            ← commands only (ICommandBase, IAuditableRequest<TResponse>); writes just inside Transaction, before its commit
11. TransactionBehavior         ← commands only (ICommandBase); wraps handler + commit
12. CacheInvalidationBehavior   ← commands only (ICommandBase, IInvalidatesCache); innermost — after commit
```

`ApplicationBehaviorsBuilder.Build()` always registers behaviors so this canonical temporal order results, regardless of the order `.AddXBehavior()` was called in. Step 6 and step 7 are mutually exclusive with each other and with {8, 9, 10, 11, 12} at the request-type level — a query never satisfies `ICommandBase`, and a command never satisfies `ICacheableQuery<TResponse>` — so a single request only ever actually traverses one of {7} or {6, 8, 9, 10, 11, 12}. `DualApprovalBehavior` (step 6) is distinct from `AuthorizationBehavior` (step 5): Authorization answers "is this identity permitted to attempt this kind of action at all" (a static permission/policy question); DualApproval answers "has a second, distinct identity signed off on this exact pending instance of the action" (a per-instance maker-checker gate). The two are orthogonal and independently opt-in.

> **Physical DI registration order vs. the canonical step order above.** MediatR wraps `IPipelineBehavior<,>` instances so the *first-registered* behavior is outermost — its post-`next()` code runs *last*, after every later-registered (more-inner) behavior's post-`next()` code has already run. Two pairs of behaviors deliberately invert relative to the step numbers above, because each behavior's meaningful side effect happens *after* `next()` returns: `AuditingBehavior`'s write must observably complete *before* `TransactionBehavior`'s own commit executes ("just inside Transaction"), so `AuditingBehavior` is registered internally *after* `TransactionBehavior` — even though Auditing is step 10 and Transaction is step 11 above. `CacheInvalidationBehavior`'s eviction must observably follow *after* `TransactionBehavior`'s own commit, so `CacheInvalidationBehavior` is registered internally *before* `TransactionBehavior` — even though CacheInvalidation is step 12 and Transaction is step 11 above. `ApplicationBehaviorsBuilder` handles both internally — callers only ever see the twelve-step temporal order documented above, never the underlying registration-list order.

## The local-seam bridging pattern

`TransactionBehavior` (`IUnitOfWork`), `AuthorizationBehavior` (`IAuthorizationContext`), `IdempotentCommandBehavior` (`IIdempotencyKeyStore`), `DualApprovalBehavior` (`IDualApprovalStore`, plus `IAuthorizationContextIdentity` as an additive sibling capability on `IAuthorizationContext`), and `AuditingBehavior` (`IAuditTrailWriter`) each define a **minimal interface owned by this package** — never a direct reference to the "real" infrastructure (`06.Persistence`, `12.Security`, `07.Messaging`, `06.Persistence` respectively, none of which this package may reference). The consuming service bridges each local seam to its real implementation at the composition root. This is the same pattern applied five times, not five different patterns.

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

## Quick Start — full twelve-named-slot registration

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
    .AddDualApprovalBehavior()      // requires IAuthorizationContext (with IAuthorizationContextIdentity) AND IDualApprovalStore registered (see below)
    .AddCachingBehavior()           // requires SharedKernel.Caching.Abstractions.ICacheService registered
    .AddCacheInvalidationBehavior() // reuses the ICacheService guard above
    .AddResilienceBehavior()        // requires a ResiliencePipelineProvider registered
    .AddIdempotencyBehavior()       // requires IIdempotencyKeyStore registered (see below)
    .AddAuditingBehavior()          // requires SharedKernel.Application.Behaviors.IAuditTrailWriter registered (see below)
    .AddTransactionBehavior()       // requires SharedKernel.Application.Behaviors.IUnitOfWork registered (see below)
    .AddFireAndForgetDispatch(opts => opts.Capacity = 500)  // opt-in; see Fire-and-forget dispatch below
    .AddStreamingBehaviors()        // opt-in; see Streaming behaviors below
    .Build();
```

`Build()` throws `InvalidOperationException` at registration time if `.AddTransactionBehavior()`, `.AddCachingBehavior()`/`.AddCacheInvalidationBehavior()`, `.AddAuthorizationBehavior()`, `.AddDualApprovalBehavior()`, `.AddIdempotencyBehavior()`, `.AddAuditingBehavior()`, or `.AddResilienceBehavior()` was called without its required dependency already registered in `IServiceCollection`. `.AddDualApprovalBehavior()` is this package's first **two**-dependency guard — it throws a distinct message naming whichever of `IAuthorizationContext`/`IDualApprovalStore` is missing (or both).

## Bridging the local seams at the composition root

```csharp
// IUnitOfWork — bridge this package's minimal interface to 06.Persistence's concrete IUnitOfWork.
// Never reference 06.Persistence directly from inside SharedKernel.Application.Behaviors itself.
services.AddScoped<SharedKernel.Application.Behaviors.IUnitOfWork>(sp =>
    new EfUnitOfWorkAdapter(sp.GetRequiredService<SharedKernel.Persistence.Abstractions.IUnitOfWork>()));

// IAuthorizationContext — bridge to 12.Security's real IUserContext/ITenantProvider.
// Additionally implementing IAuthorizationContextIdentity lets DualApprovalBehavior resolve "who is
// calling right now" from the SAME bridge AuthorizationBehavior already uses — no second registration.
services.AddScoped<SharedKernel.Application.Behaviors.IAuthorizationContext>(sp =>
    new UserContextAuthorizationAdapter(sp.GetRequiredService<SharedKernel.Security.Abstractions.IUserContext>()));
// public sealed class UserContextAuthorizationAdapter : IAuthorizationContext, IAuthorizationContextIdentity
// {
//     public Task<string> GetCurrentIdentityAsync(CancellationToken ct) => Task.FromResult(_userContext.UserId);
//     // ... IAuthorizationContext members unchanged ...
// }

// IDualApprovalStore — the consuming service supplies its own implementation (e.g. a dedicated
// approvals table or a distributed cache key). Never a 06.Persistence/07.Messaging/12.Security
// reference from this package itself.
services.AddScoped<SharedKernel.Application.Behaviors.IDualApprovalStore, SqlDualApprovalStore>();

// IIdempotencyKeyStore — the consuming service supplies its own implementation
// (e.g. backed by the same distributed store 07.Messaging's IIdempotencyStore uses,
// or a dedicated table/cache key). Never a 07.Messaging reference from this package.
// Additionally implementing IIdempotencyResponseStore opts the store in to response replay (see below).
services.AddScoped<SharedKernel.Application.Behaviors.IIdempotencyKeyStore, RedisIdempotencyKeyStore>();

// IAuditTrailWriter — bridge this package's minimal local interface to 06.Persistence's real,
// richer IAuditTrailWriter (SharedKernel.Persistence.Abstractions). Never reference
// 06.Persistence directly from inside SharedKernel.Application.Behaviors itself.
services.AddScoped<SharedKernel.Application.Behaviors.Auditing.IAuditTrailWriter>(sp =>
    new PersistenceAuditTrailWriterAdapter(
        sp.GetRequiredService<SharedKernel.Persistence.Abstractions.IAuditTrailWriter>()));
// public sealed class PersistenceAuditTrailWriterAdapter(
//     SharedKernel.Persistence.Abstractions.IAuditTrailWriter realWriter)
//     : SharedKernel.Application.Behaviors.Auditing.IAuditTrailWriter
// {
//     public async Task RecordAsync(
//         SharedKernel.Application.Behaviors.Auditing.AuditEntry entry, CancellationToken ct = default)
//     {
//         // Actor identity, tenant identity, timestamp, and hash-chain linkage are all resolved
//         // internally by the real writer — this adapter only maps the smaller local shape onto the
//         // richer one, and discards the returned persisted record (this local seam never needs it back).
//         await realWriter.RecordAsync(new SharedKernel.Persistence.Abstractions.AuditEntry(
//             entry.Action, entry.ResourceType, entry.ResourceId,
//             entry.BeforeSnapshot, entry.AfterSnapshot, CorrelationId: null, entry.ApprovalId), ct);
//     }
// }
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

## Dual-control / maker-checker approval — `IRequiresDualApproval` + `IDualApprovalStore`

Maker-checker (four-eyes) controls — one identity initiates a privileged action, a distinct second identity must approve it before it executes — are a baseline requirement (SOX, banking regulation, PCI-DSS) for high-value operations: large payment approval, credit-limit changes, signing-key rotation, production configuration changes. `DualApprovalBehavior` is a pipeline-behavior authorization gate structurally identical to `AuthorizationBehavior`, gated by a new marker a high-risk command implements:

```csharp
// A high-risk command opts in via IRequiresDualApproval:
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
```

The retry-after-approval flow:

1. Alice dispatches `RotateSigningKeyCommand` → no approval recorded yet → `Result.Failure(Error.Forbidden(...))` — "awaiting a second approver." Handler never invoked.
2. Bob (a DISTINCT identity) dispatches `ApproveKeyRotationCommand` for the same `KeyId` → `IDualApprovalStore.RecordApprovalAsync("rotate-signing-key:{KeyId}", "bob", ct)`.
3. Alice dispatches `RotateSigningKeyCommand` a SECOND time (same command/key) → approval record found, recorded identity `"bob"` != initiator identity `"alice"` → `next()` is called → handler executes.
4. If Alice had instead recorded her OWN approval in step 2 (self-approval), step 3 would STILL short-circuit with `Result.Failure(Error.Forbidden(...))` — self-approval is structurally impossible: `DualApprovalBehavior` checks the recorded approver's identity against the resolved initiator's identity unconditionally whenever a record exists, so there is no code path that calls `next()` when the two identities match, regardless of how the record was created.

`DualApprovalBehavior` never calls `next()` unless BOTH "an approval record exists" AND "the recorded approver differs from the current initiator" hold — and it never throws for the awaiting-approval or self-approval cases, since both are foreseeable, expected outcomes (the same never-throw contract `AuthorizationBehavior` follows). It also never clears, consumes, or expires the approval record itself — that lifecycle (one-time-use invalidation, expiry, re-approval-on-command-change) is the consuming service's own approval-recording workflow's responsibility.

## Explicit audit-trail writes — `IAuditableRequest<TResponse>` + `IAuditTrailWriter`

Auditing is **never** fed automatically off `SaveChanges`/the existing EF Core `AuditInterceptor` — it is always an explicit, opt-in act. A command opts in by implementing `IAuditableRequest<TResponse>`, mirroring `ILoggableRequest<TResponse>`'s exact self-supplied-field shape:

```csharp
// A plain audited command — no dual-approval — records exactly one entry, ApprovalId always null:
public sealed record UpdateCustomerAddressCommand(Guid CustomerId, string NewAddress, string OldAddressSnapshot)
    : ICommand, IAuditableRequest<Result>
{
    public string Action => "customer.address.update";
    public string ResourceType => "Customer";
    public string ResourceId => CustomerId.ToString("D");
    public string? BeforeSnapshot => OldAddressSnapshot; // caller pre-serializes; this package never parses it
    public string? GetAfterSnapshot(Result response) => response.IsSuccess ? NewAddress : null;
}

// A command combining BOTH capabilities — the recorded audit entry's ApprovalId is automatically
// populated from IRequiresDualApproval.ApprovalKey, linking the two without either interface
// referencing the other:
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
// ApprovalId == "rotate-signing-key:{KeyId}" — AuditingBehavior populates it automatically via an
// `is IRequiresDualApproval` check, never a manual field the command author has to remember to set.
```

`AuditingBehavior<TRequest,TResponse>` calls `next()` first, then unconditionally calls `IAuditTrailWriter.RecordAsync(...)` for BOTH a `Result.Success` and a `Result.Failure` outcome — a rejected high-risk attempt is itself often the compliance-relevant event, not just a successful one — but never on a thrown exception (there is no response to project). It never catches an exception thrown by `RecordAsync` itself: a failed audit write propagates and blocks `TransactionBehavior`'s own commit (fail closed), consistent with this package's "log/audit failures are never silently swallowed" convention.

`IAuditTrailWriter` (`Auditing/IAuditTrailWriter.cs`) is a local seam deliberately smaller than the real, richer `06.Persistence.Abstractions.IAuditTrailWriter` — it never resolves actor identity, tenant identity, timestamp, or hash-chain linkage; the composition-root bridge maps this package's `AuditEntry` onto the real contract (see "Bridging the local seams" above). This is the fifth instance of the local-seam-bridging pattern in this package, not a sixth different one.

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
| `AuditingBehavior` | Constrained to `ICommandBase`; there is no streaming command shape to audit a mutation from. |
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

## Logging authoring standard and EventId allocation

Every production log statement in this package is authored via the source-generated `[LoggerMessage]` partial-method pattern (`Microsoft.Extensions.Logging.Abstractions`) — never a direct `ILogger.LogInformation/LogWarning/LogError(...)` extension-method call, and never a hand-written `LoggerMessage.Define<>()` delegate. This is the platform-wide logging standard (root `CLAUDE.md`'s "Logging Conventions" section), mechanically enforced by `00.Governance`'s `LoggingAuthoringStyleAnalyzer`/architecture tests.

Every `[LoggerMessage]` method carries an explicit `EventId` drawn from this domain's reserved block in `01.Core`'s `SharedKernel.Primitives.Logging.LoggingEventIdRanges` registry — never a bare numeric literal or compiler-auto-numbered id. `05.Application`'s domain range is `5000`-`5999`, sub-divided one 100-wide block per package in declaration order:

| Package | Range | Status |
| --- | --- | --- |
| `SharedKernel.Application` | `5000`-`5099` | Reserved, currently unused (zero `ILogger` call sites in that package) |
| `SharedKernel.Application.Behaviors` | `5100`-`5199` | Ten allocated EventIds (below); `5104`-`5109`/`5112`-`5119`/`5124`-`5129` reserved headroom |

| EventId | Method | File | Level | Message |
| --- | --- | --- | --- | --- |
| 5100 | `LogHandling` | `Logging/LoggingBehavior.cs` | Information | `Handling {RequestName}` |
| 5101 | `LogHandledSuccess` | `Logging/LoggingBehavior.cs` | Information | `Handled {RequestName} in {ElapsedMilliseconds}ms` |
| 5102 | `LogHandledFailure` | `Logging/LoggingBehavior.cs` | Warning | `Handled {RequestName} with failure in {ElapsedMilliseconds}ms` |
| 5103 | `LogHandlingFailed` | `Logging/LoggingBehavior.cs` | Error | `Handling {RequestName} failed after {ElapsedMilliseconds}ms` |
| 5110 | `LogChannelFull` | `FireAndForget/ChannelFireAndForgetDispatcher.cs` | Warning | `Fire-and-forget channel is full (capacity={Capacity}). Command {CommandType} was dropped and will not be executed.` |
| 5111 | `LogCommandFaulted` | `FireAndForget/FireAndForgetBackgroundConsumer.cs` | Error | `Fire-and-forget command {CommandType} faulted and its result was discarded.` |
| 5120 | `LogStreamStarted` | `Streaming/StreamLoggingBehavior.cs` | Information | `Streaming {RequestName} started.` |
| 5121 | `LogFirstItem` | `Streaming/StreamLoggingBehavior.cs` | Debug | `Streaming {RequestName} produced first item in {ElapsedMilliseconds}ms.` |
| 5122 | `LogStreamCompleted` | `Streaming/StreamLoggingBehavior.cs` | Information | `Streaming {RequestName} completed in {ElapsedMilliseconds}ms.` |
| 5123 | `LogStreamFaulted` | `Streaming/StreamLoggingBehavior.cs` | Warning | `Streaming {RequestName} faulted after {ElapsedMilliseconds}ms.` |

The constants live in `Shared/ApplicationBehaviorsLoggingEventIds.cs` (`internal static class`, ten `const int` fields, each computed as `LoggingEventIdRanges.Application + offset`) — never a bare numeric literal disconnected from the registry. Message templates use PascalCase named placeholders (`{RequestName}`, `{ElapsedMilliseconds}`) that match the call's named arguments; correlation/trace/tenant context is never passed as an explicit placeholder — it flows ambiently through the OpenTelemetry logging pipeline (`13.ServiceDefaults`). See `05.Application/CLAUDE.md`'s "Logging EventId Allocation" section for the full narrative (WO-041, P-253).

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [05.Application/CLAUDE.md](../CLAUDE.md) for the full interface contracts, hard violations, the reusable pipeline test harness, and AOT notes.
