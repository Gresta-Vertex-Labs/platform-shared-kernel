using SharedKernel.Execution.Tenancy;
using SharedKernel.Cryptography.Random;
using SharedKernel.Security.ApiKey.Keys;
using SharedKernel.Security.ApiKey.Options;
using SharedKernel.Security.ApiKey.Validation;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Keys;

public sealed class ManagedApiKeyValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TenantId TenantId = new TenantId(Guid.Parse("5b0e7a51-3f7c-4f55-9d3a-6f1f2a9c8e11"));

    private readonly InMemoryApiKeyStore _store = new();
    private readonly FakeClock _clock = new(Now);
    private readonly ApiKeyGenerator _generator = CreateGenerator("acme_live");

    [Fact]
    public async Task ValidateAsync_ValidKey_ReturnsRecordIdentity()
    {
        GeneratedApiKey key = _generator.Generate();
        _store.Add(new ApiKeyRecord(key.KeyId, key.KeyHash, "billing-service")
        {
            TenantId = TenantId,
            Roles = ["admin", "auditor"],
            Permissions = ["invoices:read", "invoices:write"],
            ExpiresAt = Now.AddDays(1),
        });

        ApiKeyValidationResult result = await CreateValidator().ValidateAsync(key.Key, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("billing-service", result.ClientId);
        Assert.Equal(TenantId, result.TenantId);
        Assert.Equal(["admin", "auditor"], result.Roles);
        Assert.Equal(["invoices:read", "invoices:write"], result.Permissions);
        Assert.Equal(key.KeyId, result.KeyId);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_ValidKeyWithoutTenantOrGrants_ReturnsEmptyGrants()
    {
        GeneratedApiKey key = _generator.Generate();
        _store.Add(key, "reporting");

        ApiKeyValidationResult result = await CreateValidator().ValidateAsync(key.Key, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Null(result.TenantId);
        Assert.Empty(result.Roles);
        Assert.Empty(result.Permissions);
    }

    [Fact]
    public async Task ValidateAsync_RecordWithEmptyTenant_HasNoTenant()
    {
        GeneratedApiKey key = _generator.Generate();
        _store.Add(new ApiKeyRecord(key.KeyId, key.KeyHash, "reporting") { TenantId = default(TenantId) });

        ApiKeyValidationResult result = await CreateValidator().ValidateAsync(key.Key, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Null(result.TenantId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("acme_live_0123456789ABCDEF_abcdefghijklmnopqrstuvwxyzABCDEFxxxxxx")]
    public async Task ValidateAsync_MalformedKey_FailsWithoutKeyId(string presented)
    {
        ApiKeyValidationResult result = await CreateValidator().ValidateAsync(presented, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("Malformed", result.FailureReason);
        Assert.Null(result.KeyId);
        Assert.Null(result.ClientId);
    }

    [Fact]
    public async Task ValidateAsync_KeyWithOneCharacterChanged_FailsAsMalformedWithoutStoreLookup()
    {
        GeneratedApiKey key = _generator.Generate();
        _store.Add(key, "billing-service");
        char last = key.Key[^7];
        string typo = key.Key[..^7] + (last == 'a' ? 'b' : 'a') + key.Key[^6..];
        var store = new CountingStore(_store);

        ApiKeyValidationResult result = await CreateValidator(store).ValidateAsync(typo, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("Malformed", result.FailureReason);
        Assert.Equal(0, store.Lookups);
    }

    [Fact]
    public async Task ValidateAsync_TestKeyInLiveService_FailsWithWrongPrefix()
    {
        GeneratedApiKey testKey = CreateGenerator("acme_test").Generate();
        _store.Add(testKey, "billing-service");
        var store = new CountingStore(_store);

        ApiKeyValidationResult result = await CreateValidator(store).ValidateAsync(testKey.Key, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("WrongPrefix", result.FailureReason);
        Assert.Equal(testKey.KeyId, result.KeyId);
        Assert.Equal(0, store.Lookups);
    }

    [Fact]
    public async Task ValidateAsync_PrefixThatExtendsConfiguredPrefix_FailsWithWrongPrefix()
    {
        GeneratedApiKey key = CreateGenerator("acme_live_eu").Generate();
        _store.Add(key, "billing-service");

        ApiKeyValidationResult result = await CreateValidator().ValidateAsync(key.Key, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("WrongPrefix", result.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_UnknownKeyId_FailsWithUnknownKey()
    {
        GeneratedApiKey key = _generator.Generate();

        ApiKeyValidationResult result = await CreateValidator().ValidateAsync(key.Key, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("UnknownKey", result.FailureReason);
        Assert.Equal(key.KeyId, result.KeyId);
        Assert.Null(result.ClientId);
    }

    [Fact]
    public async Task ValidateAsync_KnownKeyIdWithDifferentSecret_FailsWithUnknownKey()
    {
        GeneratedApiKey stored = _generator.Generate();
        _store.Add(stored, "billing-service");
        string secret = new('Z', 32);
        string forged = ApiKeyFormat.Compose("acme_live", stored.KeyId, secret);
        Assert.True(ApiKeyFormat.TryParse(forged, out _, out string? forgedKeyId));
        Assert.Equal(stored.KeyId, forgedKeyId);

        ApiKeyValidationResult result = await CreateValidator().ValidateAsync(forged, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("UnknownKey", result.FailureReason);
        Assert.Null(result.ClientId);
    }

    [Fact]
    public async Task ValidateAsync_StoreReturnsRecordForDifferentKeyId_FailsWithUnknownKey()
    {
        GeneratedApiKey presented = _generator.Generate();
        var store = new FixedRecordStore(new ApiKeyRecord("ZZZZZZZZZZZZZZZZ", presented.KeyHash, "billing-service"));

        ApiKeyValidationResult result = await CreateValidator(store).ValidateAsync(presented.Key, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("UnknownKey", result.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_RevokedInThePast_FailsWithRevoked()
    {
        GeneratedApiKey key = _generator.Generate();
        _store.Add(key, "billing-service");
        _store.Revoke(key.KeyId, Now.AddMinutes(-1));

        ApiKeyValidationResult result = await CreateValidator().ValidateAsync(key.Key, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("Revoked", result.FailureReason);
        Assert.Equal(key.KeyId, result.KeyId);
    }

    [Fact]
    public async Task ValidateAsync_RevokedExactlyNow_FailsWithRevoked()
    {
        GeneratedApiKey key = _generator.Generate();
        _store.Add(key, "billing-service");
        _store.Revoke(key.KeyId, Now);

        ApiKeyValidationResult result = await CreateValidator().ValidateAsync(key.Key, CancellationToken.None);

        Assert.Equal("Revoked", result.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_RevocationScheduledInTheFuture_SucceedsUntilThen()
    {
        GeneratedApiKey key = _generator.Generate();
        _store.Add(key, "billing-service");
        _store.Revoke(key.KeyId, Now.AddHours(1));
        ManagedApiKeyValidator validator = CreateValidator();

        ApiKeyValidationResult before = await validator.ValidateAsync(key.Key, CancellationToken.None);
        _clock.Advance(TimeSpan.FromHours(1));
        ApiKeyValidationResult after = await validator.ValidateAsync(key.Key, CancellationToken.None);

        Assert.True(before.IsValid);
        Assert.False(after.IsValid);
        Assert.Equal("Revoked", after.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_Expired_FailsWithExpired()
    {
        GeneratedApiKey key = _generator.Generate();
        _store.Add(key, "billing-service", expiresAt: Now.AddSeconds(-1));

        ApiKeyValidationResult result = await CreateValidator().ValidateAsync(key.Key, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("Expired", result.FailureReason);
        Assert.Equal(key.KeyId, result.KeyId);
    }

    [Fact]
    public async Task ValidateAsync_ExpiresExactlyNow_FailsWithExpired()
    {
        GeneratedApiKey key = _generator.Generate();
        _store.Add(key, "billing-service", expiresAt: Now);

        ApiKeyValidationResult result = await CreateValidator().ValidateAsync(key.Key, CancellationToken.None);

        Assert.Equal("Expired", result.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_NotYetExpired_SucceedsUntilExpiry()
    {
        GeneratedApiKey key = _generator.Generate();
        _store.Add(key, "billing-service", expiresAt: Now.AddMinutes(5));
        ManagedApiKeyValidator validator = CreateValidator();

        ApiKeyValidationResult before = await validator.ValidateAsync(key.Key, CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(5));
        ApiKeyValidationResult after = await validator.ValidateAsync(key.Key, CancellationToken.None);

        Assert.True(before.IsValid);
        Assert.Equal("Expired", after.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_RevokedAndExpired_ReportsRevoked()
    {
        GeneratedApiKey key = _generator.Generate();
        _store.Add(key, "billing-service", expiresAt: Now.AddDays(-1));
        _store.Revoke(key.KeyId, Now.AddDays(-2));

        ApiKeyValidationResult result = await CreateValidator().ValidateAsync(key.Key, CancellationToken.None);

        Assert.Equal("Revoked", result.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_TwoKeysForSameClient_BothValidUntilOneIsRevoked()
    {
        GeneratedApiKey oldKey = _generator.Generate();
        GeneratedApiKey newKey = _generator.Generate();
        _store.Add(oldKey, "billing-service");
        _store.Add(newKey, "billing-service");
        ManagedApiKeyValidator validator = CreateValidator();

        Assert.True((await validator.ValidateAsync(oldKey.Key, CancellationToken.None)).IsValid);
        Assert.True((await validator.ValidateAsync(newKey.Key, CancellationToken.None)).IsValid);

        _store.Revoke(oldKey.KeyId, Now);

        Assert.False((await validator.ValidateAsync(oldKey.Key, CancellationToken.None)).IsValid);
        Assert.True((await validator.ValidateAsync(newKey.Key, CancellationToken.None)).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_NullKey_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await CreateValidator().ValidateAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_CancellationToken_IsPassedToStore()
    {
        GeneratedApiKey key = _generator.Generate();
        var store = new CountingStore(_store);
        using var cancellation = new CancellationTokenSource();

        await CreateValidator(store).ValidateAsync(key.Key, cancellation.Token);

        Assert.Equal(cancellation.Token, store.LastToken);
    }

    private ManagedApiKeyValidator CreateValidator(IApiKeyStore? store = null) =>
        new(store ?? _store, _clock, Microsoft.Extensions.Options.Options.Create(new ManagedApiKeyOptions { Prefix = "acme_live" }));

    private static ApiKeyGenerator CreateGenerator(string prefix) =>
        new(new SecureRandomGenerator(), Microsoft.Extensions.Options.Options.Create(new ManagedApiKeyOptions { Prefix = prefix }));

    private sealed class CountingStore(IApiKeyStore inner) : IApiKeyStore
    {
        public int Lookups { get; private set; }

        public CancellationToken LastToken { get; private set; }

        public ValueTask<ApiKeyRecord?> FindAsync(string keyId, CancellationToken cancellationToken)
        {
            Lookups++;
            LastToken = cancellationToken;
            return inner.FindAsync(keyId, cancellationToken);
        }
    }

    private sealed class FixedRecordStore(ApiKeyRecord record) : IApiKeyStore
    {
        public ValueTask<ApiKeyRecord?> FindAsync(string keyId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ApiKeyRecord?>(record);
    }
}
