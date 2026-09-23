using FluentAssertions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Tests.Models;

/// <summary>
/// <see cref="SearchCount"/> tests — the qualified count type introduced by the pre-publish pass to
/// stop <c>ISearchIndex&lt;TDocument&gt;.CountAsync</c> publishing a Meilisearch-truncated total as
/// fact. The behaviour worth pinning is that a count carries its own accuracy and that
/// <see cref="SearchCount.ToString"/> can never render a lower bound so it reads as an exact figure.
/// </summary>
public sealed class SearchCountTests
{
    [Fact]
    public void Exact_CarriesExactAccuracy()
    {
        var count = SearchCount.Exact(42);

        count.Value.Should().Be(42);
        count.Accuracy.Should().Be(TotalHitsAccuracy.Exact);
        count.IsExact.Should().BeTrue();
    }

    [Fact]
    public void AtLeast_CarriesLowerBoundAccuracy()
    {
        var count = SearchCount.AtLeast(1000);

        count.Value.Should().Be(1000);
        count.Accuracy.Should().Be(TotalHitsAccuracy.LowerBound);
        count.IsExact.Should().BeFalse();
    }

    [Fact]
    public void Default_IsZeroAndExact()
    {
        // A default-constructed struct must not silently claim to be a lower bound, and must not claim
        // a non-zero total. Exact(0) is the only honest reading of an unset value.
        var count = default(SearchCount);

        count.Value.Should().Be(0);
        count.Accuracy.Should().Be(TotalHitsAccuracy.Exact);
        count.IsExact.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void Exact_NegativeValue_Throws(long value)
    {
        var act = () => SearchCount.Exact(value);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void AtLeast_NegativeValue_Throws(long value)
    {
        var act = () => SearchCount.AtLeast(value);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToString_Exact_RendersTheBareNumber()
    {
        SearchCount.Exact(7).ToString().Should().Be("7");
    }

    [Fact]
    public void ToString_LowerBound_IsNeverMistakableForAnExactFigure()
    {
        // This is the whole reason ToString is overridden: a truncated count rendered as "1000" in a
        // log line or an assertion-failure message reads as fact.
        SearchCount.AtLeast(1000).ToString().Should().Be(">=1000");
    }

    [Fact]
    public void Equality_DistinguishesAccuracy_NotJustValue()
    {
        // "Exactly 1000" and "at least 1000" are different answers and must not compare equal, or a
        // consuming service's caching or change-detection would treat a truncation as a real total.
        SearchCount.Exact(1000).Should().NotBe(SearchCount.AtLeast(1000));
        SearchCount.Exact(1000).Should().Be(SearchCount.Exact(1000));
    }
}
