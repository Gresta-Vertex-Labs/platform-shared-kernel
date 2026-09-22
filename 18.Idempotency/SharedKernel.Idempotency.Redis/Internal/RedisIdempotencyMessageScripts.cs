namespace SharedKernel.Idempotency.Redis.Internal;

/// <summary>
/// Lua scripts backing the message-store reservation protocol.
/// </summary>
/// <remarks>
/// Each script is a single atomic server-side operation. Doing the same work as separate
/// <c>GET</c>/<c>SET</c> round trips would reintroduce the race the reservation contract exists to
/// remove: two deliveries could both read "free" and both claim the id.
/// </remarks>
internal static class RedisIdempotencyMessageScripts
{
    /// <summary>
    /// Value stored once a message has been consumed to completion, distinguishing a finished
    /// message from one merely reserved and in flight.
    /// </summary>
    internal const string CompletedValue = "~sk-idempotency-completed~";

    /// <summary>
    /// Attempts to claim the key. KEYS[1] = key; ARGV[1] = new reservation token;
    /// ARGV[2] = in-flight lease in milliseconds; ARGV[3] = the completed sentinel.
    /// </summary>
    /// <remarks>
    /// Returns <c>"started"</c> when this caller now owns the key, <c>"completed"</c> when the
    /// message was already consumed, or <c>"in_progress"</c> when another delivery holds the lease.
    /// </remarks>
    internal const string TryBegin = """
        local current = redis.call('GET', KEYS[1])
        if current == false then
            redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[2])
            return 'started'
        end
        if current == ARGV[3] then
            return 'completed'
        end
        return 'in_progress'
        """;

    /// <summary>
    /// Marks the key completed, but only if this caller still owns it.
    /// KEYS[1] = key; ARGV[1] = reservation token; ARGV[2] = retention in milliseconds;
    /// ARGV[3] = the completed sentinel.
    /// </summary>
    /// <remarks>
    /// The ownership check makes the write safe against a lease that expired mid-consume and was
    /// taken over by a redelivery: the original, now-stale holder must not overwrite the new one.
    /// </remarks>
    internal const string Complete = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            redis.call('SET', KEYS[1], ARGV[3], 'PX', ARGV[2])
            return 1
        end
        return 0
        """;

    /// <summary>
    /// Releases the key, but only if this caller still owns it.
    /// KEYS[1] = key; ARGV[1] = reservation token.
    /// </summary>
    internal const string Release = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            redis.call('DEL', KEYS[1])
            return 1
        end
        return 0
        """;
}
