using SharedKernel.Primitives.Logging;

namespace SharedKernel.Storage.S3.Diagnostics;

/// <summary>
/// The <c>EventId</c>s of <c>SharedKernel.Storage.S3</c>: the second 100-wide sub-block (8100-8199) of
/// <c>08.Storage</c>'s <see cref="LoggingEventIdRanges.Storage"/> block. OBS logs through this package and declares
/// none of its own (8200-8299 stay reserved); the abstractions package logs nothing (8000-8099).
/// </summary>
/// <remarks>Every field is a <see langword="const"/>, as <c>[LoggerMessage(EventId = ...)]</c> requires.</remarks>
internal static class S3StorageLoggingEventIds
{
    /// <summary>The credentials were denied an operation (Warning).</summary>
    internal const int AccessDenied = LoggingEventIdRanges.Storage + 100;

    /// <summary>The provider was unreachable, throttling or failing (Warning).</summary>
    internal const int Unavailable = LoggingEventIdRanges.Storage + 101;

    /// <summary>The provider rejected a request for another reason (Error).</summary>
    internal const int ProviderError = LoggingEventIdRanges.Storage + 102;

    /// <summary>The provider answered with a caller-driven outcome such as a missing object (Debug).</summary>
    internal const int ExpectedFailure = LoggingEventIdRanges.Storage + 103;

    /// <summary>A store's reachability probe failed (Warning).</summary>
    internal const int ProbeFailed = LoggingEventIdRanges.Storage + 104;

    /// <summary>The bucket is not served by the configured endpoint or region (Error).</summary>
    internal const int WrongRegion = LoggingEventIdRanges.Storage + 105;
}
