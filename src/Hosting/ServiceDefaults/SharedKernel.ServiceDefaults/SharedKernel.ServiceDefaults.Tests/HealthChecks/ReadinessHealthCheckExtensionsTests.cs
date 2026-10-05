using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Health;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Tests.HealthChecks;

/// <summary>P-569: every registered <see cref="IReadinessProbe"/> becomes one ready-tagged health check.</summary>
public sealed class ReadinessHealthCheckExtensionsTests
{
    [Fact]
    public void EveryProbe_BecomesAReadyTaggedRegistration_NamedAfterTheProbe()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddSharedKernelReadiness();
        services.AddReadinessProbe(_ => new StubProbe("redis", ReadinessReport.Healthy()));
        services.AddReadinessProbe(_ => new StubProbe("storage-invoices", ReadinessReport.Healthy()));
        using var provider = services.BuildServiceProvider();

        var registrations = Registrations(provider);

        Assert.Equal(["redis", "storage-invoices"], registrations.Select(r => r.Name).Order().ToArray());
        Assert.All(registrations, r => Assert.Equal([HealthCheckTags.Ready], r.Tags));
    }

    [Theory]
    [InlineData(ReadinessStatus.Healthy, HealthStatus.Healthy)]
    [InlineData(ReadinessStatus.Degraded, HealthStatus.Degraded)]
    [InlineData(ReadinessStatus.Unhealthy, HealthStatus.Unhealthy)]
    public async Task ProbeStatus_MapsToHealthStatus_WithDataAndLatency(ReadinessStatus status, HealthStatus expected)
    {
        var report = new ReadinessReport(
            status,
            TimeSpan.FromMilliseconds(12),
            "detail",
            new Dictionary<string, object> { ["UnsealedRecords"] = 3L });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReadinessProbe(_ => new StubProbe("probe", report));
        services.AddHealthChecks().AddSharedKernelReadiness();
        await using var provider = services.BuildServiceProvider();

        var entry = (await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync()).Entries["probe"];

        Assert.Equal(expected, entry.Status);
        Assert.Equal("detail", entry.Description);
        Assert.Equal(3L, entry.Data["UnsealedRecords"]);
        Assert.Equal(12d, entry.Data["LatencyMilliseconds"]);
    }

    [Fact]
    public async Task ThrowingProbe_IsUnhealthy_WithTheExceptionTypeOnly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReadinessProbe(_ => new ThrowingProbe());
        services.AddHealthChecks().AddSharedKernelReadiness();
        await using var provider = services.BuildServiceProvider();

        var entry = (await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync()).Entries["throwing"];

        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.Contains(nameof(InvalidOperationException), entry.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", entry.Description, StringComparison.Ordinal);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public void ExcludedProbe_IsNotMapped_AndTimeoutIsApplied()
    {
        var services = new ServiceCollection();
        services.AddReadinessProbe(_ => new StubProbe("cache", ReadinessReport.Healthy()));
        services.AddReadinessProbe(_ => new StubProbe("redis", ReadinessReport.Healthy()));
        services.AddHealthChecks().AddSharedKernelReadiness(o =>
        {
            o.Exclude("cache");
            o.Timeout = TimeSpan.FromSeconds(3);
        });
        using var provider = services.BuildServiceProvider();

        var registration = Assert.Single(Registrations(provider));
        Assert.Equal("redis", registration.Name);
        Assert.Equal(TimeSpan.FromSeconds(3), registration.Timeout);
    }

    [Fact]
    public void CallingTwice_MapsEachProbeOnce()
    {
        var services = new ServiceCollection();
        services.AddReadinessProbe(_ => new StubProbe("redis", ReadinessReport.Healthy()));
        services.AddHealthChecks().AddSharedKernelReadiness();
        services.AddHealthChecks().AddSharedKernelReadiness();
        using var provider = services.BuildServiceProvider();

        Assert.Single(Registrations(provider));
    }

    [Fact]
    public void DuplicateProbeNames_FailWithTheName()
    {
        var services = new ServiceCollection();
        services.AddReadinessProbe(_ => new StubProbe("redis", ReadinessReport.Healthy()));
        services.AddReadinessProbe(_ => new StubProbe("redis", ReadinessReport.Healthy()));
        services.AddHealthChecks().AddSharedKernelReadiness();
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => Registrations(provider));
        Assert.Contains("'redis'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoProbes_MapsNothing_AndKeepsOtherChecks()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelHealthChecks().AddSharedKernelReadiness();
        using var provider = services.BuildServiceProvider();

        Assert.Equal([HealthCheckNames.Startup], Registrations(provider).Select(r => r.Name).ToArray());
    }

    private static IReadOnlyList<HealthCheckRegistration> Registrations(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.ToList();

    private sealed class StubProbe(string name, ReadinessReport report) : IReadinessProbe
    {
        public string Name => name;

        public Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default) => Task.FromResult(report);
    }

    private sealed class ThrowingProbe : IReadinessProbe
    {
        public string Name => "throwing";

        public Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("secret connection string");
    }
}
