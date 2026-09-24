using OrderApi.Domain;

namespace OrderApi.Features.Orders;

/// <summary>
/// Local persistence seam. The sample deliberately declares its own narrow port rather than
/// referencing <c>SharedKernel.Persistence.Abstractions</c>, so it runs with no database.
/// A real service would inject <c>IRepository&lt;Order, OrderId&gt;</c> instead.
/// </summary>
public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken ct);
    Task<Order?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Order>> ListAsync(CancellationToken ct);
}
