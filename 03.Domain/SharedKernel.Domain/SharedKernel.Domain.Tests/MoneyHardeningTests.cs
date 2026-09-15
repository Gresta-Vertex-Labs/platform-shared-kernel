using System.Globalization;
using FluentAssertions;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Domain.Monetary;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Tests;

/// <summary>Money and currency behaviour fixed and added in the pre-publish hardening pass.</summary>
public sealed class MoneyHardeningTests
{
    private static Money Usd(decimal amount) => Money.Create(amount, Currency.Usd).Value;

    private static Money Eur(decimal amount) => Money.Create(amount, Currency.Eur).Value;

    // ---- Currency catalog ----

    [Theory]
    [InlineData("BIF", 0)] // was 2 before the fix
    [InlineData("XCG", 2)] // introduced 2025, was unknown
    [InlineData("ZWG", 2)] // introduced 2024, was unknown
    [InlineData("VED", 2)] // introduced 2021, was unknown
    [InlineData("KWD", 3)]
    [InlineData("TRY", 2)]
    public void Catalog_HasIsoMinorUnits(string code, int digits) =>
        Currency.Create(code).Value.MinorUnitDigits.Should().Be(digits);

    [Theory]
    [InlineData("ANG")] // withdrawn 2025
    [InlineData("ZWL")] // withdrawn 2024
    [InlineData("CUC")] // withdrawn 2021
    public void Catalog_ExcludesWithdrawnCurrencies(string code)
    {
        var result = Currency.Create(code);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("currency.code.unknown");
    }

    [Fact]
    public void Catalog_ExposesItsCodesAndRegistryDate()
    {
        CurrencyCatalog.Codes.Should().Contain("USD").And.OnlyHaveUniqueItems();
        CurrencyCatalog.RegistryAsOf.Should().MatchRegex(@"^\d{4}-\d{2}$");
    }

    [Fact]
    public void Currency_Try_IsTheTurkishLira() => Currency.Try.Code.Should().Be("TRY");

    [Fact]
    public void Currency_Create_Null_IsAFailure() => Currency.Create(null).IsValid.Should().BeFalse();

    // ---- Creation ----

    [Fact]
    public void Create_NullCurrency_IsAFailure_NotAnException()
    {
        // Before: TryCreate did not catch the guard's DomainException, so Create threw.
        var result = Money.Create(1m, null!);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Validation.Required);
    }

    [Fact]
    public void Create_UndefinedRoundingPolicy_IsAFailure() =>
        Money.Create(1m, Currency.Usd, (RoundingPolicy)99).IsValid.Should().BeFalse();

    // ---- Formatting ----

    [Fact]
    public void ToString_UsesMinorUnitsAndCode_Invariantly()
    {
        // Before: "SharedKernel.Domain.ValueObjects.Money.Money".
        Usd(1234.5m).ToString().Should().Be("1234.50 USD");
        Money.Create(5m, Currency.Jpy).Value.ToString().Should().Be("5 JPY");
        Money.Create(1.5m, Currency.Create("KWD").Value).Value.ToString().Should().Be("1.500 KWD");
    }

    [Fact]
    public void ToString_WithFormatAndCulture()
    {
        Usd(1234.5m).ToString("N2", CultureInfo.GetCultureInfo("tr-TR")).Should().Be("1.234,50 USD");
        $"{Usd(3m):N2}".Should().EndWith(" USD");
    }

    // ---- Predicates and math ----

    [Fact]
    public void Predicates()
    {
        Money.Zero(Currency.Usd).IsZero.Should().BeTrue();
        Usd(1m).IsPositive.Should().BeTrue();
        Usd(-1m).IsNegative.Should().BeTrue();
        Usd(-2.5m).Abs().Should().Be(Usd(2.5m));
    }

    [Fact]
    public void MinAndMax()
    {
        Money.Min(Usd(1m), Usd(2m)).Should().Be(Usd(1m));
        Money.Max(Usd(1m), Usd(2m)).Should().Be(Usd(2m));
    }

    [Fact]
    public void MinAcrossCurrencies_ThrowsWithTheMismatchCode()
    {
        var act = () => Money.Min(Usd(1m), Eur(1m));

        act.Should().Throw<BusinessRuleViolationException>().Which.Error.Code.Should().Be(CurrencyMismatchRule.ErrorCode);
    }

    [Fact]
    public void Divide_RoundsTheQuotient()
    {
        Usd(10m).Divide(3m).Should().Be(Usd(3.33m));
        (Usd(10m) / 4m).Should().Be(Usd(2.5m));
        Usd(10m).Divide(3m, RoundingPolicy.Ceiling).Should().Be(Usd(3.34m));
    }

    [Fact]
    public void Divide_ByZero_Throws() =>
        FluentActions.Invoking(() => Usd(1m).Divide(0m)).Should().Throw<DivideByZeroException>();

    [Fact]
    public void Operators_NullOperand_ThrowArgumentNullException()
    {
        Money nothing = null!;
        FluentActions.Invoking(() => nothing + Usd(1m)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => Usd(1m) + nothing).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => nothing < Usd(1m)).Should().Throw<ArgumentNullException>();
    }

    // ---- Sum ----

    [Fact]
    public void Sum_WithCurrency_IncludingEmpty()
    {
        Money.Sum([Usd(1.10m), Usd(2.20m)], Currency.Usd).Should().Be(Usd(3.30m));
        Money.Sum([], Currency.Eur).Should().Be(Money.Zero(Currency.Eur));
        new[] { Usd(1m), Usd(2m) }.Sum(Currency.Usd).Should().Be(Usd(3m));
    }

    [Fact]
    public void Sum_Extension_RejectsEmptyAndMixed()
    {
        new[] { Usd(1m), Usd(2m) }.Sum().Should().Be(Usd(3m));
        FluentActions.Invoking(() => Array.Empty<Money>().Sum()).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => new[] { Usd(1m), Eur(1m) }.Sum()).Should().Throw<BusinessRuleViolationException>();
    }

    // ---- Rounding modes ----

    [Theory]
    [InlineData(2.349, RoundingPolicy.ToZero, 2.34)]
    [InlineData(-2.349, RoundingPolicy.ToZero, -2.34)]
    [InlineData(2.341, RoundingPolicy.Ceiling, 2.35)]
    [InlineData(-2.349, RoundingPolicy.Ceiling, -2.34)]
    [InlineData(2.349, RoundingPolicy.Floor, 2.34)]
    [InlineData(-2.341, RoundingPolicy.Floor, -2.35)]
    [InlineData(2.345, RoundingPolicy.BankersRounding, 2.34)]
    [InlineData(2.345, RoundingPolicy.AwayFromZero, 2.35)]
    public void RoundingPolicies(double input, RoundingPolicy policy, double expected) =>
        Money.Create((decimal)input, Currency.Usd, policy).Value.Amount.Should().Be((decimal)expected);

    // ---- Allocation ----

    [Theory]
    [InlineData(-10.00, 3)]
    [InlineData(0.01, 7)]
    [InlineData(1000.00, 3)]
    public void Allocate_AlwaysAddsUpToTheOriginal(double amount, int parts)
    {
        var money = Usd((decimal)amount);

        var shares = money.Allocate(parts);

        Money.Sum(shares, Currency.Usd).Should().Be(money);
        (shares.Max(s => s.Amount) - shares.Min(s => s.Amount)).Should().BeLessThanOrEqualTo(0.01m);
    }

    [Fact]
    public void Allocate_NullRatios_ThrowsArgumentNullException() =>
        FluentActions.Invoking(() => Usd(1m).Allocate(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void CurrencyMismatchRule_HasItsOwnCode()
    {
        var rule = new CurrencyMismatchRule(Currency.Usd, Currency.Eur);

        rule.IsBroken().Should().BeTrue();
        rule.Code.Should().Be("money.currency_mismatch");
        rule.Message.Should().Be("Expected an amount in USD but got EUR.");
    }
}
