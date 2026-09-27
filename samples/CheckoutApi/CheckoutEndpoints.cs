using CheckoutApi.Features.Checkout;
using SharedKernel.Application.Messaging;
using SharedKernel.Presentation.WebApi;

namespace CheckoutApi;

/// <summary>
/// Checkout, over two outbound calls to InventoryApi. InventoryApi's errors come back unchanged: an unknown SKU is 404
/// <c>inventory.sku_not_found</c>, no stock 409 <c>inventory.insufficient_stock</c>, a bad quantity 400 with its field
/// error; InventoryApi down is 503 <c>communication.unreachable</c>.
/// </summary>
public sealed class CheckoutEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // POST /checkout { "sku": "sku-1", "quantity": 2 } → 200 Receipt
        app.MapPost("/checkout", (PlaceOrderRequest body, ISender sender, CancellationToken ct) =>
            sender.Send(new PlaceOrder(body.Sku, body.Quantity), ct).ToOk());

        // GET /quotes/sku-1 → 200 Quote
        app.MapGet("/quotes/{sku}", (string sku, ISender sender, CancellationToken ct) =>
            sender.Send(new GetQuote(sku), ct).ToOk());
    }
}

/// <summary>A checkout request.</summary>
public sealed record PlaceOrderRequest(string Sku, int Quantity);
