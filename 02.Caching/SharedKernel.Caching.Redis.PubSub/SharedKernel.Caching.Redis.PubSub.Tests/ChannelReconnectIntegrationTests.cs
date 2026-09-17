using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Core;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.PubSub.Extensions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.PubSub.Tests;

/// <summary>
/// Integration tests for <see cref="IRedisChannelService"/> reconnect resilience.
/// Verifies that subscriptions are automatically reinstated after the Redis connection
/// is dropped and restored, and that <see cref="ConnectionHealthState"/> transitions correctly.
/// </summary>
[Collection("Redis")]
public sealed class ChannelReconnectIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private ServiceProvider? _provider;
    private IConnectionMultiplexer? _multiplexer;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug));
        services.AddRedisConnection(
            _redisContainer.GetConnectionString() + ",abortConnect=false,connectRetry=10");

        var builder = new TestCachingBuilder(services);
        builder.AddRedisChannelService();

        _provider = services.BuildServiceProvider();
        _multiplexer = _provider.GetRequiredService<IConnectionMultiplexer>();

        // Wait for the initial connection to become established.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!_multiplexer.IsConnected && DateTime.UtcNow < deadline)
            await Task.Delay(200);
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    private IRedisChannelService ChannelService =>
        _provider!.GetRequiredService<IRedisChannelService>();

    // ─── Unit-level tests (InternalsVisibleTo + mocked subscriber) ──────────────

    /// <summary>
    /// Verifies initial <see cref="ConnectionHealthState"/> is <see cref="ConnectionHealthState.Connected"/>.
    /// </summary>
    [Fact]
    public void ConnectionHealth_InitialState_IsConnected()
    {
        var (service, _) = BuildIsolatedService(multiplexerIsConnected: true);
        Assert.Equal(ConnectionHealthState.Connected, service.ConnectionHealth);
    }

    /// <summary>
    /// When <c>ConnectionFailed</c> fires and the multiplexer reports disconnected,
    /// <see cref="ConnectionHealthState"/> transitions to <see cref="ConnectionHealthState.Reconnecting"/>.
    /// </summary>
    [Fact]
    public void ConnectionHealth_OnConnectionFailed_MultiplexerDisconnected_TransitionsToReconnecting()
    {
        var (service, mux) = BuildIsolatedService(multiplexerIsConnected: false);

        // Simulate ConnectionFailed event.
        service.OnConnectionFailed(mux, null!);

        Assert.Equal(ConnectionHealthState.Reconnecting, service.ConnectionHealth);
    }

    /// <summary>
    /// When <c>ConnectionRestored</c> fires, <see cref="ConnectionHealthState"/> transitions
    /// back to <see cref="ConnectionHealthState.Connected"/>.
    /// </summary>
    [Fact]
    public void ConnectionHealth_OnConnectionRestored_TransitionsToConnected()
    {
        var (service, mux) = BuildIsolatedService(multiplexerIsConnected: false);

        // Start from Reconnecting.
        service.OnConnectionFailed(mux, null!);
        Assert.Equal(ConnectionHealthState.Reconnecting, service.ConnectionHealth);

        // Restore.
        service.OnConnectionRestored(mux, null!);

        Assert.Equal(ConnectionHealthState.Connected, service.ConnectionHealth);
    }

    /// <summary>
    /// On <c>ConnectionRestored</c>, all registered channels are resubscribed atomically;
    /// the resubscription count equals the pre-disconnect subscription count.
    /// </summary>
    [Fact]
    public async Task ConnectionRestored_ResubscribesAllRegisteredChannels_CountMatchesPreDisconnect()
    {
        var subscribeCallCount = 0;
        var subscriber = BuildRecordingSubscriber(onSubscribe: () => subscribeCallCount++);

        var mux = BuildMockedMultiplexer(subscriber, isConnected: true);
        var service = new RedisChannelService(mux, NullLogger<RedisChannelService>.Instance);

        const int channelCount = 3;
        for (var i = 1; i <= channelCount; i++)
            await service.SubscribeAsync($"ch:{i}", _ => ValueTask.CompletedTask);

        // Reset counter to measure only reconnect-driven calls.
        subscribeCallCount = 0;

        // Act: simulate ConnectionRestored.
        service.OnConnectionRestored(mux, null!);

        // All channelCount subscriptions must have been replayed.
        Assert.Equal(channelCount, subscribeCallCount);
        Assert.Equal(ConnectionHealthState.Connected, service.ConnectionHealth);
    }

    /// <summary>
    /// A resubscription failure for one channel must not abort replay of remaining channels;
    /// health transitions to <see cref="ConnectionHealthState.Connected"/> regardless.
    /// </summary>
    [Fact]
    public async Task ConnectionRestored_PartialResubscriptionFailure_RemainingChannelsResubscribed()
    {
        var replayAttempts = new List<string>();
        var throwOnChannel = "ch:2";
        // Flag: throw only during replay, not during initial subscriptions.
        var inReplay = false;

        var subscriber = BuildRecordingSubscriber(onSubscribeChannel: ch =>
        {
            if (!inReplay) return;
            replayAttempts.Add(ch);
            if (ch == throwOnChannel)
                throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "simulated");
        });

        var mux = BuildMockedMultiplexer(subscriber, isConnected: true);
        var service = new RedisChannelService(mux, NullLogger<RedisChannelService>.Instance);

        // Initial subscriptions — no throws.
        await service.SubscribeAsync("ch:1", _ => ValueTask.CompletedTask);
        await service.SubscribeAsync("ch:2", _ => ValueTask.CompletedTask);
        await service.SubscribeAsync("ch:3", _ => ValueTask.CompletedTask);

        // Enable replay mode: subsequent SubscribeAsync calls on ch:2 will throw.
        inReplay = true;

        // Act: simulate reconnect.
        service.OnConnectionRestored(mux, null!);

        // All 3 channels must have been attempted during replay
        // (order may vary — Dictionary iteration order is unspecified).
        Assert.Equal(3, replayAttempts.Count);
        Assert.Contains("ch:1", replayAttempts);
        Assert.Contains("ch:2", replayAttempts);
        Assert.Contains("ch:3", replayAttempts);

        // Health is still Connected (per-channel failure is logged, not fatal).
        Assert.Equal(ConnectionHealthState.Connected, service.ConnectionHealth);
    }

    /// <summary>
    /// After <c>SubscribeAsync</c> is called, the channel appears in the registry.
    /// After <c>UnsubscribeAsync</c>, it is removed. Registry count stays consistent.
    /// </summary>
    [Fact]
    public async Task SubscriptionRegistry_ReflectsSubscribeAndUnsubscribeCalls()
    {
        var (service, _) = BuildIsolatedService(multiplexerIsConnected: true);

        Assert.Equal(0, service.SubscriptionCount);

        await service.SubscribeAsync("reg:ch:1", _ => ValueTask.CompletedTask);
        await service.SubscribeAsync("reg:ch:2", _ => ValueTask.CompletedTask);
        Assert.Equal(2, service.SubscriptionCount);

        await service.UnsubscribeAsync("reg:ch:1");
        Assert.Equal(1, service.SubscriptionCount);

        await service.UnsubscribeAsync("reg:ch:2");
        Assert.Equal(0, service.SubscriptionCount);
    }

    // ─── Live integration test against real Redis ────────────────────────────────

    /// <summary>
    /// Forces a subscriber connection drop via the Redis <c>CLIENT KILL</c> command.
    /// Verifies messages are delivered on both subscribed channels after SE.Redis reconnects
    /// and <see cref="RedisChannelService"/> resubscribes automatically.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task LiveRedis_AfterForcedSubscriberConnectionKill_MessagesDeliveredAfterReconnect()
    {
        var channelA = "live-reconnect:a:" + Guid.NewGuid();
        var channelB = "live-reconnect:b:" + Guid.NewGuid();

        var receivedA = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedB = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var preKillReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Subscribe to two channels before the forced drop.
        await ChannelService.SubscribeAsync(channelA, msg =>
        {
            receivedA.TrySetResult(msg);
            return ValueTask.CompletedTask;
        });

        await ChannelService.SubscribeAsync(channelB, msg =>
        {
            receivedB.TrySetResult(msg);
            return ValueTask.CompletedTask;
        });

        // Subscribe a pre-kill channel and confirm delivery works before we kill.
        var preKillChannel = "live-reconnect:pre:" + Guid.NewGuid();
        await ChannelService.SubscribeAsync(preKillChannel, _ =>
        {
            preKillReceived.TrySetResult(true);
            return ValueTask.CompletedTask;
        });
        await Task.Delay(300);

        await ChannelService.PublishAsync(preKillChannel, "pre-kill-ping");
        await preKillReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // ── Force subscriber connection kill ─────────────────────────────────────
        var db = _multiplexer!.GetDatabase();
        var clientListResult = (string?)await db.ExecuteAsync("CLIENT", "LIST");
        if (clientListResult is not null)
        {
            foreach (var line in clientListResult.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                // Kill subscriber connections (flags field contains 'S').
                if (!line.Contains("flags=S") && !line.Contains("flags=PS"))
                    continue;

                var idPart = Array.Find(
                    line.Split(' '),
                    p => p.StartsWith("id=", StringComparison.Ordinal));

                if (idPart is null) continue;
                var id = idPart["id=".Length..];
                try { await db.ExecuteAsync("CLIENT", "KILL", "ID", id); } catch { /* intentionally ignored */ }
            }
        }

        // Allow SE.Redis to detect the drop, reconnect, and trigger resubscription.
        await Task.Delay(2000);

        // ── Publish and verify delivery after reconnect ───────────────────────────
        await ChannelService.PublishAsync(channelA, "after-reconnect-a");
        await ChannelService.PublishAsync(channelB, "after-reconnect-b");

        var resultA = await receivedA.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var resultB = await receivedB.Task.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal("after-reconnect-a", resultA);
        Assert.Equal("after-reconnect-b", resultB);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="RedisChannelService"/> with a mocked multiplexer and a
    /// no-op subscriber for simple unit-level event and health state tests.
    /// </summary>
    private static (RedisChannelService Service, IConnectionMultiplexer Mux)
        BuildIsolatedService(bool multiplexerIsConnected)
    {
        var subscriber = BuildRecordingSubscriber();
        var mux = BuildMockedMultiplexer(subscriber, isConnected: multiplexerIsConnected);
        var service = new RedisChannelService(mux, NullLogger<RedisChannelService>.Instance);
        return (service, mux);
    }

    /// <summary>
    /// Builds a no-op or recording <see cref="ISubscriber"/> substitute.
    /// </summary>
    private static ISubscriber BuildRecordingSubscriber(
        Action? onSubscribe = null,
        Action<string>? onSubscribeChannel = null)
    {
        var subscriber = Substitute.For<ISubscriber>();

        // ChannelMessageQueue is sealed — return null! via Task<ChannelMessageQueue> directly.
        // RedisChannelService does not use the returned ChannelMessageQueue — it is safe to return null.
        subscriber
            .SubscribeAsync(
                Arg.Any<RedisChannel>(),
                Arg.Any<Action<RedisChannel, RedisValue>>(),
                Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var channel = (RedisChannel)callInfo[0];
                var channelName = (string)channel;
                onSubscribe?.Invoke();
                onSubscribeChannel?.Invoke(channelName);
                return Task.FromResult<ChannelMessageQueue>(null!);
            });

        subscriber
            .UnsubscribeAsync(
                Arg.Any<RedisChannel>(),
                Arg.Any<Action<RedisChannel, RedisValue>?>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.CompletedTask);

        return subscriber;
    }

    /// <summary>
    /// Builds an <see cref="IConnectionMultiplexer"/> substitute with the given subscriber and connection state.
    /// </summary>
    private static IConnectionMultiplexer BuildMockedMultiplexer(ISubscriber subscriber, bool isConnected)
    {
        var mux = Substitute.For<IConnectionMultiplexer>();
        mux.GetSubscriber(Arg.Any<object?>()).Returns(subscriber);
        mux.IsConnected.Returns(isConnected);
        return mux;
    }
}
