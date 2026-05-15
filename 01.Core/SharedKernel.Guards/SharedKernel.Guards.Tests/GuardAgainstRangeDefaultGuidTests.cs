using SharedKernel.Guards;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Guards.Tests;

/// <summary>Tests for OutOfRange, Default, and InvalidGuid guard extensions (T-14).</summary>
public sealed class GuardAgainstRangeDefaultGuidTests
{
    // ── OutOfRange<T> ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(5,  1,  10)]  // within range      → pass
    [InlineData(1,  1,  10)]  // at lower bound    → pass
    [InlineData(10, 1,  10)]  // at upper bound    → pass
    public void OutOfRange_WhenWithinBounds_ReturnsNull(int value, int min, int max)
    {
        Error? error = Guard.Against.OutOfRange(value, min, max, "v");
        Assert.Null(error);
    }

    [Theory]
    [InlineData(0,   1,  10)]  // one below lower bound → violation
    [InlineData(11,  1,  10)]  // one above upper bound → violation
    [InlineData(-1,  0,   5)]  // negative below range  → violation
    public void OutOfRange_WhenOutsideBounds_ReturnsError(int value, int min, int max)
    {
        Error? error = Guard.Against.OutOfRange(value, min, max, "v");
        Assert.NotNull(error);
        Assert.Equal(ErrorType.Validation, error!.Type);
    }

    [Fact]
    public void OutOfRange_WorksWithDateTimeOffset()
    {
        var past   = DateTimeOffset.UtcNow.AddDays(-1);
        var now    = DateTimeOffset.UtcNow;
        var future = DateTimeOffset.UtcNow.AddDays(1);

        Assert.Null(Guard.Against.OutOfRange(now, past, future, "dt"));
        Assert.NotNull(Guard.Against.OutOfRange(past.AddDays(-1), past, future, "dt"));
    }

    // ── Default<T> ────────────────────────────────────────────────────────────

    [Fact]
    public void Default_WhenIntIsZero_ReturnsError()
    {
        Error? error = Guard.Against.Default(0, "n");
        Assert.NotNull(error);
    }

    [Fact]
    public void Default_WhenIntIsNonZero_ReturnsNull()
    {
        Error? error = Guard.Against.Default(1, "n");
        Assert.Null(error);
    }

    [Fact]
    public void Default_WhenStringIsNull_ReturnsError()
    {
        Error? error = Guard.Against.Default<string>(null!, "s");
        Assert.NotNull(error);
    }

    [Fact]
    public void Default_WhenStringIsNonEmpty_ReturnsNull()
    {
        Error? error = Guard.Against.Default("hello", "s");
        Assert.Null(error);
    }

    // ── InvalidGuid ───────────────────────────────────────────────────────────

    [Fact]
    public void InvalidGuid_WhenEmpty_ReturnsError()
    {
        Error? error = Guard.Against.InvalidGuid(Guid.Empty, "id");
        Assert.NotNull(error);
        Assert.Contains("id", error!.Message);
    }

    [Fact]
    public void InvalidGuid_WhenNonEmpty_ReturnsNull()
    {
        Error? error = Guard.Against.InvalidGuid(Guid.NewGuid(), "id");
        Assert.Null(error);
    }
}
