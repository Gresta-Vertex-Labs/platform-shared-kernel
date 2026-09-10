using SharedKernel.Guards;
using SharedKernel.Guards.Clauses;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Core.Tests.Guards;

/// <summary>Tests for null/empty/whitespace functional guard path (T-11).</summary>
public sealed class GuardAgainstNullTests
{
    // ── IGuardClause is a marker interface ────────────────────────────────────

    [Fact]
    public void Against_ReturnsIGuardClause()
    {
        IGuardClause clause = Guard.Against;
        Assert.NotNull(clause);
    }

    [Fact]
    public void Against_AlwaysReturnsSameInstance()
    {
        // Singleton — no allocation per call
        Assert.Same(Guard.Against, Guard.Against);
    }

    // ── Null<T> ───────────────────────────────────────────────────────────────

    [Fact]
    public void Null_WhenValueIsNull_ReturnsNonNullError()
    {
        object? value = null;
        Error? error = Guard.Against.Null(value, "value");
        Assert.NotNull(error);
        Assert.Equal(ErrorType.Validation, error.Type);
    }

    [Fact]
    public void Null_WhenValueIsNotNull_ReturnsNull()
    {
        object value = new();
        Error? error = Guard.Against.Null(value, "value");
        Assert.Null(error);
    }

    [Fact]
    public void Null_ErrorContainsParamName()
    {
        Error? error = Guard.Against.Null<string>(null, "myParam");
        Assert.NotNull(error);
        Assert.Contains("myParam", error!.Message);
    }

    // ── NullOrEmpty ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullOrEmpty_WhenNullOrEmpty_ReturnsNonNullError(string? value)
    {
        Error? error = Guard.Against.NullOrEmpty(value, "s");
        Assert.NotNull(error);
    }

    [Fact]
    public void NullOrEmpty_WhenNotEmpty_ReturnsNull()
    {
        Error? error = Guard.Against.NullOrEmpty("hello", "s");
        Assert.Null(error);
    }

    // ── NullOrWhiteSpace ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void NullOrWhiteSpace_WhenNullOrWhiteSpace_ReturnsNonNullError(string? value)
    {
        Error? error = Guard.Against.NullOrWhiteSpace(value, "s");
        Assert.NotNull(error);
    }

    [Fact]
    public void NullOrWhiteSpace_WhenHasContent_ReturnsNull()
    {
        Error? error = Guard.Against.NullOrWhiteSpace("text", "s");
        Assert.Null(error);
    }

    // ── Null never returns Error.None ────────────────────────────────────────

    [Fact]
    public void NullGuard_OnPass_ReturnsActualNull_NotErrorNone()
    {
        // The contract is: null == passed, not Error.None
        Error? result = Guard.Against.Null(new object(), "x");
        Assert.Null(result);
        // If we had returned Error.None this would be non-null
    }
}
