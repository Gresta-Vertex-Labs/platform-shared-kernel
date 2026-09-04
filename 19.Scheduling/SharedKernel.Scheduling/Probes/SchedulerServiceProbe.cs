using SharedKernel.Scheduling.Hosting;

namespace SharedKernel.Scheduling.Probes;

/// <summary>
/// The single implementation of <see cref="ISchedulerServiceProbe"/>, reading
/// <see cref="SchedulingHostedService"/>'s in-process state only — zero I/O.
/// </summary>
internal sealed class SchedulerServiceProbe : ISchedulerServiceProbe
{
    private readonly SchedulingHostedService _hostedService;

    public SchedulerServiceProbe(SchedulingHostedService hostedService)
    {
        _hostedService = hostedService;
    }

    /// <inheritdoc />
    public Task<SchedulerServiceHealth> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var health = new SchedulerServiceHealth
        {
            IsRunning = _hostedService.IsRunning,
            RegisteredJobCount = _hostedService.RegisteredJobCount,
            LastTickUtc = _hostedService.LastTickUtc,
        };

        return Task.FromResult(health);
    }
}
