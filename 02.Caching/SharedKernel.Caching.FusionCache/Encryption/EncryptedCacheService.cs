using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Implementations;
using SharedKernel.Caching.FusionCache.Serialization;
using SharedKernel.Cryptography;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Caching.FusionCache.Encryption;

/// <summary>
/// An <see cref="ICacheService"/> decorator that applies opt-in AES-GCM authenticated encryption
/// to every cached value, bound to the exact cache key each member receives.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this decorates <see cref="ICacheService"/> and not <c>IFusionCacheSerializer</c>.</b>
/// FusionCache never passes the cache key to the registered serializer, so a serializer-level
/// decorator cannot derive key-bound associated data (AAD). This layer receives the key as an
/// explicit parameter on every member. See "Cache-value encryption rules" in
/// <c>02.Caching/CLAUDE.md</c>.
/// </para>
/// <para>
/// <b>Associated data is the UTF-8 bytes of the cache key — never tags</b>, which are not available
/// on the read path. Composed under <see cref="ITenantCacheService"/>, the key is the tenant key, so
/// the AAD is tenant-bound too.
/// </para>
/// <para>
/// <b>Stored shape.</b> The wrapped <see cref="ICacheService"/> stores a <see cref="T:byte[]"/> per key:
/// the <see cref="EncryptedPayload"/> storage format.
/// </para>
/// <para>
/// <b>Compression composition.</b> When Brotli compression is also enabled this decorator compresses
/// plaintext before encrypting, with the configured threshold and level, and decompresses after
/// decrypting, reusing <see cref="BrotliPayloadCodec"/>.
/// </para>
/// <para>
/// <b>Tamper/mismatched-AAD handling.</b> An entry that fails to decrypt is logged, evicted and
/// treated as a miss. <see cref="GetOrSetAsync{T}(string, Func{CacheFactoryContext, CancellationToken, ValueTask{T}}, CachePolicy, CancellationToken)"/>
/// then recomputes it through the wrapped service, so the recomputation keeps stampede protection.
/// </para>
/// </remarks>
internal sealed partial class EncryptedCacheService : ICacheService
{
    private readonly ICacheService _inner;
    private readonly ISymmetricEncryptionService _encryption;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly CacheCompressionOptions? _compression;
    private readonly ILogger<EncryptedCacheService> _logger;

    public EncryptedCacheService(
        ICacheService inner,
        ISymmetricEncryptionService encryption,
        JsonSerializerOptions jsonOptions,
        CacheCompressionOptions? compression,
        ILogger<EncryptedCacheService> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(encryption);
        ArgumentNullException.ThrowIfNull(jsonOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _encryption = encryption;
        _jsonOptions = jsonOptions;
        _compression = compression;
        _logger = logger;
    }

    public async ValueTask<CacheLookup<T>> TryGetAsync<T>(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        CacheLookup<byte[]> stored = await _inner.TryGetAsync<byte[]>(key, ct).ConfigureAwait(false);
        return await DecryptLookupAsync<T>(key, stored, ct).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyDictionary<string, CacheLookup<T>>> TryGetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        IReadOnlyDictionary<string, CacheLookup<byte[]>> stored =
            await _inner.TryGetManyAsync<byte[]>(keys, ct).ConfigureAwait(false);

        var result = new Dictionary<string, CacheLookup<T>>(stored.Count, StringComparer.Ordinal);
        foreach ((string key, CacheLookup<byte[]> lookup) in stored)
        {
            result[key] = await DecryptLookupAsync<T>(key, lookup, ct).ConfigureAwait(false);
        }

        return result;
    }

    public ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return GetOrSetAsync(key, (_, token) => factory(token), policy, ct);
    }

    public async ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CacheFactoryContext, CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(policy);

        Func<CacheFactoryContext, CancellationToken, ValueTask<byte[]>> encryptingFactory = async (context, token) =>
        {
            T value = await factory(context, token).ConfigureAwait(false);
            return await EncryptValueAsync(value, key, token).ConfigureAwait(false);
        };

        byte[] payload = await _inner.GetOrSetAsync(key, encryptingFactory, policy, ct).ConfigureAwait(false);
        Result<T> decrypted = await TryDecryptAsync<T>(payload, key, ct).ConfigureAwait(false);
        if (decrypted.IsSuccess)
            return decrypted.Value;

        // The entry does not authenticate (written under another key or configuration). Evict it
        // and recompute once through the wrapped service, which keeps stampede protection.
        await HandleDecryptFailureAsync(key, decrypted.Error, ct).ConfigureAwait(false);

        payload = await _inner.GetOrSetAsync(key, encryptingFactory, policy, ct).ConfigureAwait(false);
        decrypted = await TryDecryptAsync<T>(payload, key, ct).ConfigureAwait(false);
        if (decrypted.IsSuccess)
            return decrypted.Value;

        // Still unreadable: another writer keeps storing entries this process cannot decrypt.
        // Serve a fresh value without caching rather than fail the caller.
        await HandleDecryptFailureAsync(key, decrypted.Error, ct).ConfigureAwait(false);
        return await factory(new CacheFactoryContext(key, policy), ct).ConfigureAwait(false);
    }

    public async ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(policy);

        byte[] payload = await EncryptValueAsync(value, key, ct).ConfigureAwait(false);
        await _inner.SetAsync(key, payload, policy, ct).ConfigureAwait(false);
    }

    public async ValueTask SetManyAsync<T>(
        IReadOnlyDictionary<string, T> entries,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(policy);

        var encryptedEntries = new Dictionary<string, byte[]>(entries.Count, StringComparer.Ordinal);
        foreach ((string key, T value) in entries)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(entries));
            encryptedEntries[key] = await EncryptValueAsync(value, key, ct).ConfigureAwait(false);
        }

        await _inner.SetManyAsync(encryptedEntries, policy, ct).ConfigureAwait(false);
    }

    public ValueTask RemoveAsync(string key, CancellationToken ct = default) => _inner.RemoveAsync(key, ct);

    public ValueTask ExpireAsync(string key, CancellationToken ct = default) => _inner.ExpireAsync(key, ct);

    public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default) => _inner.RemoveByTagAsync(tag, ct);

    public ValueTask RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default) => _inner.RemoveByTagsAsync(tags, ct);

    public ValueTask ClearAsync(CancellationToken ct = default) => _inner.ClearAsync(ct);

    private async ValueTask<CacheLookup<T>> DecryptLookupAsync<T>(string key, CacheLookup<byte[]> stored, CancellationToken ct)
    {
        if (!stored.TryGetValue(out byte[]? payload) || payload is null)
            return CacheLookup<T>.Miss;

        Result<T> decrypted = await TryDecryptAsync<T>(payload, key, ct).ConfigureAwait(false);
        if (decrypted.IsSuccess)
            return CacheLookup<T>.Hit(decrypted.Value);

        await HandleDecryptFailureAsync(key, decrypted.Error, ct).ConfigureAwait(false);
        return CacheLookup<T>.Miss;
    }

    private async ValueTask<byte[]> EncryptValueAsync<T>(T value, string key, CancellationToken ct)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(value, _jsonOptions);

        if (_compression is not null)
            plaintext = BrotliPayloadCodec.Compress(plaintext, _compression.ThresholdBytes, _compression.Level);

        byte[] associatedData = Encoding.UTF8.GetBytes(key);
        EncryptedPayload payload = await _encryption.EncryptAsync(plaintext, associatedData, ct).ConfigureAwait(false);
        return payload.ToBytes();
    }

    private async ValueTask<Result<T>> TryDecryptAsync<T>(byte[] stored, string key, CancellationToken ct)
    {
        if (!EncryptedPayload.TryParse(stored, out EncryptedPayload? payload))
        {
            return Result<T>.Failure(Error.Validation(
                CryptographyErrorCodes.MalformedPayload, "The cached value is not an encrypted payload."));
        }

        byte[] associatedData = Encoding.UTF8.GetBytes(key);
        Result<byte[]> decryptResult = await _encryption.DecryptAsync(payload, associatedData, ct).ConfigureAwait(false);

        if (decryptResult.IsFailure)
            return Result<T>.Failure(decryptResult.Error);

        // Payloads below the threshold carry no marker and are returned unchanged.
        byte[] plaintext = _compression is not null
            ? BrotliPayloadCodec.Decompress(decryptResult.Value)
            : decryptResult.Value;

        T? value = JsonSerializer.Deserialize<T>(plaintext, _jsonOptions);
        return Result<T>.Success(value!);
    }

    private async ValueTask HandleDecryptFailureAsync(string key, Error error, CancellationToken ct)
    {
        Log.DecryptFailed(_logger, FusionCacheService.ExtractKeyPrefix(key), error.Code);

        try
        {
            await _inner.RemoveAsync(key, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort eviction: the entry is still treated as a miss, and a later read retries.
        }
    }

    // Source-generated log methods.
    private static partial class Log
    {
        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 15, Level = LogLevel.Warning,
            Message = "Cache entry for {KeyPrefix} failed to decrypt ({Reason}) — treating as a cache miss and evicting the corrupt entry.")]
        internal static partial void DecryptFailed(ILogger logger, string keyPrefix, string reason);
    }
}
