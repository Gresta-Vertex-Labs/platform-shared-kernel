using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SharedKernel.Primitives.Propagation;
using Shop.AppHost;
using Shop.Contracts.Billing;

namespace Shop.E2E.Infrastructure;

/// <summary>An order as its customer reads it from Ordering.</summary>
public sealed record OrderView(
    Guid Id,
    string Status,
    string CustomerEmail,
    Guid? ReservationId,
    string? RejectionReason
);

/// <summary>A SKU's stock as Inventory reports it.</summary>
public sealed record StockLevel(string Sku, int OnHand, int Reserved, int Available);

/// <summary>The steps every order flow shares: stock a SKU, place an order, wait for fulfilment to settle it.</summary>
public sealed class OrderFlow(ShopPlatform platform)
{
    public const string CustomerEmail = "customer@contoso.example";

    public Task<HttpClient> OrderingAsync(string user) =>
        platform.ClientAsync(ShopResources.Ordering, user);

    /// <summary>A fresh SKU of the Contoso merchant with <paramref name="onHand"/> in stock.</summary>
    public async Task<string> StockAsync(int onHand)
    {
        string sku = $"SKU-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        using var inventory = await InventoryAsync();
        (
            await inventory.PutAsJsonAsync($"/stock/{sku}", new { OnHand = onHand })
        ).EnsureSuccessStatusCode();
        return sku;
    }

    public async Task<StockLevel> InventoryLevelAsync(string sku)
    {
        using var inventory = await InventoryAsync();
        return (await inventory.GetFromJsonAsync<StockLevel>($"/stock/{sku}"))!;
    }

    public static object Order(
        string sku,
        int quantity,
        string paymentToken = PaymentTokens.Approved,
        string customerEmail = CustomerEmail
    ) =>
        new
        {
            CustomerEmail = customerEmail,
            ShippingAddress = "1 Main Street, Springfield",
            Currency = "EUR",
            Lines = new[]
            {
                new
                {
                    Sku = sku,
                    Quantity = quantity,
                    UnitPrice = 12.50m,
                },
            },
            PaymentToken = paymentToken,
        };

    public static async Task<Guid> PlaceAsync(
        HttpClient client,
        string sku,
        int quantity,
        string key,
        string paymentToken = PaymentTokens.Approved,
        string customerEmail = CustomerEmail
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = JsonContent.Create(Order(sku, quantity, paymentToken, customerEmail)),
        };
        request.Headers.Add(WellKnownHeaders.IdempotencyKey, key);
        using var response = await client.SendAsync(request);
        response
            .StatusCode.Should()
            .Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Placed>())!.Id;
    }

    public static async Task<OrderView> WaitForStatusAsync(
        HttpClient client,
        Guid id,
        string status
    )
    {
        OrderView? order = null;
        await Eventually(
            async () =>
                (order = await client.GetFromJsonAsync<OrderView>($"/orders/{id}"))!.Status
                == status,
            $"order {id} becomes {status} (last seen: {order?.Status})"
        );
        return order!;
    }

    public static Task Eventually(Func<bool> condition, string because) =>
        Eventually(() => Task.FromResult(condition()), because);

    public static async Task Eventually(Func<Task<bool>> condition, string because)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!await condition())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(60))
            {
                throw new TimeoutException($"Timed out waiting until {because}.");
            }

            await Task.Delay(300);
        }
    }

    public static string NewKey() => Guid.NewGuid().ToString("D");

    private Task<HttpClient> InventoryAsync() =>
        platform.ClientAsync(
            ShopResources.Inventory,
            ShopResources.Identity.ContosoMerchant,
            endpoint: "http"
        );

    private sealed record Placed(Guid Id);
}
