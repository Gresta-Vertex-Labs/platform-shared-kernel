using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Logging;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.DistributedLocking.Implementations;

/// <summary>
/// <see cref="IDistributedLockService"/> over Redis using atomic Lua scripts: acquisition and
/// fencing-token issue happen in one server-side step, and extension and release apply only to the
/// caller's own lock.
/// </summary>
/// <remarks>
/// Guarantees assume one authoritative Redis primary. After a failover, a replica that had not yet
/// received a lock key can grant it again; fencing tokens are what protect the resource then.
/// </remarks>
internal sealed partial class RedisDistributedLockService : IDistributedLockService
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RedisDistributedLockService> _logger;

    public RedisDistributedLockService(
        IConnectionMultiplexer multiplexer,
        TimeProvider timeProvider,
        ILogger<RedisDistributedLockService> logger)
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _multiplexer = multiplexer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async ValueTask<IDistributedLock?> TryAcquireAsync(
        string resource,
        DistributedLockOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        options ??= DistributedLockOptions.Default;

        IDatabase database = _multiplexer.GetDatabase();
        string owner = Guid.NewGuid().ToString("N");
        long startedAt = _timeProvider.GetTimestamp();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            long requestedAt = _timeProvider.GetTimestamp();
            long fencingToken = await ClaimAsync(database, resource, owner, options.Expiry).ConfigureAwait(false);
            if (fencingToken > 0)
            {
                var handle = new RedisDistributedLock(database, resource, owner, fencingToken, options.Expiry, requestedAt, _timeProvider, _logger);
                handle.StartKeepAlive();
                Log.LockAcquired(_logger, resource, fencingToken);
                return handle;
            }

            TimeSpan remaining = options.WaitTime - _timeProvider.GetElapsedTime(startedAt);
            if (remaining <= TimeSpan.Zero)
            {
                Log.LockContended(_logger, resource);
                return null;
            }

            TimeSpan delay = remaining < options.RetryInterval ? remaining : options.RetryInterval;
            await Task.Delay(delay, _timeProvider, ct).ConfigureAwait(false);
        }
    }

    public async ValueTask<DistributedLease?> TryAcquireLeaseAsync(
        string resource,
        TimeSpan duration,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        ct.ThrowIfCancellationRequested();

        DateTimeOffset requestedAt = _timeProvider.GetUtcNow();
        string owner = "lease:" + Guid.NewGuid().ToString("N");

        long fencingToken = await ClaimAsync(_multiplexer.GetDatabase(), resource, owner, duration).ConfigureAwait(false);
        if (fencingToken == 0)
        {
            Log.LeaseContended(_logger, resource);
            return null;
        }

        Log.LeaseAcquired(_logger, resource, fencingToken, duration);
        return new DistributedLease(resource, fencingToken, requestedAt + duration);
    }

    private static async Task<long> ClaimAsync(IDatabase database, string resource, string owner, TimeSpan duration)
    {
        try
        {
            RedisResult result = await database.ScriptEvaluateAsync(
                RedisLockScripts.Acquire,
                [RedisLockScripts.LockKey(resource), RedisLockScripts.FencingKey(resource)],
                [owner, RedisLockScripts.ToMilliseconds(duration)]).ConfigureAwait(false);

            return (long)result;
        }
        catch (Exception ex) when (RedisLockScripts.IsStoreFailure(ex))
        {
            throw DistributedLockUnavailableException.ForResource(resource, ex);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 300, Level = LogLevel.Debug,
            Message = "Distributed lock acquired on '{Resource}' (fencing token {FencingToken})")]
        internal static partial void LockAcquired(ILogger logger, string resource, long fencingToken);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 301, Level = LogLevel.Debug,
            Message = "Distributed lock on '{Resource}' is held by another owner")]
        internal static partial void LockContended(ILogger logger, string resource);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 302, Level = LogLevel.Debug,
            Message = "Lease acquired on '{Resource}' for {Duration} (fencing token {FencingToken})")]
        internal static partial void LeaseAcquired(ILogger logger, string resource, long fencingToken, TimeSpan duration);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 303, Level = LogLevel.Debug,
            Message = "Lease on '{Resource}' is held by another owner")]
        internal static partial void LeaseContended(ILogger logger, string resource);
    }
}
