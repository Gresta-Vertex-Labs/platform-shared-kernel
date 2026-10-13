using SharedKernel.Primitives.Logging;

namespace SharedKernel.ServiceDefaults.Localization.Diagnostics;

/// <summary>
/// The EventIds of this package: sub-block 4 of the <see cref="LoggingEventIdRanges.ServiceDefaults"/> block
/// (13400-13499).
/// </summary>
internal static class ServiceDefaultsLocalizationLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.ServiceDefaults + 4 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>Neither dynamic culture-resolution step can ever resolve a culture.</summary>
    internal const int NoDynamicCultureStepCanResolve = Base + 0;
}
