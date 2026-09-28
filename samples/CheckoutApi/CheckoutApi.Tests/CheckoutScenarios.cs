using System.Net;
using System.Net.Http.Json;
using CheckoutApi.Features.Checkout;
using FluentAssertions;
using InventoryApi;

namespace CheckoutApi.Tests;

/// <summary>
/// CheckoutApi calls InventoryApi over gRPC (the price) and REST (the reservation), and InventoryApi's answers — or its
/// absence — come back to CheckoutApi's caller as the same errors.
/// </summary>
[Collection(ServicesCollection.Name)]
public sealed class CheckoutScenarios(InventoryHost inventory) : IDisposable
{
    private readonly CheckoutHost _checkout = new(inventory.HttpPort, inventory.GrpcPort);

    [Fact]
    public async Task A_checkout_prices_over_gRPC_and_reserves_over_REST()
    {
        using HttpClient api = _checkout.CreateClient();

        using HttpResponseMessage response = await api.PostAsJsonAsync("/checkout", new PlaceOrderRequest("sku-1", 2));
        Receipt receipt = (await response.Content.ReadFromJsonAsync<Receipt>(CheckoutHost.Json))!;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        receipt.Total.Should().Be(39.98m, "2 × 19.99 EUR, the unit price that travelled as google.type.Money");
        receipt.Currency.Should().Be("EUR");
        inventory.Store.GetReservation(receipt.ReservationId).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task The_callers_correlation_id_and_an_idempotency_key_reach_the_inventory()
    {
        using HttpClient api = _checkout.CreateClient();
        const string correlationId = "0b7f3c1e-5d2a-4c6b-9e8f-7a6b5c4d3e2f";
        api.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        using HttpResponseMessage response = await api.PostAsJsonAsync("/checkout", new PlaceOrderRequest("sku-1", 1));
        Receipt receipt = (await response.Content.ReadFromJsonAsync<Receipt>(CheckoutHost.Json))!;
        Reservation reservation = inventory.Store.GetReservation(receipt.ReservationId).Value;

        reservation.CorrelationId.Should().Be(correlationId);
        reservation.IdempotencyKey.Should().NotBeNullOrEmpty("the REST client sends one with every POST");
    }

    [Fact]
    public async Task A_quote_arrives_over_gRPC()
    {
        using HttpClient api = _checkout.CreateClient();

        Quote quote = (await api.GetFromJsonAsync<Quote>("/quotes/sku-1", CheckoutHost.Json))!;

        quote.Should().BeEquivalentTo(new { Sku = "sku-1", UnitPrice = 19.99m, Currency = "EUR" });
    }

    [Fact]
    public async Task An_unknown_SKU_is_the_inventorys_404_from_its_gRPC_status()
    {
        using HttpClient api = _checkout.CreateClient();

        using HttpResponseMessage response = await api.PostAsJsonAsync("/checkout", new PlaceOrderRequest("sku-404", 1));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CheckoutHost.ErrorCodeAsync(response)).Should().Be(InventoryStore.SkuNotFound);
    }

    [Fact]
    public async Task No_stock_is_the_inventorys_409_from_its_problem_details()
    {
        using HttpClient api = _checkout.CreateClient();

        using HttpResponseMessage response = await api.PostAsJsonAsync("/checkout", new PlaceOrderRequest("sku-2", 1));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CheckoutHost.ErrorCodeAsync(response)).Should().Be(InventoryStore.InsufficientStock);
    }

    [Fact]
    public async Task A_bad_quantity_is_the_inventorys_field_error()
    {
        using HttpClient api = _checkout.CreateClient();

        using HttpResponseMessage response = await api.PostAsJsonAsync("/checkout", new PlaceOrderRequest("sku-1", 0));
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("quantity").And.Contain("Quantity must be positive.");
    }

    [Fact]
    public async Task A_wrong_API_key_is_refused_by_the_inventory()
    {
        using var checkout = new CheckoutHost(inventory.HttpPort, inventory.GrpcPort, apiKey: "wrong-key");
        using HttpClient api = checkout.CreateClient();

        using HttpResponseMessage response = await api.GetAsync("/quotes/sku-1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_inventory_down_is_a_503_not_an_exception()
    {
        // One attempt: a refused connection on Windows takes about 2 s, and three would outlast the 5 s deadline (a timeout).
        using var checkout = new CheckoutHost(
            InventoryHost.FreePort(),
            InventoryHost.FreePort(),
            settings: new Dictionary<string, string?> { ["SharedKernel:Communication:Clients:inventory-grpc:Retry:MaxAttempts"] = "1" });
        using HttpClient api = checkout.CreateClient();

        using HttpResponseMessage response = await api.PostAsJsonAsync("/checkout", new PlaceOrderRequest("sku-1", 1));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CheckoutHost.ErrorCodeAsync(response)).Should().Be("communication.unreachable");
    }

    [Fact]
    public async Task A_retried_reservation_is_made_once()
    {
        using HttpClient rest = inventory.Rest();
        const string key = "5e1d3c2b-1a09-4f8e-8d7c-6b5a4f3e2d1c";
        int before = inventory.Store.ReservationCount;

        async Task<Guid> ReserveAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/reservations") { Content = JsonContent.Create(new { sku = "sku-1", quantity = 1 }) };
            request.Headers.Add("Idempotency-Key", key);
            using HttpResponseMessage response = await rest.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<Reservation>(CheckoutHost.Json))!.Id;
        }

        Guid first = await ReserveAsync();
        Guid second = await ReserveAsync();

        second.Should().Be(first);
        inventory.Store.ReservationCount.Should().Be(before + 1);
    }

    public void Dispose() => _checkout.Dispose();
}
