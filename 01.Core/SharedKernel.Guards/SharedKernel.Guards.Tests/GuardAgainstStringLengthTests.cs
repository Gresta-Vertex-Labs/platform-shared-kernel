using SharedKernel.Guards;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Guards.Tests;

/// <summary>Tests for string length guard extensions (T-12).</summary>
public sealed class GuardAgainstStringLengthTests
{
    // ── ShorterThan ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ab",  3)]   // one below min → violation
    [InlineData("",    1)]   // empty, min=1  → violation
    public void ShorterThan_WhenBelowMinLength_ReturnsError(string value, int minLength)
    {
        Error? error = Guard.Against.ShorterThan(value, minLength, "s");
        Assert.NotNull(error);
        Assert.Equal(ErrorType.Validation, error!.Type);
    }

    [Theory]
    [InlineData("abc", 3)]   // exactly at min → pass
    [InlineData("abcd", 3)]  // one above min  → pass
    public void ShorterThan_WhenAtOrAboveMinLength_ReturnsNull(string value, int minLength)
    {
        Error? error = Guard.Against.ShorterThan(value, minLength, "s");
        Assert.Null(error);
    }

    // ── LongerThan ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("abcd",  3)]  // one above max → violation
    [InlineData("abcde", 3)]  // two above max → violation
    public void LongerThan_WhenAboveMaxLength_ReturnsError(string value, int maxLength)
    {
        Error? error = Guard.Against.LongerThan(value, maxLength, "s");
        Assert.NotNull(error);
        Assert.Equal(ErrorType.Validation, error!.Type);
    }

    [Theory]
    [InlineData("abc", 3)]  // exactly at max → pass
    [InlineData("ab",  3)]  // one below max  → pass
    public void LongerThan_WhenAtOrBelowMaxLength_ReturnsNull(string value, int maxLength)
    {
        Error? error = Guard.Against.LongerThan(value, maxLength, "s");
        Assert.Null(error);
    }

    // ── Error messages include param name ─────────────────────────────────────

    [Fact]
    public void ShorterThan_ErrorContainsParamNameAndMinLength()
    {
        Error? error = Guard.Against.ShorterThan("a", 5, "myField");
        Assert.NotNull(error);
        Assert.Contains("myField", error!.Message);
        Assert.Contains("5", error.Message);
    }

    [Fact]
    public void LongerThan_ErrorContainsParamNameAndMaxLength()
    {
        Error? error = Guard.Against.LongerThan("toolongstring", 3, "myField");
        Assert.NotNull(error);
        Assert.Contains("myField", error!.Message);
        Assert.Contains("3", error.Message);
    }
}
