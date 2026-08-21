using System.Diagnostics;

namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>
/// Distributed-tracing source for outbound webhook dispatch.
/// </summary>
/// <remarks>
/// <see cref="WebhookDispatcher.DispatchToSubscriptionAsync{TEvent}"/> starts a
/// <c>"WebhookDispatcher.DispatchToSubscription"</c> span (tags: <see cref="WebhookActivityTags.SubscriptionId"/>,
/// <see cref="WebhookActivityTags.EventType"/>, <see cref="WebhookActivityTags.Outcome"/>,
/// <see cref="WebhookActivityTags.AttemptCount"/>); <see cref="WebhookDispatcher.DispatchAsync{TEvent}"/>
/// starts a parent <c>"WebhookDispatcher.Dispatch"</c> span (tags: <see cref="WebhookActivityTags.SubscriptionCount"/>,
/// <see cref="WebhookActivityTags.EventType"/>) around the fan-out. Spans never carry
/// <c>WebhookSubscription.Url</c> or any signing secret as a tag, under any circumstance.
/// </remarks>
public static class WebhookIntegrationActivitySource
{
    /// <summary>The name of the <see cref="ActivitySource"/> used by this package.</summary>
    public const string Name = "SharedKernel.Integration";

    private static readonly ActivitySource Source = new(Name);

    /// <summary>Starts the parent span wrapping <see cref="WebhookDispatcher.DispatchAsync{TEvent}"/>'s fan-out.</summary>
    internal static Activity? StartDispatch(int subscriptionCount, string eventType)
    {
        var activity = Source.StartActivity("WebhookDispatcher.Dispatch");
        if (activity is not null)
        {
            activity.SetTag(WebhookActivityTags.SubscriptionCount, subscriptionCount);
            activity.SetTag(WebhookActivityTags.EventType, eventType);
        }

        return activity;
    }

    /// <summary>
    /// Starts the per-subscription span wrapping
    /// <see cref="WebhookDispatcher.DispatchToSubscriptionAsync{TEvent}"/>.
    /// </summary>
    internal static Activity? StartDispatchToSubscription(Guid subscriptionId, string eventType)
    {
        var activity = Source.StartActivity("WebhookDispatcher.DispatchToSubscription");
        if (activity is not null)
        {
            activity.SetTag(WebhookActivityTags.SubscriptionId, subscriptionId);
            activity.SetTag(WebhookActivityTags.EventType, eventType);
        }

        return activity;
    }
}
