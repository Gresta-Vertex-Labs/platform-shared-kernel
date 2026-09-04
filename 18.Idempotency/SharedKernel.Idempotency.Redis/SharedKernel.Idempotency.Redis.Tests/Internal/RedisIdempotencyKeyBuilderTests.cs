using SharedKernel.Idempotency.Redis.Internal;
using Xunit;

namespace SharedKernel.Idempotency.Redis.Tests.Internal;

public sealed class RedisIdempotencyKeyBuilderTests
{
    [Fact]
    public void BuildKeyStoreKey_WithTenant_IncludesTenantSegment()
    {
        var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var key = RedisIdempotencyKeyBuilder.BuildKeyStoreKey(tenantId, "order-42");

        Assert.Equal("sk:idempotency:11111111-1111-1111-1111-111111111111:key:order-42", key);
    }

    [Fact]
    public void BuildKeyStoreKey_WithNullTenant_UsesFixedNonTenantSegment()
    {
        var key = RedisIdempotencyKeyBuilder.BuildKeyStoreKey(null, "order-42");

        Assert.Equal($"sk:idempotency:{RedisIdempotencyKeyBuilder.NonTenantSegment}:key:order-42", key);
        Assert.Equal("sk:idempotency:no-tenant:key:order-42", key);
    }

    [Fact]
    public void BuildMessageStoreKey_WithTenant_IncludesTenantSegment()
    {
        var tenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var messageId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var key = RedisIdempotencyKeyBuilder.BuildMessageStoreKey(tenantId, messageId);

        Assert.Equal(
            "sk:idempotency:22222222-2222-2222-2222-222222222222:msg:33333333-3333-3333-3333-333333333333",
            key);
    }

    [Fact]
    public void KeyStoreAndMessageStore_WithIdenticalRawStringForm_NeverCollide()
    {
        var tenantId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var sharedGuid = Guid.Parse("77777777-7777-7777-7777-777777777777");

        // A caller-supplied idempotency key that happens to look exactly like a message id's
        // string form must still land in a different Redis key, because "kind" (key/msg)
        // partitions the two namespaces (D-03).
        var keyStoreKey = RedisIdempotencyKeyBuilder.BuildKeyStoreKey(tenantId, sharedGuid.ToString("D"));
        var messageStoreKey = RedisIdempotencyKeyBuilder.BuildMessageStoreKey(tenantId, sharedGuid);

        Assert.NotEqual(keyStoreKey, messageStoreKey);
    }

    [Fact]
    public void DifferentTenants_WithIdenticalRawKey_ProduceDifferentKeys()
    {
        var tenantA = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var tenantB = Guid.Parse("66666666-6666-6666-6666-666666666666");

        var keyA = RedisIdempotencyKeyBuilder.BuildKeyStoreKey(tenantA, "same-raw-key");
        var keyB = RedisIdempotencyKeyBuilder.BuildKeyStoreKey(tenantB, "same-raw-key");

        Assert.NotEqual(keyA, keyB);
    }
}
