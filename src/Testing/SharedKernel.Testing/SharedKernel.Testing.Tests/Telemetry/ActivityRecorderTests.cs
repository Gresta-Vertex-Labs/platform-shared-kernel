using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Telemetry;

public sealed class ActivityRecorderTests
{
    [Fact]
    public void StartRecording_RecordsActivitiesStartedAgainstTheNamedSource()
    {
        using var source = new ActivitySource(Unique("test.source"));
        using var recorder = ActivityRecorder.StartRecording(source.Name);

        using (var activity = source.StartActivity("first-operation"))
        {
            activity?.SetTag("operation.index", 1);
        }

        using (var activity = source.StartActivity("second-operation"))
        {
            activity?.SetTag("operation.index", 2);
        }

        Assert.Equal(2, recorder.RecordedActivities.Count);
        Assert.Equal("first-operation", recorder.RecordedActivities[0].OperationName);
        Assert.Equal("second-operation", recorder.RecordedActivities[1].OperationName);
        Assert.Equal(1, recorder.RecordedActivities[0].GetTagItem("operation.index"));
        Assert.Equal(2, recorder.RecordedActivities[1].GetTagItem("operation.index"));
    }

    [Fact]
    public void StartRecording_PreservesOrderAcrossManyActivities()
    {
        using var source = new ActivitySource(Unique("test.source.order"));
        using var recorder = ActivityRecorder.StartRecording(source.Name);

        const int activityCount = 5;
        for (var i = 0; i < activityCount; i++)
        {
            using var activity = source.StartActivity($"operation-{i}");
        }

        Assert.Equal(activityCount, recorder.RecordedActivities.Count);
        for (var i = 0; i < activityCount; i++)
        {
            Assert.Equal($"operation-{i}", recorder.RecordedActivities[i].OperationName);
        }
    }

    [Fact]
    public void StartRecording_IndependentRecorderOnDifferentSourceDoesNotCaptureOtherSourceActivities()
    {
        using var sourceA = new ActivitySource(Unique("test.source.a"));
        using var sourceB = new ActivitySource(Unique("test.source.b"));

        using var recorderA = ActivityRecorder.StartRecording(sourceA.Name);
        using var recorderB = ActivityRecorder.StartRecording(sourceB.Name);

        using (sourceA.StartActivity("from-a"))
        {
        }

        using (sourceB.StartActivity("from-b-1"))
        {
        }

        using (sourceB.StartActivity("from-b-2"))
        {
        }

        Assert.Equal(["from-a"], recorderA.RecordedActivities.Select(a => a.OperationName));
        Assert.Equal(["from-b-1", "from-b-2"], recorderB.RecordedActivities.Select(a => a.OperationName));
    }

    [Fact]
    public void StartRecording_SeveralNames_RecordsEachNamedSourceAndNothingElse()
    {
        using var first = new ActivitySource(Unique("multi.first"));
        using var second = new ActivitySource(Unique("multi.second"));
        using var unlistened = new ActivitySource(Unique("multi.unlistened"));
        using var recorder = ActivityRecorder.StartRecording(first.Name, second.Name);

        using (first.StartActivity("a"))
        {
        }

        using (second.StartActivity("b"))
        {
        }

        using (unlistened.StartActivity("c"))
        {
        }

        Assert.Equal(["a", "b"], recorder.RecordedActivities.Select(a => a.OperationName));
    }

    [Fact]
    public void StartRecordingPrefix_RecordsEverySourceUnderThePrefix()
    {
        var prefix = Unique("prefixed") + ".";
        using var first = new ActivitySource(prefix + "one");
        using var second = new ActivitySource(prefix + "two");
        using var other = new ActivitySource(Unique("elsewhere"));
        using var recorder = ActivityRecorder.StartRecordingPrefix(prefix);

        using (first.StartActivity("a"))
        {
        }

        using (second.StartActivity("b"))
        {
        }

        using (other.StartActivity("c"))
        {
        }

        Assert.Equal(["a", "b"], recorder.RecordedActivities.Select(a => a.OperationName));
    }

    [Fact]
    public void StartRecording_WithoutNames_Throws()
    {
        Assert.Throws<ArgumentException>(() => ActivityRecorder.StartRecording(Array.Empty<string>()));
    }

    [Fact]
    public void NestedSpans_AreRecordedInStopOrder_AndStartedInStartOrder_WithParentLinks()
    {
        using var source = new ActivitySource(Unique("nested"));
        using var recorder = ActivityRecorder.StartRecording(source.Name);

        using (var parent = source.StartActivity("parent"))
        {
            using var child = source.StartActivity("child");
            Assert.NotNull(child);
            Assert.NotNull(parent);
            Assert.Equal(parent.SpanId, child.ParentSpanId);
        }

        Assert.Equal(["child", "parent"], recorder.RecordedActivities.Select(a => a.OperationName));
        Assert.Equal(["parent", "child"], recorder.StartedActivities.Select(a => a.OperationName));
        recorder.RecordedActivities.ShouldHaveSpan("child").ShouldHaveParent(recorder.RecordedActivities.ShouldHaveSpan("parent"));
    }

    [Fact]
    public void ASpanNeverStopped_AppearsOnlyInStartedActivities_AndTripsShouldHaveStoppedAll()
    {
        using var source = new ActivitySource(Unique("leak"));
        using var recorder = ActivityRecorder.StartRecording(source.Name);

        var leaked = source.StartActivity("leaked");
        using (source.StartActivity("finished"))
        {
        }

        Assert.NotNull(leaked);
        Assert.Equal(["finished"], recorder.RecordedActivities.Select(a => a.OperationName));
        Assert.Equal(["leaked", "finished"], recorder.StartedActivities.Select(a => a.OperationName));
        var ex = Assert.Throws<InvalidOperationException>(recorder.ShouldHaveStoppedAll);
        Assert.Contains("'leaked'", ex.Message);

        leaked.Stop();
        Assert.Same(recorder, recorder.ShouldHaveStoppedAll());
    }

    [Fact]
    public void ForTrace_IsolatesTwoInterleavedTraces()
    {
        using var source = new ActivitySource(Unique("interleaved"));
        using var recorder = ActivityRecorder.StartRecording(source.Name);

        var traceA = ActivityTraceId.CreateRandom();
        var traceB = ActivityTraceId.CreateRandom();
        var contextA = new ActivityContext(traceA, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
        var contextB = new ActivityContext(traceB, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);

        var a1 = source.StartActivity("a1", ActivityKind.Internal, contextA);
        var b1 = source.StartActivity("b1", ActivityKind.Internal, contextB);
        a1!.Stop();
        var a2 = source.StartActivity("a2", ActivityKind.Internal, contextA);
        a2!.Stop();
        b1!.Stop();

        Assert.Equal(["a1", "a2"], recorder.ForTrace(traceA).Select(a => a.OperationName));
        Assert.Equal(["b1"], recorder.ForTrace(traceB).Select(a => a.OperationName));
        Assert.Empty(recorder.ForTrace(ActivityTraceId.CreateRandom()));
    }

    [Fact]
    public void Clear_ForgetsRecordedAndStarted_ButKeepsRecording()
    {
        using var source = new ActivitySource(Unique("clear"));
        using var recorder = ActivityRecorder.StartRecording(source.Name);

        using (source.StartActivity("before"))
        {
        }

        recorder.Clear();
        using (source.StartActivity("after"))
        {
        }

        Assert.Equal(["after"], recorder.RecordedActivities.Select(a => a.OperationName));
        Assert.Equal(["after"], recorder.StartedActivities.Select(a => a.OperationName));
    }

    [Fact]
    public void Dispose_StopsFurtherRecording()
    {
        using var source = new ActivitySource(Unique("test.source.dispose"));
        var recorder = ActivityRecorder.StartRecording(source.Name);

        using (source.StartActivity("before-dispose"))
        {
        }

        recorder.Dispose();

        using (source.StartActivity("after-dispose"))
        {
        }

        Assert.Single(recorder.RecordedActivities);
        Assert.Equal("before-dispose", recorder.RecordedActivities[0].OperationName);
        Assert.Single(recorder.StartedActivities);
    }

    [Fact]
    public void Dispose_KeepsEarlierSnapshotsReadable_IsIdempotent_AndLeavesASecondRecorderUnaffected()
    {
        using var source = new ActivitySource(Unique("dispose.two"));
        var first = ActivityRecorder.StartRecording(source.Name);
        using var second = ActivityRecorder.StartRecording(source.Name);

        using (var activity = source.StartActivity("work"))
        {
            activity!.SetTag("k", "v");
            activity.SetStatus(ActivityStatusCode.Error, "boom");
            activity.AddEvent(new ActivityEvent("evt"));
        }

        var snapshot = first.RecordedActivities;
        first.Dispose();
        first.Dispose();

        using (source.StartActivity("later"))
        {
        }

        Assert.Equal("v", snapshot[0].GetTagItem("k"));
        Assert.Equal(ActivityStatusCode.Error, snapshot[0].Status);
        Assert.Single(snapshot[0].Events);
        Assert.Single(first.RecordedActivities);
        Assert.Equal(["work", "later"], second.RecordedActivities.Select(a => a.OperationName));
    }

    [Fact]
    public async Task ConcurrentSpans_AreAllRecorded_WithExactCountsAndReadableSnapshots()
    {
        using var source = new ActivitySource(Unique("concurrent"));
        using var meter = new Meter(Unique("concurrent.meter"));
        var counter = meter.CreateCounter<long>("sharedkernel.selftest.count");
        using var spans = ActivityRecorder.StartRecording(source.Name);
        using var metrics = MetricRecorder.StartRecording(meter.Name);

        const int iterations = 2000;
        var readers = Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                _ = spans.RecordedActivities.Count;
                _ = metrics.Measurements.Count;
            }
        });

        Parallel.For(0, iterations, i =>
        {
            var parity = i % 2 == 0 ? "even" : "odd";
            using var activity = source.StartActivity("work");
            activity!.SetTag("parity", parity);
            counter.Add(1, new KeyValuePair<string, object?>("parity", parity));
        });
        await readers;

        Assert.Equal(iterations, spans.RecordedActivities.Count);
        Assert.Equal(iterations, spans.StartedActivities.Count);
        spans.ShouldHaveStoppedAll();
        Assert.Equal(iterations / 2, spans.RecordedActivities.Count(a => (string?)a.GetTagItem("parity") == "even"));
        Assert.Equal(iterations, metrics.Count("sharedkernel.selftest.count"));
        Assert.Equal(iterations / 2, metrics.Sum("sharedkernel.selftest.count", new KeyValuePair<string, object?>("parity", "odd")));
    }

    private static string Unique(string prefix) => SpanFactory.Unique(prefix);
}
