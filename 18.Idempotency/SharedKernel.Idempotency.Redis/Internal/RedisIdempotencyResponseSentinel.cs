namespace SharedKernel.Idempotency.Redis.Internal;

/// <summary>
/// The sentinel value written by <see cref="MessageStore.RedisIdempotencyMessageStore"/>'s
/// reservation <c>SET key &lt;sentinel&gt; NX PX</c>. <see cref="KeyStore.RedisRequestIdempotencyStore"/>
/// does not use this sentinel — its entries are Redis hashes with an explicit <c>status</c> field,
/// so "reserved, no response yet" is represented directly rather than inferred from a placeholder
/// value. Chosen so it can never collide with real message-store content, though the message store
/// never actually reads this value back — it exists only because <c>SET</c> requires some value.
/// </summary>
internal static class RedisIdempotencyResponseSentinel
{
    /// <summary>
    /// The literal sentinel value. A key holding exactly this value has been reserved but has not
    /// yet had a response stored via <c>StoreResponseAsync</c>.
    /// </summary>
    internal const string Value = "~sk-idempotency-reserved~";
}
