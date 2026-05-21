using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests.Policies;

/// <summary>
/// Tests for <see cref="CachePolicy.Sliding"/> factory method, the
/// <see cref="CachePolicy.SlidingWindow"/> property, and the NeverExpire + SlidingWindow
/// guard in <c>BuildEntryOptions</c>.
/// </summary>
public sealed class SlidingExpirationTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ICacheService _cache;

    public SlidingExpirationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching();
        _provider = services.BuildServiceProvider();
        _cache = _provider.GetRequiredService<ICacheService>();
    }

    public void Dispose() => _provider.Dispose();

    // -------------------------------------------------------------------------
    // CachePolicy.SlidingWindow default values
    // -------------------------------------------------------------------------

    [Fact]
    public void Default_HasNullSlidingWindow()
    {
        Assert.Null(CachePolicy.Default.SlidingWindow);
    }

    [Fact]
    public void NeverExpire_HasNullSlidingWindow()
    {
        Assert.Null(CachePolicy.NeverExpire.SlidingWindow);
    }

    [Fact]
    public void For_ReturnsPolicy_WithNullSlidingWindow()
    {
        var policy = CachePolicy.For(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        Assert.Null(policy.SlidingWindow);
    }

    // -------------------------------------------------------------------------
    // CachePolicy.Sliding() factory — property correctness (SE-02)
    // -------------------------------------------------------------------------

    [Fact]
    public void Sliding_ReturnsPolicyWithSlidingWindowSet()
    {
        var window = TimeSpan.FromSeconds(30);
        var policy = CachePolicy.Sliding(window);

        Assert.Equal(window, policy.SlidingWindow);
    }

    [Fact]
    public void Sliding_UsesDefaultL1Duration()
    {
        var policy = CachePolicy.Sliding(TimeSpan.FromSeconds(30));

        Assert.Equal(CachePolicy.Default.L1Duration, policy.L1Duration);
    }

    [Fact]
    public void Sliding_UsesDefaultL2Duration()
    {
        var policy = CachePolicy.Sliding(TimeSpan.FromSeconds(30));

        Assert.Equal(CachePolicy.Default.L2Duration, policy.L2Duration);
    }

    [Fact]
    public void Sliding_HasFailSafeEnabled()
    {
        var policy = CachePolicy.Sliding(TimeSpan.FromSeconds(30));

        Assert.True(policy.FailSafeEnabled);
    }

    [Fact]
    public void Sliding_HasEmptyTags()
    {
        var policy = CachePolicy.Sliding(TimeSpan.FromSeconds(30));

        Assert.Empty(policy.Tags);
    }

    [Fact]
    public void Sliding_ThrowsWhenWindowIsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CachePolicy.Sliding(TimeSpan.Zero));
    }

    [Fact]
    public void Sliding_ThrowsWhenWindowIsNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CachePolicy.Sliding(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Sliding_SlidingWindowIsSmallerThanAbsoluteL1Ceiling()
    {
        // The absolute ceiling (L1Duration) must be >= the idle window.
        var window = TimeSpan.FromSeconds(30);
        var policy = CachePolicy.Sliding(window);

        Assert.True(policy.SlidingWindow < policy.L1Duration,
            "SlidingWindow should be less than the absolute L1Duration ceiling.");
    }

    // -------------------------------------------------------------------------
    // Fluent chaining compatibility (rule 6)
    // -------------------------------------------------------------------------

    [Fact]
    public void Sliding_WithTagsChaining_PreservesSlidingWindowAndTags()
    {
        var window = TimeSpan.FromSeconds(45);
        var policy = CachePolicy.Sliding(window).WithTags("tenant:1");

        Assert.Equal(window, policy.SlidingWindow);
        Assert.Equal(["tenant:1"], policy.Tags);
        Assert.Equal(CachePolicy.Default.L1Duration, policy.L1Duration);
        Assert.Equal(CachePolicy.Default.L2Duration, policy.L2Duration);
    }

    [Fact]
    public void Sliding_WithTagsDoesNotMutateOriginal()
    {
        var original = CachePolicy.Sliding(TimeSpan.FromSeconds(30));
        _ = original.WithTags("x");

        Assert.Empty(original.Tags);
        Assert.Equal(TimeSpan.FromSeconds(30), original.SlidingWindow);
    }

    [Fact]
    public void Sliding_WithEagerRefreshChaining_PreservesSlidingWindowAndThreshold()
    {
        var window = TimeSpan.FromSeconds(60);
        var policy = CachePolicy.Sliding(window).WithEagerRefresh(0.8);

        Assert.Equal(window, policy.SlidingWindow);
        Assert.Equal(0.8, policy.EagerRefreshThreshold);
    }

    [Fact]
    public void Sliding_WithoutEagerRefreshChaining_PreservesSlidingWindow()
    {
        var window = TimeSpan.FromSeconds(60);
        var policy = CachePolicy.Sliding(window).WithoutEagerRefresh();

        Assert.Equal(window, policy.SlidingWindow);
        Assert.Null(policy.EagerRefreshThreshold);
    }

    // -------------------------------------------------------------------------
    // NeverExpire + SlidingWindow validation guard (SE-04)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NeverExpire_UsedAlone_DoesNotThrow()
    {
        // NeverExpire has no SlidingWindow — the guard should not fire.
        await _cache.SetAsync("test:never-expire-guard", "value", CachePolicy.NeverExpire);
        var result = await _cache.GetAsync<string>("test:never-expire-guard");
        Assert.Equal("value", result);
    }

    [Fact]
    public async Task Sliding_UsedAlone_DoesNotThrow()
    {
        // Sliding() uses Default.L1Duration (not MaxValue) — the guard should not fire.
        await _cache.SetAsync("test:sliding-guard", "value",
            CachePolicy.Sliding(TimeSpan.FromSeconds(30)));
        var result = await _cache.GetAsync<string>("test:sliding-guard");
        Assert.Equal("value", result);
    }

    // -------------------------------------------------------------------------
    // Idle expiry behaviour — entry expires after window with no access (SE-05)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SlidingPolicy_EntryIsReturnedBeforeWindowExpires()
    {
        const string key = "test:sliding:before-expiry";
        const string value = "alive";

        var policy = CachePolicy.Sliding(TimeSpan.FromSeconds(5));
        await _cache.SetAsync(key, value, policy);

        // Immediately after set — should still be present.
        var result = await _cache.GetAsync<string>(key);
        Assert.Equal(value, result);
    }

    [Fact]
    public async Task SlidingPolicy_EntryIsGoneAfterWindowElapses_WithoutAccess()
    {
        var uniqueKey = "test:sliding:expired-" + Guid.NewGuid().ToString("N");
        const string value = "will-expire";

        // Use a very short sliding window so the test finishes quickly.
        // FusionCache approximation sets MemoryCacheDuration = SlidingWindow (200 ms).
        var policy = CachePolicy.Sliding(TimeSpan.FromMilliseconds(200));
        await _cache.SetAsync(uniqueKey, value, policy);

        // Wait longer than the window with no access.
        await Task.Delay(TimeSpan.FromMilliseconds(600));

        var result = await _cache.GetAsync<string>(uniqueKey);
        // The L1 entry has expired; no L2 in this test setup → should be null.
        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // Absolute ceiling applies (L1Duration takes precedence over SlidingWindow)
    // -------------------------------------------------------------------------

    [Fact]
    public void Sliding_AbsoluteCeilingMatchesDefaultL1Duration()
    {
        // When SlidingWindow is set, the absolute L1 ceiling is L1Duration (Default = 5 min).
        var policy = CachePolicy.Sliding(TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromMinutes(5), policy.L1Duration);
    }
}
