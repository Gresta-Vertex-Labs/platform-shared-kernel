using System.Collections.Concurrent;
using System.Text.Json;
using SharedKernel.Integration.Webhooks.Signing;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;
using Shop.Contracts.Billing;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddHealthChecks().AddSharedKernelReadiness();
builder.Services.AddSingleton<ReceivedWebhooks>();

var app = builder.Build();
app.MapDefaultHealthCheckEndpoints();

// Billing's deliveries. The signature covers "{timestamp}.{body}", so the body is read raw, verified, then parsed.
app.MapPost(
    "/webhooks/billing",
    async (HttpRequest request, IConfiguration configuration, ReceivedWebhooks received) =>
    {
        using var reader = new StreamReader(request.Body);
        string body = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
        bool verified = WebhookSignatureVerifier.Verify(
            body,
            request.Headers[WebhookSignatureHeaders.TimestampHeaderName],
            request.Headers[WebhookSignatureHeaders.SignatureHeaderName],
            configuration[MerchantSettings.SecretKey]
        );
        if (!verified)
        {
            return Results.Unauthorized();
        }

        received.Add(
            request.Headers[WebhookSignatureHeaders.DeliveryIdHeaderName].ToString(),
            body
        );
        return Results.NoContent();
    }
);

// What arrived, for the end-to-end tests: one entry per event, however often it was delivered.
app.MapGet("/webhooks/received", (ReceivedWebhooks received) => received.Events);

app.Lifetime.ApplicationStarted.Register(() =>
    app.Services.GetRequiredService<StartupGate>().MarkReady()
);

await app.RunAsync();

/// <summary>Where the merchant keeps its webhook settings.</summary>
internal static class MerchantSettings
{
    public const string SecretKey = "Merchant:Webhooks:Secret";
}

/// <summary>A verified delivery: the event as the merchant read it.</summary>
public sealed record ReceivedWebhook(string DeliveryId, PaymentCaptured Event);

/// <summary>Verified deliveries, deduplicated by event id (a retried delivery repeats the event).</summary>
internal sealed class ReceivedWebhooks
{
    private readonly ConcurrentDictionary<Guid, ReceivedWebhook> _events = new();

    public IReadOnlyCollection<ReceivedWebhook> Events => [.. _events.Values];

    public void Add(string deliveryId, string body)
    {
        // The body is the event itself (this endpoint receives one event type); property names are matched ignoring case.
        var captured = JsonSerializer.Deserialize(
            body,
            BillingJsonContext.Default.PaymentCaptured
        )!;
        _events.TryAdd(captured.EventId, new ReceivedWebhook(deliveryId, captured));
    }
}
