using SharedKernel.Contracts.Events;

namespace SharedKernel.Contracts.Tests;

[IntegrationEvent("orders.order-placed", Version = 2)]
public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId, decimal Total) : IIntegrationEvent
{
    public static OrderPlaced New() =>
        new(Guid.CreateVersion7(), new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero), Guid.NewGuid(), 59.97m);
}

[IntegrationEvent("orders.order-shipped")]
public sealed record OrderShipped(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IIntegrationEvent;

public sealed record Unnamed(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

[IntegrationEvent("Orders.OrderPlaced")]
public sealed record UpperCaseName(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

[IntegrationEvent("orders..placed")]
public sealed record DoubleSeparator(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

[IntegrationEvent("orders.placed", Version = 0)]
public sealed record VersionZero(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

[IntegrationEvent("orders.duplicate")]
public sealed record DuplicateA(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

[IntegrationEvent("orders.duplicate")]
public sealed record DuplicateB(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

[IntegrationEvent("orders.base-event")]
public record BaseEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

public sealed record DerivedWithoutAttribute(Guid EventId, DateTimeOffset OccurredOn) : BaseEvent(EventId, OccurredOn);
