using SharedKernel.Execution.Tenancy;
using Xunit;

namespace SharedKernel.Caching.Abstractions.Tests;

public sealed class CachePolicyTests
{
    private static readonly TenantId TenantA = new(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));
    private static readonly TenantId TenantB = new(Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"));

    [Fact]
    public void Default_HasDocumentedSettings()
    {
        CachePolicy policy = CachePolicy.Default;

        Assert.Equal(TimeSpan.FromMinutes(5), policy.L1Duration);
        Assert.Equal(TimeSpan.FromMinutes(30), policy.L2Duration);
        Assert.True(policy.IsFailSafeEnabled);
        Assert.Null(policy.FailSafeMaxDuration);
        Assert.Null(policy.FactorySoftTimeout);
        Assert.Null(policy.FactoryHardTimeout);
        Assert.Equal(0.9, policy.EagerRefreshThreshold);
        Assert.Null(policy.JitterMaxDuration);
        Assert.False(policy.IsLocalOnly);
        Assert.False(policy.IsTenantScoped);
        Assert.Empty(policy.Tags);
    }

    [Fact]
    public void NeverExpire_HasInfiniteDurationsAndNoEagerRefresh()
    {
        Assert.Equal(TimeSpan.MaxValue, CachePolicy.NeverExpire.L1Duration);
        Assert.Equal(TimeSpan.MaxValue, CachePolicy.NeverExpire.L2Duration);
        Assert.Null(CachePolicy.NeverExpire.EagerRefreshThreshold);
    }

    [Fact]
    public void For_SetsDurations()
    {
        CachePolicy policy = CachePolicy.For(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1));

        Assert.Equal(TimeSpan.FromSeconds(10), policy.L1Duration);
        Assert.Equal(TimeSpan.FromMinutes(1), policy.L2Duration);
        Assert.Equal(TimeSpan.FromSeconds(7), CachePolicy.For(TimeSpan.FromSeconds(7)).L2Duration);
    }

    [Fact]
    public void For_NonPositiveDuration_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CachePolicy.For(TimeSpan.Zero, TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CachePolicy.For(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void For_L1LongerThanL2_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CachePolicy.For(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(1)));

    [Fact]
    public void WithTags_CopiesCallerArray()
    {
        string[] tags = ["orders", "invoices"];
        CachePolicy policy = CachePolicy.Default.WithTags(tags);

        tags[0] = "mutated";

        Assert.Equal(["orders", "invoices"], policy.Tags);
    }

    [Fact]
    public void WithTags_DoesNotChangeOriginal()
    {
        CachePolicy.Default.WithTags("orders");

        Assert.Empty(CachePolicy.Default.Tags);
    }

    [Fact]
    public void WithTags_Empty_Throws() =>
        Assert.Throws<ArgumentException>(() => CachePolicy.Default.WithTags());

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("@tenant-a:orders")]
    public void WithTags_InvalidTag_Throws(string tag) =>
        Assert.Throws<ArgumentException>(() => CachePolicy.Default.WithTags("ok", tag));

    [Fact]
    public void WithTags_NullElement_Throws() =>
        Assert.Throws<ArgumentException>(() => CachePolicy.Default.WithTags("ok", null!));

    [Fact]
    public void ForTenant_ScopesTagsAndAddsTenantWideTag()
    {
        CachePolicy policy = CachePolicy.Default.WithTags("orders").ForTenant(TenantA);

        Assert.True(policy.IsTenantScoped);
        Assert.Equal([$"@{TenantA}:orders", $"@{TenantA}"], policy.Tags);
    }

    [Fact]
    public void ForTenant_WithoutTags_AddsOnlyTenantWideTag() =>
        Assert.Equal([$"@{TenantA}"], CachePolicy.Default.ForTenant(TenantA).Tags);

    [Fact]
    public void ForTenant_DefaultTenant_Throws() =>
        Assert.Throws<ArgumentException>(() => CachePolicy.Default.ForTenant(default));

    [Fact]
    public void ForTenant_Twice_Throws() =>
        Assert.Throws<InvalidOperationException>(() => CachePolicy.Default.ForTenant(TenantA).ForTenant(TenantB));

    [Fact]
    public void WithTags_AfterForTenant_Throws() =>
        Assert.Throws<InvalidOperationException>(() => CachePolicy.Default.ForTenant(TenantA).WithTags("orders"));

    [Fact]
    public void WithFailSafe_EnablesWithMaxDuration()
    {
        CachePolicy policy = CachePolicy.Default.WithoutFailSafe().WithFailSafe(TimeSpan.FromHours(1));

        Assert.True(policy.IsFailSafeEnabled);
        Assert.Equal(TimeSpan.FromHours(1), policy.FailSafeMaxDuration);
    }

    [Fact]
    public void WithoutFailSafe_ClearsSoftTimeout()
    {
        CachePolicy policy = CachePolicy.Default
            .WithFactoryTimeouts(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(2))
            .WithoutFailSafe();

        Assert.False(policy.IsFailSafeEnabled);
        Assert.Null(policy.FactorySoftTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), policy.FactoryHardTimeout);
    }

    [Fact]
    public void WithFactoryTimeouts_SetsBoth()
    {
        CachePolicy policy = CachePolicy.Default.WithFactoryTimeouts(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromMilliseconds(100), policy.FactorySoftTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), policy.FactoryHardTimeout);
    }

    [Fact]
    public void WithFactoryTimeouts_SoftNotShorterThanHard_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CachePolicy.Default.WithFactoryTimeouts(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)));

    [Fact]
    public void WithFactoryTimeouts_SoftWithoutFailSafe_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            CachePolicy.Default.WithoutFailSafe().WithFactoryTimeouts(TimeSpan.FromSeconds(1), null));

    [Fact]
    public void WithFactoryTimeouts_HardOnlyWithoutFailSafe_IsAllowed() =>
        Assert.Equal(
            TimeSpan.FromSeconds(1),
            CachePolicy.Default.WithoutFailSafe().WithFactoryTimeouts(null, TimeSpan.FromSeconds(1)).FactoryHardTimeout);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    public void WithEagerRefresh_OutOfRange_Throws(double threshold) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CachePolicy.Default.WithEagerRefresh(threshold));

    [Fact]
    public void WithoutEagerRefresh_ClearsThreshold() =>
        Assert.Null(CachePolicy.Default.WithoutEagerRefresh().EagerRefreshThreshold);

    [Fact]
    public void WithJitter_NonPositive_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CachePolicy.Default.WithJitter(TimeSpan.Zero));

    [Fact]
    public void LocalOnly_SetsFlag() => Assert.True(CachePolicy.Default.LocalOnly().IsLocalOnly);

    [Fact]
    public void Equality_ComparesTagsByValue()
    {
        CachePolicy first = CachePolicy.Default.WithTags("a", "b");
        CachePolicy second = CachePolicy.Default.WithTags("a", "b");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, CachePolicy.Default.WithTags("b", "a"));
    }

    [Fact]
    public void Equality_DiffersBySetting() =>
        Assert.NotEqual(CachePolicy.Default, CachePolicy.Default.LocalOnly());
}
