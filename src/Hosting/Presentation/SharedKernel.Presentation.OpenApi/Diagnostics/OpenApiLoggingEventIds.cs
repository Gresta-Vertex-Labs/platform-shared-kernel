using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.OpenApi.Diagnostics;

/// <summary>
/// The <c>EventId</c>s of this package: sub-block 3 of <c>14.Presentation</c>'s block (14300-14399), built from
/// <see cref="LoggingEventIdRanges"/>. Retired ids are never reused.
/// </summary>
internal static class OpenApiLoggingEventIds
{
    /// <summary>The first id of this package's sub-block (14300).</summary>
    public const int Base = LoggingEventIdRanges.Presentation + (3 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>The documents are not mapped in this environment (Information level).</summary>
    public const int DocumentsNotMapped = Base + 0;

    /// <summary>The documents are served outside Development without authorization (Warning level).</summary>
    public const int DocumentsServedWithoutAuthorization = Base + 1;
}
