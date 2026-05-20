using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.Core;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.Redis.Extensions;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Tests;

// ---------------------------------------------------------------------------
// Unit tests for CacheInvalidationReceiver dispatch logic (via mock channel service)
// ---------------------------------------------------------------------------

/// <summary>
/// Unit tests for <see cref="CacheInvalidationReceiver"/> dispatch logic.
/// Uses NSubstitute to control the channel service, avoiding Redis timing issues.
/// </summary>
public sealed class CacheInvalidationReceiverUnitTests
{
    private const string ServiceName = "unit-svc";

    private static string TargetedChannel =>
        $"sharedkernel:cache:invalidation:{ServiceName}";

    private static string BroadcastChannel =>
        "sharedkernel:cache:invalidation:broadcast";

    /// <summary>
    /// Builds a <see cref="CacheInvalidationReceiver"/> with a mock channel service so tests
    /// can directly invoke the subscription handler without Redis round-trips.
    /// </summary>
    private static (CacheInvalidationReceiver receiver, ICacheService cacheService, Func<string, ValueTask> targetedHandler, Func<string, ValueTask> broadcastHandler)
        BuildReceiver()
    {
        var channelService = Substitute.For<IRedisChannelService>();
        var cacheService = Substitute.For<ICacheService>();
        var logger = Substitute.For<ILogger<CacheInvalidationReceiver>>();

        Func<string, ValueTask> capturedTargetedHandler = null!;
        Func<string, ValueTask> capturedBroadcastHandler = null!;

        // Capture the handlers passed to SubscribeAsync.
        channelService
            .SubscribeAsync(TargetedChannel, Arg.Do<Func<string, ValueTask>>(h => capturedTargetedHandler = h), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        channelService
            .SubscribeAsync(BroadcastChannel, Arg.Do<Func<string, ValueTask>>(h => capturedBroadcastHandler = h), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        channelService
            .UnsubscribeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(channelService);
        services.AddSingleton(cacheService);
        services.AddOptions<CachingCoreOptions>().Configure(o => o.ServiceName = ServiceName);
        services.AddLogging();

        var provider = services.BuildServiceProvider();

        var receiver = new CacheInvalidationReceiver(
            channelService,
            cacheService,
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CachingCoreOptions>>(),
            provider.GetRequiredService<ILogger<CacheInvalidationReceiver>>());

        // Trigger ExecuteAsync to register subscriptions.
        // Do NOT use 'using' â€” the CTS must remain alive so the captured stoppingToken
        // in the handler closures stays valid throughout the test.
        var cts = new CancellationTokenSource();
        _ = receiver.StartAsync(cts.Token);

        // Tiny synchronous spin to let the async SubscribeAsync calls complete
        // (they call Substitute which returns immediately).
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while ((capturedTargetedHandler is null || capturedBroadcastHandler is null)
               && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        return (receiver, cacheService, capturedTargetedHandler, capturedBroadcastHandler);
    }

    private static string Serialize(CacheInvalidationMessage message) =>
        JsonSerializer.Serialize(message, CacheInvalidationMessageJsonContext.Default.CacheInvalidationMessage);

    [Fact]
    public async Task HandleMessage_KeyInvalidation_CallsRemoveAsyncForEachKey()
    {
        var (receiver, cacheService, targetedHandler, _) = BuildReceiver();
        Assert.NotNull(targetedHandler);

        var message = new CacheInvalidationMessage(
            SourceService: ServiceName,
            InvalidationType: CacheInvalidationType.Key,
            Keys: ["key:1", "key:2"],
            Tags: null,
            CorrelationId: Guid.NewGuid().ToString("N"),
            TimestampUtc: DateTimeOffset.UtcNow);

        var json = Serialize(message);
        await targetedHandler(json);

        await cacheService.Received(1).RemoveAsync("key:1", Arg.Any<CancellationToken>());
        await cacheService.Received(1).RemoveAsync("key:2", Arg.Any<CancellationToken>());

        await receiver.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HandleMessage_TagInvalidation_CallsRemoveByTagAsyncForEachTag()
    {
        var (receiver, cacheService, targetedHandler, _) = BuildReceiver();
        Assert.NotNull(targetedHandler);

        var message = new CacheInvalidationMessage(
            SourceService: ServiceName,
            InvalidationType: CacheInvalidationType.Tag,
            Keys: null,
            Tags: ["tag-a", "tag-b"],
            CorrelationId: Guid.NewGuid().ToString("N"),
            TimestampUtc: DateTimeOffset.UtcNow);

        var json = Serialize(message);
        await targetedHandler(json);

        await cacheService.Received(1).RemoveByTagAsync("tag-a", Arg.Any<CancellationToken>());
        await cacheService.Received(1).RemoveByTagAsync("tag-b", Arg.Any<CancellationToken>());

        await receiver.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HandleMessage_BroadcastAll_DoesNotCallRemoveAnyKeys()
    {
        var (receiver, cacheService, _, broadcastHandler) = BuildReceiver();
        Assert.NotNull(broadcastHandler);

        var message = new CacheInvalidationMessage(
            SourceService: ServiceName,
            InvalidationType: CacheInvalidationType.All,
            Keys: null,
            Tags: null,
            CorrelationId: Guid.NewGuid().ToString("N"),
            TimestampUtc: DateTimeOffset.UtcNow);

        var json = Serialize(message);
        await broadcastHandler(json);

        // No RemoveAsync or RemoveByTagAsync calls for All invalidation.
        await cacheService.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await cacheService.DidNotReceive().RemoveByTagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        await receiver.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HandleMessage_InvalidJson_DoesNotThrow_ProcessingContinues()
    {
        var (receiver, cacheService, targetedHandler, _) = BuildReceiver();
        Assert.NotNull(targetedHandler);

        // Bad JSON â€” should be caught and swallowed.
        await targetedHandler("this is not json");

        // No cache operations should occur.
        await cacheService.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Subsequent valid message should still be processed.
        var message = new CacheInvalidationMessage(
            SourceService: ServiceName,
            InvalidationType: CacheInvalidationType.Key,
            Keys: ["key:after-error"],
            Tags: null,
            CorrelationId: Guid.NewGuid().ToString("N"),
            TimestampUtc: DateTimeOffset.UtcNow);

        await targetedHandler(Serialize(message));
        await cacheService.Received(1).RemoveAsync("key:after-error", Arg.Any<CancellationToken>());

        await receiver.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HandleMessage_CacheServiceThrows_ExceptionSwallowed()
    {
        var (receiver, cacheService, targetedHandler, _) = BuildReceiver();
        Assert.NotNull(targetedHandler);

        cacheService.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(ValueTask.FromException(new InvalidOperationException("cache failure")));

        var message = new CacheInvalidationMessage(
            SourceService: ServiceName,
            InvalidationType: CacheInvalidationType.Key,
            Keys: ["fail-key"],
            Tags: null,
            CorrelationId: Guid.NewGuid().ToString("N"),
            TimestampUtc: DateTimeOffset.UtcNow);

        // Should not throw.
        await targetedHandler(Serialize(message));

        await receiver.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HandleMessage_CreatesOTelActivity_WithExpectedTags()
    {
        var capturedTags = new Dictionary<string, object?>();
        var spanStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "SharedKernel.Caching.Invalidation",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                foreach (var tag in activity.TagObjects)
                    capturedTags[tag.Key] = tag.Value;
                spanStopped.TrySetResult(true);
            }
        };
        ActivitySource.AddActivityListener(listener);

        var (receiver, _, targetedHandler, _) = BuildReceiver();
        Assert.NotNull(targetedHandler);

        var correlationId = Guid.NewGuid().ToString("N");
        var message = new CacheInvalidationMessage(
            SourceService: ServiceName,
            InvalidationType: CacheInvalidationType.Key,
            Keys: ["otel-key"],
            Tags: null,
            CorrelationId: correlationId,
            TimestampUtc: DateTimeOffset.UtcNow);

        await targetedHandler(Serialize(message));

        await spanStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(capturedTags.ContainsKey("cache.invalidation.source"), "Expected cache.invalidation.source tag");
        Assert.True(capturedTags.ContainsKey("cache.invalidation.type"), "Expected cache.invalidation.type tag");
        Assert.True(capturedTags.ContainsKey("cache.invalidation.correlation_id"), "Expected cache.invalidation.correlation_id tag");
        Assert.Equal(correlationId, capturedTags["cache.invalidation.correlation_id"]);

        await receiver.StopAsync(CancellationToken.None);
    }
}

// ---------------------------------------------------------------------------
// Integration tests for DI registration and guard conditions
// ---------------------------------------------------------------------------

/// <summary>
/// Integration tests for <see cref="ICacheInvalidationBus"/> DI registration and
/// opt-in guard conditions. Uses Testcontainers for real Redis.
/// </summary>
[Collection("Redis")]
public sealed class CacheInvalidationDiIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private ServiceProvider? _provider;

    public async Task InitializeAsync() =>
        await _redisContainer.StartAsync();

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();

        await _redisContainer.DisposeAsync();
    }

    [Fact]
    public void AddRedisCacheInvalidationBus_WithoutChannelService_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(opts => opts.ServiceName = "svc");

        // Do NOT call AddRedisChannelService â€” guard should fire.
        var builder = new TestCachingBuilder(services);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.AddRedisCacheInvalidationBus());

        Assert.Contains("AddRedisChannelService", ex.Message);
    }

    [Fact]
    public void AddRedisCacheInvalidationBus_RegistersICacheInvalidationBus_AsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSharedKernelCaching(opts => opts.ServiceName = "svc")
                .AddRedisL2(_redisContainer.GetConnectionString())
                .AddRedisChannelService()
                .AddRedisCacheInvalidationBus();

        _provider = services.BuildServiceProvider();

        var first = _provider.GetRequiredService<ICacheInvalidationBus>();
        var second = _provider.GetRequiredService<ICacheInvalidationBus>();

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void AddCacheInvalidationReceiver_RegistersHostedService()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSharedKernelCaching(opts => opts.ServiceName = "svc")
                .AddRedisL2(_redisContainer.GetConnectionString())
                .AddRedisChannelService()
                .AddRedisCacheInvalidationBus()
                .AddCacheInvalidationReceiver();

        _provider = services.BuildServiceProvider();

        var hostedServices = _provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().ToList();
        Assert.Contains(hostedServices, hs => hs is CacheInvalidationReceiver);
    }

    [Fact]
    public async Task Receiver_SenderOffline_NoCrash()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSharedKernelCaching(opts => opts.ServiceName = "offline-svc")
                .AddRedisL2(_redisContainer.GetConnectionString())
                .AddRedisChannelService()
                .AddRedisCacheInvalidationBus()
                .AddCacheInvalidationReceiver();

        _provider = services.BuildServiceProvider();

        var receiver = _provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                                .OfType<CacheInvalidationReceiver>().Single();

        await receiver.StartAsync(CancellationToken.None);
        await Task.Delay(200);

        // No messages published â€” receiver should start and stop cleanly.
        await receiver.StopAsync(CancellationToken.None);
    }
}

// ---------------------------------------------------------------------------
// Integration test for broadcast warning log via real Redis
// ---------------------------------------------------------------------------

/// <summary>
/// Integration test verifying the broadcast warning log is emitted by the receiver
/// when a broadcast invalidation message is published over Redis.
/// </summary>
[Collection("Redis")]
public sealed class CacheInvalidationBroadcastIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private ServiceProvider? _provider;

    public async Task InitializeAsync() => await _redisContainer.StartAsync();

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    [Fact]
    public async Task PublishBroadcastInvalidationAsync_ReceiverLogsWarning()
    {
        var logMessages = new List<string>();

        var services = new ServiceCollection();
        services.AddLogging(b => b
            .AddProvider(new CapturingLoggerProvider(logMessages))
            .SetMinimumLevel(LogLevel.Warning));

        // Use RedisDistributedLocking for multiplexer only; avoid L2 FusionCache backplane
        // competing on the same Redis pub/sub channel.
        services.AddRedisDistributedLocking(_redisContainer.GetConnectionString());

        services.AddSharedKernelCaching(opts => opts.ServiceName = "broadcast-svc")
                .AddRedisChannelService()
                .AddRedisCacheInvalidationBus()
                .AddCacheInvalidationReceiver();

        _provider = services.BuildServiceProvider();

        var receiver = _provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                                .OfType<CacheInvalidationReceiver>().Single();

        await receiver.StartAsync(CancellationToken.None);

        // Wait for Redis subscriptions to fully establish.
        await Task.Delay(500);

        var bus = _provider.GetRequiredService<ICacheInvalidationBus>();
        await bus.PublishBroadcastInvalidationAsync();

        // Wait for message to be received and processed.
        await Task.Delay(1000);

        Assert.Contains(logMessages, m => m.Contains("Full L1 flush is not supported"));

        await receiver.StopAsync(CancellationToken.None);
    }
}

// ---------------------------------------------------------------------------
// Minimal capturing logger for broadcast warning test
// ---------------------------------------------------------------------------

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _messages;
    public CapturingLoggerProvider(List<string> messages) => _messages = messages;
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);
    public void Dispose() { }
}

internal sealed class CapturingLogger : ILogger
{
    private readonly List<string> _messages;
    public CapturingLogger(List<string> messages) => _messages = messages;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _messages.Add(formatter(state, exception));
    }
}

