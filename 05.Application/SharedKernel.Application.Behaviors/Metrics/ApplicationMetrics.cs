using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SharedKernel.Application.Behaviors.Metrics;

/// <summary>
/// Holds the request-duration histogram recorded by <see cref="MetricsBehavior{TRequest,TResponse}"/>.
/// </summary>
/// <remarks>
/// A DI-created singleton, resolved from <see cref="IMeterFactory"/> — not a static field — so its
/// lifetime is owned by the host's dependency injection container rather than the process. Registered
/// by <c>ApplicationBehaviorsBuilder.Build()</c> only when <c>AddMetricsBehavior()</c> was opted
/// into, alongside a call to <c>services.AddMetrics()</c> to ensure an <see cref="IMeterFactory"/>
/// is available to resolve.
/// </remarks>
internal sealed class ApplicationMetrics
{
    /// <summary>The meter name every instrument in this domain is recorded under.</summary>
    internal const string MeterName = "SharedKernel.Application";

    /// <summary>The instrument name for the per-request duration histogram.</summary>
    internal const string RequestDurationName = "sharedkernel.application.request.duration";

    private readonly Histogram<double> _requestDuration;

    /// <summary>Initialises a new <see cref="ApplicationMetrics"/>.</summary>
    /// <param name="meterFactory">The factory used to create this domain's <see cref="Meter"/>.</param>
    public ApplicationMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(MeterName);
        _requestDuration = meter.CreateHistogram<double>(
            RequestDurationName,
            unit: "s",
            description: "Duration, in seconds, of a single request's pipeline traversal.");
    }

    /// <summary>Records the duration, in seconds, of a single request's pipeline traversal.</summary>
    /// <param name="seconds">The elapsed duration, in seconds.</param>
    /// <param name="tags">The tags to attach to the recorded measurement.</param>
    internal void RecordRequestDuration(double seconds, in TagList tags) => _requestDuration.Record(seconds, tags);
}
