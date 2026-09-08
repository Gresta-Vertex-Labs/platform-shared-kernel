using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Serialization;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Caching.FusionCache.Encryption;

/// <summary>
/// An <see cref="ICacheService"/> decorator that applies opt-in AES-GCM authenticated encryption
/// to every cached value, keyed by the exact cache key each member receives — replacing Phase 42's
/// <c>CacheEncryptionSerializer</c> (Phase 46/WO-081).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this decorates <see cref="ICacheService"/> and not <c>IFusionCacheSerializer</c>.</b>
/// <c>IFusionCacheSerializer.Serialize&lt;T&gt;</c>/<c>Deserialize&lt;T&gt;</c> (and their async
/// counterparts) receive only the value — FusionCache never passes the cache key to the registered
/// serializer. A serializer-level decorator is therefore structurally incapable of deriving
/// key-bound associated data (AAD), no matter how it is written. This layer is the one place the
/// cache key is an explicit method parameter on every member, so AAD derivation is trivial,
/// deterministic, and needs no ambient state. See "Cache-value encryption rules" in
/// <c>02.Caching/CLAUDE.md</c> for the full structural finding.
/// </para>
/// <para>
/// <b>Associated data is the UTF-8 bytes of the cache key — never tags.</b>
/// <see cref="CachePolicy.WithTags(string[])"/> tags are available at <see cref="SetAsync{T}"/> time
/// but not at <see cref="GetAsync{T}"/> time (that member takes no <see cref="CachePolicy"/>
/// parameter), so a tag-inclusive AAD could not be reproduced at decrypt time. The cache key alone
/// is available, identical, on both the encrypt and decrypt paths.
/// </para>
/// <para>
/// <b>Exclusively async crypto.</b> Every member calls
/// <see cref="ISymmetricEncryptionService.EncryptAsync"/>/<see cref="ISymmetricEncryptionService.DecryptAsync"/>
/// — never the four synchronous members — regardless of which <c>IEncryptionKeyProvider</c> a
/// consuming service registers. This decorator never needs
/// <c>ISynchronousEncryptionKeyProvider</c>'s capability gate at all, because it never attempts the
/// synchronous path in the first place.
/// </para>
/// <para>
/// <b>Compression composition.</b> When both Brotli compression (<c>AddBrotliCompression()</c>) and
/// encryption (<c>AddCacheEncryption()</c>) are opted in, this decorator takes over compression duty
/// itself — compressing plaintext before encrypting it, and decompressing after decrypting — reusing
/// <see cref="BrotliPayloadCodec"/>, the exact codec <c>BrotliCacheSerializer</c> uses. Encryption
/// must see plaintext before compression can safely run, so compression can no longer happen inside
/// the serializer pipeline once this decorator is in the picture. <see cref="_compressionEnabled"/>
/// is captured once at construction (by <c>AddCacheEncryption()</c>) — never re-derived per call.
/// </para>
/// <para>
/// <b>Tamper/mismatched-AAD handling.</b> A decrypt failure (tamper, wrong key, or AAD/key
/// mismatch — indistinguishable from one another, by design) is treated as a cache miss: the corrupt
/// entry is logged and best-effort evicted so it does not fail identically on every subsequent read,
/// and the caller sees exactly what a genuine miss looks like. See <see cref="Log.DecryptFailed"/>.
/// </para>
/// </remarks>
internal sealed partial class EncryptedCacheService : ICacheService
{
    private readonly ICacheService _inner;
    private readonly ISymmetricEncryptionService _encryption;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly bool _compressionEnabled;
    private readonly ILogger<EncryptedCacheService> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="EncryptedCacheService"/>.
    /// </summary>
    /// <param name="inner">The wrapped, non-encrypting cache service.</param>
    /// <param name="encryption">Performs the actual AES-GCM encryption/decryption.</param>
    /// <param name="jsonOptions">
    /// The shared <see cref="JsonSerializerOptions"/> used to serialize/deserialize the cached
    /// value <c>T</c> to/from plaintext bytes — the same instance
    /// <c>AddSharedKernelCaching</c> constructs for FusionCache's own serializer (see
    /// <see cref="CacheSerializationOptions"/>), never re-derived here.
    /// </param>
    /// <param name="compressionEnabled">
    /// Whether Brotli compression was previously applied (via <c>AddBrotliCompression()</c>) and has
    /// now moved to this decorator. Captured once by <c>AddCacheEncryption()</c> at registration
    /// time — never re-derived per call.
    /// </param>
    /// <param name="logger">Used to log a decrypt-failure warning (<see cref="Log.DecryptFailed"/>).</param>
    public EncryptedCacheService(
        ICacheService inner,
        ISymmetricEncryptionService encryption,
        JsonSerializerOptions jsonOptions,
        bool compressionEnabled,
        ILogger<EncryptedCacheService> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(encryption);
        ArgumentNullException.ThrowIfNull(jsonOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _encryption = encryption;
        _jsonOptions = jsonOptions;
        _compressionEnabled = compressionEnabled;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        EncryptedPayload? payload = await _inner.GetAsync<EncryptedPayload>(key, ct).ConfigureAwait(false);
        if (payload is null)
            return default;

        Result<T> decrypted = await TryDecryptAsync<T>(payload, key, ct).ConfigureAwait(false);
        if (decrypted.IsSuccess)
            return decrypted.Value;

        await HandleDecryptFailureAsync(key, decrypted.Error, ct).ConfigureAwait(false);
        return default;
    }

    /// <inheritdoc />
    public async ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(policy);

        EncryptedPayload payload = await EncryptValueAsync(value, key, ct).ConfigureAwait(false);
        await _inner.SetAsync(key, payload, policy, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The caller's factory is wrapped into a fresh closure on every call, capturing only local
    /// state (<paramref name="key"/>'s derived AAD) — no ambient/<see cref="System.Threading.AsyncLocal{T}"/>
    /// context is used or needed. When FusionCache re-invokes this wrapped factory on a background
    /// continuation to satisfy <see cref="CachePolicy.WithEagerRefresh(double)"/>, it invokes the
    /// exact same closure the original caller's request created, so encryption behaves identically
    /// regardless of which execution context ultimately calls it.
    /// </remarks>
    public async ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(policy);

        Func<CancellationToken, ValueTask<EncryptedPayload>> wrappedFactory = async innerCt =>
        {
            T value = await factory(innerCt).ConfigureAwait(false);
            return await EncryptValueAsync(value, key, innerCt).ConfigureAwait(false);
        };

        EncryptedPayload payload = await _inner.GetOrSetAsync(key, wrappedFactory, policy, ct).ConfigureAwait(false);

        Result<T> decrypted = await TryDecryptAsync<T>(payload, key, ct).ConfigureAwait(false);
        if (decrypted.IsSuccess)
            return decrypted.Value;

        // The entry FusionCache returned (a stale hit written under a different key/config) fails
        // to authenticate — evict it and fall through to the factory exactly as a genuine miss
        // would, then persist the freshly computed value.
        await HandleDecryptFailureAsync(key, decrypted.Error, ct).ConfigureAwait(false);

        T freshValue = await factory(ct).ConfigureAwait(false);
        EncryptedPayload freshPayload = await EncryptValueAsync(freshValue, key, ct).ConfigureAwait(false);
        await _inner.SetAsync(key, freshPayload, policy, ct).ConfigureAwait(false);
        return freshValue;
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _inner.RemoveAsync(key, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Tags are opaque to this decorator and pass straight through — there is no cryptographic
    /// involvement in tag-based invalidation.
    /// </remarks>
    public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return _inner.RemoveByTagAsync(tag, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each key is decrypted independently with its own AAD — no batch-wide AAD, no cross-key
    /// optimization that would weaken the per-key binding. A decrypt failure for one key is handled
    /// exactly like <see cref="GetAsync{T}"/> and maps that key to <see langword="null"/>, matching
    /// the "every requested key has an entry" contract <see cref="ICacheService.GetManyAsync{T}"/>
    /// already documents.
    /// </remarks>
    public async ValueTask<IReadOnlyDictionary<string, T?>> GetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        IReadOnlyDictionary<string, EncryptedPayload?> encrypted =
            await _inner.GetManyAsync<EncryptedPayload>(keys, ct).ConfigureAwait(false);

        var result = new Dictionary<string, T?>(encrypted.Count);

        foreach ((string key, EncryptedPayload? payload) in encrypted)
        {
            if (payload is null)
            {
                result[key] = default;
                continue;
            }

            Result<T> decrypted = await TryDecryptAsync<T>(payload, key, ct).ConfigureAwait(false);
            if (decrypted.IsSuccess)
            {
                result[key] = decrypted.Value;
            }
            else
            {
                await HandleDecryptFailureAsync(key, decrypted.Error, ct).ConfigureAwait(false);
                result[key] = default;
            }
        }

        return result;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each entry is encrypted independently with its own key-derived AAD before the whole batch is
    /// delegated to the inner <see cref="ICacheService.SetManyAsync{T}"/> — no batch-wide AAD.
    /// </remarks>
    public async ValueTask SetManyAsync<T>(
        IReadOnlyDictionary<string, T> entries,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(policy);

        var encryptedEntries = new Dictionary<string, EncryptedPayload>(entries.Count);

        foreach ((string key, T value) in entries)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            encryptedEntries[key] = await EncryptValueAsync(value, key, ct).ConfigureAwait(false);
        }

        await _inner.SetManyAsync(encryptedEntries, policy, ct).ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Serializes <paramref name="value"/> to plaintext bytes, compresses them when
    /// <see cref="_compressionEnabled"/>, and encrypts the result with AAD derived from
    /// <paramref name="key"/>.
    /// </summary>
    private async ValueTask<EncryptedPayload> EncryptValueAsync<T>(T value, string key, CancellationToken ct)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(value, _jsonOptions);

        if (_compressionEnabled)
            plaintext = BrotliPayloadCodec.Compress(plaintext, thresholdBytes: 0, CompressionLevel.Fastest);

        byte[] associatedData = Encoding.UTF8.GetBytes(key);
        return await _encryption.EncryptAsync(plaintext, associatedData, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Decrypts <paramref name="payload"/> with AAD derived from <paramref name="key"/>, decompresses
    /// the result when <see cref="_compressionEnabled"/>, and deserializes it to <typeparamref name="T"/>.
    /// </summary>
    private async ValueTask<Result<T>> TryDecryptAsync<T>(EncryptedPayload payload, string key, CancellationToken ct)
    {
        byte[] associatedData = Encoding.UTF8.GetBytes(key);
        Result<byte[]> decryptResult = await _encryption.DecryptAsync(payload, associatedData, ct).ConfigureAwait(false);

        if (decryptResult.IsFailure)
            return Result<T>.Failure(decryptResult.Error);

        byte[] plaintext = _compressionEnabled
            ? BrotliPayloadCodec.Decompress(decryptResult.Value)
            : decryptResult.Value;

        T? value = JsonSerializer.Deserialize<T>(plaintext, _jsonOptions);
        return Result<T>.Success(value!);
    }

    /// <summary>
    /// Logs a decrypt-failure warning and best-effort evicts the corrupt entry so it does not fail
    /// identically on every subsequent read. Never lets an eviction failure propagate — the decrypt
    /// failure itself is already being treated as a cache miss regardless.
    /// </summary>
    private async ValueTask HandleDecryptFailureAsync(string key, Error error, CancellationToken ct)
    {
        Log.DecryptFailed(_logger, key, error.Message);

        try
        {
            await _inner.RemoveAsync(key, ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort eviction — the corrupt entry will still be treated as a miss on the
            // current call, and a future read will retry the same eviction.
        }
    }

    // Source-generated log methods.
    private static partial class Log
    {
        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 15, Level = LogLevel.Warning,
            Message = "Cache entry for key '{Key}' failed to decrypt ({Reason}) — treating as a cache miss and evicting the corrupt entry.")]
        internal static partial void DecryptFailed(ILogger logger, string key, string reason);
    }
}
