using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Primitives.Tests.Results;

/// <summary>
/// Covers T-29 and T-30: IHasSuccessFlag and IResultOfT&lt;T&gt; interface contracts.
/// </summary>
public sealed class ResultInterfaceTests
{
    // ---- T-29: IHasSuccessFlag ----

    [Fact]
    public void ResultOfT_IsAssignableTo_IHasSuccessFlag()
    {
        var success = Result<int>.Success(1);
        Assert.IsAssignableFrom<IHasSuccessFlag>(success);
    }

    [Fact]
    public void NonGenericResult_IsAssignableTo_IHasSuccessFlag()
    {
        var success = Result.Success();
        // Struct — box to object, then check
        object boxed = success;
        Assert.IsAssignableFrom<IHasSuccessFlag>(boxed);
    }

    [Fact]
    public void IHasSuccessFlag_FromSuccessResultOfT_ReflectsIsSuccess()
    {
        IHasSuccessFlag flagged = Result<string>.Success("ok");
        // IHasSuccessFlag is a zero-member marker — we confirm identity and then check IsSuccess
        // via the concrete instance that was returned to us (pipeline behavior pattern: is + concrete check)
        var typed = (Result<string>)flagged;
        Assert.True(typed.IsSuccess);
        Assert.False(typed.IsFailure);
    }

    [Fact]
    public void IHasSuccessFlag_FromFailureResultOfT_ReflectsIsFailure()
    {
        IHasSuccessFlag flagged = Result<int>.Failure(Error.Unexpected("e.1", "fail"));
        var typed = (Result<int>)flagged;
        Assert.False(typed.IsSuccess);
        Assert.True(typed.IsFailure);
    }

    [Fact]
    public void IHasSuccessFlag_FromSuccessNonGenericResult_ReflectsIsSuccess()
    {
        // Box the struct to assign to the interface
        IHasSuccessFlag flagged = (IHasSuccessFlag)(object)Result.Success();
        var typed = (Result)flagged;
        Assert.True(typed.IsSuccess);
        Assert.False(typed.IsFailure);
    }

    [Fact]
    public void IHasSuccessFlag_FromFailureNonGenericResult_ReflectsIsFailure()
    {
        IHasSuccessFlag flagged = (IHasSuccessFlag)(object)Result.Failure(Error.Validation("v.1", "bad"));
        var typed = (Result)flagged;
        Assert.False(typed.IsSuccess);
        Assert.True(typed.IsFailure);
    }

    // ---- T-30: IResultOfT<T> ----

    [Fact]
    public void ResultOfT_IsAssignableTo_IResultOfT()
    {
        var success = Result<string>.Success("hello");
        Assert.IsAssignableFrom<IResultOfT<string>>(success);
    }

    [Fact]
    public void NonGenericResult_IsNotAssignableTo_IResultOfT()
    {
        // Result (non-generic readonly struct) must NOT implement IResultOfT<T>
        var result = Result.Success();
        Assert.False(result is IResultOfT<object>);
    }

    [Fact]
    public void IResultOfT_Success_IsSuccess_True()
    {
        IResultOfT<int> typed = Result<int>.Success(42);
        Assert.True(typed.IsSuccess);
        Assert.False(typed.IsFailure);
    }

    [Fact]
    public void IResultOfT_Success_Value_ReturnsValue()
    {
        IResultOfT<string> typed = Result<string>.Success("world");
        Assert.Equal("world", typed.Value);
    }

    [Fact]
    public void IResultOfT_Failure_IsFailure_True()
    {
        IResultOfT<int> typed = Result<int>.Failure(Error.NotFound("nf.1", "not found"));
        Assert.False(typed.IsSuccess);
        Assert.True(typed.IsFailure);
    }

    [Fact]
    public void IResultOfT_Failure_Value_ThrowsInvalidOperationException()
    {
        IResultOfT<int> typed = Result<int>.Failure(Error.Unexpected("e.2", "oops"));
        Assert.Throws<InvalidOperationException>(() => _ = typed.Value);
    }

    [Fact]
    public void IResultOfT_SuccessAndIHasSuccessFlag_BothSatisfied()
    {
        // A single Result<T> instance satisfies both interfaces simultaneously
        var result = Result<decimal>.Success(3.14m);
        Assert.IsAssignableFrom<IHasSuccessFlag>(result);
        Assert.IsAssignableFrom<IResultOfT<decimal>>(result);
    }

    [Fact]
    public void IResultOfT_GenericConstraintPattern_WorksWithoutReflection()
    {
        // Simulate the pipeline-behavior generic-constraint usage:
        // InspectResult<TResponse> where TResponse : IResultOfT<TResponse>
        // This proves the constraint resolves at compile time, not runtime.
        var result = Result<int>.Success(7);
        var isSuccess = InspectResult(result);
        Assert.True(isSuccess);

        var failed = Result<int>.Failure(Error.Conflict("c.1", "conflict"));
        Assert.False(InspectResult(failed));
    }

    // Helper that mirrors the generic constraint pattern used in 05.Application pipeline behaviors
    private static bool InspectResult<TResponse>(TResponse response)
        where TResponse : IResultOfT<TResponse>
        => response.IsSuccess;
}
