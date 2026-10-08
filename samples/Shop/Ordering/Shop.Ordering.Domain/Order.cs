using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Entities;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Monetary;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace Shop.Ordering.Domain;

/// <summary>Strongly-typed identifier of an <see cref="Order"/>.</summary>
public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static OrderId New() => new(Guid.CreateVersion7());
}

/// <summary>Where an order is in its life.</summary>
public enum OrderStatus
{
    /// <summary>Accepted from the customer; fulfilment has not finished.</summary>
    Placed = 0,

    /// <summary>Stock is held for every line.</summary>
    Confirmed = 1,

    /// <summary>Fulfilment refused it (for example, not enough stock).</summary>
    Rejected = 2,

    /// <summary>Cancelled by the customer after it was confirmed.</summary>
    Cancelled = 3,
}

/// <summary>One line of an order.</summary>
public sealed class OrderLine : Entity<Guid>, IHasTenant
{
    internal OrderLine(string sku, int quantity, Money unitPrice)
        : base(Guid.CreateVersion7())
    {
        Sku = sku;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    private OrderLine() { }

    public string Sku { get; private set; } = string.Empty;

    public int Quantity { get; private set; }

    public Money UnitPrice { get; private set; } = null!;

    /// <summary>The order's tenant: a child row is filtered and guarded like its root (the kernel stamps it).</summary>
    public TenantId TenantId { get; private set; }

    public Money Total => UnitPrice * Quantity;
}

/// <summary>An order a customer placed with a tenant. Status changes raise <see cref="OrderStatusChanged"/>.</summary>
public sealed class Order : TenantedAuditableAggregateRoot<OrderId>
{
    private readonly List<OrderLine> _lines = [];

    private Order(
        OrderId id,
        TenantId tenantId,
        string customerEmail,
        string shippingAddress,
        IClock clock
    )
        : base(id, tenantId, clock)
    {
        CustomerEmail = customerEmail.Trim();
        ShippingAddress = shippingAddress.Trim();
        Status = OrderStatus.Placed;
    }

    private Order() { }

    /// <summary>Personal data: encrypted at rest, found through a blind index.</summary>
    public string CustomerEmail { get; private set; } = string.Empty;

    /// <summary>Personal data: encrypted at rest.</summary>
    public string ShippingAddress { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; }

    /// <summary>The Inventory reservation, once confirmed.</summary>
    public Guid? ReservationId { get; private set; }

    /// <summary>Why fulfilment refused the order.</summary>
    public string? RejectionReason { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    public Money Total =>
        Money.Sum(_lines.Select(line => line.Total), _lines[0].UnitPrice.Currency);

    /// <summary>Places an order with at least one line, every line in one currency.</summary>
    public static ValidationResult<Order> Place(
        TenantId tenantId,
        string customerEmail,
        string shippingAddress,
        IReadOnlyList<(string Sku, int Quantity, Money UnitPrice)> lines,
        IClock clock
    ) =>
        TryCreate(() =>
        {
            CheckRule(new OrderNeedsLines(lines.Count));
            CheckRule(
                new OneCurrencyPerOrder(
                    lines.Select(line => line.UnitPrice.Currency).Distinct().Count()
                )
            );
            var order = new Order(OrderId.New(), tenantId, customerEmail, shippingAddress, clock);
            foreach (var (sku, quantity, unitPrice) in lines)
            {
                CheckRule(new QuantityIsPositive(quantity));
                order._lines.Add(new OrderLine(sku.Trim().ToUpperInvariant(), quantity, unitPrice));
            }

            order.RaiseDomainEvent(at => new OrderStatusChanged(
                order.Id,
                tenantId,
                OrderStatus.Placed
            )
            {
                OccurredOn = at,
            });
            return order;
        });

    /// <summary>Stock is held: the order is confirmed. Idempotent for the same reservation.</summary>
    public Result Confirm(Guid reservationId)
    {
        if (Status == OrderStatus.Confirmed && ReservationId == reservationId)
        {
            return Result.Success();
        }

        if (Status != OrderStatus.Placed)
        {
            return Result.Failure(OrderErrors.InvalidTransition(Id, Status, OrderStatus.Confirmed));
        }

        Status = OrderStatus.Confirmed;
        ReservationId = reservationId;
        RaiseDomainEvent(at => new OrderStatusChanged(Id, TenantId, Status) { OccurredOn = at });
        return Result.Success();
    }

    /// <summary>Fulfilment refused the order. Idempotent.</summary>
    public Result Reject(string reason)
    {
        if (Status == OrderStatus.Rejected)
        {
            return Result.Success();
        }

        if (Status != OrderStatus.Placed)
        {
            return Result.Failure(OrderErrors.InvalidTransition(Id, Status, OrderStatus.Rejected));
        }

        Status = OrderStatus.Rejected;
        RejectionReason = reason;
        RaiseDomainEvent(at => new OrderStatusChanged(Id, TenantId, Status) { OccurredOn = at });
        return Result.Success();
    }

    /// <summary>The customer cancels a confirmed order.</summary>
    public Result Cancel()
    {
        if (Status != OrderStatus.Confirmed)
        {
            return Result.Failure(OrderErrors.InvalidTransition(Id, Status, OrderStatus.Cancelled));
        }

        Status = OrderStatus.Cancelled;
        RaiseDomainEvent(at => new OrderStatusChanged(Id, TenantId, Status) { OccurredOn = at });
        return Result.Success();
    }
}

/// <summary>Raised whenever an order changes status (including when it is placed).</summary>
[DomainEventVersion(1)]
public sealed record OrderStatusChanged(OrderId OrderId, TenantId TenantId, OrderStatus Status)
    : DomainEvent;

/// <summary>The errors of the order aggregate.</summary>
public static class OrderErrors
{
    public static Error InvalidTransition(OrderId id, OrderStatus from, OrderStatus to) =>
        Error.Conflict(
            "ordering.order.invalid_transition",
            $"Order {id.Value} is {from}; it cannot become {to}."
        );
}

public sealed class OrderNeedsLines(int lines) : IBusinessRule
{
    public string Code => "ordering.order.no_lines";

    public string Message => "An order needs at least one line.";

    public bool IsBroken() => lines == 0;
}

public sealed class OneCurrencyPerOrder(int currencies) : IBusinessRule
{
    public string Code => "ordering.order.mixed_currencies";

    public string Message => "Every line of an order must be in the same currency.";

    public bool IsBroken() => currencies > 1;
}

public sealed class QuantityIsPositive(int quantity) : IBusinessRule
{
    public string Code => "ordering.order.quantity_not_positive";

    public string Message => "A line's quantity must be greater than zero.";

    public bool IsBroken() => quantity <= 0;
}
