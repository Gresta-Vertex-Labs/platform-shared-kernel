using SharedKernel.Primitives.Logging;

namespace SharedKernel.Communication.Rest.Internal;

/// <summary>
/// The EventIds of this package: sub-block 2 of the <see cref="LoggingEventIdRanges.Communication"/> block
/// (11200-11299).
/// </summary>
internal static class CommunicationRestLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.Communication + 2 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>The caller could not be written onto a request.</summary>
    internal const int PropagationFailed = Base + 0;

    /// <summary>The problem-details body of an error response could not be read.</summary>
    internal const int ProblemDetailsUnreadable = Base + 1;

    /// <summary>The body of a success response could not be read as the expected type.</summary>
    internal const int SuccessBodyUnreadable = Base + 2;
}
