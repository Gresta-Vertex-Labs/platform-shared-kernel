using System.Text;
using System.Text.Json;
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
/// <para>
/// <c>Microsoft.Extensions.Caching.StackExchangeRedis</c> (the <c>IDistributedCache</c>
/// implementation <c>AddRedisL2</c> is built on) stores every entry as a Redis <b>Hash</b> —
/// fields <c>absexp</c>/<c>sldexp</c>/<c>data</c> — via its own internal Lua script, never a plain
/// Redis string. The actual serialized payload lives in the hash's <c>data</c> field, so this test
/// reads via <see cref="IDatabase.HashGetAsync(RedisKey, RedisValue, CommandFlags)"/>, not
/// <c>StringGetAsync</c> (which would fail with a Redis <c>WRONGTYPE</c> error against this key).
/// </para>
/// <para>
/// <b>Phase 46/WO-081:</b> the wire shape changed from Phase 42's magic-byte-prefixed opaque blob
/// (produced by the now-retired <c>CacheEncryptionSerializer</c>) to a directly-stored,
/// JSON-serialized <c>EncryptedPayload</c> envelope — <c>EncryptedCacheService</c> now encrypts at
/// the <c>ICacheService</c> level, storing an <c>EncryptedPayload</c> (<c>KeyId</c>/<c>Nonce</c>/
/// <c>Ciphertext</c>/<c>Tag</c>) as the value FusionCache's own (now plain, unwrapped) serializer
/// writes to Redis. The assertion below no longer checks for the retired magic-byte prefix; it
/// instead proves the raw bytes contain neither the plaintext secret nor the DTO's own property
/// name, while genuinely looking like the new <c>EncryptedPayload</c> JSON envelope.
/// </para>
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
    /// FusionCache/the SharedKernel serializer pipeline entirely. Asserts the raw bytes are genuine
    /// AES-GCM ciphertext: they contain neither the plaintext value nor any readable JSON of the
    /// original DTO, while genuinely carrying the new <c>EncryptedPayload</c> envelope's own field
    /// names.
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

        // Genuinely parses as JSON — it is now a directly-stored EncryptedPayload envelope
        // (nested inside FusionCache's own distributed-entry wrapper, whose own properties are
        // "Value"/"Timestamp"/"LogicalExpirationTimestamp"/"Tags"/"Metadata" — "Value" is the
        // EncryptedPayload), not an opaque magic-byte-prefixed blob. Confirms it is well-formed
        // JSON at all (JsonDocument.Parse throws on malformed input) rather than asserting on a
        // specific naming policy for the nested EncryptedPayload's own property names, which the
        // substring checks below cover case-insensitively instead.
        using (JsonDocument.Parse(rawBytes))
        {
            // Parsed successfully — see remarks above.
        }

        string rawAsLatin1 = Encoding.Latin1.GetString(rawBytes);
        Assert.Contains("Ciphertext", rawAsLatin1, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("KeyId", rawAsLatin1, StringComparison.OrdinalIgnoreCase);

        // The raw bytes must not contain the plaintext secret value or the DTO's own property
        // name — genuine ciphertext-on-the-wire proof, not merely "serialized." (A bare "{" is
        // not checked: high-entropy AES-GCM ciphertext, Base64-encoded, can coincidentally contain
        // any single character without that indicating a plaintext leak.)
        Assert.DoesNotContain("super-secret-value", rawAsLatin1, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Secret\"", rawAsLatin1, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Number\"", rawAsLatin1, StringComparison.Ordinal);
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
