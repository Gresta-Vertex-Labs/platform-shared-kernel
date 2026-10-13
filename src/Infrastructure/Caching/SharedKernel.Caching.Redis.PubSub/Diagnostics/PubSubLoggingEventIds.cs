using SharedKernel.Primitives.Logging;

namespace SharedKernel.Caching.Redis.PubSub.Diagnostics;

/// <summary>
/// The <c>SharedKernel.Caching.Redis.PubSub</c> sub-block of <c>02.Caching</c>: EventIds 2500-2599.
/// </summary>
internal static class PubSubLoggingEventIds
{
    /// <summary>First id of the sub-block (2500).</summary>
    public const int Base = LoggingEventIdRanges.Caching + (5 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>A subscription handler failed.</summary>
    public const int HandlerFailed = Base + 0;

    /// <summary>A typed message could not be deserialized.</summary>
    public const int MessageNotDeserialized = Base + 1;

    /// <summary>Unsubscribing failed.</summary>
    public const int UnsubscribeFailed = Base + 2;
}
