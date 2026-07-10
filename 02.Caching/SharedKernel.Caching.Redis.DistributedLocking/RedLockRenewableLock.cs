using Microsoft.Extensions.Logging;
using RedLockNet;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Logging;

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
/// </remarks>
internal sealed partial class RedLockRenewableLock : IRenewableLock
{
    // Guarded by _renewLock; swapped atomically on every successful renewal.
    private IRedLock _redLock;

    private readonly IDistributedLockFactory _factory;
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
        string resource,
        TimeSpan expiry,
        TimeSpan renewalWait,
        TimeSpan renewalRetry,
        ILogger logger)
    {
        _redLock = redLock;
        _factory = factory;
        _resource = resource;
        _expiry = expiry;
        _renewalWait = renewalWait;
        _renewalRetry = renewalRetry;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAcquired => !_disposed && _redLock.IsAcquired;

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

            // New lock acquired — replace the stored handle.
            _redLock = newLock;

            Log.LockRenewed(_logger, _resource, _expiry);
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
            Message = "Renewable lock on '{Resource}' renewed successfully (new expiry: {Expiry})")]
        internal static partial void LockRenewed(ILogger logger, string resource, TimeSpan expiry);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 311, Level = LogLevel.Debug,
            Message = "Releasing renewable lock on '{Resource}'")]
        internal static partial void ReleasingRenewableLock(ILogger logger, string resource);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 312, Level = LogLevel.Warning,
            Message = "Failed to dispose old RedLock handle during renewal on '{Resource}'")]
        internal static partial void OldLockDisposeFailed(ILogger logger, string resource, Exception ex);
    }
}
