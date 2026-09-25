using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.SignalR.Filters;
using SharedKernel.Presentation.SignalR.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.ResultMapping;

/// <summary>
/// R35 (review finding C14): SignalR streams only a hub method declared to return <c>IAsyncEnumerable&lt;T&gt;</c> or
/// <c>ChannelReader&lt;T&gt;</c>, so a method returning a stream inside a <c>Result</c> is an ordinary invocation.
/// Its failure is the coded HubException; its success, which used to close the connection (JSON protocol) or send the
/// channel reader as a meaningless object, is refused with a coded error and the connection stays open. The streaming
/// pattern — a stream type, with the failure thrown before the stream is returned — gets the coded HubException or
/// the items, and an error after the stream started stays uncoded.
/// </summary>
public sealed class ResultStreamTests
{
    private static readonly string MappingCategory = typeof(HubExceptionMappingFilter).FullName!;

    [Theory]
    [InlineData(nameof(StreamsHub.ResultOfStream))]
    [InlineData(nameof(StreamsHub.ResultOfStreamAsync))]
    [InlineData(nameof(StreamsHub.ResultOfChannel))]
    public async Task ResultOfStream_Failure_IsTheCodedHubException(string method)
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Streams);

        var error = await connection.InvokeExpectingErrorAsync(method, true);

        error.Should().Be(new HubError("order.not_found", "Order 42 was not found."));
        (await connection.InvokeAsync<string>(nameof(StreamsHub.Ping))).Should().Be("pong");
    }

    [Theory]
    [InlineData(nameof(StreamsHub.ResultOfStream))]
    [InlineData(nameof(StreamsHub.ResultOfStreamAsync))]
    [InlineData(nameof(StreamsHub.ResultOfChannel))]
    public async Task ResultOfStream_Success_IsACodedUnexpectedError_AndTheConnectionStaysOpen(string method)
    {
        var loggerFactory = new InMemoryLoggerFactory();
        await using var app = await StartAsync(loggerFactory: loggerFactory);
        await using var connection = await app.ConnectAsync(HubPaths.Streams);

        var error = await connection.InvokeExpectingErrorAsync(method, false);

        error.Should().Be(new HubError(ErrorCodes.Unexpected.Default, "An unexpected error occurred."));
        connection.State.Should().Be(HubConnectionState.Connected);
        (await connection.InvokeAsync<string>(nameof(StreamsHub.Ping))).Should().Be("pong");
        loggerFactory.GetLogger(MappingCategory).Records.ShouldHaveLogged(new EventId(14107), LogLevel.Error);
        app.Services.GetRequiredService<InvocationCounter>().Count.Should().Be(0, "a refused stream is never read");
    }

    [Fact]
    public async Task ResultOfStream_Success_InDevelopment_SaysHowToStream()
    {
        await using var app = await StartAsync(SignalRTestHost.Development);
        await using var connection = await app.ConnectAsync(HubPaths.Streams);

        var error = await connection.InvokeExpectingErrorAsync(nameof(StreamsHub.ResultOfStream), false);

        error.Code.Should().Be(ErrorCodes.Unexpected.Default);
        error.Message.Should().Contain($"'{nameof(StreamsHub.ResultOfStream)}'")
            .And.Contain("IAsyncEnumerable<T> or ChannelReader<T>")
            .And.Contain("GetValueOrThrow()");
    }

    [Theory]
    [InlineData(nameof(StreamsHub.ResultOfStream))]
    [InlineData(nameof(StreamsHub.ResultOfChannel))]
    public async Task ResultOfStream_IsNoStreamingMethod_ForSignalR(string method)
    {
        // Why the platform cannot stream it: SignalR classifies a hub method by its declared return type and refuses a
        // streaming call to any other method before a hub filter runs.
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Streams);

        var (items, failure) = await connection.ReadStreamAsync<int>(method, false);

        items.Should().BeEmpty();
        failure!.Message.Should().Be($"The client attempted to invoke the non-streaming '{method}' method with a streaming invocation.");
    }

    [Theory]
    [InlineData(nameof(StreamsHub.Stream))]
    [InlineData(nameof(StreamsHub.ChannelStream))]
    public async Task StreamingMethod_FailingBeforeItReturnsTheStream_IsTheCodedHubException(string method)
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Streams);

        var (items, failure) = await connection.ReadStreamAsync<int>(method, true);

        items.Should().BeEmpty();
        failure.Should().NotBeNull();
        SignalRTestHost.ReadCodedError(failure!, method).Should().Be(new HubError("order.not_found", "Order 42 was not found."));
    }

    [Theory]
    [InlineData(nameof(StreamsHub.Stream))]
    [InlineData(nameof(StreamsHub.ChannelStream))]
    public async Task StreamingMethod_Success_StreamsTheItems(string method)
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Streams);

        var (items, failure) = await connection.ReadStreamAsync<int>(method, false);

        failure.Should().BeNull();
        items.Should().Equal(StreamsHub.Items);
    }

    [Fact]
    public async Task StreamingMethod_ErrorAfterTheStreamStarted_ReachesTheClientWithoutACode()
    {
        var loggerFactory = new InMemoryLoggerFactory();
        await using var app = await StartAsync(loggerFactory: loggerFactory);
        await using var connection = await app.ConnectAsync(HubPaths.Streams);

        var (items, failure) = await connection.ReadStreamAsync<int>(nameof(StreamsHub.StreamThenFail));

        items.Should().Equal(StreamsHub.Items[0]);
        failure!.Message.Should().Be(SignalRTestHost.StreamFailure);
        HubErrorMessage.TryParse(failure.Message, out _, out _).Should().BeFalse();
        loggerFactory.GetLogger(MappingCategory).Records
            .Should().BeEmpty("the error mapping wraps the hub method, which returned its stream successfully");
        (await connection.InvokeAsync<string>(nameof(StreamsHub.Ping))).Should().Be("pong");
    }

    private static Task<WebApplication> StartAsync(
        string environment = SignalRTestHost.Production,
        InMemoryLoggerFactory? loggerFactory = null) =>
        SignalRTestHost.StartAsync(
            app => app.MapHub<StreamsHub>(HubPaths.Streams),
            environment: environment,
            loggerFactory: loggerFactory);
}
