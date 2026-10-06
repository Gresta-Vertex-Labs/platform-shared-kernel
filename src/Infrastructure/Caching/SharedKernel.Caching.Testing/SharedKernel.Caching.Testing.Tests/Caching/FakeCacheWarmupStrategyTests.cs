using System.Collections.Concurrent;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>
/// Proves <see cref="FakeCacheWarmupStrategy"/> against <c>ICacheWarmupStrategy</c>'s contract,
/// including a hand-rolled ordering/dispatch loop mirroring `CacheWarmupHostedService`'s documented
/// sort-by-`Order`/catch-per-strategy-exception/log-and-continue contract. This package never
/// references `CacheWarmupHostedService` itself — it lives in `02.Caching`, outside this package's
/// dependency set — so the loop is hand-rolled here, not borrowed from production.
/// </summary>
public sealed class FakeCacheWarmupStrategyTests
{
    [Fact]
    public void Constructor_NameAndOrder_AreFixed()
    {
        var strategy = new FakeCacheWarmupStrategy("my-strategy", order: 3);

        Assert.Equal("my-strategy", strategy.Name);
        Assert.Equal(3, strategy.Order);
    }

    [Fact]
    public async Task WarmupAsync_IncrementsCallCount()
    {
        var strategy = new FakeCacheWarmupStrategy("strategy-1");
        var cache = new FakeCacheService();

        await strategy.WarmupAsync(cache, CancellationToken.None);

        Assert.Equal(1, strategy.CallCount);
    }

    [Fact]
    public async Task WarmupAsync_InvokesOnWarmup_WithExactCacheServiceInstance()
    {
        var strategy = new FakeCacheWarmupStrategy("strategy-1");
        var cache = new FakeCacheService();
        ICacheService? received = null;
        strategy.OnWarmup = c => received = c;

        await strategy.WarmupAsync(cache, CancellationToken.None);

        Assert.Same(cache, received);
    }

    [Fact]
    public async Task WarmupAsync_SimulateFailure_ThrowsButStillRecordsCallAndLogEntry()
    {
        var log = new ConcurrentQueue<string>();
        var strategy = new FakeCacheWarmupStrategy("strategy-1", executionLog: log) { SimulateFailure = true };
        var cache = new FakeCacheService();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await strategy.WarmupAsync(cache, CancellationToken.None));

        Assert.Equal(1, strategy.CallCount);
        Assert.Equal(["strategy-1"], log);
    }

    [Fact]
    public async Task OrderingDispatchLoop_ThreeInstancesSharingOneQueue_MixedOrderAndFailure_AllRunExactlyOnceInOrder()
    {
        // Three instances share one ConcurrentQueue<string>, with mixed Order values and mixed
        // SimulateFailure — constructed out of Order to prove the dispatch loop (not construction
        // order) determines execution order.
        var log = new ConcurrentQueue<string>();
        var strategyCharlie = new FakeCacheWarmupStrategy("charlie", order: 2, executionLog: log);
        var strategyAlpha = new FakeCacheWarmupStrategy("alpha", order: 0, executionLog: log) { SimulateFailure = true };
        var strategyBravo = new FakeCacheWarmupStrategy("bravo", order: 1, executionLog: log);
        ICacheWarmupStrategy[] strategies = [strategyCharlie, strategyAlpha, strategyBravo];
        var cache = new FakeCacheService();

        // Hand-rolled loop mirroring CacheWarmupHostedService's documented contract: sort ascending
        // by Order, catch and continue past any strategy that throws — a failed strategy must never
        // abort the remaining strategies.
        foreach (var strategy in strategies.OrderBy(s => s.Order))
        {
            try
            {
                await strategy.WarmupAsync(cache, CancellationToken.None);
            }
            catch (Exception)
            {
                // Swallowed here, mirroring CacheWarmupHostedService logging at Error and continuing.
            }
        }

        Assert.Equal(["alpha", "bravo", "charlie"], log);
        Assert.Equal(1, strategyAlpha.CallCount);
        Assert.Equal(1, strategyBravo.CallCount);
        Assert.Equal(1, strategyCharlie.CallCount);
    }
}
