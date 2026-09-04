using SharedKernel.Cryptography.KeyVault.Azure.Options;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Results;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests;

/// <summary>
/// Local-only coverage for <see cref="AzureKeyVaultEncryptionKeyProvider"/> — every case here
/// exercises a code path that returns before any Azure SDK call is made (purely local
/// validation), so it needs no reachable vault or credentials. Genuine network-round-trip
/// behavior (an unreachable vault surfacing as a thrown exception, and the full
/// generate/wrap/unwrap round trip against a real Azure Key Vault) is covered separately in
/// <c>AzureKeyVaultEncryptionKeyProviderIntegrationTests</c> — see that file's own docs for what
/// is and is not genuinely exercised in this environment.
/// </summary>
public sealed class AzureKeyVaultEncryptionKeyProviderTests
{
    private static AzureKeyVaultEncryptionKeyProvider CreateProvider() =>
        new(
            MsOptions.Create(new AzureKeyVaultCryptographyOptions
            {
                VaultUri = new Uri("https://my-vault.vault.azure.net/"),
                CurrentKeyId = "primary",
                KeyNames = new Dictionary<string, string> { ["primary"] = "tenant-data-key" },
            }),
            new CryptoRandomGenerator());

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = new AzureKeyVaultEncryptionKeyProvider(null!, new CryptoRandomGenerator());
        });
    }

    [Fact]
    public void Constructor_NullSecureRandomGenerator_Throws()
    {
        var options = MsOptions.Create(new AzureKeyVaultCryptographyOptions
        {
            VaultUri = new Uri("https://my-vault.vault.azure.net/"),
            CurrentKeyId = "primary",
            KeyNames = new Dictionary<string, string> { ["primary"] = "tenant-data-key" },
        });

        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = new AzureKeyVaultEncryptionKeyProvider(options, null!);
        });
    }

    [Fact]
    public void Constructor_NoExplicitCredential_DefaultsToDefaultAzureCredential_WithoutThrowing()
    {
        // DefaultAzureCredential construction performs no network I/O by itself.
        var exception = Record.Exception(CreateProvider);

        Assert.Null(exception);
    }

    [Fact]
    public void ImplementsIEncryptionKeyProviderProbe()
    {
        AzureKeyVaultEncryptionKeyProvider provider = CreateProvider();

        Assert.IsAssignableFrom<IEncryptionKeyProviderProbe>(provider);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_NullWrappedDataKey_Throws()
    {
        AzureKeyVaultEncryptionKeyProvider provider = CreateProvider();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.UnwrapDataKeyAsync(null!, "https://my-vault.vault.azure.net/keys/tenant-data-key/abc123").AsTask());
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_NullMasterKeyId_Throws()
    {
        AzureKeyVaultEncryptionKeyProvider provider = CreateProvider();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.UnwrapDataKeyAsync([1, 2, 3], null!).AsTask());
    }

    [Theory]
    [InlineData("not-a-uri-at-all")]
    [InlineData("https://my-vault.vault.azure.net/secrets/tenant-data-key/abc123")] // wrong path ("secrets", not "keys")
    [InlineData("https://my-vault.vault.azure.net/keys/tenant-data-key")] // missing version segment
    [InlineData("https://my-vault.vault.azure.net/keys//abc123")] // empty name segment
    [InlineData("https://my-vault.vault.azure.net/keys/tenant-data-key/")] // empty version segment
    public async Task UnwrapDataKeyAsync_MalformedMasterKeyId_ReturnsFailure_WithoutCallingAzure(string masterKeyId)
    {
        // A malformed masterKeyId is rejected purely locally, before any Azure SDK call — proven
        // by this completing instantly against a VaultUri that is never actually contacted.
        AzureKeyVaultEncryptionKeyProvider provider = CreateProvider();

        Result<byte[]> result = await provider.UnwrapDataKeyAsync([1, 2, 3, 4], masterKeyId);

        Assert.True(result.IsFailure);
        Assert.Equal(AzureKeyVaultCryptographyErrorCodes.MalformedMasterKeyId, result.Error.Code);
    }

    [Fact]
    public async Task GetKeyAsync_NotBase64_ReturnsNull_WithoutCallingAzure()
    {
        AzureKeyVaultEncryptionKeyProvider provider = CreateProvider();

        var result = await provider.GetKeyAsync("this is not valid base64!!");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetKeyAsync_TruncatedBuffer_ReturnsNull_WithoutCallingAzure()
    {
        // Fewer than the 4-byte length prefix this provider always encodes.
        AzureKeyVaultEncryptionKeyProvider provider = CreateProvider();

        var result = await provider.GetKeyAsync(Convert.ToBase64String([1, 2]));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetKeyAsync_DecodesToMalformedMasterKeyId_ReturnsNull_WithoutCallingAzure()
    {
        // A well-formed envelope (correct length prefix) whose embedded masterKeyId is not a
        // valid Azure Key Vault key identifier URI — GetKeyAsync must translate
        // UnwrapDataKeyAsync's local-validation Result failure into null, per
        // IEncryptionKeyProvider.GetKeyAsync's "retired or unknown" contract, never throw.
        AzureKeyVaultEncryptionKeyProvider provider = CreateProvider();
        string keyId = EncodeForTest("not-a-uri", [9, 9, 9]);

        var result = await provider.GetKeyAsync(keyId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetKeyAsync_NullKeyId_Throws()
    {
        AzureKeyVaultEncryptionKeyProvider provider = CreateProvider();

        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GetKeyAsync(null!).AsTask());
    }

    /// <summary>
    /// Mirrors <see cref="AzureKeyVaultEncryptionKeyProvider"/>'s private <c>EncodeKeyId</c> —
    /// duplicated here (rather than exposed via <c>InternalsVisibleTo</c>) since it is exercised
    /// purely as a black-box input-construction helper for this test, not as a unit under test in
    /// its own right (the round trip through <see cref="AzureKeyVaultEncryptionKeyProvider.GetCurrentKeyAsync"/>
    /// would be the real unit-under-test path, but that requires a reachable vault — see the
    /// integration test file).
    /// </summary>
    private static string EncodeForTest(string masterKeyId, byte[] wrappedKey)
    {
        byte[] masterKeyIdBytes = System.Text.Encoding.UTF8.GetBytes(masterKeyId);
        byte[] buffer = new byte[4 + masterKeyIdBytes.Length + wrappedKey.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(0, 4), masterKeyIdBytes.Length);
        masterKeyIdBytes.CopyTo(buffer.AsSpan(4));
        wrappedKey.CopyTo(buffer.AsSpan(4 + masterKeyIdBytes.Length));
        return Convert.ToBase64String(buffer);
    }
}
