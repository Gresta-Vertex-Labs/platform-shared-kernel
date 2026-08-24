using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.DistributedLocking;

/// <summary>
/// Sources monotonically increasing fencing tokens for distributed-lock acquisitions from an
/// atomic, per-resource Redis counter.
/// </summary>
/// <remarks>
/// <para>
/// Backed by <c>IDatabase.StringIncrementAsync</c> on a dedicated per-resource key,
/// <c>sharedkernel:lock:fencing:{resource}</c> — atomic and monotonic at the Redis server,
/// never a client-generated GUID or wall-clock timestamp. Counters are scoped per lock
/// resource, not global: two different resources have entirely independent fencing
/// sequences.
/// </para>
/// <para>
/// Callers must invoke <see cref="NextAsync"/> only after RedLock itself has confirmed a
/// successful acquisition — never before, and never for a failed or contended attempt. This
/// is a strict-monotonicity guarantee, not a no-gaps guarantee: gaps from failed attempts,
/// or from a resource simply never being consulted, are acceptable.
/// </para>
/// <para>
/// Reads/writes the counter through the caller-supplied <see cref="IConnectionMultiplexer"/> —
/// the same shared connection this package already sources via <c>AddRedisConnection</c>
/// (<c>SharedKernel.Caching.Redis.Core</c>). No second connection is opened.
/// </para>
/// </remarks>
internal static class RedisFencingTokenSource
{
    private const string KeyPrefix = "sharedkernel:lock:fencing:";

    /// <summary>
    /// Atomically increments and returns the next fencing token for <paramref name="resource"/>.
    /// </summary>
    /// <param name="multiplexer">The shared Redis connection multiplexer.</param>
    /// <param name="resource">The lock resource name the fencing sequence is scoped to.</param>
    internal static async ValueTask<long> NextAsync(IConnectionMultiplexer multiplexer, string resource)
    {
        var db = multiplexer.GetDatabase();
        return await db.StringIncrementAsync(BuildKey(resource)).ConfigureAwait(false);
    }

    private static RedisKey BuildKey(string resource) => KeyPrefix + resource;
}
