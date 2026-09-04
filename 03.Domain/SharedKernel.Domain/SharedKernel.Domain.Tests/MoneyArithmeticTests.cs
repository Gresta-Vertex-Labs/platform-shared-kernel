using FluentAssertions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Domain.ValueObjects.Money;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-45: P-439f/WO-066 — <see cref="Money"/> arithmetic/comparison tests.
/// </summary>
public class MoneyArithmeticTests
{
    private static Money Usd(decimal amount) => Money.Create(amount, Currency.Usd).Value;
    private static Money Eur(decimal amount) => Money.Create(amount, Currency.Eur).Value;

    // --- Same-currency arithmetic ---

    [Fact]
    public void Add_SameCurrency_ProducesCorrectResult()
    {
        var result = Usd(10.00m).Add(Usd(5.50m));

        result.Amount.Should().Be(15.50m);
        result.Currency.Should().Be(Currency.Usd);
    }

    [Fact]
    public void Subtract_SameCurrency_ProducesCorrectResult()
    {
        var result = Usd(10.00m).Subtract(Usd(3.25m));

        result.Amount.Should().Be(6.75m);
    }

    [Fact]
    public void Multiply_ReRoundsProductCorrectly()
    {
        var result = Usd(10.00m).Multiply(1.0555m);

        // 10.00 * 1.0555 = 10.555 -> an exact 2-decimal midpoint; default BankersRounding
        // rounds to the even neighbor, 10.56 (6 is even, 5 is odd).
        result.Amount.Should().Be(10.56m);
    }

    [Fact]
    public void Multiply_AtMidpoint_UsesSuppliedRoundingPolicy()
    {
        var money = Usd(1.00m);

        var bankers = money.Multiply(1.125m, RoundingPolicy.BankersRounding);
        var awayFromZero = money.Multiply(1.125m, RoundingPolicy.AwayFromZero);

        bankers.Amount.Should().Be(1.12m);
        awayFromZero.Amount.Should().Be(1.13m);
    }

    [Fact]
    public void Negate_FlipsSign_SameCurrency()
    {
        var negated = Usd(5.00m).Negate();

        negated.Amount.Should().Be(-5.00m);
        negated.Currency.Should().Be(Currency.Usd);
    }

    [Fact]
    public void CompareTo_SameCurrency_MatchesAmountComparison()
    {
        Usd(5.00m).CompareTo(Usd(10.00m)).Should().BeLessThan(0);
        Usd(10.00m).CompareTo(Usd(5.00m)).Should().BeGreaterThan(0);
        Usd(5.00m).CompareTo(Usd(5.00m)).Should().Be(0);
    }

    // --- Cross-currency rejection ---

    [Fact]
    public void Add_CrossCurrency_ThrowsBusinessRuleViolationException()
    {
        var act = () => Usd(10.00m).Add(Eur(10.00m));

        act.Should().Throw<BusinessRuleViolationException>()
            .Which.Rule.Should().BeOfType<CurrencyMismatchRule>();
    }

    [Fact]
    public void Add_CrossCurrency_ErrorTypeIsBusinessRule()
    {
        var act = () => Usd(10.00m).Add(Eur(10.00m));

        act.Should().Throw<BusinessRuleViolationException>()
            .Which.Error.Type.Should().Be(ErrorType.BusinessRule);
    }

    [Fact]
    public void Subtract_CrossCurrency_ThrowsBusinessRuleViolationException()
    {
        var act = () => Usd(10.00m).Subtract(Eur(10.00m));

        act.Should().Throw<BusinessRuleViolationException>();
    }

    [Fact]
    public void Subtract_CrossCurrency_CarriesCurrencyMismatchRuleAndBusinessRuleErrorType()
    {
        var act = () => Usd(10.00m).Subtract(Eur(10.00m));

        var exception = act.Should().Throw<BusinessRuleViolationException>().Which;
        exception.Rule.Should().BeOfType<CurrencyMismatchRule>();
        exception.Error.Type.Should().Be(ErrorType.BusinessRule);
    }

    [Fact]
    public void CompareTo_CrossCurrency_ThrowsBusinessRuleViolationException()
    {
        var act = () => Usd(10.00m).CompareTo(Eur(10.00m));

        act.Should().Throw<BusinessRuleViolationException>();
    }

    [Fact]
    public void CompareTo_CrossCurrency_CarriesCurrencyMismatchRuleAndBusinessRuleErrorType()
    {
        var act = () => Usd(10.00m).CompareTo(Eur(10.00m));

        var exception = act.Should().Throw<BusinessRuleViolationException>().Which;
        exception.Rule.Should().BeOfType<CurrencyMismatchRule>();
        exception.Error.Type.Should().Be(ErrorType.BusinessRule);
    }

    [Fact]
    public void CompareTo_Null_ReturnsPositiveOne()
    {
        Usd(10.00m).CompareTo(null).Should().Be(1);
    }

    // --- Operators mirror named methods ---

    [Fact]
    public void OperatorPlus_MatchesAdd()
    {
        var viaOperator = Usd(1.00m) + Usd(2.00m);
        var viaMethod = Usd(1.00m).Add(Usd(2.00m));

        viaOperator.Should().Be(viaMethod);
    }

    [Fact]
    public void OperatorMinus_MatchesSubtract()
    {
        var viaOperator = Usd(5.00m) - Usd(2.00m);
        var viaMethod = Usd(5.00m).Subtract(Usd(2.00m));

        viaOperator.Should().Be(viaMethod);
    }

    [Fact]
    public void UnaryOperatorMinus_MatchesNegate()
    {
        var viaOperator = -Usd(5.00m);
        var viaMethod = Usd(5.00m).Negate();

        viaOperator.Should().Be(viaMethod);
    }

    [Fact]
    public void OperatorStar_MatchesMultiply()
    {
        var viaOperator = Usd(5.00m) * 2m;
        var viaMethod = Usd(5.00m).Multiply(2m);

        viaOperator.Should().Be(viaMethod);
    }

    [Fact]
    public void ComparisonOperators_AgreeWithCompareTo_SameCurrency()
    {
        var small = Usd(5.00m);
        var smallEqual = Usd(5.00m);
        var big = Usd(10.00m);

        (small < big).Should().BeTrue();
        (small <= big).Should().BeTrue();
        (big > small).Should().BeTrue();
        (big >= small).Should().BeTrue();
        (small <= smallEqual).Should().BeTrue();
        (small >= smallEqual).Should().BeTrue();
        (big < small).Should().BeFalse();
        (small > big).Should().BeFalse();
    }

    [Fact]
    public void ComparisonOperators_CrossCurrency_Throw()
    {
        var usd = Usd(5.00m);
        var eur = Eur(5.00m);

        var lt = () => usd < eur;
        var lte = () => usd <= eur;
        var gt = () => usd > eur;
        var gte = () => usd >= eur;

        lt.Should().Throw<BusinessRuleViolationException>();
        lte.Should().Throw<BusinessRuleViolationException>();
        gt.Should().Throw<BusinessRuleViolationException>();
        gte.Should().Throw<BusinessRuleViolationException>();
    }
}
