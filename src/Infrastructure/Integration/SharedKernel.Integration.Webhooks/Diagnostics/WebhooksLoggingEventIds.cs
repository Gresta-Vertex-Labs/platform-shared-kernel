using SharedKernel.Primitives.Logging;

namespace SharedKernel.Integration.Webhooks.Diagnostics;

/// <summary>
/// The EventIds of this package: sub-block 0 of the <see cref="LoggingEventIdRanges.Integration"/> block
/// (15000-15099). Sub-block 1 (15100-15199) is reserved for <c>SharedKernel.Integration.Notifications.Abstractions</c>,
/// sub-block 2 (15200-15299) belongs to the SendGrid provider and sub-block 3 (15300-15399) to the Twilio provider.
/// </summary>
internal static class WebhooksLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.Integration + 0 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>A delivery observer threw.</summary>
    internal const int ObserverException = Base + 0;

    /// <summary>A delivery succeeded.</summary>
    internal const int DeliverySucceeded = Base + 1;

    /// <summary>A delivery failed with attempts left.</summary>
    internal const int DeliveryFailed = Base + 2;

    /// <summary>A delivery was exhausted.</summary>
    internal const int DeliveryExhausted = Base + 3;

    /// <summary>The exhaustion event was refused by the publisher.</summary>
    internal const int ExhaustionEventNotPublished = Base + 4;

    /// <summary>The per-subscription fault boundary caught an unexpected exception.</summary>
    internal const int DeliveryFaulted = Base + 5;

    /// <summary>The event publisher threw while publishing the exhaustion event.</summary>
    internal const int ExhaustionPublisherFaulted = Base + 6;
}
