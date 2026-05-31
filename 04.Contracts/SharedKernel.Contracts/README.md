# SharedKernel.Contracts

Cross-service DTO layer for the Platform.SharedKernel mono-repo. This package is the lingua franca between microservices — every shared payload, integration event, and transport wrapper that must cross a service boundary lives here.

---

## 1. Purpose and Boundaries

### What belongs here

- **`PagedList<T>`** — paged result DTO for cross-service list queries
- **`Envelope` / `Envelope<T>`** — transport envelopes for void and typed operation results at service boundaries
- **`IIntegrationEvent`** — marker interface for integration event payloads
- **`EventEnvelope<TEvent>`** — messaging transport wrapper carrying routing and tracing metadata alongside a domain event
- **`ContractsJsonContext`** — STJ source-generated context base for AOT-safe serialization

### What does NOT belong here

| Concern | Correct home |
|---------|-------------|
| Domain entities, aggregates, value objects | `03.Domain` |
| Railway-oriented `Result<T>` / `Result` | `01.Core / SharedKernel.Primitives` |
| MediatR commands, queries, pipeline behaviors | `05.Application` |
| EF Core, repositories, `DbContext` | `06.Persistence` |
| `IMessageBus`, consumer registration, MassTransit types | `07.Messaging` |
| Validation rules, business invariants | `03.Domain` or `05.Application` |

All types in this package are **pure DTOs** — no behavior, no validation rules, no domain logic.

---

## 2. Quick-Start Examples

### PagedList\<T\>

```csharp
// Creating a paged result (Create is the only valid construction path)
var page = PagedList<OrderDto>.Create(
    items: orders,
    page: 1,       // 1-based — page 1 is the first page
    pageSize: 20,
    totalCount: 157);

Console.WriteLine(page.TotalPages);      // 8
Console.WriteLine(page.HasNextPage);     // true
Console.WriteLine(page.HasPreviousPage); // false
```

### Envelope (void operation)

```csharp
// At the communication boundary — after an application-layer Result is resolved:
Envelope ok = Envelope.Ok();
Envelope fail = Envelope.Fail(Error.Create("order.notFound", "Order not found."));

// Implicit operator (sugar for Fail):
Envelope fromError = Error.Create("order.notFound", "Order not found.");
```

### Envelope\<T\> (typed result)

```csharp
Envelope<OrderDto> ok   = Envelope<OrderDto>.Ok(dto);
Envelope<OrderDto> fail = Envelope<OrderDto>.Fail(Error.Create("order.notFound", "Order not found."));

// Implicit operators:
Envelope<OrderDto> fromValue = dto;                    // wraps Ok
Envelope<OrderDto> fromError = someError;              // wraps Fail
```

### IIntegrationEvent

```csharp
// Integration events are public projections of domain events.
// Always use sealed record or sealed class.
public sealed record OrderPlacedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid OrderId,
    string CustomerId,
    decimal TotalAmount) : IIntegrationEvent;
```

### EventEnvelope\<TEvent\>

```csharp
// Wrap a domain event for publishing (07.Messaging calls this internally):
var envelope = EventEnvelope.Wrap(
    domainEvent:   orderPlacedDomainEvent,
    sourceService: "orders-service",
    correlationId: Activity.Current?.TraceId.ToString(),
    causationId:   commandCorrelationId);

// envelope.EventId      == domainEvent.Id   (copied, not new)
// envelope.EventType    == "OrderPlacedEvent"
// envelope.EventVersion == 1  (or value from [DomainEventVersionAttribute])
// envelope.Payload      == orderPlacedDomainEvent
```

---

## 3. Result\<T\> vs Envelope\<T\> — Boundary Rule

These two types are **not interchangeable**. They serve different roles in different layers:

| Type | Layer | Purpose |
|------|-------|---------|
| `Result<T>` (from `SharedKernel.Primitives`) | Application / Domain | Railway-oriented error propagation within a single service |
| `Envelope<T>` (this package) | Presentation / Communication | Serializable transport payload across a service boundary |

**The rule:**

```
Application layer  →  returns Result<T>
Communication layer  →  maps Result<T> to Envelope<T>  →  serializes across the boundary
```

Never serialize `Result<T>` as a payload. Never return `Envelope<T>` from an application-layer method (MediatR handler, domain service).

Example mapping at the HTTP presentation boundary:

```csharp
// In a minimal API or controller:
var result = await mediator.Send(new PlaceOrderCommand(request));

return result.IsSuccess
    ? Results.Ok(Envelope<OrderDto>.Ok(result.Value!))
    : Results.UnprocessableEntity(Envelope<OrderDto>.Fail(result.Error!));
```

---

## 4. STJ Usage Pattern for Consuming Services

`ContractsJsonContext` is `internal` — consuming services must not reference it directly. Instead:

**Step 1** — declare your own `partial JsonSerializerContext` with concrete type arguments:

```csharp
using System.Text.Json.Serialization;
using SharedKernel.Contracts.Events;
using SharedKernel.Contracts.Pagination;

[JsonSerializable(typeof(EventEnvelope<OrderPlacedEvent>))]
[JsonSerializable(typeof(EventEnvelope<OrderCancelledEvent>))]
[JsonSerializable(typeof(PagedList<OrderDto>))]
[JsonSerializable(typeof(Envelope<OrderDto>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class OrdersServiceJsonContext : JsonSerializerContext { }
```

**Step 2** — merge into a single `JsonSerializerOptions` at service startup:

```csharp
var options = new JsonSerializerOptions();
options.TypeInfoResolverChain.Add(OrdersServiceJsonContext.Default);
// ContractsJsonContext.Default covers base types (Envelope, PagedList<object>, etc.)
// Access via InternalsVisibleTo if needed in tests; in production code use your own context.

// Register with ASP.NET Core:
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.TypeInfoResolverChain.Add(OrdersServiceJsonContext.Default);
});
```

**Why the chain pattern?**

`EventEnvelope<OrderPlacedEvent>` is a generic type instantiation. STJ source generation requires a
`[JsonSerializable(typeof(EventEnvelope<OrderPlacedEvent>))]` entry to exist in a context that
the serializer can reach. Because this package does not know your concrete event types at compile
time, you supply those entries in your own context and the chain merges both.

---

## 5. EventEnvelope\<TEvent\> — Composition Pattern for 07.Messaging

`07.Messaging` uses `EventEnvelope<TEvent>` as the canonical wire format for all published domain events. The composition flow is:

```
Domain raises IDomainEvent
    ↓
Application layer commits the aggregate (UoW)
    ↓
07.Messaging outbox reads raised domain events
    ↓
EventEnvelope.Wrap(domainEvent, sourceService, correlationId, causationId)
    ↓
Serialized to JSON via consuming service's JsonSerializerContext
    ↓
Published to RabbitMQ / Azure Service Bus topic
```

The `SourceService` value is injected at the composition root:

```csharp
// In 07.Messaging configuration (pseudo-code):
services.AddEventPublisher(options =>
{
    options.SourceService = configuration["ServiceName"]; // e.g. "orders-service"
});
```

The `CorrelationId` is propagated from the ambient OpenTelemetry `Activity`:

```csharp
// Inside the event publisher:
var envelope = EventEnvelope.Wrap(
    domainEvent:   evt,
    sourceService: _options.SourceService,
    correlationId: Activity.Current?.TraceId.ToString(),
    causationId:   _currentCommandId);
```

Consumers deserialize the envelope, read `EventType` for routing, and deserialize `Payload` into
the concrete integration event type using the source-generated context:

```csharp
// Consumer side:
var envelope = JsonSerializer.Deserialize<EventEnvelope<OrderPlacedEvent>>(
    json,
    OrdersServiceJsonContext.Default.EventEnvelopeOrderPlacedEvent);

var payload = envelope!.Payload; // OrderPlacedEvent
```

---

## Package Dependencies

```
SharedKernel.Contracts
    → SharedKernel.Primitives  (01.Core — Error, Result<T>)
    → SharedKernel.Domain      (03.Domain — IDomainEvent, DomainEventVersionHelper)
    → System.Text.Json         (in-box with net10.0)
```

Zero external NuGet dependencies.
