using System.Runtime.CompilerServices;

namespace SharedKernel.ServiceDefaults.Tests;

internal static class TestAssemblyDefaults
{
    /// <summary>
    /// The telemetry tests compose hosts with the real OTLP exporter and dispose their providers; with no collector
    /// listening, each shutdown would otherwise wait out the exporter's default timeout.
    /// </summary>
    [ModuleInitializer]
    internal static void ShortenOtlpTimeout() =>
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_TIMEOUT", "100");
}
