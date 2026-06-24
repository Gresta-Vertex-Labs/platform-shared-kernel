using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Testing.ServiceDefaults;
using Xunit;

namespace SharedKernel.Testing.SelfTests.ServiceDefaults;

public sealed class HealthCheckAssertionExtensionsTests
{
    private static HealthCheckRegistration CreateRegistration(string name, params string[] tags) =>
        new(name, sp => new NoOpHealthCheck(), HealthStatus.Unhealthy, tags);

    private sealed class NoOpHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
            Task.FromResult(HealthCheckResult.Healthy());
    }

    [Fact]
    public void ShouldBeTaggedReady_HasReadyTag_DoesNotThrow()
    {
        var registration = CreateRegistration("db", "ready");
        registration.ShouldBeTaggedReady();
    }

    [Fact]
    public void ShouldBeTaggedReady_MissingReadyTag_Throws()
    {
        var registration = CreateRegistration("db", "live");
        Assert.Throws<InvalidOperationException>(registration.ShouldBeTaggedReady);
    }

    [Fact]
    public void ShouldNotBeTaggedLive_NoLiveTag_DoesNotThrow()
    {
        var registration = CreateRegistration("db", "ready");
        registration.ShouldNotBeTaggedLive();
    }

    [Fact]
    public void ShouldNotBeTaggedLive_HasLiveTag_Throws()
    {
        var registration = CreateRegistration("db", "live");
        Assert.Throws<InvalidOperationException>(registration.ShouldNotBeTaggedLive);
    }

    [Fact]
    public void ShouldBeTaggedReady_NullRegistration_Throws() =>
        Assert.Throws<ArgumentNullException>(((HealthCheckRegistration)null!).ShouldBeTaggedReady);
}
