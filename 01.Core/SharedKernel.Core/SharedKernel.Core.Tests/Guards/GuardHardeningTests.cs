using System.Globalization;
using System.Reflection;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Core.Tests.Guards;

/// <summary>
/// Null inputs, invariant messages, error codes, caller-supplied names, generic math, and the guards and
/// bridges added before the first release.
/// </summary>
public sealed class GuardHardeningTests
{
    private enum Color
    {
        Red = 1,
        Green = 2,
    }

    // ---- A guard never throws on null input ----

    [Fact]
    public void FunctionalGuards_NullInput_ReturnRequiredErrorInsteadOfThrowing()
    {
        string? text = null;
        IEnumerable<int>? items = null;

        Error?[] errors =
        [
            Guard.Against.ShorterThan(text, 3),
            Guard.Against.LongerThan(text, 3),
            Guard.Against.InvalidFormat(text, "^a$"),
            Guard.Against.Empty(items),
            Guard.Against.MinCount(items, 1),
            Guard.Against.MaxCount(items, 1),
            Guard.Against.OutOfRange(text!, "a", "z"),
            Guard.Against.LessThan(text!, "a"),
            Guard.Against.GreaterThan(text!, "z"),
        ];

        Assert.All(errors, e =>
        {
            Assert.NotNull(e);
            Assert.Equal(ErrorCodes.Validation.Required, e!.Code);
        });
    }

    // ---- Error codes and messages ----

    [Fact]
    public void LengthAndFormatGuards_UseSpecificErrorCodes()
    {
        Assert.Equal(ErrorCodes.Validation.MinLength, Guard.Against.ShorterThan("ab", 3)!.Code);
        Assert.Equal(ErrorCodes.Validation.MaxLength, Guard.Against.LongerThan("abcd", 3)!.Code);
        Assert.Equal(ErrorCodes.Validation.InvalidFormat, Guard.Against.InvalidFormat("b", "^a$")!.Code);
        Assert.Equal(ErrorCodes.Validation.InvalidFormat, Guard.Against.Email("not-an-email")!.Code);
        Assert.Equal(ErrorCodes.Validation.Required, Guard.Against.Email("  ")!.Code);
    }

    [Fact]
    public void Messages_AreFormattedWithInvariantCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            var error = Guard.Against.OutOfRange(5.5m, 1.25m, 2.5m, "amount");

            Assert.Equal("'amount' must be between 1.25 and 2.5 (inclusive).", error!.Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void InvalidFormat_MessageDoesNotRevealPattern()
    {
        var error = Guard.Against.InvalidFormat("x", @"^\d{4}-internal$", "code");

        Assert.DoesNotContain("internal", error!.Message);
    }

    // ---- Caller argument expression ----

    [Fact]
    public void ParamName_IsCapturedFromArgumentExpression()
    {
        var request = new { CustomerName = (string?)null };

        var error = Guard.Against.NullOrWhiteSpace(request.CustomerName);

        Assert.Equal("'request.CustomerName' must not be null, empty, or whitespace.", error!.Message);
    }

    [Fact]
    public void Throw_ParamName_IsCapturedFromCallerNotFromWrapper()
    {
        string? customerEmail = "nope";

        var thrown = Assert.Throws<DomainException>(() => Guard.Throw.Email(customerEmail));

        Assert.Contains("'customerEmail'", thrown.Message);
    }

    [Fact]
    public void ExplicitParamName_StillWins()
        => Assert.Contains("'custom'", Guard.Against.Null<object>(null, "custom")!.Message);

    // ---- Generic math ----

    [Fact]
    public void NumericGuards_WorkForAnyNumericType()
    {
        Assert.NotNull(Guard.Against.Negative((short)-1));
        Assert.NotNull(Guard.Against.NegativeOrZero(0f));
        Assert.Null(Guard.Against.NegativeOrZero(0.1));
        Assert.Null(Guard.Against.Negative(0L));
        Assert.NotNull(Guard.Against.Negative(-0.01m));
    }

    [Fact]
    public void NumericGuards_NaN_IsAViolation()
    {
        Assert.NotNull(Guard.Against.Negative(double.NaN));
        Assert.NotNull(Guard.Against.NegativeOrZero(float.NaN));
        Assert.NotNull(Guard.Against.OutOfRange(double.NaN, 0d, 1d));
    }

    // ---- New guards ----

    [Fact]
    public void LessThanAndGreaterThan_AreInclusiveBounds()
    {
        Assert.NotNull(Guard.Against.LessThan(4, 5));
        Assert.Null(Guard.Against.LessThan(5, 5));
        Assert.NotNull(Guard.Against.GreaterThan(6, 5));
        Assert.Null(Guard.Against.GreaterThan(5, 5));
    }

    [Fact]
    public void InvalidEnumValue_RejectsUndefinedCast()
    {
        Assert.Null(Guard.Against.InvalidEnumValue(Color.Green));

        var error = Guard.Against.InvalidEnumValue((Color)99, "color");

        Assert.Equal("'color' is not a defined Color value.", error!.Message);
    }

    [Fact]
    public void NotUtc_DateTimeOffset_RequiresZeroOffset()
    {
        Assert.Null(Guard.Against.NotUtc(DateTimeOffset.UtcNow));
        Assert.NotNull(Guard.Against.NotUtc(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(3))));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void NotUtc_DateTime_RejectsNonUtcKinds(DateTimeKind kind)
        => Assert.NotNull(Guard.Against.NotUtc(new DateTime(2026, 1, 1, 0, 0, 0, kind)));

    [Fact]
    public void Null_NullableValueType_IsChecked()
    {
        int? missing = null;
        int? present = 0;

        Assert.NotNull(Guard.Against.Null(missing));
        Assert.Null(Guard.Against.Null(present));
    }

    [Fact]
    public void MaxCount_LazySequence_StopsReadingAfterMaxPlusOne()
    {
        var read = 0;
        IEnumerable<int> Endless()
        {
            while (true)
                yield return ++read;
        }

        Assert.NotNull(Guard.Against.MaxCount(Endless(), 3));
        Assert.Equal(4, read);
    }

    [Fact]
    public void MinCount_LazySequence_StopsReadingAtMin()
    {
        var read = 0;
        IEnumerable<int> Endless()
        {
            while (true)
                yield return ++read;
        }

        Assert.Null(Guard.Against.MinCount(Endless(), 2));
        Assert.Equal(2, read);
    }

    // ---- Collect and ToResult ----

    [Fact]
    public void Collect_KeepsEveryFailureInOrder()
    {
        var validation = Guard.Collect(
            Guard.Against.NullOrWhiteSpace("", "name"),
            Guard.Against.Email("valid@example.com"),
            Guard.Against.Negative(-1, "age"));

        Assert.False(validation.IsValid);
        Assert.Equal(2, validation.Errors.Count);
        Assert.Contains("'name'", validation.Errors[0].Message);
        Assert.Contains("'age'", validation.Errors[1].Message);
    }

    [Fact]
    public void Collect_AllPassed_ReturnsSuccess()
        => Assert.True(Guard.Collect(null, null).IsValid);

    [Fact]
    public void ToResult_Passed_ReturnsSuccessWithValue()
    {
        Result<int> result = Guard.Against.Negative(3).ToResult(3);

        Assert.Equal(3, result.Value);
    }

    [Fact]
    public void ToResult_Factory_NotInvokedWhenAGuardFailed()
    {
        var created = false;

        Result<object> result = Guard.Against.NullOrWhiteSpace("").ToResult(() =>
        {
            created = true;
            return new object();
        });

        Assert.True(result.IsFailure);
        Assert.False(created);
    }

    [Fact]
    public void ToResult_NonGeneric_MapsNullToSuccess()
    {
        Assert.True(((Error?)null).ToResult().IsSuccess);
        Assert.True(Guard.Against.True(false, Error.Validation("v", "m")).ToResult().IsFailure);
    }

    // ---- Throw path ----

    [Fact]
    public void Throw_Null_LetsTheCompilerTreatValueAsNonNull()
    {
        string? value = "present";

        Guard.Throw.Null(value);

        // Compiles without a nullable warning only because of [NotNull].
        Assert.Equal(7, value.Length);
    }

    [Fact]
    public void ThrowPath_MirrorsEveryFunctionalGuard()
    {
        static IEnumerable<string> Names(IEnumerable<MethodInfo> methods)
            => methods.Where(m => m.IsPublic && m.IsStatic).Select(m => m.Name).Distinct().Order();

        var functional = Names(typeof(GuardClauseExtensions).GetMethods());
        var imperative = Names(typeof(Guard.Throw).GetMethods());

        Assert.Equal(functional, imperative);
    }
}
