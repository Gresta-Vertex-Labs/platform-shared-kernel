using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.PubSub.Tests;

/// <summary>
/// Subscriptions after the server drops the subscription connection. StackExchange.Redis restores the channels
/// itself, so each subscription must receive every later message exactly once, with no replayed or doubled delivery.
/// </summary>
[Collection("Redis")]
public sealed class ChannelReconnectIntegrationTests(RedisFixture fixture)
{
    [Fact(Timeout = 60_000)]
    public async Task AfterTheSubscriptionConnectionIsKilled_EachSubscriptionReceivesEachMessageExactlyOnce()
    {
        var clientName = "reconnect-" + Guid.NewGuid().ToString("N");
        await using var host = fixture.BuildProvider(connectionStringSuffix: ",name=" + clientName);
        var service = host.GetRequiredService<IRedisChannelService>();
        var multiplexer = host.GetRequiredService<IConnectionMultiplexer>();

        var channel = RedisFixture.NewChannel("reconnect");
        var otherChannel = RedisFixture.NewChannel("reconnect-other");
        var first = new MessageRecorder<string>();
        var second = new MessageRecorder<string>();
        var other = new MessageRecorder<string>();

        await using var firstSubscription = await service.SubscribeAsync(channel, first.Handle);
        await using var secondSubscription = await service.SubscribeAsync(channel, second.Handle);
        await using var otherSubscription = await service.SubscribeAsync(otherChannel, other.Handle);

        Assert.Equal(1, await service.PublishAsync(channel, "before"));
        await first.WaitForCountAsync(1);
        await second.WaitForCountAsync(1);

        // Both events are raised on the thread pool, so their handlers may run in either order.
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        multiplexer.ConnectionFailed += (_, e) =>
        {
            if (e.ConnectionType == ConnectionType.Subscription)
                failed.TrySetResult();
        };
        multiplexer.ConnectionRestored += (_, e) =>
        {
            if (e.ConnectionType == ConnectionType.Subscription)
                restored.TrySetResult();
        };

        var killed = await KillSubscriptionConnectionsAsync(clientName);
        Assert.True(killed > 0, "No subscription connection with the test's client name was found to kill.");

        await failed.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await restored.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await WaitForSubscribersAsync(channel, expected: 1);
        await WaitForSubscribersAsync(otherChannel, expected: 1);

        Assert.Equal(1, await service.PublishAsync(channel, "after-1"));
        await service.PublishAsync(channel, "after-2");
        await service.PublishAsync(otherChannel, "other-after");

        await first.WaitForCountAsync(3);
        await second.WaitForCountAsync(3);
        await other.WaitForCountAsync(1);

        // Leave time for any duplicate delivery to show up.
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Equal(["before", "after-1", "after-2"], first.Messages);
        Assert.Equal(["before", "after-1", "after-2"], second.Messages);
        Assert.Equal(["other-after"], other.Messages);
    }

    [Fact(Timeout = 60_000)]
    public async Task SubscriptionDisposedBeforeReconnect_IsNotRestored()
    {
        var clientName = "reconnect-disposed-" + Guid.NewGuid().ToString("N");
        await using var host = fixture.BuildProvider(connectionStringSuffix: ",name=" + clientName);
        var service = host.GetRequiredService<IRedisChannelService>();
        var multiplexer = host.GetRequiredService<IConnectionMultiplexer>();

        var disposedChannel = RedisFixture.NewChannel("reconnect-disposed");
        var keptChannel = RedisFixture.NewChannel("reconnect-kept");
        var disposed = new MessageRecorder<string>();
        var kept = new MessageRecorder<string>();

        var disposedSubscription = await service.SubscribeAsync(disposedChannel, disposed.Handle);
        await using var keptSubscription = await service.SubscribeAsync(keptChannel, kept.Handle);
        await disposedSubscription.DisposeAsync();

        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        multiplexer.ConnectionRestored += (_, e) =>
        {
            if (e.ConnectionType == ConnectionType.Subscription)
                restored.TrySetResult();
        };

        var killed = await KillSubscriptionConnectionsAsync(clientName);
        Assert.True(killed > 0, "No subscription connection with the test's client name was found to kill.");
        await restored.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await WaitForSubscribersAsync(keptChannel, expected: 1);

        var disposedSubscribers = await fixture.CountSubscribersAsync(disposedChannel);
        Assert.True(disposedSubscribers == 0, $"The disposed channel has {disposedSubscribers} subscriber(s) after reconnecting.");
        var receivers = await service.PublishAsync(disposedChannel, "ignored");
        Assert.True(receivers == 0, $"Publishing to the disposed channel reached {receivers} subscriber(s).");
        await service.PublishAsync(keptChannel, "kept");
        await kept.WaitForCountAsync(1);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        Assert.Empty(disposed.Messages);
    }

    private async Task<int> KillSubscriptionConnectionsAsync(string clientName)
    {
        var list = (string)(await fixture.Database.ExecuteAsync("CLIENT", "LIST", "TYPE", "pubsub"))!;
        var ids = list
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Contains(" name=" + clientName + " ", StringComparison.Ordinal))
            .Select(line => Regex.Match(line, @"\bid=(\d+)").Groups[1].Value)
            .ToArray();

        foreach (var id in ids)
            await fixture.Database.ExecuteAsync("CLIENT", "KILL", "ID", id);

        return ids.Length;
    }

    private async Task WaitForSubscribersAsync(string channel, long expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (await fixture.CountSubscribersAsync(channel) != expected)
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Channel '{channel}' did not reach {expected} subscriber(s) after reconnecting.");

            await Task.Delay(50);
        }
    }
}
