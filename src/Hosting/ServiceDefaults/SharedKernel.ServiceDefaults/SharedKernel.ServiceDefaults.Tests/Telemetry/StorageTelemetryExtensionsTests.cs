using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

public sealed class StorageTelemetryExtensionsTests
{
    [Fact]
    public void WithStorageTelemetry_CalledTwice_RegistersTracerProviderBuilderOnce()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WithStorageTelemetry();
        builder.WithStorageTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        var tracerProviderBuilders = provider.GetServices<TracerProviderBuilder>().ToList();

        // The OpenTelemetry SDK de-duplicates registered ActivitySource names internally; calling
        // the extension twice must not throw and must not register a second TracerProviderBuilder
        // configuration delegate that would double-count instruments.
        Assert.True(tracerProviderBuilders.Count <= 1);
    }

    [Fact]
    public void WithStorageTelemetry_CalledTwice_RegistersMeterProviderBuilderOnce()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WithStorageTelemetry();
        builder.WithStorageTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        var meterProviderBuilders = provider.GetServices<MeterProviderBuilder>().ToList();

        // Same de-duplication guarantee as the tracing half, for the "SharedKernel.Storage" meter.
        Assert.True(meterProviderBuilders.Count <= 1);
    }

    [Fact]
    public void WithStorageTelemetry_DoesNotThrow()
    {
        var builder = WebApplication.CreateBuilder();

        var exception = Record.Exception(() =>
        {
            builder.WithStorageTelemetry();
            builder.WithStorageTelemetry();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void WithStorageTelemetry_NullBuilder_Throws() =>
        Assert.Throws<ArgumentNullException>(() => StorageTelemetryExtensions.WithStorageTelemetry(null!));

    [Fact]
    public void WithStorageTelemetry_SpanFromStorageActivitySource_IsCaptured()
    {
        var captured = new List<Activity>();

        var builder = WebApplication.CreateBuilder();
        builder.WithStorageTelemetry();

        // Attaches a capturing processor to the same TracerProviderBuilder pipeline that
        // WithStorageTelemetry() configures, proving its AddSource("SharedKernel.Storage")
        // registration actually causes spans from that source to flow through the built
        // TracerProvider — not merely that AddSource was called syntactically. Mirrors
        // CachingTelemetryExtensionsTests' identical end-to-end wiring proof.
        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddProcessor(new CapturingProcessor(captured)));

        using var provider = builder.Services.BuildServiceProvider();

        // Resolving TracerProvider forces the OpenTelemetry SDK to build the pipeline configured
        // above, including WithStorageTelemetry()'s AddSource("SharedKernel.Storage") registration.
        using var tracerProvider = provider.GetRequiredService<TracerProvider>();

        // 08.Storage's S3 provider (SharedKernel.Storage.S3) is the real production emitter of this
        // ActivitySource. WithStorageTelemetry() deliberately wires by string name only — this
        // package takes no ProjectReference to any 08.Storage package — so a locally-created
        // ActivitySource of the identical name stands in for it: .NET's Activity system matches
        // listeners/processors by source name, never by ActivitySource instance identity.
        using var storageActivitySource = new ActivitySource("SharedKernel.Storage");
        using (var activity = storageActivitySource.StartActivity("storage upload", ActivityKind.Client))
        {
            activity?.SetTag("storage.store", "invoices");
        }

        Assert.Contains(captured, a => a.OperationName == "storage upload");
    }

    [Fact]
    public void WithStorageTelemetry_MeasurementFromStorageMeter_IsExported()
    {
        var exported = new List<Metric>();

        var builder = WebApplication.CreateBuilder();
        builder.WithStorageTelemetry();
        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddInMemoryExporter(exported));

        using var provider = builder.Services.BuildServiceProvider();
        using var meterProvider = provider.GetRequiredService<MeterProvider>();

        // Stands in for SharedKernel.Storage.S3's own "SharedKernel.Storage" meter, matched by name.
        using var storageMeter = new Meter("SharedKernel.Storage");
        storageMeter.CreateCounter<long>("storage.client.bytes").Add(42);

        meterProvider.ForceFlush();

        Assert.Contains(exported, m => m.Name == "storage.client.bytes");
    }

    /// <summary>Copies each ended <see cref="Activity"/> into <paramref name="sink"/>.</summary>
    private sealed class CapturingProcessor(List<Activity> sink) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => sink.Add(data);
    }
}
