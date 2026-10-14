using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.Grpc.Diagnostics;

/// <summary>
/// The <c>EventId</c>s of this package: sub-block 2 of <c>14.Presentation</c>'s block (14200-14299), built from
/// <see cref="LoggingEventIdRanges"/>. Retired ids (14200, 14201) are never reused.
/// </summary>
internal static class GrpcLoggingEventIds
{
    /// <summary>The first id of this package's sub-block (14200).</summary>
    public const int Base = LoggingEventIdRanges.Presentation + (2 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>A server-category failure of a call (Error level).</summary>
    public const int ServerError = Base + 2;

    /// <summary>A client-category failure of a call (Debug level).</summary>
    public const int ClientError = Base + 3;

    /// <summary>A call cancelled before it completed (Debug level).</summary>
    public const int CallCancelled = Base + 4;
}
