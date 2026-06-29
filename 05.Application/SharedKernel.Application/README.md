# SharedKernel.Application

MediatR-based CQRS vocabulary for Platform.SharedKernel microservices. Defines `ICommand` / `ICommand<TResponse>` / `IQuery<TResponse>`, the handler-alias interfaces, and the domain-event-to-MediatR bridge. References only `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Domain`, and `MediatR` — never `06.Persistence`, `07.Messaging`, `12.Security`, or `SharedKernel.Caching.Abstractions`.

This package ships **no MediatR registration of its own**. The consuming service owns `services.AddMediatR(...)` and its assembly scanning; this package only adds platform vocabulary on top.

## Included Types

- `ICommandBase` — zero-member marker implemented by both command shapes; never by `IQuery<TResponse>`
- `ICommand` / `ICommand<TResponse>` — MediatR `IRequest<Result>` / `IRequest<Result<TResponse>>` aliases
- `IQuery<TResponse>` — MediatR `IRequest<Result<TResponse>>` alias; does not implement `ICommandBase`
- `ICommandHandler<TCommand>` / `ICommandHandler<TCommand, TResponse>` / `IQueryHandler<TQuery, TResponse>` — pure `IRequestHandler<,>` aliases that let a handler class self-document its CQRS role
- `IDomainEventHandler<TDomainEvent>` — the contract consuming services implement to react to a `03.Domain` domain event
- `DomainEventNotification<TDomainEvent>` — the `INotification` wrapper that lets a plain `IDomainEvent` travel through MediatR's `IPublisher`
- `MediatRDomainEventDispatcher` — `IDomainEventDispatcher` (from `03.Domain`) implementation bridging domain events to MediatR notifications
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

// Domain-event-to-MediatR bridge
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

## Opting into pipeline behaviors

Cross-cutting concerns (validation, logging, metrics, transactions, caching, authorization, idempotency) are **not** in this package — they live in the separate, opt-in `SharedKernel.Application.Behaviors` package. Reference it only when adopting one or more platform behaviors.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [05.Application/CLAUDE.md](../CLAUDE.md) for the full interface contracts, implementation rules, and pipeline composition order.
