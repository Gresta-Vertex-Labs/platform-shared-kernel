namespace SharedKernel.Idempotency.Redis.Internal;

/// <summary>
/// The sentinel value written by the initial reservation <c>SET key &lt;sentinel&gt; NX PX</c>
/// (D-04). Chosen so it can never collide with real <see cref="System.Text.Json.JsonSerializer"/>
/// output: every value that type produces for a response payload starts with one of
/// <c>{ [ " -</c> or a digit, or the literals <c>true</c>/<c>false</c>/<c>null</c>. This sentinel
/// starts with <c>~</c>, a character JSON never emits as the first character of a serialized
/// payload, so a stored value equal to this constant unambiguously means "reserved, no response
/// written yet" rather than a real (if unlikely) response body.
/// </summary>
internal static class RedisIdempotencyResponseSentinel
{
    /// <summary>
    /// The literal sentinel value. A key holding exactly this value has been reserved but has not
    /// yet had a response stored via <c>StoreResponseAsync</c>.
    /// </summary>
    internal const string Value = "~sk-idempotency-reserved~";
}
