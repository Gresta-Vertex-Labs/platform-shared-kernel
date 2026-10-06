using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.DistributedLocking.Implementations;

/// <summary>
/// The Redis keys and Lua scripts behind locks and leases. Each script runs atomically on the server.
/// </summary>
internal static class RedisLockScripts
{
    // Both keys share the hash tag {resource}, so the acquire script is valid on Redis Cluster.
    private const string LockKeyPrefix = "sharedkernel:lock:";
    private const string FencingKeyPrefix = "sharedkernel:lock-fencing:";

    // Claims the lock key only when it is free and, in the same atomic step, issues the next fencing
    // token. Returns the token, or 0 when another owner holds the key.
    internal const string Acquire = """
        if redis.call('SET', KEYS[1], ARGV[1], 'NX', 'PX', ARGV[2]) then
          return redis.call('INCR', KEYS[2])
        end
        return 0
        """;

    // Extends the lock only while this owner still holds it. Returns 1 when extended, 0 when lost.
    internal const string Extend = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
          return redis.call('PEXPIRE', KEYS[1], ARGV[2])
        end
        return 0
        """;

    // Deletes the lock only while this owner still holds it, never another owner's lock.
    internal const string Release = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
          return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    internal static RedisKey LockKey(string resource) => LockKeyPrefix + "{" + resource + "}";

    // Never expires: the counter must keep increasing for the life of the resource name.
    internal static RedisKey FencingKey(string resource) => FencingKeyPrefix + "{" + resource + "}";

    // PX takes whole milliseconds and rejects 0.
    internal static long ToMilliseconds(TimeSpan duration) => Math.Max(1, (long)Math.Ceiling(duration.TotalMilliseconds));

    // Every caller wraps only one ScriptEvaluateAsync call, so whatever it throws is the client failing to reach the
    // store, never this package's own logic. StackExchange.Redis does not always wrap that in a RedisException:
    // - OperationCanceledException: the call takes no token, so a cancellation is the client abandoning a frozen or
    //   dropped store, never a caller's cancellation;
    // - InvalidOperationException (incl. ObjectDisposedException): the socket pipe was completed or the multiplexer
    //   disposed under an in-flight call ("Reading is not allowed after reader was completed");
    // - IOException / SocketException: a transport error that surfaced unwrapped.
    internal static bool IsStoreFailure(Exception exception) =>
        exception is RedisException
            or TimeoutException
            or OperationCanceledException
            or InvalidOperationException
            or IOException
            or System.Net.Sockets.SocketException;
}
