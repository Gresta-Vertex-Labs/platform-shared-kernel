using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.SignalR.Diagnostics;

/// <summary>
/// The <c>EventId</c>s of this package: sub-block 1 of <c>14.Presentation</c>'s block (14100-14199), built from
/// <see cref="LoggingEventIdRanges"/>. Retired ids (14100, 14102, 14105) are never reused.
/// </summary>
internal static class SignalRLoggingEventIds
{
    /// <summary>The first id of this package's sub-block (14100).</summary>
    public const int Base = LoggingEventIdRanges.Presentation + (1 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>A hub invocation refused by the invocation rate limit (Warning level).</summary>
    public const int InvocationRateLimited = Base + 1;

    /// <summary>A server-category failure of a hub invocation (Error level).</summary>
    public const int ServerError = Base + 3;

    /// <summary>A client-category failure of a hub invocation (Debug level).</summary>
    public const int ClientError = Base + 4;

    /// <summary>A connection that closed during an invocation (Debug level).</summary>
    public const int ConnectionAborted = Base + 6;

    /// <summary>A hub method that returned a stream inside a <c>Result</c> (Error level).</summary>
    public const int StreamInResult = Base + 7;

    /// <summary>A hub method that returned a server-category failure as a <c>Result</c> (Warning level).</summary>
    public const int ServerFailure = Base + 8;
}
