using SharedKernel.Idempotency.Abstractions;

namespace SharedKernel.Idempotency.Redis.Internal;

/// <summary>Composes the Redis key of an idempotency entry.</summary>
/// <remarks>
/// Key shape: <c>sk:idempotency:{tenantScope}:{kind}:{rawKey}</c>. <c>tenantScope</c> is
/// <see cref="IdempotencyTenantScope"/>'s encoding (a tenant id in "D" form, or <c>no-tenant</c>); <c>kind</c> is
/// <c>key</c> for <see cref="IdempotencyPurpose.Request"/> and <c>msg</c> for <see cref="IdempotencyPurpose.Message"/>,
/// so a request key can never collide with a message id inside one keyspace. Request keys are byte-identical to the
/// keys written before P-568.
/// </remarks>
internal static class RedisIdempotencyKeyBuilder
{
    private const string Prefix = "sk:idempotency";
    private const string RequestKind = "key";
    private const string MessageKind = "msg";

    /// <summary>Builds the Redis key for <paramref name="key"/>.</summary>
    public static string Build(string tenantScope, IdempotencyPurpose purpose, string key) =>
        $"{Prefix}:{tenantScope}:{Kind(purpose)}:{key}";

    private static string Kind(IdempotencyPurpose purpose) => purpose switch
    {
        IdempotencyPurpose.Request => RequestKind,
        IdempotencyPurpose.Message => MessageKind,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown idempotency purpose."),
    };
}
