using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.SignalR.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Telemetry;

/// <summary>
/// SK.14.PresentationConformance D-03: the hub filter records what it mapped on the framework's hub-invocation span
/// (source <c>Microsoft.AspNetCore.SignalR.Server</c>, kind Server, started before the filter runs), once, through the
/// shared recorder. A server-category failure carries the code and one exception event (none for a returned error or a
/// timeout); a client-category failure only the code; a connection that went away records nothing of ours; without a
/// listener there is no span and nothing breaks.
/// </summary>
public sealed class HubInvocationSpanTests
{
    private const string SourceName = "Microsoft.AspNetCore.SignalR.Server";

    [Fact]
    public async Task AServerCategoryException_RecordsItsCodeAndOneExceptionEvent()
    {
        using var recorder = ActivityRecorder.StartRecording(SourceName);

        var span = await InvokeAsync(recorder, nameof(SpanHub.Unavailable));

        span.ShouldHaveKind(ActivityKind.Server);
        span.ShouldHaveTag(WellKnownTagKeys.ErrorCode, "search.unavailable");
        span.Events.Count(e => e.Name == "exception").Should().Be(1);
        span.ShouldHaveStatus(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task AnUnclassifiedException_RecordsTheUnexpectedDefaultCode_AndOneExceptionEvent_NeverTheMessage()
    {
        using var recorder = ActivityRecorder.StartRecording(SourceName);

        var span = await InvokeAsync(recorder, nameof(SpanHub.Unknown));

        span.ShouldHaveTag(WellKnownTagKeys.ErrorCode, ErrorCodes.Unexpected.Default);
        span.Events.Count(e => e.Name == "exception").Should().Be(1);
        span.ShouldNotLeak("Password=hunter2");
    }

    [Fact]
    public async Task AReturnedServerError_RecordsItsCode_WithoutAnExceptionEvent()
    {
        using var recorder = ActivityRecorder.StartRecording(SourceName);

        var span = await InvokeAsync(recorder, nameof(SpanHub.Outage));

        span.ShouldHaveTag(WellKnownTagKeys.ErrorCode, "search.unavailable");
        span.Events.Count(e => e.Name == "exception").Should().Be(0);
    }

    [Fact]
    public async Task ATimeout_IsARecordedFailure_NotAnException()
    {
        using var recorder = ActivityRecorder.StartRecording(SourceName);

        var span = await InvokeAsync(recorder, nameof(SpanHub.Timeout));

        span.ShouldHaveTag(WellKnownTagKeys.ErrorCode, ErrorCodes.Timeout.Default);
        span.Events.Count(e => e.Name == "exception").Should().Be(0);
    }

    [Fact]
    public async Task AClientCategoryFailure_OnlyTagsTheCode()
    {
        using var recorder = ActivityRecorder.StartRecording(SourceName);

        var span = await InvokeAsync(recorder, nameof(SpanHub.NotFound));

        span.ShouldHaveTag(WellKnownTagKeys.ErrorCode, "order.not_found");
        span.Events.Count(e => e.Name == "exception").Should().Be(0);
    }

    [Fact]
    public async Task AValidationException_OnlyTagsTheCode_WhateverTheTypeOfItsError()
    {
        using var recorder = ActivityRecorder.StartRecording(SourceName);

        var span = await InvokeAsync(recorder, nameof(SpanHub.Validation));

        span.ShouldHaveTag(WellKnownTagKeys.ErrorCode, "name.required");
        span.Events.Count(e => e.Name == "exception").Should().Be(0);
    }

[Fact]    public async Task AConnectionThatWentAway_RecordsNothingOfOurs()    {        using var recorder = ActivityRecorder.StartRecording(SourceName);        await using var app = await StartAsync();        var connection = await app.ConnectAsync("/hubs/span");        _ = connection.InvokeAsync<string>(nameof(SpanHub.WaitForever));        await SignalRTestHost.WaitUntilAsync(() => recorder.StartedActivities.Any(span => span.DisplayName.EndsWith($"{nameof(SpanHub)}/{nameof(SpanHub.WaitForever)}", StringComparison.Ordinal)));        await connection.DisposeAsync();        await SignalRTestHost.WaitUntilAsync(() => recorder.RecordedActivities.Any(span => span.DisplayName.EndsWith($"{nameof(SpanHub)}/{nameof(SpanHub.WaitForever)}", StringComparison.Ordinal)));        var span = recorder.RecordedActivities.First(span => span.DisplayName.EndsWith($"{nameof(SpanHub)}/{nameof(SpanHub.WaitForever)}", StringComparison.Ordinal));        span.ShouldNotHaveTag(WellKnownTagKeys.ErrorCode);        span.Events.Count(e => e.Name == "exception").Should().Be(0);    }
    [Fact]
    public async Task WithoutAListener_ThereIsNoSpan_AndTheClientStillGetsTheCodedError()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync("/hubs/span");

        var failure = await connection.InvokeExpectingFailureAsync(nameof(SpanHub.Unavailable));

        SignalRTestHost.ReadCodedErrorWithIds(failure, nameof(SpanHub.Unavailable)).Code.Should().Be("search.unavailable");
    }

    private static async Task<Activity> InvokeAsync(ActivityRecorder recorder, string method)
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync("/hubs/span");

        await connection.InvokeExpectingFailureAsync(method);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            if (recorder.RecordedActivities.FirstOrDefault(span =>
                    span.DisplayName.EndsWith($"{nameof(SpanHub)}/{method}", StringComparison.Ordinal)) is { } span)
            {
                return span;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"The hub invocation span of '{method}' was never recorded.");
    }

    private static Task<WebApplication> StartAsync() =>
        SignalRTestHost.StartAsync(endpoints => endpoints.MapHub<SpanHub>("/hubs/span"));

    /// <summary>A hub of this test class only, so the spans it asserts on belong to no parallel test.</summary>
    public sealed class SpanHub : Hub
    {
        public async Task<string> WaitForever()
        {
            await Task.Delay(System.Threading.Timeout.Infinite, Context.ConnectionAborted);
            return "unreachable";
        }

        public string Unavailable() => throw Error.Unavailable("search.unavailable", "Host db-7.internal is down.").ToException();

        public string Unknown() => throw new InvalidOperationException("Password=hunter2");

        public Result Outage() => Error.Unavailable("search.unavailable", "Host db-7.internal is down.");

        public string Timeout() => throw new TimeoutException("Password=hunter2");

        public string NotFound() => throw new NotFoundException(Error.NotFound("order.not_found", "Order 42 was not found."));

        public string Validation() => throw new ValidationException(Error.Validation("name.required", "Name is required."));
    }
}
