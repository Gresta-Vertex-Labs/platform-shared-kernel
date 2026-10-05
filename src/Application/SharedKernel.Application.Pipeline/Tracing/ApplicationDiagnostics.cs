using System.Diagnostics;

namespace SharedKernel.Application.Pipeline.Tracing;

/// <summary>
/// The single, process-lifetime <see cref="System.Diagnostics.ActivitySource"/> instance for this
/// domain.
/// </summary>
/// <remarks>
/// A static <see cref="System.Diagnostics.ActivitySource"/> field is the platform-standard
/// diagnostics-instrument pattern the BCL itself is designed around — a process-long-lived
/// instrument carries no mutable business state, so it is not subject to this domain's
/// no-static-mutable-state rule. <c>13.ServiceDefaults</c> (host composition) subscribes to the
/// <c>"SharedKernel.Application"</c> source name with the host's <c>TracerProvider</c> — this
/// package never reaches into <c>13.ServiceDefaults</c>.
/// </remarks>
internal static class ApplicationDiagnostics
{
    /// <summary>The name every span emitted by this package's <see cref="Tracing.TracingBehavior{TRequest,TResponse}"/> is recorded under.</summary>
    internal const string ActivitySourceName = "SharedKernel.Application";

    /// <summary>The shared <see cref="System.Diagnostics.ActivitySource"/> for this domain.</summary>
    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
