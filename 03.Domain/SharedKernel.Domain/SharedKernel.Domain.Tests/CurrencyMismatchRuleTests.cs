using FluentAssertions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Monetary;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-43: P-439d/WO-066 — <see cref="CurrencyMismatchRule"/> tests, exercised standalone
/// (independent of <see cref="Money"/>) to prove the reuse claim.
/// </summary>
public class CurrencyMismatchRuleTests
{
    [Fact]
    public void IsBroken_DifferentCurrencies_ReturnsTrue()
    {
        var rule = new CurrencyMismatchRule(Currency.Usd, Currency.Eur);

        rule.IsBroken().Should().BeTrue();
    }

    [Fact]
    public void IsBroken_SameCurrencyByValue_ReturnsFalse()
    {
        var expected = Currency.Create("USD").Value;
        var actual = Currency.Create("usd").Value;

        var rule = new CurrencyMismatchRule(expected, actual);

        rule.IsBroken().Should().BeFalse();
    }

    [Fact]
    public void IsBroken_SameCurrencyDifferentInstance_ReturnsFalse()
    {
        // Proves equality is by value, not reference.
        ReferenceEquals(Currency.Usd, Currency.Create("USD").Value).Should().BeFalse();

        var rule = new CurrencyMismatchRule(Currency.Usd, Currency.Create("USD").Value);

        rule.IsBroken().Should().BeFalse();
    }

    [Fact]
    public void Message_IncludesBothCurrencyCodes()
    {
        var rule = new CurrencyMismatchRule(Currency.Usd, Currency.Eur);

        rule.Message.Should().Contain("USD").And.Contain("EUR");
    }
}
