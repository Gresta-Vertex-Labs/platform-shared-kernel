using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Seeding;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Wraps <see cref="DbContextDiagnosticsExtensions.CheckReadinessAsync"/> (<c>06.Persistence.EfCore</c>)
/// in an <see cref="IHealthCheck"/>.
/// </summary>
/// <remarks>
/// Reports <see cref="HealthStatus.Unhealthy"/> when <c>DatabaseReadinessResult.IsHealthy</c> is
/// <see langword="false"/>; <see cref="HealthStatus.Healthy"/> otherwise. <c>Latency</c> and
/// <c>Provider</c> are surfaced via <see cref="HealthCheckResult.Data"/>. <c>06.Persistence</c>
/// ships only the probe primitive — this adapter is the <c>13.ServiceDefaults</c>-owned
/// <see cref="IHealthCheck"/> wiring per the platform's "OTel/health check/probe wiring lives in
/// 13.ServiceDefaults" rule.
/// </remarks>
/// <typeparam name="TContext">The <see cref="SharedKernelDbContext"/> subclass to probe.</typeparam>
internal sealed class DatabaseReadinessHealthCheck<TContext>(TContext dbContext, IPersistenceStartup? persistenceStartup = null) : IHealthCheck
    where TContext : SharedKernelDbContext
{
    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // A database whose startup migrations have not finished is not ready to serve this service.
        if (persistenceStartup is { IsCompleted: false })
            return HealthCheckResult.Unhealthy("Startup migrations and seeders have not completed.");

        var readiness = await dbContext.CheckReadinessAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var data = new Dictionary<string, object>
        {
            ["Latency"] = readiness.Latency,
            ["Provider"] = readiness.Provider,
        };

        return readiness.IsHealthy
            ? HealthCheckResult.Healthy("Database reachable.", data)
            : HealthCheckResult.Unhealthy(
                readiness.ErrorMessage ?? "Database unreachable.",
                exception: null,
                data: data);
    }
}
