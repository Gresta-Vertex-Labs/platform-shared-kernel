using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Health;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Testing.Logging;

namespace SharedKernel.ServiceDefaults.Tests.HealthChecks;

/// <summary>
/// 13005: a readiness probe that throws logs its exception object (type and stack for the sink), while the
/// network-visible health report shows the type name only.
/// </summary>
public sealed class ReadinessProbeLoggingTests
{
    private const string Canary = "canary-connection-string-7f3a91";

    private static async Task<(HealthReport Report, IReadOnlyList<LogRecord> Records)> RunAsync(
        IReadinessProbe probe,
        CancellationToken cancellationToken = default)
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddReadinessProbe(_ => probe);
        services.AddHealthChecks().AddSharedKernelReadiness();
        await using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(cancellationToken);
        var factory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        return (report, [.. factory.Loggers.Values.SelectMany(logger => logger.Records)]);
    }

    [Fact]
    public async Task AThrowingProbe_LogsTheExceptionObject_AndTheBodyShowsTheTypeNameOnly()
    {
        var (report, records) = await RunAsync(new Probe("throwing", () => throw new InvalidOperationException(Canary)));

        var record = records.ShouldHaveLoggedWithException(13005, typeof(InvalidOperationException));
        record.LogLevel.Should().Be(LogLevel.Warning);
        record.TryGetProperty("ProbeName", out var probeName).Should().BeTrue();
        probeName.Should().Be("throwing");
        record.Message.Should().NotContain(Canary, "the message is the template; the exception object carries the detail");

        var entry = report.Entries["throwing"];
        entry.Description.Should().Contain(nameof(InvalidOperationException)).And.NotContain(Canary);
        entry.Exception.Should().BeNull();
    }

    [Fact]
    public async Task AnUnhealthyReport_LogsNothing()
    {
        var (report, records) = await RunAsync(new Probe("down", () => ReadinessReport.Unhealthy("down")));

        report.Entries["down"].Status.Should().Be(HealthStatus.Unhealthy);
        records.ShouldNotHaveLogged(13005);
    }

    [Fact]
    public async Task Cancellation_IsNotLogged()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var probe = new Probe("slow", () => throw new OperationCanceledException(cts.Token));

        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddReadinessProbe(_ => probe);
        services.AddHealthChecks().AddSharedKernelReadiness();
        await using var provider = services.BuildServiceProvider();

        try
        {
            await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected: cancellation propagates.
        }

        var factory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        factory.Loggers.Values.SelectMany(logger => logger.Records).ToList().ShouldNotHaveLogged(13005);
    }

    private sealed class Probe(string name, Func<ReadinessReport> probe) : IReadinessProbe
    {
        public string Name => name;

        public Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default) => Task.FromResult(probe());
    }
}
