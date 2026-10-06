using Dapper;
using SharedKernel.Application.Messaging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Dapper.Sessions;
using SharedKernel.Primitives.Results;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Scheduling.Registry;
using Shop.Inventory.Api.Stock;

namespace Shop.Inventory.Api.Reconciliation;

/// <summary>Which replica this process is (<c>Inventory:Replica</c>), recorded with every job execution.</summary>
public sealed class ReplicaIdentity(IConfiguration configuration)
{
    public string Name { get; } = configuration["Inventory:Replica"] ?? Environment.MachineName;
}

/// <summary>
/// Rebuilds every tenant's stock hash from the database. Scheduled on every replica; the scheduler's cross-replica
/// lease runs each occurrence once, and the <c>job_runs</c> row it writes is how that is proved.
/// </summary>
public sealed record ReconcileStockCommand(DateTimeOffset FireTime) : ICommand;

public sealed class ReconcileStockHandler(
    ICrossTenantScope crossTenant,
    IDbSessionFactory sessions,
    StockLevelCache cache,
    ReplicaIdentity replica
) : ICommandHandler<ReconcileStockCommand>
{
    public const string JobName = "inventory-reconciliation";

    public async Task<Result> Handle(ReconcileStockCommand command, CancellationToken ct)
    {
        using (crossTenant.Enter("inventory reconciliation"))
        {
            await using var session = await sessions.OpenAsync(ct);
            var rows = (
                await session.Connection.QueryAsync<(
                    Guid TenantId,
                    string Sku,
                    int OnHand,
                    int Reserved
                )>(
                    session.Command(
                        "SELECT tenant_id, sku, on_hand, reserved FROM stock_items",
                        cancellationToken: ct
                    )
                )
            ).ToList();

            foreach (var tenant in rows.GroupBy(row => row.TenantId))
            {
                await cache.SetAllAsync(
                    new TenantId(tenant.Key),
                    tenant.ToDictionary(
                        row => row.Sku,
                        row => new StockLevel(row.Sku, row.OnHand, row.Reserved),
                        StringComparer.Ordinal
                    ),
                    ct
                );
            }

            await session.Connection.ExecuteAsync(
                session.Command(
                    "INSERT INTO job_runs (job, fire_time, replica, items) VALUES (@job, @fireTime, @replica, @items)",
                    new
                    {
                        job = JobName,
                        fireTime = command.FireTime,
                        replica = replica.Name,
                        items = rows.Count,
                    },
                    ct
                )
            );
            await session.CommitAsync(ct);
        }

        return Result.Success();
    }
}

/// <summary>A job execution, as recorded in <c>job_runs</c>.</summary>
public sealed record JobRun(string Job, DateTimeOffset FireTime, string Replica, int Items);

/// <summary>The inventory's scheduled jobs, registered by the host and by the tests on an in-memory registry.</summary>
public static class InventoryJobs
{
    /// <summary>The default schedule: every two seconds (Quartz syntax, seconds first).</summary>
    public const string DefaultReconciliationCron = "0/2 * * * * ?";

    public static IScheduledJobRegistry Register(
        IScheduledJobRegistry registry,
        string? reconciliationCron
    ) =>
        registry.AddRecurring(
            ReconcileStockHandler.JobName,
            reconciliationCron ?? DefaultReconciliationCron,
            context => new ReconcileStockCommand(context.ScheduledFireTimeUtc),
            options =>
            {
                // A missed tick is not caught up (the next one reconciles everything anyway), and a slow run is never
                // overlapped by the next.
                options.MisfirePolicy = MisfirePolicy.Skip;
                options.OverlapPolicy = OverlapPolicy.Skip;
            }
        );
}
