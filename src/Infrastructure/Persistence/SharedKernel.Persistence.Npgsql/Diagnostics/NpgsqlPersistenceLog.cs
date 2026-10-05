using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.Npgsql.Diagnostics;

/// <summary>
/// <c>[LoggerMessage]</c> source-generated log events for <c>SharedKernel.Persistence.Npgsql</c>.
/// </summary>
/// <remarks>
/// Uses <c>6300-6349</c> of <see cref="LoggingEventIdRanges.Persistence"/> (<c>6000-6999</c>);
/// <c>6350-6399</c> belongs to the row-level security code in <c>SharedKernel.Persistence.EfCore</c> and
/// <c>6400-6499</c> to <c>SharedKernel.Persistence.Dapper</c>.
/// </remarks>
internal static partial class NpgsqlPersistenceLog
{
    /// <summary>Logged once per data source whose TLS mode is below <c>VerifyFull</c> for a non-loopback host.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 300,
        Level = LogLevel.Warning,
        Message = "PostgreSQL data source '{DataSourceName}' uses SSL mode '{SslMode}', below the secure default "
            + "'VerifyFull': the server's identity is not verified.")]
    public static partial void InsecureSslMode(this ILogger logger, string dataSourceName, string sslMode);

    /// <summary>Logged when an advisory migration lock is acquired.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 301,
        Level = LogLevel.Information,
        Message = "Advisory migration lock '{LockKey}' acquired after {ElapsedMilliseconds}ms.")]
    public static partial void AdvisoryMigrationLockAcquired(this ILogger logger, string lockKey, long elapsedMilliseconds);

    /// <summary>Logged when acquiring an advisory migration lock times out.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 302,
        Level = LogLevel.Warning,
        Message = "Advisory migration lock '{LockKey}' was not acquired within {TimeoutMilliseconds}ms; "
            + "another replica is likely holding it.")]
    public static partial void AdvisoryMigrationLockTimedOut(this ILogger logger, string lockKey, long timeoutMilliseconds);

    /// <summary>Logged when an advisory migration lock is released.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 303,
        Level = LogLevel.Information,
        Message = "Advisory migration lock '{LockKey}' released.")]
    public static partial void AdvisoryMigrationLockReleased(this ILogger logger, string lockKey);

    /// <summary>Logged when the row-level security privilege check finds a role that bypasses RLS and is set to warn.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 304,
        Level = LogLevel.Warning,
        Message = "Row-level security privilege check failed for data source '{DataSourceName}': {Problem}")]
    public static partial void RowLevelSecurityPrivilegeProblem(this ILogger logger, string dataSourceName, string problem);

    /// <summary>Logged when the row-level security privilege check could not reach the database.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 305,
        Level = LogLevel.Warning,
        Message = "Row-level security privilege check for data source '{DataSourceName}' could not run: the database "
            + "was not reachable at startup.")]
    public static partial void RowLevelSecurityPrivilegeCheckSkipped(this ILogger logger, string dataSourceName, Exception exception);

    /// <summary>Logged when the row-level security privilege check passed.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 306,
        Level = LogLevel.Information,
        Message = "Row-level security privilege check passed for data source '{DataSourceName}' (role '{RoleName}').")]
    public static partial void RowLevelSecurityPrivilegeCheckPassed(this ILogger logger, string dataSourceName, string roleName);
}
