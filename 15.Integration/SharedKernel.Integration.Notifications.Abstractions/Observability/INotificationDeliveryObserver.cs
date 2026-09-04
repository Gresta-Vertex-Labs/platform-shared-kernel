using SharedKernel.Integration.Notifications.Abstractions.Delivery;

namespace SharedKernel.Integration.Notifications.Abstractions.Observability;

/// <summary>Optional observation hook into the notification send pipeline.</summary>
/// <remarks>
/// Mirrors <c>IWebhookDeliveryObserver</c>'s exact shape. Zero or more observers are registered via
/// <c>WithNotificationDeliveryObserver&lt;T&gt;()</c>. All registered observers are invoked for every
/// send attempt and completion; an observer's exception is caught and logged at
/// <see cref="Microsoft.Extensions.Logging.LogLevel.Warning"/>, never allowed to fault the send
/// outcome — same hard rule as webhooks. This is the seam a consuming service uses to persist a
/// delivery-history ledger against its own <c>06.Persistence</c> stack without this package taking
/// a <c>06.Persistence</c> dependency.
/// </remarks>
public interface INotificationDeliveryObserver
{
    /// <summary>Invoked immediately before the provider send attempt is made.</summary>
    /// <param name="context">The in-flight send's non-generic context.</param>
    /// <param name="attemptNumber">The 1-based attempt number about to be made.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnAttemptAsync(NotificationDeliveryContext context, int attemptNumber, CancellationToken ct);

    /// <summary>Invoked once the send has reached a terminal outcome.</summary>
    /// <param name="context">The in-flight send's non-generic context.</param>
    /// <param name="result">The terminal delivery outcome.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnCompletedAsync(NotificationDeliveryContext context, NotificationDeliveryResult result, CancellationToken ct);
}
