using System.Collections.Concurrent;
using OrderApi.Domain;
using OrderApi.Features.Orders;

namespace OrderApi.Infrastructure;

/// <summary>
/// In-memory store so the sample runs with no database. A real service would use
/// <c>SharedKernel.Persistence.EfCore</c>'s <c>EfRepository&lt;Order, OrderId&gt;</c>.
/// </summary>
public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    public Task AddAsync(Order order, CancellationToken ct)
    {
        _orders[order.Id.Value] = order;
        return Task.CompletedTask;
    }

    public Task<Order?> GetAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_orders.TryGetValue(id, out var order) ? order : null);

    public Task<IReadOnlyList<Order>> ListAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Order>>(_orders.Values.ToList());
}
