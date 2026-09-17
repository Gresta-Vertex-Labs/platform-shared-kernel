using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Caching.Abstractions;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Caching.Tests.HealthChecks;

public sealed class CacheReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ProbeMisses_ReportsHealthy()
    {
        var cacheService = Substitute.For<ICacheService>();
        cacheService.TryGetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CacheLookup<string>.Miss));

        var healthCheck = new CacheReadinessHealthCheck(cacheService);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        await cacheService.Received(1).TryGetAsync<string>(Arg.Is<string>(key => !string.IsNullOrWhiteSpace(key)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckHealthAsync_ProbeHits_ReportsHealthy()
    {
        var cacheService = Substitute.For<ICacheService>();
        cacheService.TryGetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CacheLookup<string>.Hit("value")));

        var healthCheck = new CacheReadinessHealthCheck(cacheService);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ProbeThrows_ReportsDegradedNeverUnhealthy()
    {
        var cacheService = Substitute.For<ICacheService>();
        cacheService.TryGetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<CacheLookup<string>>>(_ => throw new InvalidOperationException("cache unavailable"));

        var healthCheck = new CacheReadinessHealthCheck(cacheService);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.NotEqual(HealthStatus.Unhealthy, result.Status);
        Assert.IsType<InvalidOperationException>(result.Exception);
    }

    [Fact]
    public async Task CheckHealthAsync_ProbeFaultsAsynchronously_ReportsDegraded()
    {
        var cacheService = Substitute.For<ICacheService>();
        cacheService.TryGetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<CacheLookup<string>>(FaultAsync()));

        var healthCheck = new CacheReadinessHealthCheck(cacheService);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);

        static async Task<CacheLookup<string>> FaultAsync()
        {
            await Task.Yield();
            throw new TimeoutException("redis timeout");
        }
    }
}
