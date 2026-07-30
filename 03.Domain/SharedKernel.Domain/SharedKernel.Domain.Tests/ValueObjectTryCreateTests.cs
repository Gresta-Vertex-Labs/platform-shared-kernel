using FluentAssertions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.ValueObjects;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-35: P-310/WO-051 — ValueObject.TryCreate&lt;T&gt;/CheckRule tests, mirroring the existing
/// AggregateRoot&lt;TId&gt; TryCreateTests.cs coverage.
/// </summary>
public class ValueObjectTryCreateTests
{
    private sealed class NoNegativeAmountRule : IBusinessRule
    {
        private readonly decimal _amount;
        public NoNegativeAmountRule(decimal amount) => _amount = amount;
        public string Message => "Amount must not be negative.";
        public bool IsBroken() => _amount < 0;
    }

    // Uses the field-initializer / primary-constructor pattern documented as construction-order-safe
    // on ValueObject itself — Amount/Currency are assigned before the base ValueObject() constructor
    // invokes Validate(), so Validate() can safely read them.
    private sealed class Money(decimal amount, string currency) : ValueObject
    {
        public decimal Amount { get; } = GuardAmount(amount);
        public string Currency { get; } = currency;

        public static Result<Money> Create(decimal amount, string currency) =>
            TryCreate(() => new Money(amount, currency));

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }

        protected override IEnumerable<Error>? Validate()
        {
            if (string.IsNullOrWhiteSpace(Currency))
                yield return Error.Validation("money.currency.required", "Currency is required.");
        }

        private static decimal GuardAmount(decimal amount)
        {
            CheckRule(new NoNegativeAmountRule(amount));
            return amount;
        }
    }

    private sealed class NoNegativeAmountRuleThrower : ValueObject
    {
        public NoNegativeAmountRuleThrower(decimal amount) => CheckRule(new NoNegativeAmountRule(amount));

        protected override IEnumerable<object?> GetEqualityComponents() => [];

        protected override IEnumerable<Error>? Validate() => null;
    }

    private sealed class AlwaysInvalidMoney : ValueObject
    {
        private AlwaysInvalidMoney() { }

        public static Result<AlwaysInvalidMoney> CreateInvalid() =>
            TryCreate<AlwaysInvalidMoney>(() =>
                throw new SharedKernel.Core.Exceptions.ValidationException(
                    [Error.Validation("test.error", "Always invalid")]));

        protected override IEnumerable<object?> GetEqualityComponents() => [];

        protected override IEnumerable<Error>? Validate() => null;
    }

    private sealed class PercentageValueObject : SingleValueObject<int>
    {
        private PercentageValueObject(int value) : base(value) { }

        public static Result<PercentageValueObject> Create(int value) =>
            TryCreate(() => new PercentageValueObject(value));

        protected override IEnumerable<Error>? Validate()
        {
            if (Value is < 0 or > 100)
                yield return Error.Validation("percentage.range", "Value must be between 0 and 100.");
        }
    }

    // --- Success path ---

    [Fact]
    public void TryCreate_Success_ReturnsSuccessResult()
    {
        var result = Money.Create(100m, "USD");

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(100m);
        result.Value.Currency.Should().Be("USD");
    }

    // --- BusinessRuleViolationException → Result.Failure ---

    [Fact]
    public void TryCreate_BusinessRuleViolationException_ReturnsFailure()
    {
        var result = Money.Create(-50m, "USD");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.BusinessRule);
    }

    [Fact]
    public void TryCreate_BusinessRuleViolationException_ErrorCode_IsRuleViolated()
    {
        var result = Money.Create(-1m, "USD");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Domain.RuleViolated);
    }

    [Fact]
    public void CheckRule_BrokenRule_OutsideTryCreate_ThrowsBusinessRuleViolationException()
    {
        var act = () => new NoNegativeAmountRuleThrower(-1m);

        act.Should().Throw<SharedKernel.Domain.Exceptions.BusinessRuleViolationException>()
            .Which.Rule.Should().BeOfType<NoNegativeAmountRule>();
    }

    // --- ValidationException → Result.Failure ---

    [Fact]
    public void TryCreate_ValidationException_ReturnsFailure()
    {
        var result = Money.Create(50m, string.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void TryCreate_ThrownValidationException_ReturnsFailure()
    {
        var result = AlwaysInvalidMoney.CreateInvalid();

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    // --- SingleValueObject<TValue> inherits both helpers with zero additional code ---

    [Fact]
    public void SingleValueObject_TryCreate_Success_ReturnsSuccessResult()
    {
        var result = PercentageValueObject.Create(50);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(50);
    }

    [Fact]
    public void SingleValueObject_TryCreate_ValidationFailure_ReturnsFailure()
    {
        var result = PercentageValueObject.Create(150);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }
}
