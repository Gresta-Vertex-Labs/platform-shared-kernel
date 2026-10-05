using SharedKernel.Execution.Tenancy;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Idempotency.Redis.Internal;
using Xunit;

namespace SharedKernel.Idempotency.Redis.Tests.Internal;

public sealed class RedisIdempotencyKeyBuilderTests
{
    [Fact]
    public void Build_Request_UsesTheKeyKind_AndTheTenantInDForm()
    {
        var tenantId = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");

        var key = RedisIdempotencyKeyBuilder.Build(IdempotencyTenantScope.For(new TenantId(tenantId)), IdempotencyPurpose.Request, "abc");

        // Byte-identical to the pre-P-568 request-store key.
        Assert.Equal("sk:idempotency:7c9e6679-7425-40de-944b-e07fc1f90ae7:key:abc", key);
    }

    [Fact]
    public void Build_Message_UsesTheMsgKind()
    {
        var messageId = Guid.NewGuid();

        var key = RedisIdempotencyKeyBuilder.Build(IdempotencyTenantScope.NoTenant, IdempotencyPurpose.Message, messageId.ToString("D"));

        Assert.Equal($"sk:idempotency:no-tenant:msg:{messageId:D}", key);
    }

    [Fact]
    public void Build_SameRawKey_DifferentPurposes_NeverCollide()
    {
        var request = RedisIdempotencyKeyBuilder.Build(IdempotencyTenantScope.NoTenant, IdempotencyPurpose.Request, "same");
        var message = RedisIdempotencyKeyBuilder.Build(IdempotencyTenantScope.NoTenant, IdempotencyPurpose.Message, "same");

        Assert.NotEqual(request, message);
    }

    [Fact]
    public void Build_UnknownPurpose_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RedisIdempotencyKeyBuilder.Build(IdempotencyTenantScope.NoTenant, (IdempotencyPurpose)42, "k"));
}
