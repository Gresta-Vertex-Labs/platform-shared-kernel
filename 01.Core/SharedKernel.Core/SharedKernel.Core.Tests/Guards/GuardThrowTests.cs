using SharedKernel.Core.Exceptions;
using SharedKernel.Guards;
using SharedKernel.Primitives.Enums;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Core.Tests.Guards;

/// <summary>Tests for the imperative Guard.Throw path (T-18).</summary>
public sealed class GuardThrowTests
{
    // ── Null / empty / whitespace ─────────────────────────────────────────────

    [Fact]
    public void Throw_Null_WhenNull_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.Null<string>(null, "s"));

    [Fact]
    public void Throw_Null_WhenNotNull_DoesNotThrow()
        => Guard.Throw.Null("hello", "s"); // must not throw

    [Fact]
    public void Throw_NullOrEmpty_WhenEmpty_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.NullOrEmpty(string.Empty, "s"));

    [Fact]
    public void Throw_NullOrEmpty_WhenNotEmpty_DoesNotThrow()
        => Guard.Throw.NullOrEmpty("hi", "s");

    [Fact]
    public void Throw_NullOrWhiteSpace_WhenWhiteSpace_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.NullOrWhiteSpace("   ", "s"));

    [Fact]
    public void Throw_NullOrWhiteSpace_WhenHasContent_DoesNotThrow()
        => Guard.Throw.NullOrWhiteSpace("text", "s");

    // ── String length ─────────────────────────────────────────────────────────

    [Fact]
    public void Throw_ShorterThan_WhenTooShort_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.ShorterThan("ab", 5, "s"));

    [Fact]
    public void Throw_ShorterThan_WhenLongEnough_DoesNotThrow()
        => Guard.Throw.ShorterThan("hello", 5, "s");

    [Fact]
    public void Throw_LongerThan_WhenTooLong_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.LongerThan("toolong", 3, "s"));

    [Fact]
    public void Throw_LongerThan_WhenShortEnough_DoesNotThrow()
        => Guard.Throw.LongerThan("hi", 3, "s");

    // ── Numeric ───────────────────────────────────────────────────────────────

    [Fact]
    public void Throw_NegativeOrZero_Int_WhenZero_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.NegativeOrZero(0, "n"));

    [Fact]
    public void Throw_NegativeOrZero_Int_WhenPositive_DoesNotThrow()
        => Guard.Throw.NegativeOrZero(1, "n");

    [Fact]
    public void Throw_Negative_Int_WhenNegative_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.Negative(-1, "n"));

    [Fact]
    public void Throw_Negative_Int_WhenZero_DoesNotThrow()
        => Guard.Throw.Negative(0, "n");

    [Fact]
    public void Throw_NotPositive_Int_WhenZero_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.NotPositive(0, "n"));

    [Fact]
    public void Throw_NotPositive_Int_WhenPositive_DoesNotThrow()
        => Guard.Throw.NotPositive(5, "n");

    [Fact]
    public void Throw_NegativeOrZero_Decimal_WhenZero_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.NegativeOrZero(0m, "d"));

    [Fact]
    public void Throw_NegativeOrZero_Long_WhenNegative_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.NegativeOrZero(-1L, "l"));

    // ── Range ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Throw_OutOfRange_WhenOutsideBounds_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.OutOfRange(0, 1, 10, "v"));

    [Fact]
    public void Throw_OutOfRange_WhenWithinBounds_DoesNotThrow()
        => Guard.Throw.OutOfRange(5, 1, 10, "v");

    // ── Default / Guid ────────────────────────────────────────────────────────

    [Fact]
    public void Throw_Default_WhenDefault_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.Default(0, "n"));

    [Fact]
    public void Throw_Default_WhenNonDefault_DoesNotThrow()
        => Guard.Throw.Default(42, "n");

    [Fact]
    public void Throw_InvalidGuid_WhenEmpty_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.InvalidGuid(Guid.Empty, "id"));

    [Fact]
    public void Throw_InvalidGuid_WhenNonEmpty_DoesNotThrow()
        => Guard.Throw.InvalidGuid(Guid.NewGuid(), "id");

    // ── Format / Email ────────────────────────────────────────────────────────

    [Fact]
    public void Throw_InvalidFormat_WhenNoMatch_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.InvalidFormat("abc", @"^\d+$", "v"));

    [Fact]
    public void Throw_InvalidFormat_WhenMatches_DoesNotThrow()
        => Guard.Throw.InvalidFormat("123", @"^\d+$", "v");

    [Fact]
    public void Throw_Email_WhenInvalid_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.Email("notanemail", "email"));

    [Fact]
    public void Throw_Email_WhenValid_DoesNotThrow()
        => Guard.Throw.Email("user@example.com", "email");

    // ── Collections ───────────────────────────────────────────────────────────

    [Fact]
    public void Throw_Empty_WhenEmpty_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.Empty(Array.Empty<int>(), "items"));

    [Fact]
    public void Throw_Empty_WhenNonEmpty_DoesNotThrow()
        => Guard.Throw.Empty([1, 2], "items");

    [Fact]
    public void Throw_MaxCount_WhenExceedsMax_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.MaxCount([1, 2, 3, 4], 3, "items"));

    [Fact]
    public void Throw_MaxCount_WhenAtMax_DoesNotThrow()
        => Guard.Throw.MaxCount([1, 2, 3], 3, "items");

    [Fact]
    public void Throw_MinCount_WhenBelowMin_ThrowsDomainException()
        => Assert.Throws<DomainException>(() => Guard.Throw.MinCount([1], 3, "items"));

    [Fact]
    public void Throw_MinCount_WhenAtMin_DoesNotThrow()
        => Guard.Throw.MinCount([1, 2, 3], 3, "items");

    // ── Boolean predicate ─────────────────────────────────────────────────────

    [Fact]
    public void Throw_True_WhenConditionIsFalse_ThrowsDomainException()
    {
        var error = Error.Validation("test", "msg");
        Assert.Throws<DomainException>(() => Guard.Throw.True(false, error));
    }

    [Fact]
    public void Throw_True_WhenConditionIsTrue_DoesNotThrow()
    {
        var error = Error.Validation("test", "msg");
        Guard.Throw.True(true, error); // must not throw
    }

    [Fact]
    public void Throw_False_WhenConditionIsTrue_ThrowsDomainException()
    {
        var error = Error.Validation("test", "msg");
        Assert.Throws<DomainException>(() => Guard.Throw.False(true, error));
    }

    [Fact]
    public void Throw_False_WhenConditionIsFalse_DoesNotThrow()
    {
        var error = Error.Validation("test", "msg");
        Guard.Throw.False(false, error);
    }

    // ── SmartEnum ─────────────────────────────────────────────────────────────

    [Fact]
    public void Throw_InvalidSmartEnum_WhenUnknownValue_ThrowsDomainException()
    {
        _ = ThrowTestStatus.Active; // ensure static init
        Assert.Throws<DomainException>(() => Guard.Throw.InvalidSmartEnum<ThrowTestStatus, int>(99));
    }

    [Fact]
    public void Throw_InvalidSmartEnum_WhenKnownValue_DoesNotThrow()
    {
        _ = ThrowTestStatus.Active; // ensure static init
        Guard.Throw.InvalidSmartEnum<ThrowTestStatus, int>(1);
    }

    // ── DomainException carries correct Error ─────────────────────────────────

    [Fact]
    public void Throw_NullOrEmpty_DomainException_CarriesValidationError()
    {
        var ex = Assert.Throws<DomainException>(() => Guard.Throw.NullOrEmpty(null, "field"));
        Assert.Equal(ErrorType.Validation, ex.Error.Type);
    }

    // ── Fixture ───────────────────────────────────────────────────────────────

    private sealed class ThrowTestStatus : SmartEnum<ThrowTestStatus, int>
    {
        public static readonly ThrowTestStatus Active = new(nameof(Active), 1);

        private ThrowTestStatus(string name, int value) : base(name, value) { }
    }
}
