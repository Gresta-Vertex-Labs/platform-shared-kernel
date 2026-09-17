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
/// from one key to another to simulate a replay, or inspecting stored length. Counts the calls the
/// decorator makes so eviction/recompute paths can be asserted.
/// </summary>
internal sealed class InMemoryDictionaryCacheService : ICacheService
{
    private readonly Dictionary<string, object?> _store = new(StringComparer.Ordinal);

    public int GetOrSetCalls { get; private set; }

    public int FactoryCalls { get; private set; }

    public int RemoveCalls { get; private set; }

    public ValueTask<CacheLookup<T>> TryGetAsync<T>(string key, CancellationToken ct = default) =>
        new(_store.TryGetValue(key, out var value) ? CacheLookup<T>.Hit((T)value!) : CacheLookup<T>.Miss);

    public async ValueTask<IReadOnlyDictionary<string, CacheLookup<T>>> TryGetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, CacheLookup<T>>(StringComparer.Ordinal);
        foreach (var key in keys)
            result[key] = await TryGetAsync<T>(key, ct);

        return result;
    }

    public ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default) =>
        GetOrSetAsync(key, (_, token) => factory(token), policy, ct);

    public async ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CacheFactoryContext, CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        GetOrSetCalls++;
        if (_store.TryGetValue(key, out var existing))
            return (T)existing!;

        FactoryCalls++;
        var context = new CacheFactoryContext(key, policy);
        T value = await factory(context, ct).ConfigureAwait(false);
        if (!context.IsCachingSkipped)
            _store[key] = value;

        return value;
    }

    public ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
    {
        _store[key] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct = default)
    {
        foreach (var (key, value) in entries)
            _store[key] = value;

        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        RemoveCalls++;
        _store.Remove(key);
        return ValueTask.CompletedTask;
    }

    public ValueTask ExpireAsync(string key, CancellationToken ct = default) => RemoveAsync(key, ct);

    public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default) => ValueTask.CompletedTask;

    public ValueTask RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default) => ValueTask.CompletedTask;

    public ValueTask ClearAsync(CancellationToken ct = default)
    {
        _store.Clear();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A bespoke <see cref="ICacheService"/> double that captures the <c>byte[]</c>-typed
/// factory <see cref="EncryptedCacheService"/> passes down to the inner <c>GetOrSetAsync</c>, so
/// the eager-refresh proof can re-invoke that exact closure from an unrelated execution context
/// afterwards. Every other member is unused by that test and throws if called.
/// </summary>
internal sealed class FactoryCapturingCacheService : ICacheService
{
    public Func<CacheFactoryContext, CancellationToken, ValueTask<byte[]>>? CapturedFactory { get; private set; }

    public ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CacheFactoryContext, CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        if (typeof(T) == typeof(byte[]))
            CapturedFactory = (Func<CacheFactoryContext, CancellationToken, ValueTask<byte[]>>)(object)factory;

        return factory(new CacheFactoryContext(key, policy), ct);
    }

    public ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by this test.");

    public ValueTask<CacheLookup<T>> TryGetAsync<T>(string key, CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by this test.");

    public ValueTask<IReadOnlyDictionary<string, CacheLookup<T>>> TryGetManyAsync<T>(IEnumerable<string> keys, CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by this test.");

    public ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by this test.");

    public ValueTask SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by this test.");

    public ValueTask RemoveAsync(string key, CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by this test.");

    public ValueTask ExpireAsync(string key, CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by this test.");

    public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by this test.");

    public ValueTask RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by this test.");

    public ValueTask ClearAsync(CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by this test.");
}

/// <summary>
/// Unit tests for <see cref="EncryptedCacheService"/> — key-bound AAD, asynchronous crypto,
/// compression composition, tamper/decrypt-failure handling, and <c>AddCacheEncryption()</c> DI wiring.
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

    private static async Task<byte[]> StoredBytesAsync(ICacheService inner, string key) =>
        (await inner.TryGetAsync<byte[]>(key)).Value;

    // -------------------------------------------------------------------------
    // Round trip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetAsync_ThenTryGetAsync_RoundTripsCorrectly()
    {
        var sut = CreateSut(new InMemoryDictionaryCacheService(), CreateRealEncryptionService());
        var original = new EncryptedCacheServiceTestPayload("secret-value", 42);

        await sut.SetAsync("key-1", original, CachePolicy.Default);
        var result = await sut.TryGetAsync<EncryptedCacheServiceTestPayload>("key-1");

        Assert.Equal(original, result.Value);
    }

    [Fact]
    public async Task TryGetAsync_UnknownKey_IsMiss()
    {
        var sut = CreateSut(new InMemoryDictionaryCacheService(), CreateRealEncryptionService());

        var result = await sut.TryGetAsync<EncryptedCacheServiceTestPayload>("unknown-key");

        Assert.False(result.IsHit);
    }

    [Fact]
    public async Task SetAsync_NullAndZero_RoundTripAsHits()
    {
        var sut = CreateSut(new InMemoryDictionaryCacheService(), CreateRealEncryptionService());

        await sut.SetAsync<string?>("null-key", null, CachePolicy.Default);
        await sut.SetAsync("zero-key", 0, CachePolicy.Default);

        var cachedNull = await sut.TryGetAsync<string?>("null-key");
        var cachedZero = await sut.TryGetAsync<int>("zero-key");

        Assert.True(cachedNull.IsHit);
        Assert.Null(cachedNull.Value);
        Assert.True(cachedZero.IsHit);
        Assert.Equal(0, cachedZero.Value);
    }

    [Fact]
    public async Task GetOrSetAsync_CacheMiss_InvokesFactoryAndCaches()
    {
        var sut = CreateSut(new InMemoryDictionaryCacheService(), CreateRealEncryptionService());
        var calls = 0;

        async ValueTask<EncryptedCacheServiceTestPayload> Factory(CancellationToken _)
        {
            calls++;
            await Task.Yield();
            return new EncryptedCacheServiceTestPayload("computed", 7);
        }

        var first = await sut.GetOrSetAsync<EncryptedCacheServiceTestPayload>("gos-1", Factory, CachePolicy.Default);
        var second = await sut.GetOrSetAsync<EncryptedCacheServiceTestPayload>("gos-1", Factory, CachePolicy.Default);

        Assert.Equal("computed", first.Value);
        Assert.Equal("computed", second.Value);
        Assert.Equal(1, calls); // second call is a cache hit — factory not invoked again.
    }

    [Fact]
    public async Task GetOrSetAsync_ContextFactory_SkipCaching_PassesThroughToInner()
    {
        var inner = new InMemoryDictionaryCacheService();
        var sut = CreateSut(inner, CreateRealEncryptionService());

        var result = await sut.GetOrSetAsync<string>(
            "skip-key",
            (context, _) =>
            {
                context.SkipCaching();
                return ValueTask.FromResult("uncached");
            },
            CachePolicy.Default);

        Assert.Equal("uncached", result);
        Assert.False((await inner.TryGetAsync<byte[]>("skip-key")).IsHit);
    }

    [Fact]
    public async Task RemoveAsync_RemovesEntry()
    {
        var sut = CreateSut(new InMemoryDictionaryCacheService(), CreateRealEncryptionService());
        await sut.SetAsync("rm-1", new EncryptedCacheServiceTestPayload("v", 1), CachePolicy.Default);

        await sut.RemoveAsync("rm-1");

        Assert.False((await sut.TryGetAsync<EncryptedCacheServiceTestPayload>("rm-1")).IsHit);
    }

    // -------------------------------------------------------------------------
    // Cross-key replay fails authentication
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetAsync_PayloadReplayedUnderDifferentKey_FailsAuthentication_TreatedAsCacheMiss()
    {
        var inner = new InMemoryDictionaryCacheService();
        var sut = CreateSut(inner, CreateRealEncryptionService());

        await sut.SetAsync("key-a", new EncryptedCacheServiceTestPayload("secret", 1), CachePolicy.Default);

        // Take the raw stored entry written under key-a's AAD and place it, byte-for-byte,
        // under a different key — the ciphertext itself is untouched but the key it's read back
        // under has changed.
        await inner.SetAsync("key-b", await StoredBytesAsync(inner, "key-a"), CachePolicy.Default);

        var result = await sut.TryGetAsync<EncryptedCacheServiceTestPayload>("key-b");

        Assert.False(result.IsHit);
    }

    [Fact]
    public async Task TryGetAsync_PayloadReplayedUnderDifferentKey_EvictsTheCorruptEntry()
    {
        var inner = new InMemoryDictionaryCacheService();
        var sut = CreateSut(inner, CreateRealEncryptionService());

        await sut.SetAsync("key-a", new EncryptedCacheServiceTestPayload("secret", 1), CachePolicy.Default);
        await inner.SetAsync("key-b", await StoredBytesAsync(inner, "key-a"), CachePolicy.Default);

        await sut.TryGetAsync<EncryptedCacheServiceTestPayload>("key-b");

        // Best-effort eviction — the corrupt entry must no longer be present at the inner layer,
        // so it does not fail identically again on a subsequent read.
        Assert.False((await inner.TryGetAsync<byte[]>("key-b")).IsHit);
    }

    [Fact]
    public async Task TryGetAsync_TamperedCiphertext_TreatedAsCacheMiss()
    {
        var inner = new InMemoryDictionaryCacheService();
        var sut = CreateSut(inner, CreateRealEncryptionService());

        await sut.SetAsync("tamper-key", new EncryptedCacheServiceTestPayload("secret", 1), CachePolicy.Default);

        // The ciphertext is the tail of the storage format — flip its last byte.
        var tampered = (byte[])(await StoredBytesAsync(inner, "tamper-key")).Clone();
        tampered[^1] ^= 0xFF;
        await inner.SetAsync("tamper-key", tampered, CachePolicy.Default);

        var result = await sut.TryGetAsync<EncryptedCacheServiceTestPayload>("tamper-key");

        Assert.False(result.IsHit);
    }

    [Fact]
    public async Task TryGetAsync_StoredValueIsNotAnEncryptedPayload_TreatedAsCacheMiss_AndEvicted()
    {
        var inner = new InMemoryDictionaryCacheService();
        var sut = CreateSut(inner, CreateRealEncryptionService());

        // Bytes that never went through EncryptedCacheService, e.g. written by a service without encryption.
        await inner.SetAsync("plain-key", "{\"Value\":\"x\",\"Number\":1}"u8.ToArray(), CachePolicy.Default);

        var result = await sut.TryGetAsync<EncryptedCacheServiceTestPayload>("plain-key");

        Assert.False(result.IsHit);
        Assert.False((await inner.TryGetAsync<byte[]>("plain-key")).IsHit);
    }

    [Fact]
    public async Task SetAsync_StoresEncryptedPayloadStorageFormat_BoundToCacheKey()
    {
        var inner = new InMemoryDictionaryCacheService();
        var encryption = new AesGcmEncryptionService(new AsyncOnlyEncryptionKeyProvider("cache-v7"));
        var sut = CreateSut(inner, encryption);

        await sut.SetAsync("format-key", new EncryptedCacheServiceTestPayload("v", 1), CachePolicy.Default);

        byte[] stored = await StoredBytesAsync(inner, "format-key");
        Assert.True(EncryptedPayload.TryParse(stored, out EncryptedPayload? payload));
        Assert.Equal("cache-v7", payload.KeyId);

        Result<byte[]> decrypted = await encryption.DecryptAsync(payload, System.Text.Encoding.UTF8.GetBytes("format-key"));
        Assert.True(decrypted.IsSuccess);
    }

    [Fact]
    public async Task TryGetManyAsync_MapsDecryptFailureAndAbsentKey_ToMiss_ValidKeyStillDecrypts()
    {
        var inner = new InMemoryDictionaryCacheService();
        var sut = CreateSut(inner, CreateRealEncryptionService());

        await sut.SetAsync("valid-key", new EncryptedCacheServiceTestPayload("ok", 1), CachePolicy.Default);
        await sut.SetAsync("replay-source", new EncryptedCacheServiceTestPayload("secret", 2), CachePolicy.Default);
        await inner.SetAsync("replayed-key", await StoredBytesAsync(inner, "replay-source"), CachePolicy.Default);

        var results = await sut.TryGetManyAsync<EncryptedCacheServiceTestPayload>(
            new[] { "valid-key", "replayed-key", "missing-key" });

        Assert.Equal(3, results.Count);
        Assert.Equal("ok", results["valid-key"].Value.Value);
        Assert.False(results["replayed-key"].IsHit);
        Assert.False(results["missing-key"].IsHit);
    }

    // -------------------------------------------------------------------------
    // Corrupted entry in GetOrSetAsync: evict and recompute through the inner service
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetOrSetAsync_CorruptedStoredEntry_EvictsAndRecomputesOnceThroughInner_ReturningFreshValue()
    {
        var inner = new InMemoryDictionaryCacheService();
        var encryption = CreateRealEncryptionService();
        var sut = CreateSut(inner, encryption);

        await sut.SetAsync("corrupt-key", new EncryptedCacheServiceTestPayload("stale", 1), CachePolicy.Default);
        var corrupted = (byte[])(await StoredBytesAsync(inner, "corrupt-key")).Clone();
        corrupted[^1] ^= 0xFF;
        await inner.SetAsync("corrupt-key", corrupted, CachePolicy.Default);

        var factoryCalls = 0;
        var result = await sut.GetOrSetAsync<EncryptedCacheServiceTestPayload>(
            "corrupt-key",
            _ =>
            {
                factoryCalls++;
                return ValueTask.FromResult(new EncryptedCacheServiceTestPayload("fresh", 2));
            },
            CachePolicy.Default);

        Assert.Equal(new EncryptedCacheServiceTestPayload("fresh", 2), result);
        Assert.Equal(1, factoryCalls);
        Assert.Equal(1, inner.RemoveCalls);
        Assert.Equal(2, inner.GetOrSetCalls);  // the corrupt hit, then one recompute
        Assert.Equal(1, inner.FactoryCalls);   // the recompute ran through the inner service's factory path

        // The recomputed entry is now stored encrypted and readable, so the next call is a plain hit.
        Assert.Equal("fresh", (await sut.TryGetAsync<EncryptedCacheServiceTestPayload>("corrupt-key")).Value.Value);
        var again = await sut.GetOrSetAsync<EncryptedCacheServiceTestPayload>(
            "corrupt-key",
            _ => throw new InvalidOperationException("must not recompute"),
            CachePolicy.Default);
        Assert.Equal("fresh", again.Value);
    }

    [Fact]
    public async Task GetOrSetAsync_CorruptedEntry_WithRealFusionCache_RecomputesOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "corrupt-test");
        using var provider = services.BuildServiceProvider();
        var inner = provider.GetRequiredService<ICacheService>();
        var sut = CreateSut(inner, CreateRealEncryptionService());

        await inner.SetAsync("svc:entity:corrupt", "not an encrypted payload"u8.ToArray(), CachePolicy.Default);

        var factoryCalls = 0;
        var result = await sut.GetOrSetAsync<string>(
            "svc:entity:corrupt",
            _ =>
            {
                factoryCalls++;
                return ValueTask.FromResult("fresh");
            },
            CachePolicy.Default);

        Assert.Equal("fresh", result);
        Assert.Equal(1, factoryCalls);
        Assert.Equal("fresh", (await sut.TryGetAsync<string>("svc:entity:corrupt")).Value);
    }

    // -------------------------------------------------------------------------
    // Every member works against a key provider that only completes asynchronously
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AllMembers_WithAsyncOnlyKeyProvider_RoundTripCorrectly()
    {
        var encryption = CreateRealEncryptionService();
        var sut = CreateSut(new InMemoryDictionaryCacheService(), encryption, compressionEnabled: true);

        await sut.SetAsync("k1", new EncryptedCacheServiceTestPayload("v1", 1), CachePolicy.Default);
        Assert.Equal("v1", (await sut.TryGetAsync<EncryptedCacheServiceTestPayload>("k1")).Value.Value);

        var many = await sut.TryGetManyAsync<EncryptedCacheServiceTestPayload>(new[] { "k1", "missing" });
        Assert.Equal("v1", many["k1"].Value.Value);
        Assert.False(many["missing"].IsHit);

        await sut.SetManyAsync(
            new Dictionary<string, EncryptedCacheServiceTestPayload>
            {
                ["k2"] = new("v2", 2),
                ["k3"] = new("v3", 3),
            },
            CachePolicy.Default);

        Assert.Equal("v2", (await sut.TryGetAsync<EncryptedCacheServiceTestPayload>("k2")).Value.Value);
        Assert.Equal("v3", (await sut.TryGetAsync<EncryptedCacheServiceTestPayload>("k3")).Value.Value);

        var orSet = await sut.GetOrSetAsync<EncryptedCacheServiceTestPayload>(
            "k4", _ => ValueTask.FromResult(new EncryptedCacheServiceTestPayload("v4", 4)), CachePolicy.Default);
        Assert.Equal("v4", orSet.Value);

        await sut.RemoveAsync("k1");
        Assert.False((await sut.TryGetAsync<EncryptedCacheServiceTestPayload>("k1")).IsHit);

        await sut.ExpireAsync("k2");
        Assert.False((await sut.TryGetAsync<EncryptedCacheServiceTestPayload>("k2")).IsHit);

        await sut.ClearAsync();
        Assert.False((await sut.TryGetAsync<EncryptedCacheServiceTestPayload>("k3")).IsHit);
    }

    [Fact]
    public async Task GetOrSetAsync_WrappedFactory_ReinvokedFromUnrelatedExecutionContext_StillEncryptsCorrectly()
    {
        // Proves the closure EncryptedCacheService.GetOrSetAsync builds needs no ambient/AsyncLocal
        // context: capture the exact byte[]-typed factory it hands to the inner ICacheService, then
        // invoke that SAME closure again from a background Task.Run — simulating FusionCache
        // re-invoking it on its own eager-refresh continuation — and prove it still produces an
        // entry that decrypts correctly under the original key's AAD.
        var innerCache = new FactoryCapturingCacheService();
        var encryption = CreateRealEncryptionService();
        var sut = CreateSut(innerCache, encryption);

        const string key = "eager-refresh-key";
        var policy = CachePolicy.Default.WithEagerRefresh(0.9);
        var result = await sut.GetOrSetAsync<EncryptedCacheServiceTestPayload>(
            key,
            async _ =>
            {
                await Task.Yield();
                return new EncryptedCacheServiceTestPayload("first-value", 1);
            },
            policy);

        Assert.Equal("first-value", result.Value);
        Assert.NotNull(innerCache.CapturedFactory);

        byte[] refreshedEntry = await Task.Run(
            () => innerCache.CapturedFactory!(new CacheFactoryContext(key, policy), CancellationToken.None).AsTask());

        byte[] associatedData = System.Text.Encoding.UTF8.GetBytes(key);
        Assert.True(EncryptedPayload.TryParse(refreshedEntry, out EncryptedPayload? refreshedPayload));
        Result<byte[]> decrypted = await encryption.DecryptAsync(refreshedPayload, associatedData);

        Assert.True(decrypted.IsSuccess);
        var value = JsonSerializer.Deserialize<EncryptedCacheServiceTestPayload>(decrypted.Value, new JsonSerializerOptions());
        Assert.Equal("first-value", value!.Value);
    }

    // -------------------------------------------------------------------------
    // Compress-then-encrypt / decrypt-then-decompress ordering
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

        var compressedPayload = await StoredBytesAsync(innerCompressed, "k");
        var uncompressedPayload = await StoredBytesAsync(innerUncompressed, "k");

        Assert.True(
            compressedPayload.Length < uncompressedPayload.Length,
            "Compressing before encrypting a highly-compressible payload must produce meaningfully shorter ciphertext.");

        // Round trip still holds — decrypt-then-decompress correctly recovers the original value.
        Assert.Equal(highlyCompressible, (await withCompression.TryGetAsync<string>("k")).Value);
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

        Assert.Equal(value, (await cache.TryGetAsync<EncryptedCacheServiceTestPayload>("compress-order-test")).Value);
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
        // (internal, InternalsVisibleTo-accessible) so the ICacheService prerequisite guard fires
        // even though the ISymmetricEncryptionService guard above it already passed.
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
        Assert.Equal(value, (await cache.TryGetAsync<EncryptedCacheServiceTestPayload>("builder-key")).Value);
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

        Assert.IsType<EncryptedCacheService>(provider.GetRequiredService<ICacheService>());
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
    // Composition with AddTenantCacheService: the tenant-scoped key binds the AAD
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

        Assert.Equal("secret-a", (await tenantCache.TryGetAsync<string>("tenant-a", "orders", "1")).Value);
        Assert.Equal("secret-b", (await tenantCache.TryGetAsync<string>("tenant-b", "orders", "1")).Value);
    }

    [Fact]
    public async Task TenantScopedKey_UsedAsAad_CrossTenantReplayOfSameEntityId_FailsAuthentication()
    {
        // ITenantCacheKeyProvider's tenant-scoped key string is exactly what EncryptedCacheService
        // receives and binds as AAD, so a raw payload replayed from one tenant's key to another
        // tenant's key for the identical (entity, id) pair fails to decrypt — even though the
        // underlying ciphertext bytes were never altered.
        ITenantCacheKeyProvider keyProvider = new CacheKeyProvider(Options.Create(new CachingOptions { ServiceName = "svc" }));
        var inner = new InMemoryDictionaryCacheService();
        var sut = CreateSut(inner, CreateRealEncryptionService());

        string tenantAKey = keyProvider.BuildTenantKey("tenant-a", "orders", "1");
        string tenantBKey = keyProvider.BuildTenantKey("tenant-b", "orders", "1");
        Assert.NotEqual(tenantAKey, tenantBKey);

        await sut.SetAsync(tenantAKey, "secret-a", CachePolicy.Default);

        // Simulate the raw payload landing under tenant B's key for the same (entity, id) pair —
        // a hypothetical routing/replay bug, not something the ITenantCacheService surface can
        // produce on its own, but exactly the scenario key-bound AAD must defend against.
        await inner.SetAsync(tenantBKey, await StoredBytesAsync(inner, tenantAKey), CachePolicy.Default);

        Assert.False((await sut.TryGetAsync<string>(tenantBKey)).IsHit);
    }
}
