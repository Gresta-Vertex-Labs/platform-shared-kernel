using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.GraphQL.Diagnostics;

/// <summary>
/// The <c>EventId</c>s of this package: sub-block 4 of <c>14.Presentation</c>'s block (14400-14499), built from
/// <see cref="LoggingEventIdRanges"/>. Retired ids are never reused.
/// </summary>
internal static class GraphQLLoggingEventIds
{
    /// <summary>The first id of this package's sub-block (14400).</summary>
    public const int Base = LoggingEventIdRanges.Presentation + (4 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>A server-category GraphQL error (Error level).</summary>
    public const int ServerError = Base + 0;

    /// <summary>A client-category GraphQL error (Debug level).</summary>
    public const int ClientError = Base + 1;
}
