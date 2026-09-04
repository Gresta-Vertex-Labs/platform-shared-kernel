using FluentAssertions;
using SharedKernel.Domain.ValueObjects.Money;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-40 (RoundingPolicy behavior, exercised end-to-end through <see cref="Money.Create"/> since
/// the rounding helper is private per the design's own construction-order-safety constraints)
/// and T-44: P-439a/P-439e/WO-066 — <see cref="Money"/> construction/rounding/equality tests.
/// </summary>
public class MoneyEqualityTests
{
    // --- RoundingPolicy (T-40) ---

    [Fact]
    public void Create_BankersRounding_MidpointRoundsToEven()
    {
        // 2.125 at 2 digits: nearest even neighbor of the third-decimal-digit-2 midpoint is 2.12.
        var result = Money.Create(2.125m, Currency.Usd, RoundingPolicy.BankersRounding);

        result.Value.Amount.Should().Be(2.12m);
    }

    [Fact]
    public void Create_AwayFromZero_MidpointRoundsAwayFromZero()
    {
        var result = Money.Create(2.125m, Currency.Usd, RoundingPolicy.AwayFromZero);

        result.Value.Amount.Should().Be(2.13m);
    }

    [Fact]
    public void Create_BankersRounding_NegativeMidpoint_RoundsAwayFromZeroOppositeOfAwayFromZeroPolicy()
    {
        var bankers = Money.Create(-2.125m, Currency.Usd, RoundingPolicy.BankersRounding).Value;
        var awayFromZero = Money.Create(-2.125m, Currency.Usd, RoundingPolicy.AwayFromZero).Value;

        bankers.Amount.Should().Be(-2.12m);
        awayFromZero.Amount.Should().Be(-2.13m);
    }

    [Fact]
    public void Create_ZeroDecimalCurrency_RoundsAwayAllDecimalPlaces()
    {
        var result = Money.Create(1500.6m, Currency.Jpy);

        result.Value.Amount.Should().Be(1501m);
    }

    [Fact]
    public void Create_ThreeDecimalCurrency_RoundsToThreeDigits()
    {
        var bhd = Currency.Create("BHD").Value;

        var result = Money.Create(1.23456m, bhd, RoundingPolicy.AwayFromZero);

        result.Value.Amount.Should().Be(1.235m);
    }

    [Fact]
    public void Create_BankersRounding_ZeroDecimalMidpoint_RoundsToEven()
    {
        // 1500.5 at 0 digits: nearest even neighbor of the midpoint is 1500.
        var result = Money.Create(1500.5m, Currency.Jpy, RoundingPolicy.BankersRounding);

        result.Value.Amount.Should().Be(1500m);
    }

    [Fact]
    public void Create_AwayFromZero_ZeroDecimalMidpoint_RoundsAwayFromZero()
    {
        var result = Money.Create(1500.5m, Currency.Jpy, RoundingPolicy.AwayFromZero);

        result.Value.Amount.Should().Be(1501m);
    }

    [Fact]
    public void Create_BankersRounding_ThreeDecimalMidpoint_RoundsToEven()
    {
        var bhd = Currency.Create("BHD").Value;

        // 1.2345 at 3 digits: the third decimal digit (4) is already even, so BankersRounding stays.
        var result = Money.Create(1.2345m, bhd, RoundingPolicy.BankersRounding);

        result.Value.Amount.Should().Be(1.234m);
    }

    [Fact]
    public void Create_AwayFromZero_ThreeDecimalMidpoint_RoundsAwayFromZero()
    {
        var bhd = Currency.Create("BHD").Value;

        var result = Money.Create(1.2345m, bhd, RoundingPolicy.AwayFromZero);

        result.Value.Amount.Should().Be(1.235m);
    }

    // --- Construction / rounding is unconditional (T-44) ---

    [Fact]
    public void Create_Default_UsesBankersRounding()
    {
        var result = Money.Create(10.005m, Currency.Usd);

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(10.00m);
    }

    [Fact]
    public void Create_ExplicitAwayFromZero_RoundsAwayFromZero()
    {
        var result = Money.Create(10.005m, Currency.Usd, RoundingPolicy.AwayFromZero);

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(10.01m);
    }

    [Fact]
    public void Create_AmountAlreadyAtPrecision_IsUnchanged()
    {
        var result = Money.Create(10.00m, Currency.Usd);

        result.Value.Amount.Should().Be(10.00m);
    }

    // --- Structural equality ---

    [Fact]
    public void Equals_SameAmountAndCurrency_AreEqual()
    {
        var left = Money.Create(10.00m, Currency.Usd).Value;
        var right = Money.Create(10.00m, Currency.Usd).Value;

        left.Should().Be(right);
        (left == right).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentAmount_AreNotEqual()
    {
        var left = Money.Create(10.00m, Currency.Usd).Value;
        var right = Money.Create(11.00m, Currency.Usd).Value;

        left.Should().NotBe(right);
    }

    [Fact]
    public void Equals_DifferentCurrency_AreNotEqual()
    {
        var left = Money.Create(10.00m, Currency.Usd).Value;
        var right = Money.Create(10.00m, Currency.Eur).Value;

        left.Should().NotBe(right);
    }

    [Fact]
    public void GetHashCode_EqualInstances_ProduceSameHashCode()
    {
        var left = Money.Create(10.00m, Currency.Usd).Value;
        var right = Money.Create(10.00m, Currency.Usd).Value;

        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    // --- Zero ---

    [Fact]
    public void Zero_AmountIsZero()
    {
        Money.Zero(Currency.Usd).Amount.Should().Be(0m);
    }

    [Fact]
    public void Zero_CannotFail_ReturnsMoneyDirectly()
    {
        var zero = Money.Zero(Currency.Eur);

        zero.Should().NotBeNull();
        zero.Currency.Should().Be(Currency.Eur);
    }
}
