using SharedKernel.Core.Exceptions;
using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Core.Tests.Extensions;

/// <summary>
/// Async railway overloads: exception and cancellation propagation, overload binding for async lambdas,
/// and the Task, ValueTask, and plain-result source shapes.
/// </summary>
public sealed class ResultExtensionsAsyncTests
{
    private static readonly Error Failure = Error.Validation("v.1", "invalid");

    // ---- Propagation: a faulted or cancelled source must surface unchanged ----

    [Fact]
    public async Task TaskSource_Faulted_RethrowsOriginalException()
    {
        var source = Task.FromException<Result<int>>(new InvalidOperationException("boom"));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => source.Map(x => x + 1));

        Assert.Equal("boom", thrown.Message);
    }

    [Fact]
    public async Task TaskSource_Cancelled_ThrowsOperationCanceledException()
    {
        var source = Task.FromCanceled<Result<int>>(new CancellationToken(canceled: true));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.Bind(x => Result<int>.Success(x)));
    }

    [Fact]
    public async Task ValueTaskSource_Faulted_RethrowsOriginalException()
    {
        var source = ValueTask.FromException<Result<int>>(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await source.Map(x => x + 1));
    }

    [Fact]
    public async Task ValueTaskSource_Cancelled_ThrowsOperationCanceledException()
    {
        var source = ValueTask.FromCanceled<Result<int>>(new CancellationToken(canceled: true));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await source.Tap(_ => { }));
    }

    [Fact]
    public async Task AsyncContinuation_ThrowingOperationCanceled_Propagates()
    {
        var source = Task.FromResult(Result<int>.Success(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.Bind(Task<Result<int>> (_) => throw new OperationCanceledException()));
    }

    [Fact]
    public async Task AsyncContinuation_ReturningNullTask_ThrowsInvalidOperationException()
    {
        var source = Task.FromResult(Result<int>.Success(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.Bind(_ => (Task<Result<int>>)null!));
    }

    [Fact]
    public void TaskSource_NullArguments_ThrowSynchronously()
    {
        Assert.Throws<ArgumentNullException>(() => { _ = ((Task<Result<int>>)null!).Map(x => x); });
        Assert.Throws<ArgumentNullException>(() => { _ = Task.FromResult(Result<int>.Success(1)).Map((Func<int, int>)null!); });
    }

    // ---- Overload binding: an async lambda must pick the async overload ----

    [Fact]
    public async Task PlainResult_AsyncLambda_BindsToTaskOverload()
    {
        Result<string> result = await Result<int>.Success(2)
            .Map(async x => { await Task.Yield(); return x * 2; })
            .Bind(async x => { await Task.Yield(); return Result<string>.Success(x.ToString()); });

        Assert.Equal("4", result.Value);
    }

    [Fact]
    public async Task ValueTaskSource_AsyncLambda_BindsToValueTaskOverload()
    {
        var source = ValueTask.FromResult(Result<int>.Success(3));

        Result<int> result = await source
            .Map(async x => { await Task.Yield(); return x + 1; })
            .Ensure(async x => { await Task.Yield(); return x > 0; }, Failure);

        Assert.Equal(4, result.Value);
    }

    [Fact]
    public async Task TaskSource_FullPipeline_ShortCircuitsOnFirstFailure()
    {
        var mapped = false;
        var errorSeen = default(Error);

        Result<int> result = await Task.FromResult(Result<int>.Success(5))
            .Ensure(x => x > 10, Failure)
            .Map(x => { mapped = true; return x; })
            .TapError(e => errorSeen = e);

        Assert.True(result.IsFailure);
        Assert.False(mapped);
        Assert.Equal(Failure, errorSeen);
    }

    [Fact]
    public async Task TaskSource_AsyncTapAndTapError_RunOnMatchingBranchOnly()
    {
        var tapped = 0;
        var tappedError = 0;

        await Task.FromResult(Result<int>.Success(1))
            .Tap(async _ => { await Task.Yield(); tapped++; })
            .TapError(async _ => { await Task.Yield(); tappedError++; });

        Assert.Equal(1, tapped);
        Assert.Equal(0, tappedError);
    }

    [Fact]
    public async Task TaskSource_Match_AsyncSelectors()
    {
        var output = await Task.FromResult(Result<int>.Failure(Failure))
            .Match(x => Task.FromResult($"ok {x}"), e => Task.FromResult(e.Code));

        Assert.Equal("v.1", output);
    }

    [Fact]
    public async Task TaskSource_GetValueOrThrow_ThrowsMatchingException()
    {
        var source = Task.FromResult(Result<int>.Failure(Error.NotFound("nf", "missing")));

        await Assert.ThrowsAsync<NotFoundException>(() => source.GetValueOrThrow());
    }

    // ---- Non-generic Result ----

    [Fact]
    public void NonGeneric_BindMapEnsure_Chain()
    {
        var calls = 0;

        Result<int> result = Result.Success()
            .Tap(() => calls++)
            .Ensure(() => true, Failure)
            .Bind(() => Result.Success())
            .Map(() => 42);

        Assert.Equal(42, result.Value);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void NonGeneric_Failure_SkipsContinuationsAndMapsError()
    {
        var mapped = Error.Conflict("c", "conflict");

        Result result = Result.Failure(Failure)
            .Bind(Result () => throw new InvalidOperationException("must not run"))
            .MapError(_ => mapped);

        Assert.Equal(mapped, result.Error);
    }

    [Fact]
    public void NonGeneric_BindToGeneric_ForwardsError()
    {
        Result<int> result = Result.Failure(Failure).Bind(() => Result<int>.Success(1));

        Assert.Equal(Failure, result.Error);
    }

    [Fact]
    public void NonGeneric_Match_ReturnsFoldedValue()
        => Assert.Equal("v.1", Result.Failure(Failure).Match(() => "ok", e => e.Code));

    [Fact]
    public async Task NonGeneric_TaskSource_AsyncBind()
    {
        Result result = await Task.FromResult(Result.Success())
            .Bind(async () => { await Task.Yield(); return Result.Failure(Failure); });

        Assert.Equal(Failure, result.Error);
    }

    [Fact]
    public async Task NonGeneric_ValueTaskSource_ThrowIfFailure_Throws()
    {
        var source = ValueTask.FromResult(Result.Failure(Error.Forbidden("f", "forbidden")));

        await Assert.ThrowsAsync<ForbiddenException>(async () => await source.ThrowIfFailure());
    }

    // ---- Result<T> additions ----

    [Fact]
    public void Ensure_PredicateFails_UsesErrorFactory()
    {
        Result<int> result = Result<int>.Success(7).Ensure(x => x < 5, x => Error.Validation("v.max", $"{x} too big"));

        Assert.Equal("7 too big", result.Error.Message);
    }

    [Fact]
    public void Ensure_OnFailure_DoesNotCallPredicate()
    {
        Result<int> result = Result<int>.Failure(Failure).Ensure(bool (_) => throw new InvalidOperationException(), Failure);

        Assert.Equal(Failure, result.Error);
    }

    [Fact]
    public void BindToNonGeneric_OnSuccess_RunsOperation()
    {
        Result result = Result<int>.Success(1).Bind(_ => Result.Failure(Failure));

        Assert.Equal(Failure, result.Error);
    }

    [Fact]
    public void GetValueOrThrow_Success_ReturnsValue()
        => Assert.Equal(3, Result<int>.Success(3).GetValueOrThrow());

    [Fact]
    public void GetValueOrThrow_Failure_ThrowsExceptionMatchingErrorType()
    {
        var thrown = Assert.Throws<ConflictException>(() => Result<int>.Failure(Error.Conflict("c", "taken")).GetValueOrThrow());

        Assert.Equal("c", thrown.Error.Code);
    }

    [Fact]
    public void ThrowIfFailure_Success_DoesNotThrow()
        => Result.Success().ThrowIfFailure();
}
