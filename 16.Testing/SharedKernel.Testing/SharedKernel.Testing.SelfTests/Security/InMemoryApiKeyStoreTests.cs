using SharedKernel.Execution.Tenancy;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Random;
using SharedKernel.Security.ApiKey.Keys;
using SharedKernel.Security.ApiKey.Options;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Security;

public sealed class InMemoryApiKeyStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly TenantId TenantId = new TenantId(Guid.Parse("9a1c7e52-0d3b-4f6a-8e2d-5b4c3a2f1e0d"));

    [Fact]
    public async Task FindAsync_UnknownKeyId_ReturnsNull()
    {
        var store = new InMemoryApiKeyStore();
        store.Add(new ApiKeyRecord("key-1", "hash-1", "client-1"));

        Assert.Null(await store.FindAsync("key-2", CancellationToken.None));
        Assert.Null(await store.FindAsync("KEY-1", CancellationToken.None));
    }

    [Fact]
    public async Task FindAsync_NullKeyId_ThrowsArgumentNull()
    {
        var store = new InMemoryApiKeyStore();

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.FindAsync(null!, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task AddRecord_ThenFind_ReturnsSameRecord()
    {
        var store = new InMemoryApiKeyStore();
        var record = new ApiKeyRecord("key-1", "hash-1", "client-1") { Roles = ["admin"] };

        store.Add(record);

        Assert.Same(record, await store.FindAsync("key-1", CancellationToken.None));
    }

    [Fact]
    public async Task AddRecord_SameKeyIdTwice_ReplacesRecord()
    {
        var store = new InMemoryApiKeyStore();
        store.Add(new ApiKeyRecord("key-1", "hash-1", "client-1"));
        var replacement = new ApiKeyRecord("key-1", "hash-2", "client-2");

        store.Add(replacement);

        Assert.Same(replacement, await store.FindAsync("key-1", CancellationToken.None));
    }

    [Fact]
    public void AddRecord_Null_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new InMemoryApiKeyStore().Add((ApiKeyRecord)null!));
    }

    [Fact]
    public async Task AddGeneratedKey_StoresIdHashAndOptionalFields()
    {
        var store = new InMemoryApiKeyStore();
        var key = Generate();

        var record = store.Add(key, "client-1", TenantId, ["orders:read"], Now.AddDays(30));

        Assert.Equal(key.KeyId, record.KeyId);
        Assert.Equal(key.KeyHash, record.KeyHash);
        Assert.NotEqual(key.Key, record.KeyHash);
        Assert.Equal("client-1", record.ClientId);
        Assert.Equal(TenantId, record.TenantId);
        Assert.Equal(["orders:read"], record.Permissions);
        Assert.Equal(Now.AddDays(30), record.ExpiresAt);
        Assert.Null(record.RevokedAt);
        Assert.Empty(record.Roles);
        Assert.Same(record, await store.FindAsync(key.KeyId, CancellationToken.None));
    }

    [Fact]
    public void AddGeneratedKey_OptionalArgumentsOmitted_Defaults()
    {
        var record = new InMemoryApiKeyStore().Add(Generate(), "client-1");

        Assert.Null(record.TenantId);
        Assert.Empty(record.Permissions);
        Assert.Null(record.ExpiresAt);
        Assert.Null(record.RevokedAt);
    }

    [Fact]
    public void AddGeneratedKey_NullKey_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new InMemoryApiKeyStore().Add((GeneratedApiKey)null!, "client-1"));
    }

    [Fact]
    public async Task Revoke_SetsRevokedAtAndKeepsOtherFields()
    {
        var store = new InMemoryApiKeyStore();
        store.Add(new ApiKeyRecord("key-1", "hash-1", "client-1")
        {
            TenantId = TenantId,
            Roles = ["admin"],
            Permissions = ["orders:write"],
            ExpiresAt = Now.AddDays(1),
        });

        store.Revoke("key-1", Now);

        var revoked = await store.FindAsync("key-1", CancellationToken.None);
        Assert.NotNull(revoked);
        Assert.Equal(Now, revoked.RevokedAt);
        Assert.Equal("key-1", revoked.KeyId);
        Assert.Equal("hash-1", revoked.KeyHash);
        Assert.Equal("client-1", revoked.ClientId);
        Assert.Equal(TenantId, revoked.TenantId);
        Assert.Equal(["admin"], revoked.Roles);
        Assert.Equal(["orders:write"], revoked.Permissions);
        Assert.Equal(Now.AddDays(1), revoked.ExpiresAt);
    }

    [Fact]
    public async Task Revoke_OtherKeysUnaffected()
    {
        var store = new InMemoryApiKeyStore();
        store.Add(new ApiKeyRecord("key-1", "hash-1", "client-1"));
        var other = new ApiKeyRecord("key-2", "hash-2", "client-1");
        store.Add(other);

        store.Revoke("key-1", Now);

        Assert.Same(other, await store.FindAsync("key-2", CancellationToken.None));
        Assert.Null(other.RevokedAt);
    }

    [Fact]
    public void Revoke_UnknownKey_ThrowsKeyNotFound()
    {
        Assert.Throws<KeyNotFoundException>(() => new InMemoryApiKeyStore().Revoke("missing", Now));
    }

    private static GeneratedApiKey Generate() =>
        new ApiKeyGenerator(new SecureRandomGenerator(), Options.Create(new ManagedApiKeyOptions { Prefix = "acme_test" })).Generate();
}
