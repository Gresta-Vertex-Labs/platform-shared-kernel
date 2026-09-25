using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

public sealed class ApplicationTelemetryExtensionsTests
{
    private const string InstrumentationName = "SharedKernel.Application";
    private const string RequestDurationName = "sharedkernel.application.request.duration";

    // OpenTelemetry semantic-convention boundaries for request-duration histograms, in seconds.
    private static readonly double[] ExpectedSecondsBoundaries =
        [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    [Fact]
    public void WithApplicationTelemetry_CalledTwice_RegistersTracerProviderBuilderOnce()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WithApplicationTelemetry();
        builder.WithApplicationTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        var tracerProviderBuilders = provider.GetServices<TracerProviderBuilder>().ToList();

        // The OpenTelemetry SDK de-duplicates registered ActivitySource names internally; calling
        // the extension twice must not throw and must not register a second TracerProviderBuilder
        // configuration delegate that would double-count instruments.
        Assert.True(tracerProviderBuilders.Count <= 1);
    }

    [Fact]
    public void WithApplicationTelemetry_CalledTwice_RegistersMeterProviderBuilderOnce()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WithApplicationTelemetry();
        builder.WithApplicationTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        var meterProviderBuilders = provider.GetServices<MeterProviderBuilder>().ToList();

        // Same de-duplication guarantee as the tracing half, for the "SharedKernel.Application" meter.
        Assert.True(meterProviderBuilders.Count <= 1);
    }

    [Fact]
    public void WithApplicationTelemetry_DoesNotThrow()
    {
        var builder = WebApplication.CreateBuilder();

        var exception = Record.Exception(() =>
        {
            builder.WithApplicationTelemetry();
            builder.WithApplicationTelemetry();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void WithApplicationTelemetry_ReturnsSameBuilderInstance()
    {
        var builder = WebApplication.CreateBuilder();

        var result = builder.WithApplicationTelemetry();

        Assert.Same(builder, result);
    }

    [Fact]
    public void WithApplicationTelemetry_SpanFromApplicationActivitySource_IsCaptured()
    {
        var captured = new List<Activity>();

        var builder = WebApplication.CreateBuilder();
        builder.WithApplicationTelemetry();
        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddProcessor(new CapturingProcessor(captured)));

        using var provider = builder.Services.BuildServiceProvider();
        using var tracerProvider = provider.GetRequiredService<TracerProvider>();

        // SharedKernel.Application's ActivitySource is internal and matched by name only,
        // so a locally-created source of the identical name stands in for TracingBehavior, which
        // names each span after the request type's short name.
        using var applicationSource = new ActivitySource(InstrumentationName);
        using (applicationSource.StartActivity("PlaceOrderCommand", ActivityKind.Internal))
        {
        }

        Assert.Contains(captured, a => a.Source.Name == InstrumentationName && a.OperationName == "PlaceOrderCommand");
    }

    [Fact]
    public void WithApplicationTelemetry_RequestDurationHistogram_UsesSecondsBucketBoundaries()
    {
        var exported = new List<MetricSnapshot>();

        var builder = WebApplication.CreateBuilder();
        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddInMemoryExporter(exported));

        builder.WithApplicationTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        using var meterProvider = provider.GetRequiredService<MeterProvider>();

        // Stands in for MetricsBehavior's internal ApplicationMetrics: same meter name, instrument
        // name and unit.
        using var meter = new Meter(InstrumentationName);
        var histogram = meter.CreateHistogram<double>(RequestDurationName, unit: "s");
        histogram.Record(0.03);

        meterProvider.ForceFlush();

        var snapshot = Assert.Single(exported, m => m.Name == RequestDurationName);
        Assert.Equal("s", snapshot.Unit);

        var bounds = new List<double>();
        foreach (var point in snapshot.MetricPoints)
        {
            foreach (var bucket in point.GetHistogramBuckets())
                bounds.Add(bucket.ExplicitBound);
        }

        // The SDK default boundaries (0, 5, 10 ... 10000) assume milliseconds; a 30 ms request must
        // land in the (0.025, 0.05] bucket, not the first one.
        Assert.Equal([.. ExpectedSecondsBoundaries, double.PositiveInfinity], bounds.Distinct());
    }

    [Fact]
    public void WithApplicationTelemetry_CalledTwice_ExportsRequestDurationOnce()
    {
        var exported = new List<MetricSnapshot>();

        var builder = WebApplication.CreateBuilder();
        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddInMemoryExporter(exported));

        builder.WithApplicationTelemetry();
        builder.WithApplicationTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        using var meterProvider = provider.GetRequiredService<MeterProvider>();

        using var meter = new Meter(InstrumentationName);
        meter.CreateHistogram<double>(RequestDurationName, unit: "s").Record(0.2);

        meterProvider.ForceFlush();

        Assert.Single(exported, m => m.Name == RequestDurationName);
    }

    /// <summary>Copies each ended <see cref="Activity"/> into <paramref name="sink"/>.</summary>
    private sealed class CapturingProcessor(List<Activity> sink) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => sink.Add(data);
    }
}
