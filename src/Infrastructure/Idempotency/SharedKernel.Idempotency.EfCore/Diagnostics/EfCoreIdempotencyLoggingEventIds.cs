using SharedKernel.Primitives.Logging;

namespace SharedKernel.Idempotency.EfCore.Diagnostics;

/// <summary>The EventIds of this package: sub-block 1 of the <see cref="LoggingEventIdRanges.Idempotency"/> block (18100-18199).</summary>
internal static class EfCoreIdempotencyLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.Idempotency + 1 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>A store outage was let through because fail-open is enabled.</summary>
    internal const int StoreUnavailableFailOpen = Base + 0;
}
