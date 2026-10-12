using SharedKernel.Primitives.Logging;

namespace SharedKernel.Reporting.Gotenberg.Diagnostics;

/// <summary>
/// The <c>SharedKernel.Reporting.Gotenberg</c> sub-block of <c>20.Reporting</c>: EventIds 20400-20499.
/// </summary>
internal static class GotenbergLoggingEventIds
{
    /// <summary>First id of the sub-block (20400).</summary>
    public const int Base = LoggingEventIdRanges.Reporting + (4 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>Gotenberg answered a conversion with a non-success HTTP status code.</summary>
    public const int Rejected = Base + 0;

    /// <summary>Gotenberg could not be reached.</summary>
    public const int Unreachable = Base + 1;
}
