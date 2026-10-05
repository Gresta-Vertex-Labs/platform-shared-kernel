using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Primitives.Tests.Results;

public sealed class ValidationResultTests
{
    // ---- Non-generic ValidationResult ----

    [Fact]
    public void NonGeneric_Success_IsValid()
    {
        var result = ValidationResult.Success();
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void NonGeneric_Failure_IsNotValid()
    {
        var errors = new[] { Error.Validation("v.1", "required") };
        var result = ValidationResult.Failure(errors);
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void NonGeneric_Failure_MultipleErrors_PreservesAll()
    {
        var errors = new[]
        {
            Error.Validation("v.1", "required"),
            Error.Validation("v.2", "too long"),
        };
        var result = ValidationResult.Failure(errors);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void NonGeneric_Failure_EmptyErrors_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ValidationResult.Failure(Array.Empty<Error>()));
    }

    // ---- Generic ValidationResult<T> ----

    [Fact]
    public void Generic_Success_IsValidAndHasValue()
    {
        var result = ValidationResult<int>.Success(42);
        Assert.True(result.IsValid);
        Assert.Equal(42, result.Value);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Generic_Failure_IsNotValid()
    {
        var errors = new[] { Error.Validation("v.1", "required") };
        var result = ValidationResult<string>.Failure(errors);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Generic_Failure_Value_Throws()
    {
        var errors = new[] { Error.Validation("v.1", "required") };
        var result = ValidationResult<string>.Failure(errors);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Generic_Failure_EmptyErrors_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ValidationResult<int>.Failure(Array.Empty<Error>()));
    }

    // ---- Distinction from Result<T> ----

    [Fact]
    public void ValidationResult_IsDistinctType_FromResultT()
    {
        // ValidationResult and Result<T> are distinct types — cannot assign one to the other
        var vr = ValidationResult.Success();
        Assert.IsNotType<Primitives.Results.Result<bool>>(vr);
    }
}
