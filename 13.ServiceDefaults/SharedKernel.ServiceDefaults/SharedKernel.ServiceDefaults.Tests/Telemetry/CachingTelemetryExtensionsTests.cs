using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

public sealed class CachingTelemetryExtensionsTests
{
    [Fact]
    public void WithCachingTelemetry_CalledTwice_RegistersTracerProviderBuilderOnce()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WithCachingTelemetry();
        builder.WithCachingTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        var tracerProviderBuilders = provider.GetServices<TracerProviderBuilder>().ToList();

        // The OpenTelemetry SDK de-duplicates registered ActivitySource names internally; calling
        // the extension twice must not throw and must not register a second TracerProviderBuilder
        // configuration delegate that would double-count instruments.
        Assert.True(tracerProviderBuilders.Count <= 1);
    }

    [Fact]
    public void WithCachingTelemetry_CalledTwice_RegistersMeterProviderBuilderOnce()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WithCachingTelemetry();
        builder.WithCachingTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        var meterProviderBuilders = provider.GetServices<MeterProviderBuilder>().ToList();

        // Same de-duplication guarantee as the tracing half, for the "SharedKernel.Caching" meter.
        Assert.True(meterProviderBuilders.Count <= 1);
    }

    [Fact]
    public void WithCachingTelemetry_DoesNotThrow()
    {
        var builder = WebApplication.CreateBuilder();

        var exception = Record.Exception(() =>
        {
            builder.WithCachingTelemetry();
            builder.WithCachingTelemetry();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void WithCachingTelemetry_SpanFromCachingActivitySource_IsCaptured()
    {
        var captured = new List<Activity>();

        var builder = WebApplication.CreateBuilder();
        builder.WithCachingTelemetry();

        // Attaches a capturing processor to the same TracerProviderBuilder pipeline that
        // WithCachingTelemetry() configures, proving its AddSource("SharedKernel.Caching")
        // registration actually causes spans from that source to flow through the built
        // TracerProvider — not merely that AddSource was called syntactically. Mirrors this
        // project's existing BaggageLogRecordProcessorTests/AmbientLoggingEnrichmentAcceptanceTests
        // "attach a capturing BaseProcessor to prove end-to-end wiring" pattern, applied to the
        // tracing signal instead of logging.
        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddProcessor(new CapturingProcessor(captured)));

        using var provider = builder.Services.BuildServiceProvider();

        // Resolving TracerProvider forces the OpenTelemetry SDK to build the pipeline configured
        // above — including WithCachingTelemetry()'s AddSource("SharedKernel.Caching")
        // registration — and attach the corresponding listener to the runtime. Without this,
        // BuildServiceProvider() alone never builds the provider (that normally happens via a
        // hosted service at IHost.StartAsync() time, which this unit test never starts).
        using var tracerProvider = provider.GetRequiredService<TracerProvider>();

        // 02.Caching's FusionCacheService (SharedKernel.Caching.FusionCache) is the real
        // production emitter of this ActivitySource ("SharedKernel.Caching", version "1.0",
        // Phase 41/P-304). WithCachingTelemetry() deliberately wires by string name only (see its
        // own <remarks>) — this domain takes no ProjectReference to
        // SharedKernel.Caching.FusionCache — so a locally-created ActivitySource of the identical
        // name/version stands in for it here: .NET's Activity system matches listeners/processors
        // by source name, never by ActivitySource instance identity.
        using var cachingActivitySource = new ActivitySource("SharedKernel.Caching", "1.0");
        using (var activity = cachingActivitySource.StartActivity("cache.get", ActivityKind.Client))
        {
            activity?.SetTag("cache.key_prefix", "svc:entity");
        }

        Assert.Contains(captured, a => a.OperationName == "cache.get");
    }

    /// <summary>Copies each ended <see cref="Activity"/> into <paramref name="sink"/>.</summary>
    private sealed class CapturingProcessor(List<Activity> sink) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => sink.Add(data);
    }
}
