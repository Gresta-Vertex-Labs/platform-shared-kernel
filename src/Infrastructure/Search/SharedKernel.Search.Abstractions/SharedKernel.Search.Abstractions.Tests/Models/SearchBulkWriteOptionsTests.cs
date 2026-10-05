using FluentAssertions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Tests.Models;

/// <summary>
/// T-30 (P-354): <see cref="SearchBulkWriteOptions"/> tests — <see cref="SearchBulkWriteOptions.Default"/>
/// has <see cref="SearchBulkWriteOptions.MaxBatchesPerSecond"/> <see langword="null"/>; a zero or
/// negative <c>MaxBatchesPerSecond</c> throws <see cref="ArgumentException"/>; a positive value is
/// accepted and round-trips.
/// </summary>
public sealed class SearchBulkWriteOptionsTests
{
    [Fact]
    public void Default_HasNullMaxBatchesPerSecond()
    {
        SearchBulkWriteOptions.Default.MaxBatchesPerSecond.Should().BeNull();
    }

    [Fact]
    public void Default_IsTheSameInstanceAcrossCalls()
    {
        // A singleton static instance — mirrors SearchRequest.Default's own shape.
        SearchBulkWriteOptions.Default.Should().BeSameAs(SearchBulkWriteOptions.Default);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(-0.5d)]
    public void MaxBatchesPerSecond_SetToZeroOrNegative_ThrowsArgumentException(double invalidValue)
    {
        var act = () => new SearchBulkWriteOptions { MaxBatchesPerSecond = invalidValue };

        act.Should().Throw<ArgumentException>()
            .WithParameterName("value");
    }

    [Fact]
    public void MaxBatchesPerSecond_SetToNull_DoesNotThrow_AndStaysNull()
    {
        var options = new SearchBulkWriteOptions { MaxBatchesPerSecond = null };

        options.MaxBatchesPerSecond.Should().BeNull();
    }

    [Theory]
    [InlineData(0.1d)]
    [InlineData(1d)]
    [InlineData(50d)]
    [InlineData(double.MaxValue)]
    public void MaxBatchesPerSecond_SetToPositiveValue_IsAccepted_AndRoundTrips(double validValue)
    {
        var options = new SearchBulkWriteOptions { MaxBatchesPerSecond = validValue };

        options.MaxBatchesPerSecond.Should().Be(validValue);
    }

    [Fact]
    public void TwoInstances_WithSameMaxBatchesPerSecond_AreValueEqual()
    {
        // sealed record — value equality, not reference equality, over MaxBatchesPerSecond.
        var first = new SearchBulkWriteOptions { MaxBatchesPerSecond = 10 };
        var second = new SearchBulkWriteOptions { MaxBatchesPerSecond = 10 };

        first.Should().Be(second);
    }
}
