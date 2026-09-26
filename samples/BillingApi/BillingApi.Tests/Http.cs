using System.Net.Http.Json;
using System.Text.Json;

namespace BillingApi.Tests;

internal static class Http
{
    public const string Read = "billing.read";
    public const string Write = "billing.write";
    public const string Admin = "billing.admin";

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Reads the <c>errorCode</c> member every problem response carries, or <see langword="null"/> without one.</summary>
    public static async Task<string?> ErrorCodeAsync(this HttpResponseMessage response)
    {
        var body = await response.JsonAsync();
        return body.ValueKind == JsonValueKind.Object && body.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    public static async Task<Guid> RegisterCustomerAsync(this HttpClient client, string name, string email, string? taxNumber = null)
    {
        var response = await client.PostAsJsonAsync("/customers", new { name, email, taxNumber });
        await response.EnsureAsync(System.Net.HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> DraftInvoiceAsync(this HttpClient client, Guid customerId, string taxRate = "STD", params (string Description, int Quantity, decimal UnitPrice)[] lines)
    {
        if (lines.Length == 0)
            lines = [("Consulting", 3, 100.50m), ("Travel", 1, 49.50m)];

        var response = await client.PostAsJsonAsync("/invoices", new
        {
            customerId,
            currency = "EUR",
            taxRate,
            lines = lines.Select(l => new { description = l.Description, quantity = l.Quantity, unitPrice = l.UnitPrice }),
        });
        await response.EnsureAsync(System.Net.HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    public static async Task EnsureAsync(this HttpResponseMessage response, System.Net.HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            throw new InvalidOperationException(
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} returned {(int)response.StatusCode}, " +
                $"expected {(int)expected}: {await response.Content.ReadAsStringAsync()}");
        }
    }
}
