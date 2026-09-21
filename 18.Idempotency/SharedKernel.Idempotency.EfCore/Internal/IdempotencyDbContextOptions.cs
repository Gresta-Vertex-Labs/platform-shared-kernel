using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace SharedKernel.Idempotency.EfCore.Internal;

/// <summary>Options the idempotency store's context always runs with, whatever the caller configured.</summary>
internal static class IdempotencyDbContextOptions
{
    /// <summary>
    /// Replaces any retrying execution strategy with a non-retrying one.
    /// </summary>
    /// <remarks>
    /// Every store operation is a single atomic statement (<c>INSERT ... ON CONFLICT</c>, a conditional
    /// <c>UPDATE</c>/<c>DELETE</c>); a retry belongs to the caller, who owns the request that the reservation guards.
    /// With the platform default (<c>UsePostgreSQL</c>: 6 retries, up to 30 s apart) an unreachable store would
    /// otherwise spend minutes backing off before the fail-open or fail-closed decision is made.
    /// </remarks>
    public static void DisableRetry(DbContextOptionsBuilder options)
    {
        if (options.Options.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault() is not { } relational)
            return;

        ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
            relational.WithExecutionStrategyFactory(dependencies => new NonRetryingExecutionStrategy(dependencies)));
    }
}
