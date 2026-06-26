using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Subscriptions;

namespace SharedKernel.Integration.Webhooks.Observability;

/// <summary>
/// Optional observation hook into the webhook delivery pipeline.
/// </summary>
/// <remarks>
/// Zero or more observers are registered via <c>WithDeliveryObserver&lt;T&gt;()</c>. All registered
/// observers are invoked for every attempt and completion; an observer's exception is caught and
/// logged at <see cref="Microsoft.Extensions.Logging.LogLevel.Warning"/>, never allowed to fault the
/// delivery pipeline. This is the seam a consuming service uses to persist a delivery-history ledger
/// against its own <c>06.Persistence</c> stack without this package taking a <c>06.Persistence</c>
/// dependency.
/// </remarks>
public interface IWebhookDeliveryObserver
{
    /// <summary>Invoked immediately before each HTTP attempt is made.</summary>
    /// <param name="subscription">The subscription being delivered to.</param>
    /// <param name="attemptNumber">The 1-based attempt number about to be made.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnAttemptAsync(WebhookSubscription subscription, int attemptNumber, CancellationToken ct);

    /// <summary>Invoked once delivery to a subscription has reached a terminal outcome.</summary>
    /// <param name="subscription">The subscription that was delivered to.</param>
    /// <param name="result">The terminal delivery outcome.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnCompletedAsync(WebhookSubscription subscription, WebhookDeliveryResult result, CancellationToken ct);
}
