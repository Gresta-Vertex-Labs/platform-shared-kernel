namespace SharedKernel.Integration.Notifications.Abstractions.Tracing;

/// <summary>
/// Single source of truth for the <see cref="System.Diagnostics.Activity.SetTag(string, object?)"/>
/// attribute-key names emitted by <see cref="NotificationIntegrationActivitySource"/>.
/// </summary>
/// <remarks>
/// None of these concepts overlap an existing cross-domain
/// <c>SharedKernel.Primitives.Propagation.WellKnownTagKeys</c> entry — every key below is
/// domain-local to this package family, following the same single-source-of-truth discipline as
/// <c>WebhookActivityTags</c> (<c>SharedKernel.Integration.Webhooks</c>). Should a future span here
/// need a cross-domain concept (e.g. a tenant-id tag), reference <c>WellKnownTagKeys</c> instead of
/// adding a duplicate literal here.
/// </remarks>
public static class NotificationActivityTags
{
    /// <summary>The tag carrying the notification's <see cref="Notifications.NotificationChannel"/>.</summary>
    public const string Channel = "notification.channel";

    /// <summary>The tag carrying the terminal send outcome (<c>"success"</c> or <c>"failure"</c>).</summary>
    public const string Outcome = "notification.outcome";

    /// <summary>The tag carrying the 1-based send-attempt number.</summary>
    public const string AttemptCount = "notification.attempt_count";
}
