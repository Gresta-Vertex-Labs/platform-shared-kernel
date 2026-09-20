using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Diagnostics;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Wraps <see cref="DbConnectionFactoryDiagnosticsExtensions.CheckReadinessAsync"/>
/// (<c>06.Persistence.Abstractions</c>) in an <see cref="IHealthCheck"/>, for Dapper-only read
/// services that have no <c>DbContext</c> in scope.
/// </summary>
/// <remarks>
/// Reports <see cref="HealthStatus.Unhealthy"/> when <c>DatabaseReadinessResult.IsHealthy</c> is
/// <see langword="false"/>; <see cref="HealthStatus.Healthy"/> otherwise. <c>Latency</c> and
/// <c>Provider</c> are surfaced via <see cref="HealthCheckResult.Data"/> — the same surfacing as
/// <see cref="DatabaseReadinessHealthCheck{TContext}"/>.
/// </remarks>
internal sealed class DapperDatabaseReadinessHealthCheck(IDbConnectionFactory connectionFactory) : IHealthCheck
{
    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var readiness = await connectionFactory.CheckReadinessAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

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
