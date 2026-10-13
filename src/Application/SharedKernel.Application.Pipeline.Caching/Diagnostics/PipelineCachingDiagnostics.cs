namespace SharedKernel.Application.Pipeline.Caching;

/// <summary>
/// The telemetry identity of <c>SharedKernel.Application.Pipeline.Caching</c>: it records on the same meter and
/// activity source as the rest of the Application domain.
/// </summary>
/// <remarks>
/// Declared here as well as in <see cref="ApplicationDiagnostics"/> so the name is a public constant of the assembly
/// that creates the instruments. A host that subscribes by name uses either constant; both are
/// <c>"SharedKernel.Application"</c>, and the <c>SharedKernel.*</c> wildcard of <c>AddServiceDefaults</c> needs neither.
/// </remarks>
public static class PipelineCachingDiagnostics
{
    /// <summary>The name of the <see cref="System.Diagnostics.Metrics.Meter"/> the cache instruments are created on (<c>"SharedKernel.Application"</c>).</summary>
    public const string MeterName = ApplicationDiagnostics.MeterName;
}
