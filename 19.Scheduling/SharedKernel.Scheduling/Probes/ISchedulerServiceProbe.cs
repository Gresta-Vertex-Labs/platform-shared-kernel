namespace SharedKernel.Scheduling.Probes;

/// <summary>
/// A zero-I/O readiness-probe primitive reporting whether the hosted scheduling loop is running and
/// how many jobs are registered.
/// </summary>
/// <remarks>
/// <para>
/// The implementation reads only in-process state already held by the hosted loop — never performs
/// I/O of any kind (no distributed-lock probe call, no database ping). This mirrors the zero-I/O
/// contract every other readiness-probe primitive on this platform follows for a component with
/// nothing external to check reachability against.
/// </para>
/// <para>
/// <c>19.Scheduling</c> ships this probe primitive only; it ships <b>no</b> <c>IHealthCheck</c>
/// implementation and never references <c>Microsoft.Extensions.Diagnostics.HealthChecks</c>. Wiring
/// this into <c>AddHealthChecks()</c> is <c>13.ServiceDefaults</c>'s concern
/// (<c>AddSchedulerReadinessCheck</c>), via a narrow, separately-named <c>13 → 19</c> layering grant
/// scoped to exactly this interface and <see cref="SchedulerServiceHealth"/> — no other type in this
/// package may be reached through it.
/// </para>
/// </remarks>
public interface ISchedulerServiceProbe
{
    /// <summary>Reports the current in-process state of the hosted scheduling loop.</summary>
    /// <param name="cancellationToken">
    /// Accepted for signature symmetry with every other probe primitive on this platform; never
    /// actually observed, since this probe performs no I/O.
    /// </param>
    Task<SchedulerServiceHealth> ProbeAsync(CancellationToken cancellationToken = default);
}
