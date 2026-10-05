using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Core.Tests.Extensions;

public sealed class ResultExtensionsTests
{
    // ---- Map ----

    [Fact]
    public void Map_OnSuccess_TransformsValue()
    {
        var result = Result<int>.Success(5).Map(x => x * 2);
        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value);
    }

    [Fact]
    public void Map_OnFailure_ForwardsError()
    {
        var error = Error.Unexpected("e.1", "fail");
        var result = Result<int>.Failure(error).Map(x => x * 2);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    // ---- MapError ----

    [Fact]
    public void MapError_OnFailure_TransformsError()
    {
        var original = Error.Unexpected("e.1", "original");
        var mapped   = Error.Validation("e.2", "mapped");
        var result = Result<int>.Failure(original).MapError(_ => mapped);
        Assert.True(result.IsFailure);
        Assert.Equal(mapped, result.Error);
    }

    [Fact]
    public void MapError_OnSuccess_PassesThrough()
    {
        var result = Result<int>.Success(7).MapError(_ => Error.Unexpected("e", "x"));
        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value);
    }

    // ---- Bind ----

    [Fact]
    public void Bind_OnSuccess_ChainsNextOperation()
    {
        var result = Result<int>.Success(3)
            .Bind(x => Result<string>.Success(x.ToString()));
        Assert.True(result.IsSuccess);
        Assert.Equal("3", result.Value);
    }

    [Fact]
    public void Bind_OnFailure_ShortCircuits()
    {
        var error = Error.Unexpected("e.1", "fail");
        var called = false;
        var result = Result<int>.Failure(error)
            .Bind(x => { called = true; return Result<string>.Success(x.ToString()); });
        Assert.False(called);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Bind_ChainedBindFails_PropagatesSecondError()
    {
        var secondError = Error.NotFound("nf", "not found");
        var result = Result<int>.Success(1)
            .Bind(_ => Result<string>.Failure(secondError));
        Assert.True(result.IsFailure);
        Assert.Equal(secondError, result.Error);
    }

    // ---- Match ----

    [Fact]
    public void Match_OnSuccess_CallsOnSuccess()
    {
        var result = Result<int>.Success(42).Match(
            onSuccess: v => $"ok:{v}",
            onFailure: e => $"err:{e.Code}");
        Assert.Equal("ok:42", result);
    }

    [Fact]
    public void Match_OnFailure_CallsOnFailure()
    {
        var error = Error.Unexpected("e.1", "fail");
        var result = Result<int>.Failure(error).Match(
            onSuccess: _ => "ok",
            onFailure: e => $"err:{e.Code}");
        Assert.Equal("err:e.1", result);
    }

    // ---- Tap ----

    [Fact]
    public void Tap_OnSuccess_ExecutesSideEffect_AndReturnsOriginal()
    {
        int captured = 0;
        var result = Result<int>.Success(7).Tap(v => captured = v);
        Assert.Equal(7, captured);
        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value);
    }

    [Fact]
    public void Tap_OnFailure_SkipsSideEffect()
    {
        int captured = 0;
        var result = Result<int>.Failure(Error.Unexpected("e", "x")).Tap(v => captured = v);
        Assert.Equal(0, captured);
        Assert.True(result.IsFailure);
    }

    // ---- Chain: map → bind → match ----

    [Fact]
    public void Chain_MapBindMatch_SuccessPath()
    {
        var outcome = Result<int>.Success(2)
            .Map(x => x + 3)
            .Bind(x => Result<string>.Success($"val={x}"))
            .Match(s => s, e => $"err:{e.Code}");
        Assert.Equal("val=5", outcome);
    }

    [Fact]
    public void Chain_MapBindMatch_FailurePath()
    {
        var error = Error.Unexpected("e.1", "fail");
        var outcome = Result<int>.Failure(error)
            .Map(x => x + 3)
            .Bind(x => Result<string>.Success($"val={x}"))
            .Match(s => s, e => $"err:{e.Code}");
        Assert.Equal("err:e.1", outcome);
    }

    // ---- Non-generic Result.Match (void) ----

    [Fact]
    public void NonGeneric_Match_OnSuccess_CallsOnSuccess()
    {
        bool onSuccessCalled = false;
        bool onFailureCalled = false;
        Result.Success().Match(
            onSuccess: () => onSuccessCalled = true,
            onFailure: _ => onFailureCalled = true);
        Assert.True(onSuccessCalled);
        Assert.False(onFailureCalled);
    }

    [Fact]
    public void NonGeneric_Match_OnFailure_CallsOnFailure()
    {
        var error = Error.Unexpected("e.1", "fail");
        Error? captured = null;
        Result.Failure(error).Match(
            onSuccess: () => { },
            onFailure: e => captured = e);
        Assert.Equal(error, captured);
    }

    // ---- Async extensions ----

    [Fact]
    public async Task AsyncMap_OnSuccess_TransformsValue()
    {
        var result = await Task.FromResult(Result<int>.Success(5)).Map(x => x * 2);
        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value);
    }

    [Fact]
    public async Task AsyncMap_AsyncLambda_OnSuccess_TransformsValue()
    {
        var result = await Task.FromResult(Result<int>.Success(5))
            .Map(async x => await Task.FromResult(x * 2));
        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value);
    }

    [Fact]
    public async Task AsyncBind_OnSuccess_ChainsOperation()
    {
        var result = await Task.FromResult(Result<int>.Success(3))
            .Bind(x => Result<string>.Success(x.ToString()));
        Assert.True(result.IsSuccess);
        Assert.Equal("3", result.Value);
    }

    [Fact]
    public async Task AsyncBind_AsyncLambda_OnSuccess_ChainsOperation()
    {
        var result = await Task.FromResult(Result<int>.Success(3))
            .Bind(x => Task.FromResult(Result<string>.Success(x.ToString())));
        Assert.True(result.IsSuccess);
        Assert.Equal("3", result.Value);
    }

    [Fact]
    public async Task AsyncMatch_OnSuccess_ReturnsOnSuccessValue()
    {
        var outcome = await Task.FromResult(Result<int>.Success(4))
            .Match(v => $"v={v}", e => $"e={e.Code}");
        Assert.Equal("v=4", outcome);
    }

    [Fact]
    public async Task AsyncTap_OnSuccess_ExecutesSideEffect()
    {
        int captured = 0;
        await Task.FromResult(Result<int>.Success(9)).Tap(v => captured = v);
        Assert.Equal(9, captured);
    }

    [Fact]
    public async Task AsyncMapError_OnFailure_TransformsError()
    {
        var original = Error.Unexpected("e.1", "original");
        var mapped   = Error.Validation("e.2", "mapped");
        var result = await Task.FromResult(Result<int>.Failure(original))
            .MapError(_ => mapped);
        Assert.True(result.IsFailure);
        Assert.Equal(mapped, result.Error);
    }
}
