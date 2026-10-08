using SharedKernel.Contracts.Events;

namespace Shop.Contracts.Ordering;

/// <summary>One line of an order as other services see it.</summary>
public sealed record OrderLineContract(string Sku, int Quantity, decimal UnitPrice);

/// <summary>A customer placed an order. Ordering starts fulfilment on it; Billing and Notify react to it later.</summary>
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

/// <summary>Stock is held for every line of the order.</summary>
[IntegrationEvent("ordering.order-confirmed", Version = 1)]
public sealed record OrderConfirmed(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid OrderId,
    Guid ReservationId,
    decimal Total,
    string Currency
) : IIntegrationEvent;

/// <summary>Fulfilment refused the order.</summary>
[IntegrationEvent("ordering.order-rejected", Version = 1)]
public sealed record OrderRejected(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid OrderId,
    string Reason
) : IIntegrationEvent;

/// <summary>The customer cancelled a confirmed order; its stock was released.</summary>
[IntegrationEvent("ordering.order-cancelled", Version = 1)]
public sealed record OrderCancelled(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId)
    : IIntegrationEvent;
