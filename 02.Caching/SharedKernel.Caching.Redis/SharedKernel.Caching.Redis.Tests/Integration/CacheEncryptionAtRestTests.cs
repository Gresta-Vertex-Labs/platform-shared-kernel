using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.Redis.Extensions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Testing.Cryptography;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Tests.Integration;

/// <summary>DTO used by <see cref="CacheEncryptionAtRestTests"/>.</summary>
internal sealed record CacheEncryptionAtRestPayload(string Secret, int Number);

/// <summary>
/// Integration test proving genuine ciphertext-on-the-wire: a value written through
/// <c>AddCacheEncryption()</c> composed with <c>AddRedisL2(...)</c> is unrecoverable by a raw
/// read against the underlying Redis key — not merely "serialized differently."
/// </summary>
/// <remarks>
/// <c>Microsoft.Extensions.Caching.StackExchangeRedis</c> (the <c>IDistributedCache</c>
/// implementation <c>AddRedisL2</c> is built on) stores every entry as a Redis <b>Hash</b> —
/// fields <c>absexp</c>/<c>sldexp</c>/<c>data</c> — via its own internal Lua script, never a plain
/// Redis string. The actual serialized payload lives in the hash's <c>data</c> field, so this test
/// reads via <see cref="IDatabase.HashGetAsync(RedisKey, RedisValue, CommandFlags)"/>, not
/// <c>StringGetAsync</c> (which would fail with a Redis <c>WRONGTYPE</c> error against this key).
/// </remarks>
[Collection("Redis")]
public sealed class CacheEncryptionAtRestTests : IAsyncLifetime
{
    private const string UserKey = "encryption-test:entry:1";
    private const string RedisSchemaVersionSeparator = "v2:";

    /// <summary>
    /// The hash field name <c>Microsoft.Extensions.Caching.StackExchangeRedis</c> uses to store
    /// the actual serialized payload bytes.
    /// </summary>
    private const string RedisDataField = "data";

    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private ServiceProvider? _provider;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();

        // A real AES-GCM encryption service backed by an in-memory key provider — proves genuine
        // authenticated encryption, not merely that some transform ran.
        services.AddSingleton<ISymmetricEncryptionService>(
            new AesGcmEncryptionService(new FakeEncryptionKeyProvider()));

        services
            .AddSharedKernelCaching(o => o.ServiceName = "encryption-test-svc")
            .AddRedisL2(_redisContainer.GetConnectionString())
            .AddCacheEncryption();

        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();

        await _redisContainer.DisposeAsync();
    }

    private ICacheService Cache => _provider!.GetRequiredService<ICacheService>();
    private IConnectionMultiplexer Multiplexer => _provider!.GetRequiredService<IConnectionMultiplexer>();

    /// <summary>
    /// Writes a value through <see cref="ICacheService.SetAsync{T}"/> with cache encryption
    /// enabled, then reads the same key's <c>data</c> hash field with a raw
    /// <see cref="IDatabase.HashGetAsync(RedisKey, RedisValue, CommandFlags)"/> call — bypassing
    /// FusionCache/the SharedKernel serializer pipeline entirely. Asserts the raw bytes are
    /// genuine AES-GCM ciphertext: prefixed with the encryption magic bytes, and containing
    /// neither the plaintext value nor any readable JSON.
    /// </summary>
    [Fact]
    public async Task SetAsync_WithCacheEncryption_RawRedisRead_IsNotThePlaintextJson()
    {
        var value = new CacheEncryptionAtRestPayload("super-secret-value", 42);
        await Cache.SetAsync(UserKey, value, CachePolicy.Default);

        // Allow FusionCache's async L2 write to propagate.
        await Task.Delay(500);

        var db = Multiplexer.GetDatabase();
        var expectedKey = $"{RedisSchemaVersionSeparator}{UserKey}";

        RedisValue raw = await db.HashGetAsync(expectedKey, RedisDataField);

        Assert.True(
            raw.HasValue,
            $"Expected to find Redis hash field '{expectedKey}'.'{RedisDataField}' but it was not present.");

        byte[] rawBytes = raw!;

        // The wire frame must start with the encryption magic bytes ("EN" = 0x45 0x4E) —
        // deliberately distinct from Brotli's "BR" (0x42 0x52).
        Assert.True(rawBytes.Length >= 2, "Raw Redis value must be at least 2 bytes long.");
        Assert.Equal(0x45, rawBytes[0]);
        Assert.Equal(0x4E, rawBytes[1]);

        // The raw bytes must not contain the plaintext secret value or the DTO's own property
        // name — genuine ciphertext-on-the-wire proof, not merely "serialized." (A bare "{" is
        // not checked: high-entropy AES-GCM ciphertext can coincidentally contain any single byte
        // value, including 0x7B, without that indicating a plaintext leak.)
        string rawAsLatin1 = Encoding.Latin1.GetString(rawBytes);
        Assert.DoesNotContain("super-secret-value", rawAsLatin1, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", rawAsLatin1, StringComparison.Ordinal);
    }

    /// <summary>
    /// Confirms the encryption round trip is not broken by the Redis L2 wiring — a value written
    /// with encryption enabled reads back correctly through the normal <see cref="ICacheService"/>
    /// surface.
    /// </summary>
    [Fact]
    public async Task SetAsync_WithCacheEncryption_GetAsync_ReturnsCorrectValue()
    {
        const string key = "encryption-test:roundtrip:1";
        var value = new CacheEncryptionAtRestPayload("round-trip-secret", 7);

        await Cache.SetAsync(key, value, CachePolicy.Default);
        var result = await Cache.GetAsync<CacheEncryptionAtRestPayload>(key);

        Assert.Equal(value, result);
    }
}
