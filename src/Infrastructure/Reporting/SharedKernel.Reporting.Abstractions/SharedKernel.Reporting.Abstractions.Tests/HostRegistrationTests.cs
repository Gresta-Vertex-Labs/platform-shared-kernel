using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Reporting.Abstractions.Tests;

/// <summary>Reporting resolved and emitting through a started host (metrics registered) and a bare provider (none).</summary>
public sealed class HostRegistrationTests
{
    [Fact]
    public async Task Host_WithMetrics_ResolvesTheDependenciesAndEmitsTheInstruments()
    {
        using var metrics = MetricRecorder.StartRecording(ReportingDiagnostics.MeterName);
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddSharedKernelReporting().AddExporter(LineExporter<Row>.Lines, typeof(LineExporter<>));
        using IHost host = builder.Build();
        await host.StartAsync();

        host.Services.GetService<IMeterFactory>().Should().NotBeNull();
        await ExportAndAssertAsync(host.Services, metrics);

        await host.StopAsync();
    }

    [Fact]
    public async Task Provider_WithoutMetrics_StillEmitsTheInstruments()
    {
        using var metrics = MetricRecorder.StartRecording(ReportingDiagnostics.MeterName);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelReporting().AddExporter(LineExporter<Row>.Lines, typeof(LineExporter<>));
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetService<IMeterFactory>().Should().BeNull();
        await ExportAndAssertAsync(provider, metrics);
    }

    private static async Task ExportAndAssertAsync(IServiceProvider services, MetricRecorder metrics)
    {
        services.GetRequiredService<ReportingDependencies>().Should().NotBeNull();
        var exporter = services.GetRequiredKeyedService<IReportExporter<Row>>("lines");
        using var stream = new MemoryStream();

        Result<ReportStreamOutcome> result = await exporter.ExportToStreamAsync(Rows.Generate(2), Rows.Definition, stream);

        result.IsSuccess.Should().BeTrue();
        metrics.ShouldHaveSum("sharedkernel.reporting.rows", 2);
        metrics.ShouldHaveMeasurement(
            "sharedkernel.reporting.operation.duration",
            null,
            new KeyValuePair<string, object?>("outcome", WellKnownOutcomes.Success));
    }
}
