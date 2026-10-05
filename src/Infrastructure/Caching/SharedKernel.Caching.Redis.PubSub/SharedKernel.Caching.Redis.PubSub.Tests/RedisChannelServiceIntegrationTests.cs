using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;
using Xunit;

namespace SharedKernel.Caching.Redis.PubSub.Tests;

/// <summary>
/// <see cref="IRedisChannelService"/> against a real Redis server.
/// </summary>
[Collection("Redis")]
public sealed class RedisChannelServiceIntegrationTests(RedisFixture fixture)
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);

    private IRedisChannelService ChannelService => fixture.ChannelService;

    // -------------------------------------------------------------------------
    // Publish and receive
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SubscribeAsync_ThenPublishAsync_HandlerReceivesTheMessage()
    {
        var channel = RedisFixture.NewChannel("round-trip");
        var recorder = new MessageRecorder<string>();

        await using var subscription = await ChannelService.SubscribeAsync(channel, recorder.Handle);
        var receivers = await ChannelService.PublishAsync(channel, "hello-world");

        await recorder.WaitForCountAsync(1);
        Assert.Equal(1, receivers);
        Assert.Equal(["hello-world"], recorder.Messages);
    }

    [Fact]
    public async Task PublishAsync_NoSubscribers_ReturnsZero()
    {
        Assert.Equal(0, await ChannelService.PublishAsync(RedisFixture.NewChannel("nobody"), "lost"));
    }

    [Fact]
    public async Task PublishAsync_ReturnsTheNumberOfSubscribedConnections()
    {
        var channel = RedisFixture.NewChannel("receiver-count");
        await using var otherHost = fixture.BuildProvider();
        var otherService = otherHost.GetRequiredService<IRedisChannelService>();

        await using var first = await ChannelService.SubscribeAsync(channel, (_, _) => ValueTask.CompletedTask);
        Assert.Equal(1, await ChannelService.PublishAsync(channel, "one"));

        // Subscriptions sharing one connection are one subscriber to Redis.
        await using var second = await ChannelService.SubscribeAsync(channel, (_, _) => ValueTask.CompletedTask);
        Assert.Equal(1, await ChannelService.PublishAsync(channel, "two"));

        await using var onOtherConnection = await otherService.SubscribeAsync(channel, (_, _) => ValueTask.CompletedTask);
        Assert.Equal(2, await ChannelService.PublishAsync(channel, "three"));
    }

    [Fact]
    public async Task TwoSubscriptionsOnOneChannel_BothReceiveEveryMessage()
    {
        var channel = RedisFixture.NewChannel("two-subscriptions");
        var first = new MessageRecorder<string>();
        var second = new MessageRecorder<string>();

        await using var firstSubscription = await ChannelService.SubscribeAsync(channel, first.Handle);
        await using var secondSubscription = await ChannelService.SubscribeAsync(channel, second.Handle);

        await ChannelService.PublishAsync(channel, "a");
        await ChannelService.PublishAsync(channel, "b");

        await first.WaitForCountAsync(2);
        await second.WaitForCountAsync(2);
        await Task.Delay(Quiet);
        Assert.Equal(["a", "b"], first.Messages);
        Assert.Equal(["a", "b"], second.Messages);
    }

    [Fact]
    public async Task DisposingOneSubscription_LeavesTheOtherReceiving_AndTheDisposedOneReceivesNothing()
    {
        var channel = RedisFixture.NewChannel("dispose-one");
        var disposed = new MessageRecorder<string>();
        var kept = new MessageRecorder<string>();

        var disposedSubscription = await ChannelService.SubscribeAsync(channel, disposed.Handle);
        await using var keptSubscription = await ChannelService.SubscribeAsync(channel, kept.Handle);

        await ChannelService.PublishAsync(channel, "before");
        await disposed.WaitForCountAsync(1);
        await kept.WaitForCountAsync(1);

        await disposedSubscription.DisposeAsync();

        Assert.Equal(1, await fixture.CountSubscribersAsync(channel));
        Assert.Equal(1, await ChannelService.PublishAsync(channel, "after-1"));
        await ChannelService.PublishAsync(channel, "after-2");

        await kept.WaitForCountAsync(3);
        await Task.Delay(Quiet);
        Assert.Equal(["before", "after-1", "after-2"], kept.Messages);
        Assert.Equal(["before"], disposed.Messages);
    }

    [Fact]
    public async Task DisposingEverySubscription_UnsubscribesTheChannel()
    {
        var channel = RedisFixture.NewChannel("dispose-all");
        var recorder = new MessageRecorder<string>();

        var first = await ChannelService.SubscribeAsync(channel, recorder.Handle);
        var second = await ChannelService.SubscribeAsync(channel, recorder.Handle);
        Assert.Equal(1, await fixture.CountSubscribersAsync(channel));

        await first.DisposeAsync();
        await second.DisposeAsync();

        Assert.Equal(0, await fixture.CountSubscribersAsync(channel));
        Assert.Equal(0, await ChannelService.PublishAsync(channel, "nobody"));
        await Task.Delay(Quiet);
        Assert.Empty(recorder.Messages);
    }

    [Fact]
    public async Task SubscribeAgainAfterDispose_OnlyTheNewHandlerReceives()
    {
        var channel = RedisFixture.NewChannel("resubscribe");
        var old = new MessageRecorder<string>();
        var current = new MessageRecorder<string>();

        var oldSubscription = await ChannelService.SubscribeAsync(channel, old.Handle);
        await oldSubscription.DisposeAsync();
        await using var currentSubscription = await ChannelService.SubscribeAsync(channel, current.Handle);

        await ChannelService.PublishAsync(channel, "m");

        await current.WaitForCountAsync(1);
        await Task.Delay(Quiet);
        Assert.Equal(["m"], current.Messages);
        Assert.Empty(old.Messages);
    }

    [Fact]
    public async Task BurstOfMessages_IsHandledInPublishOrder_PerSubscription()
    {
        const int count = 300;
        var channel = RedisFixture.NewChannel("order");
        var fast = new MessageRecorder<string>();
        var slow = new MessageRecorder<string>();
        var concurrent = 0;
        var maxConcurrent = 0;

        await using var fastSubscription = await ChannelService.SubscribeAsync(channel, fast.Handle);
        await using var slowSubscription = await ChannelService.SubscribeAsync(channel, async (message, ct) =>
        {
            var now = Interlocked.Increment(ref concurrent);
            Interlocked.Exchange(ref maxConcurrent, Math.Max(Volatile.Read(ref maxConcurrent), now));
            if (int.Parse(message) % 25 == 0)
                await Task.Delay(5, ct);
            else
                await Task.Yield();

            slow.Add(message);
            Interlocked.Decrement(ref concurrent);
        });

        var expected = Enumerable.Range(0, count).Select(i => i.ToString()).ToArray();
        foreach (var message in expected)
            await ChannelService.PublishAsync(channel, message);

        await fast.WaitForCountAsync(count, TimeSpan.FromSeconds(30));
        await slow.WaitForCountAsync(count, TimeSpan.FromSeconds(30));
        Assert.Equal(expected, fast.Messages);
        Assert.Equal(expected, slow.Messages);
        Assert.Equal(1, maxConcurrent);
    }

    // -------------------------------------------------------------------------
    // Failures
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HandlerThrows_IsLogged_AndLaterMessagesAreStillHandled()
    {
        var channel = RedisFixture.NewChannel("handler-throws");
        var recorder = new MessageRecorder<string>();

        await using var subscription = await ChannelService.SubscribeAsync(channel, (message, ct) =>
        {
            recorder.Add(message);
            return message.StartsWith("boom", StringComparison.Ordinal)
                ? throw new InvalidOperationException("intentional handler exception")
                : ValueTask.CompletedTask;
        });

        await ChannelService.PublishAsync(channel, "boom-1");
        await ChannelService.PublishAsync(channel, "after-1");
        await ChannelService.PublishAsync(channel, "boom-2");
        await ChannelService.PublishAsync(channel, "after-2");

        await recorder.WaitForCountAsync(4);
        Assert.Equal(["boom-1", "after-1", "boom-2", "after-2"], recorder.Messages);

        var failures = fixture.Logs.ForChannel(channel);
        Assert.Equal(2, failures.Count);
        Assert.All(failures, log =>
        {
            Assert.Equal(LoggingEventIdRanges.Caching + 500, log.EventId.Id);
            Assert.Equal(LogLevel.Error, log.Level);
            Assert.IsType<InvalidOperationException>(log.Exception);
        });
    }

    [Fact]
    public async Task HandlerThrowsOperationCanceled_WhileNotDisposed_IsLoggedAndSubscriptionContinues()
    {
        var channel = RedisFixture.NewChannel("handler-cancels");
        var recorder = new MessageRecorder<string>();

        await using var subscription = await ChannelService.SubscribeAsync(channel, (message, ct) =>
        {
            recorder.Add(message);
            return message == "cancel" ? throw new OperationCanceledException() : ValueTask.CompletedTask;
        });

        await ChannelService.PublishAsync(channel, "cancel");
        await ChannelService.PublishAsync(channel, "after");

        await recorder.WaitForCountAsync(2);
        var failure = Assert.Single(fixture.Logs.ForChannel(channel));
        Assert.Equal(LoggingEventIdRanges.Caching + 500, failure.EventId.Id);
    }

    // -------------------------------------------------------------------------
    // Typed messages
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TypedPublishAndSubscribe_RoundTripsTheMessage()
    {
        var channel = RedisFixture.NewChannel("typed");
        var recorder = new MessageRecorder<TestMessage>();
        var raw = new MessageRecorder<string>();
        var message = new TestMessage("created", 42);

        await using var typed = await ChannelService.SubscribeAsync(channel, TestJsonContext.Default.TestMessage, recorder.Handle);
        await using var text = await ChannelService.SubscribeAsync(channel, raw.Handle);

        Assert.Equal(1, await ChannelService.PublishAsync(channel, message, TestJsonContext.Default.TestMessage));

        await recorder.WaitForCountAsync(1);
        await raw.WaitForCountAsync(1);
        Assert.Equal([message], recorder.Messages);
        Assert.Equal(["{\"Kind\":\"created\",\"Sequence\":42}"], raw.Messages);
    }

    [Fact]
    public async Task TypedSubscription_MalformedMessage_IsLoggedAndSkipped_AndLaterMessagesArrive()
    {
        var channel = RedisFixture.NewChannel("typed-malformed");
        var recorder = new MessageRecorder<TestMessage>();

        await using var subscription = await ChannelService.SubscribeAsync(channel, TestJsonContext.Default.TestMessage, recorder.Handle);

        await ChannelService.PublishAsync(channel, "{not json");
        await ChannelService.PublishAsync(channel, new TestMessage("valid", 1), TestJsonContext.Default.TestMessage);
        await ChannelService.PublishAsync(channel, "[1,2,3]");
        await ChannelService.PublishAsync(channel, new TestMessage("valid", 2), TestJsonContext.Default.TestMessage);

        await recorder.WaitForCountAsync(2);
        await Task.Delay(Quiet);
        Assert.Equal([new TestMessage("valid", 1), new TestMessage("valid", 2)], recorder.Messages);

        var skipped = fixture.Logs.ForChannel(channel);
        Assert.Equal(2, skipped.Count);
        Assert.All(skipped, log =>
        {
            Assert.Equal(LoggingEventIdRanges.Caching + 501, log.EventId.Id);
            Assert.Equal(LogLevel.Warning, log.Level);
            Assert.Equal(nameof(TestMessage), log.Properties["MessageType"]);
        });
        Assert.DoesNotContain(skipped, log => log.EventId.Id == LoggingEventIdRanges.Caching + 500);
    }

    // -------------------------------------------------------------------------
    // Disposal
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DisposeFromInsideTheHandler_DoesNotDeadlock_AndEndsTheSubscription()
    {
        var channel = RedisFixture.NewChannel("dispose-inside");
        var subscription = new TaskCompletionSource<IAsyncDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposedInside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handled = 0;

        subscription.SetResult(await ChannelService.SubscribeAsync(channel, async (_, ct) =>
        {
            Interlocked.Increment(ref handled);
            await (await subscription.Task).DisposeAsync();
            disposedInside.TrySetResult();
        }));

        await ChannelService.PublishAsync(channel, "first");
        await disposedInside.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, await fixture.CountSubscribersAsync(channel));
        Assert.Equal(0, await ChannelService.PublishAsync(channel, "second"));
        await Task.Delay(Quiet);
        Assert.Equal(1, handled);

        // Disposing again from outside is a no-op.
        await (await subscription.Task).DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Dispose_CancelsTheHandlerToken_AndWaitsForTheHandlerToFinish()
    {
        var channel = RedisFixture.NewChannel("dispose-cancels");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokenCancelled = false;
        var finished = false;

        var subscription = await ChannelService.SubscribeAsync(channel, async (_, ct) =>
        {
            Assert.False(ct.IsCancellationRequested);
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                tokenCancelled = true;
            }

            // Work that ignores the token; disposal must still wait for it.
            await Task.Delay(300, CancellationToken.None);
            finished = true;
        });

        await ChannelService.PublishAsync(channel, "long-running");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await subscription.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(tokenCancelled);
        Assert.True(finished, "DisposeAsync returned before the running handler finished.");
        Assert.Empty(fixture.Logs.ForChannel(channel));
    }

    [Fact]
    public async Task Dispose_HandlerRethrowsCancellation_EndsQuietly()
    {
        var channel = RedisFixture.NewChannel("dispose-rethrows");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var subscription = await ChannelService.SubscribeAsync(channel, async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });

        await ChannelService.PublishAsync(channel, "long-running");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await subscription.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        await subscription.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(fixture.Logs.ForChannel(channel));
        Assert.Equal(0, await fixture.CountSubscribersAsync(channel));
    }

    // -------------------------------------------------------------------------
    // Arguments and cancellation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankChannel_ThrowsArgumentException(string channel)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ChannelService.PublishAsync(channel, "m").AsTask());
        await Assert.ThrowsAsync<ArgumentException>(
            () => ChannelService.PublishAsync(channel, new TestMessage("k", 1), TestJsonContext.Default.TestMessage).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => ChannelService.SubscribeAsync(channel, (_, _) => ValueTask.CompletedTask).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(
            () => ChannelService.SubscribeAsync(channel, TestJsonContext.Default.TestMessage, (_, _) => ValueTask.CompletedTask).AsTask());
    }

    [Fact]
    public async Task NullArguments_ThrowArgumentNullException()
    {
        var channel = RedisFixture.NewChannel("null-arguments");

        await Assert.ThrowsAnyAsync<ArgumentException>(() => ChannelService.PublishAsync(null!, "m").AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => ChannelService.PublishAsync(channel, (string)null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => ChannelService.PublishAsync(channel, new TestMessage("k", 1), null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => ChannelService.SubscribeAsync(channel, null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => ChannelService.SubscribeAsync<TestMessage>(channel, null!, (_, _) => ValueTask.CompletedTask).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => ChannelService.SubscribeAsync(channel, TestJsonContext.Default.TestMessage, null!).AsTask());

        Assert.Equal(0, await fixture.CountSubscribersAsync(channel));
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceledException_AndSubscribesNothing()
    {
        var channel = RedisFixture.NewChannel("cancelled");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ChannelService.SubscribeAsync(channel, (_, _) => ValueTask.CompletedTask, cts.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ChannelService.PublishAsync(channel, "m", cts.Token).AsTask());

        Assert.Equal(0, await fixture.CountSubscribersAsync(channel));
    }
}
