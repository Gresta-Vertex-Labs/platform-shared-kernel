using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace SharedKernel.Idempotency.EfCore.Internal;

/// <summary>Options the idempotency store's context always runs with, whatever the caller configured.</summary>
internal static class IdempotencyDbContextOptions
{
    /// <summary>
    /// The migrations history table of an opted-in database (<c>EfCoreIdempotencyOptions.MigrateOnStartup</c>), in the
    /// default schema. A stored format: renaming it later needs a migration story.
    /// </summary>
    internal const string HistoryTableName = "__ef_migrations_history_idempotency";

    /// <summary>
    /// Replaces any retrying execution strategy with a non-retrying one.
    /// </summary>
    /// <remarks>
    /// Every store operation is a single atomic statement (<c>INSERT ... ON CONFLICT</c>, a conditional
    /// <c>UPDATE</c>/<c>DELETE</c>); a retry belongs to the caller, who owns the request that the reservation guards.
    /// With the platform default (<c>UsePostgres</c>: 6 retries, up to 30 s apart) an unreachable store would
    /// otherwise spend minutes backing off before the fail-open or fail-closed decision is made.
    /// </remarks>
    public static void DisableRetry(DbContextOptionsBuilder options)
    {
        if (options.Options.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault() is not { } relational)
            return;

        ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
            relational.WithExecutionStrategyFactory(dependencies => new NonRetryingExecutionStrategy(dependencies)));
    }

    /// <summary>
    /// Gives the context its own migrations history table (<see cref="HistoryTableName"/>, default schema), after
    /// whatever the caller configured. Applied only when the package migrates the context itself.
    /// </summary>
    public static void UseHistoryTable(DbContextOptionsBuilder options)
    {
        if (options.Options.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault() is not { } relational)
            return;

        ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
            relational.WithMigrationsHistoryTableName(HistoryTableName));
    }
}
