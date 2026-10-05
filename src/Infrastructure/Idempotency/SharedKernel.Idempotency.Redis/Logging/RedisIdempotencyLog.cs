using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Idempotency.Redis.Logging;

/// <summary>
/// Source-generated <c>[LoggerMessage]</c> log statements for this package, reserved at
/// <see cref="LoggingEventIdRanges.Idempotency"/> + 0..99 (18000-18099), this domain's
/// <c>.Redis</c> sub-block per <c>src/Infrastructure/Idempotency/CLAUDE.md</c>'s Technology table.
/// </summary>
internal static partial class RedisIdempotencyLog
{
    /// <summary>
    /// Logged when a genuine Redis connectivity/timeout failure is caught with
    /// <c>AllowExecutionOnStoreUnavailable = true</c>, and the guarded call is therefore allowed
    /// to proceed as though the key had not yet been processed (Domain Invariant 4).
    /// </summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Idempotency + 0,
        Level = LogLevel.Warning,
        Message = "Redis idempotency store was unavailable during {Operation}. " +
            "AllowExecutionOnStoreUnavailable is enabled, so the guarded call is proceeding as " +
            "not-yet-processed. This increases duplicate-execution risk.")]
    internal static partial void StoreUnavailableFailOpen(ILogger logger, string operation, Exception exception);
}
