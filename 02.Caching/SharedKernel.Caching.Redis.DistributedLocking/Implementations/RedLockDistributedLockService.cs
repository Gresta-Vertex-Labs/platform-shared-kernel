using Microsoft.Extensions.Logging;
using RedLockNet;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Logging;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.DistributedLocking.Implementations;

/// <summary>
/// <see cref="IDistributedLockService"/> implementation backed by RedLock.net over Redis.
/// </summary>
/// <remarks>
/// Returns <see langword="null"/> on timeout — never throws for a contended lock.
/// The returned <see cref="IAsyncDisposable"/> handle releases the lock on disposal.
/// Supports renewable locks via <see cref="AcquireRenewableAsync"/> for long-running
/// operations that need to extend their lock before expiry.
/// <para>
/// Every successful acquisition (via <see cref="AcquireAsync"/> or
/// <see cref="AcquireRenewableAsync"/>) is additionally issued a monotonically increasing
/// fencing token, sourced from an atomic per-resource Redis counter
/// (<see cref="RedisFencingTokenSource"/>). The handle returned by <see cref="AcquireAsync"/>
/// implements <see cref="IFencedLock"/> even though its declared return type
/// (<see cref="IAsyncDisposable"/>) is unchanged — see <see cref="IFencedLock"/> for the
/// standard usage contract.
/// </para>
/// </remarks>
internal sealed partial class RedLockDistributedLockService : IDistributedLockService
{
    private readonly IDistributedLockFactory _factory;
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<RedLockDistributedLockService> _logger;

    public RedLockDistributedLockService(
        IDistributedLockFactory factory,
        IConnectionMultiplexer multiplexer,
        ILogger<RedLockDistributedLockService> logger)
    {
        _factory = factory;
        _multiplexer = multiplexer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable?> AcquireAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan wait,
        TimeSpan retry,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expiry.Ticks, nameof(expiry));
        ArgumentOutOfRangeException.ThrowIfNegative(wait.Ticks, nameof(wait));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retry.Ticks, nameof(retry));

        Log.AcquiringLock(_logger, resource, expiry, wait);

        var redLock = await _factory.CreateLockAsync(
            resource,
            expiryTime: expiry,
            waitTime: wait,
            retryTime: retry,
            cancellationToken: ct).ConfigureAwait(false);

        if (redLock.IsAcquired)
        {
            // The fencing INCR fires only after RedLock itself confirms acquisition —
            // never before, and never on a failed/contended attempt.
            var fencingToken = await RedisFencingTokenSource.NextAsync(_multiplexer, resource)
                .ConfigureAwait(false);
            Log.LockAcquired(_logger, resource, fencingToken);
            return new LockHandle(redLock, resource, fencingToken, _logger);
        }

        // Lock not acquired within wait window — release the RedLock object and return null.
        await redLock.DisposeAsync().ConfigureAwait(false);
        Log.LockNotAcquired(_logger, resource);

        return null;
    }

    /// <inheritdoc />
    public async ValueTask<IRenewableLock?> AcquireRenewableAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan wait,
        TimeSpan retry,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expiry.Ticks, nameof(expiry));
        ArgumentOutOfRangeException.ThrowIfNegative(wait.Ticks, nameof(wait));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retry.Ticks, nameof(retry));

        Log.AcquiringRenewableLock(_logger, resource, expiry, wait);

        var redLock = await _factory.CreateLockAsync(
            resource,
            expiryTime: expiry,
            waitTime: wait,
            retryTime: retry,
            cancellationToken: ct).ConfigureAwait(false);

        if (redLock.IsAcquired)
        {
            // The fencing INCR fires only after RedLock itself confirms acquisition —
            // never before, and never on a failed/contended attempt.
            var fencingToken = await RedisFencingTokenSource.NextAsync(_multiplexer, resource)
                .ConfigureAwait(false);
            Log.RenewableLockAcquired(_logger, resource, fencingToken);
            return new RedLockRenewableLock(
                redLock,
                _factory,
                _multiplexer,
                resource,
                expiry,
                renewalWait: wait,
                renewalRetry: retry,
                fencingToken,
                _logger);
        }

        // Lock not acquired within wait window — release the RedLock object and return null.
        await redLock.DisposeAsync().ConfigureAwait(false);
        Log.RenewableLockNotAcquired(_logger, resource);

        return null;
    }

    // Wraps the RedLock handle to provide IAsyncDisposable semantics. Additionally implements
    // IFencedLock (Phase 43) so callers who want the fencing token can perform an
    // `is IFencedLock`/`as IFencedLock` check on the handle AcquireAsync returns — the
    // declared IAsyncDisposable? return type itself is unchanged.
    private sealed partial class LockHandle : IFencedLock
    {
        private readonly IRedLock _redLock;
        private readonly string _resource;
        private readonly ILogger _logger;

        /// <inheritdoc />
        public long FencingToken { get; }

        internal LockHandle(IRedLock redLock, string resource, long fencingToken, ILogger logger)
        {
            _redLock = redLock;
            _resource = resource;
            FencingToken = fencingToken;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
        {
            Log.ReleasingLock(_logger, _resource);
            await _redLock.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 300, Level = LogLevel.Debug,
            Message = "Acquiring distributed lock on '{Resource}' (expiry={Expiry}, wait={Wait})")]
        internal static partial void AcquiringLock(ILogger logger, string resource, TimeSpan expiry, TimeSpan wait);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 301, Level = LogLevel.Debug,
            Message = "Distributed lock acquired on '{Resource}' (fencing token={FencingToken})")]
        internal static partial void LockAcquired(ILogger logger, string resource, long fencingToken);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 302, Level = LogLevel.Warning,
            Message = "Distributed lock NOT acquired on '{Resource}' within wait window")]
        internal static partial void LockNotAcquired(ILogger logger, string resource);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 303, Level = LogLevel.Debug,
            Message = "Releasing distributed lock on '{Resource}'")]
        internal static partial void ReleasingLock(ILogger logger, string resource);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 304, Level = LogLevel.Debug,
            Message = "Acquiring renewable distributed lock on '{Resource}' (expiry={Expiry}, wait={Wait})")]
        internal static partial void AcquiringRenewableLock(ILogger logger, string resource, TimeSpan expiry, TimeSpan wait);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 305, Level = LogLevel.Debug,
            Message = "Renewable distributed lock acquired on '{Resource}' (fencing token={FencingToken})")]
        internal static partial void RenewableLockAcquired(ILogger logger, string resource, long fencingToken);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 306, Level = LogLevel.Warning,
            Message = "Renewable distributed lock NOT acquired on '{Resource}' within wait window")]
        internal static partial void RenewableLockNotAcquired(ILogger logger, string resource);
    }
}
