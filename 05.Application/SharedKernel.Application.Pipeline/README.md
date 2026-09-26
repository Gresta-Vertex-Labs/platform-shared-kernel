# SharedKernel.Application.Pipeline

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)
![Tier: Host](https://img.shields.io/badge/tier-Host-5c6bc0)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

**The application layer of a service in one call — handlers, validators and domain-event handlers found, and the
cross-cutting half of a request (tracing, logging, metrics, authorization, validation, idempotency, transactions,
auditing) composed in one fixed order.**

A handler decides business outcomes. Everything around that decision lives here, so adding an audit trail or
an idempotency guard later changes a marker on a command, never a handler body. The order the behaviors run in is
fixed by this package rather than by your registration order, because the order is a correctness property:
authorization has to precede validation, a commit has to precede a cache eviction.

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

**Tier:** Host — referenced by a service's composition-root (API/worker) project, never by its
application-layer project, which needs only [`SharedKernel.Application`](../SharedKernel.Application/README.md).

**Dependencies:** `SharedKernel.Application`, `SharedKernel.Execution`, `SharedKernel.Primitives`,
`SharedKernel.Core`, `SharedKernel.Idempotency.Abstractions` and first-party `Microsoft.Extensions.*` packages. **No
mediator, no FluentValidation, no cache, no Polly, no hosting** — the transport is
[`SharedKernel.Application.Mediator.MediatR`](../SharedKernel.Application.Mediator.MediatR/README.md) (`UseMediatR()`)
and the caching behaviors live in the separate
[`SharedKernel.Application.Pipeline.Caching`](../SharedKernel.Application.Pipeline.Caching/README.md) (`WithCaching()`).

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [The pipeline](#the-pipeline)
- [What each behavior does](#what-each-behavior-does)
- [What happens when something fails](#what-happens-when-something-fails)
- [Seams you must register](#seams-you-must-register)
- [Nested commands and `ICommandScope`](#nested-commands-and-icommandscope)
- [Extending the pipeline](#extending-the-pipeline)
- [Telemetry reference](#telemetry-reference)
- [Pitfalls](#pitfalls)
- [Testing](#testing)
- [Package](#package)

## Install

```xml
<PackageReference Include="SharedKernel.Application.Pipeline" />
<PackageReference Include="SharedKernel.Application.Mediator.MediatR" />   <!-- the ISender transport -->
```

Versions come from your single `SharedKernelVersion` property (see the repository's `PLATFORM.md`,
"Consuming the kernel").

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
builder.Services.AddSharedKernelRequestContext();                        // IRequestContext (13.ServiceDefaults.Security)
builder.AddSharedKernelPostgres<OrderDbContext>("orders", p => p.UseAuditTrail()); // IUnitOfWork + IAuditTrailWriter (06.Persistence)
builder.Services.AddRedisIdempotency(p => p.ForRequests());                 // IIdempotencyStore for requests (18.Idempotency)
```

A missing seam fails the **host start** with one `OptionsValidationException` naming every missing service and
what needs it — never the first request. `ISender` is always required (a mediator must be plugged in), and so is
`IRequestContext` as soon as a request type of the assemblies carries `[RequirePermission]`. Calling
`AddSharedKernelApplication` a second time throws: pass every assembly and option to the one call.

## The pipeline

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

The behavior classes are internal: the public surface is the registration call, its builder, the options and
`RequestPipeline<,>`.

Tracing is outermost so every log line below it carries the trace id. **Authorization precedes validation**
deliberately: a caller who may not perform an operation should not learn its validation rules, and validators
often hit the database.

Here is one successful command, end to end:

```mermaid
sequenceDiagram
    autonumber
    participant Caller
    participant Tracing
    participant Logging as Logging + Metrics
    participant Authz as Authorization
    participant Valid as Validation
    participant Scope as Command scope
    participant Idem as Idempotency
    participant Audit as Auditing
    participant Tx as Transaction
    participant Handler

    Caller->>Tracing: Send(command)
    Tracing->>Logging: span started
    Logging->>Authz: timer started
    Authz->>Valid: permissions satisfied
    Valid->>Scope: no validation errors
    Scope->>Idem: scope entered, depth 1
    Idem->>Audit: key reserved, token issued
    Audit->>Tx: (records a failure on the way back, after rollback)
    Tx->>Handler: ExecuteInTransactionAsync — the rest runs inside the transaction
    Handler-->>Tx: Result.Success (the Succeeded audit entry is queued on OnBeforeCommit)
    Tx-->>Audit: saved, audit entry written, committed
    Audit-->>Idem: nothing to record (success was recorded inside the transaction)
    Idem-->>Scope: key completed with the response
    Note over Scope: OnCompleted callbacks run here, after the commit
    Scope-->>Caller: Result.Success
```

Read the arrows down as "before the handler" and up as "after it". The `Succeeded` audit entry lands
**inside** the transaction, a `Failed` one after the rollback, the idempotency key is completed **after** the
commit, and post-commit callbacks run last of all.

## Which markers do I implement?

Tracing, logging, metrics and validation apply to every request — a request declares nothing. The other four are
opt-in **per request**, through an attribute or a marker it implements:

```mermaid
flowchart TD
    Start["A request"]
    Start --> P{"Does the caller<br/>need a permission?"}
    P -- yes --> P1["[RequirePermission(...)]<br/>one attribute = alternatives, several = all"]
    P -- no --> D

    P1 --> D{"Is it a command<br/>that must not run twice?"}
    D -- yes --> D1["IIdempotentRequest<br/>IdempotencyKey + Fingerprint"]
    D -- no --> A

    D1 --> A{"Is it a command whose<br/>attempt must be recorded?"}
    A -- yes --> A1["IAuditableRequest&lt;TResponse&gt;<br/>Action, ResourceType, ResourceId, snapshots"]
    A -- no --> L

    A1 --> L{"Should some of its fields<br/>reach the logs?"}
    L -- yes --> L1["ILoggableRequest&lt;TResponse&gt;<br/>the fields you choose, never a reflection walk"]
    L -- no --> Done["Done"]
    L1 --> Done

    style P1 fill:#ede7f6
    style D1 fill:#ede7f6
    style A1 fill:#ede7f6
    style L1 fill:#ede7f6
```

They compose: a refund command can be all four at once. Caching markers live in the
[caching package](../SharedKernel.Application.Pipeline.Caching/README.md).

## What each behavior does

### `TracingBehavior`

Starts an `Activity` named after the request type (`PlaceOrderCommand`), tagged `request.type` (full name)
and `request.kind` (`command`/`query`/`request`). On a failed `Result` it sets the span status to `Error`
with `error.type` and `error.code`; on an exception it sets `Error`, records the exception as a span event,
and rethrows. No listener registered means no allocation.

### `LoggingBehavior`

`Debug` on entry, then one completion line: `Information` on success, `Warning` when the elapsed time crosses
`SlowRequestThreshold` (500 ms by default), `Warning` with the error type and code on a failed `Result`, and
`Error` with the exception on a throw. Payloads are never logged unless the request opts in by implementing
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

Configure the threshold with `services.Configure<ApplicationLoggingOptions>(…)`. It is validated at startup
and must be greater than zero.

### `MetricsBehavior`

Records one histogram measurement per request — `sharedkernel.application.request.duration`, **in seconds**,
from an `IMeterFactory` meter named `SharedKernel.Application`. Tags: `request.type`, `request.kind`,
`outcome` (`success`/`failure`/`exception`), and `error.type` when not successful. Recorded in a `finally`,
so a throw is measured too.

### Authorization

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
the permission (`14.Presentation`'s `[RequireEndpointPermission]` is for endpoints that send no command).

### `ValidationBehavior`

Runs every registered `IRequestValidator<TRequest>` (`SharedKernel.Application.Validation`) **sequentially** —
not in parallel, because two async validators sharing a scoped `DbContext` would throw — collects every error,
and returns
`Error.Validation(errors)`: one error with code `validation.failed` carrying each field failure in
`Error.Details`. It does not throw. At the HTTP boundary `14.Presentation` renders that as a 400 with a
per-field `errors` map; `11.Communication.Rest` rebuilds the same detail on the calling side.

The pipeline carries no validation library. Implement `IRequestValidator<TRequest>` yourself, or keep writing
FluentValidation validators and bridge them with `services.AddFluentValidationRequestValidators(typeof(Program).Assembly)`
(`SharedKernel.Validation.FluentValidation`), which registers the assembly's validators and adapts every `IValidator<T>` and keeps each
failure's error code and property path.

### `IdempotencyBehavior`

Applies to commands implementing `IIdempotentRequest` (opted in with `WithIdempotency()`). It reserves the key through the `IIdempotencyStore` registered for `IdempotencyPurpose.Request`
before the handler runs, and settles it afterwards:

| `TryBeginAsync` returns | The behavior |
| --- | --- |
| `Started` | Runs the handler. On success: `CompleteAsync` with the serialized response, retained for `IdempotencyBehaviorOptions.RetentionWindow` (the reservation itself holds for `LeaseDuration`). On a failed `Result`: `ReleaseAsync`, so the caller may retry with the same key. On an exception: `ReleaseAsync`, then rethrows |
| `Completed` | Returns the stored response — the original outcome, not a fresh conflict |
| `InProgress` | `Error.Conflict(ErrorCodes.Idempotency.InProgress)` (`idempotency.in_progress`) |
| `FingerprintMismatch` | `Error.Conflict(ErrorCodes.Idempotency.KeyReused)` (`idempotency.key_reused`) |

An empty key is `Error.Validation(ErrorCodes.Idempotency.KeyRequired)` (`idempotency.key_required`), the code
`14.Presentation` answers a missing `Idempotency-Key` header with — both take it from `01.Core`'s
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

### `TransactionBehavior`

Runs the rest of the pipeline and the handler **inside** `IUnitOfWork.ExecuteInTransactionAsync`
(`SharedKernel.Execution.Transactions`): the unit of work saves what was staged, runs the `OnBeforeCommit`
callbacks and commits — **only for the outermost command, and only when the response is successful**. A failed
`Result` rolls back, so a handler that mutated an aggregate before deciding to fail leaves no trace; an
exception rolls back and propagates.

- **Handlers must be re-runnable.** Under a retrying execution strategy (on by default in `06.Persistence`) a
  transient failure replays the whole delegate: the unit of work discards what the failed attempt staged and the
  handler runs again. Load what you need through repositories inside the handler; keep HTTP calls and messages
  out of it — queue them with `ICommandScope.OnCompleted`, which runs after the commit. Callbacks queued by a
  discarded attempt are dropped.
- **A command sent while a transaction is active joins it.** If the joined command fails, the transaction becomes
  rollback-only: the outer command commits nothing and, if it would have succeeded, gets
  `TransactionRolledBackException`.
- **An ambiguous commit is not retried.** `CommitOutcomeUnknownException` means the commit may or may not have
  happened — re-read (or rely on the idempotency key) before repeating.

### `AuditingBehavior`

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

## What happens when something fails

```mermaid
flowchart TD
    R{"How did the request end?"}
    R -- "invalid input" --> V["Error.Validation with every field<br/>handler never ran"]
    R -- "denied" --> A["Error.Unauthorized 401<br/>or Error.Forbidden 403"]
    R -- "handler returned a failure" --> F["the failure, unchanged"]
    R -- "handler threw" --> E["the exception, rethrown"]
    R -- "success" --> S["the value"]

    V --> N1["no commit · no audit · key released"]
    A --> N2["no commit · no audit · key released"]
    F --> N3["no commit · audit written · key released"]
    E --> N4["no commit · audit written · key released"]
    S --> N5["commit · audit written · key completed<br/>then OnCompleted callbacks"]

    style S fill:#e8f5e9
    style N5 fill:#e8f5e9
    style E fill:#ffebee
    style N4 fill:#ffebee
```

The rule in one sentence: **only a successful outcome persists anything.** The exception is the audit trail,
which records the attempt either way.

## Seams you must register

Each is a contract from a Foundation or Abstractions-tier package, implemented by infrastructure. That is what
keeps this package from referencing persistence, security or a cache.

| Contract | Declared in | Needed by | Implemented by |
| --- | --- | --- | --- |
| `IRequestContext` | `SharedKernel.Execution` (`.Context`) | Authorization (only for `[RequirePermission]` requests), idempotency (per-caller keys), caching, auditing | `SharedKernel.ServiceDefaults.Security`'s `AddSharedKernelRequestContext()` (over `12.Security`), or the shipped `SystemRequestContext` |
| `IUnitOfWork` | `SharedKernel.Execution` (`.Transactions`) | Transaction, auditing | `06.Persistence` (`AddSharedKernelPostgres`), directly — no adapter |
| `IIdempotencyStore` (keyed by `IdempotencyPurpose.Request`) | `SharedKernel.Idempotency.Abstractions` | Idempotency | `18.Idempotency`'s `AddRedisIdempotency(p => p.ForRequests())`/`AddEfCoreIdempotency(…)` |
| `IAuditTrailWriter` | `SharedKernel.Execution` (`.Auditing`) | Auditing | `06.Persistence.EfCore.Auditing` (`UseAuditTrail()`), directly |

| `ISender` | `SharedKernel.Application` | Sending (always required) | `SharedKernel.Application.Mediator.MediatR`'s `app.UseMediatR()` |

Each is checked when the host starts, so the order of the registrations does not matter.

`06.Persistence` implements the same `IUnitOfWork`/`IAuditTrailWriter` the behaviors consume and reads the same
`IRequestContext`; there is exactly one of each.

## Nested commands and `ICommandScope`

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

## Extending the pipeline

Your own behavior goes into a named stage instead of wherever it happened to be registered:

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

## Telemetry reference

| Signal | Name | Detail |
| --- | --- | --- |
| Activity source | `SharedKernel.Application` | Span name = request type short name, kind Internal |
| Span tags | | `request.type`, `request.kind`, plus `error.type`/`error.code` on failure |
| Meter | `SharedKernel.Application` | via `IMeterFactory` |
| Histogram | `sharedkernel.application.request.duration` | **seconds**; tags `request.type`, `request.kind`, `outcome`, `error.type` |

| EventId | Level | Emitted when |
| --- | --- | --- |
| 5100 | Debug | A request enters the pipeline |
| 5101 | Information | A request completed successfully |
| 5102 | Warning | A successful request crossed `SlowRequestThreshold` |
| 5103 | Warning | A request returned a failed `Result` (carries error type and code) |
| 5104 | Error | A request threw |
| 5110 | Error | A post-commit `OnCompleted` callback threw |
| 5120 | Warning | `CompleteAsync` reported the idempotency reservation was lost |
| 5130 | Error | The audit write failed while handling a handler exception |

Wire the meter and activity source into a host with `13.ServiceDefaults`' `WithApplicationTelemetry()`, which
also registers the seconds-based bucket boundaries this histogram needs.

## Pitfalls

**Forgetting the mediator.** Without `app.UseMediatR()` (or another `ISender`) nothing can send a request; the host
start fails naming `ISender`. A plain `ServiceProvider` built in a test runs no start check — call
`IStartupValidator.Validate()` or use `ApplicationPipelineTestHarness`, which does.

**Calling `AddSharedKernelApplication` twice.** It throws, because the second call would register every behavior
again. Pass every assembly to the one call.

**A response type that is not `Result`/`Result<T>`.** Authorization and idempotency short-circuit by
*constructing* a failed response. Any other response type throws `InvalidOperationException` at the first
denial. Analyzer `SK0040` catches this at build time.

**Assuming registration order matters.** It does not. The stage decides the order; `.WithAuditing().WithTransactions()` and
`.WithTransactions().WithAuditing()` compose identically, and the seams may be registered before or after the call.

**Expecting a failed `Result` to roll back.** Nothing is rolled back, because nothing was committed — the
commit simply never happens. If your handler wrote through a second store directly, that write is yours to
undo.

**Reusing an idempotency key after a business failure.** The key is released on failure, so the same key is
accepted again. That is intentional: a rejected command should be correctable and resubmitted.

**Letting the automatic fingerprint ride.** Adding a property to a command changes it, and every in-flight
retry across the deploy comes back as `idempotency.key_reused`. Set `Fingerprint` on commands that matter.

**Calling `ICommandScope.OnCompleted` from a query handler.** It throws — no command is active. Post-commit
work only makes sense where there is a commit.

**Registering `IRequestContext` as a singleton over a scoped identity.** The first request's caller would be
frozen in for the lifetime of the process. `AddSharedKernelRequestContext()` gets this right (a transient that
reads the ambient `RequestContextScope`, falling back to the scoped caller); a hand-written one should be scoped.

## Testing

`SharedKernel.Application.Testing`'s `ApplicationPipelineTestHarness` (`SharedKernel.Testing.Application`)
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

Useful assertions from there:

```csharp
// A denial, not an exception:
harness.Services.AddSingleton<IRequestContext>(new FakeRequestContext { Permissions = [] });
Assert.Equal(ErrorType.Forbidden, result.Error.Type);

// A failed command commits nothing:
Assert.Equal(0, fakeUnitOfWork.SaveChangesCallCount);

// What the pipeline emitted:
harness.WithActivityCapture();
Assert.Contains(harness.CapturedActivities, a => a.DisplayName == "RefundOrderCommand");
Assert.Contains(harness.CapturedMeasurements, m => m.InstrumentName == "sharedkernel.application.request.duration");
```

`AddFakeApplicationBehaviorServices()` registers `IRequestContext`, `IUnitOfWork` and
`IIdempotencyStore` (request purpose) fakes in one call. `FakeIdempotencyStore`
(`SharedKernel.Idempotency.Testing`, registered with `AddFakeIdempotencyStore(purposes)`) implements the real reservation
protocol, including rejecting a stale token, so an idempotency test exercises the same states the Redis and
EF Core stores produce.

## Package

| | |
| --- | --- |
| **Tier** | Host |
| **Depends on** | `SharedKernel.Application`, `SharedKernel.Execution`, `SharedKernel.Primitives`, `SharedKernel.Core` (the exception of a denied stream), `SharedKernel.Idempotency.Abstractions`, `Microsoft.Extensions.{DependencyInjection.Abstractions, Diagnostics, Logging, Logging.Abstractions, Options, Options.DataAnnotations}` |
| **Does not depend on** | A mediator, FluentValidation, any cache, Polly or hosting — enforced by architecture tests |
| **Target** | `net10.0` |
| **Public API** | Tracked; an unrecorded change fails the build |
| **EventId range** | 5100–5199, within `01.Core`'s `05.Application` block |

Maintainer rules live in [`05.Application/CLAUDE.md`](../CLAUDE.md); the layer overview, including the
pipeline and commit diagrams, is in [`05.Application/README.md`](../README.md).
