using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Propagation;
using Shop.AppHost;
using Shop.Contracts.Billing;
using Shop.E2E.Infrastructure;
using Xunit;
using static Shop.E2E.Infrastructure.OrderFlow;

namespace Shop.E2E;

/// <summary>
/// Flow 2–6 without payment: an order placed once however often it is submitted, the outbox and RabbitMQ starting a
/// Temporal workflow that reserves stock in Inventory over mutual TLS, SignalR status pushes, tenant isolation,
/// encrypted columns and the audit ledger, and cancelling behind an authenticator step-up.
/// </summary>
[Collection(ShopPlatformCollection.Name)]
public sealed class OrderingFlowTests(ShopPlatform platform)
{
    private const string Alice = ShopResources.Identity.ContosoMerchant;
    private const string Bruno = ShopResources.Identity.FabrikamMerchant;

    [E2EFact]
    public async Task PlacedOrder_IsFulfilledByTheWorkflow_AndStockIsReservedInInventory()
    {
        string sku = await StockAsync(5);
        using var alice = await OrderingAsync(Alice);

        var id = await PlaceAsync(alice, sku, quantity: 2, key: NewKey());
        var order = await WaitForStatusAsync(alice, id, "Confirmed");

        order.ReservationId.Should().NotBeNull();
        (await InventoryLevelAsync(sku)).Reserved.Should().Be(2);
    }

    [E2EFact]
    public async Task SameIdempotencyKey_PlacesOneOrder()
    {
        string sku = await StockAsync(5);
        using var alice = await OrderingAsync(Alice);
        string key = NewKey();

        var first = await PlaceAsync(alice, sku, quantity: 1, key);
        var retried = await PlaceAsync(alice, sku, quantity: 1, key);

        retried.Should().Be(first, "a retried submission replays the first answer");
        await WaitForStatusAsync(alice, first, "Confirmed");
        (await InventoryLevelAsync(sku))
            .Reserved.Should()
            .Be(1, "only one order reached Inventory");
    }

    [E2EFact]
    public async Task MissingIdempotencyKey_IsRefused()
    {
        using var alice = await OrderingAsync(Alice);

        var response = await alice.PostAsJsonAsync("/orders", Order(await StockAsync(1), 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [E2EFact]
    public async Task NotEnoughStock_TheWorkflowRejectsTheOrder()
    {
        string sku = await StockAsync(1);
        using var alice = await OrderingAsync(Alice);

        var id = await PlaceAsync(alice, sku, quantity: 3, key: NewKey());
        var order = await WaitForStatusAsync(alice, id, "Rejected");

        order.RejectionReason.Should().Be("inventory.insufficient_stock");
        (await InventoryLevelAsync(sku)).Reserved.Should().Be(0);
    }

    [E2EFact]
    public async Task StatusChanges_ArePushedOverSignalR_ToTheTenantsClients()
    {
        string sku = await StockAsync(5);
        var notices = new ConcurrentQueue<Notice>();
        await using var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(platform.App.GetEndpoint(ShopResources.Ordering, "http"), "/hubs/orders"),
                options =>
                    options.AccessTokenProvider = async () => await platform.TokenAsync(Alice)
            )
            .Build();
        connection.On<Notice>("orderStatusChanged", notices.Enqueue);
        await connection.StartAsync();

        using var alice = await OrderingAsync(Alice);
        var id = await PlaceAsync(alice, sku, quantity: 1, key: NewKey());

        await Eventually(
            () => notices.Any(n => n.OrderId == id && n.Status == "Confirmed"),
            "the confirmation is pushed"
        );
        notices.Where(n => n.OrderId == id).Select(n => n.Status).Should().StartWith("Placed");
    }

    [E2EFact]
    public async Task AnotherTenant_CannotReadTheOrder()
    {
        using var alice = await OrderingAsync(Alice);
        using var bruno = await OrderingAsync(Bruno);
        var id = await PlaceAsync(alice, await StockAsync(2), quantity: 1, key: NewKey());

        (await bruno.GetAsync($"/orders/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [E2EFact]
    public async Task PersonalData_IsEncryptedAtRest_AndEveryStepIsAudited()
    {
        using var alice = await OrderingAsync(Alice);
        string key = NewKey();
        var id = await PlaceAsync(alice, await StockAsync(2), quantity: 1, key);
        var order = await WaitForStatusAsync(alice, id, "Confirmed");

        var stored = await alice.GetFromJsonAsync<Stored>($"/ops/orders/{id}/stored?key={key}");

        order.CustomerEmail.Should().Be("customer@contoso.example", "reads decrypt");
        stored!.CustomerEmailColumn.Should().NotContain("customer@contoso.example");
        stored.ShippingAddressColumn.Should().NotContain("Main Street");
        stored.AuditActions.Should().Contain(["order.placed", "order.confirmed"]);
    }

    [E2EFact]
    public async Task Cancelling_NeedsAnAuthenticatorStepUp_ThenReleasesTheStock()
    {
        string sku = await StockAsync(4);
        using var alice = await OrderingAsync(Alice);
        var id = await PlaceAsync(alice, sku, quantity: 2, key: NewKey());
        await WaitForStatusAsync(alice, id, "Confirmed");

        var withoutStepUp = await alice.PostAsync($"/orders/{id}/cancel", content: null);
        withoutStepUp
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized, "cancelling needs a recent step-up");

        var enrolled = await (
            await alice.PostAsync("/me/totp", content: null)
        ).Content.ReadFromJsonAsync<Enrollment>();
        string code = new TotpGenerator(new SystemClock()).GenerateCode(
            Base32.Decode(enrolled!.SecretBase32).Value
        );
        (await alice.PostAsJsonAsync("/me/totp/verify", new { Code = code }))
            .StatusCode.Should()
            .Be(HttpStatusCode.NoContent);

        (await alice.PostAsync($"/orders/{id}/cancel", content: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.NoContent);
        (await alice.GetFromJsonAsync<OrderView>($"/orders/{id}"))!.Status.Should().Be("Cancelled");
        (await InventoryLevelAsync(sku))
            .Reserved.Should()
            .Be(0, "cancelling released the reservation");

        using var billing = await platform.ClientAsync(ShopResources.Billing, Alice);
        (await billing.GetFromJsonAsync<PaymentView>($"/payments/by-order/{id}"))!
            .Status.Should()
            .Be("Refunded", "cancelling refunded the payment over REST");
    }

    private readonly OrderFlow _flow = new(platform);

    private Task<HttpClient> OrderingAsync(string user) => _flow.OrderingAsync(user);

    private Task<string> StockAsync(int onHand) => _flow.StockAsync(onHand);

    private Task<StockLevel> InventoryLevelAsync(string sku) => _flow.InventoryLevelAsync(sku);

    private sealed record Notice(Guid OrderId, Guid TenantId, string Status);

    private sealed record Stored(
        string CustomerEmailColumn,
        string ShippingAddressColumn,
        List<string> AuditActions
    );

    private sealed record Enrollment(string SecretBase32, Uri ProvisioningUri);
}
