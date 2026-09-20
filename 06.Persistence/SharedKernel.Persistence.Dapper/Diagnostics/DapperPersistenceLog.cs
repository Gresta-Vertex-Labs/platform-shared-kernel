using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.Dapper.Diagnostics;

/// <summary>
/// <c>[LoggerMessage]</c> source-generated log events for <c>SharedKernel.Persistence.Dapper</c>.
/// </summary>
/// <remarks>
/// Claims the <c>6200-6299</c> sub-block of <c>01.Core</c>'s
/// <see cref="LoggingEventIdRanges.Persistence"/> (<c>6000-6999</c>) — the next unclaimed 100-wide
/// slot after <c>SharedKernel.Persistence.EfCore</c>'s <c>6000-6099</c> and
/// <c>SharedKernel.Persistence.Npgsql</c>'s <c>6100-6199</c> (<c>SharedKernel.Persistence.PostgreSQL</c>
/// ships no runtime <c>[LoggerMessage]</c> events of its own — its conventions/migration helpers are
/// build-time/migration-time only — so it claims no sub-block). <c>06.Persistence/CLAUDE.md</c>'s own
/// sub-block registry is updated in a later documentation wave; this remark is the
/// authoritative record of the claim until then.
/// </remarks>
internal static partial class DapperPersistenceLog
{
    /// <summary>
    /// Logged when a tenant-safe Dapper query/command is attempted with no tenant resolved and no
    /// active cross-tenant scope — the call is rejected before any SQL executes.
    /// </summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 200,
        Level = LogLevel.Warning,
        Message = "Tenant-safe Dapper call rejected: no tenant resolved and no active cross-tenant scope.")]
    public static partial void TenantSafeCallRejectedNoTenant(this ILogger logger);
}
