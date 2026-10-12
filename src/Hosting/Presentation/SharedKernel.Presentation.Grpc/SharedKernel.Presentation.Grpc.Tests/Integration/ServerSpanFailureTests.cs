using System.Diagnostics;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;
using SharedKernel.Presentation.Grpc.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Integration;

/// <summary>
/// SK.14.EdgeErrorContext: the exception interceptor records what it mapped on the framework's server span
/// (<c>HttpRequestIn</c>, the ambient <c>Activity.Current</c> of the call) once, for all four call shapes. A
/// server-category failure fails the span (status <c>Error</c>, <c>error.type</c>, <c>error.code</c>, one exception
/// event; a timeout and a foreign <see cref="RpcException"/> are recorded failures, never with the foreign detail); a
/// client-category failure only tags <c>error.code</c>; a cancelled call or a passed deadline leaves nothing.
/// </summary>
public sealed class ServerSpanFailureTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    public enum Shape
    {
        Unary,
        ClientStreaming,
        ServerStreaming,
        Duplex,
    }

    [Theory]
    [InlineData(Shape.Unary)]
    [InlineData(Shape.ClientStreaming)]
    [InlineData(Shape.ServerStreaming)]
    [InlineData(Shape.Duplex)]
    public async Task ServerCategoryException_FailsTheServerSpan_InEveryCallShape(Shape shape)
    {
        using var recorder = ServerSpans.Record();
        await using var app = await GrpcTestHost.StartAsync();
        var expected = Error.Unavailable("search.unreachable", "x").ToException();

        var (exception, span) = await FailAsync(app, recorder, shape, Failures.UnavailableException);

        exception.StatusCode.Should().Be(StatusCode.Unavailable);
        span.ShouldBeRecordedException(expected.GetType(), "search.unreachable");
        span.Events.Count(e => e.Name == "exception").Should().Be(1);
    }

    [Theory]
    [InlineData(Shape.Unary)]
    [InlineData(Shape.ClientStreaming)]
    [InlineData(Shape.ServerStreaming)]
    [InlineData(Shape.Duplex)]
    public async Task UnclassifiedException_FailsTheServerSpan_WithTheTypeNameNeverTheMessage(Shape shape)
    {
        using var recorder = ServerSpans.Record();
        await using var app = await GrpcTestHost.StartAsync();

        var (exception, span) = await FailAsync(app, recorder, shape, Failures.Unknown);

        exception.StatusCode.Should().Be(StatusCode.Internal);
        span.ShouldBeRecordedException(typeof(InvalidOperationException), ErrorCodes.Unexpected.Default);
        span.Events.Count(e => e.Name == "exception").Should().Be(1);
        span.StatusDescription.Should().Be(typeof(InvalidOperationException).FullName).And.NotContain("secret");
        span.ShouldNotLeak("secret");
    }

    [Theory]
    [InlineData(Shape.Unary)]
    [InlineData(Shape.Duplex)]
    public async Task TimeoutInsideTheService_IsARecordedFailure_NotAnException(Shape shape)
    {
        using var recorder = ServerSpans.Record();
        await using var app = await GrpcTestHost.StartAsync();

        var (exception, span) = await FailAsync(app, recorder, shape, Failures.TimeoutException);

        exception.StatusCode.Should().Be(StatusCode.DeadlineExceeded);
        span.ShouldBeRecordedFailure(Error.Timeout(ErrorCodes.Timeout.Default, "The operation did not complete in time."));
        span.Events.Should().NotContain(e => e.Name == "exception");
    }

    [Fact]
    public async Task TimeoutResult_FailsTheServerSpan_WithItsOwnCode()
    {
        using var recorder = ServerSpans.Record();
        await using var app = await GrpcTestHost.StartAsync();
        var expected = Error.Timeout("search.timeout", "x").ToException();

        var (exception, span) = await FailAsync(app, recorder, Shape.Unary, Failures.TimeoutResult);

        exception.StatusCode.Should().Be(StatusCode.DeadlineExceeded);
        span.ShouldBeRecordedException(expected.GetType(), "search.timeout");
    }

    [Theory]
    [InlineData(Failures.UnavailableRpc, StatusCode.Unavailable, "grpc.unavailable", ErrorType.Unavailable)]
    [InlineData(Failures.InternalRpcWithTrailers, StatusCode.Internal, "grpc.internal", ErrorType.Unexpected)]
    public async Task ForeignServerCategoryRpcException_IsARecordedFailure_WithoutItsDetail(
        string failure,
        StatusCode status,
        string code,
        ErrorType type)
    {
        using var recorder = ServerSpans.Record();
        await using var app = await GrpcTestHost.StartAsync();

        var (exception, span) = await FailAsync(app, recorder, Shape.Unary, failure);

        exception.StatusCode.Should().Be(status);
        span.ShouldBeRecordedFailure(new Error(code, "ignored", type));
        span.Events.Should().NotContain(e => e.Name == "exception");
        span.ShouldNotLeak(Failures.InternalRpcDetail, Failures.InternalTrailerValue, Failures.InternalTrailerKey);
    }

    [Theory]
    [InlineData(Failures.NotFoundException, "order.not_found")]
    [InlineData(Failures.ConflictException, "order.version_conflict")]
    [InlineData(Failures.ValidationException, ErrorCodes.Validation.Failed)]
    [InlineData(Failures.SingleValidationException, "customer.name_required")]
    [InlineData(Failures.Rpc, "grpc.resource_exhausted")]
    [InlineData(Failures.NotFoundRpc, "grpc.not_found")]
    public async Task ClientCategoryFailure_OnlyTagsTheErrorCode(string failure, string code)
    {
        using var recorder = ServerSpans.Record();
        await using var app = await GrpcTestHost.StartAsync();

        var (_, span) = await FailAsync(app, recorder, Shape.Unary, failure);

        span.ShouldBeUnset();
        span.ShouldHaveTag(WellKnownTagKeys.ErrorCode, code);
        span.ShouldNotHaveTag(WellKnownTagKeys.ErrorType);
        span.Events.Should().NotContain(e => e.Name == "exception");
    }

    [Fact]
    public async Task ClientCancellation_LeavesNoErrorTagAndNoEvent()
    {
        using var recorder = ServerSpans.Record();
        await using var app = await GrpcTestHost.StartAsync();
        var probe = app.Services.GetRequiredService<CallProbe>();
        using var cancellation = new CancellationTokenSource();
        var headers = ServerSpans.NewTrace(out var traceId);

        using var call = app.CreateClient().WaitForCancellationAsync(
            new EchoRequest { Value = AfterCancellation.ServerError },
            headers,
            cancellationToken: cancellation.Token);
        await probe.Entered.Task.WaitAsync(Patience);
        await cancellation.CancelAsync();
        var act = async () => await call;
        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Cancelled);

        var span = await recorder.WaitForServerSpanAsync(traceId);
        AssertNothingRecorded(span);
    }

    [Fact]
    public async Task PassedDeadline_LeavesNoErrorTagAndNoEvent()
    {
        using var recorder = ServerSpans.Record();
        await using var app = await GrpcTestHost.StartAsync();
        var headers = ServerSpans.NewTrace(out var traceId);

        using var call = app.CreateClient().WaitForCancellationAsync(
            new EchoRequest { Value = AfterCancellation.Io },
            headers,
            deadline: DateTime.UtcNow.AddMilliseconds(300));
        var act = async () => await call;
        // Which status the client reports (its own DeadlineExceeded, the server's Cancelled, or a stream reset) races with
        // the deadline; what the server span carries does not.
        await act.Should().ThrowAsync<RpcException>();

        var span = await recorder.WaitForServerSpanAsync(traceId);
        AssertNothingRecorded(span);
    }

    [Fact]
    public async Task SuccessfulCall_TouchesNothing()
    {
        using var recorder = ServerSpans.Record();
        await using var app = await GrpcTestHost.StartAsync();
        var headers = ServerSpans.NewTrace(out var traceId);

        var reply = await app.CreateClient().EchoAsync(new EchoRequest { Value = "hi" }, headers);

        reply.Value.Should().Be("hi");
        (await recorder.WaitForServerSpanAsync(traceId)).ShouldBeRecordedSuccess();
    }

    private static void AssertNothingRecorded(Activity span)
    {
        span.ShouldBeUnset();
        span.ShouldNotHaveTag(WellKnownTagKeys.ErrorCode);
        span.ShouldNotHaveTag(WellKnownTagKeys.ErrorType);
        span.Events.Should().NotContain(e => e.Name == "exception");
    }

    private static async Task<(RpcException Exception, Activity Span)> FailAsync(
        Microsoft.AspNetCore.Builder.WebApplication app,
        SharedKernel.Testing.Telemetry.ActivityRecorder recorder,
        Shape shape,
        string failure)
    {
        var client = app.CreateClient();
        var headers = ServerSpans.NewTrace(out var traceId);
        var request = new EchoRequest { Value = failure };

        Func<Task> act = shape switch
        {
            Shape.Unary => async () => await client.FailAsync(request, headers),
            Shape.ServerStreaming => async () =>
            {
                using var call = client.StreamFail(request, headers);
                while (await call.ResponseStream.MoveNext(CancellationToken.None))
                {
                }
            },
            Shape.ClientStreaming => async () =>
            {
                using var call = client.ClientStreamFail(headers);
                await call.RequestStream.WriteAsync(request);
                await call.RequestStream.CompleteAsync();
                await call.ResponseAsync;
            },
            _ => async () =>
            {
                using var call = client.DuplexFail(headers);
                await call.RequestStream.WriteAsync(request);
                await call.RequestStream.CompleteAsync();
                while (await call.ResponseStream.MoveNext(CancellationToken.None))
                {
                }
            },
        };

        var exception = (await act.Should().ThrowAsync<RpcException>()).Which;
        return (exception, await recorder.WaitForServerSpanAsync(traceId));
    }
}
