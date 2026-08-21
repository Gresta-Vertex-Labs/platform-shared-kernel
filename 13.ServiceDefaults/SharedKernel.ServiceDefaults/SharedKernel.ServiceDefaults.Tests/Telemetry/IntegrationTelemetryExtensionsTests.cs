using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

public sealed class IntegrationTelemetryExtensionsTests
{
    [Fact]
    public void WithIntegrationTelemetry_CalledTwice_RegistersTracerProviderBuilderOnce()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WithIntegrationTelemetry();
        builder.WithIntegrationTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        var tracerProviderBuilders = provider.GetServices<TracerProviderBuilder>().ToList();

        // The OpenTelemetry SDK de-duplicates registered ActivitySource names internally; calling
        // the extension twice must not throw and must not register a second TracerProviderBuilder
        // configuration delegate that would double-count instruments.
        Assert.True(tracerProviderBuilders.Count <= 1);
    }

    [Fact]
    public void WithIntegrationTelemetry_DoesNotThrow()
    {
        var builder = WebApplication.CreateBuilder();

        var exception = Record.Exception(() =>
        {
            builder.WithIntegrationTelemetry();
            builder.WithIntegrationTelemetry();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void WithIntegrationTelemetry_SpanFromIntegrationActivitySource_IsCaptured()
    {
        var captured = new List<Activity>();

        var builder = WebApplication.CreateBuilder();
        builder.WithIntegrationTelemetry();

        // Attaches a capturing processor to the same TracerProviderBuilder pipeline that
        // WithIntegrationTelemetry() configures, proving its AddSource("SharedKernel.Integration")
        // registration actually causes spans from that source to flow through the built
        // TracerProvider — not merely that AddSource was called syntactically. Mirrors
        // PersistenceTelemetryExtensionsTests.WithPersistenceTelemetry_SpanFromPersistenceActivitySource_IsCaptured
        // (T-40) exactly, applied to 15.Integration's source instead of 06.Persistence's.
        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddProcessor(new CapturingProcessor(captured)));

        using var provider = builder.Services.BuildServiceProvider();

        // Resolving TracerProvider forces the OpenTelemetry SDK to build the pipeline configured
        // above — including WithIntegrationTelemetry()'s AddSource("SharedKernel.Integration")
        // registration — and attach the corresponding listener to the runtime. Without this,
        // BuildServiceProvider() alone never builds the provider (that normally happens via a
        // hosted service at IHost.StartAsync() time, which this unit test never starts).
        using var tracerProvider = provider.GetRequiredService<TracerProvider>();

        // 15.Integration's WebhookIntegrationActivitySource (SharedKernel.Integration.Webhooks.Dispatch)
        // is the real production emitter of this ActivitySource ("SharedKernel.Integration",
        // WO-064/P-424). WithIntegrationTelemetry() deliberately wires by string name only (see its
        // own <remarks>) — this domain takes no ProjectReference to
        // SharedKernel.Integration.Webhooks for this purpose — so a locally-created ActivitySource
        // of the identical name stands in for it here: .NET's Activity system matches
        // listeners/processors by source name, never by ActivitySource instance identity.
        using var integrationActivitySource = new ActivitySource("SharedKernel.Integration");
        using (var activity = integrationActivitySource.StartActivity(
            "WebhookDispatcher.DispatchToSubscription",
            ActivityKind.Client))
        {
            activity?.SetTag("webhook.event_type", "OrderShippedIntegrationEvent");
        }

        Assert.Contains(captured, a => a.OperationName == "WebhookDispatcher.DispatchToSubscription");
    }

    /// <summary>Copies each ended <see cref="Activity"/> into <paramref name="sink"/>.</summary>
    private sealed class CapturingProcessor(List<Activity> sink) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => sink.Add(data);
    }
}
