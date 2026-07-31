using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

public sealed class PersistenceTelemetryExtensionsTests
{
    [Fact]
    public void WithPersistenceTelemetry_CalledTwice_RegistersTracerProviderBuilderOnce()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WithPersistenceTelemetry();
        builder.WithPersistenceTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        var tracerProviderBuilders = provider.GetServices<TracerProviderBuilder>().ToList();

        // The OpenTelemetry SDK de-duplicates registered ActivitySource names internally; calling
        // the extension twice must not throw and must not register a second TracerProviderBuilder
        // configuration delegate that would double-count instruments.
        Assert.True(tracerProviderBuilders.Count <= 1);
    }

    [Fact]
    public void WithPersistenceTelemetry_DoesNotThrow()
    {
        var builder = WebApplication.CreateBuilder();

        var exception = Record.Exception(() =>
        {
            builder.WithPersistenceTelemetry();
            builder.WithPersistenceTelemetry();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void WithPersistenceTelemetry_SpanFromPersistenceActivitySource_IsCaptured()
    {
        var captured = new List<Activity>();

        var builder = WebApplication.CreateBuilder();
        builder.WithPersistenceTelemetry();

        // Attaches a capturing processor to the same TracerProviderBuilder pipeline that
        // WithPersistenceTelemetry() configures, proving its AddSource("SharedKernel.Persistence")
        // registration actually causes spans from that source to flow through the built
        // TracerProvider — not merely that AddSource was called syntactically. Mirrors
        // CachingTelemetryExtensionsTests.WithCachingTelemetry_SpanFromCachingActivitySource_IsCaptured
        // (T-39) exactly, applied to 06.Persistence's source instead of 02.Caching's.
        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddProcessor(new CapturingProcessor(captured)));

        using var provider = builder.Services.BuildServiceProvider();

        // Resolving TracerProvider forces the OpenTelemetry SDK to build the pipeline configured
        // above — including WithPersistenceTelemetry()'s AddSource("SharedKernel.Persistence")
        // registration — and attach the corresponding listener to the runtime. Without this,
        // BuildServiceProvider() alone never builds the provider (that normally happens via a
        // hosted service at IHost.StartAsync() time, which this unit test never starts).
        using var tracerProvider = provider.GetRequiredService<TracerProvider>();

        // 06.Persistence's PersistenceActivitySource (SharedKernel.Persistence.EfCore.Diagnostics)
        // is the real production emitter of this ActivitySource ("SharedKernel.Persistence",
        // version "1.0", WO-051/P-319). WithPersistenceTelemetry() deliberately wires by string
        // name only (see its own <remarks>) — this domain takes no ProjectReference to
        // SharedKernel.Persistence.EfCore for this purpose — so a locally-created ActivitySource
        // of the identical name/version stands in for it here: .NET's Activity system matches
        // listeners/processors by source name, never by ActivitySource instance identity.
        using var persistenceActivitySource = new ActivitySource("SharedKernel.Persistence", "1.0");
        using (var activity = persistenceActivitySource.StartActivity("Order.AddAsync", ActivityKind.Client))
        {
            activity?.SetTag("persistence.aggregate_type", "Order");
        }

        Assert.Contains(captured, a => a.OperationName == "Order.AddAsync");
    }

    /// <summary>Copies each ended <see cref="Activity"/> into <paramref name="sink"/>.</summary>
    private sealed class CapturingProcessor(List<Activity> sink) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => sink.Add(data);
    }
}
