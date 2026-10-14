using SharedKernel.Primitives.Logging;

namespace SharedKernel.MultiTenancy.Diagnostics;

/// <summary>
/// The EventIds of this package: sub-block 1 of the <see cref="LoggingEventIdRanges.ServiceDefaults"/> block
/// (13100-13199).
/// </summary>
internal static class MultiTenancyLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.ServiceDefaults + 1 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>A strategy resolved a tenant for the request.</summary>
    internal const int TenantResolved = Base + 0;

    /// <summary>No strategy resolved a tenant for the request.</summary>
    internal const int TenantNotResolved = Base + 1;
}
