using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.KeyVault.Azure.Tests.Fakes;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests;

public sealed class AzureKeyVaultEncryptionKeyProviderTests
{
    private readonly EncryptionFixture _fixture = new();

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        IOptions<AzureKeyVaultEncryptionOptions> options = Microsoft.Extensions.Options.Options.Create(EncryptionFixture.CreateOptions());
        var random = new SecureRandomGenerator();
        FakeKeyVault vault = _fixture.Vault;

        Assert.Throws<ArgumentNullException>(() => new AzureKeyVaultEncryptionKeyProvider(null!, vault.KeyClient, vault.SecretClient, random, _fixture.Time));
        Assert.Throws<ArgumentNullException>(() => new AzureKeyVaultEncryptionKeyProvider(options, null!, vault.SecretClient, random, _fixture.Time));
        Assert.Throws<ArgumentNullException>(() => new AzureKeyVaultEncryptionKeyProvider(options, vault.KeyClient, null!, random, _fixture.Time));
        Assert.Throws<ArgumentNullException>(() => new AzureKeyVaultEncryptionKeyProvider(options, vault.KeyClient, vault.SecretClient, null!, _fixture.Time));
        Assert.Throws<ArgumentNullException>(() => new AzureKeyVaultEncryptionKeyProvider(options, vault.KeyClient, vault.SecretClient, random, null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Constructor_MissingMasterKeyName_ThrowsArgumentException(string? masterKeyName)
    {
        Assert.ThrowsAny<ArgumentException>(() => _fixture.CreateProvider(o => o.MasterKeyName = masterKeyName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Constructor_MissingDataKeySecretName_ThrowsArgumentException(string? dataKeySecretName)
    {
        Assert.ThrowsAny<ArgumentException>(() => _fixture.CreateProvider(o => o.DataKeySecretName = dataKeySecretName));
    }

    [Fact]
    public void Constructor_ValidOptions_MakesNoVaultCalls()
    {
        _fixture.CreateProvider();

        Assert.Equal(0, _fixture.Vault.TotalCalls);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_NoDataKeyVersions_ThrowsInvalidOperationExceptionNamingRotate()
    {
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.GetCurrentKeyAsync());

        Assert.Contains("RotateDataKeyAsync", exception.Message, StringComparison.Ordinal);
        Assert.Contains(EncryptionFixture.DataKeySecretName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_OnlyDisabledVersions_ThrowsInvalidOperationException()
    {
        string version = await _fixture.CreateProvider().RotateDataKeyAsync();
        _fixture.Vault.GetStoredSecret(version).Enabled = false;

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await _fixture.CreateProvider().GetCurrentKeyAsync());
    }

    [Fact]
    public async Task RotateDataKeyAsync_Always_StoresWrappedDataKeyJson()
    {
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();

        string version = await provider.RotateDataKeyAsync();

        FakeKeyVault.StoredSecret stored = _fixture.Vault.GetStoredSecret(version);
        Assert.Equal(EncryptionFixture.DataKeySecretName, stored.Name);
        Assert.Equal("application/json", stored.ContentType);
        Assert.Matches("^[0-9a-f]{32}$", version);

        using JsonDocument json = JsonDocument.Parse(stored.Value);
        Assert.Equal(["masterKeyId", "wrappedKey"], json.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(_fixture.MasterKeyId, json.RootElement.GetProperty("masterKeyId").GetString());

        byte[] wrapped = Convert.FromBase64String(json.RootElement.GetProperty("wrappedKey").GetString()!);
        byte[] dataKey = _fixture.Vault.GetRsa(EncryptionFixture.MasterKeyName, _fixture.MasterKeyVersion).Decrypt(wrapped, RSAEncryptionPadding.OaepSHA256);
        Assert.Equal(32, dataKey.Length);

        CryptographicKey current = await provider.GetCurrentKeyAsync();
        Assert.Equal(version, current.Id);
        Assert.Equal(dataKey, current.Material.ToArray());
    }

    [Fact]
    public async Task RotateDataKeyAsync_Always_MakesNewKeyCurrentImmediately()
    {
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        string first = await provider.RotateDataKeyAsync();
        CryptographicKey firstKey = await provider.GetCurrentKeyAsync();

        string second = await provider.RotateDataKeyAsync();
        CryptographicKey secondKey = await provider.GetCurrentKeyAsync();

        Assert.Equal(first, firstKey.Id);
        Assert.Equal(second, secondKey.Id);
        Assert.Equal(32, secondKey.Material.Length);
        Assert.False(firstKey.Material.SequenceEqual(secondKey.Material));
        Assert.Equal(0, _fixture.Vault.UnwrapCalls);
        Assert.Equal(2, _fixture.Vault.SetSecretCalls);
    }

    [Fact]
    public async Task RotateDataKeyAsync_Always_KeepsEarlierKeyResolvable()
    {
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        string first = await provider.RotateDataKeyAsync();
        byte[] firstMaterial = (await provider.GetCurrentKeyAsync()).Material.ToArray();

        await provider.RotateDataKeyAsync();

        CryptographicKey? retired = await provider.GetKeyAsync(first);
        Assert.NotNull(retired);
        Assert.Equal(firstMaterial, retired.Material.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetCurrentKeyAsync_SeveralVersions_ReturnsNewestByCreatedOn(bool newestFirst)
    {
        AzureKeyVaultEncryptionKeyProvider rotator = _fixture.CreateProvider();
        string v1 = await rotator.RotateDataKeyAsync();
        await rotator.RotateDataKeyAsync();
        string v3 = await rotator.RotateDataKeyAsync();
        _fixture.Vault.ListNewestFirst = newestFirst;

        Assert.Equal(v3, (await _fixture.CreateProvider().GetCurrentKeyAsync()).Id);

        _fixture.Vault.GetStoredSecret(v1).CreatedOn = DateTimeOffset.UtcNow.AddYears(5);
        Assert.Equal(v1, (await _fixture.CreateProvider().GetCurrentKeyAsync()).Id);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_NewestVersionDisabled_SkipsIt()
    {
        AzureKeyVaultEncryptionKeyProvider rotator = _fixture.CreateProvider();
        await rotator.RotateDataKeyAsync();
        string v2 = await rotator.RotateDataKeyAsync();
        string v3 = await rotator.RotateDataKeyAsync();

        _fixture.Vault.GetStoredSecret(v3).Enabled = false;

        Assert.Equal(v2, (await _fixture.CreateProvider().GetCurrentKeyAsync()).Id);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_EqualCreatedOn_PicksOrdinallyGreatestVersion()
    {
        AzureKeyVaultEncryptionKeyProvider rotator = _fixture.CreateProvider();
        string[] versions = [await rotator.RotateDataKeyAsync(), await rotator.RotateDataKeyAsync(), await rotator.RotateDataKeyAsync()];
        DateTimeOffset createdOn = _fixture.Vault.GetStoredSecret(versions[0]).CreatedOn;
        foreach (string version in versions)
        {
            _fixture.Vault.GetStoredSecret(version).CreatedOn = createdOn;
        }

        CryptographicKey current = await _fixture.CreateProvider().GetCurrentKeyAsync();

        Assert.Equal(versions.Max(StringComparer.Ordinal), current.Id);
    }

    [Fact]
    public async Task RotateDataKeyAsync_TwoReplicas_BothVersionsResolvableFromThirdInstance()
    {
        AzureKeyVaultEncryptionKeyProvider replicaA = _fixture.CreateProvider();
        AzureKeyVaultEncryptionKeyProvider replicaB = _fixture.CreateProvider();

        string versionA = await replicaA.RotateDataKeyAsync();
        byte[] materialA = (await replicaA.GetCurrentKeyAsync()).Material.ToArray();
        string versionB = await replicaB.RotateDataKeyAsync();
        byte[] materialB = (await replicaB.GetCurrentKeyAsync()).Material.ToArray();

        AzureKeyVaultEncryptionKeyProvider replicaC = _fixture.CreateProvider();
        CryptographicKey? keyA = await replicaC.GetKeyAsync(versionA);
        CryptographicKey? keyB = await replicaC.GetKeyAsync(versionB);

        Assert.NotEqual(versionA, versionB);
        Assert.Equal(2, _fixture.Vault.GetStoredSecrets(EncryptionFixture.DataKeySecretName).Count);
        Assert.NotNull(keyA);
        Assert.NotNull(keyB);
        Assert.Equal(materialA, keyA.Material.ToArray());
        Assert.Equal(materialB, keyB.Material.ToArray());
        Assert.Equal(versionB, (await replicaC.GetCurrentKeyAsync()).Id);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_WithinRefreshInterval_ReadsVaultOnce()
    {
        string version = await _fixture.CreateProvider().RotateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        _fixture.Vault.ResetCounters();

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(version, (await provider.GetCurrentKeyAsync()).Id);
            _fixture.Time.Advance(TimeSpan.FromSeconds(30));
        }

        Assert.Equal(1, _fixture.Vault.ListCalls);
        Assert.Equal(1, _fixture.Vault.GetSecretCalls);
        Assert.Equal(1, _fixture.Vault.UnwrapCalls);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_AfterRefreshInterval_ListsAgainWithoutUnwrappingAgain()
    {
        string version = await _fixture.CreateProvider().RotateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        _fixture.Vault.ResetCounters();
        await provider.GetCurrentKeyAsync();

        _fixture.Time.Advance(EncryptionFixture.RefreshInterval);
        CryptographicKey key = await provider.GetCurrentKeyAsync();

        Assert.Equal(version, key.Id);
        Assert.Equal(2, _fixture.Vault.ListCalls);
        Assert.Equal(1, _fixture.Vault.GetSecretCalls);
        Assert.Equal(1, _fixture.Vault.UnwrapCalls);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_RotationByAnotherInstance_VisibleAfterRefreshInterval()
    {
        string first = await _fixture.CreateProvider().RotateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        Assert.Equal(first, (await provider.GetCurrentKeyAsync()).Id);

        string second = await _fixture.CreateProvider().RotateDataKeyAsync();
        _fixture.Time.Advance(EncryptionFixture.RefreshInterval - TimeSpan.FromSeconds(1));
        Assert.Equal(first, (await provider.GetCurrentKeyAsync()).Id);

        _fixture.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(second, (await provider.GetCurrentKeyAsync()).Id);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef0")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    [InlineData(" 0123456789abcdef0123456789abcde")]
    [InlineData("orders-kek/0123456789abcdef01234")]
    public async Task GetKeyAsync_NotVersionShaped_ReturnsNullWithoutVaultCalls(string keyId)
    {
        await _fixture.CreateProvider().RotateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        _fixture.Vault.ResetCounters();

        CryptographicKey? key = await provider.GetKeyAsync(keyId);

        Assert.Null(key);
        Assert.Equal(0, _fixture.Vault.TotalCalls);
    }

    [Fact]
    public async Task GetKeyAsync_NullKeyId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _fixture.CreateProvider().GetKeyAsync(null!));
    }

    [Fact]
    public async Task GetKeyAsync_UnknownWellFormedVersion_ReturnsNull()
    {
        await _fixture.CreateProvider().RotateDataKeyAsync();

        CryptographicKey? key = await _fixture.CreateProvider().GetKeyAsync(Guid.NewGuid().ToString("N"));

        Assert.Null(key);
    }

    [Fact]
    public async Task GetKeyAsync_UnknownWellFormedVersionWithNoSecret_ReturnsNull()
    {
        CryptographicKey? key = await _fixture.CreateProvider().GetKeyAsync(Guid.NewGuid().ToString("N"));

        Assert.Null(key);
    }

    [Fact]
    public async Task GetKeyAsync_UnknownVersionOnColdCache_ListsExactlyOnce()
    {
        await _fixture.CreateProvider().RotateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        _fixture.Vault.ResetCounters();

        Assert.Null(await provider.GetKeyAsync(Guid.NewGuid().ToString("N")));

        Assert.Equal(1, _fixture.Vault.ListCalls);
        Assert.Equal(0, _fixture.Vault.GetSecretCalls);
    }

    [Fact]
    public async Task GetKeyAsync_ManyUnknownVersionsWhileSnapshotYoungerThanTenSeconds_ForcesNoListing()
    {
        await _fixture.CreateProvider().RotateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        _fixture.Vault.ResetCounters();

        for (int i = 0; i < 50; i++)
        {
            Assert.Null(await provider.GetKeyAsync(Guid.NewGuid().ToString("N")));
            _fixture.Time.Advance(TimeSpan.FromMilliseconds(199));
        }

        Assert.Equal(1, _fixture.Vault.ListCalls);
        Assert.Equal(0, _fixture.Vault.GetSecretCalls);
        Assert.Equal(0, _fixture.Vault.UnwrapCalls);
    }

    [Fact]
    public async Task GetKeyAsync_ManyUnknownVersionsAfterSnapshotAgesTenSeconds_ForcesOneListingPerTenSeconds()
    {
        await _fixture.CreateProvider().RotateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        _fixture.Vault.ResetCounters();
        await provider.GetKeyAsync(Guid.NewGuid().ToString("N"));

        _fixture.Time.Advance(TimeSpan.FromSeconds(10));
        for (int i = 0; i < 20; i++)
        {
            await provider.GetKeyAsync(Guid.NewGuid().ToString("N"));
        }

        Assert.Equal(2, _fixture.Vault.ListCalls);

        _fixture.Time.Advance(TimeSpan.FromSeconds(9));
        await provider.GetKeyAsync(Guid.NewGuid().ToString("N"));
        Assert.Equal(2, _fixture.Vault.ListCalls);

        _fixture.Time.Advance(TimeSpan.FromSeconds(1));
        await provider.GetKeyAsync(Guid.NewGuid().ToString("N"));
        await provider.GetKeyAsync(Guid.NewGuid().ToString("N"));
        Assert.Equal(3, _fixture.Vault.ListCalls);
    }

    [Fact]
    public async Task GetKeyAsync_VersionRotatedElsewhere_FoundOnceSnapshotIsTenSecondsOld()
    {
        await _fixture.CreateProvider().RotateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        await provider.GetCurrentKeyAsync();
        string newVersion = await _fixture.CreateProvider().RotateDataKeyAsync();

        _fixture.Time.Advance(TimeSpan.FromSeconds(9));
        Assert.Null(await provider.GetKeyAsync(newVersion));

        _fixture.Time.Advance(TimeSpan.FromSeconds(1));
        CryptographicKey? key = await provider.GetKeyAsync(newVersion);

        Assert.NotNull(key);
        Assert.Equal(newVersion, key.Id);
    }

    [Fact]
    public async Task Constructor_NullPreviousMasterKeyNames_ProviderWorks()
    {
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider(o => o.PreviousMasterKeyNames = null!);

        string version = await provider.RotateDataKeyAsync();
        CryptographicKey? key = await _fixture.CreateProvider(o => o.PreviousMasterKeyNames = null!).GetKeyAsync(version);

        Assert.NotNull(key);
        Assert.Equal(version, key.Id);
    }

    [Fact]
    public async Task GetKeyAsync_VersionDisabled_ReturnsNullAfterRefresh()
    {
        AzureKeyVaultEncryptionKeyProvider rotator = _fixture.CreateProvider();
        string retired = await rotator.RotateDataKeyAsync();
        await rotator.RotateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        Assert.NotNull(await provider.GetKeyAsync(retired));

        _fixture.Vault.GetStoredSecret(retired).Enabled = false;
        _fixture.Time.Advance(EncryptionFixture.RefreshInterval);

        Assert.Null(await provider.GetKeyAsync(retired));
    }

    [Fact]
    public async Task GetKeyAsync_KnownVersion_ReturnsMemoizedKey()
    {
        string version = await _fixture.CreateProvider().RotateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        _fixture.Vault.ResetCounters();

        CryptographicKey? first = await provider.GetKeyAsync(version);
        _fixture.Time.Advance(TimeSpan.FromHours(2));
        CryptographicKey? second = await provider.GetKeyAsync(version);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Material.ToArray(), second.Material.ToArray());
        Assert.Equal(1, _fixture.Vault.GetSecretCalls);
        Assert.Equal(1, _fixture.Vault.UnwrapCalls);
    }

    [Fact]
    public async Task GetKeyAsync_DataKeyWrappedByPreviousMasterKey_ResolvesOnlyWhenNameIsListed()
    {
        _fixture.Vault.AddRsaKey("old-kek");
        string version = await _fixture.CreateProvider(o => o.MasterKeyName = "old-kek").RotateDataKeyAsync();

        CryptographicKey? withPrevious = await _fixture.CreateProvider(o => o.PreviousMasterKeyNames = ["old-kek"]).GetKeyAsync(version);

        Assert.NotNull(withPrevious);
        Assert.Equal(version, withPrevious.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await _fixture.CreateProvider().GetKeyAsync(version));
    }

    [Fact]
    public async Task AesGcmEncryptionService_OverProvider_RoundTripsAcrossRotation()
    {
        AzureKeyVaultEncryptionKeyProvider writer = _fixture.CreateProvider();
        await writer.RotateDataKeyAsync();
        var writerService = new AesGcmEncryptionService(writer);
        byte[] aad = Encoding.UTF8.GetBytes("orders/42");

        EncryptedPayload oldPayload = await writerService.EncryptAsync(Encoding.UTF8.GetBytes("before rotation"), aad);
        await writer.RotateDataKeyAsync();
        EncryptedPayload newPayload = await writerService.EncryptAsync(Encoding.UTF8.GetBytes("after rotation"), aad);

        var readerService = new AesGcmEncryptionService(_fixture.CreateProvider());
        Result<byte[]> oldPlaintext = await readerService.DecryptAsync(oldPayload, aad);
        Result<byte[]> newPlaintext = await readerService.DecryptAsync(newPayload, aad);

        Assert.NotEqual(oldPayload.KeyId, newPayload.KeyId);
        Assert.True(oldPlaintext.IsSuccess);
        Assert.Equal("before rotation", Encoding.UTF8.GetString(oldPlaintext.Value));
        Assert.True(newPlaintext.IsSuccess);
        Assert.Equal("after rotation", Encoding.UTF8.GetString(newPlaintext.Value));
        Assert.True(await readerService.IsEncryptedWithCurrentKeyAsync(newPayload));
        Assert.False(await readerService.IsEncryptedWithCurrentKeyAsync(oldPayload));
    }

    [Fact]
    public async Task AesGcmEncryptionService_ForgedKeyId_ReturnsUnknownKeyIdWithoutVaultCalls()
    {
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        await provider.RotateDataKeyAsync();
        var service = new AesGcmEncryptionService(provider);
        EncryptedPayload real = await service.EncryptAsync(new byte[] { 1, 2, 3 }, ReadOnlyMemory<byte>.Empty);
        var forged = new EncryptedPayload("forged-key", real.Nonce, real.Ciphertext, real.Tag);
        _fixture.Vault.ResetCounters();

        Result<byte[]> result = await service.DecryptAsync(forged, ReadOnlyMemory<byte>.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.UnknownKeyId, result.Error.Code);
        Assert.Equal(0, _fixture.Vault.TotalCalls);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("bad-base64")]
    [InlineData("disallowed-master-key")]
    [InlineData("malformed-master-version")]
    [InlineData("missing-master-key-id")]
    [InlineData("json-null")]
    [InlineData("empty-object")]
    [InlineData("tampered-wrapped-key")]
    [InlineData("wrong-data-key-length")]
    public async Task GetKeyAsync_CorruptRegistryEntry_ThrowsInvalidOperationException(string corruption)
    {
        _fixture.Vault.AddRsaKey("other-kek");
        RSA master = _fixture.Vault.GetRsa(EncryptionFixture.MasterKeyName, _fixture.MasterKeyVersion);
        string validWrapped = Convert.ToBase64String(master.Encrypt(RandomNumberGenerator.GetBytes(32), RSAEncryptionPadding.OaepSHA256));
        string value = corruption switch
        {
            "not-json" => "this is not json",
            "bad-base64" => Record(_fixture.MasterKeyId, "!!!not base64!!!"),
            "disallowed-master-key" => Record($"other-kek/{_fixture.MasterKeyVersion}", validWrapped),
            "malformed-master-version" => Record($"{EncryptionFixture.MasterKeyName}/V1", validWrapped),
            "missing-master-key-id" => $$"""{"wrappedKey":"{{validWrapped}}"}""",
            "json-null" => "null",
            "empty-object" => "{}",
            "tampered-wrapped-key" => Record(_fixture.MasterKeyId, Convert.ToBase64String(RandomNumberGenerator.GetBytes(256))),
            "wrong-data-key-length" => Record(
                _fixture.MasterKeyId,
                Convert.ToBase64String(master.Encrypt(RandomNumberGenerator.GetBytes(16), RSAEncryptionPadding.OaepSHA256))),
            _ => throw new ArgumentOutOfRangeException(nameof(corruption)),
        };
        string version = _fixture.Vault.AddSecretVersion(EncryptionFixture.DataKeySecretName, value);
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.GetKeyAsync(version));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.GetCurrentKeyAsync());
    }

    [Fact]
    public async Task GetKeyAsync_CorruptRegistryEntry_IsNotCached()
    {
        string version = _fixture.Vault.AddSecretVersion(EncryptionFixture.DataKeySecretName, "corrupt");
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.GetKeyAsync(version));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.GetKeyAsync(version));

        Assert.Equal(2, _fixture.Vault.GetSecretCalls);
    }

    private static string Record(string masterKeyId, string wrappedKey) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["masterKeyId"] = masterKeyId, ["wrappedKey"] = wrappedKey });
}
