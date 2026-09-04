using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Scheduling.Probes;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Tests.HealthChecks;

public sealed class SchedulerReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_IsRunningTrue_ReportsHealthy()
    {
        var probe = Substitute.For<ISchedulerServiceProbe>();
        var health = new SchedulerServiceHealth
        {
            IsRunning = true,
            RegisteredJobCount = 3,
            LastTickUtc = DateTimeOffset.UtcNow,
        };
        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(health));

        var healthCheck = new SchedulerReadinessHealthCheck(probe);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_IsRunningFalse_ReportsUnhealthy_NeverDegraded()
    {
        var probe = Substitute.For<ISchedulerServiceProbe>();
        var health = new SchedulerServiceHealth
        {
            IsRunning = false,
            RegisteredJobCount = 0,
            LastTickUtc = null,
        };
        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(health));

        var healthCheck = new SchedulerReadinessHealthCheck(probe);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Status.Should().NotBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_IsRunningTrue_LargeRegisteredJobCount_StillReportsHealthy()
    {
        // Dedicated regression: RegisteredJobCount must never factor into the Healthy/Unhealthy
        // decision, even when it is large — a busy scheduler is not an unhealthy one, mirroring
        // the PendingWriteCount/TaskQueueBacklog-is-never-unhealthy precedent exactly.
        var probe = Substitute.For<ISchedulerServiceProbe>();
        var health = new SchedulerServiceHealth
        {
            IsRunning = true,
            RegisteredJobCount = 250_000,
            LastTickUtc = DateTimeOffset.UtcNow,
        };
        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(health));

        var healthCheck = new SchedulerReadinessHealthCheck(probe);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("registeredJobCount");
        result.Data["registeredJobCount"].Should().Be(250_000);
    }

    [Fact]
    public async Task CheckHealthAsync_LastTickUtcNull_SurfacedAsInformationalDataOnly_StillReportsHealthy()
    {
        // Freshly-started state: no tick has occurred yet. Must not affect Healthy/Unhealthy.
        var probe = Substitute.For<ISchedulerServiceProbe>();
        var health = new SchedulerServiceHealth
        {
            IsRunning = true,
            RegisteredJobCount = 1,
            LastTickUtc = null,
        };
        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(health));

        var healthCheck = new SchedulerReadinessHealthCheck(probe);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("lastTickUtc");
        result.Data["lastTickUtc"].Should().Be("n/a");
    }
}
