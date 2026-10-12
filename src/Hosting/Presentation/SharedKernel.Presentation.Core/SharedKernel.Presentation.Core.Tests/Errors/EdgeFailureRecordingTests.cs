using System.Diagnostics;
using FluentAssertions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Presentation.Core.Tests.Errors;

/// <summary>
/// SK.14.EdgeErrorContext D-01: the one decision of what a failure the edge handled leaves on the ambient server span.
/// Server category fails the span through the shared recorder; client category only tags <c>error.code</c>; a
/// cancellation and a missing span record nothing.
/// </summary>
public sealed class EdgeFailureRecordingTests : IDisposable
{
    private static readonly Error ServerError = Error.Unavailable("search.unreachable", "Host 10.0.0.5 is down.");

    private static readonly Error ClientError = Error.NotFound("order.not_found", "Order 42 was not found.");

    private readonly string _sourceName = "Test.EdgeFailureRecording." + Guid.NewGuid().ToString("N");
    private readonly ActivityRecorder _recorder;
    private readonly ActivitySource _source;

    public EdgeFailureRecordingTests()
    {
        _recorder = ActivityRecorder.StartRecording(_sourceName);
        _source = new ActivitySource(_sourceName);
    }

    public void Dispose()
    {
        _source.Dispose();
        _recorder.Dispose();
    }

    [Fact]
    public void ServerCategoryFailure_WithoutAnException_FailsTheSpan()
    {
        var span = Run(() => EdgeFailureRecording.Record(ServerError));

        span.ShouldBeRecordedFailure(ServerError);
        span.Events.Should().BeEmpty();
    }

    [Fact]
    public void ServerCategoryFailure_WithAnException_FailsTheSpan_WithOneExceptionEvent()
    {
        var exception = ServerError.ToException();

        var span = Run(() => EdgeFailureRecording.Record(ServerError, exception));

        span.ShouldBeRecordedException(exception.GetType(), ServerError.Code);
        span.Events.Count(e => e.Name == "exception").Should().Be(1);
    }

    [Fact]
    public void UnclassifiedException_IsTaggedWithTheCodeItIsAnsweredWith()
    {
        var error = Error.Unexpected(ErrorCodes.Unexpected.Default, "An unexpected error occurred.");
        var exception = new InvalidOperationException("Server=db;Password=secret");

        var span = Run(() => EdgeFailureRecording.Record(error, exception));

        span.ShouldBeRecordedException(typeof(InvalidOperationException), ErrorCodes.Unexpected.Default);
        span.StatusDescription.Should().NotContain("secret");
    }

    [Fact]
    public void ClientCategoryFailure_OnlyTagsTheCode_WithOrWithoutAnException()
    {
        var withoutException = Run(() => EdgeFailureRecording.Record(ClientError));
        var withException = Run(() => EdgeFailureRecording.Record(ClientError, ClientError.ToException()));

        foreach (var span in new[] { withoutException, withException })
        {
            span.ShouldBeUnset();
            span.ShouldHaveTag(WellKnownTagKeys.ErrorCode, "order.not_found");
            span.ShouldNotHaveTag(WellKnownTagKeys.ErrorType);
            span.Events.Should().BeEmpty();
        }
    }

    [Fact]
    public void ClientCode_OnlyTagsTheCode()
    {
        var span = Run(() => EdgeFailureRecording.RecordClientCode("grpc.not_found"));

        span.ShouldBeUnset();
        span.ShouldHaveTag(WellKnownTagKeys.ErrorCode, "grpc.not_found");
        span.ShouldNotHaveTag(WellKnownTagKeys.ErrorType);
    }

    [Fact]
    public void ACancellation_RecordsNothing_NotEvenACode()
    {
        var span = Run(() => EdgeFailureRecording.Record(Error.Unexpected("x.y", "z"), new OperationCanceledException()));

        span.ShouldBeRecordedCancellation();
    }

    [Fact]
    public void WithoutASpan_NothingThrows()
    {
        Activity.Current = null;

        var act = () =>
        {
            EdgeFailureRecording.Record(ServerError);
            EdgeFailureRecording.Record(ServerError, ServerError.ToException());
            EdgeFailureRecording.Record(ClientError);
            EdgeFailureRecording.RecordClientCode("grpc.not_found");
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void AStoppedSpan_IsNeverTouched()
    {
        Activity stopped;
        using (stopped = _source.StartActivity("request")!)
        {
        }

        Activity.Current = stopped;
        EdgeFailureRecording.Record(ServerError);
        EdgeFailureRecording.RecordClientCode("grpc.not_found");
        Activity.Current = null;

        stopped.ShouldBeRecordedSuccess();
    }

    [Fact]
    public void TheSpanIsFailedExactlyForTheServerCategory_OfEveryErrorType()
    {
        foreach (var type in Enum.GetValues<ErrorType>())
        {
            EdgeFailureRecording.FailsSpan(type).Should().Be(ErrorPresentation.IsServerError(type), $"{type} is one category on every protocol");
        }

        EdgeFailureRecording.FailsSpan((ErrorType)999).Should().BeTrue("an undefined type is answered 500");
    }

    private Activity Run(Action record)
    {
        using (var activity = _source.StartActivity("request"))
        {
            activity.Should().NotBeNull();
            record();
        }

        return _recorder.RecordedActivities.Last();
    }
}
