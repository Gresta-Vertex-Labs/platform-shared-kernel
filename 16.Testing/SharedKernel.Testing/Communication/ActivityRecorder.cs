using System.Collections.Concurrent;
using System.Diagnostics;

namespace SharedKernel.Testing.Communication;

/// <summary>
/// Records every <see cref="Activity"/> started against a single, named <see cref="ActivitySource"/>
/// while recording is active, so a test can assert span count/tags/duration after the fact.
/// </summary>
/// <remarks>
/// Additive sibling to <see cref="AmbientActivityTestHelper"/>, not a replacement —
/// <see cref="AmbientActivityTestHelper"/> sets <see cref="Activity.Current"/> to a caller-built
/// <see cref="Activity"/> for propagation/correlation-id tests (an ambient-context setter);
/// <see cref="ActivityRecorder"/> instead listens for spans started by code under test against a
/// named, externally-owned <see cref="ActivitySource"/> (e.g. 05.Application's
/// <c>"SharedKernel.Application"</c>) so a test can assert on them after execution completes
/// (a recording listener). Each instance registers its own process-scoped <see cref="ActivityListener"/>
/// filtered to one <see cref="ActivitySource"/> name only, so multiple <see cref="ActivityRecorder"/>
/// instances recording different source names never interfere with one another.
/// </remarks>
public sealed class ActivityRecorder : IDisposable
{
    private readonly ConcurrentQueue<Activity> _recordedActivities = new();
    private readonly ActivityListener _listener;

    private ActivityRecorder(string activitySourceName)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == activitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _recordedActivities.Enqueue(activity),
        };

        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>
    /// Every <see cref="Activity"/> started against the recorded <see cref="ActivitySource"/> while
    /// recording is active, in start order.
    /// </summary>
    public IReadOnlyList<Activity> RecordedActivities => [.. _recordedActivities];

    /// <summary>
    /// Registers a process-scoped <see cref="ActivityListener"/> filtered to the
    /// <see cref="ActivitySource"/> named <paramref name="activitySourceName"/> only, and begins
    /// recording every <see cref="Activity"/> started against it.
    /// </summary>
    /// <param name="activitySourceName">The exact <see cref="ActivitySource.Name"/> to record.</param>
    /// <returns>A disposable recorder; dispose it to unregister the listener.</returns>
    public static ActivityRecorder StartRecording(string activitySourceName) => new(activitySourceName);

    /// <inheritdoc />
    public void Dispose() => _listener.Dispose();
}
