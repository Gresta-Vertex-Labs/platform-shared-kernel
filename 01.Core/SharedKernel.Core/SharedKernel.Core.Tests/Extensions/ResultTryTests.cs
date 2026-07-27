using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Core.Tests.Extensions;

public sealed class ResultTryTests
{
    private sealed class CustomException(string message) : Exception(message);

    // ---- Try (sync, default mapping) ----

    [Fact]
    public void Try_Success_ReturnsSuccessResult()
    {
        var result = ResultTry.Try(() => 42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Try_ThrowsException_ReturnsFailureWithDefaultMapping()
    {
        var result = ResultTry.Try<int>(() => throw new InvalidOperationException("boom"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unexpected, result.Error.Type);
        Assert.Equal(ErrorCodes.Unexpected.Default, result.Error.Code);
        Assert.Equal("InvalidOperationException: boom", result.Error.Message);
    }

    [Fact]
    public void Try_ThrowsCustomExceptionType_NeverRethrows()
    {
        var result = ResultTry.Try<int>(() => throw new CustomException("custom failure"));

        Assert.True(result.IsFailure);
        Assert.Equal("CustomException: custom failure", result.Error.Message);
    }

    [Fact]
    public void Try_ThrowsAggregateException_FlattensAllInnerExceptions()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("first"),
            new ArgumentException("second"));

        var result = ResultTry.Try<int>(() => throw aggregate);

        Assert.True(result.IsFailure);
        Assert.Contains("InvalidOperationException: first", result.Error.Message);
        Assert.Contains("ArgumentException: second", result.Error.Message);
    }

    [Fact]
    public void Try_ThrowsNestedAggregateException_FlattenBeforeMessageConstruction()
    {
        var nested = new AggregateException(
            new AggregateException(new InvalidOperationException("inner-most")),
            new ArgumentException("sibling"));

        var result = ResultTry.Try<int>(() => throw nested);

        Assert.True(result.IsFailure);
        Assert.Contains("InvalidOperationException: inner-most", result.Error.Message);
        Assert.Contains("ArgumentException: sibling", result.Error.Message);
        Assert.DoesNotContain("AggregateException", result.Error.Message);
    }

    [Fact]
    public void Try_NullOperation_ThrowsArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => ResultTry.Try<int>(null!));

    // ---- Try (sync, custom mapper) ----

    [Fact]
    public void Try_WithCustomMapper_OnSuccess_UsesSuccessValue()
    {
        var result = ResultTry.Try(() => 7, ex => Error.Unexpected("custom.code", ex.Message));

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value);
    }

    [Fact]
    public void Try_WithCustomMapper_OnFailure_UsesCustomMapper()
    {
        var result = ResultTry.Try<int>(
            () => throw new InvalidOperationException("boom"),
            ex => Error.Conflict("custom.conflict", $"mapped: {ex.Message}"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("custom.conflict", result.Error.Code);
        Assert.Equal("mapped: boom", result.Error.Message);
    }

    [Fact]
    public void Try_WithCustomMapper_NullOnException_ThrowsArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => ResultTry.Try(() => 1, null!));

    // ---- TryAsync (default mapping) ----

    [Fact]
    public async Task TryAsync_Success_ReturnsSuccessResult()
    {
        var result = await ResultTry.TryAsync(() => Task.FromResult(99));

        Assert.True(result.IsSuccess);
        Assert.Equal(99, result.Value);
    }

    [Fact]
    public async Task TryAsync_ThrowsException_ReturnsFailureWithDefaultMapping()
    {
        var result = await ResultTry.TryAsync<int>(() => throw new InvalidOperationException("async-boom"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.Unexpected.Default, result.Error.Code);
        Assert.Equal("InvalidOperationException: async-boom", result.Error.Message);
    }

    [Fact]
    public async Task TryAsync_TaskFaults_ReturnsFailureWithDefaultMapping()
    {
        var result = await ResultTry.TryAsync<int>(() => Task.FromException<int>(new ArgumentException("faulted")));

        Assert.True(result.IsFailure);
        Assert.Equal("ArgumentException: faulted", result.Error.Message);
    }

    [Fact]
    public async Task TryAsync_ThrowsAggregateException_FlattensAllInnerExceptions()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("a"),
            new ArgumentException("b"));

        var result = await ResultTry.TryAsync<int>(() => throw aggregate);

        Assert.True(result.IsFailure);
        Assert.Contains("InvalidOperationException: a", result.Error.Message);
        Assert.Contains("ArgumentException: b", result.Error.Message);
    }

    [Fact]
    public async Task TryAsync_NullOperation_ThrowsArgumentNullException()
        => await Assert.ThrowsAsync<ArgumentNullException>(() => ResultTry.TryAsync<int>(null!));

    // ---- TryAsync (custom mapper) ----

    [Fact]
    public async Task TryAsync_WithCustomMapper_OnSuccess_UsesSuccessValue()
    {
        var result = await ResultTry.TryAsync(
            () => Task.FromResult(3),
            ex => Error.Unexpected("custom", ex.Message));

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);
    }

    [Fact]
    public async Task TryAsync_WithCustomMapper_OnFailure_UsesCustomMapper()
    {
        var result = await ResultTry.TryAsync<int>(
            () => throw new InvalidOperationException("async-boom"),
            ex => Error.NotFound("custom.notfound", $"mapped: {ex.Message}"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("mapped: async-boom", result.Error.Message);
    }

    [Fact]
    public async Task TryAsync_WithCustomMapper_NullOnException_ThrowsArgumentNullException()
        => await Assert.ThrowsAsync<ArgumentNullException>(
            () => ResultTry.TryAsync(() => Task.FromResult(1), null!));
}
