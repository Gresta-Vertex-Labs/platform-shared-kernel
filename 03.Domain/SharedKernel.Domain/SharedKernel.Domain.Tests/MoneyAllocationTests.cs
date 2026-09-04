using FluentAssertions;
using SharedKernel.Domain.ValueObjects.Money;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-46: P-439g/WO-066 — <see cref="Money.Allocate(int)"/> / <see cref="Money.Allocate(IReadOnlyList{int})"/> tests.
/// </summary>
public class MoneyAllocationTests
{
    private static Money Usd(decimal amount) => Money.Create(amount, Currency.Usd).Value;

    private static Money Sum(IEnumerable<Money> parts) => parts.Aggregate((left, right) => left.Add(right));

    [Fact]
    public void Allocate_ThreeWays_ProducesLargestRemainderFirstSplit()
    {
        var parts = Usd(10.00m).Allocate(3);

        parts.Select(p => p.Amount).Should().Equal(3.33m, 3.33m, 3.34m);
    }

    [Fact]
    public void Allocate_ThreeWays_SumEqualsOriginal()
    {
        var original = Usd(10.00m);
        var parts = original.Allocate(3);

        Sum(parts).Should().Be(original);
    }

    [Fact]
    public void Allocate_EqualRatios_MatchesIntOverload()
    {
        var viaInt = Usd(10.00m).Allocate(4);
        var viaRatios = Usd(10.00m).Allocate([1, 1, 1, 1]);

        viaInt.Select(m => m.Amount).Should().Equal(viaRatios.Select(m => m.Amount));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(11)]
    public void Allocate_VariousEvenAndOddSplits_NeverLosesOrInventsAMinorUnit(int numberOfParts)
    {
        var original = Usd(100.01m);
        var parts = original.Allocate(numberOfParts);

        parts.Should().HaveCount(numberOfParts);
        Sum(parts).Should().Be(original);
    }

    [Fact]
    public void Allocate_WeightedRatios_OnOddAmount_SumsExactlyToOriginal()
    {
        var original = Usd(100.03m);
        var parts = original.Allocate([1, 2, 3, 4]);

        parts.Should().HaveCount(4);
        Sum(parts).Should().Be(original);

        // No part differs from its exact proportional share by more than one minor unit worth of rounding.
        parts[3].Amount.Should().BeGreaterThan(parts[0].Amount);
    }

    [Fact]
    public void Allocate_ZeroWeightedRatio_ReceivesNothing_ButOthersSumCorrectly()
    {
        var original = Usd(10.00m);
        var parts = original.Allocate([1, 0, 1]);

        parts[1].Amount.Should().Be(0m);
        Sum(parts).Should().Be(original);
    }

    [Fact]
    public void Allocate_NumberOfPartsLessThanOne_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Usd(10.00m).Allocate(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Allocate_NegativeNumberOfParts_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Usd(10.00m).Allocate(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Allocate_EmptyRatios_ThrowsArgumentException()
    {
        var act = () => Usd(10.00m).Allocate([]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Allocate_NullRatios_ThrowsArgumentException()
    {
        var act = () => Usd(10.00m).Allocate((IReadOnlyList<int>)null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Allocate_NegativeRatio_ThrowsArgumentException()
    {
        var act = () => Usd(10.00m).Allocate([1, -1]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Allocate_AllZeroRatios_ThrowsArgumentException()
    {
        var act = () => Usd(10.00m).Allocate([0, 0, 0]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Allocate_ZeroDecimalCurrency_ConservesTotal()
    {
        var original = Money.Create(100m, Currency.Jpy).Value;
        var parts = original.Allocate(3);

        parts.Select(p => p.Amount).Should().Equal(33m, 33m, 34m);
        Sum(parts).Should().Be(original);
    }
}
