using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel.Caching.Redis.DistributedLocking.Implementations;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.DistributedLocking.Tests;

/// <summary>
/// Loss detection of <see cref="RedisDistributedLock"/> on a manual clock: a lock whose extensions stop succeeding
/// is reported lost at five sixths of its expiry, measured from when the last successful request was sent, and
/// then stops extending.
/// </summary>
public sealed class RedisDistributedLockLossTests
{
    private static readonly TimeSpan Expiry = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan KeepAliveInterval = Expiry / 3;
    private static readonly TimeSpan LossDeadline = Expiry * 5 / 6;
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);

    private readonly ManualTimeProvider _time = new();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private int _extendCalls;
    private int _releaseCalls;
    private Func<int, Task<RedisResult>> _extend = _ => Task.FromResult(RedisResult.Create((RedisValue)1));

    public RedisDistributedLockLossTests()
    {
        _database
            .ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]?>(), Arg.Any<RedisValue[]?>(), Arg.Any<CommandFlags>())
            .Returns(call =>
            {
                var script = call.ArgAt<string>(0);
                if (script == RedisLockScripts.Extend)
                    return _extend(Interlocked.Increment(ref _extendCalls));

                if (script == RedisLockScripts.Release)
                {
                    Interlocked.Increment(ref _releaseCalls);
                    return Task.FromResult(RedisResult.Create((RedisValue)1));
                }

                throw new InvalidOperationException("Unexpected script.");
            });
    }

    [Fact]
    public async Task ExtensionsFail_LossIsReportedAtFiveSixthsOfExpiry_AndKeepAliveStops()
    {
        _extend = _ => Task.FromException<RedisResult>(new RedisTimeoutException("simulated", CommandStatus.Sent));
        var handle = StartLock(requestedAt: _time.GetTimestamp());
        await WaitUntilAsync(() => _time.ActiveTimers == 2, "the keep-alive timer to start");

        _time.Advance(KeepAliveInterval);
        await WaitUntilAsync(() => _extendCalls == 1, "the first extension");
        _time.Advance(KeepAliveInterval);
        await WaitUntilAsync(() => _extendCalls == 2, "the second extension");

        _time.Advance(LossDeadline - _time.Elapsed - OneTick);
        Assert.True(handle.IsHeld);
        Assert.False(handle.LostToken.IsCancellationRequested);

        _time.Advance(OneTick);
        Assert.False(handle.IsHeld);
        Assert.True(handle.LostToken.IsCancellationRequested);
        Assert.True(_time.Elapsed < Expiry);

        // The keep-alive loop ends and disposes its timer; later ticks send nothing.
        await WaitUntilAsync(() => _time.ActiveTimers == 1, "the keep-alive loop to stop");
        _time.Advance(Expiry * 3);
        await Task.Delay(50);
        Assert.Equal(2, _extendCalls);

        await handle.DisposeAsync();
        Assert.Equal(0, _releaseCalls);
        Assert.Equal(0, _time.ActiveTimers);
    }

    [Fact]
    public async Task SuccessfulExtension_MovesTheDeadline_MeasuredFromWhenTheRequestWasSent()
    {
        var firstReply = new TaskCompletionSource<RedisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _extend = call => call == 1
            ? firstReply.Task
            : Task.FromException<RedisResult>(new TimeoutException("simulated"));

        var handle = StartLock(requestedAt: _time.GetTimestamp());
        await WaitUntilAsync(() => _time.ActiveTimers == 2, "the keep-alive timer to start");

        _time.Advance(KeepAliveInterval);
        await WaitUntilAsync(() => _extendCalls == 1, "the first extension");
        var sentAt = _time.Elapsed;

        // The reply arrives five seconds after the request was sent.
        _time.Advance(TimeSpan.FromSeconds(5));
        firstReply.SetResult(RedisResult.Create((RedisValue)1));
        var expectedDeadline = sentAt + LossDeadline;
        await WaitUntilAsync(() => _time.DueTimes.Contains(expectedDeadline), $"the loss deadline to move to {expectedDeadline}");

        _time.Advance(expectedDeadline - _time.Elapsed - OneTick);
        Assert.True(handle.IsHeld, "The deadline must count from the successful extension, not the acquisition.");

        _time.Advance(OneTick);
        Assert.False(handle.IsHeld);
        Assert.True(handle.LostToken.IsCancellationRequested);

        await handle.DisposeAsync();
        Assert.Equal(0, _releaseCalls);
    }

    [Fact]
    public async Task ExtensionFindsAnotherOwner_LossIsReportedAtOnce_AndNothingIsSentAfterwards()
    {
        _extend = _ => Task.FromResult(RedisResult.Create((RedisValue)0));
        var handle = StartLock(requestedAt: _time.GetTimestamp());
        await WaitUntilAsync(() => _time.ActiveTimers == 2, "the keep-alive timer to start");

        _time.Advance(KeepAliveInterval);
        await WaitUntilAsync(() => handle.LostToken.IsCancellationRequested, "the lock to be reported lost");

        Assert.False(handle.IsHeld);
        Assert.True(_time.Elapsed < LossDeadline);

        await WaitUntilAsync(() => _time.ActiveTimers == 1, "the keep-alive loop to stop");
        _time.Advance(Expiry * 3);
        await Task.Delay(50);
        Assert.Equal(1, _extendCalls);

        await handle.DisposeAsync();
        Assert.Equal(0, _releaseCalls);
    }

    [Fact]
    public async Task LossDeadline_CountsFromTheAcquireRequest_NotFromTheReply()
    {
        long requestedAt = _time.GetTimestamp();
        var delay = TimeSpan.FromSeconds(30);
        _time.Advance(delay);

        var handle = new RedisDistributedLock(_database, "resource", "owner", 7, Expiry, requestedAt, _time, NullLogger.Instance);

        _time.Advance(LossDeadline - delay - OneTick);
        Assert.True(handle.IsHeld);

        _time.Advance(OneTick);
        Assert.False(handle.IsHeld);
        Assert.True(handle.LostToken.IsCancellationRequested);

        await handle.DisposeAsync();
        Assert.Equal(0, _releaseCalls);
    }

    [Fact]
    public async Task ReplyArrivingAfterTheDeadline_ReportsLossImmediately()
    {
        long requestedAt = _time.GetTimestamp();
        _time.Advance(Expiry);

        var handle = new RedisDistributedLock(_database, "resource", "owner", 7, Expiry, requestedAt, _time, NullLogger.Instance);
        _time.Advance(TimeSpan.Zero);

        Assert.False(handle.IsHeld);
        Assert.True(handle.LostToken.IsCancellationRequested);
        await handle.DisposeAsync();
        Assert.Equal(0, _releaseCalls);
    }

    [Fact]
    public async Task DisposeWhileHeld_SendsReleaseOnce_AndDisposesBothTimers()
    {
        var handle = StartLock(requestedAt: _time.GetTimestamp());
        await WaitUntilAsync(() => _time.ActiveTimers == 2, "the keep-alive timer to start");

        await handle.DisposeAsync();
        await handle.DisposeAsync();

        Assert.Equal(1, _releaseCalls);
        Assert.Equal(0, _time.ActiveTimers);
        Assert.False(handle.IsHeld);
        Assert.True(handle.LostToken.IsCancellationRequested);

        _time.Advance(Expiry * 3);
        Assert.Equal(0, _extendCalls);
    }

    [Fact]
    public async Task ExtensionCancelledByTheClient_IsAStoreFailure_AndDisposeStillReleases()
    {
        // ScriptEvaluateAsync takes no token, so a cancellation from it is the client giving up on a frozen
        // store. It must be handled like a timeout: never escape the keep-alive loop and fault DisposeAsync.
        _extend = _ => Task.FromCanceled<RedisResult>(new CancellationToken(canceled: true));
        var handle = StartLock(requestedAt: _time.GetTimestamp());
        await WaitUntilAsync(() => _time.ActiveTimers == 2, "the keep-alive timer to start");

        _time.Advance(KeepAliveInterval);
        await WaitUntilAsync(() => _extendCalls == 1, "the first extension");
        Assert.True(handle.IsHeld, "One failed extension is not a loss before the deadline.");

        await handle.DisposeAsync();

        Assert.Equal(1, _releaseCalls);
        Assert.Equal(0, _time.ActiveTimers);
    }

    private RedisDistributedLock StartLock(long requestedAt)
    {
        var handle = new RedisDistributedLock(_database, "resource", "owner", 7, Expiry, requestedAt, _time, NullLogger.Instance);
        handle.StartKeepAlive();
        return handle;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Timed out waiting for {description}.");

            await Task.Delay(5);
        }
    }
}
