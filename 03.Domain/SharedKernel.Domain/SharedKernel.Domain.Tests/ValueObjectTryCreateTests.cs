using FluentAssertions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Domain.ValueObjects;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// ValueObject explicit validation (EnsureValid), TryCreate and CheckRule, and their inheritance by
/// SingleValueObject.
/// </summary>
public class ValueObjectTryCreateTests
{
    private sealed class NoNegativeAmountRule(decimal amount) : IBusinessRule
    {
        public string Code => "amount.negative";
        public string Message => "Amount must not be negative.";
        public bool IsBroken() => amount < 0;
    }

    /// <summary>Assigns in the constructor body, then validates: the pattern the base now requires.</summary>
    private sealed class Price : ValueObject
    {
        private Price(decimal amount, string currency)
        {
            CheckRule(new NoNegativeAmountRule(amount));
            Amount = amount;
            Currency = currency;
            EnsureValid();
        }

        public decimal Amount { get; }

        public string Currency { get; }

        public static ValidationResult<Price> Create(decimal amount, string currency) =>
            TryCreate(() => new Price(amount, currency));

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }

        protected override IEnumerable<Error> Validate()
        {
            if (string.IsNullOrWhiteSpace(Currency))
                yield return Error.Validation("price.currency_required", "Currency is required.");
            if (Amount > 1_000_000m)
                yield return Error.Validation("price.too_large", "Amount is too large.");
        }
    }

    /// <summary>Never calls EnsureValid, so its Validate rules are never applied.</summary>
    private sealed class NeverValidated : ValueObject
    {
        public NeverValidated() { }

        protected override IEnumerable<object?> GetEqualityComponents() => [];

        protected override IEnumerable<Error> Validate() => [Error.Validation("always", "Always invalid.")];
    }

    private sealed class Percentage : SingleValueObject<int>
    {
        private Percentage(int value) : base(value) { }

        public static ValidationResult<Percentage> Create(int value) => TryCreate(() => new Percentage(value));

        protected override IEnumerable<Error> Validate()
        {
            if (Value is < 0 or > 100)
                yield return Error.Validation("percentage.range", "Value must be between 0 and 100.");
        }
    }

    [Fact]
    public void Success_ReturnsTheValue()
    {
        var result = Price.Create(100m, "USD");

        result.IsValid.Should().BeTrue();
        result.Value.Amount.Should().Be(100m);
    }

    [Fact]
    public void Validate_SeesMembersAssignedInTheConstructorBody()
    {
        // Before EnsureValid, the base constructor validated before the body ran, so Currency was still null.
        Price.Create(1m, "EUR").IsValid.Should().BeTrue();
    }

    [Fact]
    public void EveryValidationError_IsReturned()
    {
        var result = Price.Create(2_000_000m, "");

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.Code).Should().Equal("price.currency_required", "price.too_large");
    }

    [Fact]
    public void BrokenRule_ReturnsFailure_WithTheRulesCode()
    {
        var result = Price.Create(-1m, "USD");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("amount.negative");
    }

    [Fact]
    public void FailedResult_ValueAccess_Throws()
    {
        var act = () => Price.Create(-1m, "USD").Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValid_ThrowsValidationException_WithAllErrors()
    {
        var act = () => new ThrowingPrice();

        act.Should().Throw<ValidationException>().Which.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void ValueObject_ThatNeverCallsEnsureValid_IsNotValidated()
    {
        var act = () => new NeverValidated();

        act.Should().NotThrow();
    }

    [Fact]
    public void SingleValueObject_ValidatesAutomatically()
    {
        Percentage.Create(50).IsValid.Should().BeTrue();
        Percentage.Create(150).Errors.Should().ContainSingle().Which.Code.Should().Be("percentage.range");
    }

    [Fact]
    public void SingleValueObject_NullValue_IsAFailure()
    {
        var act = () => new NullableText(null!);

        act.Should().Throw<DomainException>().Which.Error.Code.Should().Be(ErrorCodes.Validation.Required);
    }

    private sealed class ThrowingPrice : ValueObject
    {
        public ThrowingPrice() => EnsureValid();

        protected override IEnumerable<object?> GetEqualityComponents() => [];

        protected override IEnumerable<Error> Validate() =>
            [Error.Validation("one", "One."), Error.Validation("two", "Two.")];
    }

    private sealed class NullableText(string value) : SingleValueObject<string>(value)
    {
        protected override IEnumerable<Error>? Validate() => null;
    }
}
