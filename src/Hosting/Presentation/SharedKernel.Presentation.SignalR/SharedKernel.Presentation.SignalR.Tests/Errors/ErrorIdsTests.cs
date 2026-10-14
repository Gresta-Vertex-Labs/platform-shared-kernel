using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR.Client;
using SharedKernel.Presentation.SignalR.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Errors;

/// <summary>
/// SK.14.EdgeErrorContext: every <c>{code}: {message}</c> this package builds carries the trace id and the correlation
/// id as a trailer, the ids an HTTP request to the same service reports, over a live connection. Errors SignalR writes
/// itself carry neither a code nor ids.
/// </summary>
public sealed class ErrorIdsTests
{
    private const string CorrelationId = "flow-signalr-42";

    private const string GenericUnexpected = "An unexpected error occurred.";

    private static readonly IReadOnlyDictionary<string, string> Headers =
        new Dictionary<string, string> { [WellKnownHeaders.CorrelationId] = CorrelationId };

    public static TheoryData<string, string, string> HubErrors => new()
    {
        { HubPaths.Errors, nameof(ErrorsHub.ThrowNotFound), "order.not_found" },
        { HubPaths.Errors, nameof(ErrorsHub.ThrowUnavailable), "search.unavailable" },
        { HubPaths.Errors, nameof(ErrorsHub.ThrowUnknown), ErrorCodes.Unexpected.Default },
        { HubPaths.Errors, nameof(ErrorsHub.ThrowTimeout), ErrorCodes.Timeout.Default },
        { HubPaths.Errors, nameof(ErrorsHub.ThrowInternalCancellation), ErrorCodes.Timeout.Default },
        { HubPaths.Errors, nameof(ErrorsHub.ThrowValidation), ErrorCodes.Validation.Failed },
        { HubPaths.Errors, nameof(ErrorsHub.ThrowSingleValidation), "name.required" },
        { HubPaths.Results, nameof(ResultsHub.Fail), "order.already_paid" },
        { HubPaths.Results, nameof(ResultsHub.NumberNotFound), "order.not_found" },
        { HubPaths.Results, nameof(ResultsHub.Outage), "search.unavailable" },
        { HubPaths.Results, nameof(ResultsHub.Uninitialized), ErrorCodes.Unexpected.Default },
    };

    [Theory]
    [MemberData(nameof(HubErrors))]
    public async Task EveryError_CarriesTheTraceId_AndTheConnectionsCorrelationId(string path, string method, string expectedCode)
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(path, Headers);

        var failure = await connection.InvokeExpectingFailureAsync(method);

        var error = SignalRTestHost.ReadCodedErrorWithIds(failure, method);

        error.Code.Should().Be(expectedCode);
        error.TraceId.Should().NotBeNullOrWhiteSpace();
        error.CorrelationId.Should().Be(CorrelationId);

        // The two-argument overload reads the message without the trailer.
        HubErrorMessage.TryParse(failure.Message, out _, out var message).Should().BeTrue();
        message.Should().Be(error.Message).And.NotContain("traceId=");
    }

    [Fact]
    public async Task StreamingMethodFailingBeforeItReturnsTheStream_CarriesTheIds()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Streams, Headers);

        var (_, failure) = await connection.ReadStreamAsync<int>(nameof(StreamsHub.Stream), true);

        var error = SignalRTestHost.ReadCodedErrorWithIds(failure!, nameof(StreamsHub.Stream));
        error.Code.Should().Be("order.not_found");
        error.TraceId.Should().NotBeNullOrWhiteSpace();
        error.CorrelationId.Should().Be(CorrelationId);
    }

    [Fact]
    public async Task InvocationRateLimitRefusal_CarriesTheIds()
    {
        await using var app = await SignalRTestHost.StartAsync(
            app => app.MapHub<RateLimitedHub>(HubPaths.RateLimited),
            configuration: new Dictionary<string, string?> { ["SharedKernel:Presentation:SignalR:InvocationRateLimit:PermitLimit"] = "1" });
        await using var connection = await app.ConnectAsync(HubPaths.RateLimited, Headers);
        await connection.InvokeAsync<string>(nameof(RateLimitedHub.Ping));

        var failure = await connection.InvokeExpectingFailureAsync(nameof(RateLimitedHub.Ping));
        var error = SignalRTestHost.ReadCodedErrorWithIds(failure, nameof(RateLimitedHub.Ping));

        error.Code.Should().Be(PresentationErrorCodes.RateLimitExceeded);
        error.Message.Should().Be("Too many requests.");
        error.TraceId.Should().NotBeNullOrWhiteSpace();
        error.CorrelationId.Should().Be(CorrelationId);
    }

    [Fact]
    public async Task UnclassifiedException_OutsideDevelopment_IsRedacted_ButStillCarriesTheIds()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Errors, Headers);

        var failure = await connection.InvokeExpectingFailureAsync(nameof(ErrorsHub.ThrowUnknown));
        var error = SignalRTestHost.ReadCodedErrorWithIds(failure, nameof(ErrorsHub.ThrowUnknown));

        error.Message.Should().Be(GenericUnexpected);
        failure.Message.Should().NotContain("db.internal").And.NotContain("secret");
        error.CorrelationId.Should().Be(CorrelationId);
    }

    [Fact]
    public async Task UnclassifiedException_InDevelopment_ShowsItsMessage_AndTheIdsStayAfterIt()
    {
        await using var app = await StartAsync(SignalRTestHost.Development);
        await using var connection = await app.ConnectAsync(HubPaths.Errors, Headers);

        var failure = await connection.InvokeExpectingFailureAsync(nameof(ErrorsHub.ThrowUnknown));
        var error = SignalRTestHost.ReadCodedErrorWithIds(failure, nameof(ErrorsHub.ThrowUnknown));

        error.Message.Should().Be(HubMessages.Secret);
        error.CorrelationId.Should().Be(CorrelationId);
    }

    [Fact]
    public async Task AMessageImitatingTheTrailer_CannotForgeTheIds()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Errors, Headers);

        var failure = await connection.InvokeExpectingFailureAsync(nameof(ErrorsHub.ThrowForgedTrailer));
        var error = SignalRTestHost.ReadCodedErrorWithIds(failure, nameof(ErrorsHub.ThrowForgedTrailer));

        error.Code.Should().Be("order.forged");
        error.Message.Should().Be("Forged [traceId=evil; correlationId=evil] text [traceId=evil]");
        error.TraceId.Should().NotBe("evil").And.NotBeNullOrWhiteSpace();
        error.CorrelationId.Should().Be(CorrelationId);
    }

    [Fact]
    public async Task AHubExceptionThrownByTheHub_HasNoTrailer()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Errors, Headers);

        var failure = await connection.InvokeExpectingFailureAsync(nameof(ErrorsHub.ThrowHubException));

        failure.Message.Should().Be(SignalRTestHost.InvocationFailure(nameof(ErrorsHub.ThrowHubException), "kept exactly as thrown"));
        HubErrorMessage.TryParse(failure.Message, out _, out _, out var traceId, out var correlationId).Should().BeFalse();
        (traceId, correlationId).Should().Be((null, null));
    }

    [Fact]
    public async Task ErrorsSignalRWritesItself_HaveNoCodeAndNoIds()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Errors, Headers);

        var unknownMethod = await connection.InvokeExpectingFailureAsync("NoSuchHubMethod");

        HubErrorMessage.TryParse(unknownMethod.Message, out _, out _, out var traceId, out var correlationId).Should().BeFalse();
        (traceId, correlationId).Should().Be((null, null));
        unknownMethod.Message.Should().NotContain("traceId=");
    }

    [Fact]
    public async Task SignalRsOwnAuthorizationRefusal_HasNoCodeAndNoIds()
    {
        await using var app = await SignalRTestHost.StartAsync(app => app.MapHub<MethodAuthorizationHub>(HubPaths.MethodAuthorization));
        await using var connection = await app.ConnectAsync(HubPaths.MethodAuthorization, Headers);

        var message = await connection.InvokeExpectingRefusalAsync(nameof(MethodAuthorizationHub.ReadOrders));

        HubErrorMessage.TryParse(message, out _, out _, out var traceId, out var correlationId).Should().BeFalse();
        (traceId, correlationId).Should().Be((null, null));
    }

    [Fact]
    public async Task WithoutTheRequestContext_TheTrailerCarriesTheTraceIdOnly()
    {
        await using var app = await SignalRTestHost.StartAsync(
            app => app.MapHub<ErrorsHub>(HubPaths.Errors),
            withRequestContext: false);
        await using var connection = await app.ConnectAsync(HubPaths.Errors, Headers);

        var failure = await connection.InvokeExpectingFailureAsync(nameof(ErrorsHub.ThrowNotFound));
        var error = SignalRTestHost.ReadCodedErrorWithIds(failure, nameof(ErrorsHub.ThrowNotFound));

        error.TraceId.Should().NotBeNullOrWhiteSpace();
        error.CorrelationId.Should().BeNull("no request context was composed, so no correlation id exists");
    }

    private static Task<WebApplication> StartAsync(string environment = SignalRTestHost.Production) =>
        SignalRTestHost.StartAsync(
            app =>
            {
                app.MapHub<ErrorsHub>(HubPaths.Errors);
                app.MapHub<ResultsHub>(HubPaths.Results);
                app.MapHub<StreamsHub>(HubPaths.Streams);
            },
            environment: environment);
}
