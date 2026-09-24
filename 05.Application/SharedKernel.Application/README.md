# SharedKernel.Application

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)
![MediatR 12.4.1](https://img.shields.io/badge/MediatR-12.4.1%20(MIT)-5c6bc0)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

**Commands, queries and their handlers, and the pipeline that runs around every handler: tracing, logging, metrics,
authorization, validation, idempotency, transactions and auditing, in a fixed order, registered with one call.**

The overview, the 10-minute path and the pipeline table are in the [domain README](../README.md). This page is the
reference.

## Contents

- [Install and register](#install-and-register)
- [Commands and queries](#commands-and-queries)
- [Who is calling: `IRequestContext`](#who-is-calling-irequestcontext)
- [Domain events](#domain-events)
- [The behaviors](#the-behaviors)
- [What happens when something fails](#what-happens-when-something-fails)
- [Nested commands and `ICommandScope`](#nested-commands-and-icommandscope)
- [Your own behavior](#your-own-behavior)
- [Telemetry](#telemetry)
- [Pitfalls](#pitfalls)
- [Testing](#testing)
- [Reference](#reference)

## Install and register

```shell
dotnet add package SharedKernel.Application
```

```csharp
using SharedKernel.Application;

// The always-on behaviors only: tracing, logging, metrics, validation.
builder.Services.AddSharedKernelApplication(typeof(Program).Assembly);

// Or with opt-ins (one call per service collection; pass every assembly here):
builder.Services.AddSharedKernelApplication(typeof(Program).Assembly, app => app
    .WithAuthorization()
    .WithIdempotency()
    .WithTransactions()
    .WithAuditing());
```

| Always registered | |
| --- | --- |
| MediatR | with the handlers of the assemblies you pass |
| FluentValidation validators | of the same assemblies, public and internal, scoped |
| `IDomainEventDispatcher` | the domain-event bridge to MediatR notifications |
| `ICommandScope` | injectable by any handler |
| Tracing, Logging, Metrics, Validation | the four behaviors that need nothing else |

An overload takes `Assembly[]` for handlers spread over several assemblies. The seams the opt-ins need are checked when
the host starts (`ValidateOnStart`), so they may be registered before or after this call; a missing one fails the start
with an `OptionsValidationException` naming every missing service and the `With…` call that needs it. A second call
throws `InvalidOperationException`: it would register every behavior twice.

## Commands and queries

| Request | Handler | Handler returns | Command-stage behaviors |
| --- | --- | --- | --- |
| `ICommand` | `ICommandHandler<TCommand>` | `Result` | yes |
| `ICommand<TResponse>` | `ICommandHandler<TCommand, TResponse>` | `Result<TResponse>` | yes |
| `IQuery<TResponse>` | `IQueryHandler<TQuery, TResponse>` | `Result<TResponse>` | no |
| `IStreamQuery<TResponse>` | `IStreamQueryHandler<TQuery, TResponse>` | `IAsyncEnumerable<TResponse>` | no behavior at all |

The handler interfaces are MediatR's `IRequestHandler<,>` under a name that says what the class is. `ICommandBase`
and `IQueryBase` are member-less markers: a behavior constrained to `ICommandBase` is never resolved for a query.

```csharp
public sealed record GetOrder(Guid Id) : IQuery<OrderDto>;

public sealed class GetOrderHandler(IOrderReader reader) : IQueryHandler<GetOrder, OrderDto>
{
    public async Task<Result<OrderDto>> Handle(GetOrder query, CancellationToken cancellationToken) =>
        await reader.FindAsync(query.Id, cancellationToken) is { } order
            ? order
            : Error.NotFound("order.not_found", $"Order {query.Id} was not found.");
}
```

- **Return, never throw, an expected failure.** A missing order, a rule violation, a blocked customer: each is a
  failed `Result`. An exception is a fault; it skips the failure path and reaches the client as a 500.
- **The response is `Result` or `Result<T>`.** The authorization, validation and idempotency behaviors answer by
  constructing a failed response, which only those types can express.
- **A stream is not wrapped in `Result`** and no behavior runs for it: MediatR routes streams through a separate
  pipeline, and this package registers nothing there. Authorize and validate inside the stream handler.

## Who is calling: `IRequestContext`

`IRequestContext` (namespace `SharedKernel.Application.Context`, package `SharedKernel.Application.Abstractions`) is how
the pipeline, the persistence layer and your handlers learn the caller: `IsAuthenticated`, `UserId`, `TenantId`,
`ActorKind` (`User`, `Service`, `System`, `Anonymous`), `ClientId`, `SessionId`, `ImpersonatorId` and
`HasPermissionAsync(permission)`.

| Registration | For |
| --- | --- |
| `services.AddSharedKernelRequestContext()` (`13.ServiceDefaults`) | a service with authenticated callers, over `12.Security`'s `IUserContext` and `ITenantProvider` |
| `new SystemRequestContext(permissions, identity, tenantId)` | a worker: authenticated, `ActorKind.System`, exactly the permissions listed, never "all" |
| `AnonymousRequestContext.Instance` | an unauthenticated path |

Register it scoped: a singleton over a scoped identity freezes the first caller for the life of the process.

## Domain events

An aggregate raises an `IDomainEvent` (`03.Domain`); `06.Persistence` dispatches the events of every save through
`IDomainEventDispatcher`, which this package implements over MediatR. Handle an event against the raw event type:

```csharp
public sealed class SendConfirmation(IEmailSender email) : IDomainEventHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced domainEvent, CancellationToken cancellationToken) =>
        email.SendAsync(domainEvent.CustomerEmail, cancellationToken);
}

builder.Services.AddDomainEventHandler<OrderPlaced, SendConfirmation>();
```

A MediatR `INotificationHandler<DomainEventNotification<OrderPlaced>>` in a registered assembly works as well; it is
found by the assembly scan.

- Dispatch is **serial**, in the order the aggregate raised the events, **before** the physical save. A handler's
  database changes join the same save and the same transaction.
- A handler that throws abandons the save and fails the request.
- Work outside the database (mail, another service) does not belong in a domain-event handler: queue it with
  `ICommandScope.OnCompleted` or publish an integration event through the outbox (`07.Messaging`).

## The behaviors

The order is in the [domain README](../README.md#the-pipeline). Each behavior in detail:

### Tracing

Starts an `Activity` named after the request type on the `SharedKernel.Application` source, tagged `request.type`
(full name) and `request.kind` (`command`, `query`, `request`). A failed `Result` sets the status to `Error` with
`error.type` and `error.code`; an exception records the exception and rethrows.

### Logging

`Debug` on entry; on completion `Information`, or `Warning` above `ApplicationLoggingOptions.SlowRequestThreshold`
(500 ms by default, validated at start to be positive), `Warning` with the error type and code for a failed `Result`,
`Error` with the exception for a throw. No payload is logged unless the request implements `ILoggableRequest<TResponse>`
and names the fields itself:

```csharp
public sealed record PayInvoice(Guid InvoiceId, string CardNumber, decimal Amount)
    : ICommand, ILoggableRequest<Result>
{
    public IReadOnlyDictionary<string, object?> LoggableRequestFields =>
        new Dictionary<string, object?> { ["InvoiceId"] = InvoiceId, ["Amount"] = Amount };   // never the card

    public IReadOnlyDictionary<string, object?>? GetLoggableResponseFields(Result response) => null;
}
```

Change the threshold with `services.Configure<ApplicationLoggingOptions>(o => o.SlowRequestThreshold = …)`.

### Metrics

One measurement per request on the histogram `sharedkernel.application.request.duration`, in seconds, from the meter
`SharedKernel.Application`: tags `request.type`, `request.kind`, `outcome` (`success`, `failure`, `exception`) and
`error.type` when not successful. Recorded in a `finally`, so a throw is measured too.

### Authorization (`WithAuthorization()`)

Checks `[RequirePermission]` against `IRequestContext`:

```csharp
[RequirePermission("invoices.pay", "invoices.admin")]   // either one
[RequirePermission("customers.read")]                     // and this one
public sealed record PayInvoice(Guid InvoiceId, decimal Amount) : ICommand;
```

| Situation | Result |
| --- | --- |
| No `[RequirePermission]` on the request | not checked |
| `IRequestContext.IsAuthenticated` is false | `Error.Unauthorized("unauthorized.default")`: 401 at the HTTP edge |
| One attribute none of whose values the caller holds | `Error.Forbidden("forbidden.insufficient_permission")`: 403; the message never names the permission |
| Every attribute satisfied | the request continues |

The attributes are read once per request type and inherited from base types. An attribute with no value or a blank
one throws when the type is first checked, so every send of that request fails instead of running unchecked. The
codes are the ones `14.Presentation`'s endpoint attributes answer with, so a client sees the same 401 and 403 whether
the edge or the pipeline refused it.

Permissions go here, on the use case. `14.Presentation`'s `[RequireEndpointPermission]` is only for hubs, gRPC
services and endpoints that send no command; an endpoint that sends this command does not repeat its permission.

### Validation

Runs every registered `IValidator<TRequest>` **one after another** (validators may share a scoped `DbContext`),
collects every failure and returns `Error.Validation(errors)`: code `validation.failed`, one child error per failure,
each carrying its field path. `14.Presentation` writes it as a 400 with `errors` and `errorCodes` per field. It never
throws.

### Idempotency (`WithIdempotency()`)

For commands implementing `IIdempotentRequest`. The key is reserved in `IRequestIdempotencyStore` before the handler
runs and settled after it:

| The store answers | The behavior |
| --- | --- |
| `Started` | runs the handler; on success stores the response (`CompleteAsync`); on a failed `Result` or an exception releases the key (`ReleaseAsync`) so the caller may retry |
| `Completed` | returns the stored response; the handler does not run |
| `InProgress` | `Error.Conflict("idempotency.in_progress")` |
| `FingerprintMismatch` | `Error.Conflict("idempotency.key_reused")` |

A blank key fails with `Error.Validation("idempotency.key_required")`, the code `14.Presentation` answers a missing
`Idempotency-Key` header with. The codes are `ErrorCodes.Idempotency` (`01.Core`).

**Keys are reserved per tenant and caller.** A client-chosen key is not a secret, so the store never sees it: it gets a
SHA-256 digest of the tenant, the actor kind, the subject, the OAuth client, the impersonator and the key. Two callers
using the same key each get their own execution; the same caller retrying gets the replay, even after signing in
again (the session is not part of the scope).

| The same key is sent again by | Same body | Different body |
| --- | --- | --- |
| the same caller | the stored response | `idempotency.key_reused` |
| another user, service or system identity, or another tenant | its own execution | its own execution |
| another **anonymous** caller of the same tenant | **the stored response** | `idempotency.key_reused` |

Anonymous callers of a tenant share one scope, so only the fingerprint separates them: on a command anonymous callers
can send, use random keys and return nothing only the sender may see.

The fingerprint defaults to a SHA-256 hash of the serialized command. Set `Fingerprint` on a command that may be
retried across a deploy or carries a timestamp or a generated id, because the automatic hash changes with them:

```csharp
public sealed record TransferMoney(Guid From, Guid To, decimal Amount, DateTimeOffset RequestedAt, string IdempotencyKey)
    : ICommand, IIdempotentRequest
{
    public string? Fingerprint => $"{From}:{To}:{Amount}";   // RequestedAt left out on purpose
}
```

A nested command skips idempotency: the outermost command owns the key.

### Transaction (`WithTransactions()`)

Runs the rest of the pipeline and the handler inside `IUnitOfWork.ExecuteInTransactionAsync`, which saves what was
staged, runs the `OnBeforeCommit` callbacks and commits — only for the outermost command, only when the response is
successful. A failed `Result` rolls back; an exception rolls back and propagates.

- **Handlers must be re-runnable.** A transient failure may replay the whole delegate (on by default in
  `06.Persistence`); what the failed attempt staged is discarded, and so are its `OnCompleted` callbacks.
- **A command sent inside an active transaction joins it.** If it fails, the transaction becomes rollback-only and the
  outer command, if it would have succeeded, gets `TransactionRolledBackException`.
- **An ambiguous commit is not retried.** `CommitOutcomeUnknownException` means the commit may or may not have
  happened; re-read, or rely on the idempotency key, before repeating.

### Auditing (`WithAuditing()`)

For commands implementing `IAuditableRequest<TResponse>`, which supplies its own opaque, pre-serialized snapshots:

```csharp
public sealed record UpdateLimit(Guid CustomerId, decimal NewLimit) : ICommand, IAuditableRequest<Result>
{
    public string Action => "customer.limit.update";
    public string ResourceType => "Customer";
    public string ResourceId => CustomerId.ToString("D");
    public string? BeforeSnapshot => null;
    public string? GetAfterSnapshot(Result response) => response.IsSuccess ? $$"""{"limit":{{NewLimit}}}""" : null;
}
```

Two halves surround the transaction. The inner one queues the `Succeeded` entry on `IUnitOfWork.OnBeforeCommit`, so it
commits or rolls back with the change it attests to. The outer one records every failure — a failed `Result`, an
exception, a failed commit — after the rollback, on the writer's own connection. If the audit write fails while an
exception is being handled, the original exception propagates and the audit failure is logged. Actor, tenant, time and
correlation are resolved by the writer from `IRequestContext`, never taken from the command.

## What happens when something fails

| The request ends with | Commit | Audit entry | Idempotency key | `OnCompleted` callbacks |
| --- | --- | --- | --- | --- |
| a denial (401, 403) | no | none | not reserved | none |
| invalid input (400) | no | none | not reserved | none |
| a failed `Result` from the handler | no | `Failed`, with the error code | released | discarded |
| an exception | no | `Failed`, with the exception type | released | discarded |
| success | yes | `Succeeded`, in the same transaction | completed with the response | run after the commit |

## Nested commands and `ICommandScope`

A handler that sends another command creates a nested command in the same DI scope. The nested command joins the outer
transaction, skips idempotency and writes its own audit entry (which rolls back if the outer command fails).

`ICommandScope.OnCompleted` queues work for after the commit of the outermost command:

```csharp
public sealed class ShipOrderHandler(IOrderRepository orders, ICommandScope scope, INotifier notifier)
    : ICommandHandler<ShipOrder>
{
    public async Task<Result> Handle(ShipOrder command, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(command.OrderId, cancellationToken);
        if (order is null)
            return Error.NotFound("order.not_found", $"Order {command.OrderId} was not found.");

        var shipped = order.Ship();
        if (shipped.IsSuccess)
            scope.OnCompleted(ct => notifier.OrderShippedAsync(order.Id, ct));   // after the commit

        return shipped;
    }
}
```

Callbacks run once, in the order they were queued. A nested command's callbacks join the outer command's; a failed
command discards its own. A callback that throws is logged (EventId 5110) and does not change the response: the work is
already committed. `OnCompleted` outside a command throws. `IsActive` and `IsNested` tell a handler where it runs.

## Your own behavior

`WithBehavior` places an open-generic `IPipelineBehavior<,>` in a stage, after that stage's built-in behaviors and
after behaviors added to it earlier:

```csharp
builder.Services.AddSharedKernelApplication(typeof(Program).Assembly, app => app
    .WithAuthorization()
    .WithBehavior(typeof(FeatureGateBehavior<,>), PipelineStage.Authorization, typeof(IFeatureGate)));
```

The trailing types are services the behavior needs; they are checked when the host starts, like the built-in seams. The
type must be an open generic implementing `IPipelineBehavior<,>`, and the stage a defined `PipelineStage`. A package
that ships a behavior adds an extension on `ApplicationPipelineBuilder` and registers what the behavior needs through
its `Services` property, as `SharedKernel.Application.Caching`'s `WithCaching()` does.

## Telemetry

| Signal | Name | Detail |
| --- | --- | --- |
| Activity source | `SharedKernel.Application` | span per request, named after the request type |
| Meter | `SharedKernel.Application` | from `IMeterFactory` |
| Histogram | `sharedkernel.application.request.duration` | seconds; `request.type`, `request.kind`, `outcome`, `error.type` |

`13.ServiceDefaults`' `WithApplicationTelemetry()` exports both, with bucket boundaries in seconds.

| EventId | Level | Event |
| --- | --- | --- |
| 5100 | Debug | A request entered the pipeline |
| 5101 | Information | A request succeeded |
| 5102 | Warning | A successful request was slower than `SlowRequestThreshold` |
| 5103 | Warning | A request returned a failed `Result` (error type and code) |
| 5104 | Error | A request threw |
| 5110 | Error | An `OnCompleted` callback threw |
| 5120 | Warning | The idempotency store reported the reservation lost when completing it |
| 5130 | Error | The audit write failed while an exception was being handled |

## Pitfalls

- **`[RequirePermission]` without `WithAuthorization()`.** Nothing enforces the attribute then: the request runs for
  every caller. Every service whose use cases declare permissions opts into authorization.
- **A permission repeated on the endpoint.** `[RequireEndpointPermission]` on an endpoint that sends a command
  duplicates the command's `[RequirePermission]` and drifts from it; keep the permission on the use case only.
- **A response type that is not `Result`/`Result<T>`** on a request with `[RequirePermission]` or `IIdempotentRequest`
  throws at the first denial. `SK0040` catches it at build time.
- **A handler that is not re-runnable** under `WithTransactions()`: an HTTP call or a message sent from inside it runs
  again on a replay. Queue it with `OnCompleted`.
- **Reusing a key after a failure** is accepted: the key was released so the caller can correct and resubmit.
- **Looking for the raw key in the store.** The store holds the scoped digest, never the key you sent.
- **`OnCompleted` in a query handler** throws: there is no command and no commit.

## Testing

A handler is a class returning a `Result`: test it directly. To test the composed pipeline, `16.Testing`'s
`ApplicationPipelineTestHarness` registers the real one:

```csharp
using var harness = new ApplicationPipelineTestHarness()
    .Configure(app => app.WithAuthorization().WithTransactions());

var unitOfWork = new FakeUnitOfWork();
harness.Services.AddSingleton<IRequestContext>(new FakeRequestContext { Permissions = ["invoices.pay"] });
harness.Services.AddSingleton<IUnitOfWork>(unitOfWork);
harness.Build<Program>();   // the handlers and validators of Program's assembly

var result = await harness.SendAsync(new PayInvoice(invoiceId, 10m));

Assert.True(result.IsSuccess);
Assert.Equal(1, unitOfWork.CommitCount);
```

`Build<T>()` runs the start-time checks, so a missing seam fails there. `WithActivityCapture()`,
`CapturedActivities` and `CapturedMeasurements` assert the telemetry. `AddFakeApplicationBehaviorServices()` registers
`FakeRequestContext`, `FakeUnitOfWork` and `FakeRequestIdempotencyStore` in one call; the store implements the real
reservation protocol and receives the same caller-scoped digest the real stores do. `FakeUnitOfWork.TransientFailures`
replays the transaction to prove a handler re-runnable.

## Reference

### Namespaces

| Namespace | For | Holds |
| --- | --- | --- |
| `SharedKernel.Application` | every service | `ICommand`, `ICommand<T>`, `IQuery<T>`, `ICommandBase`, `IQueryBase`, the handler interfaces, `IStreamQuery<T>`, `IStreamQueryHandler<,>`, `RequirePermissionAttribute`, `IIdempotentRequest`, `IAuditableRequest<T>`, `ILoggableRequest<T>`, `ICommandScope`, `PipelineStage`, `ApplicationPipelineBuilder`, `AddSharedKernelApplication`, `AddDomainEventHandler`, `ApplicationLoggingOptions`, `IDomainEventHandler<T>`, `DomainEventNotification<T>` |
| `SharedKernel.Application.Idempotency` | store implementers (`18.Idempotency`) | `IRequestIdempotencyStore`, `IdempotencyBeginResult`, `IdempotencyBeginStatus` |
| `SharedKernel.Application.Context`, `.Transactions`, `.Auditing` | in `SharedKernel.Application.Abstractions` | `IRequestContext` and its implementations, `IUnitOfWork`, `IAuditTrailWriter` |

Every behavior, the domain-event dispatcher and the metrics type are internal, in `SharedKernel.Application.Pipeline`.

### Package

| | |
| --- | --- |
| **Depends on** | `SharedKernel.Application.Abstractions`, `SharedKernel.Primitives`, `SharedKernel.Domain`, `MediatR` 12.4.1, `FluentValidation`, `Microsoft.Extensions.*` (DI, logging, options, diagnostics) |
| **Never depends on** | a cache, persistence, messaging, security or any other infrastructure package |
| **Target** | `net10.0` |
| **Public API** | tracked; an unrecorded change fails the build |
| **EventIds** | 5100–5199 |

MediatR 12.4.1 is the last MIT-licensed release and is pinned on purpose; the interfaces here derive from MediatR's, so
a consuming service resolves the same version.

Maintainer rules: [`05.Application/CLAUDE.md`](../CLAUDE.md).
