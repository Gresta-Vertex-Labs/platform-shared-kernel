using System.Diagnostics;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Primitives.Telemetry;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Telemetry;

/// <summary>
/// The assertions against the real producer, <see cref="ActivityFailureExtensions"/>: a regression in 01.Core's
/// recorder fails here, and so does an assertion that drifts from it.
/// </summary>
public sealed class ProducerConformanceTests : IDisposable
{
    private readonly SpanFactory _spans = new();

    public void Dispose() => _spans.Dispose();

    private Activity Record(Func<Activity, string> record, out string outcome)
    {
        var activity = _spans.Source.StartActivity("conformance op")!;
        outcome = record(activity);
        activity.SetTag(WellKnownTagKeys.Outcome, outcome);
        activity.Stop();
        return activity;
    }

    public static TheoryData<ErrorType> DefinedErrorTypes()
    {
        var data = new TheoryData<ErrorType>();
        foreach (var type in Enum.GetValues<ErrorType>())
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DefinedErrorTypes))]
    public void EveryErrorType_IsRecordedAsFailure_AndMappedToItsOwnName(ErrorType type)
    {
        var error = new Error("conformance.failed", "Failed.", type);

        var span = Record(a => a.RecordFailure(error), out var outcome);

        span.ShouldBeRecordedFailure(error).ShouldHaveOutcome(WellKnownOutcomes.Failure);
        Assert.Equal(WellKnownOutcomes.Failure, outcome);
        // A member added to ErrorType falls to "_OTHER" in both the producer and the assertion until someone maps it;
        // this line is what makes that omission fail.
        Assert.Equal(type.ToString(), span.GetTagItem(WellKnownTagKeys.ErrorType));
    }

    [Fact]
    public void AnUndefinedErrorType_IsRecordedAsOther()
    {
        var error = new Error("conformance.failed", "Failed.", (ErrorType)999);

        Record(a => a.RecordFailure(error), out _).ShouldBeRecordedFailure(error).ShouldHaveTag(WellKnownTagKeys.ErrorType, "_OTHER");
    }

    [Fact]
    public void RecordResult_FailureAndSuccess()
    {
        var error = Error.Conflict("conformance.conflict", "Conflict.");

        Record(a => a.RecordResult(Result.Failure(error)), out var failure).ShouldBeRecordedFailure(error);
        Record(a => a.RecordResult(Result<int>.Failure(error)), out var typedFailure).ShouldBeRecordedFailure(error);
        Record(a => a.RecordResult(Result.Success()), out var success).ShouldBeRecordedSuccess().ShouldHaveOutcome(WellKnownOutcomes.Success);
        Record(a => a.RecordResult(Result<int>.Success(1)), out var typedSuccess).ShouldBeRecordedSuccess();

        Assert.Equal([WellKnownOutcomes.Failure, WellKnownOutcomes.Failure, WellKnownOutcomes.Success, WellKnownOutcomes.Success],
            [failure, typedFailure, success, typedSuccess]);
    }

    [Fact]
    public void RecordException_PlainException()
    {
        var span = Record(a => a.RecordException(new InvalidOperationException("secret detail")), out var outcome);

        span.ShouldBeRecordedException(typeof(InvalidOperationException)).ShouldHaveOutcome(WellKnownOutcomes.Exception);
        Assert.Equal(WellKnownOutcomes.Exception, outcome);
        Assert.DoesNotContain("secret detail", span.StatusDescription);
    }

    [Fact]
    public void RecordException_SharedKernelException_CarriesItsErrorCode()
    {
        var exception = new NotFoundException(Error.NotFound("conformance.gone", "Gone."));

        Record(a => a.RecordException(exception), out _)
            .ShouldBeRecordedException(typeof(NotFoundException), "conformance.gone");
    }

    [Fact]
    public void RecordException_AggregateException_RecordsOneEventPerInnerException()
    {
        var aggregate = new AggregateException(new InvalidOperationException("one"), new ArgumentException("two"));

        var span = Record(a => a.RecordException(aggregate), out _);

        span.ShouldBeRecordedException(typeof(AggregateException));
        Assert.Equal(2, span.Events.Count(e => e.Name == "exception"));
    }

    [Theory]
    [InlineData(typeof(TaskCanceledException))]
    [InlineData(typeof(OperationCanceledException))]
    public void RecordException_Cancellation_LeavesTheSpanQuiet(Type cancellationType)
    {
        var exception = (Exception)Activator.CreateInstance(cancellationType)!;

        var span = Record(a => a.RecordException(exception), out var outcome);

        span.ShouldBeRecordedCancellation().ShouldHaveOutcome(WellKnownOutcomes.Canceled);
        Assert.Equal(WellKnownOutcomes.Canceled, outcome);
    }

    [Fact]
    public void ASpanTheProducerLeftUntouched_IsASuccess_AndNeverOk()
    {
        var span = Record(_ => WellKnownOutcomes.Success, out _);

        span.ShouldBeRecordedSuccess();
        Assert.NotEqual(ActivityStatusCode.Ok, span.Status);
    }

    [Fact]
    public void RecordedFailureAssertion_RejectsAnExceptionShapedSpan_AndTheOtherWayAround()
    {
        var error = Error.NotFound("conformance.gone", "Gone.");
        var failure = Record(a => a.RecordFailure(error), out _);
        var exception = Record(a => a.RecordException(new InvalidOperationException("x")), out _);

        Assert.Throws<InvalidOperationException>(() => exception.ShouldBeRecordedFailure(error));
        Assert.Throws<InvalidOperationException>(() => failure.ShouldBeRecordedException(typeof(InvalidOperationException)));
        Assert.Throws<InvalidOperationException>(() => failure.ShouldBeRecordedSuccess());
        Assert.Throws<InvalidOperationException>(() => exception.ShouldBeRecordedCancellation());
    }
}
