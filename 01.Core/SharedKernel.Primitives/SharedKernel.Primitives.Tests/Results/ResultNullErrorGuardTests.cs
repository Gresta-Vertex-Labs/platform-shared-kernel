using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Primitives.Tests.Results;

/// <summary>
/// Pins that no factory or conversion can build a failure carrying a <see langword="null"/> error.
/// </summary>
/// <remarks>
/// <see cref="Error"/>'s contract is explicit that <see cref="Error.None"/> — never
/// <see langword="null"/> — expresses the absence of an error. The failure factories previously
/// took the argument unchecked, so <c>Result.Failure(null!)</c> deliberately constructed the same
/// broken state that <see cref="ResultUninitializedTests"/> covers arising accidentally. Both
/// routes are now closed, which is what lets <c>Result&lt;T&gt;.Error</c> document itself as
/// never-null without a runtime guard of its own.
/// </remarks>
public sealed class ResultNullErrorGuardTests
{
    [Fact]
    public void GenericFailure_NullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result<int>.Failure(null!));
    }

    [Fact]
    public void NonGenericFailure_NullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
    }

    [Fact]
    public void GenericImplicitConversion_NullError_Throws()
    {
        // The implicit operator is the path most call sites actually take, via `return error;`.
        Assert.Throws<ArgumentNullException>(() =>
        {
            Result<int> result = (Error)null!;
            return result;
        });
    }

    [Fact]
    public void NonGenericImplicitConversion_NullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            Result result = (Error)null!;
            return result;
        });
    }

    [Fact]
    public void GenericFailure_RealError_IsUnaffected()
    {
        var error = Error.Conflict(ErrorCodes.Conflict.Duplicate, "Already exists.");

        var result = Result<int>.Failure(error);

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void GenericSuccess_HoldsErrorNoneInternally_AndRefusesToExposeIt()
    {
        // Success stores Error.None rather than null, which is why Error can be documented as
        // never-null even though reading it on a success is still a programming error.
        var result = Result<int>.Success(7);

        Assert.True(result.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }
}
