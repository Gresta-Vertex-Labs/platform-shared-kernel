using SharedKernel.Primitives.Logging;

namespace SharedKernel.Idempotency.Redis.Diagnostics;

/// <summary>The EventIds of this package: sub-block 0 of the <see cref="LoggingEventIdRanges.Idempotency"/> block (18000-18099).</summary>
internal static class RedisIdempotencyLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.Idempotency + 0 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>A store outage was let through because fail-open is enabled.</summary>
    internal const int StoreUnavailableFailOpen = Base + 0;
}
