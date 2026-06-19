using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SharedKernel.ServiceDefaults.Probes;

/// <summary>
/// Reports <see cref="HealthStatus.Unhealthy"/> until <see cref="StartupGate.MarkReady"/> has
/// been called; reports <see cref="HealthStatus.Healthy"/> thereafter.
/// </summary>
/// <remarks>
/// Tagged <c>"ready"</c> only — startup gating must never affect <c>/health/live</c>, since a
/// pod that is alive but still warming up should not be killed and restarted by Kubernetes.
/// Registered automatically and unconditionally by <see cref="Extensions.ServiceDefaultsExtensions.AddServiceDefaults"/>.
/// Depends only on <see cref="StartupGate"/> — no provider-specific coupling.
/// </remarks>
public sealed class StartupGateHealthCheck(StartupGate startupGate) : IHealthCheck
{
    /// <inheritdoc/>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = startupGate.IsReady
            ? HealthCheckResult.Healthy("Startup completed.")
            : HealthCheckResult.Unhealthy("Startup has not completed yet.");

        return Task.FromResult(result);
    }
}
