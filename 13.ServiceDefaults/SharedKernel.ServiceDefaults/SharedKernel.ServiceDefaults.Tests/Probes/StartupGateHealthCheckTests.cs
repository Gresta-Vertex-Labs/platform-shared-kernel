using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;

namespace SharedKernel.ServiceDefaults.Tests.Probes;

public sealed class StartupGateHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_BeforeMarkReady_ReportsUnhealthy()
    {
        var gate = new StartupGate();
        var healthCheck = new StartupGateHealthCheck(gate);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_AfterMarkReady_ReportsHealthy()
    {
        var gate = new StartupGate();
        gate.MarkReady();
        var healthCheck = new StartupGateHealthCheck(gate);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
