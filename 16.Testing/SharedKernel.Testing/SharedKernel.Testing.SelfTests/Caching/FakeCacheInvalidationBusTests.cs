using SharedKernel.Caching.Abstractions;
using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>
/// Proves <see cref="FakeCacheInvalidationBus"/> against <c>ICacheInvalidationBus</c>'s contract —
/// no existing `02.Caching` consuming-domain test (e.g. `SharedKernel.Caching.Redis.PubSub.Tests`)
/// currently exercises this fake's recorder/handler behavior directly, so coverage is provided here
/// per the documented SelfTests fallback.
/// </summary>
public sealed class FakeCacheInvalidationBusTests
{
    [Fact]
    public async Task PublishKeyInvalidationAsync_RecordsMessage()
    {
        var bus = new FakeCacheInvalidationBus();
        await bus.PublishKeyInvalidationAsync(["k1", "k2"]);

        Assert.Single(bus.PublishedInvalidations);
        Assert.Equal(CacheInvalidationType.Key, bus.PublishedInvalidations[0].InvalidationType);
        Assert.Equal(["k1", "k2"], bus.PublishedInvalidations[0].Keys ?? []);
    }

    [Fact]
    public async Task PublishTagInvalidationAsync_RecordsMessage()
    {
        var bus = new FakeCacheInvalidationBus();
        await bus.PublishTagInvalidationAsync(["t1"]);

        Assert.Equal(CacheInvalidationType.Tag, bus.PublishedInvalidations[0].InvalidationType);
    }

    [Fact]
    public async Task PublishBroadcastInvalidationAsync_RecordsMessage()
    {
        var bus = new FakeCacheInvalidationBus();
        await bus.PublishBroadcastInvalidationAsync();

        Assert.Equal(CacheInvalidationType.All, bus.PublishedInvalidations[0].InvalidationType);
    }

    [Fact]
    public async Task OnInvalidation_HandlerInvokedSynchronously_OnEveryPublish()
    {
        var bus = new FakeCacheInvalidationBus();
        var received = new List<CacheInvalidationMessage>();
        bus.OnInvalidation(msg =>
        {
            received.Add(msg);
            return ValueTask.CompletedTask;
        });

        await bus.PublishKeyInvalidationAsync(["k1"]);
        await bus.PublishBroadcastInvalidationAsync();

        Assert.Equal(2, received.Count);
    }

    [Fact]
    public async Task Reset_ClearsMessagesAndHandlers()
    {
        var bus = new FakeCacheInvalidationBus();
        var handlerCalls = 0;
        bus.OnInvalidation(_ =>
        {
            handlerCalls++;
            return ValueTask.CompletedTask;
        });
        await bus.PublishKeyInvalidationAsync(["k1"]);

        bus.Reset();
        await bus.PublishKeyInvalidationAsync(["k2"]);

        Assert.Single(bus.PublishedInvalidations);
        Assert.Equal(1, handlerCalls);
    }

    [Fact]
    public async Task PublishKeyInvalidationAsync_NullKeys_Throws()
    {
        var bus = new FakeCacheInvalidationBus();
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await bus.PublishKeyInvalidationAsync(null!));
    }
}
