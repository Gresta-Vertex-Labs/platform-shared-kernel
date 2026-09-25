using SharedKernel.Domain.Aggregates;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace OrderApi.Domain;

/// <summary>
/// Order aggregate. Demonstrates the platform's aggregate conventions:
/// a strongly-typed id, an injected <see cref="IClock"/> (never <c>DateTime.UtcNow</c> —
/// analyzer SK0001 forbids it), a <see cref="Result{T}"/>-returning factory instead of a
/// throwing constructor, and a domain event raised through the base class.
/// </summary>
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<string> _lines = [];

    public string Customer { get; private set; } = string.Empty;
    public Money Total { get; private set; } = Money.Zero("EUR");
    public IReadOnlyList<string> Lines => _lines.AsReadOnly();

    private Order(OrderId id, IClock clock) : base(id, clock) { }

    /// <summary>ORM materialisation path — never call from application code.</summary>
    private Order() { }

    public static Result<Order> Place(string customer, Money total, IEnumerable<string> lines, IClock clock)
    {
        if (string.IsNullOrWhiteSpace(customer))
            return Result<Order>.Failure(Error.Validation("order.customer", "Customer is required."));

        var items = lines?.Where(l => !string.IsNullOrWhiteSpace(l)).ToList() ?? [];
        if (items.Count == 0)
            return Result<Order>.Failure(Error.Validation("order.lines", "An order needs at least one line."));

        var order = new Order(OrderId.New(), clock)
        {
            Customer = customer.Trim(),
            Total = total,
        };
        order._lines.AddRange(items);

        order.RaiseDomainEvent(ts => new OrderPlacedEvent(
            order.Id.Value, order.Customer, total.Amount, total.Currency) { OccurredOn = ts });

        return Result<Order>.Success(order);
    }
}
