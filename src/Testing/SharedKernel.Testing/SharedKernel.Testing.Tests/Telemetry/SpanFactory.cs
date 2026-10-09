using System.Diagnostics;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Testing.SelfTests.Telemetry;

/// <summary>
/// A process-unique <see cref="ActivitySource"/> plus a recorder, so a test builds real, stopped spans it owns without
/// seeing any other test's spans.
/// </summary>
internal sealed class SpanFactory : IDisposable
{
    public SpanFactory(string? sourceName = null)
    {
        Source = new ActivitySource(sourceName ?? Unique("selftest.source"));
        Recorder = ActivityRecorder.StartRecording(Source.Name);
    }

    public ActivitySource Source { get; }

    public ActivityRecorder Recorder { get; }

    public static string Unique(string prefix) => $"{prefix}.{Guid.NewGuid():N}";

    /// <summary>Starts a span, lets <paramref name="configure"/> fill it, stops it and returns it.</summary>
    public Activity Make(string name, ActivityKind kind, Action<Activity>? configure = null)
    {
        var activity = Source.StartActivity(name, kind) ?? throw new InvalidOperationException("The span was not sampled.");
        configure?.Invoke(activity);
        activity.Stop();
        return activity;
    }

    public void Dispose()
    {
        Recorder.Dispose();
        Source.Dispose();
    }
}
