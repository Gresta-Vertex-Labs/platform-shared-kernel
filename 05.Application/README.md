# 05.Application

The MediatR-based CQRS plumbing layer for Platform.SharedKernel microservices — command/query/streaming-query vocabulary, the domain-event-to-MediatR bridge, and opt-in cross-cutting pipeline behaviors.

| Package | Role |
| --- | --- |
| [`SharedKernel.Application`](SharedKernel.Application/README.md) | `ICommand` / `ICommand<TResponse>` / `IQuery<TResponse>` / `IStreamQuery<TResponse>`, handler-alias interfaces, `IFireAndForgetCommand`, the domain-event-to-MediatR bridge. Every consuming service references this package. |
| [`SharedKernel.Application.Behaviors`](SharedKernel.Application.Behaviors/README.md) | Ten opt-in unary pipeline behaviors (Logging/Metrics/Tracing/Validation/Authorization/Caching/Resilience/Idempotency/Transaction/CacheInvalidation) plus five streaming behaviors, a fire-and-forget dispatcher, and `ApplicationBehaviorsBuilder`. Reference only if adopting one or more platform behaviors. |

## Quick start for a new microservice

The fastest path to a working pipeline is `AddDefaultBehaviors()` — it registers the four behaviors with zero infrastructure prerequisites (Logging, Metrics, Tracing, Validation). Add the rest one at a time as their supporting infrastructure (a `IUnitOfWork` bridge, a cache, an authorization/idempotency store) becomes available.

```csharp
// Composition root
services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
services.AddValidatorsFromAssemblyContaining<Program>();

services.AddSharedKernelApplication();   // domain-event-to-MediatR bridge

services
    .AddSharedKernelApplicationBehaviors()
    .AddDefaultBehaviors()               // Logging, Metrics, Tracing, Validation — zero prerequisites
    .Build();
```

## Command/query vocabulary quick start

```csharp
public sealed record PlaceOrderCommand(Guid CustomerId, decimal Total) : ICommand<Guid>;

public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand request, CancellationToken ct)
        => Result<Guid>.Success(orderId);
}

public sealed record GetOrderByIdQuery(Guid OrderId) : IQuery<OrderDto>;

public sealed class GetOrderByIdQueryHandler : IQueryHandler<GetOrderByIdQuery, OrderDto>
{
    public Task<Result<OrderDto>> Handle(GetOrderByIdQuery request, CancellationToken ct) { /* ... */ }
}
```

Handlers return `Result` / `Result<T>` exclusively — never `Envelope` / `Envelope<T>` (`04.Contracts`), which is a presentation-boundary type only.

See each package's own README for the full vocabulary (streaming queries, fire-and-forget commands, domain events) and the full ten-behavior registration example, local-seam bridging pattern, and pairing guidance (caching + invalidation, resilience + idempotency).

## Full documentation

[`CLAUDE.md`](CLAUDE.md) is the authoritative source for interface contracts, the canonical pipeline composition order and its positional rationale, hard violations, AOT compatibility notes, DI registration shapes, and test rules.
