using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.Dapper.Diagnostics;

/// <summary>
/// The <c>SharedKernel.Persistence.Dapper</c> sub-block of <c>06.Persistence</c>: EventIds 6400-6499.
/// </summary>
internal static class DapperLoggingEventIds
{
    /// <summary>First id of the sub-block (6400).</summary>
    public const int Base = LoggingEventIdRanges.Persistence + (4 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>Rolling back an uncommitted session failed.</summary>
    public const int SessionRollbackFailed = Base + 0;

    /// <summary>A session opened on the cross-tenant database role.</summary>
    public const int CrossTenantSessionOpened = Base + 1;
}
