using SharedKernel.Primitives.Logging;

namespace SharedKernel.Messaging.MassTransit.Diagnostics;

/// <summary>
/// The EventIds of <c>SharedKernel.Messaging.MassTransit</c>: sub-block 0 of the <see cref="LoggingEventIdRanges.Messaging"/>
/// block (7000-7099). 7006, 7007 and 7010 are retired and never reused; 7010 moved to the Azure Service Bus sub-block.
/// </summary>
internal static class MessagingLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.Messaging + 0 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>A <c>ConsumerBase</c> consume threw.</summary>
    internal const int ConsumeFailed = Base + 1;

    /// <summary>A batch is about to be processed.</summary>
    internal const int BatchProcessing = Base + 2;

    /// <summary>A <c>BatchConsumerBase</c> consume threw.</summary>
    internal const int BatchFailed = Base + 3;

    /// <summary>A fault message is being handed to its fault consumer.</summary>
    internal const int FaultHandling = Base + 4;

    /// <summary>A fault consumer threw.</summary>
    internal const int FaultConsumerFailed = Base + 5;

    /// <summary>A legacy message is being translated.</summary>
    internal const int Translating = Base + 8;

    /// <summary>A translator is registered without a consumer for its new schema.</summary>
    internal const int NoConsumerForNewSchema = Base + 9;

    /// <summary><c>IRequestContext</c> was registered after <c>WithInboundRequestContext()</c>.</summary>
    internal const int RequestContextOverridden = Base + 11;

    /// <summary><c>IRequestContext</c> could not be resolved while checking the registration order.</summary>
    internal const int RequestContextNotResolvable = Base + 12;

    /// <summary>A dispatch verb converted a transport fault into a failed result.</summary>
    internal const int DispatchFailed = Base + 13;

    /// <summary>The scheduler swallowed a transport fault while cancelling a scheduled message.</summary>
    internal const int SchedulerCancelFailed = Base + 14;
}
