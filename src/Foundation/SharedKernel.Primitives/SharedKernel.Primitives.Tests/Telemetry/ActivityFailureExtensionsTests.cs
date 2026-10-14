using System.Diagnostics;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Primitives.Telemetry;
using Xunit;

namespace SharedKernel.Primitives.Tests.Telemetry;

public sealed class ActivityFailureExtensionsTests : IDisposable
{
    private const string SecretMessage = "password=hunter2";

    private readonly ActivitySource _recordedSource = new($"{nameof(ActivityFailureExtensionsTests)}.recorded.{Guid.NewGuid()}");
    private readonly ActivitySource _unsampledSource = new($"{nameof(ActivityFailureExtensionsTests)}.unsampled.{Guid.NewGuid()}");
    private readonly ActivityListener _recordedListener;
    private readonly ActivityListener _unsampledListener;

    public ActivityFailureExtensionsTests()
    {
        _recordedListener = Listen(_recordedSource, ActivitySamplingResult.AllDataAndRecorded);
        _unsampledListener = Listen(_unsampledSource, ActivitySamplingResult.PropagationData);
    }

    public void Dispose()
    {
        _recordedListener.Dispose();
        _unsampledListener.Dispose();
        _recordedSource.Dispose();
        _unsampledSource.Dispose();
    }

    private sealed class CodedException(Error error) : Exception("coded"), IHasError
    {
        public Error Error { get; } = error;
    }

    private static ActivityListener Listen(ActivitySource source, ActivitySamplingResult sampling)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s => ReferenceEquals(s, source),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => sampling,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private Activity Start() => _recordedSource.StartActivity("op") ?? throw new InvalidOperationException("No activity.");

    private static string? Tag(Activity activity, string key) => activity.GetTagItem(key) as string;

    private static void AssertUntouched(Activity activity)
    {
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Null(activity.StatusDescription);
        Assert.Null(activity.GetTagItem(WellKnownTagKeys.ErrorCode));
        Assert.Null(activity.GetTagItem(WellKnownTagKeys.ErrorType));
        Assert.Empty(activity.Events);
    }

    // ---- RecordFailure / RecordResult ----

    [Fact]
    public void RecordFailure_SetsStatusDescribedByCodeAndTags()
    {
        using var activity = Start();
        var error = Error.NotFound("order.not_found", "The order was not found.");

        var outcome = activity.RecordFailure(error);

        Assert.Equal(WellKnownOutcomes.Failure, outcome);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("order.not_found", activity.StatusDescription);
        Assert.Equal("order.not_found", Tag(activity, WellKnownTagKeys.ErrorCode));
        Assert.Equal("NotFound", Tag(activity, WellKnownTagKeys.ErrorType));
        Assert.Empty(activity.Events);
    }

    [Fact]
    public void RecordFailure_NeverPutsTheMessageOnTheSpan()
    {
        using var activity = Start();

        activity.RecordFailure(Error.Unexpected("x.y", SecretMessage));

        Assert.DoesNotContain(activity.TagObjects, t => t.Value?.ToString()?.Contains("hunter2") == true);
        Assert.DoesNotContain("hunter2", activity.StatusDescription);
    }

    public static IEnumerable<object[]> EveryErrorType()
        => Enum.GetValues<ErrorType>().Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(EveryErrorType))]
    public void RecordFailure_MapsEveryErrorTypeMemberToItsName(ErrorType type)
    {
        // Enumerates the enum: a new member fails here until the recorder's switch maps it.
        using var activity = Start();

        activity.RecordFailure(new Error("t.code", "message", type));

        var tag = Tag(activity, WellKnownTagKeys.ErrorType);
        Assert.NotEqual("_OTHER", tag);
        Assert.Equal(type.ToString(), tag);
    }

    [Fact]
    public void RecordFailure_UndefinedErrorType_IsOther()
    {
        using var activity = Start();

        activity.RecordFailure(new Error("t.code", "message", (ErrorType)999));

        Assert.Equal("_OTHER", Tag(activity, WellKnownTagKeys.ErrorType));
    }

    [Fact]
    public void RecordFailure_NullError_Throws()
    {
        using var activity = Start();
        Assert.Throws<ArgumentNullException>(() => activity.RecordFailure(null!));
    }

    [Fact]
    public void RecordResult_Success_TouchesNothingAndNeverSetsOk()
    {
        using var activity = Start();

        var outcome = activity.RecordResult(Result.Success());

        Assert.Equal(WellKnownOutcomes.Success, outcome);
        AssertUntouched(activity);
    }

    [Fact]
    public void RecordResult_GenericSuccess_TouchesNothing()
    {
        using var activity = Start();

        var outcome = activity.RecordResult(Result<int>.Success(1));

        Assert.Equal(WellKnownOutcomes.Success, outcome);
        AssertUntouched(activity);
    }

    [Fact]
    public void RecordResult_Failure_DelegatesToRecordFailure()
    {
        using var activity = Start();

        var outcome = activity.RecordResult(Result.Failure(Error.Conflict("c.code", "m")));

        Assert.Equal(WellKnownOutcomes.Failure, outcome);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("c.code", activity.StatusDescription);
        Assert.Equal("Conflict", Tag(activity, WellKnownTagKeys.ErrorType));
    }

    [Fact]
    public void RecordResult_GenericFailure_DelegatesToRecordFailure()
    {
        using var activity = Start();

        var outcome = activity.RecordResult(Result<int>.Failure(Error.Validation("v.code", "m")));

        Assert.Equal(WellKnownOutcomes.Failure, outcome);
        Assert.Equal("v.code", Tag(activity, WellKnownTagKeys.ErrorCode));
        Assert.Equal("Validation", Tag(activity, WellKnownTagKeys.ErrorType));
    }

    // ---- RecordException ----

    [Fact]
    public void RecordException_SetsStatusToTheFullTypeNameNeverTheMessage()
    {
        using var activity = Start();

        var outcome = activity.RecordException(new InvalidOperationException(SecretMessage));

        Assert.Equal(WellKnownOutcomes.Exception, outcome);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, activity.StatusDescription);
        Assert.DoesNotContain("hunter2", activity.StatusDescription);
        Assert.Equal(typeof(InvalidOperationException).FullName, Tag(activity, WellKnownTagKeys.ErrorType));
        Assert.Null(activity.GetTagItem(WellKnownTagKeys.ErrorCode));
    }

    [Fact]
    public void RecordException_AddsTheFullExceptionEventWithTheStack()
    {
        using var activity = Start();
        Exception thrown;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException e)
        {
            thrown = e;
        }

        activity.RecordException(thrown);

        var exceptionEvent = Assert.Single(activity.Events);
        Assert.Equal("exception", exceptionEvent.Name);
        var tags = exceptionEvent.Tags.ToDictionary(t => t.Key, t => t.Value as string);
        Assert.Equal(typeof(InvalidOperationException).FullName, tags["exception.type"]);
        Assert.Equal("boom", tags["exception.message"]);
        Assert.Contains(nameof(RecordException_AddsTheFullExceptionEventWithTheStack), tags["exception.stacktrace"]);
    }

    [Fact]
    public void RecordException_ExceptionWithError_AddsErrorCodeFromIHasError()
    {
        using var activity = Start();

        activity.RecordException(new CodedException(Error.Conflict("coded.conflict", "m")));

        Assert.Equal("coded.conflict", Tag(activity, WellKnownTagKeys.ErrorCode));
        Assert.Equal(typeof(CodedException).FullName, Tag(activity, WellKnownTagKeys.ErrorType));
    }

    [Fact]
    public void RecordException_AggregateException_AddsOneEventPerFlattenedInner()
    {
        using var activity = Start();
        var aggregate = new AggregateException(
            new AggregateException(new InvalidOperationException("inner-most")),
            new ArgumentException("sibling"));

        var outcome = activity.RecordException(aggregate);

        Assert.Equal(WellKnownOutcomes.Exception, outcome);
        Assert.Equal(2, activity.Events.Count());
        Assert.Equal(typeof(AggregateException).FullName, Tag(activity, WellKnownTagKeys.ErrorType));
    }

    [Theory]
    [InlineData(typeof(OperationCanceledException))]
    [InlineData(typeof(TaskCanceledException))]
    public void RecordException_Cancellation_RecordsNothingAndReturnsCanceled(Type type)
    {
        using var activity = Start();
        var exception = (Exception)Activator.CreateInstance(type)!;

        var outcome = activity.RecordException(exception);

        Assert.Equal(WellKnownOutcomes.Canceled, outcome);
        AssertUntouched(activity);
    }

    [Fact]
    public void RecordException_NullException_Throws()
    {
        using var activity = Start();
        Assert.Throws<ArgumentNullException>(() => activity.RecordException(null!));
    }

    // ---- AddExceptionEvents ----

    [Fact]
    public void AddExceptionEvents_AddsOnlyEvents_NotStatusOrTags()
    {
        using var activity = Start();

        activity.AddExceptionEvents(new InvalidOperationException("boom"));

        Assert.Single(activity.Events);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Null(activity.GetTagItem(WellKnownTagKeys.ErrorType));
        Assert.Null(activity.GetTagItem(WellKnownTagKeys.ErrorCode));
    }

    [Fact]
    public void AddExceptionEvents_Aggregate_FlattensOneEventPerInner()
    {
        using var activity = Start();

        activity.AddExceptionEvents(new AggregateException(
            new AggregateException(new InvalidOperationException("a")),
            new ArgumentException("b")));

        Assert.Equal(2, activity.Events.Count());
    }

    [Fact]
    public void AddExceptionEvents_Cancellation_AddsNothing()
    {
        using var activity = Start();

        activity.AddExceptionEvents(new TaskCanceledException());

        AssertUntouched(activity);
    }

    // ---- No-op activities ----

    [Fact]
    public void NullActivity_IsANoOpAndStillReturnsTheOutcome()
    {
        Activity? activity = null;

        Assert.Equal(WellKnownOutcomes.Failure, activity.RecordFailure(Error.NotFound("a.b", "m")));
        Assert.Equal(WellKnownOutcomes.Exception, activity.RecordException(new InvalidOperationException()));
        Assert.Equal(WellKnownOutcomes.Canceled, activity.RecordException(new OperationCanceledException()));
        Assert.Equal(WellKnownOutcomes.Success, activity.RecordResult(Result.Success()));
        Assert.Equal(WellKnownOutcomes.Failure, activity.RecordResult(Result<int>.Failure(Error.NotFound("a.b", "m"))));
        activity.AddExceptionEvents(new InvalidOperationException());
    }

    [Fact]
    public void StoppedActivity_IsNotTouched()
    {
        var activity = Start();
        activity.Stop();
        Assert.True(activity.IsStopped);

        var failure = activity.RecordFailure(Error.NotFound("a.b", "m"));
        var exception = activity.RecordException(new InvalidOperationException("boom"));
        activity.AddExceptionEvents(new InvalidOperationException("boom"));

        Assert.Equal(WellKnownOutcomes.Failure, failure);
        Assert.Equal(WellKnownOutcomes.Exception, exception);
        AssertUntouched(activity);
    }

    [Fact]
    public void UnsampledActivity_IsNotTouched()
    {
        using var activity = _unsampledSource.StartActivity("op");
        Assert.NotNull(activity);
        Assert.False(activity.IsAllDataRequested);

        var failure = activity.RecordFailure(Error.NotFound("a.b", "m"));
        var exception = activity.RecordException(new InvalidOperationException("boom"));
        activity.AddExceptionEvents(new InvalidOperationException("boom"));

        Assert.Equal(WellKnownOutcomes.Failure, failure);
        Assert.Equal(WellKnownOutcomes.Exception, exception);
        AssertUntouched(activity);
    }
}
