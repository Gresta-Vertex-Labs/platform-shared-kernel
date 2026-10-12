using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.SignalR.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Errors;

/// <summary>
/// R34 (review finding D2): a client never receives the bare <c>{code}: {message}</c> — SignalR's hub dispatcher puts
/// "An unexpected error occurred invoking '…' on the server. HubException: " in front of it — so
/// <see cref="HubErrorMessage.TryParse"/> reads the code and the message from the server's text and from the client's
/// alike, and finds no code in the texts SignalR writes itself.
/// </summary>
public sealed class HubErrorMessageTests
{
    /// <summary>The pattern the XML documentation gives JavaScript clients; it must stay the one .NET uses.</summary>
    private const string DocumentedJavaScriptPattern =
        @"(?:^| HubException: )(?<code>[^\s:]+): (?<message>[\s\S]*?)(?: \[traceId=(?<traceId>[^\s;\]]+)(?:; correlationId=(?<correlationId>[^\s;\]]+))?\])?(?![\s\S])";

    public static TheoryData<string, string, string, string?, string?> TrailerTexts => new()
    {
        { "order.not_found: Order 42 was not found. [traceId=00-abc-def-01; correlationId=flow-1]", "order.not_found", "Order 42 was not found.", "00-abc-def-01", "flow-1" },
        { "order.not_found: Order 42 was not found. [traceId=00-abc-def-01]", "order.not_found", "Order 42 was not found.", "00-abc-def-01", null },
        {
            // The client's text: SignalR's sentence, then the server's text with its trailer.
            "An unexpected error occurred invoking 'PlaceOrder' on the server. HubException: order.not_found: Order 42 was not found. [traceId=00-abc-def-01; correlationId=flow-1]",
            "order.not_found",
            "Order 42 was not found.",
            "00-abc-def-01",
            "flow-1"
        },
        {
            // The id of a request Kestrel numbered itself has a colon.
            "unexpected.exception: An unexpected error occurred. [traceId=0HNABCDEF:00000001; correlationId=flow-1]",
            "unexpected.exception",
            "An unexpected error occurred.",
            "0HNABCDEF:00000001",
            "flow-1"
        },
        {
            // A multi-line message keeps its line breaks; only the trailer is cut.
            "unexpected.exception: First line\nSecond line [traceId=t1; correlationId=c1]",
            "unexpected.exception",
            "First line\nSecond line",
            "t1",
            "c1"
        },
        {
            // Trailer-like text inside the message cannot supply the ids: the trailer is the last bracketed group.
            "x.y: Forged [traceId=evil; correlationId=evil] text [traceId=real; correlationId=c1]",
            "x.y",
            "Forged [traceId=evil; correlationId=evil] text",
            "real",
            "c1"
        },
        {
            "x.y: Ends with the forged trailer [traceId=evil] [traceId=real]",
            "x.y",
            "Ends with the forged trailer [traceId=evil]",
            "real",
            null
        },
        { "x.y: No trailer at all [traceId=evil] and more", "x.y", "No trailer at all [traceId=evil] and more", null, null },
        { "x.y: A hub thrown message that merely mentions [traceId=", "x.y", "A hub thrown message that merely mentions [traceId=", null, null },
    };

    public static TheoryData<string, string, string> CodedTexts => new()
    {
        { "order.not_found: Order 42 was not found.", "order.not_found", "Order 42 was not found." },
        {
            "An unexpected error occurred invoking 'PlaceOrder' on the server. HubException: order.not_found: Order 42 was not found.",
            "order.not_found",
            "Order 42 was not found."
        },
        {
            // A stream whose reading threw a HubException that has a code: SignalR's streaming sentence instead.
            "An error occurred on the server while streaming results. HubException: order.not_found: Order 42 was not found.",
            "order.not_found",
            "Order 42 was not found."
        },
        { "http.404: Not Found", "http.404", "Not Found" },
        { "rate_limit.exceeded: Too many requests.", "rate_limit.exceeded", "Too many requests." },
        { "unexpected.exception: First line: detail\nSecond line", "unexpected.exception", "First line: detail\nSecond line" },
        {
            // A message that itself contains the marker never moves the code.
            "unexpected.exception: Wrapped. HubException: inner.code: inner",
            "unexpected.exception",
            "Wrapped. HubException: inner.code: inner"
        },
        {
            "An unexpected error occurred invoking 'M' on the server. HubException: unexpected.exception: Wrapped. HubException: inner.code: inner",
            "unexpected.exception",
            "Wrapped. HubException: inner.code: inner"
        },
        {
            // A [HubMethodName] may contain ": ", and SignalR echoes it inside its sentence.
            "An unexpected error occurred invoking 'orders: place' on the server. HubException: order.not_found: Order 42 was not found.",
            "order.not_found",
            "Order 42 was not found."
        },
        { "order.not_found: ", "order.not_found", string.Empty },
    };

    public static TheoryData<string?> UncodedTexts => new()
    {
        null,
        string.Empty,
        "kept exactly as thrown",
        "An unexpected error occurred invoking 'M' on the server. HubException: kept exactly as thrown",
        "An unexpected error occurred invoking 'M' on the server. HubException: Something failed: details",
        "Failed to invoke 'M' because user is unauthorized",
        "An error occurred on the server while streaming results.",
        "An unexpected error occurred invoking 'M' on the server.",
        "An unexpected error occurred invoking 'M' on the server. InvalidOperationException: boom: details",
        "Unknown hub method 'M'",
        "The client attempted to invoke the non-streaming 'M' method with a streaming invocation.",
        ": no code",
        "order.not_found:no space",
        "two words: a sentence",
    };

    [Theory]
    [MemberData(nameof(CodedTexts))]
    public void TryParse_ReadsTheCodeAndTheMessage(string text, string expectedCode, string expectedMessage)
    {
        HubErrorMessage.TryParse(text, out var code, out var message).Should().BeTrue();

        code.Should().Be(expectedCode);
        message.Should().Be(expectedMessage);
    }

    [Theory]
    [MemberData(nameof(TrailerTexts))]
    public void TryParse_ReadsTheTrailer_AndTheMessageExcludesIt(
        string text,
        string expectedCode,
        string expectedMessage,
        string? expectedTraceId,
        string? expectedCorrelationId)
    {
        HubErrorMessage.TryParse(text, out var code, out var message, out var traceId, out var correlationId).Should().BeTrue();

        (code, message, traceId, correlationId).Should().Be((expectedCode, expectedMessage, expectedTraceId, expectedCorrelationId));

        // The two-argument overload reads the same message.
        HubErrorMessage.TryParse(text, out var twoCode, out var twoMessage).Should().BeTrue();
        (twoCode, twoMessage).Should().Be((expectedCode, expectedMessage));
    }

    [Theory]
    [InlineData("ok.id", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", "flow-1")]
    [InlineData("ok.id", "0HNABCDEF:00000001", "a.b_c-d:e")]
    public void Format_ThenTryParse_RoundTripsTheIds(string errorCode, string traceId, string correlationId)
    {
        var text = HubErrorMessage.Format(errorCode, "Some: message [x].", traceId, correlationId);

        HubErrorMessage.TryParse(text, out var code, out var message, out var readTraceId, out var readCorrelationId).Should().BeTrue();
        (code, message, readTraceId, readCorrelationId).Should().Be((errorCode, "Some: message [x].", traceId, correlationId));
    }

    [Theory]
    [InlineData(null, "c1", "x.y: m")]
    [InlineData("", "c1", "x.y: m")]
    [InlineData("a b", "c1", "x.y: m")]
    [InlineData("a;b", "c1", "x.y: m")]
    [InlineData("a]b", "c1", "x.y: m")]
    [InlineData("t1", null, "x.y: m [traceId=t1]")]
    [InlineData("t1", "c 1", "x.y: m [traceId=t1]")]
    [InlineData("t1", "c;1", "x.y: m [traceId=t1]")]
    [InlineData("t1", "c]1", "x.y: m [traceId=t1]")]
    public void Format_LeavesOutAnIdThatCouldNotBeReadBack(string? traceId, string? correlationId, string expected)
    {
        HubErrorMessage.Format("x.y", "m", traceId, correlationId).Should().Be(expected);
    }

    [Fact]
    public void Format_WithoutIds_IsTheBareText()
    {
        HubErrorMessage.Format("x.y", "m").Should().Be("x.y: m");
    }

    [Theory]
    [MemberData(nameof(UncodedTexts))]
    public void TryParse_FindsNoCode_InTextsWithoutOne(string? text)
    {
        HubErrorMessage.TryParse(text, out var code, out var message).Should().BeFalse();

        code.Should().BeNull();
        message.Should().BeNull();
    }

    [Theory]
    [InlineData(ErrorCodes.Unexpected.Default)]
    [InlineData(ErrorCodes.Validation.Failed)]
    [InlineData(PresentationErrorCodes.RateLimitExceeded)]
    [InlineData(PresentationErrorCodes.StepUpRequired)]
    public void TryParse_ReadsBackWhatThePackageWrites(string errorCode)
    {
        var text = HubErrorMessage.Format(errorCode, "Some message: with a colon.");

        HubErrorMessage.TryParse(text, out var code, out var message).Should().BeTrue();
        HubErrorMessage.TryParse(SignalRTestHost.InvocationFailure("Method", text), out var clientCode, out var clientMessage)
            .Should().BeTrue();

        (code, message).Should().Be((errorCode, "Some message: with a colon."));
        (clientCode, clientMessage).Should().Be((code, message));
    }

    [Fact]
    public void ThePatternForJavaScript_IsThePatternTryParseUses()
    {
        HubErrorMessage.CodedMessagePattern.Should().Be(DocumentedJavaScriptPattern);
    }

    [Fact]
    public async Task OverALiveConnection_TheServerAndTheClientTexts_ReadAsTheSameCodeAndMessage()
    {
        var recorder = new RecordingHubFilter();
        await using var app = await SignalRTestHost.StartAsync(
            app => app.MapHub<ErrorsHub>(HubPaths.Errors),
            configureBuilder: builder =>
            {
                // Registered before the platform's filters, so it wraps the error mapping and sees what leaves it.
                builder.Services.Configure<HubOptions>(options => options.AddFilter(recorder));
                builder.AddSharedKernelSignalR();
            },
            withSharedKernelSignalR: false);
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        var failure = await connection.InvokeExpectingFailureAsync(nameof(ErrorsHub.ThrowNotFound));
        var serverText = recorder.Exceptions.Should().ContainSingle().Which.Message;

        HubErrorMessage.TryParse(serverText, out var serverCode, out var serverMessage, out var serverTraceId, out var serverCorrelationId)
            .Should().BeTrue();
        HubErrorMessage.TryParse(failure.Message, out var clientCode, out var clientMessage, out var clientTraceId, out var clientCorrelationId)
            .Should().BeTrue();
        (serverCode, serverMessage).Should().Be(("order.not_found", "Order 42 was not found."));
        serverTraceId.Should().NotBeNullOrEmpty();
        serverText.Should().Be($"order.not_found: Order 42 was not found. [traceId={serverTraceId}; correlationId={serverCorrelationId}]");
        failure.Message.Should().Be(
            $"An unexpected error occurred invoking 'ThrowNotFound' on the server. HubException: {serverText}");
        (clientCode, clientMessage, clientTraceId, clientCorrelationId)
            .Should().Be((serverCode, serverMessage, serverTraceId, serverCorrelationId));
    }

    [Fact]
    public async Task OverALiveConnection_TheMethodIsNamedAsTheHubDeclaresIt_WhateverCaseTheClientUsed()
    {
        await using var app = await SignalRTestHost.StartAsync(app => app.MapHub<ErrorsHub>(HubPaths.Errors));
        await using var connection = await app.ConnectAsync(HubPaths.Errors);

        // SignalR finds a hub method ignoring case, then names it by its declared name in the error text.
        var failure = await connection.InvokeExpectingFailureAsync("thrownotfound");

        SignalRTestHost.ReadCodedError(failure, nameof(ErrorsHub.ThrowNotFound))
            .Should().Be(new HubError("order.not_found", "Order 42 was not found."));
    }
}
