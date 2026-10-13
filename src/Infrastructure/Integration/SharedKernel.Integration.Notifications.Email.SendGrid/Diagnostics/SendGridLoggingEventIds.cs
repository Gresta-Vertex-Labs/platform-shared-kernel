using SharedKernel.Primitives.Logging;

namespace SharedKernel.Integration.Notifications.Email.SendGrid.Diagnostics;

/// <summary>
/// The EventIds of this package: sub-block 2 of the <see cref="LoggingEventIdRanges.Integration"/> block
/// (15200-15299). Sub-block 0 belongs to <c>SharedKernel.Integration.Webhooks</c>, sub-block 1 is reserved for
/// <c>SharedKernel.Integration.Notifications.Abstractions</c> and sub-block 3 belongs to the Twilio provider.
/// </summary>
internal static class SendGridLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.Integration + 2 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>A delivery succeeded.</summary>
    internal const int DeliverySucceeded = Base + 0;

    /// <summary>A delivery failed.</summary>
    internal const int DeliveryFailed = Base + 1;

    /// <summary>A delivery observer threw.</summary>
    internal const int ObserverException = Base + 2;

    /// <summary>An attachment could not be read from storage.</summary>
    internal const int AttachmentUnresolvable = Base + 3;
}
