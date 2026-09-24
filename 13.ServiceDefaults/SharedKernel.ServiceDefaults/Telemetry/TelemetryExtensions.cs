using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// OpenTelemetry composition for Platform.SharedKernel microservices: tracing, metrics, logging,
/// and the OTLP exporter, configured from the standard OpenTelemetry environment variables.
/// </summary>
public static class TelemetryExtensions
{
    /// <summary>
    /// Configures OpenTelemetry tracing, metrics, and logging for the host: a
    /// <see cref="ResourceBuilder"/> carrying <paramref name="serviceName"/> and the entry assembly
    /// version, ASP.NET Core / HttpClient / (conditionally) EF Core instrumentation in the
    /// <c>TracerProvider</c>, runtime and ASP.NET Core instrumentation in the
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
    /// <b>Inbound baggage (P-562 X2).</b> When the tracer provider is built, OpenTelemetry's
    /// default propagator is decorated so it takes no baggage from an incoming HTTP request:
    /// <c>OpenTelemetry.Baggage.Current</c> is never filled from a caller's <c>baggage</c> header,
    /// so the HttpClient and gRPC client instrumentations can no longer forward it to downstream
    /// services. Trace context is still read, baggage from other carriers (Temporal workflow
    /// headers) is kept, and the service's own outgoing baggage — <c>Baggage.Current</c> items it
    /// sets, and <see cref="System.Diagnostics.Activity"/> baggage, which .NET sends when
    /// <c>Baggage.Current</c> is empty — still propagates. A propagator the service sets before the
    /// host starts is decorated, not replaced. The request's <see cref="System.Diagnostics.Activity"/>
    /// is a separate store, cleared at the edge by <c>14.Presentation</c> (<c>TrustInboundBaggage</c>).
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder AddSharedKernelTelemetry(
        this IHostApplicationBuilder builder,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var serviceVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

        var resourceBuilder = ResourceBuilder
            .CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: serviceVersion);

        builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName: serviceName, serviceVersion: serviceVersion))
            .WithTracing(tracing => tracing
                .SetResourceBuilder(resourceBuilder)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .SetResourceBuilder(resourceBuilder)
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter())
            .WithLogging(
                logging => logging
                    .SetResourceBuilder(resourceBuilder)
                    .AddProcessor<BaggageLogRecordProcessor>()
                    .AddOtlpExporter(),
                options =>
                {
                    options.IncludeScopes = true;
                    options.IncludeFormattedMessage = true;
                });

        // Deferred to the tracer provider's construction: every instrumentation reads the default propagator per
        // call, and one the service sets before the host starts gets decorated rather than replaced.
        builder.Services.ConfigureOpenTelemetryTracerProvider(
            static (_, _) => RequestBaggageRefusingPropagator.Install());

        return builder;
    }
}
