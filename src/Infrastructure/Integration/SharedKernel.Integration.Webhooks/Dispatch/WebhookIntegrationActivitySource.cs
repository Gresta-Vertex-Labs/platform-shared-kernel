using System.Diagnostics;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>
/// Distributed-tracing source for outbound webhook dispatch.
/// </summary>
/// <remarks>
/// <see cref="WebhookDispatcher.DispatchToSubscriptionAsync{TEvent}"/> starts a <c>webhook deliver</c> span (kind
/// Client; tags <see cref="WebhookTagKeys.SubscriptionId"/>, <see cref="WebhookTagKeys.EventType"/>,
/// <see cref="WebhookTagKeys.DeliveryId"/>, <see cref="WebhookTagKeys.AttemptCount"/> and the outcome);
/// <see cref="WebhookDispatcher.DispatchAsync{TEvent}"/> starts a parent <c>webhook dispatch</c> span (kind Internal;
/// tags <see cref="WebhookTagKeys.SubscriptionCount"/> and <see cref="WebhookTagKeys.EventType"/>) around the fan-out.
/// Spans never carry <c>WebhookSubscription.Url</c> or any signing secret as a tag, under any circumstance.
/// </remarks>
public static class WebhookIntegrationActivitySource
{
    /// <summary>The name of the <see cref="ActivitySource"/> used by this package.</summary>
    public const string Name = "SharedKernel.Integration";

    private static readonly ActivitySource Source = new(Name, TelemetryVersion.Of(typeof(WebhookIntegrationActivitySource).Assembly));

    /// <summary>Starts the parent span wrapping the fan-out of <see cref="WebhookDispatcher.DispatchAsync{TEvent}"/>.</summary>
    internal static Activity? StartDispatch(int subscriptionCount, string eventType)
    {
        var activity = Source.StartActivity("webhook dispatch", ActivityKind.Internal);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(WebhookTagKeys.SubscriptionCount, subscriptionCount);
            activity.SetTag(WebhookTagKeys.EventType, eventType);
        }

        return activity;
    }

    /// <summary>
    /// Starts the per-subscription span wrapping <see cref="WebhookDispatcher.DispatchToSubscriptionAsync{TEvent}"/>.
    /// </summary>
    internal static Activity? StartDeliver(Guid subscriptionId, Guid deliveryId, string eventType)
    {
        var activity = Source.StartActivity("webhook deliver", ActivityKind.Client);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(WebhookTagKeys.SubscriptionId, subscriptionId);
            activity.SetTag(WebhookTagKeys.DeliveryId, deliveryId);
            activity.SetTag(WebhookTagKeys.EventType, eventType);
        }

        return activity;
    }
}
