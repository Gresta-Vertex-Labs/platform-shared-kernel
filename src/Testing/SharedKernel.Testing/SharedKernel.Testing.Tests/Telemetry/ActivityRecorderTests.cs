using System.Diagnostics;
using SharedKernel.Testing.Communication;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Communication;

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
    public void StartRecording_PreservesStartOrderAcrossManyActivities()
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
    }

    /// <summary>
    /// Generates a process-unique ActivitySource name per test so parallel xUnit test execution
    /// (this domain's standing thread-safety assumption) never lets two tests' ActivityListeners
    /// observe each other's Activity instances.
    /// </summary>
    private static string Unique(string prefix) => $"{prefix}.{Guid.NewGuid():N}";
}
