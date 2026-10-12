using Microsoft.Extensions.Logging;

namespace SharedKernel.Idempotency.Redis.Diagnostics;

/// <summary>
/// Source-generated <c>[LoggerMessage]</c> log statements for this package. The ids are
/// <see cref="RedisIdempotencyLoggingEventIds"/> (18000-18099).
/// </summary>
/// <remarks>The idempotency key, fingerprint, token, tenant and stored response are never log values.</remarks>
internal static partial class RedisIdempotencyLog
{
    /// <summary>
    /// Logged when a Redis connectivity or timeout failure is caught with
    /// <c>AllowExecutionOnStoreUnavailable = true</c> and the guarded call therefore proceeds as not yet processed.
    /// </summary>
    [LoggerMessage(
        EventId = RedisIdempotencyLoggingEventIds.StoreUnavailableFailOpen,
        Level = LogLevel.Warning,
        Message = "The idempotency store was unavailable during '{Operation}' for purpose '{Purpose}', so the guarded call proceeds without duplicate protection.")]
    internal static partial void StoreUnavailableFailOpen(ILogger logger, string operation, string purpose, Exception exception);
}
