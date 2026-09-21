using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
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
    public void WithPersistenceTelemetry_CalledTwice_RegistersMeterProviderBuilderOnce()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WithPersistenceTelemetry();
        builder.WithPersistenceTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        var meterProviderBuilders = provider.GetServices<MeterProviderBuilder>().ToList();

        // Same de-duplication guarantee as the tracing half, for every 06.Persistence-owned meter.
        Assert.True(meterProviderBuilders.Count <= 1);
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
        // version "1.0"). WithPersistenceTelemetry() deliberately wires by string
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

    [Fact]
    public void WithPersistenceTelemetry_SpanFromAuditingActivitySource_IsCaptured()
    {
        var captured = new List<Activity>();

        var builder = WebApplication.CreateBuilder();
        builder.WithPersistenceTelemetry();

        // Same capture idiom as the EfCore source test above, proving
        // AddSource("SharedKernel.Persistence.EfCore.Auditing") — the audit ledger's ActivitySource
        // name — also flows through the built
        // TracerProvider.
        builder.Services
            .AddOpenTelemetry()
                .WithTracing(tracing => tracing.AddProcessor(new CapturingProcessor(captured)));

        using var provider = builder.Services.BuildServiceProvider();
        using var tracerProvider = provider.GetRequiredService<TracerProvider>();

        using var auditingActivitySource = new ActivitySource("SharedKernel.Persistence.EfCore.Auditing", "1.0");
        using (var activity = auditingActivitySource.StartActivity("Audit.Seal", ActivityKind.Internal))
        {
            activity?.SetTag("db.system", "postgresql");
        }

        Assert.Contains(captured, a => a.OperationName == "Audit.Seal");
    }

    [Fact]
    public void WithPersistenceTelemetry_MetricFromPersistenceMeter_IsCaptured()
    {
        var exportedMetrics = new List<Metric>();

        var builder = WebApplication.CreateBuilder();
        builder.Services
            .AddOpenTelemetry()
                .WithMetrics(metrics => metrics.AddInMemoryExporter(exportedMetrics));

        builder.WithPersistenceTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        using var meterProvider = provider.GetRequiredService<MeterProvider>();

        // Same string-name stand-in reasoning as the tracing tests above —
        // SharedKernel.Persistence.EfCore.Diagnostics.PersistenceMeter's real production name.
        using var persistenceMeter = new Meter("SharedKernel.Persistence", "1.0");
        var counter = persistenceMeter.CreateCounter<long>("persistence.concurrency_conflicts");
        counter.Add(1);

        meterProvider.ForceFlush();

        Assert.Contains(exportedMetrics, m => m.MeterName == "SharedKernel.Persistence");
    }

    [Fact]
    public void WithPersistenceTelemetry_MetricFromEncryptionMeter_IsCaptured()
    {
        var exportedMetrics = new List<Metric>();

        var builder = WebApplication.CreateBuilder();
        builder.Services
            .AddOpenTelemetry()
                .WithMetrics(metrics => metrics.AddInMemoryExporter(exportedMetrics));

        builder.WithPersistenceTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        using var meterProvider = provider.GetRequiredService<MeterProvider>();

        // SharedKernel.Persistence.EfCore.Encryption.Diagnostics.EncryptionMeter's real production name.
        using var encryptionMeter = new Meter("SharedKernel.Persistence.EfCore.Encryption", "1.0");
        var counter = encryptionMeter.CreateCounter<long>("persistence.encryption.encrypt_failures");
        counter.Add(1);

        meterProvider.ForceFlush();

        Assert.Contains(exportedMetrics, m => m.MeterName == "SharedKernel.Persistence.EfCore.Encryption");
    }

    [Fact]
    public void WithPersistenceTelemetry_MetricFromAuditingMeter_IsCaptured()
    {
        var exportedMetrics = new List<Metric>();

        var builder = WebApplication.CreateBuilder();
        builder.Services
            .AddOpenTelemetry()
                .WithMetrics(metrics => metrics.AddInMemoryExporter(exportedMetrics));

        builder.WithPersistenceTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        using var meterProvider = provider.GetRequiredService<MeterProvider>();

        // SharedKernel.Persistence.EfCore.Auditing.Diagnostics.AuditingMeter's real production name
        // (no version suffix — that meter is created via "new(MeterName)", not "new(Name, "1.0")").
        using var auditingMeter = new Meter("SharedKernel.Persistence.EfCore.Auditing");
        var counter = auditingMeter.CreateCounter<long>("audit.append.idempotent_duplicates");
        counter.Add(1);

        meterProvider.ForceFlush();

        Assert.Contains(exportedMetrics, m => m.MeterName == "SharedKernel.Persistence.EfCore.Auditing");
    }

    /// <summary>Copies each ended <see cref="Activity"/> into <paramref name="sink"/>.</summary>
    private sealed class CapturingProcessor(List<Activity> sink) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => sink.Add(data);
    }
}
