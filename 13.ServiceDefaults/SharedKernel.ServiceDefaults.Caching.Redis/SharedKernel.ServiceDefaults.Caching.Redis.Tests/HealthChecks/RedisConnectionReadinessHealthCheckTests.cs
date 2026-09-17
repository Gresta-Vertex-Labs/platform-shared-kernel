using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharedKernel.Caching.Redis.Core.Health;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Caching.Redis.Tests.HealthChecks;

public sealed class RedisConnectionReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ProbeHealthy_ReportsHealthy_WithLatency()
    {
        var probe = ProbeReturning(new RedisConnectionHealth(true, TimeSpan.FromMilliseconds(3), null));

        var result = await new RedisConnectionReadinessHealthCheck(probe).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("latency").WhoseValue.Should().Be(TimeSpan.FromMilliseconds(3));
    }

    [Fact]
    public async Task CheckHealthAsync_ProbeUnhealthy_ReportsUnhealthy_WithProbeDescription_NeverDegraded()
    {
        var probe = ProbeReturning(new RedisConnectionHealth(false, null, "Not connected to Redis."));

        var result = await new RedisConnectionReadinessHealthCheck(probe).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Not connected to Redis.");
        result.Exception.Should().BeNull();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckHealthAsync_ProbeUnhealthyWithoutDescription_StillDescribesFailure()
    {
        var probe = ProbeReturning(new RedisConnectionHealth(false, null, null));

        var result = await new RedisConnectionReadinessHealthCheck(probe).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CheckHealthAsync_ProbeThrows_ReportsUnhealthy_WithoutExceptionMessage()
    {
        var probe = Substitute.For<IRedisConnectionProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("redis.internal:6380,password=secret"));

        var result = await new RedisConnectionReadinessHealthCheck(probe).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain(nameof(InvalidOperationException)).And.NotContain("secret");
        result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task CheckHealthAsync_Cancelled_Propagates()
    {
        var probe = Substitute.For<IRedisConnectionProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        var act = () => new RedisConnectionReadinessHealthCheck(probe).CheckHealthAsync(new HealthCheckContext());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CheckHealthAsync_PassesCancellationTokenToProbe()
    {
        var probe = ProbeReturning(new RedisConnectionHealth(true, TimeSpan.Zero, null));
        using var cts = new CancellationTokenSource();

        await new RedisConnectionReadinessHealthCheck(probe).CheckHealthAsync(new HealthCheckContext(), cts.Token);

        await probe.Received(1).ProbeAsync(cts.Token);
    }

    [Fact]
    public async Task AddRedisHealthCheck_RunsThroughHealthCheckService_UsingProbeFromDi()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(ProbeReturning(new RedisConnectionHealth(false, null, "Redis did not answer PING (RedisTimeoutException).")));
        services.AddHealthChecks().AddRedisHealthCheck(name: "cache-redis");

        await using var provider = services.BuildServiceProvider();
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        var entry = report.Entries.Should().ContainKey("cache-redis").WhoseValue;
        entry.Status.Should().Be(HealthStatus.Unhealthy);
        entry.Description.Should().Be("Redis did not answer PING (RedisTimeoutException).");
    }

    [Fact]
    public void AddRedisHealthCheck_NullBuilder_Throws()
    {
        var act = () => ((IHealthChecksBuilder)null!).AddRedisHealthCheck();

        act.Should().Throw<ArgumentNullException>();
    }

    private static IRedisConnectionProbe ProbeReturning(RedisConnectionHealth health)
    {
        var probe = Substitute.For<IRedisConnectionProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(health));
        return probe;
    }
}
