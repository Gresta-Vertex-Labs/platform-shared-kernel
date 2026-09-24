using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR.Client;
using SharedKernel.Presentation.SignalR.Filters;
using SharedKernel.Presentation.SignalR.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.ResultMapping;

/// <summary>
/// Design D12: a hub method may return <see cref="Result"/> or <see cref="Result{T}"/>. A failure reaches the client
/// as the code-prefixed HubException, a success as the value itself — for value types too, which the covariant
/// <see cref="IResultOfT{T}"/> cannot read.
/// </summary>
public sealed class ResultHubMethodTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HubConnection _connection = null!;

    public async Task InitializeAsync()
    {
        _app = await SignalRTestHost.StartAsync(app => app.MapHub<ResultsHub>(HubPaths.Results));
        _connection = await _app.ConnectAsync(HubPaths.Results);
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task SuccessfulResult_ReturnsNothing()
    {
        var returned = await _connection.InvokeAsync<object?>(nameof(ResultsHub.Succeed));

        returned.Should().BeNull();
    }

    [Fact]
    public async Task FailedResult_ReachesTheClient_AsItsCodeAndMessage()
    {
        var error = await _connection.InvokeExpectingErrorAsync(nameof(ResultsHub.Fail));

        error.Should().Be(new HubError("order.already_paid", "The order is already paid."));
    }

    [Fact]
    public async Task SuccessfulResultOfValueType_ReturnsTheValue()
    {
        var number = await _connection.InvokeAsync<int>(nameof(ResultsHub.Number));

        number.Should().Be(42);
    }

    [Fact]
    public async Task SuccessfulResultOfReferenceType_ReturnsTheValue()
    {
        var text = await _connection.InvokeAsync<string>(nameof(ResultsHub.Text));
        var order = await _connection.InvokeAsync<OrderDto>(nameof(ResultsHub.Order));

        text.Should().Be("hello");
        order.Should().Be(new OrderDto(7, "open"));
    }

    [Fact]
    public async Task AwaitedResults_ReturnTheirValues()
    {
        var order = await _connection.InvokeAsync<OrderDto>(nameof(ResultsHub.OrderAsync));
        var id = await _connection.InvokeAsync<Guid>(nameof(ResultsHub.IdAsync));

        order.Should().Be(new OrderDto(8, "shipped"));
        id.Should().Be(Guid.Parse("11111111-2222-3333-4444-555555555555"));
    }

    [Fact]
    public async Task FailedResultOfValueType_ReachesTheClient_AsItsCodeAndMessage()
    {
        var error = await _connection.InvokeExpectingErrorAsync(nameof(ResultsHub.NumberNotFound));

        error.Should().Be(new HubError("order.not_found", "Order 42 was not found."));
    }

    [Fact]
    public async Task FailedResultOfServerErrorType_IsRedactedOutsideDevelopment()
    {
        var error = await _connection.InvokeExpectingErrorAsync(nameof(ResultsHub.Outage));

        error.Should().Be(new HubError("search.unreachable", "The service is temporarily unavailable. Try again later."));
    }

    [Fact]
    public async Task UninitializedResult_IsAnUnexpectedError()
    {
        var error = await _connection.InvokeExpectingErrorAsync(nameof(ResultsHub.Uninitialized));

        error.Should().Be(new HubError(ErrorCodes.Unexpected.Default, "An unexpected error occurred."));
    }

    [Fact]
    public void Reader_LeavesAnythingButAResultAlone()
    {
        HubMethodResult.TryRead("plain value", out var value, out var error).Should().BeFalse();
        HubMethodResult.TryRead(null, out value, out error).Should().BeFalse();

        value.Should().BeNull();
        error.Should().BeNull();
    }

    [Fact]
    public void Reader_ReadsValueTypeResults_ForManyClosedTypes()
    {
        HubMethodResult.TryRead(Result<int>.Success(1), out var number, out _).Should().BeTrue();
        HubMethodResult.TryRead(Result<decimal>.Success(2.5m), out var amount, out _).Should().BeTrue();
        HubMethodResult.TryRead(Result<DateTimeOffset>.Failure(Error.Timeout("clock.timeout", "Timed out.")), out _, out var error).Should().BeTrue();

        number.Should().Be(1);
        amount.Should().Be(2.5m);
        error!.Code.Should().Be("clock.timeout");
    }

    [Fact]
    public void StreamTest_MatchesWhatSignalRStreams_AsyncEnumerablesAndChannelReaders()
    {
        HubMethodResult.IsStream(StreamsHub.Numbers()).Should().BeTrue("an async iterator implements IAsyncEnumerable<T>");
        HubMethodResult.IsStream(System.Threading.Channels.Channel.CreateUnbounded<int>().Reader)
            .Should().BeTrue("a channel's reader derives from ChannelReader<T>");

        HubMethodResult.IsStream(new List<int> { 1 }).Should().BeFalse("SignalR sends a list as one value");
        HubMethodResult.IsStream("text").Should().BeFalse();
        HubMethodResult.IsStream(null).Should().BeFalse();
    }
}
