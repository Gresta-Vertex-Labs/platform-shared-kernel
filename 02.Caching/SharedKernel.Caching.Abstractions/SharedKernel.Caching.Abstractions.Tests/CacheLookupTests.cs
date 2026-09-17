using Xunit;

namespace SharedKernel.Caching.Abstractions.Tests;

public sealed class CacheLookupTests
{
    [Fact]
    public void Miss_IsNotHit_AndValueThrows()
    {
        CacheLookup<int> miss = CacheLookup<int>.Miss;

        Assert.False(miss.IsHit);
        Assert.Throws<InvalidOperationException>(() => miss.Value);
        Assert.False(miss.TryGetValue(out _));
        Assert.Equal(7, miss.GetValueOrDefault(7));
    }

    [Fact]
    public void Default_IsMiss() => Assert.False(default(CacheLookup<string>).IsHit);

    [Fact]
    public void Hit_WithDefaultValue_IsDistinguishableFromMiss()
    {
        CacheLookup<int> zero = CacheLookup<int>.Hit(0);
        CacheLookup<string?> cachedNull = CacheLookup<string?>.Hit(null);

        Assert.True(zero.IsHit);
        Assert.Equal(0, zero.Value);
        Assert.True(cachedNull.IsHit);
        Assert.Null(cachedNull.Value);
        Assert.NotEqual(CacheLookup<int>.Miss, zero);
    }

    [Fact]
    public void TryGetValue_Hit_ReturnsValue()
    {
        Assert.True(CacheLookup<string>.Hit("x").TryGetValue(out string? value));
        Assert.Equal("x", value);
    }

    [Fact]
    public void Equality_ComparesHitAndValue()
    {
        Assert.Equal(CacheLookup<string>.Hit("a"), CacheLookup<string>.Hit("a"));
        Assert.True(CacheLookup<string>.Hit("a") != CacheLookup<string>.Hit("b"));
        Assert.True(CacheLookup<string>.Miss == default);
    }

    [Fact]
    public void ToString_DescribesOutcome()
    {
        Assert.Equal("Miss", CacheLookup<int>.Miss.ToString());
        Assert.Equal("Hit(3)", CacheLookup<int>.Hit(3).ToString());
    }
}
