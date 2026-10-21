using Microsoft.Extensions.Configuration;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>Whether one <c>AddSharedKernelTelemetry()</c> call registered the OTLP exporter (for traces, metrics and logs alike).</summary>
/// <param name="Registered">The exporter was registered.</param>
internal sealed record OtlpExporterPlan(bool Registered)
{
    /// <summary>Decides whether <paramref name="mode"/> registers the exporter for the endpoint in <paramref name="configuration"/>.</summary>
    public static OtlpExporterPlan Resolve(OtlpExporterMode mode, IConfiguration configuration) =>
        new(mode == OtlpExporterMode.Always || OtlpEndpointDetector.IsConfigured(configuration));
}
