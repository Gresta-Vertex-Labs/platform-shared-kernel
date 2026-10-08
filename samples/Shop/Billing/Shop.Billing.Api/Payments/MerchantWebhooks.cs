using SharedKernel.Contracts.Events;
using SharedKernel.Execution.Context;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Primitives.Clocks;
using Shop.Contracts.Billing;

namespace Shop.Billing.Api.Payments;

/// <summary>
/// Tells the merchant a payment was captured. The event id is the payment's, so a retried charge (which answers with
/// the first payment) repeats the same event and the merchant can tell a repeat from a new payment.
/// </summary>
public sealed class PaymentAnnouncements(IWebhookDispatcher webhooks, IClock clock)
{
    public async Task AnnounceCapturedAsync(PaymentView payment, CancellationToken ct)
    {
        // Delivery is retried by the dispatcher and observed through its results; the charge has already committed,
        // so a merchant that is down does not fail the payment.
        _ = await webhooks.DispatchAsync(
            new PaymentCaptured(
                payment.PaymentId,
                clock.UtcNow,
                payment.PaymentId,
                payment.OrderId,
                payment.Amount,
                payment.Currency
            ),
            ct
        );
    }
}

/// <summary>
/// The merchants' webhook endpoints, one per tenant (<c>Billing:Webhooks:{tenantId}:Url</c> and <c>:Secret</c>; the
/// secret comes from Key Vault). Subscriptions belong to the caller's tenant, so a payment of one merchant is never
/// announced to another.
/// </summary>
public sealed class ConfigurationWebhookSubscriptionStore(
    IConfiguration configuration,
    IRequestContext caller
) : IWebhookSubscriptionStore
{
    private static readonly string[] Events =
    [
        IntegrationEventDescriptor.For<PaymentCaptured>().Name,
    ];

    public Task<IReadOnlyList<WebhookSubscription>> GetActiveSubscriptionsAsync(
        string eventType,
        CancellationToken cancellationToken
    )
    {
        if (caller.TenantId is not { } tenant || !Events.Contains(eventType))
        {
            return Task.FromResult<IReadOnlyList<WebhookSubscription>>([]);
        }

        var merchant = configuration.GetSection($"Billing:Webhooks:{tenant.Value:D}");
        if (
            !Uri.TryCreate(merchant["Url"], UriKind.Absolute, out var url)
            || merchant["Secret"] is not { Length: > 0 } secret
        )
        {
            return Task.FromResult<IReadOnlyList<WebhookSubscription>>([]);
        }

        return Task.FromResult<IReadOnlyList<WebhookSubscription>>([
            new WebhookSubscription(tenant.Value, url, [secret], Events, IsActive: true),
        ]);
    }
}
