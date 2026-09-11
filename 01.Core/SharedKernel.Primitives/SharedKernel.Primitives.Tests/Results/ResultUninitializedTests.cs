using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Primitives.Tests.Results;

/// <summary>
/// Pins the behaviour of an uninitialized <see cref="Result"/> — the all-zero struct value that
/// ran neither factory.
/// </summary>
/// <remarks>
/// <para>
/// Before this was fixed, <c>default(Result)</c> reported <c>IsFailure == true</c> and returned
/// <see langword="null"/> from <see cref="Result.Error"/>. That broke
/// <see cref="Error"/>'s own documented "never null" contract and surfaced as a
/// <see cref="NullReferenceException"/> at whichever call site later touched
/// <c>result.Error.Code</c> — typically the ProblemDetails mapping at the HTTP boundary, arbitrarily
/// far from the code that produced the bad value. <see cref="Result"/>'s own XML docs asserted the
/// opposite: that the zero-value problem did not apply to it.
/// </para>
/// <para>
/// The tests below deliberately cover the ways the value arises WITHOUT anyone writing
/// <c>default</c>, because those are the ones that actually reach production.
/// </para>
/// </remarks>
public sealed class ResultUninitializedTests
{
    [Fact]
    public void Default_ReportsFailure()
    {
        // Not a bug on its own, but it is why the Error access below has to be defended: the value
        // looks like an ordinary failure to every caller that checks IsFailure first.
        Assert.False(default(Result).IsSuccess);
        Assert.True(default(Result).IsFailure);
    }

    [Fact]
    public void Default_AccessingError_ThrowsNamingTheCause()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => default(Result).Error);

        Assert.Contains("default(Result)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_AccessingError_DoesNotThrowNullReference()
    {
        // The regression this whole file exists for. A NullReferenceException here means the
        // null-coalescing guard was removed and the old defect is back.
        var exception = Record.Exception(() => default(Result).Error);

        Assert.NotNull(exception);
        Assert.IsNotType<NullReferenceException>(exception);
    }

    [Fact]
    public void FailedTryGetValue_OutParameter_ThrowsOnError()
    {
        // The most realistic route in: nobody writes default(Result), but plenty of code reads an
        // out-parameter without checking the bool first.
        var lookup = new Dictionary<string, Result>();

        var found = lookup.TryGetValue("absent", out var result);

        Assert.False(found);
        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void UnpopulatedArrayElement_ThrowsOnError()
    {
        var results = new Result[1];

        Assert.True(results[0].IsFailure);
        Assert.Throws<InvalidOperationException>(() => results[0].Error);
    }

    [Fact]
    public void ProperlyConstructedFailure_StillExposesItsError()
    {
        // Guards against over-correcting: the fix must not make a real failure throw.
        var error = Error.NotFound("order.not_found", "Order does not exist.");

        var result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void SuccessResult_AccessingError_StillThrowsTheSuccessMessage()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Result.Success().Error);

        Assert.Contains("successful result", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("default(Result)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericResult_Default_IsNullAndFailsImmediately()
    {
        // Result<T> is a class precisely so its uninitialized value cannot masquerade as a
        // failure. Documented on the type; pinned here so a future change to a struct would fail.
        Result<int>? uninitialized = default;

        Assert.Null(uninitialized);
    }
}
