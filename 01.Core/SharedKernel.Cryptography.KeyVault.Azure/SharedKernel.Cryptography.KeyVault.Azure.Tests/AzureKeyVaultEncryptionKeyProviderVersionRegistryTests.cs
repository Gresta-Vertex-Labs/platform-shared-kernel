using SharedKernel.Cryptography.KeyVault.Azure.Options;
using SharedKernel.Cryptography.KeyVault.Azure.Tests.TestSupport;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Symmetric;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;
using static SharedKernel.Cryptography.KeyVault.Azure.Tests.TestSupport.AzureKeyVaultCallCountingFakes;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests;

/// <summary>
/// T-73 (P-496/WO-081) coverage: the durable, shared, Key-Vault-Secrets-backed version registry
/// C-93 introduces, proven via call-counting test doubles around the
/// <see cref="Azure.Security.KeyVault.Keys.KeyClient"/>/<see cref="Azure.Security.KeyVault.Secrets.SecretClient"/>/
/// <see cref="Azure.Security.KeyVault.Keys.Cryptography.CryptographyClient"/> surfaces (see
/// <c>TestSupport/AzureKeyVaultCallCountingFakes.cs</c>) — never a live-network assertion. Every
/// test in this file constructs <see cref="AzureKeyVaultEncryptionKeyProvider"/> via its internal
/// test-seam constructor, so the class's real production logic runs unmodified against an
/// in-memory simulated vault.
/// </summary>
public sealed class AzureKeyVaultEncryptionKeyProviderVersionRegistryTests
{
    private static readonly Uri VaultUri = new("https://my-vault.vault.azure.net/");

    private static AzureKeyVaultCryptographyOptions CreateOptions() => new()
    {
        VaultUri = VaultUri,
        CurrentKeyId = "primary",
        KeyNames = new Dictionary<string, string> { ["primary"] = "tenant-data-key" },
        // Every Azure call in this file is intercepted by a fake client, so the credential is
        // never actually used — an explicit FakeTokenCredential avoids constructing a real
        // DefaultAzureCredential (a needless multi-source credential-resolution chain) per test.
        Credential = new FakeTokenCredential(),
    };

    private static AzureKeyVaultEncryptionKeyProvider CreateProvider(SharedVaultState state)
    {
        var factory = new CallCountingCryptographyClientFactory(state);
        return new AzureKeyVaultEncryptionKeyProvider(
            MsOptions.Create(CreateOptions()),
            new CryptoRandomGenerator(),
            new CallCountingFakeKeyClient(state),
            new CallCountingFakeSecretClient(state),
            factory.Create);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_NoVersionEverMinted_Throws()
    {
        var state = new SharedVaultState(VaultUri);
        AzureKeyVaultEncryptionKeyProvider provider = CreateProvider(state);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetCurrentKeyAsync().AsTask());
    }

    [Fact]
    public async Task MintNewVersionAsync_Twice_ProducesSequentialTags()
    {
        var state = new SharedVaultState(VaultUri);
        AzureKeyVaultEncryptionKeyProvider provider = CreateProvider(state);

        string tag1 = await provider.MintNewVersionAsync();
        string tag2 = await provider.MintNewVersionAsync();

        Assert.Equal("v1", tag1);
        Assert.Equal("v2", tag2);
    }

    [Fact]
    public async Task MintNewVersionAsync_ThenGetKeyAsync_DecryptsPreviouslyCurrentNowSupersededVersion()
    {
        // The exact scenario this phase's headline fix targets: a version that WAS current
        // remains fully decryptable after a later mint supersedes it.
        var state = new SharedVaultState(VaultUri);
        AzureKeyVaultEncryptionKeyProvider provider = CreateProvider(state);

        string firstTag = await provider.MintNewVersionAsync();
        CryptographicKey firstKeyAtMintTime = await provider.GetCurrentKeyAsync();
        Assert.Equal(firstTag, firstKeyAtMintTime.Id);

        string secondTag = await provider.MintNewVersionAsync();
        Assert.NotEqual(firstTag, secondTag);

        CryptographicKey? nowSuperseded = await provider.GetKeyAsync(firstTag);

        Assert.NotNull(nowSuperseded);
        Assert.Equal(firstTag, nowSuperseded!.Id);
        Assert.Equal(firstKeyAtMintTime.Material, nowSuperseded.Material);
    }

    [Fact]
    public async Task GetKeyAsync_UnknownButTagShapedVersion_ReturnsNull_ViaNotFoundTranslation()
    {
        var state = new SharedVaultState(VaultUri);
        AzureKeyVaultEncryptionKeyProvider provider = CreateProvider(state);
        await provider.MintNewVersionAsync();

        CryptographicKey? result = await provider.GetKeyAsync("v999");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetKeyAsync_SecondCallForAnAlreadyResolvedTag_MakesZeroAdditionalKeyVaultCalls()
    {
        var state = new SharedVaultState(VaultUri);
        AzureKeyVaultEncryptionKeyProvider minter = CreateProvider(state);
        string tag = await minter.MintNewVersionAsync();

        // A SEPARATE provider instance (a distinct in-process cache) so this test genuinely
        // exercises GetKeyAsync's own Key Vault-backed resolution path, not MintNewVersionAsync's
        // own local memoization shortcut for the tag it just minted.
        AzureKeyVaultEncryptionKeyProvider decryptor = CreateProvider(state);

        CryptographicKey? first = await decryptor.GetKeyAsync(tag);
        Assert.NotNull(first);

        int getSecretCallsAfterFirst = state.GetSecretCallCount;
        int getKeyCallsAfterFirst = state.GetKeyCallCount;
        int unwrapCallsAfterFirst = state.UnwrapCallCount;

        Assert.True(getSecretCallsAfterFirst > 0, "The first resolution must genuinely reach Key Vault Secrets.");

        CryptographicKey? second = await decryptor.GetKeyAsync(tag);

        Assert.NotNull(second);
        Assert.Equal(first!.Material, second!.Material);
        Assert.Equal(getSecretCallsAfterFirst, state.GetSecretCallCount);
        Assert.Equal(getKeyCallsAfterFirst, state.GetKeyCallCount);
        Assert.Equal(unwrapCallsAfterFirst, state.UnwrapCallCount);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_TwoIndependentProviderInstances_ResolveToTheSameVersionTag()
    {
        // Simulates two replicas of the same service sharing one vault: the "current version"
        // pointer must be a genuinely shared concept, not a per-process accident.
        var state = new SharedVaultState(VaultUri);
        AzureKeyVaultEncryptionKeyProvider replicaA = CreateProvider(state);
        AzureKeyVaultEncryptionKeyProvider replicaB = CreateProvider(state);

        await replicaA.MintNewVersionAsync();
        string latestTag = await replicaA.MintNewVersionAsync();

        CryptographicKey fromA = await replicaA.GetCurrentKeyAsync();
        CryptographicKey fromB = await replicaB.GetCurrentKeyAsync();

        Assert.Equal(latestTag, fromA.Id);
        Assert.Equal(latestTag, fromB.Id);
        Assert.Equal(fromA.Id, fromB.Id);
        Assert.Equal(fromA.Material, fromB.Material);
    }

    [Fact]
    public async Task MintNewVersionAsync_RepeatedCalls_ReuseTheCachedCurrentAzureKeyResolution()
    {
        // C-92's connection-reuse claim, proven directly: resolving the "current" Azure master
        // key metadata (and its CryptographyClient) happens at most once across many mints, never
        // once per call.
        var state = new SharedVaultState(VaultUri);
        AzureKeyVaultEncryptionKeyProvider provider = CreateProvider(state);

        await provider.MintNewVersionAsync();
        await provider.MintNewVersionAsync();
        await provider.MintNewVersionAsync();

        Assert.Equal(1, state.GetKeyCallCount);
    }

    [Fact]
    public async Task GetKeyAsync_LegacyPreP496EnvelopeShapedKeyId_StillResolvesCorrectly()
    {
        // Backward-read compatibility: a keyId shaped exactly like the pre-P-496 self-decodable
        // envelope (never the new short "vN" tag shape) must still decrypt indefinitely.
        var state = new SharedVaultState(VaultUri);
        AzureKeyVaultEncryptionKeyProvider provider = CreateProvider(state);

        byte[] plaintext = [11, 22, 33, 44, 55];
        byte[] wrapped = [.. plaintext.Select(b => (byte)~b)];
        string legacyMasterKeyId = $"{VaultUri}keys/tenant-data-key/{SharedVaultState.FixedAzureKeyVersion}";
        string legacyKeyId = EncodeLegacyEnvelope(legacyMasterKeyId, wrapped);

        CryptographicKey? result = await provider.GetKeyAsync(legacyKeyId);

        Assert.NotNull(result);
        Assert.Equal(legacyKeyId, result!.Id);
        Assert.Equal(plaintext, result.Material);
    }

    /// <summary>
    /// Mirrors <see cref="AzureKeyVaultEncryptionKeyProvider"/>'s retired production <c>EncodeKeyId</c>
    /// (the exact pre-P-496 shape its <c>TryDecodeLegacyKeyId</c> fallback still parses) —
    /// duplicated here as a black-box input-construction helper, matching the sibling
    /// <c>AzureKeyVaultEncryptionKeyProviderTests.EncodeForTest</c> convention.
    /// </summary>
    private static string EncodeLegacyEnvelope(string masterKeyId, byte[] wrappedKey)
    {
        byte[] masterKeyIdBytes = System.Text.Encoding.UTF8.GetBytes(masterKeyId);
        byte[] buffer = new byte[4 + masterKeyIdBytes.Length + wrappedKey.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(0, 4), masterKeyIdBytes.Length);
        masterKeyIdBytes.CopyTo(buffer.AsSpan(4));
        wrappedKey.CopyTo(buffer.AsSpan(4 + masterKeyIdBytes.Length));
        return Convert.ToBase64String(buffer);
    }
}
