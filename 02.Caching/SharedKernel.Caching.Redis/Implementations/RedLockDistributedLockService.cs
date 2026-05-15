using Microsoft.Extensions.Logging;
using RedLockNet;
using SharedKernel.Caching.Redis.Abstractions;

namespace SharedKernel.Caching.Redis.Implementations;

/// <summary>
/// <see cref="IDistributedLockService"/> implementation backed by RedLock.net over Redis.
/// </summary>
/// <remarks>
/// Returns <see langword="null"/> on timeout — never throws for a contended lock.
/// The returned <see cref="IAsyncDisposable"/> handle releases the lock on disposal.
/// </remarks>
internal sealed partial class RedLockDistributedLockService : IDistributedLockService
{
    private readonly IDistributedLockFactory _factory;
    private readonly ILogger<RedLockDistributedLockService> _logger;

    public RedLockDistributedLockService(
        IDistributedLockFactory factory,
        ILogger<RedLockDistributedLockService> logger)
    {
        _factory = factory;
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
            Log.LockAcquired(_logger, resource);
            return new LockHandle(redLock, resource, _logger);
        }

        // Lock not acquired within wait window — release the RedLock object and return null.
        await redLock.DisposeAsync().ConfigureAwait(false);
        Log.LockNotAcquired(_logger, resource);

        return null;
    }

    // Wraps the RedLock handle to provide IAsyncDisposable semantics.
    private sealed partial class LockHandle : IAsyncDisposable
    {
        private readonly IRedLock _redLock;
        private readonly string _resource;
        private readonly ILogger _logger;

        internal LockHandle(IRedLock redLock, string resource, ILogger logger)
        {
            _redLock = redLock;
            _resource = resource;
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
        [LoggerMessage(EventId = 2001, Level = LogLevel.Debug,
            Message = "Acquiring distributed lock on '{Resource}' (expiry={Expiry}, wait={Wait})")]
        internal static partial void AcquiringLock(ILogger logger, string resource, TimeSpan expiry, TimeSpan wait);

        [LoggerMessage(EventId = 2002, Level = LogLevel.Debug,
            Message = "Distributed lock acquired on '{Resource}'")]
        internal static partial void LockAcquired(ILogger logger, string resource);

        [LoggerMessage(EventId = 2003, Level = LogLevel.Warning,
            Message = "Distributed lock NOT acquired on '{Resource}' within wait window")]
        internal static partial void LockNotAcquired(ILogger logger, string resource);

        [LoggerMessage(EventId = 2004, Level = LogLevel.Debug,
            Message = "Releasing distributed lock on '{Resource}'")]
        internal static partial void ReleasingLock(ILogger logger, string resource);
    }
}
