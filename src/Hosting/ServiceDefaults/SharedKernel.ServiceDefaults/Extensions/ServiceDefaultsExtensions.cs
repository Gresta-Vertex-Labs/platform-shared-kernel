using System.Reflection;
using Microsoft.Extensions.Hosting;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Extensions;

/// <summary>
/// The Platform.SharedKernel host composition entry point.
/// </summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// Wires OpenTelemetry (tracing, metrics, and the OTLP exporter via
    /// <see cref="TelemetryExtensions.AddSharedKernelTelemetry"/>) and the base health check
    /// infrastructure (<see cref="HealthCheckExtensions.AddSharedKernelHealthChecks"/>) — the
    /// always-on <c>StartupGateHealthCheck</c> plus the registrations needed to map
    /// <c>/health/live</c> and <c>/health/ready</c> via
    /// <see cref="HealthCheckExtensions.MapDefaultHealthCheckEndpoints"/>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// This must be the <b>first</b> call in a microservice's <c>Program.cs</c> composition,
    /// before any other <c>SharedKernel.*.Add...</c> extension. Modeled on the .NET Aspire
    /// ServiceDefaults template — the SharedKernel-flavored equivalent.
    /// </para>
    /// <para>
    /// Registers only base infrastructure — never a dependency-specific health check (database,
    /// Redis, RabbitMQ, Azure Service Bus, cache). Those remain explicit opt-in calls on the
    /// <c>IHealthChecksBuilder</c> returned by a subsequent <c>services.AddHealthChecks()</c> call.
    /// </para>
    /// <para>
    /// Resilience defaults for outbound <see cref="System.Net.Http.HttpClient"/> calls are not
    /// configured here — that is <c>11.Communication</c>'s
    /// <c>AddSharedKernelRestCommunication()</c> concern.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var serviceName = Assembly.GetEntryAssembly()?.GetName().Name ?? "sharedkernel-service";

        builder.AddSharedKernelTelemetry(serviceName);
        builder.Services.AddSharedKernelHealthChecks();

        return builder;
    }
}
