using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
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
    // Builds from FusionCache's own defaults, i.e. a cache with no service-wide CachingOptions defaults.
    private static FusionCacheEntryOptions Build(CachePolicy policy) =>
        FusionCacheService.BuildEntryOptions(policy, new FusionCacheEntryOptions());

    private static FusionCacheEntryOptions ServiceDefaults(
        TimeSpan? softTimeout = null,
        TimeSpan? hardTimeout = null,
        TimeSpan? throttle = null)
    {
        var defaults = new FusionCacheEntryOptions();
        CachingServiceCollectionExtensions.ApplyDefaults(defaults, new CachingOptions
        {
            ServiceName = "svc",
            DistributedCacheSoftTimeout = softTimeout,
            DistributedCacheHardTimeout = hardTimeout,
            FailSafeThrottleDuration = throttle,
        });
        return defaults;
    }

    // -------------------------------------------------------------------------
    // Service-wide defaults (CachingOptions → DefaultEntryOptions → every entry)
    // -------------------------------------------------------------------------

    [Fact]
    public void ApplyDefaults_SetsSizeAndEveryConfiguredDefault()
    {
        var defaults = ServiceDefaults(
            softTimeout: TimeSpan.FromMilliseconds(150),
            hardTimeout: TimeSpan.FromSeconds(2),
            throttle: TimeSpan.FromSeconds(45));

        Assert.Equal(1, defaults.Size);
        Assert.Equal(TimeSpan.FromMilliseconds(150), defaults.DistributedCacheSoftTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), defaults.DistributedCacheHardTimeout);
        Assert.Equal(TimeSpan.FromSeconds(45), defaults.FailSafeThrottleDuration);
    }

    [Fact]
    public void ApplyDefaults_UnsetOptions_KeepFusionCacheDefaults()
    {
        var fusionDefaults = new FusionCacheEntryOptions();

        var defaults = ServiceDefaults();

        Assert.Equal(fusionDefaults.DistributedCacheSoftTimeout, defaults.DistributedCacheSoftTimeout);
        Assert.Equal(fusionDefaults.DistributedCacheHardTimeout, defaults.DistributedCacheHardTimeout);
        Assert.Equal(fusionDefaults.FailSafeThrottleDuration, defaults.FailSafeThrottleDuration);
    }

    [Fact]
    public void BuildEntryOptions_InheritsDistributedTimeoutsAndFailSafeThrottle_FromDefaults()
    {
        var defaults = ServiceDefaults(
            softTimeout: TimeSpan.FromMilliseconds(100),
            hardTimeout: TimeSpan.FromSeconds(1),
            throttle: TimeSpan.FromMinutes(2));

        var options = FusionCacheService.BuildEntryOptions(CachePolicy.Default, defaults);

        Assert.Equal(TimeSpan.FromMilliseconds(100), options.DistributedCacheSoftTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), options.DistributedCacheHardTimeout);
        Assert.Equal(TimeSpan.FromMinutes(2), options.FailSafeThrottleDuration);
    }

    [Fact]
    public void BuildEntryOptions_PolicySettingsOverrideDefaults()
    {
        var defaults = ServiceDefaults(hardTimeout: TimeSpan.FromSeconds(1));
        defaults.Duration = TimeSpan.FromHours(3);
        defaults.IsFailSafeEnabled = true;
        defaults.EagerRefreshThreshold = 0.5f;
        defaults.FactorySoftTimeout = TimeSpan.FromSeconds(9);

        var policy = CachePolicy.For(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1))
            .WithoutFailSafe()
            .WithoutEagerRefresh()
            .WithFactoryTimeouts(null, TimeSpan.FromSeconds(3));

        var options = FusionCacheService.BuildEntryOptions(policy, defaults);

        Assert.Equal(TimeSpan.FromSeconds(10), options.Duration);
        Assert.Equal(TimeSpan.FromMinutes(1), options.DistributedCacheDuration);
        Assert.False(options.IsFailSafeEnabled);
        Assert.Equal(TimeSpan.FromSeconds(3), options.FactoryHardTimeout);
        // A policy without a soft timeout leaves the default's value in place; one without eager
        // refresh must not inherit a default threshold.
        Assert.Equal(TimeSpan.FromSeconds(9), options.FactorySoftTimeout);
        Assert.Null(options.EagerRefreshThreshold);
        // Not overridden by the policy: still the service-wide default.
        Assert.Equal(TimeSpan.FromSeconds(1), options.DistributedCacheHardTimeout);
    }

    [Fact]
    public void BuildEntryOptions_PolicyEagerRefresh_OverridesDefaultThreshold()
    {
        var defaults = new FusionCacheEntryOptions { EagerRefreshThreshold = 0.5f };

        var options = FusionCacheService.BuildEntryOptions(CachePolicy.Default.WithEagerRefresh(0.8), defaults);

        Assert.Equal(0.8f, options.EagerRefreshThreshold);
    }

    [Fact]
    public void BuildEntryOptions_DoesNotMutateTheDefaults()
    {
        var defaults = ServiceDefaults(softTimeout: TimeSpan.FromMilliseconds(100), hardTimeout: TimeSpan.FromSeconds(1));
        var durationBefore = defaults.Duration;

        var options = FusionCacheService.BuildEntryOptions(
            CachePolicy.For(TimeSpan.FromSeconds(7)).LocalOnly().WithoutFailSafe(),
            defaults);

        Assert.NotSame(defaults, options);
        Assert.Equal(durationBefore, defaults.Duration);
        Assert.False(defaults.SkipDistributedCacheWrite);
        Assert.Equal(TimeSpan.FromMilliseconds(100), defaults.DistributedCacheSoftTimeout);
    }

    // -------------------------------------------------------------------------
    // Durations
    // -------------------------------------------------------------------------

    [Fact]
    public void Default_MapsDurationsFailSafeEagerRefreshAndSize()
    {
        var options = Build(CachePolicy.Default);

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
        var options = Build(
            CachePolicy.For(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(7)));

        Assert.Equal(TimeSpan.FromSeconds(30), options.Duration);
        Assert.Equal(TimeSpan.FromMinutes(7), options.DistributedCacheDuration);
    }

    [Fact]
    public void NeverExpire_MapsMaxValueToInfiniteDurations_AndNoEagerRefresh()
    {
        var options = Build(CachePolicy.NeverExpire);

        Assert.Equal(TimeSpan.MaxValue, options.Duration);
        Assert.Equal(TimeSpan.MaxValue, options.DistributedCacheDuration);
        Assert.Null(options.EagerRefreshThreshold);
    }

    [Fact]
    public void MaxValueL2Only_MapsOnlyDistributedDurationToInfinite()
    {
        var options = Build(
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
        var options = Build(
            CachePolicy.Default.WithFailSafe(TimeSpan.FromHours(2)));

        Assert.True(options.IsFailSafeEnabled);
        Assert.Equal(TimeSpan.FromHours(2), options.FailSafeMaxDuration);
        Assert.Equal(TimeSpan.FromHours(2), options.DistributedCacheFailSafeMaxDuration);
    }

    [Fact]
    public void WithoutFailSafe_DisablesFailSafe()
    {
        var options = Build(CachePolicy.Default.WithoutFailSafe());

        Assert.False(options.IsFailSafeEnabled);
    }

    [Fact]
    public void FailSafeWithoutMaxDuration_KeepsFusionCacheDefaults()
    {
        var defaults = new FusionCacheEntryOptions();

        var options = Build(CachePolicy.Default);

        Assert.Equal(defaults.FailSafeMaxDuration, options.FailSafeMaxDuration);
        Assert.Equal(defaults.DistributedCacheFailSafeMaxDuration, options.DistributedCacheFailSafeMaxDuration);
    }

    [Fact]
    public void WithFactoryTimeouts_MapsSoftAndHardTimeouts()
    {
        var options = Build(
            CachePolicy.Default.WithFactoryTimeouts(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(2)));

        Assert.Equal(TimeSpan.FromMilliseconds(100), options.FactorySoftTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), options.FactoryHardTimeout);
    }

    [Fact]
    public void NoFactoryTimeouts_KeepsFusionCacheDefaults()
    {
        var defaults = new FusionCacheEntryOptions();

        var options = Build(CachePolicy.Default.WithFactoryTimeouts(null, null));

        Assert.Equal(defaults.FactorySoftTimeout, options.FactorySoftTimeout);
        Assert.Equal(defaults.FactoryHardTimeout, options.FactoryHardTimeout);
    }

    // -------------------------------------------------------------------------
    // Eager refresh and jitter
    // -------------------------------------------------------------------------

    [Fact]
    public void WithEagerRefresh_MapsThreshold()
    {
        var options = Build(CachePolicy.Default.WithEagerRefresh(0.75));

        Assert.Equal(0.75f, options.EagerRefreshThreshold);
    }

    [Fact]
    public void WithoutEagerRefresh_LeavesThresholdUnset()
    {
        var options = Build(CachePolicy.Default.WithoutEagerRefresh());

        Assert.Null(options.EagerRefreshThreshold);
    }

    [Fact]
    public void WithJitter_MapsJitterMaxDuration()
    {
        var options = Build(CachePolicy.Default.WithJitter(TimeSpan.FromSeconds(15)));

        Assert.Equal(TimeSpan.FromSeconds(15), options.JitterMaxDuration);
    }

    [Fact]
    public void NoJitter_KeepsJitterAtZero()
    {
        var options = Build(CachePolicy.Default);

        Assert.Equal(TimeSpan.Zero, options.JitterMaxDuration);
    }

    // -------------------------------------------------------------------------
    // LocalOnly
    // -------------------------------------------------------------------------

    [Fact]
    public void LocalOnly_SkipsDistributedReadWrite_AndBackplaneNotifications()
    {
        var options = Build(CachePolicy.Default.LocalOnly());

        Assert.True(options.SkipDistributedCacheRead);
        Assert.True(options.SkipDistributedCacheWrite);
        Assert.True(options.SkipBackplaneNotifications);
        Assert.False(options.SkipMemoryCacheWrite);
    }

    [Fact]
    public void EachCall_ReturnsIndependentOptionsInstance()
    {
        var first = Build(CachePolicy.Default);
        var second = Build(CachePolicy.Default);

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
        var options = Build(policy);

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
        var options = Build(policy);
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
        var options = Build(policy);
        var context = new CacheFactoryContext("k", policy);
        context.SetDurations(TimeSpan.FromSeconds(5), TimeSpan.MaxValue);

        FusionCacheService.ApplyFactoryDecision(context, options);

        Assert.Equal(TimeSpan.FromSeconds(5), options.Duration);
        Assert.Equal(TimeSpan.MaxValue, options.DistributedCacheDuration);
    }
}
