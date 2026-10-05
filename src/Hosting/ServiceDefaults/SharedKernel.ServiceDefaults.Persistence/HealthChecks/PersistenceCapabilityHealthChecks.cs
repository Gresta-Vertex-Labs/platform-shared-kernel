using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Persistence.EfCore.Seeding;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>Not ready until every startup migration and seeder has completed (<see cref="IPersistenceStartup"/>).</summary>
internal sealed class PersistenceStartupHealthCheck(IPersistenceStartup startup) : IHealthCheck
{
    /// <inheritdoc/>
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(startup.IsCompleted
            ? HealthCheckResult.Healthy("Startup migrations and seeders completed.")
            : HealthCheckResult.Unhealthy("Startup migrations and seeders have not completed."));
}
