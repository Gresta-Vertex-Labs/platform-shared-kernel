using Microsoft.AspNetCore.Http;
using SharedKernel.Execution.Tenancy;

namespace SharedKernel.MultiTenancy.Resolution;

/// <summary>
/// Holds the <see cref="TenantResolutionStrategyNames.Database"/> slot when no <c>IDbConnectionFactory</c> is
/// registered. <c>TenantResolutionOptionsValidator</c> fails startup when the strategy order names
/// <c>Database</c> in that state, so this is never reached by a host that started; reaching it anyway is a bug, and it
/// says so rather than resolving no tenant silently.
/// </summary>
internal sealed class UnavailableDatabaseTenantResolutionStrategy : ITenantResolutionStrategy
{
    internal const string Reason =
        "The \"Database\" tenant resolution strategy needs an IDbConnectionFactory (SharedKernel.Persistence.Npgsql, "
        + "registered by AddSharedKernelNpgsql or AddSharedKernelPostgres). Register one, or omit \"Database\" from "
        + "SharedKernel:MultiTenancy:StrategyOrder.";

    public string StrategyName => TenantResolutionStrategyNames.Database;

    public Task<TenantId?> TryResolveAsync(HttpContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(Reason);
}
