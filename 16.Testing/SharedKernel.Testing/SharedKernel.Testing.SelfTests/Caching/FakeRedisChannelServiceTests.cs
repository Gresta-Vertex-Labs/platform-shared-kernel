using SharedKernel.Caching.Redis.Core;
using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>
/// Proves <see cref="FakeRedisChannelService"/> against <c>IRedisChannelService</c>'s contract — no
/// existing `02.Caching` consuming-domain test (e.g. `SharedKernel.Caching.Redis.PubSub.Tests`)
/// currently exercises this fake's publish/subscribe fan-out directly, so coverage is provided here
/// per the documented SelfTests fallback.
/// </summary>
public sealed class FakeRedisChannelServiceTests
{
    [Fact]
    public async Task PublishAsync_ReachesSubscribedHandler()
    {
        var service = new FakeRedisChannelService();
        var received = new List<string>();
        await service.SubscribeAsync(
            "channel-1",
            msg =>
            {
                received.Add(msg);
                return ValueTask.CompletedTask;
            });

        await service.PublishAsync("channel-1", "hello");

        Assert.Equal(["hello"], received);
    }

    [Fact]
    public async Task PublishAsync_AfterUnsubscribe_ReachesNoHandler()
    {
        var service = new FakeRedisChannelService();
        var received = new List<string>();
        await service.SubscribeAsync(
            "channel-1",
            msg =>
            {
                received.Add(msg);
                return ValueTask.CompletedTask;
            });
        await service.UnsubscribeAsync("channel-1");

        await service.PublishAsync("channel-1", "hello");

        Assert.Empty(received);
    }

    [Fact]
    public async Task PublishAsync_MultipleHandlersOnSameChannel_AllFire()
    {
        var service = new FakeRedisChannelService();
        var received1 = new List<string>();
        var received2 = new List<string>();
        await service.SubscribeAsync(
            "channel-1",
            msg =>
            {
                received1.Add(msg);
                return ValueTask.CompletedTask;
            });
        await service.SubscribeAsync(
            "channel-1",
            msg =>
            {
                received2.Add(msg);
                return ValueTask.CompletedTask;
            });

        await service.PublishAsync("channel-1", "hello");

        Assert.Equal(["hello"], received1);
        Assert.Equal(["hello"], received2);
    }

    [Fact]
    public async Task PublishAsync_ThrowingHandler_SwallowedAndSiblingHandlerStillRuns()
    {
        var service = new FakeRedisChannelService();
        var received = new List<string>();
        await service.SubscribeAsync("channel-1", _ => throw new InvalidOperationException("boom"));
        await service.SubscribeAsync(
            "channel-1",
            msg =>
            {
                received.Add(msg);
                return ValueTask.CompletedTask;
            });

        var exception = await Record.ExceptionAsync(async () => await service.PublishAsync("channel-1", "hello"));

        Assert.Null(exception);
        Assert.Equal(["hello"], received);
    }

    [Fact]
    public async Task PublishAsync_RecordsMessage_InPublishedMessages()
    {
        var service = new FakeRedisChannelService();

        await service.PublishAsync("channel-1", "hello");

        Assert.Single(service.PublishedMessages);
        Assert.Equal(("channel-1", "hello"), service.PublishedMessages[0]);
    }

    [Fact]
    public async Task PublishAsync_RecordsMessage_EvenWithNoSubscribers()
    {
        var service = new FakeRedisChannelService();

        var exception = await Record.ExceptionAsync(async () => await service.PublishAsync("no-subscribers", "hello"));

        Assert.Null(exception);
        Assert.Single(service.PublishedMessages);
    }

    [Fact]
    public async Task SubscribedChannels_ReflectsActiveSubscriptions()
    {
        var service = new FakeRedisChannelService();
        await service.SubscribeAsync("channel-1", _ => ValueTask.CompletedTask);
        await service.SubscribeAsync("channel-2", _ => ValueTask.CompletedTask);

        Assert.Equal(2, service.SubscribedChannels.Count);
        Assert.Contains("channel-1", service.SubscribedChannels);
        Assert.Contains("channel-2", service.SubscribedChannels);

        await service.UnsubscribeAsync("channel-1");

        Assert.DoesNotContain("channel-1", service.SubscribedChannels);
        Assert.Contains("channel-2", service.SubscribedChannels);
    }

    [Fact]
    public async Task IsSubscribed_TrueWhileSubscribed_FalseAfterUnsubscribe()
    {
        var service = new FakeRedisChannelService();
        Assert.False(service.IsSubscribed("channel-1"));

        await service.SubscribeAsync("channel-1", _ => ValueTask.CompletedTask);
        Assert.True(service.IsSubscribed("channel-1"));

        await service.UnsubscribeAsync("channel-1");
        Assert.False(service.IsSubscribed("channel-1"));
    }

    [Fact]
    public void ConnectionHealth_DefaultsToConnected_AndIsSettable()
    {
        var service = new FakeRedisChannelService();
        Assert.Equal(ConnectionHealthState.Connected, service.ConnectionHealth);

        service.ConnectionHealth = ConnectionHealthState.Disconnected;

        Assert.Equal(ConnectionHealthState.Disconnected, service.ConnectionHealth);
    }

    [Fact]
    public async Task SimulateFailure_PublishAsync_Throws()
    {
        var service = new FakeRedisChannelService { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.PublishAsync("channel-1", "hello"));
    }

    [Fact]
    public async Task SimulateFailure_SubscribeAsync_Throws()
    {
        var service = new FakeRedisChannelService { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.SubscribeAsync("channel-1", _ => ValueTask.CompletedTask));
    }

    [Fact]
    public async Task SimulateFailure_UnsubscribeAsync_Throws()
    {
        var service = new FakeRedisChannelService { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.UnsubscribeAsync("channel-1"));
    }

    [Fact]
    public async Task Reset_ClearsPublishedMessagesAndSubscriptions()
    {
        var service = new FakeRedisChannelService();
        await service.SubscribeAsync("channel-1", _ => ValueTask.CompletedTask);
        await service.PublishAsync("channel-1", "hello");

        service.Reset();

        Assert.Empty(service.PublishedMessages);
        Assert.Empty(service.SubscribedChannels);
        Assert.False(service.IsSubscribed("channel-1"));
    }

    [Fact]
    public async Task UnsubscribeAsync_UnknownChannel_IsIdempotentNoOp()
    {
        var service = new FakeRedisChannelService();

        var exception = await Record.ExceptionAsync(async () => await service.UnsubscribeAsync("never-subscribed"));

        Assert.Null(exception);
    }
}
