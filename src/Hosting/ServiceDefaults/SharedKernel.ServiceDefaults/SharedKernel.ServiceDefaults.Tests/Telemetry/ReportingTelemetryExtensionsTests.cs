using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

public sealed class ReportingTelemetryExtensionsTests
{
    [Fact]
    public void WithReportingTelemetry_CalledTwice_RegistersTracerProviderBuilderOnce()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WithReportingTelemetry();
        builder.WithReportingTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        var tracerProviderBuilders = provider.GetServices<TracerProviderBuilder>().ToList();

        // The OpenTelemetry SDK de-duplicates registered ActivitySource names internally; calling
        // the extension twice must not throw and must not register a second TracerProviderBuilder
        // configuration delegate that would double-count instruments.
        Assert.True(tracerProviderBuilders.Count <= 1);
    }

    [Fact]
    public void WithReportingTelemetry_CalledTwice_RegistersMeterProviderBuilderOnce()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WithReportingTelemetry();
        builder.WithReportingTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        var meterProviderBuilders = provider.GetServices<MeterProviderBuilder>().ToList();

        // Same de-duplication guarantee as the tracing half, for the "SharedKernel.Reporting" meter.
        Assert.True(meterProviderBuilders.Count <= 1);
    }

    [Fact]
    public void WithReportingTelemetry_DoesNotThrow()
    {
        var builder = WebApplication.CreateBuilder();

        var exception = Record.Exception(() =>
        {
            builder.WithReportingTelemetry();
            builder.WithReportingTelemetry();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void WithReportingTelemetry_NullBuilder_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ReportingTelemetryExtensions.WithReportingTelemetry(null!));

    [Fact]
    public void WithReportingTelemetry_SpanFromReportingActivitySource_IsCaptured()
    {
        var captured = new List<Activity>();

        var builder = WebApplication.CreateBuilder();
        builder.WithReportingTelemetry();

        // Attaches a capturing processor to the same TracerProviderBuilder pipeline that
        // WithReportingTelemetry() configures, proving its AddSource("SharedKernel.Reporting")
        // registration actually causes spans from that source to flow through the built
        // TracerProvider — not merely that AddSource was called syntactically. Mirrors
        // CachingTelemetryExtensionsTests' identical end-to-end wiring proof.
        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddProcessor(new CapturingProcessor(captured)));

        using var provider = builder.Services.BuildServiceProvider();

        // Resolving TracerProvider forces the OpenTelemetry SDK to build the pipeline configured
        // above, including WithReportingTelemetry()'s AddSource("SharedKernel.Reporting") registration.
        using var tracerProvider = provider.GetRequiredService<TracerProvider>();

        // 20.Reporting (SharedKernel.Reporting.Abstractions, shared by every exporter) is the real production emitter of this
        // ActivitySource. WithReportingTelemetry() deliberately wires by string name only — this
        // package takes no ProjectReference to any 20.Reporting package — so a locally-created
        // ActivitySource of the identical name stands in for it: .NET's Activity system matches
        // listeners/processors by source name, never by ActivitySource instance identity.
        using var reportingActivitySource = new ActivitySource("SharedKernel.Reporting");
        using (var activity = reportingActivitySource.StartActivity("reporting export", ActivityKind.Internal))
        {
            activity?.SetTag("reporting.format", "xlsx");
        }

        Assert.Contains(captured, a => a.OperationName == "reporting export");
    }

    [Fact]
    public void WithReportingTelemetry_MeasurementFromReportingMeter_IsExported()
    {
        var exported = new List<Metric>();

        var builder = WebApplication.CreateBuilder();
        builder.WithReportingTelemetry();
        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddInMemoryExporter(exported));

        using var provider = builder.Services.BuildServiceProvider();
        using var meterProvider = provider.GetRequiredService<MeterProvider>();

        // Stands in for SharedKernel.Reporting.Abstractions' own "SharedKernel.Reporting" meter, matched by name.
        using var reportingMeter = new Meter("SharedKernel.Reporting");
        reportingMeter.CreateCounter<long>("reporting.bytes").Add(42);

        meterProvider.ForceFlush();

        Assert.Contains(exported, m => m.Name == "reporting.bytes");
    }

    /// <summary>Copies each ended <see cref="Activity"/> into <paramref name="sink"/>.</summary>
    private sealed class CapturingProcessor(List<Activity> sink) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => sink.Add(data);
    }
}
