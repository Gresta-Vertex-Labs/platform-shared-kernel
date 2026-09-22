using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.EfCore.MultiTenancy;

/// <summary>
/// <c>[LoggerMessage]</c> events of the EF Core row-level security integration.
/// </summary>
/// <remarks>
/// Uses <c>6350-6399</c> of <see cref="LoggingEventIdRanges.Persistence"/>; <c>6300-6349</c> belongs to
/// <c>SharedKernel.Persistence.Npgsql</c>.
/// </remarks>
internal static partial class RowLevelSecurityLog
{
    /// <summary>Logged when a context moves onto the cross-tenant database role.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 350,
        Level = LogLevel.Information,
        Message = "Context '{ContextType}' switched to the cross-tenant database role for an active cross-tenant scope.")]
    public static partial void CrossTenantConnectionUsed(this ILogger logger, string contextType);

    /// <summary>Logged by the startup coverage check for each tenant table row-level security does not protect.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 351,
        Level = LogLevel.Warning,
        Message = "Row-level security is enabled for '{ContextType}', but tenant table {Finding}.")]
    public static partial void TenantTableNotProtected(ILogger logger, string contextType, string finding);
}
