using SharedKernel.Application.Messaging;
using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Results;

namespace InventoryApi.Features.Stock;

/// <summary>A SKU's stock and unit price.</summary>
/// <param name="Sku">The SKU.</param>
public sealed record GetStock(string Sku) : IQuery<StockLevel>;

public sealed class GetStockHandler(InventoryStore inventory) : IQueryHandler<GetStock, StockLevel>
{
    public Task<Result<StockLevel>> Handle(GetStock query, CancellationToken cancellationToken) =>
        Task.FromResult(inventory.Find(query.Sku).Map(entry =>
            new StockLevel(query.Sku, entry.Available, entry.UnitPrice.Amount, entry.UnitPrice.Currency.Code)));
}
