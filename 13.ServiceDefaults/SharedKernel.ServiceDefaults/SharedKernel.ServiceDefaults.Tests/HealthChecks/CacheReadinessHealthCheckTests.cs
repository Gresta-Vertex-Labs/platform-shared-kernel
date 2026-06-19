using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Caching.Abstractions;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Tests.HealthChecks;

public sealed class CacheReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ProbeSucceeds_ReportsHealthy()
    {
        var cacheService = Substitute.For<ICacheService>();
        cacheService.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<string?>(null));

        var healthCheck = new CacheReadinessHealthCheck(cacheService);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ProbeFails_ReportsDegradedNeverUnhealthy()
    {
        var cacheService = Substitute.For<ICacheService>();
        cacheService.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<string?>>(_ => throw new InvalidOperationException("cache unavailable"));

        var healthCheck = new CacheReadinessHealthCheck(cacheService);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.NotEqual(HealthStatus.Unhealthy, result.Status);
    }
}
