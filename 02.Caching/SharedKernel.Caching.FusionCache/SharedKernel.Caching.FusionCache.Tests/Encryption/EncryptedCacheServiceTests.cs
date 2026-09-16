using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Encryption;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Implementations;
using SharedKernel.Caching.FusionCache.Serialization;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Results;
using ZiggyCreatures.Caching.Fusion.Serialization;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests.Encryption;

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

/// <summary>Simple DTO used in round-trip tests.</summary>
internal sealed record EncryptedCacheServiceTestPayload(string Value, int Number);

/// <summary>
/// An <see cref="IEncryptionKeyProvider"/> that only completes asynchronously, as a KMS-backed
/// provider does — a single generated AES-256 key that yields before returning it. Proves
/// <see cref="EncryptedCacheService"/> needs nothing beyond the asynchronous key-provider contract.
/// </summary>
internal sealed class AsyncOnlyEncryptionKeyProvider : IEncryptionKeyProvider
{
    private readonly CryptographicKey _key;

    public AsyncOnlyEncryptionKeyProvider(string keyId = "v1") =>
        _key = new CryptographicKey(keyId, RandomNumberGenerator.GetBytes(32));

    public async ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        return _key;
    }

    public async ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        return keyId == _key.Id ? _key : null;
    }
}

/// <summary>
/// A minimal, dictionary-backed <see cref="ICacheService"/> double giving tests direct control over
/// (and visibility into) exactly what <see cref="EncryptedCacheService"/> stores/reads at the inner
/// layer — e.g. moving a raw stored entry (an <see cref="EncryptedPayload"/> in its storage format)
/// from one key to another to simulate a replay (AA-07), or inspecting stored length (AA-09).
/// </summary>
internal sealed class InMemoryDictionaryCacheService : ICacheService
{
    private readonly Dictionary<string, object?> _store = new();

    public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
        new(_store.TryGetValue(key, out var value) ? (T?)value : default);

    public ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
    {
        _store[key] = value;
        return ValueTask.CompletedTask;
    }

    public async ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        if (_store.TryGetValue(key, out var existing))
            return (T)existing!;

        T value = await factory(ct).ConfigureAwait(false);
        _store[key] = value;
        return value;
    }

    public ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        _store.Remove(key);
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default) => ValueTask.CompletedTask;

    public ValueTask<IReadOnlyDictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken ct = default)
    {
        var result = new Dictionary<string, T?>();
        foreach (var key in keys)
            result[key] = _store.TryGetValue(key, out var value) ? (T?)value : default;

        return new ValueTask<IReadOnlyDictionary<string, T?>>(result);
    }

    public ValueTask SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct = default)
    {
        foreach (var (key, value) in entries)
            _store[key] = value;

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A bespoke <see cref="ICacheService"/> double that captures the <c>byte[]</c>-typed
/// factory <see cref="EncryptedCacheService.GetOrSetAsync{T}"/> passes down to
/// <see cref="GetOrSetAsync{T}"/>, so AA-08's eager-refresh proof can re-invoke that exact closure
/// from an unrelated execution context afterwards. Every other member is unused by that test and
/// throws if called.
/// </summary>
internal sealed class FactoryCapturingCacheService : ICacheService
{
    public Func<CancellationToken, ValueTask<byte[]>>? CapturedFactory { get; private set; }

    public ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        if (typeof(T) == typeof(byte[]))
            CapturedFactory = (Func<CancellationToken, ValueTask<byte[]>>)(object)factory;

        return factory(ct);
    }

    public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by this test.");

    public ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by this test.");

    public ValueTask RemoveAsync(string key, CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by this test.");

    public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by this test.");

    public ValueTask<IReadOnlyDictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by this test.");

    public ValueTask SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by this test.");
}

/// <summary>
/// Unit tests for <see cref="EncryptedCacheService"/> (Phase 46/WO-081) — key-bound AAD, asynchronous
/// crypto, compression composition, tamper/decrypt-failure handling, and
/// <c>AddCacheEncryption()</c> DI wiring.
/// </summary>
public sealed class EncryptedCacheServiceTests
{
    private static ISymmetricEncryptionService CreateRealEncryptionService(string keyId = "v1") =>
        new AesGcmEncryptionService(new AsyncOnlyEncryptionKeyProvider(keyId));

    private static EncryptedCacheService CreateSut(
        ICacheService inner,
        ISymmetricEncryptionService encryption,
        bool compressionEnabled = false) =>
        new(inner, encryption, new JsonSerializerOptions(), compressionEnabled, NullLogger<EncryptedCacheService>.Instance);

    // -------------------------------------------------------------------------
    // Round trip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTripsCorrectly()
    {
        var sut = CreateSut(new InMemoryDictionaryCacheService(), CreateRealEncryptionService());
        var original = new EncryptedCacheServiceTestPayload("secret-value", 42);

        await sut.SetAsync("key-1", original, CachePolicy.Default);
        var result = await sut.GetAsync<EncryptedCacheServiceTestPayload>("key-1");

        Assert.Equal(original, result);
    }

    [Fact]
    public async Task GetAsync_UnknownKey_ReturnsDefault()
    {
        var sut = CreateSut(new InMemoryDictionaryCacheService(), CreateRealEncryptionService());

        var result = await sut.GetAsync<EncryptedCacheServiceTestPayload>("unknown-key");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOrSetAsync_CacheMiss_InvokesFactoryAndCaches()
    {
        var sut = CreateSut(new InMemoryDictionaryCacheService(), CreateRealEncryptionService());
        var calls = 0;

        var result = await sut.GetOrSetAsync<EncryptedCacheServiceTestPayload>(
            "gos-1",
            async _ =>
            {
                calls++;
                await Task.Yield();
                return new EncryptedCacheServiceTestPayload("computed", 7);
            },
            CachePolicy.Default);

        Assert.Equal("computed", result.Value);
        Assert.Equal(1, calls);

        var second = await sut.GetOrSetAsync<EncryptedCacheServiceTestPayload>(
            "gos-1",
            async _ =>
            {
                calls++;
                await Task.Yield();
                return new EncryptedCacheServiceTestPayload("computed", 7);
            },
            CachePolicy.Default);

        Assert.Equal("computed", second.Value);
        Assert.Equal(1, calls); // second call is a cache hit — factory not invoked again.
    }

    [Fact]
    public async Task RemoveAsync_RemovesEntry()
    {
        var sut = CreateSut(new InMemoryDictionaryCacheService(), CreateRealEncryptionService());
        await sut.SetAsync("rm-1", new EncryptedCacheServiceTestPayload("v", 1), CachePolicy.Default);

        await sut.RemoveAsync("rm-1");

        Assert.Null(await sut.GetAsync<EncryptedCacheServiceTestPayload>("rm-1"));
    }

    // -------------------------------------------------------------------------
    // AA-07 — headline test: cross-key replay fails authentication
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_PayloadReplayedUnderDifferentKey_FailsAuthentication_TreatedAsCacheMiss()
    {
        var inner = new InMemoryDictionaryCacheService();
        var sut = CreateSut(inner, CreateRealEncryptionService());

        await sut.SetAsync("key-a", new EncryptedCacheServiceTestPayload("secret", 1), CachePolicy.Default);

        // Take the raw stored entry written under key-a's AAD and place it, byte-for-byte,
        // under a different key — simulating a replay/corruption where the ciphertext itself is
        // untouched but the key it's read back under has changed.
        var storedUnderA = await inner.GetAsync<byte[]>("key-a");
        await inner.SetAsync("key-b", storedUnderA, CachePolicy.Default);

        var result = await sut.GetAsync<EncryptedCacheServiceTestPayload>("key-b");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_PayloadReplayedUnderDifferentKey_EvictsTheCorruptEntry()
    {
        var inner = new InMemoryDictionaryCacheService();
        var sut = CreateSut(inner, CreateRealEncryptionService());

        await sut.SetAsync("key-a", new EncryptedCacheServiceTestPayload("secret", 1), CachePolicy.Default);
        var storedUnderA = await inner.GetAsync<byte[]>("key-a");
        await inner.SetAsync("key-b", storedUnderA, CachePolicy.Default);

        await sut.GetAsync<EncryptedCacheServiceTestPayload>("key-b");

        // Best-effort eviction (AA-06) — the corrupt entry must no longer be present at the inner
        // layer, so it does not fail identically again on a subsequent read.
        Assert.Null(await inner.GetAsync<byte[]>("key-b"));
    }

    [Fact]
    public async Task GetAsync_TamperedCiphertext_TreatedAsCacheMiss()
    {
        var inner = new InMemoryDictionaryCacheService();
        var sut = CreateSut(inner, CreateRealEncryptionService());

        await sut.SetAsync("tamper-key", new EncryptedCacheServiceTestPayload("secret", 1), CachePolicy.Default);

        // The ciphertext is the tail of the storage format — flip its last byte.
        var tampered = (byte[])(await inner.GetAsync<byte[]>("tamper-key"))!.Clone();
        tampered[^1] ^= 0xFF;
        await inner.SetAsync("tamper-key", tampered, CachePolicy.Default);

        var result = await sut.GetAsync<EncryptedCacheServiceTestPayload>("tamper-key");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_StoredValueIsNotAnEncryptedPayload_TreatedAsCacheMiss_AndEvicted()
    {
        var inner = new InMemoryDictionaryCacheService();
        var sut = CreateSut(inner, CreateRealEncryptionService());

        // Bytes that never went through EncryptedCacheService, e.g. written by a service without encryption.
        await inner.SetAsync("plain-key", "{\"Value\":\"x\",\"Number\":1}"u8.ToArray(), CachePolicy.Default);

        var result = await sut.GetAsync<EncryptedCacheServiceTestPayload>("plain-key");

        Assert.Null(result);
        Assert.Null(await inner.GetAsync<byte[]>("plain-key"));
    }

    [Fact]
    public async Task SetAsync_StoresEncryptedPayloadStorageFormat_BoundToCacheKey()
    {
        var inner = new InMemoryDictionaryCacheService();
        var encryption = new AesGcmEncryptionService(new AsyncOnlyEncryptionKeyProvider("cache-v7"));
        var sut = CreateSut(inner, encryption);

        await sut.SetAsync("format-key", new EncryptedCacheServiceTestPayload("v", 1), CachePolicy.Default);

        byte[]? stored = await inner.GetAsync<byte[]>("format-key");
        Assert.True(EncryptedPayload.TryParse(stored, out EncryptedPayload? payload));
        Assert.Equal("cache-v7", payload.KeyId);

        Result<byte[]> decrypted = await encryption.DecryptAsync(payload, System.Text.Encoding.UTF8.GetBytes("format-key"));
        Assert.True(decrypted.IsSuccess);
    }

    [Fact]
    public async Task GetManyAsync_MapsDecryptFailureAndAbsentKey_ToNull_ValidKeyStillDecrypts()
    {
        var inner = new InMemoryDictionaryCacheService();
        var sut = CreateSut(inner, CreateRealEncryptionService());

        await sut.SetAsync("valid-key", new EncryptedCacheServiceTestPayload("ok", 1), CachePolicy.Default);
        await sut.SetAsync("replay-source", new EncryptedCacheServiceTestPayload("secret", 2), CachePolicy.Default);
        var storedForReplaySource = await inner.GetAsync<byte[]>("replay-source");
        await inner.SetAsync("replayed-key", storedForReplaySource, CachePolicy.Default);

        var results = await sut.GetManyAsync<EncryptedCacheServiceTestPayload>(
            new[] { "valid-key", "replayed-key", "missing-key" });

        Assert.Equal("ok", results["valid-key"]!.Value);
        Assert.Null(results["replayed-key"]);
        Assert.Null(results["missing-key"]);
        Assert.Equal(3, results.Count);
    }

    // -------------------------------------------------------------------------
    // AA-08 — every member works against a key provider that only completes asynchronously
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AllMembers_WithAsyncOnlyKeyProvider_RoundTripCorrectly()
    {
        var encryption = CreateRealEncryptionService();
        var sut = CreateSut(new InMemoryDictionaryCacheService(), encryption, compressionEnabled: true);

        await sut.SetAsync("k1", new EncryptedCacheServiceTestPayload("v1", 1), CachePolicy.Default);
        var got = await sut.GetAsync<EncryptedCacheServiceTestPayload>("k1");
        Assert.Equal("v1", got!.Value);

        var many = await sut.GetManyAsync<EncryptedCacheServiceTestPayload>(new[] { "k1", "missing" });
        Assert.Equal("v1", many["k1"]!.Value);
        Assert.Null(many["missing"]);

        await sut.SetManyAsync(
            new Dictionary<string, EncryptedCacheServiceTestPayload>
            {
                ["k2"] = new("v2", 2),
                ["k3"] = new("v3", 3),
            },
            CachePolicy.Default);

        Assert.Equal("v2", (await sut.GetAsync<EncryptedCacheServiceTestPayload>("k2"))!.Value);
        Assert.Equal("v3", (await sut.GetAsync<EncryptedCacheServiceTestPayload>("k3"))!.Value);

        var orSet = await sut.GetOrSetAsync<EncryptedCacheServiceTestPayload>(
            "k4", async _ => new EncryptedCacheServiceTestPayload("v4", 4), CachePolicy.Default);
        Assert.Equal("v4", orSet.Value);

        await sut.RemoveAsync("k1");
        Assert.Null(await sut.GetAsync<EncryptedCacheServiceTestPayload>("k1"));
    }

    [Fact]
    public async Task GetOrSetAsync_WrappedFactory_ReinvokedFromUnrelatedExecutionContext_StillEncryptsCorrectly()
    {
        // Proves the closure EncryptedCacheService.GetOrSetAsync builds needs no ambient/AsyncLocal
        // context: capture the exact byte[]-typed factory it hands to the inner
        // ICacheService, then invoke that SAME closure again from a background Task.Run — simulating
        // FusionCache re-invoking it on its own eager-refresh continuation — and prove it still
        // produces an entry that decrypts correctly under the original key's AAD.
        var innerCache = new FactoryCapturingCacheService();
        var encryption = CreateRealEncryptionService();
        var sut = CreateSut(innerCache, encryption);

        const string key = "eager-refresh-key";
        var result = await sut.GetOrSetAsync<EncryptedCacheServiceTestPayload>(
            key,
            async _ =>
            {
                await Task.Yield();
                return new EncryptedCacheServiceTestPayload("first-value", 1);
            },
            CachePolicy.Default.WithEagerRefresh(0.9));

        Assert.Equal("first-value", result.Value);
        Assert.NotNull(innerCache.CapturedFactory);

        byte[] refreshedEntry = await Task.Run(
            () => innerCache.CapturedFactory!(CancellationToken.None).AsTask());

        byte[] associatedData = System.Text.Encoding.UTF8.GetBytes(key);
        Assert.True(EncryptedPayload.TryParse(refreshedEntry, out EncryptedPayload? refreshedPayload));
        Result<byte[]> decrypted = await encryption.DecryptAsync(refreshedPayload, associatedData);

        Assert.True(decrypted.IsSuccess);
        var value = JsonSerializer.Deserialize<EncryptedCacheServiceTestPayload>(decrypted.Value, new JsonSerializerOptions());
        Assert.Equal("first-value", value!.Value);
    }

    // -------------------------------------------------------------------------
    // AA-09 — compress-then-encrypt / decrypt-then-decompress ordering
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Compression_AppliedBeforeEncryption_ProducesShorterCiphertext_ForHighlyCompressiblePayload()
    {
        var encryption = CreateRealEncryptionService();
        var innerCompressed = new InMemoryDictionaryCacheService();
        var innerUncompressed = new InMemoryDictionaryCacheService();

        var withCompression = CreateSut(innerCompressed, encryption, compressionEnabled: true);
        var withoutCompression = CreateSut(innerUncompressed, encryption, compressionEnabled: false);

        var highlyCompressible = new string('a', 5000);

        await withCompression.SetAsync("k", highlyCompressible, CachePolicy.Default);
        await withoutCompression.SetAsync("k", highlyCompressible, CachePolicy.Default);

        var compressedPayload = await innerCompressed.GetAsync<byte[]>("k");
        var uncompressedPayload = await innerUncompressed.GetAsync<byte[]>("k");

        Assert.True(
            compressedPayload!.Length < uncompressedPayload!.Length,
            "Compressing before encrypting a highly-compressible payload must produce meaningfully shorter ciphertext.");

        // Round trip still holds — decrypt-then-decompress correctly recovers the original value.
        var result = await withCompression.GetAsync<string>("k");
        Assert.Equal(highlyCompressible, result);
    }

    [Fact]
    public async Task AddBrotliCompression_ThenAddCacheEncryption_UnwrapsSerializer_AndRoundTripsCorrectly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateRealEncryptionService());
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "test");

        builder
            .AddBrotliCompression(o => o.L2ThresholdBytes = 16)
            .AddCacheEncryption();

        using var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredService<ICacheService>();
        Assert.IsType<EncryptedCacheService>(cache);

        // Compression duty has moved to EncryptedCacheService — the registered
        // IFusionCacheSerializer must have been unwrapped back to the plain (non-Brotli) serializer.
        var serializer = provider.GetRequiredService<IFusionCacheSerializer>();
        Assert.IsNotType<BrotliCacheSerializer>(serializer);

        var value = new EncryptedCacheServiceTestPayload(new string('a', 500), 999);
        await cache.SetAsync("compress-order-test", value, CachePolicy.Default);
        var result = await cache.GetAsync<EncryptedCacheServiceTestPayload>("compress-order-test");

        Assert.Equal(value, result);
    }

    [Fact]
    public void AddCacheEncryption_ThenAddBrotliCompression_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateRealEncryptionService());
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "test");

        builder.AddCacheEncryption();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.AddBrotliCompression(o => o.L2ThresholdBytes = 1024));

        Assert.Contains("AddBrotliCompression() before AddCacheEncryption()", ex.Message);
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
        Assert.Contains("AddSharedKernelCryptography(configuration).AddSymmetricEncryption()", ex.Message);
    }

    [Fact]
    public void AddCacheEncryption_WithoutCacheServiceRegistered_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateRealEncryptionService());

        // Deliberately never calls AddSharedKernelCaching — CachingBuilder is constructed directly
        // (internal, InternalsVisibleTo-accessible) to prove AddCacheEncryption's new prerequisite
        // guard fires even when the ISymmetricEncryptionService guard above it already passed.
        ICachingBuilder builder = new CachingBuilder(services);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddCacheEncryption());
        Assert.Contains("AddSharedKernelCaching", ex.Message);
    }

    [Fact]
    public async Task AddCacheEncryption_RegisteredThroughCryptographyBuilder_RoundTripsWithAsyncOnlyKeyProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEncryptionKeyProvider>(new AsyncOnlyEncryptionKeyProvider());
        services.AddSharedKernelCryptography(new ConfigurationBuilder().Build())
            .AddSymmetricEncryption();
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "test");

        builder.AddCacheEncryption();

        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<ICacheService>();
        Assert.IsType<EncryptedCacheService>(cache);

        var value = new EncryptedCacheServiceTestPayload("through-builder", 5);
        await cache.SetAsync("builder-key", value, CachePolicy.Default);
        Assert.Equal(value, await cache.GetAsync<EncryptedCacheServiceTestPayload>("builder-key"));
    }

    [Fact]
    public void AddCacheEncryption_WithSymmetricEncryptionServiceRegistered_ResolvesAsEncryptedCacheService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateRealEncryptionService());
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "test");

        builder.AddCacheEncryption();

        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<ICacheService>();

        Assert.IsType<EncryptedCacheService>(cache);
    }

    [Fact]
    public void AddCacheEncryption_RegistersCacheEncryptionOptionsMarker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateRealEncryptionService());
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "test");

        builder.AddCacheEncryption();

        using var provider = services.BuildServiceProvider();
        var marker = provider.GetService<CacheEncryptionOptions>();

        Assert.NotNull(marker);
        Assert.True(marker.Enabled);
    }

    // -------------------------------------------------------------------------
    // AA-11 — composition with AddTenantCacheService: tenant-scoped key binds AAD
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AddTenantCacheService_ComposedWithCacheEncryption_SameEntityId_DifferentTenants_RoundTripIndependently()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateRealEncryptionService());
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "compose-test");

        builder.AddTenantCacheService().AddCacheEncryption();

        using var provider = services.BuildServiceProvider();
        var tenantCache = provider.GetRequiredService<ITenantCacheService>();

        await tenantCache.SetAsync("tenant-a", "orders", "1", "secret-a", CachePolicy.Default);
        await tenantCache.SetAsync("tenant-b", "orders", "1", "secret-b", CachePolicy.Default);

        Assert.Equal("secret-a", await tenantCache.GetAsync<string>("tenant-a", "orders", "1"));
        Assert.Equal("secret-b", await tenantCache.GetAsync<string>("tenant-b", "orders", "1"));
    }

    [Fact]
    public async Task TenantScopedKey_UsedAsAad_CrossTenantReplayOfSameEntityId_FailsAuthentication()
    {
        // Demonstrates, rather than merely asserting in prose, the composition benefit this
        // redesign gains over the retired serializer-level approach: ITenantCacheKeyProvider's
        // tenant-scoped key string is exactly what EncryptedCacheService receives and binds as AAD,
        // so a raw payload replayed from one tenant's key to another tenant's key for the identical
        // (entity, id) pair fails to decrypt — even though the underlying ciphertext bytes were
        // never altered.
        var keyProvider = new TenantCacheKeyProvider(Options.Create(new CachingCoreOptions { ServiceName = "svc" }));
        var inner = new InMemoryDictionaryCacheService();
        var sut = CreateSut(inner, CreateRealEncryptionService());

        string tenantAKey = keyProvider.BuildTenantKey("tenant-a", "orders", "1");
        string tenantBKey = keyProvider.BuildTenantKey("tenant-b", "orders", "1");
        Assert.NotEqual(tenantAKey, tenantBKey);

        await sut.SetAsync(tenantAKey, "secret-a", CachePolicy.Default);

        // Simulate the raw payload landing under tenant B's key for the same (entity, id) pair —
        // a hypothetical routing/replay bug, not something the normal ITenantCacheService surface
        // can produce on its own, but exactly the scenario key-bound AAD must defend against.
        var storedForA = await inner.GetAsync<byte[]>(tenantAKey);
        await inner.SetAsync(tenantBKey, storedForA, CachePolicy.Default);

        var result = await sut.GetAsync<string>(tenantBKey);

        Assert.Null(result);
    }
}
