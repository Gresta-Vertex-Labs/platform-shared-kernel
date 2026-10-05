using System.Diagnostics;

namespace SharedKernel.Testing.Communication;

/// <summary>
/// Disposable scope helper that sets an ambient <see cref="Activity"/> with a specific trace id
/// (and optional parent) for correlation-id propagation tests.
/// </summary>
/// <remarks>
/// Restores the prior <see cref="Activity.Current"/> on disposal so tests do not leak ambient
/// state into subsequent test cases.
/// </remarks>
public sealed class AmbientActivityTestHelper : IDisposable
{
    private static readonly ActivitySource Source = new("SharedKernel.Testing.Communication");

    // ActivitySource.StartActivity returns null when no ActivityListener is sampling this source —
    // the default outside an OTel-instrumented host. Registering an always-sampling listener once,
    // scoped to this ActivitySource only, guarantees Start() always produces a real Activity in pure
    // unit tests that never wire up OpenTelemetry.
    private static readonly ActivityListener Listener = CreateAndRegisterListener();

    private readonly Activity? _previous;
    private readonly Activity _activity;

    private AmbientActivityTestHelper(Activity activity, Activity? previous)
    {
        _activity = activity;
        _previous = previous;
    }

    /// <summary>Gets the ambient <see cref="Activity"/> created by this scope.</summary>
    public Activity Activity => _activity;

    /// <summary>
    /// Starts a new ambient <see cref="Activity"/> with the given <paramref name="traceId"/>,
    /// optionally as a child of <paramref name="parentSpanId"/>.
    /// </summary>
    /// <param name="traceId">The W3C trace id to use for the ambient activity.</param>
    /// <param name="parentSpanId">An optional parent span id, establishing a parent/child relationship.</param>
    /// <returns>A disposable scope; dispose it to restore the prior ambient activity.</returns>
    public static AmbientActivityTestHelper Start(ActivityTraceId traceId, ActivitySpanId? parentSpanId = null)
    {
        var previous = Activity.Current;

        var context = new ActivityContext(
            traceId,
            parentSpanId ?? ActivitySpanId.CreateRandom(),
            ActivityTraceFlags.Recorded);

        var activity = Source.StartActivity("test-activity", ActivityKind.Internal, context)
            ?? throw new InvalidOperationException(
                "Failed to start an ambient Activity — ensure an ActivityListener is registered " +
                "or call Activity.HasListeners() guards are bypassed for this ActivitySource in tests.");

        Activity.Current = activity;

        return new AmbientActivityTestHelper(activity, previous);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _activity.Dispose();
        Activity.Current = _previous;
    }

    private static ActivityListener CreateAndRegisterListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => ReferenceEquals(source, Source),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
