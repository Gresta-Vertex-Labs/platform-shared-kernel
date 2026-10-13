using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.Abstractions.Diagnostics;

/// <summary>
/// The <c>SharedKernel.Persistence.Abstractions</c> sub-block of <c>06.Persistence</c>: EventIds 6100-6199.
/// </summary>
internal static class AbstractionsLoggingEventIds
{
    /// <summary>First id of the sub-block (6100).</summary>
    public const int Base = LoggingEventIdRanges.Persistence + (1 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>A cross-tenant scope was entered.</summary>
    public const int CrossTenantScopeEntered = Base + 50;
}
