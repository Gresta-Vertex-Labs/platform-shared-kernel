namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Decides when <c>AddSharedKernelTelemetry()</c> registers the OTLP exporter for traces, metrics and logs.
/// </summary>
public enum OtlpExporterMode
{
    /// <summary>
    /// The exporter is registered for all three signals whether or not an OTLP endpoint is configured. This is the default
    /// and the behaviour of 1.0.0. A host with no endpoint exports to the exporter's local default address and waits out the
    /// exporter timeout when it shuts down; one startup warning names the setting.
    /// </summary>
    Always = 0,

    /// <summary>
    /// The exporter is registered, for all three signals, only when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> holds an absolute URI
    /// in the host's configuration. A host with no collector exports nothing and shuts down without waiting for the exporter.
    /// </summary>
    WhenEndpointConfigured = 1,
}
