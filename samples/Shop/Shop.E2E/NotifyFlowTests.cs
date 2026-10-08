using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using FluentAssertions;
using Shop.AppHost;
using Shop.Contracts.Billing;
using Shop.E2E.Infrastructure;
using Xunit;
using static Shop.E2E.Infrastructure.OrderFlow;

namespace Shop.E2E;

/// <summary>
/// Flow 2's last leg: a captured payment's receipt leaves Billing through its outbox on RabbitMQ, and Notify
/// emails the customer (SendGrid) and texts the merchant (Twilio). Both providers are WireMock, which records what it
/// was sent; the tests read that back.
/// </summary>
[Collection(ShopPlatformCollection.Name)]
public sealed class NotifyFlowTests(ShopPlatform platform)
{
    private readonly OrderFlow _flow = new(platform);

    [E2EFact]
    public async Task PaidOrder_EmailsTheCustomer_AndTextsTheMerchant()
    {
        string email = $"receipt-{Guid.NewGuid():N}@contoso.example";
        string sku = await _flow.StockAsync(5);
        using var alice = await _flow.OrderingAsync(ShopResources.Identity.ContosoMerchant);
        var orderId = await PlaceAsync(
            alice,
            sku,
            quantity: 2,
            NewKey(),
            PaymentTokens.Approved,
            email
        );
        await WaitForStatusAsync(alice, orderId, "Confirmed");
        // As Notify numbers an order: the random tail of its UUIDv7 id (the head is a timestamp other orders share).
        string orderNumber = orderId.ToString("D")[^8..].ToUpperInvariant();

        using var wireMock = platform.App.CreateHttpClient(ShopResources.WireMock, "http");
        await Eventually(
            async () => (await RequestsAsync(wireMock, "/sendgrid/v3/mail/send", email)).Count == 1,
            "exactly one receipt email reached SendGrid"
        );
        var mail = (await RequestsAsync(wireMock, "/sendgrid/v3/mail/send", email)).Single();
        mail.Should().Contain(orderNumber).And.Contain("d-shop-receipt");

        try
        {
            await Eventually(
                async () => (await RequestsAsync(wireMock, Twilio, orderNumber)).Count == 1,
                "exactly one text reached Twilio"
            );
        }
        catch (TimeoutException)
        {
            // Say what WireMock did receive: no text at all, several, or ones no stub matched.
            var texts = await RequestsAsync(wireMock, Twilio, orderNumber);
            string unmatched = await wireMock.GetStringAsync("/__admin/requests/unmatched");
            throw new TimeoutException(
                $"Expected one text for order {orderNumber}; found {texts.Count}: [{string.Join(" | ", texts)}]. "
                    + $"Unmatched requests: {unmatched}"
            );
        }
        string text = (await RequestsAsync(wireMock, Twilio, orderNumber)).Single();
        text.Should().Contain(Uri.EscapeDataString(ShopResources.Providers.ContosoMerchantPhone));
    }

    private const string Twilio =
        $"/twilio/2010-04-01/Accounts/{ShopResources.Providers.TwilioAccountSid}/Messages.json";

    /// <summary>The bodies of the requests WireMock received on <paramref name="path"/> containing <paramref name="text"/>.</summary>
    private static async Task<List<string>> RequestsAsync(
        HttpClient wireMock,
        string path,
        string text
    )
    {
        using var found = await wireMock.PostAsJsonAsync(
            "/__admin/requests/find",
            new
            {
                method = "POST",
                urlPath = path,
                bodyPatterns = new[] { new { contains = text } },
            }
        );
        found.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await found.Content.ReadAsStringAsync());
        return
        [
            .. document
                .RootElement.GetProperty("requests")
                .EnumerateArray()
                .Select(r => r.GetProperty("body").GetString() ?? string.Empty),
        ];
    }
}
