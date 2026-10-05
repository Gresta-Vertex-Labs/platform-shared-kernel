using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Health;
using SharedKernel.Scheduling.Hosting;

namespace SharedKernel.Scheduling.Probes;

/// <summary>
/// The scheduler's readiness probe (<see cref="SchedulerReadiness.ProbeName"/>): reads the in-process state of
/// <see cref="SchedulingHostedService"/> — never I/O.
/// </summary>
/// <remarks>
/// The hosted service is resolved on the first probe, not in the constructor: a host constructs every probe to
/// read its name while it builds its health checks, which can happen while hosted services are being created.
/// </remarks>
internal sealed class SchedulerServiceProbe(IServiceProvider services) : IReadinessProbe
{
    public string Name => SchedulerReadiness.ProbeName;

    public Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var hostedService = services.GetRequiredService<SchedulingHostedService>();
        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [SchedulerReadiness.IsRunningKey] = hostedService.IsRunning,
            [SchedulerReadiness.RegisteredJobCountKey] = hostedService.RegisteredJobCount,
        };
        if (hostedService.LastTickUtc is { } lastTick)
            data[SchedulerReadiness.LastTickUtcKey] = lastTick;

        return Task.FromResult(hostedService.IsRunning
            ? ReadinessReport.Healthy("Scheduler loop is running.", data)
            : ReadinessReport.Unhealthy("Scheduler loop is not running.", data));
    }
}
