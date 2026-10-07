using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SharedKernel.Security.ApiKey;
using Shop.AppHost;
using Shop.Contracts.Billing;
using Shop.E2E.Infrastructure;
using Xunit;
using static Shop.E2E.Infrastructure.OrderFlow;

namespace Shop.E2E;

/// <summary>
/// Flow 2 with payment: the fulfilment workflow charges Billing over REST with an API key (its hash provisioned in Key
/// Vault and read as configuration), the invoice is signed inside Key Vault, the merchant receives a signed webhook; a
/// declined card compensates (the stock goes back); the payment provider's key, the billing profile (validated IBAN and
/// VAT, the IBAN envelope-encrypted under a Key Vault master key) and GDPR export and erasure.
/// </summary>
[Collection(ShopPlatformCollection.Name)]
public sealed class BillingFlowTests(ShopPlatform platform)
{
    private const string Alice = ShopResources.Identity.ContosoMerchant;
    private const string Bruno = ShopResources.Identity.FabrikamMerchant;

    private readonly OrderFlow _flow = new(platform);

    [E2EFact]
    public async Task PaidOrder_IsCaptured_WithASignedInvoice_AndTheMerchantIsTold()
    {
        var (orderId, _) = await PaidOrderAsync();

        using var billing = await BillingAsync(Alice);
        var payment = (
            await billing.GetFromJsonAsync<PaymentView>($"/payments/by-order/{orderId}")
        )!;
        payment.Status.Should().Be("Captured");
        payment.Amount.Should().Be(25.00m);

        var invoice = await billing.GetFromJsonAsync<Invoice>(
            $"/payments/{payment.PaymentId}/invoice"
        );
        invoice!.SignatureVerified.Should().BeTrue("Key Vault signed the stored bytes");
        invoice.SigningKeyId.Should().Be("invoices");
        invoice.Document.GetProperty("orderId").GetGuid().Should().Be(orderId);

        using var merchant = await platform.ClientAsync(ShopResources.Merchant, user: null);
        await Eventually(
            async () =>
                (await merchant.GetFromJsonAsync<List<Received>>("/webhooks/received"))!.Any(r =>
                    r.Event.OrderId == orderId && r.Event.PaymentId == payment.PaymentId
                ),
            "the merchant received the signed payment-captured webhook"
        );
    }

    [E2EFact]
    public async Task DeclinedCard_RejectsTheOrder_AndTheHeldStockGoesBack()
    {
        string sku = await _flow.StockAsync(5);
        using var alice = await _flow.OrderingAsync(Alice);

        var id = await PlaceAsync(alice, sku, quantity: 2, NewKey(), PaymentTokens.Declined);
        var order = await WaitForStatusAsync(alice, id, "Rejected");

        order.RejectionReason.Should().Be(BillingErrorCodes.PaymentDeclined);
        (await _flow.InventoryLevelAsync(sku))
            .Reserved.Should()
            .Be(0, "the workflow released the stock it had held (compensation)");
        using var billing = await BillingAsync(Alice);
        (await billing.GetAsync($"/payments/by-order/{id}"))
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
    }

    [E2EFact]
    public async Task AnotherTenant_CannotSeeThePayment()
    {
        var (orderId, _) = await PaidOrderAsync();

        using var bruno = await BillingAsync(Bruno);

        (await bruno.GetAsync($"/payments/by-order/{orderId}"))
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
    }

    [E2EFact]
    public async Task ProviderCallback_NeedsTheProvidersKey()
    {
        var (orderId, _) = await PaidOrderAsync();
        using var alice = await BillingAsync(Alice);
        var payment = (await alice.GetFromJsonAsync<PaymentView>($"/payments/by-order/{orderId}"))!;
        var notice = new { payment.PaymentId };

        using var anonymous = await platform.ClientAsync(ShopResources.Billing, user: null);
        (await anonymous.PostAsJsonAsync("/provider/disputes", notice))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);

        using var ordering = WithKey(
            await platform.ClientAsync(ShopResources.Billing, user: null),
            ShopResources.ApiKeys.OrderingService
        );
        (await ordering.PostAsJsonAsync("/provider/disputes", notice))
            .StatusCode.Should()
            .Be(
                HttpStatusCode.Forbidden,
                "the Ordering service's key may charge, not report disputes"
            );

        using var provider = WithKey(
            await platform.ClientAsync(ShopResources.Billing, user: null),
            ShopResources.ApiKeys.PaymentProvider
        );
        var disputed = await provider.PostAsJsonAsync("/provider/disputes", notice);
        disputed
            .StatusCode.Should()
            .Be(HttpStatusCode.OK, await disputed.Content.ReadAsStringAsync());
        (await disputed.Content.ReadFromJsonAsync<PaymentView>())!.Status.Should().Be("Disputed");
    }

    [E2EFact]
    public async Task BillingProfile_ValidatesIbanAndVat_AndKeepsTheIbanEncrypted()
    {
        using var alice = await BillingAsync(Alice);

        var invalid = await alice.PutAsJsonAsync(
            "/billing-profile",
            Profile(iban: "DE89 3704 0044 0532 0130 01")
        );
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync()).Should().Contain("iban");

        (
            await alice.PutAsJsonAsync(
                "/billing-profile",
                Profile(iban: "DE89 3704 0044 0532 0130 00")
            )
        )
            .StatusCode.Should()
            .Be(HttpStatusCode.NoContent);
        var profile = (await alice.GetFromJsonAsync<ProfileView>("/billing-profile"))!;

        profile.VatNumber.Should().Be("DE136695976");
        profile.IbanMasked.Should().EndWith("3000").And.NotContain("370400440532");
    }

    [E2EFact]
    public async Task CustomerData_IsExported_ThenErased_ButThePaymentStays()
    {
        string email = $"gdpr-{Guid.NewGuid():N}@contoso.example";
        var (orderId, _) = await PaidOrderAsync(email);
        using var alice = await BillingAsync(Alice);

        var exported = await alice.PostAsJsonAsync("/privacy/export", new { Email = email });
        exported
            .StatusCode.Should()
            .Be(HttpStatusCode.OK, await exported.Content.ReadAsStringAsync());
        var records = (await exported.Content.ReadFromJsonAsync<List<JsonElement>>())!;
        records
            .Should()
            .ContainSingle()
            .Which.GetProperty("orderId")
            .GetGuid()
            .Should()
            .Be(orderId);

        var erased = await alice.PostAsJsonAsync("/privacy/erase", new { Email = email });
        erased.StatusCode.Should().Be(HttpStatusCode.OK);
        (
            await (
                await alice.PostAsJsonAsync("/privacy/export", new { Email = email })
            ).Content.ReadFromJsonAsync<List<JsonElement>>()
        )
            .Should()
            .BeEmpty("the email is gone");
        (await alice.GetFromJsonAsync<PaymentView>($"/payments/by-order/{orderId}"))!
            .Status.Should()
            .Be("Captured", "the payment is kept for accounting");
    }

    private async Task<(Guid OrderId, string Sku)> PaidOrderAsync(
        string customerEmail = CustomerEmail
    )
    {
        string sku = await _flow.StockAsync(5);
        using var alice = await _flow.OrderingAsync(Alice);
        var id = await PlaceAsync(
            alice,
            sku,
            quantity: 2,
            NewKey(),
            PaymentTokens.Approved,
            customerEmail
        );
        await WaitForStatusAsync(alice, id, "Confirmed");
        return (id, sku);
    }

    private Task<HttpClient> BillingAsync(string user) =>
        platform.ClientAsync(ShopResources.Billing, user);

    private static HttpClient WithKey(HttpClient client, string key)
    {
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, key);
        return client;
    }

    private static object Profile(string iban) =>
        new
        {
            LegalName = "Contoso GmbH",
            Country = "DE",
            VatNumber = "DE136695976",
            Iban = iban,
            Bic = "COBADEFFXXX",
        };

    private sealed record Invoice(
        JsonElement Document,
        string Signature,
        string SigningKeyId,
        bool SignatureVerified
    );

    private sealed record ProfileView(
        string LegalName,
        string Country,
        string VatNumber,
        string? Bic,
        string IbanMasked
    );

    private sealed record Received(string DeliveryId, PaymentCaptured Event);
}
