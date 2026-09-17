using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Logging;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.DistributedLocking.Implementations;

/// <summary>
/// A held Redis lock. A background loop extends the key every third of the expiry. When an
/// extension finds another owner, or extensions keep failing until the expiry has passed, the lock
/// is reported lost through <see cref="IsHeld"/> and <see cref="LostToken"/>.
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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    // Never disposed: it owns no timer, and LostToken must stay readable after the lock ends.
    private readonly CancellationTokenSource _lost = new();
    private readonly CancellationTokenSource _stopKeepAlive = new();
    private Task _keepAlive = Task.CompletedTask;
    private int _state = Held;
    private int _disposed;

    internal RedisDistributedLock(
        IDatabase database,
        string resource,
        string owner,
        long fencingToken,
        TimeSpan expiry,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _database = database;
        _lockKey = RedisLockScripts.LockKey(resource);
        _owner = owner;
        _expiry = expiry;
        _timeProvider = timeProvider;
        _logger = logger;
        Resource = resource;
        FencingToken = fencingToken;
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
        long lastExtendedAt = _timeProvider.GetTimestamp();

        try
        {
            while (await timer.WaitForNextTickAsync(_stopKeepAlive.Token).ConfigureAwait(false))
            {
                try
                {
                    RedisResult result = await _database.ScriptEvaluateAsync(
                        RedisLockScripts.Extend,
                        [_lockKey],
                        [_owner, RedisLockScripts.ToMilliseconds(_expiry)]).ConfigureAwait(false);

                    if ((long)result == 1)
                    {
                        lastExtendedAt = _timeProvider.GetTimestamp();
                        continue;
                    }

                    await MarkLostAsync("another owner holds the key").ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) when (RedisLockScripts.IsStoreFailure(ex))
                {
                    Log.ExtendFailed(_logger, Resource, ex);
                    if (_timeProvider.GetElapsedTime(lastExtendedAt) >= _expiry)
                    {
                        await MarkLostAsync("the lock store stayed unreachable past the expiry").ConfigureAwait(false);
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stopKeepAlive.IsCancellationRequested)
        {
            // Released.
        }
    }

    private async Task MarkLostAsync(string reason)
    {
        if (Interlocked.CompareExchange(ref _state, Lost, Held) != Held)
            return;

        Log.LockLost(_logger, Resource, FencingToken, reason);
        await _lost.CancelAsync().ConfigureAwait(false);
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
            Message = "Distributed lock on '{Resource}' could not be extended; retrying until it expires")]
        internal static partial void ExtendFailed(ILogger logger, string resource, Exception exception);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 307, Level = LogLevel.Error,
            Message = "Distributed lock on '{Resource}' (fencing token {FencingToken}) was lost: {Reason}")]
        internal static partial void LockLost(ILogger logger, string resource, long fencingToken, string reason);
    }
}
