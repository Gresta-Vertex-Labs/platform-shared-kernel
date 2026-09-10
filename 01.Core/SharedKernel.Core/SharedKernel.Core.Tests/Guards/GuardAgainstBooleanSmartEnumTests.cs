using SharedKernel.Guards;
using SharedKernel.Primitives.Enums;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Core.Tests.Guards;

/// <summary>Tests for True/False boolean predicate guards and InvalidSmartEnum (T-17).</summary>
public sealed class GuardAgainstBooleanSmartEnumTests
{
    private static readonly Error _testError = Error.Validation("test.code", "Test error");

    // ── True ──────────────────────────────────────────────────────────────────

    [Fact]
    public void True_WhenConditionIsTrue_ReturnsNull()
    {
        Error? error = Guard.Against.True(true, _testError);
        Assert.Null(error);
    }

    [Fact]
    public void True_WhenConditionIsFalse_ReturnsError()
    {
        Error? error = Guard.Against.True(false, _testError);
        Assert.NotNull(error);
        Assert.Same(_testError, error);
    }

    // ── False ─────────────────────────────────────────────────────────────────

    [Fact]
    public void False_WhenConditionIsFalse_ReturnsNull()
    {
        Error? error = Guard.Against.False(false, _testError);
        Assert.Null(error);
    }

    [Fact]
    public void False_WhenConditionIsTrue_ReturnsError()
    {
        Error? error = Guard.Against.False(true, _testError);
        Assert.NotNull(error);
        Assert.Same(_testError, error);
    }

    // ── No allocation on pass ─────────────────────────────────────────────────

    [Fact]
    public void True_WhenPasses_ReturnsSameErrorReference_NoAllocation()
    {
        // On violation, returns the exact caller-supplied error instance (no boxing/wrapping)
        var callerError = Error.Validation("c.1", "caller");
        Error? result = Guard.Against.True(false, callerError);
        Assert.Same(callerError, result);
    }

    // ── InvalidSmartEnum ──────────────────────────────────────────────────────

    [Fact]
    public void InvalidSmartEnum_WhenKnownValue_ReturnsNull()
    {
        // Touch the type to trigger static field initialization (registers members)
        _ = TestStatus.Active;
        Error? error = Guard.Against.InvalidSmartEnum<TestStatus, int>(1);
        Assert.Null(error);
    }

    [Fact]
    public void InvalidSmartEnum_WhenUnknownValue_ReturnsError()
    {
        _ = TestStatus.Active; // ensure static init
        Error? error = Guard.Against.InvalidSmartEnum<TestStatus, int>(99);
        Assert.NotNull(error);
        Assert.Equal(ErrorType.Validation, error!.Type);
    }

    [Fact]
    public void InvalidSmartEnum_ErrorContainsEnumTypeName()
    {
        _ = TestStatus.Active; // ensure static init
        Error? error = Guard.Against.InvalidSmartEnum<TestStatus, int>(99);
        Assert.NotNull(error);
        Assert.Contains(nameof(TestStatus), error!.Message);
    }

    // ── Fixture ───────────────────────────────────────────────────────────────

    private sealed class TestStatus : SmartEnum<TestStatus, int>
    {
        public static readonly TestStatus Active   = new(nameof(Active),   1);
        public static readonly TestStatus Inactive = new(nameof(Inactive), 2);

        private TestStatus(string name, int value) : base(name, value) { }
    }
}
