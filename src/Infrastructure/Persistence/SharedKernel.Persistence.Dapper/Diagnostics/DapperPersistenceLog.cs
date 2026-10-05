using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.Dapper.Diagnostics;

/// <summary>
/// <c>[LoggerMessage]</c> source-generated log events for <c>SharedKernel.Persistence.Dapper</c>.
/// </summary>
/// <remarks>Uses <c>6400-6499</c> of <see cref="LoggingEventIdRanges.Persistence"/> (<c>6000-6999</c>).</remarks>
internal static partial class DapperPersistenceLog
{
    /// <summary>Logged when disposing an uncommitted session could not roll its transaction back.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 400,
        Level = LogLevel.Warning,
        Message = "Rolling back an uncommitted Dapper session failed; the server discards the transaction when the connection closes.")]
    public static partial void SessionRollbackFailed(this ILogger logger, Exception exception);

    /// <summary>Logged when a session opens on the cross-tenant database role.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 401,
        Level = LogLevel.Information,
        Message = "Dapper session opened on the cross-tenant database role for an active cross-tenant scope.")]
    public static partial void CrossTenantSessionOpened(this ILogger logger);
}
