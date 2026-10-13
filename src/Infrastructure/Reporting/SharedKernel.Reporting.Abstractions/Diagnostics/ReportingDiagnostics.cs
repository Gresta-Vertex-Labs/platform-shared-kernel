namespace SharedKernel.Reporting;

/// <summary>The telemetry names of <c>20.Reporting</c>, for subscribing to and asserting on its spans and instruments.</summary>
/// <remarks>
/// The <c>SharedKernel.*</c> wildcard of <c>AddServiceDefaults</c> already subscribes to both. Spans are named
/// <c>reporting export</c> and <c>reporting convert</c>; instruments are <c>sharedkernel.reporting.operation.duration</c>
/// (s), <c>sharedkernel.reporting.rows</c> ({row}) and <c>sharedkernel.reporting.bytes</c> (By).
/// </remarks>
public static class ReportingDiagnostics
{
    /// <summary>The name of the <see cref="System.Diagnostics.ActivitySource"/> every exporter and converter starts spans on.</summary>
    public const string ActivitySourceName = "SharedKernel.Reporting";

    /// <summary>The name of the <see cref="System.Diagnostics.Metrics.Meter"/> carrying the reporting instruments.</summary>
    public const string MeterName = "SharedKernel.Reporting";
}
