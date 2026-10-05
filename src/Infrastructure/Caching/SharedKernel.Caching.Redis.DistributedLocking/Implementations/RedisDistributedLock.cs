using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Logging;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.DistributedLocking.Implementations;

/// <summary>
/// A held Redis lock. A background loop extends the key every third of the expiry. The lock is reported lost,
/// through <see cref="IsHeld"/> and <see cref="LostToken"/>, when an extension finds another owner, or when no
/// extension has succeeded for five sixths of the expiry, before the key can expire on the server.
/// </summary>
internal sealed partial class RedisDistributedLock : IDistributedLock
{
    private const int Held = 0;
    private const int Lost = 1;
    private const int Released = 2;

    private readonly IDatabase _database;
    private readonly RedisKey _lockKey;
    private readonly string _owner;
    private readonly TimeSpan _expiry;
    private readonly TimeSpan _lossDeadline;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    // Never disposed: it owns no timer, and LostToken must stay readable after the lock ends.
    private readonly CancellationTokenSource _lost = new();
    private readonly CancellationTokenSource _stopKeepAlive = new();
    private readonly ITimer _lossTimer;
    private Task _keepAlive = Task.CompletedTask;
    private int _state = Held;
    private int _disposed;

    /// <param name="database">The database the lock key lives in.</param>
    /// <param name="resource">The locked resource.</param>
    /// <param name="owner">The value that identifies this holder in the lock key.</param>
    /// <param name="fencingToken">The token issued with the acquisition.</param>
    /// <param name="expiry">The key's time to live.</param>
    /// <param name="requestedAt">
    /// The <see cref="TimeProvider.GetTimestamp"/> taken before the acquire request was sent. The key cannot
    /// expire earlier than <paramref name="expiry"/> after it.
    /// </param>
    /// <param name="timeProvider">The time source.</param>
    /// <param name="logger">The logger.</param>
    internal RedisDistributedLock(
        IDatabase database,
        string resource,
        string owner,
        long fencingToken,
        TimeSpan expiry,
        long requestedAt,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _database = database;
        _lockKey = RedisLockScripts.LockKey(resource);
        _owner = owner;
        _expiry = expiry;
        _lossDeadline = expiry * 5 / 6;
        _timeProvider = timeProvider;
        _logger = logger;
        Resource = resource;
        FencingToken = fencingToken;

        _lossTimer = timeProvider.CreateTimer(
            static state => ((RedisDistributedLock)state!).OnLossDeadline(),
            this,
            RemainingUntilLoss(requestedAt),
            Timeout.InfiniteTimeSpan);
    }

    public string Resource { get; }

    public long FencingToken { get; }

    public bool IsHeld => Volatile.Read(ref _state) == Held;

    public CancellationToken LostToken => _lost.Token;

    internal void StartKeepAlive() => _keepAlive = Task.Run(KeepAliveAsync);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        // Waits for a running deadline callback, so it cannot race the disposal below.
        await _lossTimer.DisposeAsync().ConfigureAwait(false);
        await _stopKeepAlive.CancelAsync().ConfigureAwait(false);
        await _keepAlive.ConfigureAwait(false);
        _stopKeepAlive.Dispose();

        if (Interlocked.CompareExchange(ref _state, Released, Held) == Held)
        {
            try
            {
                await _database.ScriptEvaluateAsync(RedisLockScripts.Release, [_lockKey], [_owner]).ConfigureAwait(false);
                Log.LockReleased(_logger, Resource);
            }
            catch (Exception ex) when (RedisLockScripts.IsStoreFailure(ex))
            {
                // The key still expires on its own.
                Log.ReleaseFailed(_logger, Resource, ex);
            }
        }

        await _lost.CancelAsync().ConfigureAwait(false);
    }

    private async Task KeepAliveAsync()
    {
        using var timer = new PeriodicTimer(_expiry / 3, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(_stopKeepAlive.Token).ConfigureAwait(false))
            {
                long sentAt = _timeProvider.GetTimestamp();
                try
                {
                    RedisResult result = await _database.ScriptEvaluateAsync(
                        RedisLockScripts.Extend,
                        [_lockKey],
                        [_owner, RedisLockScripts.ToMilliseconds(_expiry)]).ConfigureAwait(false);

                    if ((long)result == 1)
                    {
                        // Ignored once disposal has started; the lock is being released anyway.
                        TryResetLossTimer(sentAt);
                        continue;
                    }

                    MarkLost("another owner holds the key");
                    return;
                }
                catch (Exception ex) when (RedisLockScripts.IsStoreFailure(ex))
                {
                    // The loss deadline reports the lock lost if extensions keep failing.
                    Log.ExtendFailed(_logger, Resource, ex);
                }
            }
        }
        catch (OperationCanceledException) when (_stopKeepAlive.IsCancellationRequested)
        {
            // Released or lost.
        }
    }

    private TimeSpan RemainingUntilLoss(long sentAt)
    {
        var remaining = _lossDeadline - _timeProvider.GetElapsedTime(sentAt);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private void TryResetLossTimer(long sentAt)
    {
        try
        {
            _lossTimer.Change(RemainingUntilLoss(sentAt), Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Disposal started while the extension was in flight.
        }
    }

    private void OnLossDeadline() =>
        MarkLost("no extension succeeded before the key could expire");

    private void MarkLost(string reason)
    {
        if (Interlocked.CompareExchange(ref _state, Lost, Held) != Held)
            return;

        Log.LockLost(_logger, Resource, FencingToken, reason);
        _lost.Cancel();

        // Stop extending: the holder has been told to stop, so the key must be allowed to expire.
        try
        {
            _stopKeepAlive.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Disposal already stopped the loop.
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 304, Level = LogLevel.Debug,
            Message = "Distributed lock on '{Resource}' released")]
        internal static partial void LockReleased(ILogger logger, string resource);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 305, Level = LogLevel.Warning,
            Message = "Distributed lock on '{Resource}' could not be released; it expires on its own")]
        internal static partial void ReleaseFailed(ILogger logger, string resource, Exception exception);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 306, Level = LogLevel.Warning,
            Message = "Distributed lock on '{Resource}' could not be extended; retrying until it would expire")]
        internal static partial void ExtendFailed(ILogger logger, string resource, Exception exception);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 307, Level = LogLevel.Error,
            Message = "Distributed lock on '{Resource}' (fencing token {FencingToken}) was lost: {Reason}")]
        internal static partial void LockLost(ILogger logger, string resource, long fencingToken, string reason);
    }
}
