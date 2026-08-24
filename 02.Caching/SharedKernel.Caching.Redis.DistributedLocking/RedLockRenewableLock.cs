using Microsoft.Extensions.Logging;
using RedLockNet;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Logging;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.DistributedLocking;

/// <summary>
/// <see cref="IRenewableLock"/> implementation backed by RedLock.net over Redis.
/// Renewal is implemented by re-acquiring the lock before the current expiry elapses.
/// </summary>
/// <remarks>
/// <para>
/// RedLock.net 2.3.2 does not expose a public <c>ExtendAsync</c> method on
/// <see cref="IRedLock"/>. Renewal therefore re-acquires the lock on the same resource.
/// If re-acquisition fails (lock was stolen, Redis unavailable, etc.),
/// <see cref="IsAcquired"/> transitions to <see langword="false"/> and
/// <see cref="RenewAsync"/> returns <see langword="false"/> without throwing.
/// </para>
/// <para>
/// <c>DisposeAsync</c> releases the currently held RedLock handle and transitions
/// <see cref="IsAcquired"/> to <see langword="false"/>.
/// </para>
/// <para>
/// Implements <see cref="IFencedLock"/> (Phase 43) via <see cref="IRenewableLock"/>.
/// <see cref="FencingToken"/> is sourced from an atomic per-resource Redis counter
/// (<see cref="RedisFencingTokenSource"/>) and is refreshed with a fresh <c>INCR</c> on
/// every successful renewal — see <see cref="RenewAsync"/> — so the token observed after
/// a renewal is always strictly greater than the token observed before it, directly
/// mitigating the "brief unprotected window" hazard described above.
/// </para>
/// </remarks>
internal sealed partial class RedLockRenewableLock : IRenewableLock
{
    // Guarded by _renewLock; swapped atomically on every successful renewal.
    private IRedLock _redLock;

    private readonly IDistributedLockFactory _factory;
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly string _resource;
    private readonly TimeSpan _expiry;
    private readonly TimeSpan _renewalWait;
    private readonly TimeSpan _renewalRetry;
    private readonly ILogger _logger;

    // Serialises concurrent RenewAsync / DisposeAsync calls.
    private readonly SemaphoreSlim _renewLock = new(1, 1);

    // Volatile ensures the _disposed flag is immediately visible across threads
    // without requiring the semaphore for the fast-path check.
    private volatile bool _disposed;

    internal RedLockRenewableLock(
        IRedLock redLock,
        IDistributedLockFactory factory,
        IConnectionMultiplexer multiplexer,
        string resource,
        TimeSpan expiry,
        TimeSpan renewalWait,
        TimeSpan renewalRetry,
        long fencingToken,
        ILogger logger)
    {
        _redLock = redLock;
        _factory = factory;
        _multiplexer = multiplexer;
        _resource = resource;
        _expiry = expiry;
        _renewalWait = renewalWait;
        _renewalRetry = renewalRetry;
        FencingToken = fencingToken;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAcquired => !_disposed && _redLock.IsAcquired;

    /// <inheritdoc />
    public long FencingToken { get; private set; }

    /// <inheritdoc />
    public async ValueTask<bool> RenewAsync(CancellationToken ct = default)
    {
        // Fast-path: already disposed — no semaphore needed.
        if (_disposed)
            return false;

        await _renewLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed)
                return false;

            if (!_redLock.IsAcquired)
            {
                Log.LockAlreadyLost(_logger, _resource);
                return false;
            }

            // RedLock.net 2.x has no public ExtendAsync. Renewal strategy:
            // 1. Release the current lock (brief window without the lock).
            // 2. Immediately re-acquire with a fresh handle.
            // If re-acquisition fails, the lock is considered lost.
            var oldLock = _redLock;

            // Release the old lock first to allow re-acquisition on a single-node Redis.
            try
            {
                await oldLock.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.OldLockDisposeFailed(_logger, _resource, ex);
                // Proceed to re-acquire even if the old lock dispose failed.
            }

            IRedLock newLock;
            try
            {
                newLock = await _factory.CreateLockAsync(
                    _resource,
                    expiryTime: _expiry,
                    waitTime: _renewalWait,
                    retryTime: _renewalRetry,
                    cancellationToken: ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.RenewalFailed(_logger, _resource, ex);
                // Lock is now released with no new handle — mark as lost.
                _disposed = true;
                return false;
            }

            if (!newLock.IsAcquired)
            {
                // Re-acquisition failed — release the unacquired handle and mark lost.
                await newLock.DisposeAsync().ConfigureAwait(false);
                Log.RenewalNotAcquired(_logger, _resource);
                _disposed = true;
                return false;
            }

            // New lock acquired — replace the stored handle and issue a fresh fencing token.
            // This is what makes the post-renewal handle detectable as superseding the
            // pre-renewal handle: a write attempted from the stale handle after a write from
            // the new handle has already been accepted is rejectable by a downstream
            // "reject non-increasing token" guard, even though RedLock itself cannot prevent
            // the stale handle from attempting the write in the first place.
            _redLock = newLock;
            FencingToken = await RedisFencingTokenSource.NextAsync(_multiplexer, _resource)
                .ConfigureAwait(false);

            Log.LockRenewed(_logger, _resource, _expiry, FencingToken);
            return true;
        }
        finally
        {
            _renewLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _renewLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            Log.ReleasingRenewableLock(_logger, _resource);
            await _redLock.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _renewLock.Release();
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 307, Level = LogLevel.Warning,
            Message = "Renewable lock on '{Resource}' is no longer acquired — renewal skipped")]
        internal static partial void LockAlreadyLost(ILogger logger, string resource);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 308, Level = LogLevel.Warning,
            Message = "Renewable lock on '{Resource}' could not be re-acquired during renewal")]
        internal static partial void RenewalNotAcquired(ILogger logger, string resource);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 309, Level = LogLevel.Error,
            Message = "Renewable lock on '{Resource}' renewal threw an exception")]
        internal static partial void RenewalFailed(ILogger logger, string resource, Exception ex);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 310, Level = LogLevel.Debug,
            Message = "Renewable lock on '{Resource}' renewed successfully (new expiry: {Expiry}, " +
                "new fencing token={FencingToken})")]
        internal static partial void LockRenewed(ILogger logger, string resource, TimeSpan expiry, long fencingToken);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 311, Level = LogLevel.Debug,
            Message = "Releasing renewable lock on '{Resource}'")]
        internal static partial void ReleasingRenewableLock(ILogger logger, string resource);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 312, Level = LogLevel.Warning,
            Message = "Failed to dispose old RedLock handle during renewal on '{Resource}'")]
        internal static partial void OldLockDisposeFailed(ILogger logger, string resource, Exception ex);
    }
}
