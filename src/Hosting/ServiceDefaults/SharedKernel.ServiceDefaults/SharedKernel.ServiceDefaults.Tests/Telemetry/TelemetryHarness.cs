using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SharedKernel.ServiceDefaults.Extensions;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

/// <summary>
/// A host composition with in-memory exporters: what the telemetry tests build, resolve and read back. The providers
/// are built the way the hosted service builds them, so a name subscribed here is the name subscribed in a service.
/// </summary>
internal sealed class TelemetryHarness : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly TracerProvider _tracerProvider;
    private readonly MeterProvider _meterProvider;

    private TelemetryHarness(ServiceProvider provider, List<Activity> spans, List<MetricSnapshot> metrics)
    {
        _provider = provider;
        Spans = spans;
        Metrics = metrics;
        _tracerProvider = provider.GetRequiredService<TracerProvider>();
        _meterProvider = provider.GetRequiredService<MeterProvider>();
    }

    /// <summary>The spans exported so far, in end order.</summary>
    public List<Activity> Spans { get; }

    /// <summary>The metrics exported by the last <see cref="Flush"/>, one snapshot per metric stream.</summary>
    public List<MetricSnapshot> Metrics { get; }

    /// <summary>The names of the exported spans' sources.</summary>
    public IEnumerable<string> SpanSourceNames => Spans.Select(a => a.Source.Name);

    /// <summary>The names of the exported metrics' meters.</summary>
    public IEnumerable<string> MeterNames => Metrics.Select(m => m.MeterName);

    /// <summary>Builds the host composition <paramref name="compose"/> describes, plus in-memory exporters.</summary>
    public static TelemetryHarness Create(Action<WebApplicationBuilder> compose)
    {
        var spans = new List<Activity>();
        var metrics = new List<MetricSnapshot>();

        var builder = WebApplication.CreateBuilder();
        compose(builder);
        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddInMemoryExporter(spans))
            .WithMetrics(meters => meters.AddInMemoryExporter(metrics));

        return new TelemetryHarness(builder.Services.BuildServiceProvider(), spans, metrics);
    }

    /// <summary>A host built with only <c>AddServiceDefaults()</c>.</summary>
    public static TelemetryHarness ForServiceDefaults() => Create(builder => builder.AddServiceDefaults());

    /// <summary>Starts and stops one span on a new source named <paramref name="sourceName"/>.</summary>
    public static void EmitSpan(string sourceName, string spanName = "work")
    {
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity(spanName);
    }

    /// <summary>Collects the metrics recorded so far into <see cref="Metrics"/>.</summary>
    public void Flush()
    {
        Metrics.Clear();
        _meterProvider.ForceFlush();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _tracerProvider.Dispose();
        _meterProvider.Dispose();
        _provider.Dispose();
    }
}
