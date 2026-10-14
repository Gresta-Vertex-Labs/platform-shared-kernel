using SharedKernel.Primitives.Logging;

namespace SharedKernel.Integration.Notifications.Sms.Twilio.Diagnostics;

/// <summary>
/// The EventIds of this package: sub-block 3 of the <see cref="LoggingEventIdRanges.Integration"/> block
/// (15300-15399). Sub-block 0 belongs to <c>SharedKernel.Integration.Webhooks</c>, sub-block 1 is reserved for
/// <c>SharedKernel.Integration.Notifications.Abstractions</c> and sub-block 2 belongs to the SendGrid provider.
/// </summary>
internal static class TwilioLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.Integration + 3 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>A delivery succeeded.</summary>
    internal const int DeliverySucceeded = Base + 0;

    /// <summary>A delivery failed.</summary>
    internal const int DeliveryFailed = Base + 1;

    /// <summary>A delivery observer threw.</summary>
    internal const int ObserverException = Base + 2;
}
