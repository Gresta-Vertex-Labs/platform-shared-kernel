using SharedKernel.Guards;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Guards.Tests;

/// <summary>Tests for numeric guard extensions — int, decimal, long (T-13).</summary>
public sealed class GuardAgainstNumericTests
{
    // ── int: NegativeOrZero ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NegativeOrZero_Int_WhenZeroOrNegative_ReturnsError(int value)
    {
        Error? error = Guard.Against.NegativeOrZero(value, "n");
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void NegativeOrZero_Int_WhenPositive_ReturnsNull(int value)
    {
        Error? error = Guard.Against.NegativeOrZero(value, "n");
        Assert.Null(error);
    }

    // ── int: Negative ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Negative_Int_WhenNegative_ReturnsError(int value)
    {
        Error? error = Guard.Against.Negative(value, "n");
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Negative_Int_WhenZeroOrPositive_ReturnsNull(int value)
    {
        Error? error = Guard.Against.Negative(value, "n");
        Assert.Null(error);
    }

    // ── int: NotPositive ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NotPositive_Int_WhenZeroOrNegative_ReturnsError(int value)
    {
        Error? error = Guard.Against.NotPositive(value, "n");
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void NotPositive_Int_WhenPositive_ReturnsNull(int value)
    {
        Error? error = Guard.Against.NotPositive(value, "n");
        Assert.Null(error);
    }

    // ── decimal ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.01)]
    public void NegativeOrZero_Decimal_WhenZeroOrNegative_ReturnsError(double rawValue)
    {
        decimal value = (decimal)rawValue;
        Error? error = Guard.Against.NegativeOrZero(value, "d");
        Assert.NotNull(error);
    }

    [Fact]
    public void NegativeOrZero_Decimal_WhenPositive_ReturnsNull()
    {
        Error? error = Guard.Against.NegativeOrZero(0.01m, "d");
        Assert.Null(error);
    }

    [Theory]
    [InlineData(-0.01)]
    public void Negative_Decimal_WhenNegative_ReturnsError(double rawValue)
    {
        Error? error = Guard.Against.Negative((decimal)rawValue, "d");
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void Negative_Decimal_WhenZeroOrPositive_ReturnsNull(double rawValue)
    {
        Error? error = Guard.Against.Negative((decimal)rawValue, "d");
        Assert.Null(error);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void NotPositive_Decimal_WhenZeroOrNegative_ReturnsError(double rawValue)
    {
        Error? error = Guard.Against.NotPositive((decimal)rawValue, "d");
        Assert.NotNull(error);
    }

    [Fact]
    public void NotPositive_Decimal_WhenPositive_ReturnsNull()
    {
        Error? error = Guard.Against.NotPositive(0.01m, "d");
        Assert.Null(error);
    }

    // ── long ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void NegativeOrZero_Long_WhenZeroOrNegative_ReturnsError(long value)
    {
        Error? error = Guard.Against.NegativeOrZero(value, "l");
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(long.MaxValue)]
    public void NegativeOrZero_Long_WhenPositive_ReturnsNull(long value)
    {
        Error? error = Guard.Against.NegativeOrZero(value, "l");
        Assert.Null(error);
    }

    [Theory]
    [InlineData(-1L)]
    public void Negative_Long_WhenNegative_ReturnsError(long value)
    {
        Error? error = Guard.Against.Negative(value, "l");
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    public void Negative_Long_WhenZeroOrPositive_ReturnsNull(long value)
    {
        Error? error = Guard.Against.Negative(value, "l");
        Assert.Null(error);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void NotPositive_Long_WhenZeroOrNegative_ReturnsError(long value)
    {
        Error? error = Guard.Against.NotPositive(value, "l");
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(1L)]
    public void NotPositive_Long_WhenPositive_ReturnsNull(long value)
    {
        Error? error = Guard.Against.NotPositive(value, "l");
        Assert.Null(error);
    }
}
