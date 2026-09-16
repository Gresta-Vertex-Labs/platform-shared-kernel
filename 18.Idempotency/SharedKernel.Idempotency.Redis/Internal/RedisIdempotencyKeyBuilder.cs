namespace SharedKernel.Idempotency.Redis.Internal;

/// <summary>
/// Composes tenant-scoped Redis key strings for both store classes in this package.
/// </summary>
/// <remarks>
/// Key shape: <c>{prefix}:{tenantSegment}:{kind}:{rawKey}</c> (D-03). <c>kind</c> distinguishes
/// the key-store namespace from the message-store namespace so the two can never collide inside
/// one Redis keyspace, even if a caller's raw idempotency key happens to equal a message id's
/// string form. Not shared with <c>SharedKernel.Idempotency.EfCore</c> — this domain deliberately
/// has no shared <c>.Core</c> package (18.Idempotency/CLAUDE.md, "Code shared by both providers:
/// Nowhere — duplicate it").
/// </remarks>
internal static class RedisIdempotencyKeyBuilder
{
    private const string Prefix = "sk:idempotency";
    private const string KeyStoreKind = "key";
    private const string MessageStoreKind = "msg";

    /// <summary>
    /// The fixed, non-caller-suppliable segment substituted for a <see langword="null"/> tenant
    /// identity (D-02). Never omitted — a null-tenant entry always occupies this exact segment, so
    /// it can never collide with a real tenant's GUID-formatted segment.
    /// </summary>
    internal const string NonTenantSegment = "no-tenant";

    /// <summary>Builds the Redis key for a key-store entry (<see cref="KeyStore.RedisRequestIdempotencyStore"/>).</summary>
    public static string BuildKeyStoreKey(Guid? tenantId, string idempotencyKey) =>
        $"{Prefix}:{TenantSegment(tenantId)}:{KeyStoreKind}:{idempotencyKey}";

    /// <summary>Builds the Redis key for a message-store entry (<see cref="MessageStore.RedisIdempotencyMessageStore"/>).</summary>
    public static string BuildMessageStoreKey(Guid? tenantId, Guid messageId) =>
        $"{Prefix}:{TenantSegment(tenantId)}:{MessageStoreKind}:{messageId:D}";

    private static string TenantSegment(Guid? tenantId) => tenantId is { } id ? id.ToString("D") : NonTenantSegment;
}
