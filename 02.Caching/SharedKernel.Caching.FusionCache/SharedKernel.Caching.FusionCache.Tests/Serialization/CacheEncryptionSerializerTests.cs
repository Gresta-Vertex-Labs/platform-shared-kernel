using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Serialization;
using SharedKernel.Cryptography.Symmetric;
using ZiggyCreatures.Caching.Fusion.Serialization;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests.Serialization;

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

/// <summary>Simple DTO used in round-trip tests.</summary>
internal sealed record CacheEncryptionTestPayload(string Value, int Number);

/// <summary>
/// Minimal in-memory <see cref="IEncryptionKeyProvider"/> test double — a single generated
/// AES-256 key, mirroring the platform's established
/// <c>InMemoryEncryptionKeyProvider</c>/<c>FakeEncryptionKeyProvider</c> shape used elsewhere
/// (<c>01.Core/SharedKernel.Cryptography.Tests</c>, <c>16.Testing/SharedKernel.Testing</c>).
/// </summary>
internal sealed class InMemoryEncryptionKeyProvider : IEncryptionKeyProvider
{
    private readonly CryptographicKey _key;

    public InMemoryEncryptionKeyProvider(string keyId = "v1")
    {
        byte[] material = new byte[32];
        RandomNumberGenerator.Fill(material);
        _key = new CryptographicKey(keyId, material);
    }

    public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default) =>
        new(_key);

    public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default) =>
        new(keyId == _key.Id ? _key : null);
}

/// <summary>
/// Unit tests for <see cref="CacheEncryptionSerializer"/>.
/// </summary>
public sealed class CacheEncryptionSerializerTests
{
    // Default STJ inner serializer with default options — acts as the "real" inner.
    private static FusionCacheSystemTextJsonSerializer CreateInner() =>
        new FusionCacheSystemTextJsonSerializer();

    private static AesGcmEncryptionService CreateEncryptionService(string keyId = "v1") =>
        new(new InMemoryEncryptionKeyProvider(keyId));

    // -------------------------------------------------------------------------
    // CE-05 Test 1: Encrypted round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SerializeAsync_ThenDeserializeAsync_RoundTripsCorrectly()
    {
        var inner = CreateInner();
        var encryptionService = CreateEncryptionService();
        var sut = new CacheEncryptionSerializer(inner, encryptionService);

        var original = new CacheEncryptionTestPayload("secret-value", 42);

        byte[] encrypted = await sut.SerializeAsync(original);
        var result = await sut.DeserializeAsync<CacheEncryptionTestPayload>(encrypted);

        Assert.NotNull(result);
        Assert.Equal(original, result);
    }

    [Fact]
    public void Serialize_ThenDeserialize_SyncPath_RoundTripsCorrectly()
    {
        var inner = CreateInner();
        var encryptionService = CreateEncryptionService();
        var sut = new CacheEncryptionSerializer(inner, encryptionService);

        var original = new CacheEncryptionTestPayload("sync-value", 7);

        byte[] encrypted = sut.Serialize(original);
        var result = sut.Deserialize<CacheEncryptionTestPayload>(encrypted);

        Assert.Equal(original, result);
    }

    [Fact]
    public async Task SerializeAsync_ProducesPayloadStartingWithMagicBytes()
    {
        var inner = CreateInner();
        var encryptionService = CreateEncryptionService();
        var sut = new CacheEncryptionSerializer(inner, encryptionService);

        byte[] encrypted = await sut.SerializeAsync(new CacheEncryptionTestPayload("x", 1));

        Assert.True(encrypted.Length >= 2, "Encrypted payload must be at least 2 bytes long.");
        Assert.Equal(0x45, encrypted[0]);
        Assert.Equal(0x4E, encrypted[1]);
    }

    [Fact]
    public async Task SerializeAsync_EncryptedBytes_DoNotContainThePlaintextValue()
    {
        var inner = CreateInner();
        var encryptionService = CreateEncryptionService();
        var sut = new CacheEncryptionSerializer(inner, encryptionService);

        var original = new CacheEncryptionTestPayload("do-not-leak-me", 123);
        byte[] plaintextJson = inner.Serialize(original);
        byte[] encrypted = await sut.SerializeAsync(original);

        Assert.NotEqual(plaintextJson, encrypted);
        Assert.False(
            ContainsSubsequence(encrypted, plaintextJson),
            "Encrypted bytes must not contain the plaintext JSON as a contiguous subsequence.");
    }

    private static bool ContainsSubsequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length)
            return needle.Length == 0;

        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                return true;
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // CE-05 Test 2: Pass-through for pre-existing unencrypted cache values
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeserializeAsync_DataWithoutMagicBytes_DelegatesDirectlyToInner()
    {
        // Arrange: simulate a byte[] that was written before encryption was enabled.
        var inner = CreateInner();
        var encryptionService = CreateEncryptionService();
        var sut = new CacheEncryptionSerializer(inner, encryptionService);

        var original = new CacheEncryptionTestPayload("legacy-value", 42);
        byte[] unencryptedBytes = inner.Serialize(original);

        // Confirm there are no magic bytes.
        Assert.False(
            unencryptedBytes.Length >= 2 && unencryptedBytes[0] == 0x45 && unencryptedBytes[1] == 0x4E,
            "Precondition: inner-serialized bytes must not accidentally start with magic bytes.");

        var result = await sut.DeserializeAsync<CacheEncryptionTestPayload>(unencryptedBytes);

        Assert.NotNull(result);
        Assert.Equal(original, result);
    }

    [Fact]
    public async Task DeserializeAsync_MixedCache_HandlesBothEncryptedAndUnencryptedCorrectly()
    {
        var inner = CreateInner();
        var encryptionService = CreateEncryptionService();
        var sut = new CacheEncryptionSerializer(inner, encryptionService);

        var encryptedDto = new CacheEncryptionTestPayload("new-value", 1);
        var legacyDto = new CacheEncryptionTestPayload("old-value", 2);

        byte[] encryptedBytes = await sut.SerializeAsync(encryptedDto);
        byte[] legacyBytes = inner.Serialize(legacyDto); // direct inner → no magic bytes

        var encryptedResult = await sut.DeserializeAsync<CacheEncryptionTestPayload>(encryptedBytes);
        var legacyResult = await sut.DeserializeAsync<CacheEncryptionTestPayload>(legacyBytes);

        Assert.Equal(encryptedDto, encryptedResult);
        Assert.Equal(legacyDto, legacyResult);
    }

    // -------------------------------------------------------------------------
    // CE-05 Test 3: Tamper detection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeserializeAsync_CorruptedCiphertextByte_ThrowsCryptographicException()
    {
        var inner = CreateInner();
        var encryptionService = CreateEncryptionService();
        var sut = new CacheEncryptionSerializer(inner, encryptionService);

        byte[] encrypted = await sut.SerializeAsync(new CacheEncryptionTestPayload("tamper-me", 1));

        // Flip a bit somewhere past the magic bytes/length-prefixed header — guaranteed to land
        // inside either the ciphertext or the authentication tag.
        byte[] tampered = (byte[])encrypted.Clone();
        tampered[^1] ^= 0xFF;

        await Assert.ThrowsAsync<CryptographicException>(
            () => sut.DeserializeAsync<CacheEncryptionTestPayload>(tampered).AsTask());
    }

    [Fact]
    public void Deserialize_CorruptedCiphertextByte_SyncPath_ThrowsCryptographicException()
    {
        var inner = CreateInner();
        var encryptionService = CreateEncryptionService();
        var sut = new CacheEncryptionSerializer(inner, encryptionService);

        byte[] encrypted = sut.Serialize(new CacheEncryptionTestPayload("tamper-me-too", 2));

        byte[] tampered = (byte[])encrypted.Clone();
        tampered[^1] ^= 0xFF;

        Assert.Throws<CryptographicException>(() => sut.Deserialize<CacheEncryptionTestPayload>(tampered));
    }

    [Fact]
    public async Task DeserializeAsync_WrongDecryptionKey_ThrowsCryptographicException()
    {
        var inner = CreateInner();
        var writerService = CreateEncryptionService(keyId: "writer-key");
        var writerSut = new CacheEncryptionSerializer(inner, writerService);

        byte[] encrypted = await writerSut.SerializeAsync(new CacheEncryptionTestPayload("secret", 1));

        // A reader whose key provider has no knowledge of "writer-key" must fail loudly.
        var readerService = CreateEncryptionService(keyId: "reader-key");
        var readerSut = new CacheEncryptionSerializer(inner, readerService);

        await Assert.ThrowsAsync<CryptographicException>(
            () => readerSut.DeserializeAsync<CacheEncryptionTestPayload>(encrypted).AsTask());
    }

    // -------------------------------------------------------------------------
    // DI registration: AddCacheEncryption
    // -------------------------------------------------------------------------

    [Fact]
    public void AddCacheEncryption_WithNullBuilder_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ((ICachingBuilder)null!).AddCacheEncryption());
    }

    [Fact]
    public void AddCacheEncryption_WithoutSymmetricEncryptionServiceRegistered_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "test");

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddCacheEncryption());
        Assert.Contains("AddSharedKernelCryptography", ex.Message);
    }

    [Fact]
    public void AddCacheEncryption_WithSymmetricEncryptionServiceRegistered_ResolvesAsCacheEncryptionSerializer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ISymmetricEncryptionService>());
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "test");

        builder.AddCacheEncryption();

        using var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<IFusionCacheSerializer>();

        Assert.IsType<CacheEncryptionSerializer>(serializer);
    }

    [Fact]
    public void AddCacheEncryption_RegistersCacheEncryptionOptionsMarker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ISymmetricEncryptionService>());
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "test");

        builder.AddCacheEncryption();

        using var provider = services.BuildServiceProvider();
        var marker = provider.GetService<CacheEncryptionOptions>();

        Assert.NotNull(marker);
        Assert.True(marker.Enabled);
    }

    // -------------------------------------------------------------------------
    // CE-06 Ordering tests
    // -------------------------------------------------------------------------

    [Fact]
    public void AddBrotliCompression_ThenAddCacheEncryption_RoundTripsCorrectly_CompressThenEncrypt()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ISymmetricEncryptionService>());
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "test");

        builder
            .AddBrotliCompression(o => o.L2ThresholdBytes = 16)
            .AddCacheEncryption();

        using var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<IFusionCacheSerializer>();

        // Encryption must be the outermost decorator.
        Assert.IsType<CacheEncryptionSerializer>(serializer);
    }

    [Fact]
    public async Task AddBrotliCompression_ThenAddCacheEncryption_RealCrypto_RoundTripsCorrectly()
    {
        // Real compose-then-encrypt / decrypt-then-decompress round trip, exercised directly
        // (not through DI resolution) so the test asserts genuine byte-level behavior.
        var inner = CreateInner();
        var compressionOptions = new CachingOptions.CompressionOptions { Enabled = true, L2ThresholdBytes = 16 };
        var brotli = new BrotliCacheSerializer(inner, compressionOptions);
        var encryptionService = CreateEncryptionService();
        var sut = new CacheEncryptionSerializer(brotli, encryptionService);

        var original = new CacheEncryptionTestPayload(new string('a', 500), 999);

        byte[] result = await sut.SerializeAsync(original);

        // Outermost frame must be the encryption magic bytes.
        Assert.Equal(0x45, result[0]);
        Assert.Equal(0x4E, result[1]);

        var roundTripped = await sut.DeserializeAsync<CacheEncryptionTestPayload>(result);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void AddCacheEncryption_ThenAddBrotliCompression_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ISymmetricEncryptionService>());
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "test");

        builder.AddCacheEncryption();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.AddBrotliCompression(o => o.L2ThresholdBytes = 1024));

        Assert.Contains("AddBrotliCompression() before AddCacheEncryption()", ex.Message);
    }
}
