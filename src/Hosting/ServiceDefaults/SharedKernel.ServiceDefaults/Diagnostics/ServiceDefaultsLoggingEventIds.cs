using SharedKernel.Primitives.Logging;

namespace SharedKernel.ServiceDefaults.Diagnostics;

/// <summary>
/// The EventIds of this package: sub-block 0 of the <see cref="LoggingEventIdRanges.ServiceDefaults"/> block
/// (13000-13099).
/// </summary>
internal static class ServiceDefaultsLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.ServiceDefaults + 0 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>A health check was registered.</summary>
    internal const int HealthCheckRegistered = Base + 0;

    /// <summary>A readiness probe threw instead of reporting.</summary>
    internal const int ReadinessProbeThrew = Base + 1;
}
