using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.Diagnostics;

/// <summary>
/// The <c>EventId</c>s of this package: sub-block 5 of <c>14.Presentation</c>'s block (14500-14599), built from
/// <see cref="LoggingEventIdRanges"/>. Retired ids are never reused.
/// </summary>
internal static class PresentationCoreLoggingEventIds
{
    /// <summary>The first id of this package's sub-block (14500).</summary>
    public const int Base = LoggingEventIdRanges.Presentation + (5 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>Authorization refused a request (Warning level).</summary>
    public const int AuthorizationRefused = Base + 0;

    /// <summary>A signed-in principal no <c>IUserContextMapper</c> handles (Warning level).</summary>
    public const int PrincipalWithoutMapper = Base + 1;

    /// <summary>An authentication scheme without an <c>IUserContextMapper</c>, found at startup (Warning level).</summary>
    public const int SchemeWithoutMapper = Base + 2;
}
