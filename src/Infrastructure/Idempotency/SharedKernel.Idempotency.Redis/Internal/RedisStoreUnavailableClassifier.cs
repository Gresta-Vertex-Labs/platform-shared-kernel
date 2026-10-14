using StackExchange.Redis;

namespace SharedKernel.Idempotency.Redis.Internal;

/// <summary>
/// Narrow exception classifier recognizing only genuine Redis connectivity, timeout and transient server-state
/// failures — never a blanket <c>catch (Exception)</c>. Used uniformly by every store method so a mid-flight outage
/// is handled identically regardless of which call it interrupts.
/// </summary>
/// <remarks>
/// A <see cref="RedisServerException"/> is an outage only when the server says it cannot serve the call right now
/// (it is loading, busy, read-only, down, asking for a retry or out of memory). Any other server reply — a script
/// or syntax error, a wrong-type key — is a defect that fails the same way on every retry, so it propagates raw
/// instead of being reported as an outage and, under fail-open, silently dropping duplicate protection.
/// </remarks>
internal static class RedisStoreUnavailableClassifier
{
    private static readonly string[] TransientServerPrefixes =
    [
        "LOADING",
        "BUSY",
        "READONLY",
        "MASTERDOWN",
        "CLUSTERDOWN",
        "TRYAGAIN",
        "OOM",
    ];

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/> represents Redis being unreachable,
    /// unresponsive or temporarily unable to serve, as opposed to a programming error or a data-shape defect.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>Whether the exception is a store outage.</returns>
    public static bool IsStoreUnavailable(Exception exception) => exception switch
    {
        RedisConnectionException => true,
        RedisTimeoutException => true,
        RedisServerException server => IsTransientServerState(server.Message),
        TimeoutException => true,
        _ => false,
    };

    private static bool IsTransientServerState(string message)
    {
        foreach (var prefix in TransientServerPrefixes)
        {
            if (message.StartsWith(prefix, StringComparison.Ordinal)
                && (message.Length == prefix.Length || message[prefix.Length] == ' '))
            {
                return true;
            }
        }

        return false;
    }
}
