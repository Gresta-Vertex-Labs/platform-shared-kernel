using FluentAssertions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.ValueObjects;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Tests;

public class ValueObjectEqualityTests
{
    // --- Test doubles ---

    private sealed class Money : ValueObject
    {
        public decimal Amount { get; }
        public string Currency { get; }

        public Money(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }

        protected override IEnumerable<Error>? Validate() => null; // always valid
    }

    private sealed class MoneyWithNullComponent : ValueObject
    {
        public string? Tag { get; }

        public MoneyWithNullComponent(string? tag) => Tag = tag;

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Tag;
        }

        protected override IEnumerable<Error>? Validate() => null;
    }

    private sealed class InvalidMoney : ValueObject
    {
        public InvalidMoney(decimal amount)
        {
            _ = amount; // store conceptually
        }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return 0m;
        }

        protected override IEnumerable<Error>? Validate()
        {
            yield return Error.Validation("money.invalid", "Amount must be positive");
        }
    }

    private sealed class MultiErrorMoney : ValueObject
    {
        public MultiErrorMoney()
        {
        }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return 0m;
        }

        protected override IEnumerable<Error>? Validate()
        {
            yield return Error.Validation("money.amount", "Amount must be positive");
            yield return Error.Validation("money.currency", "Currency is required");
        }
    }

    // --- Structural equality ---

    [Fact]
    public void Equals_AllComponentsEqual_ReturnsTrue()
    {
        var a = new Money(100m, "USD");
        var b = new Money(100m, "USD");

        a.Equals(b).Should().BeTrue();
    }

    [Fact]
    public void Equals_OneComponentDiffers_ReturnsFalse()
    {
        var a = new Money(100m, "USD");
        var b = new Money(100m, "EUR");

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentAmounts_ReturnsFalse()
    {
        var a = new Money(100m, "USD");
        var b = new Money(200m, "USD");

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var a = new Money(100m, "USD");
        a.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentType_ReturnsFalse()
    {
        var a = new Money(100m, "USD");
        a.Equals("not a value object").Should().BeFalse();
    }

    [Fact]
    public void Operator_Equal_SameComponents_ReturnsTrue()
    {
        var a = new Money(100m, "USD");
        var b = new Money(100m, "USD");
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Operator_NotEqual_DifferentComponents_ReturnsTrue()
    {
        var a = new Money(100m, "USD");
        var b = new Money(200m, "USD");
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void Operator_Equal_BothNull_ReturnsTrue()
    {
        Money? a = null;
        Money? b = null;
        (a == b).Should().BeTrue();
    }

    // --- Null component handling ---

    [Fact]
    public void Equals_NullComponent_BothNull_ReturnsTrue()
    {
        var a = new MoneyWithNullComponent(null);
        var b = new MoneyWithNullComponent(null);
        a.Equals(b).Should().BeTrue();
    }

    [Fact]
    public void Equals_NullComponent_OneNull_ReturnsFalse()
    {
        var a = new MoneyWithNullComponent(null);
        var b = new MoneyWithNullComponent("tag");
        a.Equals(b).Should().BeFalse();
    }

    // --- GetHashCode consistency ---

    [Fact]
    public void GetHashCode_EqualObjects_SameHash()
    {
        var a = new Money(100m, "USD");
        var b = new Money(100m, "USD");
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_ConsistentAcrossCalls()
    {
        var a = new Money(100m, "USD");
        var firstHash = a.GetHashCode();
        a.GetHashCode().Should().Be(firstHash);
    }

    // --- Validate() hook ---

    [Fact]
    public void Validate_ReturnsErrors_ThrowsValidationException()
    {
        var act = () => new InvalidMoney(-1m);
        act.Should().Throw<ValidationException>()
            .Which.Errors.Should().ContainSingle(e => e.Code == "money.invalid");
    }

    [Fact]
    public void Validate_ReturnsMultipleErrors_ThrowsValidationExceptionWithAllErrors()
    {
        var act = () => new MultiErrorMoney();
        act.Should().Throw<ValidationException>()
            .Which.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void Validate_ReturnsNull_ConstructionSucceeds()
    {
        var act = () => new Money(50m, "GBP");
        act.Should().NotThrow();
    }

    // --- T-37: P-311b/WO-051 — IEquatable<ValueObject> ---

    [Fact]
    public void ValueObject_Implements_IEquatableOfValueObject()
    {
        var money = new Money(100m, "USD");
        money.Should().BeAssignableTo<IEquatable<ValueObject>>();
    }

    [Fact]
    public void TypedEquals_SameComponents_MatchesObjectEquals()
    {
        var a = new Money(100m, "USD");
        var b = new Money(100m, "USD");

        ((IEquatable<ValueObject>)a).Equals(b).Should().Be(a.Equals((object?)b));
        ((IEquatable<ValueObject>)a).Equals(b).Should().BeTrue();
    }

    [Fact]
    public void TypedEquals_DifferentComponents_MatchesObjectEquals()
    {
        var a = new Money(100m, "USD");
        var b = new Money(200m, "USD");

        ((IEquatable<ValueObject>)a).Equals(b).Should().Be(a.Equals((object?)b));
        ((IEquatable<ValueObject>)a).Equals(b).Should().BeFalse();
    }

    [Fact]
    public void TypedEquals_Null_MatchesObjectEquals()
    {
        var a = new Money(100m, "USD");
        IEquatable<ValueObject> equatable = a;
        ValueObject? nullOther = null;

        var typedResult = equatable.Equals(nullOther);
        var objectResult = a.Equals((object?)null);

        typedResult.Should().Be(objectResult);
        typedResult.Should().BeFalse();
    }
}
