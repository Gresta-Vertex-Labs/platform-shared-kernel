using Grpc.Core;
using InventoryApi.Features.Stock;
using InventoryApi.Grpc;
using SharedKernel.Application.Messaging;
using SharedKernel.Communication;
using SharedKernel.Core.Extensions;
using SharedKernel.Domain.Monetary;

namespace InventoryApi;

/// <summary>
/// The gRPC side of the inventory. A failed query ends in <c>GetValueOrThrow()</c>, which
/// <c>AddSharedKernelGrpc()</c> turns into a rich status — the error code in <c>ErrorInfo</c> — that CheckoutApi's
/// <c>ToResultAsync()</c> reads back.
/// </summary>
public sealed class InventoryGrpcService(ISender sender) : Inventory.InventoryBase
{
    public override async Task<StockReply> GetStock(GetStockRequest request, ServerCallContext context)
    {
        StockLevel stock = await sender.Send(new GetStock(request.Sku), context.CancellationToken).GetValueOrThrow();

        return new StockReply
        {
            Sku = stock.Sku,
            Available = stock.Available,
            UnitPrice = Money.Create(stock.UnitPrice, Currency.Create(stock.Currency).Value).Value.ToMoneyProto(),
        };
    }
}
