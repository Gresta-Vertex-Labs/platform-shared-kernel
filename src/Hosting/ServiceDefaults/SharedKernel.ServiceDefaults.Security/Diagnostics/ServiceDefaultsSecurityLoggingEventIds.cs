using SharedKernel.Primitives.Logging;

namespace SharedKernel.ServiceDefaults.Security.Diagnostics;

/// <summary>
/// The EventIds of this package: sub-block 2 of the <see cref="LoggingEventIdRanges.ServiceDefaults"/> block
/// (13200-13299).
/// </summary>
internal static class ServiceDefaultsSecurityLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.ServiceDefaults + 2 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>A request carried no correlation id and a new one was created.</summary>
    internal const int CorrelationIdCreated = Base + 0;

    /// <summary>An inbound correlation id failed validation and was replaced.</summary>
    internal const int CorrelationIdRejected = Base + 1;
}
