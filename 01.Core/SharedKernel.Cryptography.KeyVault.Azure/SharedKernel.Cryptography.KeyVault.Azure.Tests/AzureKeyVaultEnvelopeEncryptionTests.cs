using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Security.KeyVault.Keys.Cryptography;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.KeyVault.Azure.Tests.Fakes;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests;

public sealed class AzureKeyVaultEnvelopeEncryptionTests
{
    private readonly EncryptionFixture _fixture = new();

    [Fact]
    public async Task GenerateDataKeyAsync_RsaMasterKey_Returns32ByteKeyWrappedWithRsaOaep256()
    {
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();

        using EnvelopeDataKey dataKey = await provider.GenerateDataKeyAsync();

        Assert.Equal(32, dataKey.PlaintextKey.Length);
        Assert.Equal(_fixture.MasterKeyId, dataKey.MasterKeyId);
        Assert.Equal([KeyWrapAlgorithm.RsaOaep256], _fixture.Vault.WrapAlgorithms);
        byte[] unwrapped = _fixture.Vault
            .GetRsa(EncryptionFixture.MasterKeyName, _fixture.MasterKeyVersion)
            .Decrypt(dataKey.WrappedKey.ToArray(), RSAEncryptionPadding.OaepSHA256);
        Assert.Equal(dataKey.PlaintextKey.ToArray(), unwrapped);
    }

    [Fact]
    public async Task GenerateDataKeyAsync_TwoCalls_ReturnDifferentKeys()
    {
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();

        using EnvelopeDataKey first = await provider.GenerateDataKeyAsync();
        using EnvelopeDataKey second = await provider.GenerateDataKeyAsync();

        Assert.False(first.PlaintextKey.SequenceEqual(second.PlaintextKey));
    }

    [Fact]
    public async Task GenerateDataKeyAsync_WithinRefreshInterval_ReadsMasterKeyOnce()
    {
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();

        for (int i = 0; i < 3; i++)
        {
            using EnvelopeDataKey dataKey = await provider.GenerateDataKeyAsync();
        }

        Assert.Equal(1, _fixture.Vault.GetKeyCalls);
        Assert.Equal(3, _fixture.Vault.WrapCalls);
    }

    [Fact]
    public async Task GenerateDataKeyAsync_MasterKeyRotatedInVault_UsesNewVersionAfterRefresh()
    {
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        using (EnvelopeDataKey before = await provider.GenerateDataKeyAsync())
        {
            Assert.Equal(_fixture.MasterKeyId, before.MasterKeyId);
        }

        string newVersion = _fixture.Vault.AddRsaKey(EncryptionFixture.MasterKeyName);
        _fixture.Time.Advance(EncryptionFixture.RefreshInterval);
        using EnvelopeDataKey after = await provider.GenerateDataKeyAsync();

        Assert.Equal($"{EncryptionFixture.MasterKeyName}/{newVersion}", after.MasterKeyId);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_GeneratedKey_RoundTrips()
    {
        using EnvelopeDataKey dataKey = await _fixture.CreateProvider().GenerateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider reader = _fixture.CreateProvider();

        Result<byte[]> unwrapped = await reader.UnwrapDataKeyAsync(dataKey.WrappedKey.ToArray(), dataKey.MasterKeyId);

        Assert.True(unwrapped.IsSuccess);
        Assert.Equal(dataKey.PlaintextKey.ToArray(), unwrapped.Value);
        Assert.Contains((EncryptionFixture.MasterKeyName, (string?)_fixture.MasterKeyVersion), _fixture.Vault.GetKeyRequests);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_OldMasterKeyVersion_UnwrapsWithThatVersion()
    {
        using EnvelopeDataKey dataKey = await _fixture.CreateProvider().GenerateDataKeyAsync();
        _fixture.Vault.AddRsaKey(EncryptionFixture.MasterKeyName);

        Result<byte[]> unwrapped = await _fixture.CreateProvider().UnwrapDataKeyAsync(dataKey.WrappedKey.ToArray(), dataKey.MasterKeyId);

        Assert.True(unwrapped.IsSuccess);
        Assert.Equal(dataKey.PlaintextKey.ToArray(), unwrapped.Value);
    }

    [Theory]
    [InlineData("other-kek/0123456789abcdef0123456789abcdef")]
    [InlineData("ORDERS-KEK/0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("/0123456789abcdef0123456789abcdef")]
    [InlineData("")]
    [InlineData("https://fake-vault.vault.azure.net/keys/orders-kek/0123456789abcdef0123456789abcdef")]
    public async Task UnwrapDataKeyAsync_MasterKeyNameNotConfigured_FailsWithoutVaultCalls(string masterKeyId)
    {
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();

        Result<byte[]> result = await provider.UnwrapDataKeyAsync(RandomNumberGenerator.GetBytes(256), masterKeyId);

        AssertUnwrapFailed(result);
        Assert.Equal(0, _fixture.Vault.TotalCalls);
    }

    [Theory]
    [InlineData("orders-kek/")]
    [InlineData("orders-kek/v1")]
    [InlineData("orders-kek/0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("orders-kek/0123456789abcdef0123456789abcde")]
    [InlineData("orders-kek/0123456789abcdef0123456789abcdef/extra")]
    public async Task UnwrapDataKeyAsync_MalformedVersion_FailsWithoutVaultCalls(string masterKeyId)
    {
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();

        Result<byte[]> result = await provider.UnwrapDataKeyAsync(RandomNumberGenerator.GetBytes(256), masterKeyId);

        AssertUnwrapFailed(result);
        Assert.Equal(0, _fixture.Vault.TotalCalls);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_EmptyWrappedKey_FailsWithoutVaultCalls()
    {
        Result<byte[]> result = await _fixture.CreateProvider().UnwrapDataKeyAsync(ReadOnlyMemory<byte>.Empty, _fixture.MasterKeyId);

        AssertUnwrapFailed(result);
        Assert.Equal(0, _fixture.Vault.TotalCalls);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_NullMasterKeyId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _fixture.CreateProvider().UnwrapDataKeyAsync(new byte[] { 1 }, null!));
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_PreviousMasterKeyName_IsAccepted()
    {
        _fixture.Vault.AddRsaKey("old-kek");
        using EnvelopeDataKey oldKey = await _fixture.CreateProvider(o => o.MasterKeyName = "old-kek").GenerateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider(o => o.PreviousMasterKeyNames = ["old-kek"]);

        Result<byte[]> unwrapped = await provider.UnwrapDataKeyAsync(oldKey.WrappedKey.ToArray(), oldKey.MasterKeyId);

        Assert.StartsWith("old-kek/", oldKey.MasterKeyId, StringComparison.Ordinal);
        Assert.True(unwrapped.IsSuccess);
        Assert.Equal(oldKey.PlaintextKey.ToArray(), unwrapped.Value);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_RetiredMasterKeyNameNotListed_FailsWithoutVaultCalls()
    {
        _fixture.Vault.AddRsaKey("old-kek");
        using EnvelopeDataKey oldKey = await _fixture.CreateProvider(o => o.MasterKeyName = "old-kek").GenerateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        _fixture.Vault.ResetCounters();

        Result<byte[]> result = await provider.UnwrapDataKeyAsync(oldKey.WrappedKey.ToArray(), oldKey.MasterKeyId);

        AssertUnwrapFailed(result);
        Assert.Equal(0, _fixture.Vault.TotalCalls);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_TamperedWrappedKey_ReturnsFailure()
    {
        using EnvelopeDataKey dataKey = await _fixture.CreateProvider().GenerateDataKeyAsync();
        byte[] tampered = dataKey.WrappedKey.ToArray();
        tampered[10] ^= 0x01;

        Result<byte[]> result = await _fixture.CreateProvider().UnwrapDataKeyAsync(tampered, dataKey.MasterKeyId);

        AssertUnwrapFailed(result);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_KeyVaultForbids_ThrowsRequestFailedException()
    {
        using EnvelopeDataKey dataKey = await _fixture.CreateProvider().GenerateDataKeyAsync();
        _fixture.Vault.UnwrapException = new RequestFailedException(403, "Forbidden");

        RequestFailedException exception = await Assert.ThrowsAsync<RequestFailedException>(
            async () => await _fixture.CreateProvider().UnwrapDataKeyAsync(dataKey.WrappedKey.ToArray(), dataKey.MasterKeyId));

        Assert.Equal(403, exception.Status);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_KeyVaultServerError_ThrowsRequestFailedException()
    {
        using EnvelopeDataKey dataKey = await _fixture.CreateProvider().GenerateDataKeyAsync();
        _fixture.Vault.UnwrapException = new RequestFailedException(500, "Internal error");

        await Assert.ThrowsAsync<RequestFailedException>(
            async () => await _fixture.CreateProvider().UnwrapDataKeyAsync(dataKey.WrappedKey.ToArray(), dataKey.MasterKeyId));
    }

    [Fact]
    public async Task EnvelopeEncryptionService_OverProvider_RoundTrips()
    {
        var writer = new EnvelopeEncryptionService(_fixture.CreateProvider());
        var reader = new EnvelopeEncryptionService(_fixture.CreateProvider());
        byte[] plaintext = Encoding.UTF8.GetBytes("statement for account 42");
        byte[] aad = Encoding.UTF8.GetBytes("tenant-7");

        EnvelopePayload payload = await writer.EncryptAsync(plaintext, aad);
        Assert.True(EnvelopePayload.TryParse(payload.ToString(), out EnvelopePayload? parsed));
        Result<byte[]> decrypted = await reader.DecryptAsync(parsed, aad);

        Assert.True(decrypted.IsSuccess);
        Assert.Equal(plaintext, decrypted.Value);
        Assert.Equal(_fixture.MasterKeyId, payload.MasterKeyId);
    }

    [Fact]
    public async Task EnvelopeEncryptionService_WrongAssociatedData_FailsDecryption()
    {
        var service = new EnvelopeEncryptionService(_fixture.CreateProvider());
        EnvelopePayload payload = await service.EncryptAsync(new byte[] { 1, 2, 3 }, Encoding.UTF8.GetBytes("tenant-7"));

        Result<byte[]> decrypted = await service.DecryptAsync(payload, Encoding.UTF8.GetBytes("tenant-8"));

        Assert.True(decrypted.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, decrypted.Error.Code);
    }

    [Fact]
    public async Task EnvelopeEncryptionService_ForgedMasterKeyId_FailsWithoutVaultCalls()
    {
        var service = new EnvelopeEncryptionService(_fixture.CreateProvider());
        EnvelopePayload payload = await service.EncryptAsync(new byte[] { 1, 2, 3 }, ReadOnlyMemory<byte>.Empty);
        var forged = new EnvelopePayload(
            "attacker-kek/0123456789abcdef0123456789abcdef",
            payload.WrappedKey,
            payload.Nonce,
            payload.Ciphertext,
            payload.Tag);
        _fixture.Vault.ResetCounters();

        Result<byte[]> decrypted = await service.DecryptAsync(forged, ReadOnlyMemory<byte>.Empty);

        AssertUnwrapFailed(decrypted);
        Assert.Equal(0, _fixture.Vault.TotalCalls);
    }

    [Fact]
    public async Task EnvelopeEncryptionService_ForgedVersionOfConfiguredMasterKey_ReturnsFailure()
    {
        var service = new EnvelopeEncryptionService(_fixture.CreateProvider());
        EnvelopePayload payload = await service.EncryptAsync(new byte[] { 1, 2, 3 }, ReadOnlyMemory<byte>.Empty);
        var forged = new EnvelopePayload(
            $"{EncryptionFixture.MasterKeyName}/{Guid.NewGuid():N}",
            payload.WrappedKey,
            payload.Nonce,
            payload.Ciphertext,
            payload.Tag);

        Result<byte[]> decrypted = await service.DecryptAsync(forged, ReadOnlyMemory<byte>.Empty);

        AssertUnwrapFailed(decrypted);
        Assert.Equal(0, _fixture.Vault.UnwrapCalls);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_ManyForgedVersions_BoundsVersionListingAndMakesNoKeyCalls()
    {
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        byte[] wrapped = RandomNumberGenerator.GetBytes(256);

        for (int i = 0; i < 50; i++)
        {
            AssertUnwrapFailed(await provider.UnwrapDataKeyAsync(wrapped, $"{EncryptionFixture.MasterKeyName}/{Guid.NewGuid():N}"));
            _fixture.Time.Advance(TimeSpan.FromMilliseconds(199));
        }

        Assert.Equal(1, _fixture.Vault.ListKeyVersionsCalls);

        _fixture.Time.Advance(TimeSpan.FromSeconds(1));
        for (int i = 0; i < 50; i++)
        {
            AssertUnwrapFailed(await provider.UnwrapDataKeyAsync(wrapped, $"{EncryptionFixture.MasterKeyName}/{Guid.NewGuid():N}"));
        }

        Assert.Equal(2, _fixture.Vault.ListKeyVersionsCalls);
        Assert.Equal(0, _fixture.Vault.CryptographicKeyCalls);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_ForgedVersionsOfTwoAllowedNames_ListEachNameOnce()
    {
        _fixture.Vault.AddRsaKey("old-kek");
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider(o => o.PreviousMasterKeyNames = ["old-kek"]);
        byte[] wrapped = RandomNumberGenerator.GetBytes(256);

        for (int i = 0; i < 10; i++)
        {
            AssertUnwrapFailed(await provider.UnwrapDataKeyAsync(wrapped, $"{EncryptionFixture.MasterKeyName}/{Guid.NewGuid():N}"));
            AssertUnwrapFailed(await provider.UnwrapDataKeyAsync(wrapped, $"old-kek/{Guid.NewGuid():N}"));
        }

        Assert.Equal(2, _fixture.Vault.ListKeyVersionsCalls);
        Assert.Equal(0, _fixture.Vault.CryptographicKeyCalls);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_MasterKeyNameMissingFromVault_ReturnsFailure()
    {
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider(o => o.PreviousMasterKeyNames = ["deleted-kek"]);

        Result<byte[]> result = await provider.UnwrapDataKeyAsync(RandomNumberGenerator.GetBytes(256), $"deleted-kek/{Guid.NewGuid():N}");

        AssertUnwrapFailed(result);
        Assert.Equal(1, _fixture.Vault.ListKeyVersionsCalls);
        Assert.Equal(0, _fixture.Vault.CryptographicKeyCalls);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_DisabledMasterKeyVersion_ReturnsFailureWithoutKeyCalls()
    {
        using EnvelopeDataKey dataKey = await _fixture.CreateProvider().GenerateDataKeyAsync();
        _fixture.Vault.SetKeyVersionEnabled(EncryptionFixture.MasterKeyName, _fixture.MasterKeyVersion, enabled: false);
        _fixture.Vault.ResetCounters();

        Result<byte[]> result = await _fixture.CreateProvider().UnwrapDataKeyAsync(dataKey.WrappedKey.ToArray(), dataKey.MasterKeyId);

        AssertUnwrapFailed(result);
        Assert.Equal(1, _fixture.Vault.ListKeyVersionsCalls);
        Assert.Equal(0, _fixture.Vault.CryptographicKeyCalls);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_MasterKeyVersionDisabledAfterListing_FailsAfterRefreshInterval()
    {
        using EnvelopeDataKey dataKey = await _fixture.CreateProvider().GenerateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        Assert.True((await provider.UnwrapDataKeyAsync(dataKey.WrappedKey.ToArray(), dataKey.MasterKeyId)).IsSuccess);

        _fixture.Vault.SetKeyVersionEnabled(EncryptionFixture.MasterKeyName, _fixture.MasterKeyVersion, enabled: false);
        _fixture.Time.Advance(EncryptionFixture.RefreshInterval);

        AssertUnwrapFailed(await provider.UnwrapDataKeyAsync(dataKey.WrappedKey.ToArray(), dataKey.MasterKeyId));
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_MasterKeyVersionCreatedAfterListing_UsableAfterTenSecondFloor()
    {
        using EnvelopeDataKey oldKey = await _fixture.CreateProvider().GenerateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider reader = _fixture.CreateProvider();
        Assert.True((await reader.UnwrapDataKeyAsync(oldKey.WrappedKey.ToArray(), oldKey.MasterKeyId)).IsSuccess);

        string newVersion = _fixture.Vault.AddRsaKey(EncryptionFixture.MasterKeyName);
        using EnvelopeDataKey newKey = await _fixture.CreateProvider().GenerateDataKeyAsync();
        Assert.Equal($"{EncryptionFixture.MasterKeyName}/{newVersion}", newKey.MasterKeyId);
        _fixture.Vault.ResetCounters();

        _fixture.Time.Advance(TimeSpan.FromSeconds(9));
        AssertUnwrapFailed(await reader.UnwrapDataKeyAsync(newKey.WrappedKey.ToArray(), newKey.MasterKeyId));
        Assert.Equal(0, _fixture.Vault.ListKeyVersionsCalls);
        Assert.Equal(0, _fixture.Vault.CryptographicKeyCalls);

        _fixture.Time.Advance(TimeSpan.FromSeconds(1));
        Result<byte[]> unwrapped = await reader.UnwrapDataKeyAsync(newKey.WrappedKey.ToArray(), newKey.MasterKeyId);

        Assert.True(unwrapped.IsSuccess);
        Assert.Equal(newKey.PlaintextKey.ToArray(), unwrapped.Value);
        Assert.Equal(1, _fixture.Vault.ListKeyVersionsCalls);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_MasterKeyVersionDeletedAfterListing_ReturnsFailure()
    {
        using EnvelopeDataKey firstKey = await _fixture.CreateProvider().GenerateDataKeyAsync();
        _fixture.Vault.AddRsaKey(EncryptionFixture.MasterKeyName);
        using EnvelopeDataKey secondKey = await _fixture.CreateProvider().GenerateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider reader = _fixture.CreateProvider();
        Assert.True((await reader.UnwrapDataKeyAsync(secondKey.WrappedKey.ToArray(), secondKey.MasterKeyId)).IsSuccess);

        _fixture.Vault.DeleteKeyVersion(EncryptionFixture.MasterKeyName, _fixture.MasterKeyVersion);
        _fixture.Vault.ResetCounters();
        Result<byte[]> result = await reader.UnwrapDataKeyAsync(firstKey.WrappedKey.ToArray(), firstKey.MasterKeyId);

        AssertUnwrapFailed(result);
        Assert.Equal(1, _fixture.Vault.GetKeyCalls);
        Assert.Equal(0, _fixture.Vault.UnwrapCalls);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_MasterKeyVersionDeletedAfterKeyWasCached_ReturnsFailure()
    {
        using EnvelopeDataKey dataKey = await _fixture.CreateProvider().GenerateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider reader = _fixture.CreateProvider();
        Assert.True((await reader.UnwrapDataKeyAsync(dataKey.WrappedKey.ToArray(), dataKey.MasterKeyId)).IsSuccess);

        _fixture.Vault.DeleteKeyVersion(EncryptionFixture.MasterKeyName, _fixture.MasterKeyVersion);
        _fixture.Vault.ResetCounters();
        Result<byte[]> result = await reader.UnwrapDataKeyAsync(dataKey.WrappedKey.ToArray(), dataKey.MasterKeyId);

        AssertUnwrapFailed(result);
        Assert.Equal(1, _fixture.Vault.UnwrapCalls);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_WithinRefreshInterval_ListsMasterKeyVersionsOnce()
    {
        AzureKeyVaultEncryptionKeyProvider writer = _fixture.CreateProvider();
        AzureKeyVaultEncryptionKeyProvider reader = _fixture.CreateProvider();

        for (int i = 0; i < 5; i++)
        {
            using EnvelopeDataKey dataKey = await writer.GenerateDataKeyAsync();
            Assert.True((await reader.UnwrapDataKeyAsync(dataKey.WrappedKey.ToArray(), dataKey.MasterKeyId)).IsSuccess);
        }

        Assert.Equal(1, _fixture.Vault.ListKeyVersionsCalls);
        Assert.Equal(5, _fixture.Vault.UnwrapCalls);

        _fixture.Time.Advance(EncryptionFixture.RefreshInterval);
        using EnvelopeDataKey later = await writer.GenerateDataKeyAsync();
        Assert.True((await reader.UnwrapDataKeyAsync(later.WrappedKey.ToArray(), later.MasterKeyId)).IsSuccess);
        Assert.Equal(2, _fixture.Vault.ListKeyVersionsCalls);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_NullPreviousMasterKeyNames_UnwrapsWithCurrentMasterKey()
    {
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider(o => o.PreviousMasterKeyNames = null!);

        using EnvelopeDataKey dataKey = await provider.GenerateDataKeyAsync();
        Result<byte[]> unwrapped = await provider.UnwrapDataKeyAsync(dataKey.WrappedKey.ToArray(), dataKey.MasterKeyId);

        Assert.True(unwrapped.IsSuccess);
        AssertUnwrapFailed(await provider.UnwrapDataKeyAsync(dataKey.WrappedKey.ToArray(), $"old-kek/{_fixture.MasterKeyVersion}"));
    }

    [Theory]
    [InlineData("P-256")]
    [InlineData("P-384")]
    public async Task GenerateDataKeyAsync_EcMasterKey_ThrowsNotSupportedException(string curve)
    {
        var vault = new FakeKeyVault();
        vault.AddEcKey("ec-kek", curve == "P-256" ? ECCurve.NamedCurves.nistP256 : ECCurve.NamedCurves.nistP384);
        var provider = new AzureKeyVaultEncryptionKeyProvider(
            Microsoft.Extensions.Options.Options.Create(EncryptionFixture.CreateOptions(o => o.MasterKeyName = "ec-kek")),
            vault.KeyClient,
            vault.SecretClient,
            new SharedKernel.Cryptography.Random.SecureRandomGenerator(),
            _fixture.Time);

        await Assert.ThrowsAsync<NotSupportedException>(async () => await provider.GenerateDataKeyAsync());
        await Assert.ThrowsAsync<NotSupportedException>(async () => await provider.RotateDataKeyAsync());
        Assert.Equal(0, vault.WrapCalls);
        Assert.Equal(0, vault.SetSecretCalls);
    }

    [Fact]
    public async Task GenerateDataKeyAsync_OctMasterKey_WrapsWithA256KW()
    {
        _fixture.Vault.AddOctKey("hsm-kek");
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider(o => o.MasterKeyName = "hsm-kek");

        using EnvelopeDataKey dataKey = await provider.GenerateDataKeyAsync();
        Result<byte[]> unwrapped = await provider.UnwrapDataKeyAsync(dataKey.WrappedKey.ToArray(), dataKey.MasterKeyId);

        Assert.Equal([KeyWrapAlgorithm.A256KW], _fixture.Vault.WrapAlgorithms);
        Assert.True(unwrapped.IsSuccess);
        Assert.Equal(dataKey.PlaintextKey.ToArray(), unwrapped.Value);
    }

    [Fact]
    public async Task RotateDataKeyAsync_OctMasterKey_ProducesUsableCurrentKey()
    {
        _fixture.Vault.AddOctKey("hsm-kek");
        string version = await _fixture.CreateProvider(o => o.MasterKeyName = "hsm-kek").RotateDataKeyAsync();

        var reader = _fixture.CreateProvider(o => o.MasterKeyName = "hsm-kek");

        Assert.Equal(version, (await reader.GetCurrentKeyAsync()).Id);
    }

    private static void AssertUnwrapFailed<T>(Result<T> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DataKeyUnwrapFailed, result.Error.Code);
    }
}
