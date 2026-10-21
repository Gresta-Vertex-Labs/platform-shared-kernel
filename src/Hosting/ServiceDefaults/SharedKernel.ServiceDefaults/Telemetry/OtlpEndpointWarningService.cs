using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.ServiceDefaults.Diagnostics;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Warns once at start-up when an OTLP exporter is registered and no OTLP endpoint is configured for any signal, so the
/// exporters send to the exporter's local default address. Registered once, however many times telemetry is added.
/// </summary>
internal sealed class OtlpEndpointWarningService(
    IConfiguration configuration,
    IEnumerable<OtlpExporterPlan> plans,
    ILoggerFactory loggerFactory) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (plans.Any(plan => plan.Registered) && !OtlpEndpointDetector.IsConfigured(configuration))
        {
            ServiceDefaultsLog.OtlpEndpointNotConfigured(loggerFactory.CreateLogger<ServiceDefaultsLogCategory>());
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
