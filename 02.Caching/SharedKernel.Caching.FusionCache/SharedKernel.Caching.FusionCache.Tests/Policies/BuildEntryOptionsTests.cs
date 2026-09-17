using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Implementations;
using ZiggyCreatures.Caching.Fusion;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests.Policies;

/// <summary>
/// Tests for the mapping of <see cref="CachePolicy"/> onto FusionCache entry options
/// (<see cref="FusionCacheService.BuildEntryOptions"/>) and of a factory's
/// <see cref="CacheFactoryContext"/> decisions onto the options of the write
/// (<see cref="FusionCacheService.ApplyFactoryDecision"/>).
/// </summary>
/// <remarks>
/// The policy type itself (defaults, fluent validation, equality) is covered by
/// <c>SharedKernel.Caching.Abstractions.Tests</c>.
/// </remarks>
public sealed class BuildEntryOptionsTests
{
    // -------------------------------------------------------------------------
    // Durations
    // -------------------------------------------------------------------------

    [Fact]
    public void Default_MapsDurationsFailSafeEagerRefreshAndSize()
    {
        var options = FusionCacheService.BuildEntryOptions(CachePolicy.Default);

        Assert.Equal(TimeSpan.FromMinutes(5), options.Duration);
        Assert.Equal(TimeSpan.FromMinutes(30), options.DistributedCacheDuration);
        Assert.True(options.IsFailSafeEnabled);
        Assert.Equal(0.9f, options.EagerRefreshThreshold);
        Assert.Equal(1, options.Size);
        Assert.False(options.SkipDistributedCacheRead);
        Assert.False(options.SkipDistributedCacheWrite);
        Assert.False(options.SkipBackplaneNotifications);
        Assert.False(options.SkipMemoryCacheWrite);
    }

    [Fact]
    public void For_MapsL1ToDuration_AndL2ToDistributedCacheDuration()
    {
        var options = FusionCacheService.BuildEntryOptions(
            CachePolicy.For(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(7)));

        Assert.Equal(TimeSpan.FromSeconds(30), options.Duration);
        Assert.Equal(TimeSpan.FromMinutes(7), options.DistributedCacheDuration);
    }

    [Fact]
    public void NeverExpire_MapsMaxValueToInfiniteDurations_AndNoEagerRefresh()
    {
        var options = FusionCacheService.BuildEntryOptions(CachePolicy.NeverExpire);

        Assert.Equal(TimeSpan.MaxValue, options.Duration);
        Assert.Equal(TimeSpan.MaxValue, options.DistributedCacheDuration);
        Assert.Null(options.EagerRefreshThreshold);
    }

    [Fact]
    public void MaxValueL2Only_MapsOnlyDistributedDurationToInfinite()
    {
        var options = FusionCacheService.BuildEntryOptions(
            CachePolicy.For(TimeSpan.FromMinutes(1), TimeSpan.MaxValue));

        Assert.Equal(TimeSpan.FromMinutes(1), options.Duration);
        Assert.Equal(TimeSpan.MaxValue, options.DistributedCacheDuration);
    }

    // -------------------------------------------------------------------------
    // Fail-safe and factory timeouts
    // -------------------------------------------------------------------------

    [Fact]
    public void WithFailSafe_MapsMaxDurationToMemoryAndDistributedFailSafe()
    {
        var options = FusionCacheService.BuildEntryOptions(
            CachePolicy.Default.WithFailSafe(TimeSpan.FromHours(2)));

        Assert.True(options.IsFailSafeEnabled);
        Assert.Equal(TimeSpan.FromHours(2), options.FailSafeMaxDuration);
        Assert.Equal(TimeSpan.FromHours(2), options.DistributedCacheFailSafeMaxDuration);
    }

    [Fact]
    public void WithoutFailSafe_DisablesFailSafe()
    {
        var options = FusionCacheService.BuildEntryOptions(CachePolicy.Default.WithoutFailSafe());

        Assert.False(options.IsFailSafeEnabled);
    }

    [Fact]
    public void FailSafeWithoutMaxDuration_KeepsFusionCacheDefaults()
    {
        var defaults = new FusionCacheEntryOptions();

        var options = FusionCacheService.BuildEntryOptions(CachePolicy.Default);

        Assert.Equal(defaults.FailSafeMaxDuration, options.FailSafeMaxDuration);
        Assert.Equal(defaults.DistributedCacheFailSafeMaxDuration, options.DistributedCacheFailSafeMaxDuration);
    }

    [Fact]
    public void WithFactoryTimeouts_MapsSoftAndHardTimeouts()
    {
        var options = FusionCacheService.BuildEntryOptions(
            CachePolicy.Default.WithFactoryTimeouts(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(2)));

        Assert.Equal(TimeSpan.FromMilliseconds(100), options.FactorySoftTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), options.FactoryHardTimeout);
    }

    [Fact]
    public void NoFactoryTimeouts_KeepsFusionCacheDefaults()
    {
        var defaults = new FusionCacheEntryOptions();

        var options = FusionCacheService.BuildEntryOptions(CachePolicy.Default.WithFactoryTimeouts(null, null));

        Assert.Equal(defaults.FactorySoftTimeout, options.FactorySoftTimeout);
        Assert.Equal(defaults.FactoryHardTimeout, options.FactoryHardTimeout);
    }

    // -------------------------------------------------------------------------
    // Eager refresh and jitter
    // -------------------------------------------------------------------------

    [Fact]
    public void WithEagerRefresh_MapsThreshold()
    {
        var options = FusionCacheService.BuildEntryOptions(CachePolicy.Default.WithEagerRefresh(0.75));

        Assert.Equal(0.75f, options.EagerRefreshThreshold);
    }

    [Fact]
    public void WithoutEagerRefresh_LeavesThresholdUnset()
    {
        var options = FusionCacheService.BuildEntryOptions(CachePolicy.Default.WithoutEagerRefresh());

        Assert.Null(options.EagerRefreshThreshold);
    }

    [Fact]
    public void WithJitter_MapsJitterMaxDuration()
    {
        var options = FusionCacheService.BuildEntryOptions(CachePolicy.Default.WithJitter(TimeSpan.FromSeconds(15)));

        Assert.Equal(TimeSpan.FromSeconds(15), options.JitterMaxDuration);
    }

    [Fact]
    public void NoJitter_KeepsJitterAtZero()
    {
        var options = FusionCacheService.BuildEntryOptions(CachePolicy.Default);

        Assert.Equal(TimeSpan.Zero, options.JitterMaxDuration);
    }

    // -------------------------------------------------------------------------
    // LocalOnly
    // -------------------------------------------------------------------------

    [Fact]
    public void LocalOnly_SkipsDistributedReadWrite_AndBackplaneNotifications()
    {
        var options = FusionCacheService.BuildEntryOptions(CachePolicy.Default.LocalOnly());

        Assert.True(options.SkipDistributedCacheRead);
        Assert.True(options.SkipDistributedCacheWrite);
        Assert.True(options.SkipBackplaneNotifications);
        Assert.False(options.SkipMemoryCacheWrite);
    }

    [Fact]
    public void EachCall_ReturnsIndependentOptionsInstance()
    {
        var first = FusionCacheService.BuildEntryOptions(CachePolicy.Default);
        var second = FusionCacheService.BuildEntryOptions(CachePolicy.Default);

        first.SkipMemoryCacheWrite = true;

        Assert.NotSame(first, second);
        Assert.False(second.SkipMemoryCacheWrite);
    }

    // -------------------------------------------------------------------------
    // ApplyFactoryDecision
    // -------------------------------------------------------------------------

    [Fact]
    public void ApplyFactoryDecision_NoDecision_LeavesOptionsUnchanged()
    {
        var policy = CachePolicy.For(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));
        var options = FusionCacheService.BuildEntryOptions(policy);

        FusionCacheService.ApplyFactoryDecision(new CacheFactoryContext("k", policy), options);

        Assert.Equal(TimeSpan.FromMinutes(1), options.Duration);
        Assert.Equal(TimeSpan.FromMinutes(10), options.DistributedCacheDuration);
        Assert.False(options.SkipMemoryCacheWrite);
        Assert.False(options.SkipDistributedCacheWrite);
        Assert.False(options.SkipBackplaneNotifications);
    }

    [Fact]
    public void ApplyFactoryDecision_SkipCaching_SkipsEveryWrite()
    {
        var policy = CachePolicy.Default;
        var options = FusionCacheService.BuildEntryOptions(policy);
        var context = new CacheFactoryContext("k", policy);
        context.SkipCaching();

        FusionCacheService.ApplyFactoryDecision(context, options);

        Assert.True(options.SkipMemoryCacheWrite);
        Assert.True(options.SkipDistributedCacheWrite);
        Assert.True(options.SkipBackplaneNotifications);
    }

    [Fact]
    public void ApplyFactoryDecision_SetDurations_OverridesBothDurations()
    {
        var policy = CachePolicy.For(TimeSpan.FromHours(1));
        var options = FusionCacheService.BuildEntryOptions(policy);
        var context = new CacheFactoryContext("k", policy);
        context.SetDurations(TimeSpan.FromSeconds(5), TimeSpan.MaxValue);

        FusionCacheService.ApplyFactoryDecision(context, options);

        Assert.Equal(TimeSpan.FromSeconds(5), options.Duration);
        Assert.Equal(TimeSpan.MaxValue, options.DistributedCacheDuration);
    }
}
