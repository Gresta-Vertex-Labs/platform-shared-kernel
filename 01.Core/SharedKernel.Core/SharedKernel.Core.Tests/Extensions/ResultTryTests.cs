using System.Diagnostics;
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
    public void Try_ThrowsException_ReturnsFailureWithFixedSafeMessage()
    {
        var result = ResultTry.Try<int>(() => throw new InvalidOperationException("boom: connectionstring=secret"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unexpected, result.Error.Type);
        Assert.Equal(ErrorCodes.Unexpected.Default, result.Error.Code);
        Assert.Equal(ResultTry.DefaultUnexpectedMessage, result.Error.Message);
        Assert.DoesNotContain("boom", result.Error.Message);
        Assert.DoesNotContain("secret", result.Error.Message);
    }

    [Fact]
    public void Try_ThrowsCustomExceptionType_NeverRethrows_UsesFixedSafeMessage()
    {
        var result = ResultTry.Try<int>(() => throw new CustomException("custom failure"));

        Assert.True(result.IsFailure);
        Assert.Equal(ResultTry.DefaultUnexpectedMessage, result.Error.Message);
    }

    [Fact]
    public void Try_ThrowsAggregateException_StillReturnsFixedSafeMessage()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("first"),
            new ArgumentException("second"));

        var result = ResultTry.Try<int>(() => throw aggregate);

        Assert.True(result.IsFailure);
        Assert.Equal(ResultTry.DefaultUnexpectedMessage, result.Error.Message);
    }

    [Fact]
    public void Try_ThrowsNestedAggregateException_StillReturnsFixedSafeMessage()
    {
        var nested = new AggregateException(
            new AggregateException(new InvalidOperationException("inner-most")),
            new ArgumentException("sibling"));

        var result = ResultTry.Try<int>(() => throw nested);

        Assert.True(result.IsFailure);
        Assert.Equal(ResultTry.DefaultUnexpectedMessage, result.Error.Message);
    }

    [Fact]
    public void Try_NullOperation_ThrowsArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => ResultTry.Try<int>(null!));

    [Fact]
    public void Try_OperationCanceledException_PropagatesUncaught()
        => Assert.Throws<OperationCanceledException>(
            () => ResultTry.Try<int>(() => throw new OperationCanceledException()));

    [Fact]
    public void Try_TaskCanceledException_PropagatesUncaught()
        => Assert.Throws<TaskCanceledException>(() => ResultTry.Try<int>(() => throw new TaskCanceledException()));

    // ---- Try (sync, custom mapper) ----

    [Fact]
    public void Try_WithCustomMapper_OnSuccess_UsesSuccessValue()
    {
        var result = ResultTry.Try(() => 7, ex => Error.Unexpected("custom.code", ex.Message));

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value);
    }

    [Fact]
    public void Try_WithCustomMapper_OnFailure_UsesCustomMapper_ByteForByte()
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

    [Fact]
    public void Try_WithCustomMapper_OperationCanceledException_PropagatesUncaught_BypassesMapper()
    {
        var mapperInvoked = false;

        Assert.Throws<OperationCanceledException>(() => ResultTry.Try<int>(
            () => throw new OperationCanceledException(),
            ex =>
            {
                mapperInvoked = true;
                return Error.Unexpected("should-not-run", ex.Message);
            }));

        Assert.False(mapperInvoked);
    }

    // ---- TryAsync (default mapping) ----

    [Fact]
    public async Task TryAsync_Success_ReturnsSuccessResult()
    {
        var result = await ResultTry.TryAsync(() => Task.FromResult(99));

        Assert.True(result.IsSuccess);
        Assert.Equal(99, result.Value);
    }

    [Fact]
    public async Task TryAsync_ThrowsException_ReturnsFailureWithFixedSafeMessage()
    {
        var result = await ResultTry.TryAsync<int>(() => throw new InvalidOperationException("async-boom"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.Unexpected.Default, result.Error.Code);
        Assert.Equal(ResultTry.DefaultUnexpectedMessage, result.Error.Message);
    }

    [Fact]
    public async Task TryAsync_TaskFaults_ReturnsFailureWithFixedSafeMessage()
    {
        var result = await ResultTry.TryAsync<int>(() => Task.FromException<int>(new ArgumentException("faulted")));

        Assert.True(result.IsFailure);
        Assert.Equal(ResultTry.DefaultUnexpectedMessage, result.Error.Message);
    }

    [Fact]
    public async Task TryAsync_ThrowsAggregateException_StillReturnsFixedSafeMessage()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("a"),
            new ArgumentException("b"));

        var result = await ResultTry.TryAsync<int>(() => throw aggregate);

        Assert.True(result.IsFailure);
        Assert.Equal(ResultTry.DefaultUnexpectedMessage, result.Error.Message);
    }

    [Fact]
    public async Task TryAsync_NullOperation_ThrowsArgumentNullException()
        => await Assert.ThrowsAsync<ArgumentNullException>(() => ResultTry.TryAsync<int>(null!));

    [Fact]
    public async Task TryAsync_OperationCanceledException_PropagatesUncaught()
        => await Assert.ThrowsAsync<OperationCanceledException>(
            () => ResultTry.TryAsync<int>(() => throw new OperationCanceledException()));

    [Fact]
    public async Task TryAsync_TaskCanceledFault_PropagatesUncaught()
        => await Assert.ThrowsAsync<TaskCanceledException>(
            () => ResultTry.TryAsync<int>(() => Task.FromCanceled<int>(new CancellationToken(true))));

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
    public async Task TryAsync_WithCustomMapper_OnFailure_UsesCustomMapper_ByteForByte()
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

    [Fact]
    public async Task TryAsync_WithCustomMapper_OperationCanceledException_PropagatesUncaught_BypassesMapper()
    {
        var mapperInvoked = false;

        await Assert.ThrowsAsync<OperationCanceledException>(() => ResultTry.TryAsync<int>(
            () => throw new OperationCanceledException(),
            ex =>
            {
                mapperInvoked = true;
                return Error.Unexpected("should-not-run", ex.Message);
            }));

        Assert.False(mapperInvoked);
    }

    // ---- Activity/OTel exception recording (default mapping only) ----

    [Fact]
    public void Try_ThrowsException_RecordsExceptionOnAmbientActivity()
    {
        using var recorded = new ActivityExceptionRecorder();
        using var activity = recorded.StartActivity();

        ResultTry.Try<int>(() => throw new InvalidOperationException("boom"));

        var exceptionEvent = Assert.Single(activity.Events, e => e.Name == "exception");
        Assert.Contains(
            exceptionEvent.Tags,
            t => t.Key == "exception.type" && (string?)t.Value == typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public void Try_ThrowsAggregateException_RecordsOneExceptionEventPerFlattenedInner()
    {
        using var recorded = new ActivityExceptionRecorder();
        using var activity = recorded.StartActivity();

        var nested = new AggregateException(
            new AggregateException(new InvalidOperationException("inner-most")),
            new ArgumentException("sibling"));

        ResultTry.Try<int>(() => throw nested);

        var exceptionEvents = activity.Events.Where(e => e.Name == "exception").ToList();
        Assert.Equal(2, exceptionEvents.Count);
        Assert.Contains(
            exceptionEvents,
            e => e.Tags.Any(
                t => t.Key == "exception.type" && (string?)t.Value == typeof(InvalidOperationException).FullName));
        Assert.Contains(
            exceptionEvents,
            e => e.Tags.Any(
                t => t.Key == "exception.type" && (string?)t.Value == typeof(ArgumentException).FullName));
    }

    [Fact]
    public async Task TryAsync_WithCustomMapper_DoesNotRecordExceptionOnActivity()
    {
        using var recorded = new ActivityExceptionRecorder();
        using var activity = recorded.StartActivity();

        await ResultTry.TryAsync<int>(
            () => throw new InvalidOperationException("boom"),
            ex => Error.Unexpected("custom", ex.Message));

        Assert.DoesNotContain(activity.Events, e => e.Name == "exception");
    }

    /// <summary>
    /// Activates a real <see cref="ActivityListener"/> for the duration of a test so
    /// <see cref="Activity.Current"/> is non-null and genuinely records <c>AddException</c> calls — never a
    /// mocked <see cref="Activity"/>.
    /// </summary>
    private sealed class ActivityExceptionRecorder : IDisposable
    {
        private readonly ActivitySource _source = new($"{nameof(ResultTryTests)}.{Guid.NewGuid()}");
        private readonly ActivityListener _listener;

        public ActivityExceptionRecorder()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = _ => true,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public Activity StartActivity()
            => _source.StartActivity(nameof(ResultTryTests))
                ?? throw new InvalidOperationException("Failed to start a test Activity.");

        public void Dispose()
        {
            _listener.Dispose();
            _source.Dispose();
        }
    }
}
