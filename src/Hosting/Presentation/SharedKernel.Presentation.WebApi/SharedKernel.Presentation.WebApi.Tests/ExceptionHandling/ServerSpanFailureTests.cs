using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.ExceptionHandling;

/// <summary>
/// SK.14.EdgeErrorContext: the platform's exception handler records what it handled on the framework's server span
/// (<c>HttpRequestIn</c>) exactly once. A server-category failure fails the span (status <c>Error</c>, <c>error.type</c>,
/// <c>error.code</c>, one exception event; a timeout is a recorded failure without an exception event); a
/// client-category failure only tags <c>error.code</c>; a client that went away leaves nothing.
/// </summary>
public sealed class ServerSpanFailureTests
{
    private const string Canary = "Server=db.internal;Password=hunter2";

    private static readonly Error UnexpectedDomain = Error.Unexpected("order.broken", "The order is broken.");

    [Fact]
    public async Task UnclassifiedException_FailsTheServerSpan_WithTheTypeNameNeverTheMessage()
    {
        using var recorder = ServerSpans.Record();
        await using var app = await StartAsync();

        using var call = await app.GetAsync(recorder, "/boom");

        await call.Response.ShouldBeProblemAsync(StatusCodes.Status500InternalServerError, ErrorCodes.Unexpected.Default);
        call.Span.ShouldBeRecordedException(typeof(InvalidOperationException), ErrorCodes.Unexpected.Default);
        call.Span.Events.Count(e => e.Name == "exception").Should().Be(1);
        call.Span.StatusDescription.Should().Be(typeof(InvalidOperationException).FullName).And.NotContain("hunter2");
        call.Span.ShouldHaveTag(WellKnownTagKeys.ErrorType, typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public async Task UnavailableException_FailsTheServerSpan_WithItsCode()
    {
        using var recorder = ServerSpans.Record();
        await using var app = await StartAsync();
        var exception = TestErrors.SearchUnreachable.ToException();

        using var call = await app.GetAsync(recorder, "/unavailable");

        await call.Response.ShouldBeProblemAsync(StatusCodes.Status503ServiceUnavailable, "search.unreachable");
        call.Span.ShouldBeRecordedException(exception.GetType(), "search.unreachable");
        call.Span.Events.Count(e => e.Name == "exception").Should().Be(1);
    }

    [Fact]
    public async Task UnexpectedSharedKernelException_FailsTheServerSpan_WithItsCode()
    {
        using var recorder = ServerSpans.Record();
        await using var app = await StartAsync();
        var exception = UnexpectedDomain.ToException();

        using var call = await app.GetAsync(recorder, "/unexpected-domain");

        await call.Response.ShouldBeProblemAsync(StatusCodes.Status500InternalServerError, "order.broken");
        call.Span.ShouldBeRecordedException(exception.GetType(), "order.broken");
        call.Span.Events.Count(e => e.Name == "exception").Should().Be(1);
    }

    [Theory]
    [InlineData("/timeout")]
    [InlineData("/internal-cancellation")]
    public async Task TimeoutInsideTheService_IsARecordedFailure_NotAnException(string path)
    {
        using var recorder = ServerSpans.Record();
        await using var app = await StartAsync();

        using var call = await app.GetAsync(recorder, path);

        await call.Response.ShouldBeProblemAsync(StatusCodes.Status504GatewayTimeout, ErrorCodes.Timeout.Default);
        call.Span.ShouldBeRecordedFailure(Error.Timeout(ErrorCodes.Timeout.Default, "The operation did not complete in time."));
        call.Span.Events.Should().NotContain(e => e.Name == "exception");
    }

    [Theory]
    [InlineData("/validation-one", StatusCodes.Status400BadRequest, "name.required")]
    [InlineData("/validation-many", StatusCodes.Status400BadRequest, "validation.failed")]
    [InlineData("/not-found", StatusCodes.Status404NotFound, "order.not_found")]
    [InlineData("/conflict", StatusCodes.Status409Conflict, "order.version_conflict")]
    [InlineData("/forbidden", StatusCodes.Status403Forbidden, "order.forbidden")]
    public async Task ClientCategoryException_OnlyTagsTheErrorCode_AndLeavesTheStatusUnset(string path, int status, string code)
    {
        using var recorder = ServerSpans.Record();
        await using var app = await StartAsync();

        using var call = await app.GetAsync(recorder, path);

        await call.Response.ShouldBeProblemAsync(status, code);
        AssertTaggedOnly(call.Span, code);
    }

    [Fact]
    public async Task UnbindableParameter_IsAClientError_ThatOnlyTagsItsCode()
    {
        using var recorder = ServerSpans.Record();
        await using var app = await StartAsync();

        using var call = await app.GetAsync(recorder, "/bind?id=not-a-number");

        await call.Response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, ErrorCodes.Validation.InvalidFormat);
        AssertTaggedOnly(call.Span, ErrorCodes.Validation.InvalidFormat);
    }

    [Fact]
    public async Task SuccessfulRequest_TouchesNothing()
    {
        using var recorder = ServerSpans.Record();
        await using var app = await StartAsync();

        using var call = await app.GetAsync(recorder, "/ok");

        call.Response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        call.Span.ShouldBeRecordedSuccess();
    }

    [Fact]
    public async Task ClientAbort_Is499_AndLeavesNoErrorTagAndNoEvent()
    {
        // The handler is driven directly: a TestServer client cannot be made to disappear in the middle of a request.
        using var recorder = ActivityRecorder.StartRecording("Test.EdgeErrorContext." + nameof(ClientAbort_Is499_AndLeavesNoErrorTagAndNoEvent));
        using var source = new ActivitySource("Test.EdgeErrorContext." + nameof(ClientAbort_Is499_AndLeavesNoErrorTagAndNoEvent));
        var handler = HandlerFactory.Create();
        using var aborted = new CancellationTokenSource();
        var context = HandlerFactory.CreateContext();
        context.RequestAborted = aborted.Token;
        await aborted.CancelAsync();

        using (var activity = source.StartActivity("request"))
        {
            await handler.HandleAsync(context, new InvalidOperationException(Canary));
        }

        context.Response.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);
        var span = recorder.RecordedActivities.Should().ContainSingle().Subject;
        span.ShouldBeRecordedCancellation();
    }

    [Fact]
    public async Task AbortedOperationCanceledException_LeavesNoErrorTagAndNoEvent()
    {
        using var recorder = ActivityRecorder.StartRecording("Test.EdgeErrorContext." + nameof(AbortedOperationCanceledException_LeavesNoErrorTagAndNoEvent));
        using var source = new ActivitySource("Test.EdgeErrorContext." + nameof(AbortedOperationCanceledException_LeavesNoErrorTagAndNoEvent));
        var handler = HandlerFactory.Create();
        using var aborted = new CancellationTokenSource();
        var context = HandlerFactory.CreateContext();
        context.RequestAborted = aborted.Token;
        await aborted.CancelAsync();

        using (var activity = source.StartActivity("request"))
        {
            await handler.HandleAsync(context, new OperationCanceledException());
        }

        recorder.RecordedActivities.Should().ContainSingle().Subject.ShouldBeRecordedCancellation();
    }

    [Fact]
    public async Task TheStatusDescription_NeverCarriesTheMessage_EvenForAServerCategorySharedKernelException()
    {
        using var recorder = ServerSpans.Record();
        await using var app = await StartAsync();

        using var call = await app.GetAsync(recorder, "/leaky-domain");

        call.Span.StatusDescription.Should().NotContain("hunter2").And.NotContain("db.internal");
        call.Span.ShouldNotLeak("hunter2");
    }

    private static void AssertTaggedOnly(Activity span, string code)
    {
        span.ShouldBeUnset();
        span.ShouldHaveTag(WellKnownTagKeys.ErrorCode, code);
        span.ShouldNotHaveTag(WellKnownTagKeys.ErrorType);
        span.Events.Should().NotContain(e => e.Name == "exception");
    }

    private static Task<WebApplication> StartAsync() =>
        WebApiTestHost.StartAsync(app =>
        {
            app.MapGet("/ok", () => "fine");
            app.MapGet("/boom", IResult () => throw new InvalidOperationException(Canary));
            app.MapGet("/unavailable", IResult () => throw TestErrors.SearchUnreachable.ToException());
            app.MapGet("/unexpected-domain", IResult () => throw UnexpectedDomain.ToException());
            app.MapGet("/leaky-domain", IResult () => throw Error.Unexpected("order.leaky", Canary).ToException());
            app.MapGet("/timeout", IResult () => throw new TimeoutException(Canary));
            app.MapGet("/internal-cancellation", IResult () => throw new TaskCanceledException(Canary));
            app.MapGet("/validation-one", IResult () => throw new ValidationException(Error.Validation("name.required", "Name is required.")));
            app.MapGet("/validation-many", IResult () => throw new ValidationException(
            [
                Error.Validation("name.required", "Name is required."),
                Error.Validation("email.invalid", "Email is invalid."),
            ]));
            app.MapGet("/not-found", IResult () => throw new NotFoundException(TestErrors.OrderNotFound));
            app.MapGet("/conflict", IResult () => throw TestErrors.VersionConflict.ToException());
            app.MapGet("/forbidden", IResult () => throw Error.Forbidden("order.forbidden", "No.").ToException());
            app.MapGet("/bind", (int id) => id);
        });
}
