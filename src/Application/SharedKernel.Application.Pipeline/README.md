# SharedKernel.Application.Pipeline

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Host](https://img.shields.io/badge/tier-Host-d73a49)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **The application layer of a service in one call — handlers, validators and domain-event handlers found, and the
> cross-cutting half of every request (tracing, logging, metrics, authorization, validation, idempotency,
> transactions, auditing) composed in one fixed order, so a handler only decides business outcomes.**

| You get | So that |
| --- | --- |
| One registration call, `AddSharedKernelApplication` | No ordering traps: seams may be registered before or after it, and a missing one fails the host start naming every one |
| A fixed five-stage pipeline | Two services compose the same request the same way, and the order can't drift |
| Authorization always on, declared with `[RequirePermission]` on the use case | A marked request is never handled unchecked, on any path (HTTP, message, job, workflow) |
| Expected failures as `Result` values | A denial or an invalid field returns an error the caller handles — exceptions stay for genuine faults |
| Commit only on success, only once | A handler that returns a failure persists nothing, and a nested command joins the outer transaction |
| `ICommandScope.OnCompleted` | Work that must follow a commit — publish, evict, notify — runs after it, or not at all |
| Idempotency per tenant and caller, with a request fingerprint | A retried submission replays its original response; another caller's key never replays yours; the same key with a different body is rejected |
| Error-aware telemetry | Spans, metrics and logs all carry the error type and code, so a dashboard can alert on *what* failed |
| `WithBehavior(type, stage)` | Your own behavior lands in the canonical order instead of wherever it was registered |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Configuration](#configuration)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)

## Install

```xml
<PackageReference Include="SharedKernel.Application.Pipeline" />
<PackageReference Include="SharedKernel.Application.Mediator.MediatR" />   <!-- the ISender transport -->
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Host — reference it from your **Api** / **Worker** project; the Application project needs only [`SharedKernel.Application`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Application/SharedKernel.Application/README.md) |
| Depends on | `SharedKernel.Application`, `SharedKernel.Execution`, `SharedKernel.Primitives`, `SharedKernel.Idempotency.Abstractions`, first-party `Microsoft.Extensions.*`; no mediator, FluentValidation, cache, Polly or hosting |
| Namespaces | `SharedKernel.Application.Pipeline` |

The transport is [`SharedKernel.Application.Mediator.MediatR`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Application/SharedKernel.Application.Mediator.MediatR/README.md)
(`UseMediatR()`); query caching is the separate
[`SharedKernel.Application.Pipeline.Caching`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Application/SharedKernel.Application.Pipeline.Caching/README.md) (`WithCaching()`).

## Quick start

```csharp
using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Pipeline;
using SharedKernel.Validation.FluentValidation;

// Handlers, validators, domain-event handlers of the assembly; tracing, logging, metrics, authorization and
// validation; MediatR as the transport.
builder.Services.AddSharedKernelApplication(typeof(PlaceOrderCommand).Assembly, app => app.UseMediatR());

// Optional: FluentValidation's IValidator<T>s of the assembly, as IRequestValidator<T>.
builder.Services.AddFluentValidationRequestValidators(typeof(PlaceOrderCommand).Assembly);
```

The rest is a deliberate opt-in, because each one needs a seam you have to provide:

```csharp
builder.Services.AddSharedKernelApplication(typeof(PlaceOrderCommand).Assembly, app => app
    .UseMediatR()
    .WithIdempotency()     // needs IIdempotencyStore (Request) + IRequestContext
    .WithTransactions()    // needs IUnitOfWork
    .WithAuditing());      // needs IAuditTrailWriter

// The seams, before or after the call above:
builder.Services.AddSharedKernelRequestContext();                        // IRequestContext (SharedKernel.ServiceDefaults.Security)
builder.AddSharedKernelPostgres<OrderDbContext>("orders", p => p.UseAuditTrail()); // IUnitOfWork + IAuditTrailWriter (SharedKernel.Persistence.EfCore)
builder.Services.AddRedisIdempotency(p => p.ForRequests());                 // IIdempotencyStore for requests (SharedKernel.Idempotency.Redis)
```

A missing seam fails the **host start** with one `OptionsValidationException` naming every missing service and
what needs it — never the first request. `ISender` is always required (a mediator must be plugged in), and so is
`IRequestContext` as soon as a request type of the assemblies carries `[RequirePermission]`. Calling
`AddSharedKernelApplication` a second time throws: pass every assembly and option to the one call.

## How it works

### The pipeline order

Five stages, always in this order, regardless of the order you called the `With…` methods:

| # | Stage | Behavior | Applies to | Registered |
| --- | --- | --- | --- | --- |
| 1 | Observability | tracing | every request | always |
| 2 | Observability | logging | every request | always |
| 3 | Observability | metrics | every request | always |
| 4 | Authorization | authorization | requests carrying `[RequirePermission]` (streaming queries too) | always |
| 5 | Validation | validation | every request with a registered `IRequestValidator<T>` | always |
| 6 | Query | *(yours, or the caching package's)* | queries | `WithBehavior(…, PipelineStage.Query)` / `WithCaching()` |
| 7 | Command | command scope | commands | automatic when any command behavior is on |
| 8 | Command | idempotency | commands implementing `IIdempotentRequest` | `WithIdempotency()` |
| 9 | Command | auditing (outer half: failures, after rollback) | commands implementing `IAuditableRequest<T>` | `WithAuditing()` |
| 10 | Command | transaction (the rest runs inside `ExecuteInTransactionAsync`) | commands | `WithTransactions()` |
| 10b | Command | inner auditing half (`Succeeded`, queued on `OnBeforeCommit`) | commands implementing `IAuditableRequest<T>` | `WithAuditing()` |
| 11 | Command | *(yours, or cache invalidation)* | commands | `WithBehavior(…, PipelineStage.Command)` / `WithCaching()` |

A streaming query (`IStreamQuery<T>`) has its own, shorter chain: stream observability (span, metrics, log), outermost
so a denial is observed too, then the `[RequirePermission]` check, then the handler.

The behavior classes are internal: the public surface is the registration call, its builder, the options and
`RequestPipeline<,>`.

Tracing is outermost so every log line below it carries the trace id. **Authorization precedes validation**
deliberately: a caller who may not perform an operation should not learn its validation rules, and validators
often hit the database.

For one successful command, read the table down as "before the handler" and back up as "after it": the `Succeeded`
audit entry is written **inside** the transaction (queued on `OnBeforeCommit`), a `Failed` one after the rollback, the
idempotency key is completed **after** the commit, and `ICommandScope.OnCompleted` callbacks run last of all.

### Which markers do I implement?

Tracing, logging, metrics and validation apply to every request — a request declares nothing. The other four are
opt-in **per request**, through an attribute or a marker it implements:

| The request… | Declares | Behavior |
| --- | --- | --- |
| needs a permission | `[RequirePermission(...)]` — one attribute's values are alternatives, several attributes all apply | Authorization |
| is a command that must not run twice | `IIdempotentRequest` (`IdempotencyKey`, optional `Fingerprint`) | Idempotency |
| is a command whose attempt must be recorded | `IAuditableRequest<TResponse>` (`Action`, `ResourceType`, `ResourceId`, snapshots) | Auditing |
| should put some of its fields in the logs | `ILoggableRequest<TResponse>` — the fields you choose, never a reflection walk | Logging |

They compose: a refund command can be all four at once. Caching markers live in the
[caching package](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Application/SharedKernel.Application.Pipeline.Caching/README.md).

### What each behavior does

#### `TracingBehavior`

Starts the span `application send` (constant name, low cardinality), tagged `application.request_type` (full name),
`application.request_kind` (`command`/`query`/`request`) and `outcome` (`success`, `failure`, `exception`,
`canceled`). Failures go through the shared recorder: a failed `Result` sets status `Error` (the error code) with
`error.type`/`error.code`; an exception sets `Error` described by its **full name** (never its message), adds the
exception event and rethrows; a cancellation is not an error. No listener registered means no allocation.

#### `LoggingBehavior`

`Debug` on entry, then one completion line: `Debug` on success, `Warning` when the elapsed time crosses
`SlowRequestThreshold` (500 ms by default), `Warning` with the error type and code on a failed `Result`, `Debug`
on a cancellation and `Error` with the exception on any other throw. The logger category is the behavior
(`SharedKernel.Application.Pipeline.Logging.LoggingBehavior`), never the request type: filter by that category, or
by the `{RequestType}` placeholder in the message. Payloads are never logged unless the request opts in by implementing
`ILoggableRequest<TResponse>`, which supplies exactly the fields it considers safe:

```csharp
public sealed record PlaceOrderCommand(string Customer, string CardNumber, decimal Amount)
    : ICommand<Guid>, ILoggableRequest<Result<Guid>>
{
    public IReadOnlyDictionary<string, object?> LoggableRequestFields =>
        new Dictionary<string, object?> { ["Customer"] = Customer, ["Amount"] = Amount };   // never CardNumber

    public IReadOnlyDictionary<string, object?>? GetLoggableResponseFields(Result<Guid> response) =>
        response.IsSuccess ? new Dictionary<string, object?> { ["OrderId"] = response.Value } : null;
}
```

#### `MetricsBehavior`

Records one histogram measurement per request — `sharedkernel.application.request.duration`, **in seconds**,
from a versioned `IMeterFactory` meter named `SharedKernel.Application`. Tags: `application.request_type`,
`application.request_kind`, `outcome` and, for a failure or an exception (never a cancellation), `error.type` (the
`ErrorType` name or exception full name, the text the span carries). Recorded in a `finally`, so a throw is measured.

#### Authorization

Always registered. Applies to requests carrying `[RequirePermission]` (`SharedKernel.Application.Authorization`):

```csharp
[RequirePermission("orders.refund")]
public sealed record RefundOrderCommand(Guid OrderId, decimal Amount) : ICommand;

[RequirePermission("invoices.write", "invoices.admin")]   // either one
[RequirePermission("customers.read")]                     // and this one
public sealed record IssueInvoiceCommand(Guid CustomerId) : ICommand<Guid>;
```

| Situation | Result |
| --- | --- |
| The request carries no `[RequirePermission]` | not checked; `IRequestContext` is not even resolved |
| `IRequestContext.IsAuthenticated` is false | `Error.Unauthorized(ErrorCodes.Unauthorized.Default)` (`unauthorized.default`) → 401 |
| No value of one attribute is held | `Error.Forbidden(ErrorCodes.Forbidden.InsufficientPermission)` → 403 |
| No `IRequestContext` is registered | the host start fails (the registration saw the attribute); a type from an unscanned assembly throws `InvalidOperationException` when sent |

The denial is returned as a failed `Result`, never thrown, and never echoes permission names. A streaming query with
the attribute is checked before its handler runs; its denial is thrown as `UnauthorizedException`/`ForbiddenException`
when enumeration starts (a stream has no `Result`). The attribute is enforced on every path a request is sent from —
HTTP, a message consumer, a scheduled job, a workflow activity — so an endpoint that sends the command does not repeat
the permission (`SharedKernel.Presentation.Core`'s `[RequireEndpointPermission]` is for endpoints that send no command).

#### `ValidationBehavior`

Runs every registered `IRequestValidator<TRequest>` **sequentially** (two async validators sharing a scoped
`DbContext` would throw in parallel) and returns one `Error.Validation(errors)` — code `validation.failed`, each field
failure in `Error.Details` — without throwing. `SharedKernel.Presentation.WebApi` renders it as a 400 with a per-field
`errors` map. FluentValidation validators are bridged by `AddFluentValidationRequestValidators(assembly)`
(`SharedKernel.Validation.FluentValidation`), keeping each failure's code and property path.

#### `IdempotencyBehavior`

Applies to commands implementing `IIdempotentRequest` (opted in with `WithIdempotency()`). It reserves the key through the `IIdempotencyStore` registered for `IdempotencyPurpose.Request`
before the handler runs, and settles it afterwards:

| `TryBeginAsync` returns | The behavior |
| --- | --- |
| `Started` | Runs the handler. On success: `CompleteAsync` with the serialized response, retained for `IdempotencyBehaviorOptions.RetentionWindow` (the reservation itself holds for `LeaseDuration`). On a failed `Result`: `ReleaseAsync`, so the caller may retry with the same key. On an exception: `ReleaseAsync`, then rethrows |
| `Completed` | Returns the stored response — the original outcome, not a fresh conflict |
| `InProgress` | `Error.Conflict(ErrorCodes.Idempotency.InProgress)` (`idempotency.in_progress`) |
| `FingerprintMismatch` | `Error.Conflict(ErrorCodes.Idempotency.KeyReused)` (`idempotency.key_reused`) |

**The store call is the fault boundary.** The package cannot reference Redis or PostgreSQL to classify their
exceptions, and `IIdempotencyStore` documents that an unreachable store throws, so:

| The store… | The behavior |
| --- | --- |
| throws from `TryBeginAsync` | Fails closed: `Error.Unavailable(ErrorCodes.Idempotency.StoreUnavailable)` (`idempotency.store_unavailable`, 503) and the handler does not run. The exception is logged (5121) and added to the request span; no exception text reaches the caller |
| throws a `SharedKernelException` from `TryBeginAsync` | Returns that exception's own `Error`, unchanged |
| throws because the caller cancelled | The cancellation propagates |
| throws from `CompleteAsync` | Logs it (5122) and returns the handler's response; the reservation expires on its lease |
| throws from `ReleaseAsync` | Logs it (5123) and keeps the handler's own outcome or exception; the reservation expires on its lease |

A defect inside a store reads as Unavailable too (the exception is on the log and the span). A fault inside the
handler is never caught here.

An empty key is `Error.Validation(ErrorCodes.Idempotency.KeyRequired)` (`idempotency.key_required`), the code
`SharedKernel.Presentation.WebApi` answers a missing `Idempotency-Key` header with — both take it from `SharedKernel.Primitives`'
`ErrorCodes.Idempotency`.

**A key belongs to one caller of one tenant.** The store never sees the raw key but a 64-character SHA-256 digest of
the tenant, the caller (`IRequestContext`: actor kind, user id, client id, impersonator — not the session) and the key.
Two callers using the same key each run once; the same caller retrying replays. Anonymous callers of a tenant share
one scope, separated only by the fingerprint — so keys must be unguessable (a UUID per operation), and an anonymous
command's response must carry nothing only its sender may see.

The fingerprint defaults to a SHA-256 hash of the serialized request. **Set `Fingerprint` explicitly on any
command you expect to be retried across a deploy**, because the automatic hash changes the moment the command
type gains a property, and never matches itself when the command carries a timestamp or a generated id:

```csharp
public sealed record TransferMoney(Guid From, Guid To, decimal Amount, DateTimeOffset RequestedAt, string IdempotencyKey)
    : ICommand, IIdempotentRequest
{
    public string? Fingerprint => $"{From}:{To}:{Amount}";   // RequestedAt deliberately excluded
}
```

A nested command skips this behavior entirely — the outermost command owns the key.

#### `TransactionBehavior`

Runs the rest of the pipeline and the handler **inside** `IUnitOfWork.ExecuteInTransactionAsync`
(`SharedKernel.Execution.Transactions`): the unit of work saves what was staged, runs the `OnBeforeCommit`
callbacks and commits — **only for the outermost command, and only when the response is successful**. A failed
`Result` rolls back, so a handler that mutated an aggregate before deciding to fail leaves no trace; an
exception rolls back and propagates.

- **Handlers must be re-runnable.** Under a retrying execution strategy (on by default in `SharedKernel.Persistence.EfCore`) a
  transient failure replays the whole delegate: the unit of work discards what the failed attempt staged and the
  handler runs again. Load what you need through repositories inside the handler; keep HTTP calls and messages
  out of it — queue them with `ICommandScope.OnCompleted`, which runs after the commit. Callbacks queued by a
  discarded attempt are dropped.
- **A command sent while a transaction is active joins it.** If the joined command fails, the transaction becomes
  rollback-only: the outer command commits nothing and, if it would have succeeded, gets
  `TransactionRolledBackException`.
- **An ambiguous commit is not retried.** `CommitOutcomeUnknownException` means the commit may or may not have
  happened — re-read (or rely on the idempotency key) before repeating.
- **It is traced.** The outermost transaction and a joined one each get an `application transaction` span tagged
  `application.transaction_joined` and `application.attempts` (how often the unit of work ran the delegate). A
  rollback caused by a failed `Result` is logged at Warning (5160), one caused by an exception at Error (5161), and a
  replay at Warning (5162); a joined command leaves the rollback log to the command that owns the transaction.

#### `AuditingBehavior`

Applies to commands implementing `IAuditableRequest<TResponse>`, which supplies its own opaque,
pre-serialized snapshots — this package never reflects over your command:

```csharp
public sealed record UpdateLimitCommand(Guid CustomerId, decimal NewLimit, string? BeforeSnapshot)
    : ICommand, IAuditableRequest<Result>
{
    public string Action => "customer.limit.update";
    public string ResourceType => "Customer";
    public string ResourceId => CustomerId.ToString("D");
    public string? GetAfterSnapshot(Result response) => response.IsSuccess ? $"{{\"limit\":{NewLimit}}}" : null;
}
```

An entry is written for **all three outcomes** — success, business failure, and a handler that throws —
because a rejected high-risk attempt is usually the compliance-relevant event. `AuditEntry.Outcome` and
`ErrorCode` carry which it was: the error code on a business failure, the exception type name on a fault.

`WithAuditing()` registers two halves around `TransactionBehavior`. The inner half queues the
`Succeeded` entry on `IUnitOfWork.OnBeforeCommit`, so it is written in the same transaction as the change it
attests to and commits or rolls back with it (a failed success write rolls the change back). The outer half,
outside the transaction, records every failure — a failed `Result`, an exception, or the commit itself failing —
**after** the rollback, on the writer's own connection. If the audit write throws while handling an exception,
the **original** exception still propagates and the audit failure is logged. Actor, tenant, time and
correlation are never in `AuditEntry`: the writer resolves them from `IRequestContext`.

### What happens when something fails

| The request ended… | Caller gets | Commit | Audit entry | Idempotency key |
| --- | --- | --- | --- | --- |
| with invalid input | `Error.Validation` with every field; the handler never ran | no | no | released |
| denied | `Error.Unauthorized` (401) or `Error.Forbidden` (403) | no | no | released |
| with a failed `Result` from the handler | the failure, unchanged | no | written | released |
| with a handler exception | the exception, rethrown | no | written | released |
| because the idempotency store is unreachable | `Error.Unavailable` (`idempotency.store_unavailable`, 503); the handler never ran | no | no | not reserved |
| successfully | the value | yes | written | completed, then `OnCompleted` callbacks |

**Only a successful outcome persists anything** — except the audit trail, which records the attempt either way.

### Nested commands and `ICommandScope`

A handler that sends another command creates a nested command in the same DI scope. `ICommandScope` tracks
that so the inner one does not open a second transaction or consume its own idempotency key:

| | Outermost command | Nested command |
| --- | --- | --- |
| `TransactionBehavior` | Commits | Joins the outer transaction — no commit of its own |
| `IdempotencyBehavior` | Reserves and settles the key | Skipped entirely |
| `AuditingBehavior` | Writes an entry | Writes its own entry |

Handlers can use the same scope to queue work that must happen **after** the commit:

```csharp
public sealed class PlaceOrderHandler(IOrderRepository repository, ICommandScope scope, IEventPublisher events)
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var order = Order.Place(command.Customer, command.Amount);
        await repository.AddAsync(order, cancellationToken);

        scope.OnCompleted(ct => events.PublishAsync(new OrderPlaced(order.Id.Value), ct));

        return Result<Guid>.Success(order.Id.Value);
    }
}
```

Callbacks run once, after the outermost command commits, in registration order. A nested command's callbacks
merge into the outer command's and run with them; if the nested command fails, its callbacks are discarded.
A callback that throws is logged and does not change the response — the work is already committed. Calling
`OnCompleted` outside a command throws.

## Recipes

### 1. Add your own behavior in a named stage

```csharp
builder.Services.AddSharedKernelApplication(typeof(Program).Assembly, app => app
    .UseMediatR()
    .WithBehavior(typeof(FeatureFlagBehavior<,>), PipelineStage.Authorization, typeof(IFeatureClient)));
```

The trailing types are required services, checked when the host starts like the built-in seams. Built-ins run first
within a stage, then your behaviors in the order you added them. `WithBehavior` throws `ArgumentException` for a type
that is not an open generic implementing `IPipelineBehavior<,>` and `ArgumentOutOfRangeException` for an undefined
stage; adding the same type twice adds it once. A package can ship its own `With…` extension on
`ApplicationPipelineBuilder` the same way (`WithCaching()` does).

## Configuration

The pipeline has no configuration section: both options types are set in code and validated when the host starts.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `ApplicationLoggingOptions.SlowRequestThreshold` | `TimeSpan` | `00:00:00.500` | A successful request slower than this logs 5102 at `Warning`; must be greater than zero. Set with `services.Configure<ApplicationLoggingOptions>(o => …)` |
| `IdempotencyBehaviorOptions.LeaseDuration` | `TimeSpan` | `00:00:30` | How long a reservation holds while the handler runs; up to 24 hours. Set with `app.WithIdempotency(o => …)` |
| `IdempotencyBehaviorOptions.RetentionWindow` | `TimeSpan` | `1.00:00:00` | How long a completed response is kept for replay; 1 second to 365 days, and longer than `LeaseDuration` |

## Reference

### Registration

| Method | Purpose |
| --- | --- |
| `AddSharedKernelApplication(Assembly, Action<ApplicationPipelineBuilder>)` | The one registration call; also `(Assembly[], Action<…>)` and `(params Assembly[])`. A second call throws |
| `ApplicationPipelineBuilder.WithIdempotency(Action<IdempotencyBehaviorOptions>?)` | Idempotency for `IIdempotentRequest` commands; needs `IIdempotencyStore` (request purpose) and `IRequestContext` |
| `ApplicationPipelineBuilder.WithTransactions()` | One transaction per outermost command; needs `IUnitOfWork` |
| `ApplicationPipelineBuilder.WithAuditing()` | Both auditing halves for `IAuditableRequest<T>` commands; needs `IAuditTrailWriter` |
| `ApplicationPipelineBuilder.WithBehavior(Type, PipelineStage, params Type[])` | Your own open-generic behavior in a stage, with the services it requires |
| `AddDomainEventHandler<TDomainEvent, THandler>()` | A domain-event handler outside the scanned assemblies |

Always registered: every `IRequestHandler<,>`/`IStreamQueryHandler<,>` of the assemblies (transient),
`IRequestValidator<>` and `IDomainEventHandler<>` (scoped), the `IDomainEventDispatcher`, `ICommandScope`, and the
tracing, logging, metrics, authorization and validation behaviors. `RequestPipeline<,>` and `StreamRequestPipeline<,>`
are public so a mediator adapter (and tests) can resolve them; the behaviors are internal.

### Errors

| Code | Type | When |
| --- | --- | --- |
| `unauthorized.default` | Unauthorized | A `[RequirePermission]` request from an unauthenticated caller |
| `forbidden.insufficient_permission` | Forbidden | The caller holds no value of one `[RequirePermission]` attribute |
| `validation.failed` | Validation | Any `IRequestValidator<T>` reported errors (each in `Error.Details`) |
| `idempotency.key_required` | Validation | An `IIdempotentRequest` with an empty key |
| `idempotency.in_progress` | Conflict | The same key is being handled right now |
| `idempotency.key_reused` | Conflict | The same key arrived with a different fingerprint |
| `idempotency.store_unavailable` | Unavailable | The idempotency store threw while reserving the key (503); a classified provider exception keeps its own error |

All come from `ErrorCodes` in `SharedKernel.Primitives`.

Application declares no error codes of its own. A configuration error throws `InvalidOperationException` with a sentence
naming the remedy; an undefined idempotency reservation status throws `UnreachableException` and never runs the handler.

### Seams

Each is a contract from a Foundation or Abstractions-tier package, implemented by infrastructure. That is what
keeps this package from referencing persistence, security or a cache.

| Contract | Declared in | Needed by | Implemented by |
| --- | --- | --- | --- |
| `IRequestContext` | `SharedKernel.Execution` (`.Context`) | Authorization (only for `[RequirePermission]` requests), idempotency (per-caller keys), caching, auditing | `SharedKernel.ServiceDefaults.Security`'s `AddSharedKernelRequestContext()`, or the shipped `SystemRequestContext` |
| `IUnitOfWork` | `SharedKernel.Execution` (`.Transactions`) | Transaction, auditing | `SharedKernel.Persistence.EfCore` (`AddSharedKernelPostgres`), directly |
| `IIdempotencyStore` (keyed by `IdempotencyPurpose.Request`) | `SharedKernel.Idempotency.Abstractions` | Idempotency | `SharedKernel.Idempotency.Redis`' `AddRedisIdempotency(p => p.ForRequests())` or `SharedKernel.Idempotency.EfCore`'s `AddEfCoreIdempotency(…)` |
| `IAuditTrailWriter` | `SharedKernel.Execution` (`.Auditing`) | Auditing | `SharedKernel.Persistence.EfCore.Auditing` (`UseAuditTrail()`), directly |
| `ISender` | `SharedKernel.Application` | Sending (always required) | `SharedKernel.Application.Mediator.MediatR`'s `app.UseMediatR()` |

### Telemetry

| Signal | Name | Detail |
| --- | --- | --- |
| Activity source | `SharedKernel.Application` (`ApplicationDiagnostics.ActivitySourceName`) | Request span `application send`, kind Internal |
| Span tags | | `application.request_type`, `application.request_kind`, `outcome`, plus `error.type`/`error.code` on failure |
| Meter | `SharedKernel.Application` (`ApplicationDiagnostics.MeterName`) | via `IMeterFactory`, versioned |
| Histogram | `sharedkernel.application.request.duration` | **seconds**; tags `application.request_type`, `application.request_kind` (`command`, `query`, `request` or `stream`), `outcome`, `error.type` |
| Counter | `sharedkernel.application.stream.items` | `{item}`; items a streaming query yielded, including those before a fault; tags `application.request_type`, `outcome` |
| Span | `application stream` | One per enumerated streaming query, kind Internal, tags `application.request_type`, `application.request_kind` = `stream`, `outcome` |
| Span | `application dispatch_events` | One per non-empty domain-event dispatch, tag `application.event_count`, `outcome` |
| Span | `application handle_event` | One child per event type that has a handler, tag `application.event_type` (and `application.handler_type` when a handler fails), `outcome` |
| Span | `application transaction` | The transaction around a command, tags `application.transaction_joined`, `application.attempts`, `outcome` |

A stream's span, timer and log start at the first read and end once; a stream never read records nothing. Ran to the
end is `success`; a cancellation or an early dispose is `canceled` (status unset, no error log); any other exception is
`exception` and is rethrown unchanged. A stream has no slow-request warning, because its consumer sets its pace.

`AddServiceDefaults` subscribes to every `SharedKernel.*` source and meter by wildcard (and registers this histogram's
second-scale buckets), so a service registers nothing; to subscribe by name use `ApplicationDiagnostics.ActivitySourceName` and `.MeterName`.

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 5100 | Debug | A request enters the pipeline |
| 5101 | Debug | A request completed successfully |
| 5102 | Warning | A successful request crossed `SlowRequestThreshold` |
| 5103 | Warning | A request returned a failed `Result` (carries error type and code) |
| 5104 | Error | A request threw |
| 5105 | Debug | A request was canceled by its caller |
| 5110 | Error | A post-commit `OnCompleted` callback threw |
| 5120 | Warning | `CompleteAsync` reported the idempotency reservation was lost |
| 5121 | Error (Warning for a classified exception) | The idempotency store threw while reserving the key; the request was not handled |
| 5122 | Error | The idempotency store threw while completing the reservation; the response is returned |
| 5123 | Warning | The idempotency store threw while releasing the reservation |
| 5130 | Error | The audit write failed while handling a handler exception |
| 5140 | Debug | A stream ran to the end (item count, elapsed time) |
| 5141 | Debug | A stream was canceled or disposed before the end |
| 5142 | Warning | A stream failed with a classified exception (error type and code) |
| 5143 | Error | A stream failed with an unexpected exception |
| 5150 | Error (Warning for a classified exception) | A domain-event handler threw (event type and handler type, never the payload) |
| 5160 | Warning | A transaction was rolled back because the command returned a failed `Result` |
| 5161 | Error | A transaction was rolled back because the command threw |
| 5162 | Warning | The unit of work ran the transaction delegate more than once |

Every event is a one-sentence template, and a log on a caught-exception path passes the exception last. The category is
the behavior that logs, never the request type. No key, tenant, actor or exception message is a placeholder.

## Testing

[`SharedKernel.Application.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Application/SharedKernel.Application.Testing/README.md)'s `ApplicationPipelineTestHarness` (`SharedKernel.Testing.Application`)
runs `AddSharedKernelApplication` with the behaviors you choose and the same host-start seam check — `Build()` needs no
mediator (handlers registered on `Services`, sent through `RequestPipeline<,>`), `Build<TMarker>()` adds `UseMediatR()`
over the marker's assembly. `FakeRequestContext`
comes from `SharedKernel.Testing`, `FakeUnitOfWork` from `SharedKernel.Persistence.Testing`:

```csharp
using var harness = new ApplicationPipelineTestHarness();

harness.Services.AddSingleton<IRequestContext>(new FakeRequestContext { Permissions = ["orders.refund"] });
harness.Services.AddScoped<IUnitOfWork, FakeUnitOfWork>();
harness.Configure(app => app.WithTransactions())   // authorization, validation, … are always on
       .Build<RefundOrderCommand>();                  // the assembly declaring the handler; adds UseMediatR()

var result = await harness.SendAsync(new RefundOrderCommand(orderId, 10m));

Assert.True(result.IsSuccess);
```

A caller without the permission gets `result.Error.Type == ErrorType.Forbidden`, not an exception; a failed command
leaves `FakeUnitOfWork.SaveChangesCallCount` at 0; and after `WithActivityCapture()` the emitted spans and the
`sharedkernel.application.request.duration` measurements are in `CapturedActivities` / `CapturedMeasurements`.

`AddFakeApplicationBehaviorServices()` registers `IRequestContext`, `IUnitOfWork` and
`IIdempotencyStore` (request purpose) fakes in one call. `FakeIdempotencyStore`
(`SharedKernel.Idempotency.Testing`, registered with `AddFakeIdempotencyStore(purposes)`) implements the real reservation
protocol, including rejecting a stale token, so an idempotency test exercises the same states the Redis and
EF Core stores produce.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Forget the mediator | Call `app.UseMediatR()` (or plug in another `ISender`) | Nothing can send a request; the host start fails naming `ISender`. A bare `ServiceProvider` runs no start check — use `ApplicationPipelineTestHarness` or `IStartupValidator.Validate()` in tests |
| Call `AddSharedKernelApplication` twice | Pass every assembly and option to one call | The second call throws; it would register every behavior again |
| Return something other than `Result`/`Result<T>` | Use `ICommand<T>`/`IQuery<T>` | Authorization and idempotency construct a failed response; any other type throws at the first denial. `SK0040` catches it at build time |
| Rely on registration order | Pick a `PipelineStage` | The stage decides the order; `.WithAuditing().WithTransactions()` and the reverse compose identically |
| Expect a failed `Result` to roll back a second store | Write through the unit of work, or undo it yourself | Nothing commits on failure, but a write the handler made elsewhere is not transactional |
| Treat a released idempotency key as spent | Let the client correct and resubmit with the same key | The key is released on failure on purpose |
| Leave the automatic fingerprint on commands retried across a deploy | Set `Fingerprint` explicitly | Adding a property changes the hash, and retries come back `idempotency.key_reused` |
| Call `ICommandScope.OnCompleted` from a query handler | Queue post-commit work only from commands | It throws — no command is active |
| Register `IRequestContext` as a singleton over a scoped identity | Use `AddSharedKernelRequestContext()`, or register your own as scoped | The first request's caller would be frozen for the process lifetime |

## Design decisions

**Why is the order fixed by the package, not by registration?** The order is a correctness property: authorization must
precede validation (a caller who may not act should not learn the rules, and validators often hit the database), and a
commit must precede a cache eviction. Two services compose the same request the same way.

**Why is authorization always on?** An opt-in authorization behavior can be forgotten; a marked use case is checked
on every path, so endpoints do not repeat it.

**Why are idempotency keys scoped per tenant and caller?** Tenant-only keys would let one caller replay another's
response. The store sees a SHA-256 digest of tenant, caller and key, never the raw key.

**What is deliberately not included?** Fire-and-forget dispatch, a resilience/retry behavior (retries belong to the unit
of work and outbound clients), parallel domain-event dispatch (handlers share one `DbContext`), a generic dual-approval
behavior (approval is a domain aggregate) and a response envelope.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Application packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Application/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
