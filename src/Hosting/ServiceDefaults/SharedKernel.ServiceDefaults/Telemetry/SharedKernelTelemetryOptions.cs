namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Options of <c>AddSharedKernelTelemetry()</c>, bound from the <see cref="SectionName"/> configuration section (environment
/// form <c>SharedKernel__ServiceDefaults__Telemetry__OtlpExporter</c>) and validated when the host starts. A delegate passed to
/// <c>AddServiceDefaults(o =&gt; …)</c> or <c>AddSharedKernelTelemetry(name, o =&gt; …)</c> overrides the configuration.
/// </summary>
/// <remarks>
/// The options are bound with <c>Microsoft.Extensions.Options</c> directly: this package references no kernel package
/// besides <c>SharedKernel.Primitives</c>, so it cannot use the kernel's own section-bound options registration.
/// </remarks>
public sealed class SharedKernelTelemetryOptions
{
    /// <summary>The configuration section the options bind from.</summary>
    public const string SectionName = "SharedKernel:ServiceDefaults:Telemetry";

    /// <summary>
    /// When the OTLP exporter is registered. Defaults to <see cref="OtlpExporterMode.Always"/>, the 1.0.0 behaviour.
    /// </summary>
    /// <remarks>
    /// The mode and the endpoint variables are read while <c>AddSharedKernelTelemetry()</c> runs, from the host's
    /// configuration as it is then (environment variables, <c>appsettings</c> and command line included): the exporter
    /// cannot be added once the telemetry providers are being built. A configuration source added afterwards is not seen.
    /// </remarks>
    public OtlpExporterMode OtlpExporter { get; set; } = OtlpExporterMode.Always;
}
