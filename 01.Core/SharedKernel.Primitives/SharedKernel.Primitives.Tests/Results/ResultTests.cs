using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Primitives.Tests.Results;

public sealed class ResultTests
{
    // ---- Result<T> success path ----

    [Fact]
    public void Success_IsSuccess_True()
    {
        var result = Result<int>.Success(42);
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public void Success_Value_ReturnsValue()
    {
        var result = Result<string>.Success("hello");
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void Success_Error_Throws()
    {
        var result = Result<int>.Success(1);
        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    // ---- Result<T> failure path ----

    [Fact]
    public void Failure_IsFailure_True()
    {
        var error = Error.Unexpected("e.001", "boom");
        var result = Result<int>.Failure(error);
        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Failure_Error_ReturnsError()
    {
        var error = Error.NotFound("e.nf", "not found");
        var result = Result<string>.Failure(error);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Failure_Value_Throws()
    {
        var result = Result<int>.Failure(Error.Unexpected("x", "y"));
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    // ---- Implicit operators ----

    [Fact]
    public void ImplicitFromValue_CreatesSuccess()
    {
        Result<int> result = 99;
        Assert.True(result.IsSuccess);
        Assert.Equal(99, result.Value);
    }

    [Fact]
    public void ImplicitFromError_CreatesFailure()
    {
        var error = Error.Conflict("c.1", "conflict");
        Result<string> result = error;
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    // ---- Non-generic Result ----

    [Fact]
    public void NonGeneric_Success_IsSuccess()
    {
        var result = Result.Success();
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public void NonGeneric_Failure_IsFailure()
    {
        var error = Error.Unauthorized("u.1", "denied");
        var result = Result.Failure(error);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void NonGeneric_Success_Error_Throws()
    {
        var result = Result.Success();
        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    [Fact]
    public void NonGeneric_ImplicitFromError_CreatesFailure()
    {
        var error = Error.Validation("v.1", "invalid");
        Result result = error;
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }
}
