using Xunit;

namespace SharedKernel.Caching.Abstractions.Tests;

public sealed class CacheFactoryContextTests
{
    [Fact]
    public void Constructor_CapturesKeyAndPolicy_WithNoOverrides()
    {
        var context = new CacheFactoryContext("svc:invoice:42", CachePolicy.Default);

        Assert.Equal("svc:invoice:42", context.Key);
        Assert.Same(CachePolicy.Default, context.Policy);
        Assert.False(context.IsCachingSkipped);
        Assert.Null(context.L1DurationOverride);
        Assert.Null(context.L2DurationOverride);
    }

    [Fact]
    public void Constructor_InvalidArguments_Throw()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CacheFactoryContext(" ", CachePolicy.Default));
        Assert.Throws<ArgumentNullException>(() => new CacheFactoryContext("k", null!));
    }

    [Fact]
    public void SkipCaching_SetsFlag()
    {
        var context = new CacheFactoryContext("k", CachePolicy.Default);

        context.SkipCaching();

        Assert.True(context.IsCachingSkipped);
    }

    [Fact]
    public void SetDurations_RecordsOverrides()
    {
        var context = new CacheFactoryContext("k", CachePolicy.Default);

        context.SetDurations(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(5), context.L1DurationOverride);
        Assert.Equal(TimeSpan.FromSeconds(10), context.L2DurationOverride);
    }

    [Fact]
    public void SetDurations_Invalid_Throws()
    {
        var context = new CacheFactoryContext("k", CachePolicy.Default);

        Assert.Throws<ArgumentOutOfRangeException>(() => context.SetDurations(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.SetDurations(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
    }
}
