# SharedKernel.Application

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)
![Tier: Abstractions](https://img.shields.io/badge/tier-Abstractions-5c6bc0)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

**The CQRS vocabulary every service in the platform speaks: the kernel's own request, handler, sender and
pipeline-behavior contracts, commands and queries, validators, domain-event handlers, and the markers the
pipeline behaviors key off — with no mediator library in it.**

This package is deliberately small. It declares *shapes*, runs no logic and performs no I/O, so a service's
application-layer project can reference it and nothing else. The behaviors that log, authorize, validate,
commit and audit requests live in
[`SharedKernel.Application.Pipeline`](../SharedKernel.Application.Pipeline/README.md); the transport behind
`ISender` is [`SharedKernel.Application.Mediator.MediatR`](../SharedKernel.Application.Mediator.MediatR/README.md).

| You get | So that |
| --- | --- |
| `IRequest<T>`, `IRequestHandler<,>`, `ISender`, `IPipelineBehavior<,>` | Handlers and behaviors are written against kernel contracts; MediatR (or any other transport) is replaceable without touching them |
| `ICommand`, `ICommand<TResponse>`, `IQuery<TResponse>` | A request's *intent* is visible in its declaration, and the pipeline can treat writes and reads differently |
| `ICommandBase` / `IQueryBase` markers | A behavior constrains to "any command" or "any query" once, and the container simply never resolves it for the other kind |
| Handler aliases (`ICommandHandler<>`, `IQueryHandler<,>`) | A handler class says what it is, instead of `IRequestHandler<PlaceOrder, Result<Guid>>` |
| Every handler returns `Result`/`Result<T>` | An expected failure is a value the caller must handle, not an exception thrown past it |
| `IStreamQuery<TResponse>` and its handler | A large read streams item by item instead of materializing in memory |
| `IRequestValidator<TRequest>` | Validation is a kernel port; FluentValidation is one optional implementation |
| `IDomainEventHandler<TDomainEvent>` | A domain event from `03.Domain` is handled against the raw event type, with no wrapper |
| `[RequirePermission]` (always enforced) and the request markers (`IIdempotentRequest`, `IAuditableRequest<T>`, `ILoggableRequest<T>`, `ICacheableQuery<T>`, `IInvalidatesCache`) and `ICommandScope` | A request opts into a behavior by declaring an interface; the handler body never changes |

**Tier:** Abstractions. **Dependencies:** `SharedKernel.Primitives`, `SharedKernel.Domain`,
`SharedKernel.Caching.Abstractions` (for `CachePolicy` on `ICacheableQuery`). No MediatR, no FluentValidation, no
HTTP, no persistence, no security stack. The caller contract `IRequestContext` is not here: it lives in
`01.Core`'s `SharedKernel.Execution`, shared with persistence and messaging.

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [Which interface do I implement?](#which-interface-do-i-implement)
- [Walkthrough](#walkthrough)
  - [1. A command that changes state](#1-a-command-that-changes-state)
  - [2. A query that reads](#2-a-query-that-reads)
  - [3. Who is calling — `IRequestContext`](#3-who-is-calling--irequestcontext)
  - [4. Validating a request](#4-validating-a-request)
  - [5. Handling a domain event](#5-handling-a-domain-event)
  - [6. Streaming a large read](#6-streaming-a-large-read)
  - [7. Writing a pipeline behavior](#7-writing-a-pipeline-behavior)
- [Reference](#reference)
- [Pitfalls](#pitfalls)
- [Testing](#testing)
- [Package](#package)

## Install

```xml
<PackageReference Include="SharedKernel.Application" />
```

Versions come from your single `SharedKernelVersion` property (the repository's `PLATFORM.md`, "Consuming the
kernel"). This package registers nothing: it has no DI extension. The composition root wires the transport and
the pipeline:

```csharp
// API / worker project
builder.Services.AddSharedKernelApplication(typeof(PlaceOrderCommand).Assembly, app => app.UseMediatR());
// AddSharedKernelApplication: SharedKernel.Application.Pipeline; UseMediatR: SharedKernel.Application.Mediator.MediatR
```

## Quick start

```csharp
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;

// 1. The command: what the caller wants, and what it gets back on success.
public sealed record PlaceOrderCommand(string Customer, decimal Amount) : ICommand<Guid>;

// 2. The handler: business decisions only. No logging, no try/catch, no SaveChanges.
public sealed class PlaceOrderHandler(IOrderRepository repository, IClock clock)
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var order = Order.Place(command.Customer, command.Amount, clock);
        if (order.IsFailure)
            return Result<Guid>.Failure(order.Error);   // an expected outcome, not an exception

        await repository.AddAsync(order.Value, cancellationToken);
        return Result<Guid>.Success(order.Value.Id.Value);
    }
}

// 3. The endpoint: send through the kernel ISender, map the Result to HTTP and stop thinking about it.
app.MapPost("/orders", async (PlaceOrderCommand command, ISender sender, CancellationToken ct) =>
    (await sender.Send(command, ct)).ToProblemDetailsResult());
```

Three properties hold from here on, and they are what the rest of the platform builds on:

- **The handler never throws for an expected failure.** A blocked customer, a missing order, a rule
  violation — each is a `Result.Failure` carrying an `Error`.
- **The handler never performs cross-cutting work.** Commit, cache, audit and log are the pipeline's job, and
  adding one later changes no handler.
- **The response type is `Result` or `Result<T>`.** Several behaviors short-circuit by *constructing* a
  failed response, which only those types can express.

## Which interface do I implement?

```mermaid
flowchart TD
    Start{"Does it change state?"}
    Start -- "yes" --> C{"Does the caller need a value back?"}
    Start -- "no" --> Q{"One response, or a stream?"}

    C -- "no, just success or failure" --> C1["ICommand<br/>handler returns Result"]
    C -- "yes, an id or a projection" --> C2["ICommand&lt;TResponse&gt;<br/>handler returns Result&lt;TResponse&gt;"]

    Q -- "one response" --> Q1["IQuery&lt;TResponse&gt;<br/>handler returns Result&lt;TResponse&gt;"]
    Q -- "a stream of items" --> Q2["IStreamQuery&lt;TResponse&gt;<br/>handler returns IAsyncEnumerable&lt;TResponse&gt;"]

    style C1 fill:#ede7f6
    style C2 fill:#ede7f6
    style Q1 fill:#e3f2fd
    style Q2 fill:#e3f2fd
```

| Request type | Handler alias | Handler returns | Transaction, idempotency, auditing apply? |
| --- | --- | --- | --- |
| `ICommand` | `ICommandHandler<TCommand>` | `Result` | Yes — it is a command |
| `ICommand<TResponse>` | `ICommandHandler<TCommand,TResponse>` | `Result<TResponse>` | Yes |
| `IQuery<TResponse>` | `IQueryHandler<TQuery,TResponse>` | `Result<TResponse>` | No — those behaviors constrain to `ICommandBase` |
| `IStreamQuery<TResponse>` | `IStreamQueryHandler<TQuery,TResponse>` | `IAsyncEnumerable<TResponse>` | No — only `IStreamPipelineBehavior<,>`s you register apply |

`ICommandBase` and `IQueryBase` carry no members. They exist so a behavior can be written once against "a
command" — the container then resolves that behavior for commands and silently skips it for everything else.
That is a registration-time fact, not an `if` inside the behavior.

## Walkthrough

### 1. A command that changes state

```csharp
public sealed record CancelOrderCommand(Guid OrderId, string Reason) : ICommand;

public sealed class CancelOrderHandler(IOrderRepository repository)
    : ICommandHandler<CancelOrderCommand>
{
    public async Task<Result> Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await repository.GetAsync(new OrderId(command.OrderId), cancellationToken);
        if (order is null)
            return Result.Failure(Error.NotFound("order.not_found", "The order does not exist."));

        return order.Cancel(command.Reason);   // the aggregate returns Result
    }
}
```

`ICommand` binds the response to the non-generic `Result`, so a caller still branches on `IsSuccess` without
an exception and without an unused return value.

### 2. A query that reads

```csharp
public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;

public sealed class GetOrderHandler(IOrderReadService reads) : IQueryHandler<GetOrderQuery, OrderDto>
{
    public async Task<Result<OrderDto>> Handle(GetOrderQuery query, CancellationToken cancellationToken)
    {
        var dto = await reads.FindAsync(query.OrderId, cancellationToken);
        return dto is null
            ? Result<OrderDto>.Failure(Error.NotFound("order.not_found", "The order does not exist."))
            : Result<OrderDto>.Success(dto);
    }
}
```

A query never implements `ICommandBase`, so `TransactionBehavior`, `IdempotencyBehavior` and
`AuditingBehavior` are simply absent from its pipeline. Nothing is skipped at runtime — they never resolve.

### 3. Who is calling — `IRequestContext`

The caller contract is `SharedKernel.Execution.Context.IRequestContext` (`01.Core`, Foundation tier) — the one
caller contract the whole platform shares: `IsAuthenticated`, `UserId`, `TenantId` (`TenantId?`), `ActorKind`
(`User`/`Service`/`System`/`Anonymous`), `ClientId`, `SessionId`, `CorrelationId` and `HasPermissionAsync`.
A handler that needs it injects it:

```csharp
public sealed class ListMyOrdersHandler(IOrderReadService reads, IRequestContext caller)
    : IQueryHandler<ListMyOrdersQuery, IReadOnlyList<OrderDto>>
{
    public async Task<Result<IReadOnlyList<OrderDto>>> Handle(ListMyOrdersQuery query, CancellationToken ct) =>
        Result<IReadOnlyList<OrderDto>>.Success(await reads.ListForCustomerAsync(caller.UserId!, ct));
}
```

An HTTP service registers it with `SharedKernel.ServiceDefaults.Security`'s `AddSharedKernelRequestContext()`
(over `12.Security`'s `IUserContext`). A caller with no HTTP request uses one of `SharedKernel.Execution`'s own
implementations:

```csharp
// A Temporal activity or a scheduled job: a named identity with exactly the permissions it needs.
services.AddScoped<IRequestContext>(_ => new SystemRequestContext(
    permissions: ["orders.expire", "invoices.issue"],
    identity: "billing-worker"));

// An unauthenticated path: not authenticated, no user, no tenant, no permissions.
services.AddScoped<IRequestContext>(_ => AnonymousRequestContext.Instance);
```

`SystemRequestContext` takes an explicit permission list on purpose. There is no "system bypasses
authorization" mode, because a worker that can do everything is indistinguishable from a bug that does
everything.

### 4. Validating a request

`ValidationBehavior` runs every `IRequestValidator<TRequest>` registered for a request, one after another, and
returns a single `Error.Validation(errors)`; the handler never runs. Write one by hand:

```csharp
public sealed class PlaceOrderValidator : IRequestValidator<PlaceOrderCommand>
{
    public ValueTask<IReadOnlyList<Error>> ValidateAsync(PlaceOrderCommand command, CancellationToken ct)
    {
        if (command.Amount > 0)
            return ValueTask.FromResult<IReadOnlyList<Error>>([]);

        var error = Error.Validation("order.amount.not_positive", "The amount must be positive.") with
        {
            MessageArguments = new Dictionary<string, object?> { [ErrorArgumentNames.PropertyPath] = "Amount" },
        };
        return ValueTask.FromResult<IReadOnlyList<Error>>([error]);
    }
}
```

or keep FluentValidation validators and bridge them at the composition root with
`services.AddFluentValidationRequestValidators()` (`01.Core/SharedKernel.Validation.FluentValidation`). Put the
field path under `ErrorArgumentNames.PropertyPath` — `14.Presentation` keys the ProblemDetails `errors` map by it —
and never put the rejected value in an error.

### 5. Handling a domain event

```csharp
public sealed class SendOrderConfirmation(IEmailSender email) : IDomainEventHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced domainEvent, CancellationToken cancellationToken) =>
        email.SendAsync(domainEvent.CustomerEmail, cancellationToken);
}
```

You implement `IDomainEventHandler<TDomainEvent>` against the **raw domain event** — there is no notification
wrapper. `AddSharedKernelApplication(assemblies, …)` discovers handlers in the scanned assemblies; a handler elsewhere is
registered with `AddDomainEventHandler<OrderPlaced, SendOrderConfirmation>()` (`SharedKernel.Application.Pipeline`).
The native `DomainEventDispatcher` (registered by the same call) resolves them.

**When events are dispatched.** `06.Persistence`'s `SharedKernelDbContext.SaveChangesAsync` collects the events
from tracked aggregates and dispatches them **before** the physical save — on every save path (the unit of
work, a seeder, a factory user) — repeating until handlers raise no more, then writes everything in one save.
Consequences worth internalising:

- **Dispatch is serial**, events in list order, handlers in registration order, exact runtime event type only.
  There is no parallel mode: concurrent handlers would share one `DbContext`, which is not thread-safe.
- **A handler's database changes join the same save and the same transaction.** Inside `TransactionBehavior`
  they commit or roll back with the command.
- **A handler exception abandons the save** and fails the request; nothing is written. Because dispatch happens
  before commit, a handler with an effect outside the database — sending mail, calling another service — must not
  run here: put it behind an integration event (`04.Contracts` + `07.Messaging`) or `ICommandScope.OnCompleted`,
  which runs after the commit.
- **No dispatcher registered**: the events are discarded with a warning.

### 6. Streaming a large read

```csharp
public sealed record ExportOrdersQuery(DateOnly From) : IStreamQuery<OrderRow>;

public sealed class ExportOrdersHandler(IOrderReadService reads)
    : IStreamQueryHandler<ExportOrdersQuery, OrderRow>
{
    public IAsyncEnumerable<OrderRow> Handle(ExportOrdersQuery query, CancellationToken cancellationToken) =>
        reads.StreamAsync(query.From, cancellationToken);
}

await foreach (var row in sender.CreateStream(query, ct))
    await writer.WriteAsync(row, ct);
```

Two deliberate differences from every other request here:

- **Items are not wrapped in `Result<T>`.** A stream's natural error channel is an exception that ends
  enumeration. Wrapping each item would force every consumer to unwrap on every iteration, and a single
  terminal `Result` cannot say "the stream opened fine, then item 4,000 failed."
- **The unary behaviors do not apply.** Streams go through `IStreamPipelineBehavior<,>`, and the platform ships
  none; `StreamRequestPipeline<,>` runs any a service registers. Logging, validation and authorization for a
  stream are otherwise the handler's own responsibility.

### 7. Writing a pipeline behavior

`IPipelineBehavior<TRequest, TResponse>` is MediatR-shaped: `next` is an argument-less
`RequestHandlerContinuation<TResponse>`, so `await next()` reads as it always did.

```csharp
public sealed class TenantRequiredBehavior<TRequest, TResponse>(IRequestContext caller)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerContinuation<TResponse> next, CancellationToken ct) =>
        caller.TenantId is null ? throw new InvalidOperationException("No tenant.") : next();
}
```

Register it into a named stage with `app.WithBehavior(typeof(TenantRequiredBehavior<,>),
PipelineStage.Authorization, typeof(IRequestContext))` on `AddSharedKernelApplication` so it lands in the canonical order.

## Reference

### Namespaces

| Namespace | Holds |
| --- | --- |
| `SharedKernel.Application.Messaging` | `IRequest<TResponse>`, `IRequestHandler<TRequest,TResponse>`, `ISender`, `IPipelineBehavior<TRequest,TResponse>`, `RequestHandlerContinuation<TResponse>`, `ICommandBase`, `ICommand`, `ICommand<TResponse>`, `IQueryBase`, `IQuery<TResponse>`, and the three handler aliases |
| `SharedKernel.Application.Streaming` | `IStreamQuery<TResponse>`, `IStreamQueryHandler<TQuery,TResponse>`, `IStreamPipelineBehavior<,>`, `StreamHandlerContinuation<TResponse>` |
| `SharedKernel.Application.Validation` | `IRequestValidator<TRequest>` |
| `SharedKernel.Application.DomainEvents` | `IDomainEventHandler<TDomainEvent>` |
| `SharedKernel.Application.Authorization` | `RequirePermissionAttribute` |
| `SharedKernel.Application.Idempotency` | `IIdempotentRequest` |
| `SharedKernel.Application.Auditing` | `IAuditableRequest<TResponse>` |
| `SharedKernel.Application.Logging` | `ILoggableRequest<TResponse>` |
| `SharedKernel.Application.Caching` | `ICacheableQuery`, `ICacheableQuery<TValue>`, `IInvalidatesCache`, `CacheKeyRef`, `CacheScope` |
| `SharedKernel.Application.Commands` | `ICommandScope` |

### Messaging vocabulary

| Type | Shape |
| --- | --- |
| `IRequest<TResponse>` | Marker for a request answered by one `TResponse` |
| `IRequestHandler<TRequest,TResponse>` | `Task<TResponse> Handle(TRequest, CancellationToken)` |
| `ISender` | `Send<TResponse>(IRequest<TResponse>, ct)`, `CreateStream<TResponse>(IStreamQuery<TResponse>, ct)` |
| `IPipelineBehavior<TRequest,TResponse>` | `Handle(request, RequestHandlerContinuation<TResponse> next, ct)` |
| `ICommandBase` | Marker. Implemented by both command interfaces, never by a query |
| `ICommand` | `ICommandBase`, `IRequest<Result>` |
| `ICommand<TResponse>` | `ICommandBase`, `IRequest<Result<TResponse>>` |
| `IQueryBase` | Marker. Implemented by `IQuery<TResponse>` |
| `IQuery<TResponse>` | `IQueryBase`, `IRequest<Result<TResponse>>` |
| `ICommandHandler<TCommand>` | `IRequestHandler<TCommand, Result>` |
| `ICommandHandler<TCommand,TResponse>` | `IRequestHandler<TCommand, Result<TResponse>>` |
| `IQueryHandler<TQuery,TResponse>` | `IRequestHandler<TQuery, Result<TResponse>>` |
| `IStreamQuery<TResponse>` | Marker for a streamed request |
| `IStreamQueryHandler<TQuery,TResponse>` | `IAsyncEnumerable<TResponse> Handle(TQuery, CancellationToken)` |

The handler aliases add no members. They exist so a class declaration states its role.

### Request markers

| Marker | Members | Used by |
| --- | --- | --- |
| `[RequirePermission(params permissions)]` | `Permissions` — values of one attribute are alternatives; several attributes all apply; `AllowMultiple`, `Inherited` | `AuthorizationBehavior` (always registered; unauthenticated → 401 `unauthorized.default`, missing permission → 403 `forbidden.insufficient_permission`; streaming queries too) |
| `IIdempotentRequest` | `IdempotencyKey` (reserved per tenant and caller), `Fingerprint` (optional) | `IdempotencyBehavior` (commands only) |
| `IAuditableRequest<TResponse>` | `Action`, `ResourceType`, `ResourceId`, `BeforeSnapshot`, `GetAfterSnapshot(response)` | `AuditingBehavior` |
| `ILoggableRequest<TResponse>` | `LoggableRequestFields`, `GetLoggableResponseFields(response)` | `LoggingBehavior` |
| `ICacheableQuery<TValue>` | `CacheKey`, `CachePolicy`, `Scope`, `RefreshCache`, `ShouldCache(value)` | `CachingBehavior` (`.Pipeline.Caching`) |
| `IInvalidatesCache` | `CacheKeysToInvalidate` (`CacheKeyRef`), `CacheTagsToInvalidate`, `Scope` | `CacheInvalidationBehavior` (`.Pipeline.Caching`) |
| `ICommandScope` (injected, not implemented) | `IsActive`, `IsNested`, `OnCompleted(callback)` | Handlers queuing post-commit work |

## Pitfalls

**A handler that throws for an expected failure.** Throwing skips the failure path the rest of the platform
is built on: the pipeline records an exception outcome instead of an error code, the transaction is not
committed *and* no `Result` reaches the caller to say why. Return `Result.Failure(error)`.

**A request whose response is not `Result`/`Result<T>`.** `IRequest<OrderDto>` compiles, and authorization or
idempotency on it throws at the first short-circuit, in production. Analyzer `SK0040` flags this at build
time — do not suppress it.

**Implementing MediatR's interfaces.** A class implementing `MediatR.IRequestHandler` or `INotificationHandler`
is not discovered by `AddSharedKernelApplication`. Implement the kernel interfaces; the application-layer project
should not reference MediatR at all.

**Expecting the unary behaviors to run for a stream.** They do not. `IStreamQuery<TResponse>` runs through
`IStreamPipelineBehavior<,>` only.

**A `SystemRequestContext` with every permission.** It takes an explicit list so a worker's blast radius is
written down. Granting it everything makes the authorization behavior decorative.

## Testing

A handler needs no harness: it is a class returning a `Result`. `16.Testing`'s Testing-tier packages supply the
doubles:

```csharp
var context = new FakeRequestContext { Permissions = ["orders.place"] };   // SharedKernel.Testing (SharedKernel.Testing.Application)

var result = await new PlaceOrderHandler(repository, new FakeClock()).Handle(command, default);

Assert.True(result.IsSuccess);
```

To test the composed pipeline instead, use `SharedKernel.Application.Testing`'s `ApplicationPipelineTestHarness` —
see the [pipeline README](../SharedKernel.Application.Pipeline/README.md#testing).

## Package

| | |
| --- | --- |
| **Tier** | Abstractions |
| **Depends on** | `SharedKernel.Primitives`, `SharedKernel.Domain`, `SharedKernel.Caching.Abstractions` |
| **Target** | `net10.0` |
| **Public API** | Tracked in `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`; an unrecorded change fails the build |
| **Versioning** | One version across every package in the repo, from a single git tag (MinVer) |

Maintainer rules live in [`05.Application/CLAUDE.md`](../CLAUDE.md); the layer overview is in
[`05.Application/README.md`](../README.md).
