using SharedKernel.Caching.Policies;
using Xunit;

namespace SharedKernel.Caching.Tests.Policies;

/// <summary>
/// Tests that verify the CachePolicy record shape, default values, factory methods,
/// and immutability guarantee.
/// </summary>
public sealed class CachePolicyTests
{
    // -------------------------------------------------------------------------
    // Default preset
    // -------------------------------------------------------------------------

    [Fact]
    public void Default_HasExpectedL1Duration()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), CachePolicy.Default.L1Duration);
    }

    [Fact]
    public void Default_HasExpectedL2Duration()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), CachePolicy.Default.L2Duration);
    }

    [Fact]
    public void Default_HasFailSafeEnabled()
    {
        Assert.True(CachePolicy.Default.FailSafeEnabled);
    }

    [Fact]
    public void Default_HasEmptyTags()
    {
        Assert.Empty(CachePolicy.Default.Tags);
    }

    [Fact]
    public void Default_HasEagerRefreshThresholdOf90Percent()
    {
        Assert.Equal(0.9, CachePolicy.Default.EagerRefreshThreshold);
    }

    // -------------------------------------------------------------------------
    // For() factory
    // -------------------------------------------------------------------------

    [Fact]
    public void For_ReturnsNewPolicyWithSpecifiedDurations()
    {
        var l1 = TimeSpan.FromMinutes(2);
        var l2 = TimeSpan.FromMinutes(10);

        var policy = CachePolicy.For(l1, l2);

        Assert.Equal(l1, policy.L1Duration);
        Assert.Equal(l2, policy.L2Duration);
    }

    [Fact]
    public void For_RetainsDefaultFailSafeAndEagerRefresh()
    {
        var policy = CachePolicy.For(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));

        Assert.True(policy.FailSafeEnabled);
        Assert.Equal(0.9, policy.EagerRefreshThreshold);
    }

    [Fact]
    public void For_ThrowsWhenL1IsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CachePolicy.For(TimeSpan.Zero, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void For_ThrowsWhenL1IsNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CachePolicy.For(TimeSpan.FromSeconds(-1), TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void For_ThrowsWhenL2IsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CachePolicy.For(TimeSpan.FromMinutes(1), TimeSpan.Zero));
    }

    [Fact]
    public void For_ThrowsWhenL2IsNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CachePolicy.For(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(-1)));
    }

    // -------------------------------------------------------------------------
    // WithTags() factory
    // -------------------------------------------------------------------------

    [Fact]
    public void WithTags_ReturnsNewPolicyWithSpecifiedTags()
    {
        var policy = CachePolicy.Default.WithTags("tenant:1", "entity:invoice");

        Assert.Equal(["tenant:1", "entity:invoice"], policy.Tags);
    }

    [Fact]
    public void WithTags_DoesNotMutateOriginalPolicy()
    {
        var original = CachePolicy.Default;
        _ = original.WithTags("tag1");

        Assert.Empty(original.Tags);
    }

    [Fact]
    public void WithTags_ThrowsWhenNoTagsSupplied()
    {
        Assert.Throws<ArgumentException>(() => CachePolicy.Default.WithTags());
    }

    // -------------------------------------------------------------------------
    // WithEagerRefresh() factory
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.8)]
    [InlineData(0.99)]
    public void WithEagerRefresh_ReturnsNewPolicyWithSpecifiedThreshold(double threshold)
    {
        var policy = CachePolicy.Default.WithEagerRefresh(threshold);

        Assert.Equal(threshold, policy.EagerRefreshThreshold);
    }

    [Fact]
    public void WithEagerRefresh_DoesNotMutateOriginalPolicy()
    {
        var original = CachePolicy.Default;
        _ = original.WithEagerRefresh(0.5);

        Assert.Equal(0.9, original.EagerRefreshThreshold);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void WithEagerRefresh_ThrowsWhenThresholdIsOutOfRange(double threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CachePolicy.Default.WithEagerRefresh(threshold));
    }

    // -------------------------------------------------------------------------
    // WithoutEagerRefresh()
    // -------------------------------------------------------------------------

    [Fact]
    public void WithoutEagerRefresh_SetsThresholdToNull()
    {
        var policy = CachePolicy.Default.WithoutEagerRefresh();

        Assert.Null(policy.EagerRefreshThreshold);
    }

    [Fact]
    public void WithoutEagerRefresh_DoesNotMutateOriginalPolicy()
    {
        var original = CachePolicy.Default;
        _ = original.WithoutEagerRefresh();

        Assert.Equal(0.9, original.EagerRefreshThreshold);
    }

    // -------------------------------------------------------------------------
    // WithFailSafeDisabled()
    // -------------------------------------------------------------------------

    [Fact]
    public void WithFailSafeDisabled_SetsFailSafeToFalse()
    {
        var policy = CachePolicy.Default.WithFailSafeDisabled();

        Assert.False(policy.FailSafeEnabled);
    }

    [Fact]
    public void WithFailSafeDisabled_DoesNotMutateOriginalPolicy()
    {
        var original = CachePolicy.Default;
        _ = original.WithFailSafeDisabled();

        Assert.True(original.FailSafeEnabled);
    }

    // -------------------------------------------------------------------------
    // Chaining
    // -------------------------------------------------------------------------

    [Fact]
    public void ChainedFactoryMethods_ProduceCorrectPolicy()
    {
        var policy = CachePolicy
            .For(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5))
            .WithTags("tenant:99")
            .WithEagerRefresh(0.75);

        Assert.Equal(TimeSpan.FromMinutes(1), policy.L1Duration);
        Assert.Equal(TimeSpan.FromMinutes(5), policy.L2Duration);
        Assert.Equal(["tenant:99"], policy.Tags);
        Assert.Equal(0.75, policy.EagerRefreshThreshold);
    }

    // -------------------------------------------------------------------------
    // Record equality
    // -------------------------------------------------------------------------

    [Fact]
    public void TwoPoliciesWithSameValues_AreEqual()
    {
        var a = CachePolicy.For(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(15));
        var b = CachePolicy.For(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(15));

        Assert.Equal(a, b);
    }

    [Fact]
    public void TwoPoliciesWithDifferentL1_AreNotEqual()
    {
        var a = CachePolicy.For(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(15));
        var b = CachePolicy.For(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(15));

        Assert.NotEqual(a, b);
    }
}
