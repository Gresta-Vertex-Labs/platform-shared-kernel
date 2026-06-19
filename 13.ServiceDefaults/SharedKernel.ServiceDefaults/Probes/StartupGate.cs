namespace SharedKernel.ServiceDefaults.Probes;

/// <summary>
/// A narrowly-scoped, thread-safe startup readiness gate. Backs the distinction between a
/// Kubernetes startup probe and the liveness/readiness probes that follow it.
/// </summary>
/// <remarks>
/// <para>
/// A long-running warm-up step (e.g. applying pending EF Core migrations or running data
/// seeders) should call <see cref="MarkReady"/> once startup work has completed. Until then,
/// <see cref="StartupGateHealthCheck"/> reports <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy"/>
/// on <c>/health/ready</c>.
/// </para>
/// <para>
/// This is the one intentional, narrowly-scoped exception to the "no static mutable state" rule
/// in this domain — a single <see langword="volatile"/> <see cref="bool"/> field, not a
/// general-purpose cache. Register as a singleton; exactly one instance per process.
/// </para>
/// </remarks>
public sealed class StartupGate
{
    private volatile bool _isReady;

    /// <summary>
    /// Gets a value indicating whether the host has completed startup and is ready to serve traffic.
    /// </summary>
    public bool IsReady => _isReady;

    /// <summary>
    /// Marks the host as ready. Idempotent — calling this method more than once has no
    /// additional effect and never throws.
    /// </summary>
    public void MarkReady() => _isReady = true;
}
