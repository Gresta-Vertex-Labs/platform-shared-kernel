using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// OpenTelemetry composition for Platform.SharedKernel microservices: tracing, metrics, logging,
/// and the OTLP exporter, configured from the standard OpenTelemetry environment variables.
/// </summary>
public static class TelemetryExtensions
{
    /// <summary>
    /// Configures OpenTelemetry tracing, metrics, and logging for the host: one resource carrying
    /// <paramref name="serviceName"/> and the entry assembly's informational version
    /// (<c>service.version</c>, <c>0.0.0</c> when there is no entry assembly), every <c>SharedKernel.*</c>
    /// <see cref="System.Diagnostics.ActivitySource"/> and <see cref="System.Diagnostics.Metrics.Meter"/> by wildcard,
    /// ASP.NET Core / HttpClient / (conditionally) EF Core instrumentation and the ASP.NET Core SignalR server source in
    /// the <c>TracerProvider</c>, runtime, ASP.NET Core and HttpClient instrumentation in the
    /// <c>MeterProvider</c>, and a <see cref="BaggageLogRecordProcessor"/> in the logging pipeline
    /// so the platform's own <see cref="System.Diagnostics.Activity"/> baggage (the correlation id
    /// and the tenant id, nothing else) is copied onto every exported log record. An OTLP exporter
    /// is registered for all three signals, with its endpoint and protocol read from the standard
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> / <c>OTEL_EXPORTER_OTLP_PROTOCOL</c> environment
    /// variables.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="serviceName">
    /// The logical service name reported in the OpenTelemetry <see cref="ResourceBuilder"/>.
    /// </param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// Called internally by <see cref="Extensions.ServiceDefaultsExtensions.AddServiceDefaults"/>.
    /// Exposed independently for services that need a custom <paramref name="serviceName"/>
    /// distinct from the entry assembly name. EF Core instrumentation is registered
    /// unconditionally here — it no-ops at runtime when no EF Core activity source is present,
    /// matching the OpenTelemetry SDK's standard behavior for unused instrumentation sources. The
    /// logging export registration (<see cref="OpenTelemetryLoggerOptions.IncludeScopes"/> and
    /// <see cref="OpenTelemetryLoggerOptions.IncludeFormattedMessage"/>, both set to
    /// <see langword="true"/>) means every <c>[LoggerMessage]</c>-authored log statement
    /// platform-wide (root <c>CLAUDE.md</c> Logging Conventions) is exported through the same OTLP
    /// pipeline as traces and metrics.
    /// <para>
    /// <b>Kernel telemetry is subscribed by wildcard.</b> Every kernel source and meter is named
    /// <c>SharedKernel.{Capability}[.{Sub}]</c> by its owner, so <c>SharedKernel.*</c> exports all of them, including
    /// ones created after the host started, with no call per capability. A source or meter outside that prefix is not
    /// exported. Only third-party instrumentation (<c>MassTransit</c>, <c>Npgsql</c>, <c>Polly</c> with gRPC client
    /// instrumentation, the Temporal tracing sources) needs a <c>With{X}Telemetry()</c> call.
    /// </para>
    /// <para>
    /// <b>One resource.</b> The resource is configured once for the three signals; <c>service.version</c> is the
    /// informational version of the entry assembly as <see cref="TelemetryVersion.Of"/> returns it (build metadata
    /// kept), so the three providers always report the same value. The standard <c>OTEL_RESOURCE_ATTRIBUTES</c> adds its
    /// attributes; the supplied <paramref name="serviceName"/> wins over <c>OTEL_SERVICE_NAME</c>.
    /// </para>
    /// <para>
    /// <b>Duration buckets.</b> A <c>Histogram&lt;double&gt;</c> with unit <c>s</c> in a <c>SharedKernel.*</c> meter
    /// gets the semantic-convention second-scale boundaries; any other histogram keeps the SDK defaults.
    /// </para>
    /// <para>
    /// <b>Inbound baggage (P-562 X2).</b> When the tracer provider is built, OpenTelemetry's
    /// default propagator is decorated so it takes no baggage from an incoming HTTP request:
    /// <c>OpenTelemetry.Baggage.Current</c> is never filled from a caller's <c>baggage</c> header,
    /// so the HttpClient and gRPC client instrumentations can no longer forward it to downstream
    /// services. Trace context is still read, baggage from other carriers (Temporal workflow
    /// headers) is kept, and the service's own outgoing baggage — <c>Baggage.Current</c> items it
    /// sets, and <see cref="System.Diagnostics.Activity"/> baggage, which .NET sends when
    /// <c>Baggage.Current</c> is empty — still propagates. A propagator the service sets before the
    /// host starts is decorated, not replaced. The request's <see cref="System.Diagnostics.Activity"/>
    /// is a separate store, cleared at the edge by <c>UseSharedKernelRequestContext()</c> (<c>SharedKernel.ServiceDefaults.Security</c>, <c>RequestContextOptions.TrustInboundBaggage</c>).
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder AddSharedKernelTelemetry(
        this IHostApplicationBuilder builder,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var serviceVersion = ResolveServiceVersion(Assembly.GetEntryAssembly());

        builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName: serviceName, serviceVersion: serviceVersion))
            .WithTracing(tracing => tracing
                .AddSource(TelemetryNames.SharedKernelWildcard)
                .AddSource(TelemetryNames.SignalRServerSource)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddMeter(TelemetryNames.SharedKernelWildcard)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter())
            .WithLogging(
                logging => logging
                    .AddProcessor<BaggageLogRecordProcessor>()
                    .AddOtlpExporter(),
                options =>
                {
                    options.IncludeScopes = true;
                    options.IncludeFormattedMessage = true;
                });

        // AddMeter and AddSource de-duplicate by name, but a second AddView for the same instruments produces a second
        // metric stream, so the duration view is registered once per service collection.
        if (!builder.Services.Any(descriptor => descriptor.ServiceType == typeof(DurationViewMarker)))
        {
            builder.Services.AddSingleton<DurationViewMarker>();
            builder.Services.ConfigureOpenTelemetryMeterProvider(
                static metrics => metrics.AddView(DurationHistogramView.Select));
        }

        // Deferred to the tracer provider's construction: every instrumentation reads the default propagator per
        // call, and one the service sets before the host starts gets decorated rather than replaced.
        builder.Services.ConfigureOpenTelemetryTracerProvider(
            static (_, _) => RequestBaggageRefusingPropagator.Install());

        return builder;
    }

    /// <summary>
    /// The <c>service.version</c> reported for <paramref name="entry"/>: its informational version, or
    /// <see cref="TelemetryVersion.Unknown"/> when there is no entry assembly (test hosts, some tool hosts).
    /// </summary>
    /// <param name="entry">The entry assembly, or <see langword="null"/>.</param>
    internal static string ResolveServiceVersion(Assembly? entry) =>
        entry is null ? TelemetryVersion.Unknown : TelemetryVersion.Of(entry);

    /// <summary>Records that the duration histogram view has already been registered.</summary>
    private sealed class DurationViewMarker;
}
