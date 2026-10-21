using Microsoft.Extensions.Configuration;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Answers "is an OTLP endpoint configured?" the way the pinned OpenTelemetry OTLP exporter (1.16.0) reads it when it is
/// registered per signal with <c>AddOtlpExporter()</c>: <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> from <see cref="IConfiguration"/>
/// (environment variables are one of its sources), a blank value counts as unset, and a value that is not an absolute URI is
/// ignored by the exporter, so it does not count either. The per-signal variables (<c>OTEL_EXPORTER_OTLP_TRACES_ENDPOINT</c>
/// and its metrics and logs siblings) are read only by <c>UseOtlpExporter()</c>, which this package does not use, so they
/// are not consulted here: counting them would register an exporter that still sends to the local default address.
/// </summary>
internal static class OtlpEndpointDetector
{
    internal const string EndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public static bool IsConfigured(IConfiguration configuration)
    {
        var value = configuration[EndpointKey];
        return !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(value, UriKind.Absolute, out _);
    }
}
