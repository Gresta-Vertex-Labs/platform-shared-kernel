using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.PostgreSQL.Diagnostics;

/// <summary>
/// <c>[LoggerMessage]</c> source-generated log events for <c>SharedKernel.Persistence.PostgreSQL</c>.
/// </summary>
/// <remarks>
/// Claims the <c>6500-6599</c> sub-block of <c>01.Core</c>'s <see cref="LoggingEventIdRanges.Persistence"/>
/// (<c>6000-6999</c>) — the next unclaimed 100-wide slot after <c>SharedKernel.Persistence.EfCore</c>
/// (<c>6000-6099</c>), <c>.Npgsql</c> (<c>6100-6199</c>), <c>.Dapper</c> (<c>6200-6299</c>),
/// <c>.EfCore.Encryption</c> (<c>6300-6399</c>), and <c>.EfCore.Auditing</c> (<c>6400-6499</c>), per
/// this domain's per-package sub-block convention. This package's conventions/migration helpers
/// remain build-time/migration-time only and log nothing; only the row-level-security connection
/// interceptor is genuine runtime code.
/// </remarks>
internal static partial class PostgreSqlPersistenceLog
{
    /// <summary>
    /// Logged when resetting a connection's row-level-security session bindings before it returns to
    /// the pool fails. Non-fatal: the connection close itself still proceeds — a connection healthy
    /// enough to fail only this reset is rare, and Npgsql discards a genuinely broken connection
    /// rather than pooling it.
    /// </summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 500,
        Level = LogLevel.Warning,
        Message = "Failed to reset row-level-security session bindings before returning a connection "
            + "to the pool. The connection close proceeded regardless; if the connection is reused "
            + "while still bound, the next lease could inherit a stale tenant binding.")]
    public static partial void RowLevelSecurityResetFailed(this ILogger logger, Exception exception);
}
