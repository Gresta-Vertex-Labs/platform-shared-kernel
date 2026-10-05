namespace SharedKernel.Primitives.Health;

/// <summary>The outcome of a readiness probe.</summary>
/// <remarks>
/// The numeric values match <c>Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus</c>, so a host can
/// convert one to the other by cast, but this package takes no dependency on the health-checks library.
/// </remarks>
public enum ReadinessStatus
{
    /// <summary>The dependency cannot serve requests. The instance should not receive traffic.</summary>
    Unhealthy = 0,

    /// <summary>
    /// The dependency works in a reduced way — for example a cache that still serves from memory while its
    /// distributed layer is down. The instance may keep receiving traffic.
    /// </summary>
    Degraded = 1,

    /// <summary>The dependency is ready.</summary>
    Healthy = 2,
}
