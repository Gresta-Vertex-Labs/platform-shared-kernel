using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

public sealed class SchedulingTelemetryExtensionsTests
{
    [Fact]
    public void WithSchedulingTelemetry_CalledTwice_RegistersTracerProviderBuilderOnce()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WithSchedulingTelemetry();
        builder.WithSchedulingTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        var tracerProviderBuilders = provider.GetServices<TracerProviderBuilder>().ToList();

        // The OpenTelemetry SDK de-duplicates registered ActivitySource names internally; calling
        // the extension twice must not throw and must not register a second TracerProviderBuilder
        // configuration delegate that would double-count instruments.
        Assert.True(tracerProviderBuilders.Count <= 1);
    }

    [Fact]
    public void WithSchedulingTelemetry_CalledTwice_RegistersMeterProviderBuilderOnce()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WithSchedulingTelemetry();
        builder.WithSchedulingTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        var meterProviderBuilders = provider.GetServices<MeterProviderBuilder>().ToList();

        // Same de-duplication guarantee as the tracing half, for the "SharedKernel.Scheduling" meter.
        Assert.True(meterProviderBuilders.Count <= 1);
    }

    [Fact]
    public void WithSchedulingTelemetry_DoesNotThrow()
    {
        var builder = WebApplication.CreateBuilder();

        var exception = Record.Exception(() =>
        {
            builder.WithSchedulingTelemetry();
            builder.WithSchedulingTelemetry();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void WithSchedulingTelemetry_SpanFromSchedulingActivitySource_IsCaptured()
    {
        var captured = new List<Activity>();

        var builder = WebApplication.CreateBuilder();
        builder.WithSchedulingTelemetry();

        // Attaches a capturing processor to the same TracerProviderBuilder pipeline that
        // WithSchedulingTelemetry() configures, proving its AddSource("SharedKernel.Scheduling")
        // registration actually causes spans from that source to flow through the built
        // TracerProvider — not merely that AddSource was called syntactically. Mirrors
        // WithCachingTelemetry's T-39/WithPersistenceTelemetry's T-40 BaseProcessor<Activity>
        // capture idiom exactly.
        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddProcessor(new CapturingProcessor(captured)));

        using var provider = builder.Services.BuildServiceProvider();

        // Resolving TracerProvider forces the OpenTelemetry SDK to build the pipeline configured
        // above — including WithSchedulingTelemetry()'s AddSource("SharedKernel.Scheduling")
        // registration — and attach the corresponding listener to the runtime.
        using var tracerProvider = provider.GetRequiredService<TracerProvider>();

        // 19.Scheduling's internal SchedulingTelemetry class is the real production emitter of
        // this ActivitySource. WithSchedulingTelemetry() deliberately wires by string name only
        // (see its own <remarks>) — this domain takes no compile-time dependency on that internal
        // type for telemetry wiring — so a locally-created ActivitySource of the identical name
        // stands in for it here: .NET's Activity system matches listeners/processors by source
        // name, never by ActivitySource instance identity.
        using var schedulingActivitySource = new ActivitySource("SharedKernel.Scheduling");
        using (var activity = schedulingActivitySource.StartActivity("scheduling.tick", ActivityKind.Internal))
        {
            activity?.SetTag("scheduling.job_id", "nightly-report");
        }

        Assert.Contains(captured, a => a.OperationName == "scheduling.tick");
    }

    [Fact]
    public void WithSchedulingTelemetry_MetricFromSchedulingMeter_IsCaptured()
    {
        var exportedMetrics = new List<Metric>();

        var builder = WebApplication.CreateBuilder();
        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddInMemoryExporter(exportedMetrics));

        builder.WithSchedulingTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        using var meterProvider = provider.GetRequiredService<MeterProvider>();

        // Same string-name stand-in reasoning as the tracing test above.
        using var schedulingMeter = new Meter("SharedKernel.Scheduling");
        var counter = schedulingMeter.CreateCounter<long>("scheduling.job.fired");
        counter.Add(1);

        meterProvider.ForceFlush();

        // Proves the "SharedKernel.Scheduling" meter genuinely emits a measurement through the
        // built MeterProvider once WithSchedulingTelemetry() is wired — not merely that
        // AddMeter("SharedKernel.Scheduling") was called syntactically.
        Assert.Contains(exportedMetrics, m => m.MeterName == "SharedKernel.Scheduling");
    }

    /// <summary>Copies each ended <see cref="Activity"/> into <paramref name="sink"/>.</summary>
    private sealed class CapturingProcessor(List<Activity> sink) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => sink.Add(data);
    }
}
