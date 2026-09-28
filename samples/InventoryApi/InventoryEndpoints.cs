using InventoryApi.Features.Reservations;
using InventoryApi.Features.Stock;
using SharedKernel.Application.Messaging;
using SharedKernel.Presentation.WebApi;

namespace InventoryApi;

/// <summary>
/// The REST side of the inventory. Every endpoint needs the caller's API key; a failure is the platform's RFC 9457
/// problem with its error code, which CheckoutApi's typed client reads back into the same <c>Error</c>.
/// </summary>
public sealed class InventoryEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder inventory = app.MapGroup(string.Empty).RequireAuthorization();

        // GET /stock/sku-1 → 200 StockLevel, or 404 inventory.sku_not_found
        inventory.MapGet("/stock/{sku}", (string sku, ISender sender, CancellationToken ct) =>
            sender.Send(new GetStock(sku), ct).ToOk());

        // POST /reservations { "sku": "sku-1", "quantity": 2 } with an optional Idempotency-Key header: a repeated key
        // returns the reservation it made the first time → 201; 400 for a non-positive quantity; 409 without stock.
        inventory.MapPost("/reservations", (ReserveRequest body, IdempotencyKey? idempotencyKey, ISender sender, CancellationToken ct) =>
            sender.Send(new ReserveStock(body.Sku, body.Quantity, idempotencyKey?.Value), ct)
                .ToCreated(reservation => $"/reservations/{reservation.Id}"));

        inventory.MapGet("/reservations/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
            sender.Send(new GetReservation(id), ct).ToOk());
    }
}

/// <summary>A reservation request.</summary>
public sealed record ReserveRequest(string Sku, int Quantity);
