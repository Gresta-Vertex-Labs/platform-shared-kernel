using Grpc.Core;
using SharedKernel.Application.Messaging;
using SharedKernel.Core.Extensions;
using Shop.Contracts.Inventory;
using Shop.Inventory.Api.Stock;

namespace Shop.Inventory.Api;

/// <summary>
/// The gRPC face of the reservation use cases. A failed <c>Result</c> is thrown and the kernel's gRPC interceptor turns it
/// into the matching status with rich error details (FailedPrecondition/AlreadyExists for a conflict, NotFound, ...).
/// </summary>
public sealed class InventoryGrpcService(ISender sender) : InventoryService.InventoryServiceBase
{
    public override async Task<ReserveStockReply> ReserveStock(
        ReserveStockRequest request,
        ServerCallContext context
    )
    {
        var lines = request
            .Lines.Select(line => new ReservationLine(line.Sku, line.Quantity))
            .ToList();
        Guid reservation = await sender
            .Send(
                new ReserveStockCommand(Guid.Parse(request.OrderId), lines),
                context.CancellationToken
            )
            .GetValueOrThrow();
        return new ReserveStockReply { ReservationId = reservation.ToString("D") };
    }

    public override async Task<ReleaseStockReply> ReleaseStock(
        ReleaseStockRequest request,
        ServerCallContext context
    )
    {
        int released = await sender
            .Send(new ReleaseStockCommand(Guid.Parse(request.OrderId)), context.CancellationToken)
            .GetValueOrThrow();
        return new ReleaseStockReply { ReleasedLines = released };
    }
}
