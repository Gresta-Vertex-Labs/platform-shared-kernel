using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Health;
using Xunit;

namespace SharedKernel.Primitives.Tests.Health;

/// <summary>P-569: the readiness contract every provider reports through.</summary>
public sealed class ReadinessReportTests
{
    [Fact]
    public void Factories_SetStatus_AndDataIsNeverNull()
    {
        Assert.Equal(ReadinessStatus.Healthy, ReadinessReport.Healthy().Status);
        Assert.True(ReadinessReport.Healthy().IsHealthy);
        Assert.Equal(ReadinessStatus.Degraded, ReadinessReport.Degraded("slow").Status);
        Assert.False(ReadinessReport.Degraded("slow").IsHealthy);
        Assert.Equal(ReadinessStatus.Unhealthy, ReadinessReport.Unhealthy("down").Status);
        Assert.Empty(ReadinessReport.Unhealthy("down").Data);
    }

    [Fact]
    public void StatusValues_MatchHealthStatus_SoAHostCanCast()
    {
        Assert.Equal(0, (int)ReadinessStatus.Unhealthy);
        Assert.Equal(1, (int)ReadinessStatus.Degraded);
        Assert.Equal(2, (int)ReadinessStatus.Healthy);
    }

    [Fact]
    public void Data_IsSnapshotted_SoChangingTheSourceDoesNotChangeTheReport()
    {
        var source = new Dictionary<string, object> { ["Count"] = 1L };
        var report = ReadinessReport.Healthy(data: source);

        source["Count"] = 2L;
        source["Other"] = "x";

        Assert.Equal(1L, report.Data["Count"]);
        Assert.Single(report.Data);
    }

    [Fact]
    public void Equality_ComparesDataByContent()
    {
        var a = ReadinessReport.Unhealthy("down", new Dictionary<string, object> { ["Store"] = "invoices" }, TimeSpan.FromMilliseconds(5));
        var b = ReadinessReport.Unhealthy("down", new Dictionary<string, object> { ["Store"] = "invoices" }, TimeSpan.FromMilliseconds(5));
        var c = ReadinessReport.Unhealthy("down", new Dictionary<string, object> { ["Store"] = "archive" }, TimeSpan.FromMilliseconds(5));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void InvalidInput_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadinessReport((ReadinessStatus)7));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReadinessReport.Healthy(latency: TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentException>(() => ReadinessReport.Healthy(data: new Dictionary<string, object> { ["Null"] = null! }));
    }

    [Fact]
    public void With_KeepsValidationAndSnapshot()
    {
        var report = ReadinessReport.Healthy() with { Status = ReadinessStatus.Degraded, Description = "slow" };

        Assert.Equal(ReadinessStatus.Degraded, report.Status);
        Assert.Equal("slow", report.Description);
        Assert.Throws<ArgumentOutOfRangeException>(() => report with { Status = (ReadinessStatus)(-1) });
    }

    [Fact]
    public void AddReadinessProbe_ByType_IsIdempotent_AndSingleton()
    {
        var services = new ServiceCollection();
        services.AddReadinessProbe<FixedProbe>();
        services.AddReadinessProbe<FixedProbe>();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IReadinessProbe));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddReadinessProbe_ByFactory_RegistersOnePerCall_AndLookupFindsByName()
    {
        var services = new ServiceCollection();
        services.AddReadinessProbe(_ => new NamedProbe("storage-a"));
        services.AddReadinessProbe(_ => new NamedProbe("storage-b"));
        using var provider = services.BuildServiceProvider();

        Assert.Equal(2, provider.GetServices<IReadinessProbe>().Count());
        Assert.Equal("storage-b", provider.GetRequiredReadinessProbe("storage-b").Name);
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredReadinessProbe("storage-c"));
    }

    [Fact]
    public void GetRequiredReadinessProbe_DuplicateNames_Throws()
    {
        var services = new ServiceCollection();
        services.AddReadinessProbe(_ => new NamedProbe("dup"));
        services.AddReadinessProbe(_ => new NamedProbe("dup"));
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredReadinessProbe("dup"));
    }

    private sealed class FixedProbe : IReadinessProbe
    {
        public string Name => "fixed";

        public Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ReadinessReport.Healthy());
    }

    private sealed class NamedProbe(string name) : IReadinessProbe
    {
        public string Name => name;

        public Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ReadinessReport.Healthy());
    }
}
