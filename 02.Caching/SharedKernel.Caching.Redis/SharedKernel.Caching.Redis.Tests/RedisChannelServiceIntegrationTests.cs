using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Extensions;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Tests;

/// <summary>
/// Integration tests for <see cref="IRedisChannelService"/> backed by StackExchange.Redis Pub/Sub.
/// Uses Testcontainers to spin up a real Redis instance.
/// </summary>
[Collection("Redis")]
public sealed class RedisChannelServiceIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private ServiceProvider? _provider;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        // AddRedisDistributedLocking registers IConnectionMultiplexer — required by RedisChannelService.
        services.AddRedisDistributedLocking(_redisContainer.GetConnectionString());

        // Build a fake ICachingBuilder wrapping the service collection.
        var builder = new TestCachingBuilder(services);
        builder.AddRedisChannelService();

        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    private IRedisChannelService ChannelService =>
        _provider!.GetRequiredService<IRedisChannelService>();

    [Fact]
    public async Task PublishAsync_SubscribeAsync_RoundTrip_HandlerReceivesMessage()
    {
        var channel = "test:channel:" + Guid.NewGuid();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        await ChannelService.SubscribeAsync(channel, msg =>
        {
            received.TrySetResult(msg);
            return ValueTask.CompletedTask;
        });

        // Give the subscription a moment to be established.
        await Task.Delay(100);

        await ChannelService.PublishAsync(channel, "hello-world");

        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("hello-world", result);
    }

    [Fact]
    public async Task UnsubscribeAsync_StopsDelivery()
    {
        var channel = "test:unsub:" + Guid.NewGuid();
        var receiveCount = 0;
        var firstReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await ChannelService.SubscribeAsync(channel, _ =>
        {
            receiveCount++;
            firstReceived.TrySetResult(true);
            return ValueTask.CompletedTask;
        });

        await Task.Delay(100);

        // Publish first message — should be received.
        await ChannelService.PublishAsync(channel, "first");
        await firstReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Unsubscribe.
        await ChannelService.UnsubscribeAsync(channel);

        // Small delay for the unsubscribe to propagate.
        await Task.Delay(200);

        // Publish second message — should NOT be received.
        await ChannelService.PublishAsync(channel, "second");

        // Wait briefly and confirm count did not increase.
        await Task.Delay(300);
        Assert.Equal(1, receiveCount);
    }

    [Fact]
    public async Task SubscribeAsync_HandlerThrows_ExceptionSwallowed_DoesNotCrash()
    {
        var channel = "test:exception:" + Guid.NewGuid();
        var firstInvoked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await ChannelService.SubscribeAsync(channel, _ =>
        {
            firstInvoked.TrySetResult(true);
            throw new InvalidOperationException("intentional handler exception");
        });

        await Task.Delay(100);
        await ChannelService.PublishAsync(channel, "trigger");

        // Handler should be invoked (and exception swallowed) — no crash.
        await firstInvoked.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Subsequent publish should still work (subscriber thread still alive).
        await ChannelService.PublishAsync(channel, "subsequent");
        // No assertion on subsequent — just verify no exception is thrown.
    }

    [Fact]
    public void AddRedisChannelService_RegistersIRedisChannelService_AsSingleton()
    {
        var first = _provider!.GetRequiredService<IRedisChannelService>();
        var second = _provider!.GetRequiredService<IRedisChannelService>();

        Assert.NotNull(first);
        Assert.Same(first, second);
    }
}
