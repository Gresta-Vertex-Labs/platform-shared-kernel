using SharedKernel.Communication;
using SharedKernel.Primitives.Results;
using Shop.Contracts.Inventory;
using Shop.Ordering.Application;
using Shop.Ordering.Domain;

namespace Shop.Ordering.Infrastructure.Inventory;

/// <summary>
/// Inventory over gRPC. The client is the kernel's (11.Communication): mutual TLS with the Shop's certificates, the
/// caller's tenant and correlation id on every call, deadlines and retries, and a gRPC status mapped back to the
/// matching <c>Error</c> — so Inventory's "insufficient stock" arrives here as a business-rule failure.
/// </summary>
public sealed class GrpcInventoryReservations(InventoryService.InventoryServiceClient client)
    : IInventoryReservations
{
    public const string ClientName = "inventory";

    public async Task<Result<Guid>> ReserveAsync(
        OrderId orderId,
        IReadOnlyList<(string Sku, int Quantity)> lines,
        CancellationToken ct
    )
    {
        var request = new ReserveStockRequest { OrderId = orderId.Value.ToString("D") };
        request.Lines.AddRange(
            lines.Select(line => new StockLine { Sku = line.Sku, Quantity = line.Quantity })
        );

        var reply = await client
            .ReserveStockAsync(request, cancellationToken: ct)
            .ToResultAsync(ct);
        return reply.IsFailure
            ? Result<Guid>.Failure(reply.Error)
            : Result<Guid>.Success(Guid.Parse(reply.Value.ReservationId));
    }

    public async Task<Result<int>> ReleaseAsync(OrderId orderId, CancellationToken ct)
    {
        var reply = await client
            .ReleaseStockAsync(
                new ReleaseStockRequest { OrderId = orderId.Value.ToString("D") },
                cancellationToken: ct
            )
            .ToResultAsync(ct);
        return reply.IsFailure
            ? Result<int>.Failure(reply.Error)
            : Result<int>.Success(reply.Value.ReleasedLines);
    }
}
