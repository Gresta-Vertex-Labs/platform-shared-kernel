using StackExchange.Redis;

namespace SharedKernel.Idempotency.Redis.Internal;

/// <summary>
/// Narrow exception classifier recognizing only genuine Redis connectivity/timeout failures
/// (D-07) — never a blanket <c>catch (Exception)</c>. Used uniformly by every store method so a
/// mid-flight outage is handled identically regardless of which call it interrupts.
/// </summary>
internal static class RedisStoreUnavailableClassifier
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/> represents Redis being
    /// unreachable or unresponsive, as opposed to a programming error or a data-shape defect.
    /// </summary>
    public static bool IsStoreUnavailable(Exception exception) => exception switch
    {
        RedisConnectionException => true,
        RedisTimeoutException => true,
        RedisServerException => true,
        TimeoutException => true,
        _ => false,
    };
}
