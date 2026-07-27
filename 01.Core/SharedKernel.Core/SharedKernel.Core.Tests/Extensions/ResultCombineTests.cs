using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Core.Tests.Extensions;

public sealed class ResultCombineTests
{
    // ---- Non-generic Combine — params overload ----

    [Fact]
    public void Combine_Params_AllSuccess_ReturnsSuccessfulValidationResult()
    {
        var combined = ResultCombine.Combine(Result.Success(), Result.Success(), Result.Success());

        Assert.True(combined.IsValid);
        Assert.Empty(combined.Errors);
    }

    [Fact]
    public void Combine_Params_SingleFailureAmongMixed_SurfacesThatOneError()
    {
        var error = Error.Validation("field.required", "Field is required.");

        var combined = ResultCombine.Combine(Result.Success(), Result.Failure(error), Result.Success());

        Assert.False(combined.IsValid);
        Assert.Single(combined.Errors);
        Assert.Equal(error, combined.Errors[0]);
    }

    [Fact]
    public void Combine_Params_AllFailure_SurfacesEveryError()
    {
        var error1 = Error.Validation("field.a", "A is invalid.");
        var error2 = Error.Validation("field.b", "B is invalid.");
        var error3 = Error.Validation("field.c", "C is invalid.");

        var combined = ResultCombine.Combine(
            Result.Failure(error1),
            Result.Failure(error2),
            Result.Failure(error3));

        Assert.False(combined.IsValid);
        Assert.Equal([error1, error2, error3], combined.Errors);
    }

    // ---- Non-generic Combine — IEnumerable overload ----

    [Fact]
    public void Combine_IEnumerable_AllSuccess_ReturnsSuccessfulValidationResult()
    {
        var results = new List<Result> { Result.Success(), Result.Success() };

        var combined = ResultCombine.Combine(results);

        Assert.True(combined.IsValid);
        Assert.Empty(combined.Errors);
    }

    [Fact]
    public void Combine_IEnumerable_AllFailure_SurfacesEveryError()
    {
        var error1 = Error.Conflict("conflict.a", "A conflicts.");
        var error2 = Error.Conflict("conflict.b", "B conflicts.");
        var results = new List<Result> { Result.Failure(error1), Result.Failure(error2) };

        var combined = ResultCombine.Combine(results);

        Assert.False(combined.IsValid);
        Assert.Equal([error1, error2], combined.Errors);
    }

    [Fact]
    public void Combine_NonGeneric_NullResults_ThrowsArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => ResultCombine.Combine((IEnumerable<Result>)null!));

    // ---- Generic Combine<T> — params overload ----

    [Fact]
    public void CombineGeneric_Params_AllSuccess_PreservesValuesInInputOrder()
    {
        var combined = ResultCombine.Combine(
            Result<int>.Success(1),
            Result<int>.Success(2),
            Result<int>.Success(3));

        Assert.True(combined.IsValid);
        Assert.Equal([1, 2, 3], combined.Value);
    }

    [Fact]
    public void CombineGeneric_Params_SingleFailureAmongMixed_SurfacesThatOneError()
    {
        var error = Error.NotFound("item.not_found", "Item not found.");

        var combined = ResultCombine.Combine(
            Result<int>.Success(1),
            Result<int>.Failure(error),
            Result<int>.Success(3));

        Assert.False(combined.IsValid);
        Assert.Single(combined.Errors);
        Assert.Equal(error, combined.Errors[0]);
    }

    [Fact]
    public void CombineGeneric_Params_AllFailure_SurfacesEveryError()
    {
        var error1 = Error.Validation("field.a", "A is invalid.");
        var error2 = Error.Validation("field.b", "B is invalid.");

        var combined = ResultCombine.Combine(
            Result<int>.Failure(error1),
            Result<int>.Failure(error2));

        Assert.False(combined.IsValid);
        Assert.Equal([error1, error2], combined.Errors);
    }

    // ---- Generic Combine<T> — IEnumerable overload ----

    [Fact]
    public void CombineGeneric_IEnumerable_AllSuccess_PreservesValuesInInputOrder()
    {
        var results = new List<Result<string>>
        {
            Result<string>.Success("a"),
            Result<string>.Success("b"),
            Result<string>.Success("c"),
        };

        var combined = ResultCombine.Combine(results);

        Assert.True(combined.IsValid);
        Assert.Equal(["a", "b", "c"], combined.Value);
    }

    [Fact]
    public void CombineGeneric_IEnumerable_AllFailure_SurfacesEveryError()
    {
        var error1 = Error.Unexpected("e.1", "first");
        var error2 = Error.Unexpected("e.2", "second");
        var results = new List<Result<string>> { Result<string>.Failure(error1), Result<string>.Failure(error2) };

        var combined = ResultCombine.Combine(results);

        Assert.False(combined.IsValid);
        Assert.Equal([error1, error2], combined.Errors);
    }

    [Fact]
    public void CombineGeneric_AccessingValueOnFailure_Throws()
    {
        var combined = ResultCombine.Combine(Result<int>.Failure(Error.Unexpected("e", "x")));

        Assert.Throws<InvalidOperationException>(() => combined.Value);
    }

    [Fact]
    public void CombineGeneric_NullResults_ThrowsArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => ResultCombine.Combine((IEnumerable<Result<int>>)null!));
}
