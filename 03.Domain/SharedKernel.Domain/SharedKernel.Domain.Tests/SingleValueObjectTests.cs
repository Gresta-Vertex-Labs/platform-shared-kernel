using FluentAssertions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.ValueObjects;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-19: P-046/WO-011 — SingleValueObject&lt;TValue&gt; tests.
/// </summary>
public class SingleValueObjectTests
{
    // --- Valid email value object ---

    private sealed class EmailAddress : SingleValueObject<string>
    {
        public EmailAddress(string value) : base(value) { }

        protected override IEnumerable<Error>? Validate()
        {
            if (string.IsNullOrWhiteSpace(Value))
                yield return Error.Validation("Email.Required", "Email address is required.");
            else if (!Value.Contains('@'))
                yield return Error.Validation("Email.InvalidFormat", "Email address is invalid.");
        }
    }

    // --- Value object that verifies Value is accessible in Validate() ---

    private sealed class VerifyValueInValidate : SingleValueObject<int>
    {
        public int ValueSeenInValidate { get; private set; }

        public VerifyValueInValidate(int value) : base(value) { }

        protected override IEnumerable<Error>? Validate()
        {
            // This value must equal the constructor argument — not 0 (default)
            ValueSeenInValidate = Value;
            return null;
        }
    }

    // --- Value object with validation failure ---

    private sealed class PositiveInteger : SingleValueObject<int>
    {
        public PositiveInteger(int value) : base(value) { }

        protected override IEnumerable<Error>? Validate()
        {
            if (Value <= 0)
                yield return Error.Validation("Int.NotPositive", "Value must be positive.");
        }
    }

    // --- Tests ---

    [Fact]
    public void SingleValueObject_Value_SetCorrectly()
    {
        var email = new EmailAddress("test@example.com");
        email.Value.Should().Be("test@example.com");
    }

    [Fact]
    public void SingleValueObject_Equality_SameValue_Equal()
    {
        var a = new EmailAddress("user@domain.com");
        var b = new EmailAddress("user@domain.com");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void SingleValueObject_Equality_DifferentValue_NotEqual()
    {
        var a = new EmailAddress("a@domain.com");
        var b = new EmailAddress("b@domain.com");

        a.Should().NotBe(b);
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void SingleValueObject_ExplicitOperator_UnwrapsValue()
    {
        var email = new EmailAddress("hello@example.com");
        var unwrapped = (string)email;

        unwrapped.Should().Be("hello@example.com");
    }

    [Fact]
    public void SingleValueObject_ToString_ReturnsValueString()
    {
        var email = new EmailAddress("test@example.com");
        email.ToString().Should().Be("test@example.com");
    }

    [Fact]
    public void SingleValueObject_Validate_FailureThrows_ValidationException()
    {
        var act = () => new EmailAddress("not-an-email");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void SingleValueObject_Validate_Success_DoesNotThrow()
    {
        var act = () => new EmailAddress("valid@example.com");

        act.Should().NotThrow();
    }

    [Fact]
    public void SingleValueObject_Value_IsAccessible_InValidate_AtConstruction()
    {
        // The critical construction-order test: Value must be non-default when Validate() runs.
        var obj = new VerifyValueInValidate(42);

        obj.ValueSeenInValidate.Should().Be(42,
            "Value must be set before Validate() is called (construction-order fix)");
        obj.Value.Should().Be(42);
    }

    [Fact]
    public void SingleValueObject_PositiveInteger_Negative_ThrowsValidationException()
    {
        var act = () => new PositiveInteger(-1);
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void SingleValueObject_PositiveInteger_Valid_Succeeds()
    {
        var obj = new PositiveInteger(5);
        obj.Value.Should().Be(5);
    }

    [Fact]
    public void SingleValueObject_GetHashCode_ConsistentWithEquality()
    {
        var a = new EmailAddress("x@example.com");
        var b = new EmailAddress("x@example.com");

        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
