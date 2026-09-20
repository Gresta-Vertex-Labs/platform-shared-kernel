using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.Npgsql.Diagnostics;

/// <summary>
/// <c>[LoggerMessage]</c> source-generated log events for <c>SharedKernel.Persistence.Npgsql</c>.
/// </summary>
/// <remarks>
/// Claims the <c>6100-6199</c> sub-block of <c>01.Core</c>'s
/// <see cref="LoggingEventIdRanges.Persistence"/> (<c>6000-6999</c>) — the next unclaimed 100-wide
/// slot after <c>SharedKernel.Persistence.EfCore</c>'s <c>6000-6099</c>, per this domain's
/// per-package sub-block convention. <c>06.Persistence/CLAUDE.md</c>'s own sub-block registry is
/// updated in a later documentation wave; this remark is the authoritative record of the
/// claim until then.
/// </remarks>
internal static partial class NpgsqlPersistenceLog
{
    /// <summary>
    /// Logged once, at startup, whenever <c>NpgsqlPersistenceExtensions</c> builds a data source with
    /// <c>NpgsqlPersistenceOptions.AcknowledgeInsecureSslMode</c> set, i.e. a deliberately downgraded
    /// TLS trust level.
    /// </summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 100,
        Level = LogLevel.Warning,
        Message = "PostgreSQL data source '{DataSourceName}' is configured with SslMode '{SslMode}', "
            + "below the secure default 'VerifyFull', because AcknowledgeInsecureSslMode is set. "
            + "This connection's TLS trust is weakened — verify this is intentional (e.g. local development).")]
    public static partial void InsecureSslModeAcknowledged(
        this ILogger logger,
        string dataSourceName,
        string sslMode);

    /// <summary>Logged when an advisory migration lock is successfully acquired.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 101,
        Level = LogLevel.Information,
        Message = "Advisory migration lock '{LockKey}' acquired after {ElapsedMilliseconds}ms.")]
    public static partial void AdvisoryMigrationLockAcquired(
        this ILogger logger,
        string lockKey,
        long elapsedMilliseconds);

    /// <summary>Logged when acquiring an advisory migration lock times out.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 102,
        Level = LogLevel.Warning,
        Message = "Advisory migration lock '{LockKey}' was not acquired within {TimeoutMilliseconds}ms "
            + "— another replica is likely holding it.")]
    public static partial void AdvisoryMigrationLockTimedOut(
        this ILogger logger,
        string lockKey,
        long timeoutMilliseconds);

    /// <summary>Logged when an advisory migration lock is released.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 103,
        Level = LogLevel.Information,
        Message = "Advisory migration lock '{LockKey}' released.")]
    public static partial void AdvisoryMigrationLockReleased(this ILogger logger, string lockKey);
}
