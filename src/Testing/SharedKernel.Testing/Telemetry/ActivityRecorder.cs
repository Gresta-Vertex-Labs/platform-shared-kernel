using System.Collections.Concurrent;
using System.Diagnostics;
using SharedKernel.Testing.Communication;

namespace SharedKernel.Testing.Telemetry;

/// <summary>
/// Records every <see cref="Activity"/> started and stopped on the <see cref="ActivitySource"/>s a test names, so the
/// test can assert on spans after the code under test has run.
/// </summary>
/// <remarks>
/// <para>
/// One recorder is one process-scoped <see cref="ActivityListener"/> sampling <see cref="ActivitySamplingResult.AllDataAndRecorded"/>,
/// filtered by exact source name or by name prefix. Recorders never interfere with each other. Reads are snapshots:
/// a list returned earlier does not change, and it stays readable after <see cref="Dispose"/>. <see cref="Dispose"/>
/// is idempotent and nothing is recorded after it.
/// </para>
/// <para>
/// <b>Static sources are shared.</b> A static <see cref="ActivitySource"/> also produces the spans of tests running in
/// parallel. Isolate a test with <see cref="ForTrace"/> (start the span under a trace the test owns) or filter by tags,
/// or keep the test collection serial. Use a source name that is unique per test when the test owns the source.
/// </para>
/// <para>
/// Sibling, not replacement, of <see cref="AmbientActivityTestHelper"/>, which sets <see cref="Activity.Current"/> for
/// propagation tests.
/// </para>
/// </remarks>
public sealed class ActivityRecorder : IDisposable
{
    private readonly ConcurrentQueue<Activity> _recorded = new();
    private readonly ConcurrentQueue<Activity> _started = new();
    private readonly ActivityListener _listener;
    private volatile bool _disposed;

    private ActivityRecorder(Func<string, bool> sourceFilter)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => !_disposed && sourceFilter(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity =>
            {
                if (!_disposed)
                {
                    _started.Enqueue(activity);
                }
            },
            ActivityStopped = activity =>
            {
                if (!_disposed)
                {
                    _recorded.Enqueue(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>
    /// The activities that have stopped since recording began (or since <see cref="Clear"/>), in stop order: a child
    /// that ends before its parent comes first.
    /// </summary>
    public IReadOnlyList<Activity> RecordedActivities => [.. _recorded];

    /// <summary>
    /// Every activity started since recording began (or since <see cref="Clear"/>), in start order, including spans
    /// that were never stopped.
    /// </summary>
    public IReadOnlyList<Activity> StartedActivities => [.. _started];

    /// <summary>Records every activity of the source named exactly <paramref name="sourceName"/>.</summary>
    /// <param name="sourceName">The exact <see cref="ActivitySource.Name"/>.</param>
    /// <returns>A disposable recorder; dispose it to unregister the listener.</returns>
    public static ActivityRecorder StartRecording(string sourceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceName);
        return new ActivityRecorder(name => string.Equals(name, sourceName, StringComparison.Ordinal));
    }

    /// <summary>Records every activity of the sources named exactly <paramref name="sourceNames"/>.</summary>
    /// <param name="sourceNames">Exact <see cref="ActivitySource.Name"/> values.</param>
    /// <returns>A disposable recorder; dispose it to unregister the listener.</returns>
    public static ActivityRecorder StartRecording(params string[] sourceNames)
    {
        ArgumentNullException.ThrowIfNull(sourceNames);
        if (sourceNames.Length == 0)
        {
            throw new ArgumentException("At least one source name is required.", nameof(sourceNames));
        }

        var names = new HashSet<string>(sourceNames, StringComparer.Ordinal);
        return new ActivityRecorder(names.Contains);
    }

    /// <summary>Records every activity of every source whose name starts with <paramref name="sourceNamePrefix"/>.</summary>
    /// <param name="sourceNamePrefix">The ordinal name prefix, for example <c>"SharedKernel."</c>.</param>
    /// <returns>A disposable recorder; dispose it to unregister the listener.</returns>
    public static ActivityRecorder StartRecordingPrefix(string sourceNamePrefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceNamePrefix);
        return new ActivityRecorder(name => name.StartsWith(sourceNamePrefix, StringComparison.Ordinal));
    }

    /// <summary>The stopped activities that belong to <paramref name="traceId"/>, in stop order.</summary>
    /// <param name="traceId">The trace a test started its operation under.</param>
    /// <returns>A snapshot.</returns>
    public IReadOnlyList<Activity> ForTrace(ActivityTraceId traceId) =>
        [.. _recorded.Where(activity => activity.TraceId == traceId)];

    /// <summary>Forgets everything recorded so far; recording continues.</summary>
    public void Clear()
    {
        _recorded.Clear();
        _started.Clear();
    }

    /// <summary>Asserts every started activity has been stopped (no span leaked).</summary>
    /// <returns>This recorder.</returns>
    /// <exception cref="InvalidOperationException">At least one started activity was never stopped.</exception>
    public ActivityRecorder ShouldHaveStoppedAll()
    {
        var open = _started.Where(activity => !activity.IsStopped).Select(activity => $"'{activity.OperationName}'").ToList();
        if (open.Count > 0)
        {
            throw new InvalidOperationException(
                $"Expected every started activity to be stopped, but {open.Count} {(open.Count == 1 ? "was" : "were")} not: {string.Join(", ", open)}.");
        }

        return this;
    }

    /// <summary>Unregisters the listener. Idempotent; snapshots taken earlier stay readable.</summary>
    public void Dispose()
    {
        _disposed = true;
        _listener.Dispose();
    }
}
