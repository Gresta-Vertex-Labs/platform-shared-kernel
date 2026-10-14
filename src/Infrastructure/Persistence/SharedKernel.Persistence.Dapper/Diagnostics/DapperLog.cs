using Microsoft.Extensions.Logging;

namespace SharedKernel.Persistence.Dapper.Diagnostics;

/// <summary>Log statements of <c>SharedKernel.Persistence.Dapper</c>. No statement text, parameter or tenant reaches a log.</summary>
internal static partial class DapperLog
{
    /// <summary>Logged when disposing an uncommitted session could not roll its transaction back.</summary>
    [LoggerMessage(
        EventId = DapperLoggingEventIds.SessionRollbackFailed,
        Level = LogLevel.Warning,
        Message = "Rolling back an uncommitted Dapper session failed, and the server discards the transaction when the connection closes.")]
    public static partial void SessionRollbackFailed(ILogger logger, Exception exception);

    /// <summary>Logged when a session opens on the cross-tenant database role.</summary>
    [LoggerMessage(
        EventId = DapperLoggingEventIds.CrossTenantSessionOpened,
        Level = LogLevel.Information,
        Message = "Dapper session opened on the cross-tenant database role for an active cross-tenant scope.")]
    public static partial void CrossTenantSessionOpened(ILogger logger);
}
