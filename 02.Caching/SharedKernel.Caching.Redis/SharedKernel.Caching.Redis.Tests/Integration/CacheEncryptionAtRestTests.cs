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
/// <c>EncryptedCacheService</c> encrypts at the <c>ICacheService</c> level and stores each value as a
/// <see cref="T:byte[]"/> holding the <c>EncryptedPayload</c> storage format
/// (<c>EncryptedPayload.ToBytes()</c>). FusionCache's own (plain, unwrapped) JSON serializer writes that
/// array into its distributed-entry wrapper as a Base64 string, so this test decodes the wrapper's
/// value and parses it with <c>EncryptedPayload.TryParse</c>.
/// </para>
/// </remarks>
[Collection("Redis")]
public sealed class CacheEncryptionAtRestTests : IAsyncLifetime
{
    private const string UserKey = "encryption-test:entry:1";
    private const string KeyId = "cache-at-rest-v1";
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
            new AesGcmEncryptionService(new FakeEncryptionKeyProvider(KeyId)));

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
    /// original DTO, and the stored value parses as an <c>EncryptedPayload</c> under the expected key id.
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

        // FusionCache's distributed-entry wrapper is JSON; its value property holds the stored byte[]
        // as a Base64 string, which must be an EncryptedPayload in its storage format.
        using (JsonDocument document = JsonDocument.Parse(rawBytes))
        {
            JsonElement storedValue = document.RootElement.EnumerateObject()
                .Single(p => string.Equals(p.Name, "Value", StringComparison.OrdinalIgnoreCase))
                .Value;

            Assert.Equal(JsonValueKind.String, storedValue.ValueKind);
            Assert.True(
                EncryptedPayload.TryParse(storedValue.GetBytesFromBase64(), out EncryptedPayload? payload),
                "The stored value must be an EncryptedPayload in its storage format.");
            Assert.Equal(KeyId, payload.KeyId);
        }

        string rawAsLatin1 = Encoding.Latin1.GetString(rawBytes);

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
