using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// OpenTelemetry composition for Platform.SharedKernel microservices: tracing, metrics, and the
/// OTLP exporter, configured from the standard OpenTelemetry environment variables.
/// </summary>
public static class TelemetryExtensions
{
    /// <summary>
    /// Configures OpenTelemetry tracing and metrics for the host: a <see cref="ResourceBuilder"/>
    /// carrying <paramref name="serviceName"/> and the entry assembly version, ASP.NET Core /
    /// HttpClient / (conditionally) EF Core instrumentation in the <c>TracerProvider</c>, runtime
    /// and ASP.NET Core instrumentation in the <c>MeterProvider</c>, and an OTLP exporter whose
    /// endpoint and protocol are read from the standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> /
    /// <c>OTEL_EXPORTER_OTLP_PROTOCOL</c> environment variables.
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
    /// matching the OpenTelemetry SDK's standard behavior for unused instrumentation sources.
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
                .AddOtlpExporter());

        return builder;
    }
}
