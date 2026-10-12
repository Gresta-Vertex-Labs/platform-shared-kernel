using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SharedKernel.Application.Pipeline.Metrics;

/// <summary>
/// Holds the instruments recorded by <see cref="MetricsBehavior{TRequest,TResponse}"/> and
/// <c>StreamObservabilityBehavior</c>: the request-duration histogram and the stream item counter.
/// </summary>
/// <remarks>
/// A DI-created singleton, resolved from <see cref="IMeterFactory"/> — not a static field — so its
/// lifetime is owned by the host's dependency injection container rather than the process. Registered
/// by <c>AddSharedKernelApplication</c> (metrics are always on), alongside a call to <c>services.AddMetrics()</c> to ensure an <see cref="IMeterFactory"/>
/// is available to resolve.
/// </remarks>
internal sealed class ApplicationMetrics
{
    /// <summary>The meter name every instrument in this domain is recorded under.</summary>
    internal const string MeterName = "SharedKernel.Application";

    /// <summary>The instrument name for the per-request duration histogram.</summary>
    internal const string RequestDurationName = "sharedkernel.application.request.duration";

    /// <summary>The instrument name for the counter of items a streaming query yielded.</summary>
    internal const string StreamItemsName = "sharedkernel.application.stream.items";

    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _streamItems;

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
        _streamItems = meter.CreateCounter<long>(
            StreamItemsName,
            unit: "{item}",
            description: "Number of items a streaming query yielded, including those yielded before a fault.");
    }

    /// <summary>Records the duration, in seconds, of a single request's pipeline traversal.</summary>
    /// <param name="seconds">The elapsed duration, in seconds.</param>
    /// <param name="tags">The tags to attach to the recorded measurement.</param>
    internal void RecordRequestDuration(double seconds, in TagList tags) => _requestDuration.Record(seconds, tags);

    /// <summary>Counts the items one streaming query yielded.</summary>
    /// <param name="items">The number of items.</param>
    /// <param name="tags">The tags to attach to the recorded measurement.</param>
    internal void RecordStreamItems(long items, in TagList tags) => _streamItems.Add(items, tags);
}
