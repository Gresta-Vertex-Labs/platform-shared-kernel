using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>Test message used by <see cref="FakeRedisChannelServiceTests"/>.</summary>
internal sealed record FakeChannelTestMessage(string Name, int Value);

[JsonSerializable(typeof(FakeChannelTestMessage))]
internal sealed partial class FakeChannelTestJsonContext : JsonSerializerContext;

/// <summary>Proves <see cref="FakeRedisChannelService"/> against <c>IRedisChannelService</c>'s contract.</summary>
public sealed class FakeRedisChannelServiceTests
{
    private static JsonTypeInfo<FakeChannelTestMessage> MessageTypeInfo => FakeChannelTestJsonContext.Default.FakeChannelTestMessage;

    [Fact]
    public async Task PublishAsync_ReachesSubscribedHandler_BeforeReturning()
    {
        var service = new FakeRedisChannelService();
        var received = new List<string>();
        await service.SubscribeAsync("channel-1", (msg, _) => Record(received, msg));

        var receivers = await service.PublishAsync("channel-1", "hello");

        Assert.Equal(1, receivers);
        Assert.Equal(["hello"], received);
    }

    [Fact]
    public async Task PublishAsync_ReturnsNumberOfSubscriptions()
    {
        var service = new FakeRedisChannelService();
        await service.SubscribeAsync("channel-1", (_, _) => ValueTask.CompletedTask);
        await service.SubscribeAsync("channel-1", (_, _) => ValueTask.CompletedTask);
        await service.SubscribeAsync("channel-2", (_, _) => ValueTask.CompletedTask);

        Assert.Equal(2, await service.PublishAsync("channel-1", "hello"));
        Assert.Equal(0, await service.PublishAsync("no-subscribers", "hello"));
    }

    [Fact]
    public async Task SubscribeAsync_SeveralSubscriptions_AreIndependent()
    {
        var service = new FakeRedisChannelService();
        var first = new List<string>();
        var second = new List<string>();
        var firstSubscription = await service.SubscribeAsync("channel-1", (msg, _) => Record(first, msg));
        await service.SubscribeAsync("channel-1", (msg, _) => Record(second, msg));

        await service.PublishAsync("channel-1", "one");
        await firstSubscription.DisposeAsync();
        await service.PublishAsync("channel-1", "two");

        Assert.Equal(["one"], first);
        Assert.Equal(["one", "two"], second);
        Assert.Equal(1, service.GetSubscriptionCount("channel-1"));
    }

    [Fact]
    public async Task DisposeAsync_LastSubscription_UnsubscribesChannel_AndIsIdempotent()
    {
        var service = new FakeRedisChannelService();
        var received = new List<string>();
        var subscription = await service.SubscribeAsync("channel-1", (msg, _) => Record(received, msg));
        Assert.True(service.IsSubscribed("channel-1"));

        await subscription.DisposeAsync();
        await subscription.DisposeAsync();

        Assert.False(service.IsSubscribed("channel-1"));
        Assert.Empty(service.SubscribedChannels);
        Assert.Equal(0, await service.PublishAsync("channel-1", "hello"));
        Assert.Empty(received);
    }

    [Fact]
    public async Task PublishAsync_ThrowingHandler_IsContained_AndDeliveryContinues()
    {
        var service = new FakeRedisChannelService();
        var boom = new InvalidOperationException("boom");
        var sibling = new List<string>();
        var own = new List<string>();
        await service.SubscribeAsync(
            "channel-1",
            (msg, _) => msg == "first" ? throw boom : Record(own, msg));
        await service.SubscribeAsync("channel-1", (msg, _) => Record(sibling, msg));

        var exception = await Xunit.Record.ExceptionAsync(async () => await service.PublishAsync("channel-1", "first"));
        await service.PublishAsync("channel-1", "second");

        Assert.Null(exception);
        Assert.Equal(["first", "second"], sibling);
        Assert.Equal(["second"], own);
        Assert.Same(boom, Assert.Single(service.HandlerExceptions));
    }

    [Fact]
    public async Task PublishFromInsideHandler_IsQueued_AndHandledAfterCurrentMessage()
    {
        var service = new FakeRedisChannelService();
        var order = new List<string>();
        await service.SubscribeAsync(
            "channel-1",
            async (msg, _) =>
            {
                order.Add($"start:{msg}");
                if (msg == "outer")
                    await service.PublishAsync("channel-1", "inner");
                order.Add($"end:{msg}");
            });

        await service.PublishAsync("channel-1", "outer");

        Assert.Equal(["start:outer", "end:outer", "start:inner", "end:inner"], order);
    }

    [Fact]
    public async Task ConcurrentPublishers_HandleMessagesOneAtATime()
    {
        var service = new FakeRedisChannelService();
        var running = 0;
        var maxRunning = 0;
        var handled = 0;
        await service.SubscribeAsync(
            "channel-1",
            async (_, _) =>
            {
                var now = Interlocked.Increment(ref running);
                lock (service)
                    maxRunning = Math.Max(maxRunning, now);
                await Task.Yield();
                Interlocked.Decrement(ref running);
                Interlocked.Increment(ref handled);
            });

        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(async () => await service.PublishAsync("channel-1", $"m{i}"))));
        await service.PublishAsync("channel-1", "last");

        Assert.Equal(1, maxRunning);
        Assert.Equal(51, handled);
    }

    [Fact]
    public async Task DisposeAsync_FromInsideHandler_DoesNotDeadlock_AndCancelsHandlerToken()
    {
        var service = new FakeRedisChannelService();
        IAsyncDisposable? subscription = null;
        var cancelledAfterDispose = false;
        var received = new List<string>();
        subscription = await service.SubscribeAsync(
            "channel-1",
            async (msg, token) =>
            {
                received.Add(msg);
                await subscription!.DisposeAsync();
                cancelledAfterDispose = token.IsCancellationRequested;
            });

        await service.PublishAsync("channel-1", "one");
        await service.PublishAsync("channel-1", "two");

        Assert.True(cancelledAfterDispose);
        Assert.Equal(["one"], received);
        Assert.False(service.IsSubscribed("channel-1"));
    }

    [Fact]
    public async Task DisposeAsync_FromAnotherFlow_WaitsForRunningHandler()
    {
        var service = new FakeRedisChannelService();
        var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerFinished = false;
        var subscription = await service.SubscribeAsync(
            "channel-1",
            async (_, _) =>
            {
                handlerEntered.SetResult();
                await releaseHandler.Task;
                handlerFinished = true;
            });

        var publish = Task.Run(async () => await service.PublishAsync("channel-1", "hello"));
        await handlerEntered.Task;
        var dispose = subscription.DisposeAsync().AsTask();

        Assert.False(dispose.IsCompleted);
        releaseHandler.SetResult();
        await dispose;
        await publish;

        Assert.True(handlerFinished);
    }

    [Fact]
    public async Task TypedPublishAndSubscribe_RoundTripAsJson()
    {
        var service = new FakeRedisChannelService();
        var received = new List<FakeChannelTestMessage>();
        await service.SubscribeAsync("channel-1", MessageTypeInfo, (msg, _) => Record(received, msg));

        var message = new FakeChannelTestMessage("order", 7);
        await service.PublishAsync("channel-1", message, MessageTypeInfo);

        Assert.Equal([message], received);
        Assert.Equal(JsonSerializer.Serialize(message, MessageTypeInfo), service.GetPublishedMessages("channel-1").Single());
        Assert.Equal([message], service.GetPublishedMessages("channel-1", MessageTypeInfo));
    }

    [Fact]
    public async Task TypedSubscribe_MalformedMessage_IsSkipped_AndDeliveryContinues()
    {
        var service = new FakeRedisChannelService();
        var received = new List<FakeChannelTestMessage>();
        await service.SubscribeAsync("channel-1", MessageTypeInfo, (msg, _) => Record(received, msg));

        await service.PublishAsync("channel-1", "not json");
        await service.PublishAsync("channel-1", new FakeChannelTestMessage("ok", 1), MessageTypeInfo);

        Assert.Equal([new FakeChannelTestMessage("ok", 1)], received);
        Assert.Equal(("channel-1", "not json"), Assert.Single(service.SkippedMessages));
        Assert.Empty(service.HandlerExceptions);
    }

    [Fact]
    public async Task PublishAsync_RecordsMessages_InPublishOrder_EvenWithoutSubscribers()
    {
        var service = new FakeRedisChannelService();

        await service.PublishAsync("channel-1", "a");
        await service.PublishAsync("channel-2", "b");

        Assert.Equal([("channel-1", "a"), ("channel-2", "b")], service.PublishedMessages);
        Assert.Equal(["a"], service.GetPublishedMessages("channel-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task InvalidChannel_Throws(string? channel)
    {
        var service = new FakeRedisChannelService();

        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await service.PublishAsync(channel!, "hello"));
        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await service.SubscribeAsync(channel!, (_, _) => ValueTask.CompletedTask));
    }

    [Fact]
    public async Task NullArguments_Throw()
    {
        var service = new FakeRedisChannelService();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await service.PublishAsync("c", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await service.PublishAsync("c", new FakeChannelTestMessage("a", 1), null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await service.SubscribeAsync("c", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.SubscribeAsync<FakeChannelTestMessage>("c", null!, (_, _) => ValueTask.CompletedTask));
    }

    [Fact]
    public async Task CancelledToken_Throws_AndRecordsNothing()
    {
        var service = new FakeRedisChannelService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await service.PublishAsync("c", "hello", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await service.SubscribeAsync("c", (_, _) => ValueTask.CompletedTask, cts.Token));

        Assert.Empty(service.PublishedMessages);
        Assert.False(service.IsSubscribed("c"));
    }

    [Fact]
    public async Task SimulateFailure_PublishAndSubscribe_ThrowTimeout()
    {
        var service = new FakeRedisChannelService { SimulateFailure = true };

        await Assert.ThrowsAsync<TimeoutException>(async () => await service.PublishAsync("channel-1", "hello"));
        await Assert.ThrowsAsync<TimeoutException>(
            async () => await service.SubscribeAsync("channel-1", (_, _) => ValueTask.CompletedTask));
        Assert.Empty(service.PublishedMessages);
    }

    [Fact]
    public async Task Reset_ClearsRecordingsAndEndsSubscriptions()
    {
        var service = new FakeRedisChannelService();
        var received = new List<string>();
        await service.SubscribeAsync("channel-1", (msg, _) => msg == "boom" ? throw new InvalidOperationException() : Record(received, msg));
        await service.PublishAsync("channel-1", "boom");

        service.Reset();
        await service.PublishAsync("channel-1", "after");

        Assert.Empty(received);
        Assert.Empty(service.HandlerExceptions);
        Assert.Equal([("channel-1", "after")], service.PublishedMessages);
        Assert.Empty(service.SubscribedChannels);
    }

    private static ValueTask Record<T>(List<T> sink, T message)
    {
        lock (sink)
            sink.Add(message);
        return ValueTask.CompletedTask;
    }
}
