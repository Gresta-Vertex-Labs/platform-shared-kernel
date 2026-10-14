namespace SharedKernel.Integration.Notifications.Abstractions.Tracing;

/// <summary>
/// Single source of truth for the span and metric tag keys of the notification family, domain-local
/// (<c>notification.{snake_name}</c>). The terminal outcome uses
/// <c>SharedKernel.Primitives.Propagation.WellKnownTagKeys.Outcome</c> with <c>WellKnownOutcomes</c> values, and a
/// cross-domain concept reuses <c>WellKnownTagKeys</c> rather than adding a literal here.
/// </summary>
public static class NotificationTagKeys
{
    /// <summary>The notification's <see cref="Notifications.NotificationChannel"/>; also a metric tag.</summary>
    public const string Channel = "notification.channel";

    /// <summary>The caller-supplied delivery id. A span tag only, never a metric tag.</summary>
    public const string DeliveryId = "notification.delivery_id";

    /// <summary>The 1-based send-attempt number.</summary>
    public const string AttemptCount = "notification.attempt_count";
}
