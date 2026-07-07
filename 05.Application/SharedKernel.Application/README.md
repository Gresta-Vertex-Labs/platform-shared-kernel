# SharedKernel.Application

MediatR-based CQRS vocabulary for Platform.SharedKernel microservices. Defines `ICommand` / `ICommand<TResponse>` / `IQuery<TResponse>`, the streaming query vocabulary (`IStreamQuery<TResponse>`), the handler-alias interfaces, the fire-and-forget command marker, and the domain-event-to-MediatR bridge. References only `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Domain`, and `MediatR` — never `06.Persistence`, `07.Messaging`, `12.Security`, or `SharedKernel.Caching.Abstractions`.

This package ships **no MediatR registration of its own**. The consuming service owns `services.AddMediatR(...)` and its assembly scanning; this package only adds platform vocabulary on top.

## Included Types

- `ICommandBase` — zero-member marker implemented by both command shapes; never by `IQuery<TResponse>`
- `ICommand` / `ICommand<TResponse>` — MediatR `IRequest<Result>` / `IRequest<Result<TResponse>>` aliases
- `IQuery<TResponse>` — MediatR `IRequest<Result<TResponse>>` alias; does not implement `ICommandBase`
- `ICommandHandler<TCommand>` / `ICommandHandler<TCommand, TResponse>` / `IQueryHandler<TQuery, TResponse>` — pure `IRequestHandler<,>` aliases that let a handler class self-document its CQRS role
- `IStreamQuery<TResponse>` / `IStreamQueryHandler<TQuery, TResponse>` — streaming-query aliases over MediatR's own `IStreamRequest<TResponse>` / `IStreamRequestHandler<,>`; yields raw `TResponse` items, deliberately **not** wrapped in `Result<TResponse>` (errors terminate the stream via a thrown exception — standard `IAsyncEnumerable` semantics)
- `IFireAndForgetCommand` — marker extending `ICommand` that identifies a command intended for enqueue-and-forget dispatch via `SharedKernel.Application.Behaviors`' `IFireAndForgetDispatcher`, never direct `ISender.Send()`
- `IDomainEventHandler<TDomainEvent>` — the contract consuming services implement to react to a `03.Domain` domain event
- `DomainEventNotification<TDomainEvent>` — the `INotification` wrapper that lets a plain `IDomainEvent` travel through MediatR's `IPublisher`
- `MediatRDomainEventDispatcher` + `MediatRDomainEventDispatcherOptions` — `IDomainEventDispatcher` (from `03.Domain`) implementation bridging domain events to MediatR notifications; serial by default, with an opt-in parallel-dispatch mode
- `AddSharedKernelApplication` / `AddDomainEventHandler<TDomainEvent, THandler>` — DI extensions

## Install

```xml
<ProjectReference Include="..\SharedKernel.Application\SharedKernel.Application.csproj" />
```

Or, once published, reference the NuGet package `SharedKernel.Application`.

## Quick Start

```csharp
// Composition root — MediatR registration is NOT this package's responsibility
services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

// Domain-event-to-MediatR bridge (serial dispatch, the default)
services.AddSharedKernelApplication();   // IDomainEventDispatcher -> MediatRDomainEventDispatcher (scoped)
services.AddDomainEventHandler<OrderPlacedDomainEvent, OrderPlacedDomainEventHandler>();

// A command
public sealed record PlaceOrderCommand(Guid CustomerId, decimal Total) : ICommand<Guid>;

public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand request, CancellationToken ct)
    {
        // Stage mutations via injected repositories only.
        return Result<Guid>.Success(orderId);
    }
}

// A query
public sealed record GetOrderByIdQuery(Guid OrderId) : IQuery<OrderDto>;

public sealed class GetOrderByIdQueryHandler : IQueryHandler<GetOrderByIdQuery, OrderDto>
{
    public Task<Result<OrderDto>> Handle(GetOrderByIdQuery request, CancellationToken ct) { /* ... */ }
}

// A domain event handler (consuming service) — the seam into integration events
public sealed class OrderPlacedDomainEventHandler : IDomainEventHandler<OrderPlacedDomainEvent>
{
    private readonly IEventPublisher _eventPublisher; // SharedKernel.Messaging.Abstractions (07.Messaging)

    public OrderPlacedDomainEventHandler(IEventPublisher eventPublisher) => _eventPublisher = eventPublisher;

    public Task Handle(OrderPlacedDomainEvent domainEvent, CancellationToken ct)
        => _eventPublisher.PublishAsync(
            new OrderPlacedIntegrationEvent(domainEvent.Payload.OrderId), ct);
}
```

Handlers return `Result` / `Result<T>` exclusively — never `Envelope` / `Envelope<T>` (`04.Contracts`), which is a presentation-boundary type only.

## Streaming queries

`IStreamQuery<TResponse>` is the platform-vocabulary counterpart to `IQuery<TResponse>` for constant-memory streaming reads. It is a **deliberate, documented deviation** from the `Result<T>` railway used everywhere else: items are yielded raw, and a fault terminates the stream via a thrown exception rather than a per-item or terminal `Result` wrapper.

```csharp
public sealed record ExportOrdersStreamQuery(DateOnly From, DateOnly To) : IStreamQuery<OrderRow>;

public sealed class ExportOrdersStreamQueryHandler : IStreamQueryHandler<ExportOrdersStreamQuery, OrderRow>
{
    public async IAsyncEnumerable<OrderRow> Handle(
        ExportOrdersStreamQuery request, [EnumeratorCancellation] CancellationToken ct)
    {
        // yield return rows one at a time — constant-memory streaming, no Result<T> wrapper per item.
    }
}

// var stream = sender.CreateStream(new ExportOrdersStreamQuery(from, to), ct);
// await foreach (var row in stream) { ... }
```

None of `SharedKernel.Application.Behaviors`' ten unary pipeline behaviors apply to this shape — see that package's README for the five dedicated `IStreamPipelineBehavior<,>` implementations.

## Parallel domain event dispatch

`MediatRDomainEventDispatcher` dispatches serially by default — the safe choice when events share an ordering dependency. Opt in to concurrent dispatch only for independently-observable events with no ordering dependency between them:

```csharp
services.AddSharedKernelApplication(opts => opts.ParallelDispatch = true);
```

When `ParallelDispatch` is `true`, all events from a single `DispatchAsync` call are published concurrently via `Task.WhenAll`; every event is dispatched even if earlier ones fault, and any exceptions are collected into a single `AggregateException` rethrown after all dispatches complete.

## Fire-and-forget commands

`IFireAndForgetCommand` marks a command that must be dispatched via `SharedKernel.Application.Behaviors`' `IFireAndForgetDispatcher.EnqueueAsync(...)` — never `ISender.Send()` directly (a registered guard behavior rejects direct `Send` calls with a descriptive error).

```csharp
// Declared here, in SharedKernel.Application, alongside the rest of the command vocabulary
public sealed record SendWelcomeEmailCommand(Guid UserId) : IFireAndForgetCommand;

public sealed class SendWelcomeEmailCommandHandler : ICommandHandler<SendWelcomeEmailCommand>
{
    public async Task<Result> Handle(SendWelcomeEmailCommand request, CancellationToken ct)
    {
        // Background execution — caller has already received its HTTP response.
        return Result.Success();
    }
}

// Enqueue from any application code holding IFireAndForgetDispatcher (SharedKernel.Application.Behaviors)
public sealed class UserRegisteredDomainEventHandler : IDomainEventHandler<UserRegisteredDomainEvent>
{
    private readonly IFireAndForgetDispatcher _dispatcher;
    public UserRegisteredDomainEventHandler(IFireAndForgetDispatcher dispatcher) => _dispatcher = dispatcher;

    public ValueTask Handle(UserRegisteredDomainEvent domainEvent, CancellationToken ct)
        => _dispatcher.EnqueueAsync(new SendWelcomeEmailCommand(domainEvent.UserId), ct);
}
```

`IFireAndForgetDispatcher`, its bounded-channel dispatcher, and the background consumer that executes enqueued commands are registered via `ApplicationBehaviorsBuilder.AddFireAndForgetDispatch()` in `SharedKernel.Application.Behaviors` — see that package's README for the registration example.

## Opting into pipeline behaviors

Cross-cutting concerns (validation, logging, metrics, tracing, transactions, caching, cache invalidation, authorization, idempotency, resilience, fire-and-forget dispatch, streaming behaviors) are **not** in this package — they live in the separate, opt-in `SharedKernel.Application.Behaviors` package. Reference it only when adopting one or more platform behaviors.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [05.Application/CLAUDE.md](../CLAUDE.md) for the full interface contracts, implementation rules, and pipeline composition order.
