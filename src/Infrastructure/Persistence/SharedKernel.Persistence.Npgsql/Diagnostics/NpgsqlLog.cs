using Microsoft.Extensions.Logging;

namespace SharedKernel.Persistence.Npgsql.Diagnostics;

/// <summary>
/// Log statements of <c>SharedKernel.Persistence.Npgsql</c>. They name data sources, roles and lock names, never a
/// host, a connection string, a tenant or a statement.
/// </summary>
internal static partial class NpgsqlLog
{
    /// <summary>Logged once per data source whose TLS mode is below <c>VerifyFull</c> for a non-loopback host.</summary>
    [LoggerMessage(
        EventId = NpgsqlLoggingEventIds.InsecureSslMode,
        Level = LogLevel.Warning,
        Message = "PostgreSQL data source '{DataSourceName}' uses SSL mode '{SslMode}', below the secure default 'VerifyFull', so the server identity is not verified.")]
    public static partial void InsecureSslMode(ILogger logger, string dataSourceName, string sslMode);

    /// <summary>Logged when an advisory migration lock is acquired.</summary>
    [LoggerMessage(
        EventId = NpgsqlLoggingEventIds.AdvisoryMigrationLockAcquired,
        Level = LogLevel.Debug,
        Message = "Advisory migration lock '{LockKey}' was acquired after {ElapsedMilliseconds} ms.")]
    public static partial void AdvisoryMigrationLockAcquired(ILogger logger, string lockKey, long elapsedMilliseconds);

    /// <summary>Logged when acquiring an advisory migration lock times out.</summary>
    [LoggerMessage(
        EventId = NpgsqlLoggingEventIds.AdvisoryMigrationLockTimedOut,
        Level = LogLevel.Warning,
        Message = "Advisory migration lock '{LockKey}' was not acquired within {TimeoutMilliseconds} ms, so another replica is likely holding it.")]
    public static partial void AdvisoryMigrationLockTimedOut(ILogger logger, string lockKey, long timeoutMilliseconds);

    /// <summary>Logged when an advisory migration lock is released.</summary>
    [LoggerMessage(
        EventId = NpgsqlLoggingEventIds.AdvisoryMigrationLockReleased,
        Level = LogLevel.Debug,
        Message = "Advisory migration lock '{LockKey}' was released.")]
    public static partial void AdvisoryMigrationLockReleased(ILogger logger, string lockKey);

    /// <summary>Logged when the row-level security privilege check finds a role that bypasses it and is set to warn.</summary>
    [LoggerMessage(
        EventId = NpgsqlLoggingEventIds.RowLevelSecurityPrivilegeProblem,
        Level = LogLevel.Warning,
        Message = "The row-level security privilege check of data source '{DataSourceName}' failed with cause {Cause}.")]
    public static partial void RowLevelSecurityPrivilegeProblem(ILogger logger, string dataSourceName, RowLevelSecurityPrivilegeProblem cause);

    /// <summary>Logged when the row-level security privilege check could not reach the database.</summary>
    [LoggerMessage(
        EventId = NpgsqlLoggingEventIds.RowLevelSecurityPrivilegeCheckSkipped,
        Level = LogLevel.Warning,
        Message = "The row-level security privilege check of data source '{DataSourceName}' did not run because the database was not reachable at startup.")]
    public static partial void RowLevelSecurityPrivilegeCheckSkipped(ILogger logger, string dataSourceName, Exception exception);

    /// <summary>Logged when the row-level security privilege check passed.</summary>
    [LoggerMessage(
        EventId = NpgsqlLoggingEventIds.RowLevelSecurityPrivilegeCheckPassed,
        Level = LogLevel.Debug,
        Message = "The row-level security privilege check of data source '{DataSourceName}' passed for role '{RoleName}'.")]
    public static partial void RowLevelSecurityPrivilegeCheckPassed(ILogger logger, string dataSourceName, string roleName);
}

/// <summary>Why the row-level security privilege check failed: a closed set, so a log carries no composed text.</summary>
internal enum RowLevelSecurityPrivilegeProblem
{
    /// <summary>The role is a superuser.</summary>
    Superuser,

    /// <summary>The role has <c>BYPASSRLS</c>.</summary>
    BypassRls,

    /// <summary>The role owns, or acts as the owner of, a protected table.</summary>
    OwnsProtectedTable,

    /// <summary>A permissive policy that does not restrict by tenant applies to the role.</summary>
    NonTenantPermissivePolicy,
}
