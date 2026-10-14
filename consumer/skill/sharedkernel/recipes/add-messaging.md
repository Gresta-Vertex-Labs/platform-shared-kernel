---
proven-by:
  - samples/Shop/Shop.Contracts/Ordering/OrderEvents.cs
  - samples/Shop/Shop.Contracts/Shop.Contracts.csproj
  - samples/Shop/Ordering/Shop.Ordering.Application/OrderCommands.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/OrderingInfrastructure.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/Persistence/OrderingDbContext.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/Fulfilment/OrderFulfilment.cs
  - samples/Shop/Ordering/Shop.Ordering.Api/Program.cs
  - samples/Shop/Ordering/Shop.Ordering.Api/appsettings.json
  - samples/Shop/Ordering/Shop.Ordering.Tests/OrderingTests.cs
  - samples/Shop/Billing/Shop.Billing.Api/Program.cs
  - samples/Shop/Billing/Shop.Billing.Api/Payments/PaymentCommands.cs
  - samples/Shop/Notify/Shop.Notify.Worker/Program.cs
  - samples/Shop/Notify/Shop.Notify.Worker/Receipts.cs
unproven:
  - Azure Service Bus transport (SharedKernel.Messaging.MassTransit.AzureServiceBus): the reference platform runs RabbitMQ only, because the Service Bus emulator cannot serve MassTransit 8.5 through one connection string
  - Sending a command with IMessageBus: the reference platform only publishes integration events
---
# Add messaging

**Use when:** a service publishes integration events for other services, consumes them, or must publish
atomically with a database write (the outbox).
**Not when:** the steps must run in order with retries, timers and compensation: that is
add-long-running-workflow.md (there are no sagas on the bus). Asking another service for an answer is
add-outbound-call.md (there is no request/response over the bus). In-process domain events stay in the
Application project (`IDomainEventHandler<TEvent>`, new-service.md).

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Shared contracts project (or Application) | `SharedKernel.Contracts` | `IIntegrationEvent`, `[IntegrationEvent]`, `EventEnvelope<T>` |
| Application | `SharedKernel.Messaging.Abstractions` | `IEventPublisher` (and `IMessageBus`) |
| Infrastructure | `SharedKernel.Messaging.MassTransit` | `AddSharedKernelMessaging(configuration)`, `ConsumerBase<T>` |
| Infrastructure | `SharedKernel.Messaging.MassTransit.RabbitMq` | `UseRabbitMq(…)` |
| Infrastructure | `SharedKernel.Messaging.MassTransit.EfCore` | `WithEntityFrameworkOutbox<TDbContext>()` |
| Infrastructure | `SharedKernel.Idempotency.EfCore` or `SharedKernel.Idempotency.Redis` | The `Message` store behind consumer deduplication |
| Api/Worker | `SharedKernel.ServiceDefaults` | `builder.WithMessagingTelemetry()` |

## Steps

1. **Contracts.** An integration event is a `sealed record` implementing `IIntegrationEvent`, with a wire
   name `{context}.{name}` and a version. It lives in the publishing service's contracts project, which
   references `SharedKernel.Contracts` and no other kernel package, so consumers can share it.

```csharp
using SharedKernel.Contracts.Events;

[IntegrationEvent("ordering.order-placed", Version = 1)]
public sealed record OrderPlaced(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid OrderId,
    IReadOnlyList<OrderLineContract> Lines,
    decimal Total,
    string Currency,
    string PaymentToken
) : IIntegrationEvent;
```

   An additive change keeps the version; a breaking one is a new type with the same name and `Version + 1`.

2. **Application.** The handler injects `IEventPublisher` and checks the `Result` it returns. Inside a
   command run by `app.WithTransactions()`, the outbox writes the event in the same transaction as the
   change, and the bus delivers it after the commit.

```csharp
using SharedKernel.Application.Messaging;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using Shop.Contracts.Ordering;

public sealed class PlaceOrderHandler(
    IRepository<Order, OrderId> orders,
    IEventPublisher events,
    IClock clock
) : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand command, CancellationToken ct)
    {
        // … create and add the order
        var published = await events.PublishAsync(
            new OrderPlaced(Guid.CreateVersion7(), clock.UtcNow, order.Id.Value, lines,
                order.Total.Amount, order.Total.Currency.Code, command.PaymentToken),
            ct
        );
        return published.IsFailure
            ? Result<Guid>.Failure(published.Error)
            : Result<Guid>.Success(order.Id.Value);
    }
}
```

3. **Infrastructure: the outbox tables.** With the EF Core outbox, the service's `DbContext` adds
   MassTransit's inbox and outbox entities. In a `TenantedDbContext` they span tenants, so mark them shared.

```csharp
using MassTransit;
using SharedKernel.Persistence.EfCore;

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.AddInboxStateEntity();
    modelBuilder.AddOutboxMessageEntity();
    modelBuilder.AddOutboxStateEntity();
    modelBuilder.Entity<MassTransit.EntityFrameworkCoreIntegration.InboxState>().IsTenantShared();
    modelBuilder.Entity<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().IsTenantShared();
    modelBuilder.Entity<MassTransit.EntityFrameworkCoreIntegration.OutboxState>().IsTenantShared();
}
```

4. **Infrastructure: the bus.** One fluent chain, ending in `Build()`. Register the request context before
   it: `WithInboundRequestContext()` makes the publisher's tenant, actor and correlation id the consumer's
   `IRequestContext`, and also writes them onto outgoing messages, so a publish-only service needs it too.

```csharp
using SharedKernel.Messaging.MassTransit.Extensions;

services
    .AddSharedKernelMessaging(configuration)
    .UseRabbitMq(
        configuration.GetConnectionString("rabbitmq")
            ?? throw new InvalidOperationException("ConnectionStrings:rabbitmq is not configured.")
    )
    .WithEntityFrameworkOutbox<OrderingDbContext>()
    .WithRetry()
    .WithIdempotency()
    .WithInboundRequestContext()
    .AddConsumer<OrderPlacedConsumer>()
    .Build();
```

   A service that only publishes drops `WithIdempotency()` and the consumers; a worker that only consumes
   drops the outbox. `WithIdempotency()` needs a `Message`-purpose store (add-idempotency.md).

   **Azure Service Bus.** Swap `UseRabbitMq(…)` for `UseAzureServiceBus(…)` from
   `SharedKernel.Messaging.MassTransit.AzureServiceBus` and read that package's README. The reference platform
   does not run it, so there is no proven snippet here.

5. **Infrastructure: a consumer.** Derive `ConsumerBase<T>` over the event's envelope. A failure that should be
   retried throws; after the retries the message goes to the error queue.

```csharp
using Microsoft.Extensions.Logging;
using SharedKernel.Contracts.Events;
using SharedKernel.Execution.Context;
using SharedKernel.Messaging.MassTransit.Consumers;
using Shop.Contracts.Ordering;

public sealed class OrderPlacedConsumer(
    IWorkflowDispatcher workflows,
    IRequestContext caller,
    ILogger<OrderPlacedConsumer> logger
) : ConsumerBase<EventEnvelope<OrderPlaced>>(logger)
{
    protected override async Task ConsumeAsync(EventEnvelope<OrderPlaced> envelope, CancellationToken ct)
    {
        var placed = envelope.Data;
        // … act for caller.TenantId; throw to have the bus retry
    }
}
```

6. **Api/Worker.** `builder.WithMessagingTelemetry()` after `AddServiceDefaults()`; the bus registers its own
   readiness probe, mapped by `AddSharedKernelReadiness()`. A worker host with no HTTP endpoints follows the
   same order.

7. **Configuration.** `ConnectionStrings:rabbitmq`, `SharedKernel:Messaging` (the service name is the
   envelope's source), `SharedKernel:Messaging:RabbitMq`, `SharedKernel:Messaging:Idempotency`,
   `SharedKernel:Messaging:DeadLetter`. The keys are in each package README.

## Test it

- Publisher: register `InMemoryEventPublisher` (`SharedKernel.Messaging.Testing`) as the scoped
  `IEventPublisher` and assert with `PublishedOf<OrderPlaced>()`; `InMemoryMessageBus` does the same for
  `IMessageBus`.
- Consumer: construct it with `TestRequestContext.ForTenant(tenant)` (`SharedKernel.Testing`) and a
  substitute `ConsumeContext<EventEnvelope<T>>` whose `Message` is `EventEnvelope.Wrap(evt, "ordering")`; call
  `Consume` twice to prove a redelivery is harmless.
- End to end against RabbitMQ: see write-tests.md.

## Check

- `SK0038` / `SK0039`: an `IIntegrationEvent` without a valid `[IntegrationEvent]` name and version.
- `SK0703`: `IMessageBus` or `IEventPublisher` registered as a singleton.
- `SK0704`: a hard-coded queue or exchange URI; `SK0705`, `SK0708`: fault and batch consumers registered the
  wrong way.
- `SK0007`: Redis pub/sub used where a message belongs on the bus.
- `SK0030`: a publish whose `Result` nobody reads.
- `Build()` fails at startup with the fix in the message: no transport, two transports, or
  `WithIdempotency()` without a `Message` store.

## Read next

`SharedKernel.Messaging.Abstractions`, `SharedKernel.Messaging.MassTransit`,
`SharedKernel.Messaging.MassTransit.RabbitMq`, `SharedKernel.Messaging.MassTransit.EfCore`,
`SharedKernel.Messaging.MassTransit.AzureServiceBus`, `SharedKernel.Contracts`
