using SharedKernel.Caching.FusionCache.Extensions;
using ZiggyCreatures.Caching.Fusion.Serialization;

namespace SharedKernel.Caching.FusionCache.Serialization;

/// <summary>
/// A decorator over <see cref="IFusionCacheSerializer"/> that applies opt-in Brotli compression
/// to serialized payloads before they are written to the L2 Redis distributed cache.
/// </summary>
/// <remarks>
/// <para>
/// Payloads at or above <see cref="CacheCompressionOptions.ThresholdBytes"/> are
/// compressed with Brotli and prefixed with the two-byte marker <c>0x42 0x52</c> ("BR"). On read,
/// the marker is detected and the payload decompressed before the inner serializer sees it. The
/// codec (<see cref="BrotliPayloadCodec"/>) is shared with <c>Encryption.EncryptedCacheService</c>.
/// </para>
/// <para>
/// Payloads below the threshold, and any payloads written before compression was enabled, are
/// forwarded directly to the inner serializer without modification — ensuring full
/// backward-compatibility with existing cached data.
/// </para>
/// <para>
/// L1 in-process cache entries are never affected by this serializer; it only executes on the
/// L2 Redis path.
/// </para>
/// <para>
/// Register via <c>ICachingBuilder.AddBrotliCompression()</c> at startup.
/// </para>
/// </remarks>
internal sealed class BrotliCacheSerializer : IFusionCacheSerializer
{
    private readonly IFusionCacheSerializer _inner;
    private readonly CacheCompressionOptions _options;

    /// <summary>
    /// Initialises a new instance of <see cref="BrotliCacheSerializer"/>.
    /// </summary>
    /// <param name="inner">
    /// The base serializer to delegate to. Must not be <see langword="null"/>.
    /// This should be the STJ serializer registered by <c>AddSharedKernelCaching</c>.
    /// </param>
    /// <param name="options">
    /// Compression options controlling the threshold and compression level.
    /// </param>
    internal BrotliCacheSerializer(
        IFusionCacheSerializer inner,
        CacheCompressionOptions options)
    {
        _inner = inner;
        _options = options;
    }

    // AddCacheEncryption unwraps compression from the serializer and moves it into
    // EncryptedCacheService, which must compress plaintext before encrypting it.
    public IFusionCacheSerializer Inner => _inner;

    public CacheCompressionOptions Options => _options;

    /// <inheritdoc />
    public byte[] Serialize<T>(T? obj)
    {
        var payload = _inner.Serialize(obj);
        return BrotliPayloadCodec.Compress(payload, _options.ThresholdBytes, _options.Level);
    }

    /// <inheritdoc />
    public T? Deserialize<T>(byte[] data)
    {
        var decompressed = BrotliPayloadCodec.Decompress(data);
        return _inner.Deserialize<T>(decompressed);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]> SerializeAsync<T>(T? obj, CancellationToken token = default)
    {
        var payload = await _inner.SerializeAsync(obj, token).ConfigureAwait(false);
        return BrotliPayloadCodec.Compress(payload, _options.ThresholdBytes, _options.Level);
    }

    /// <inheritdoc />
    public async ValueTask<T?> DeserializeAsync<T>(byte[] data, CancellationToken token = default)
    {
        var decompressed = BrotliPayloadCodec.Decompress(data);
        return await _inner.DeserializeAsync<T>(decompressed, token).ConfigureAwait(false);
    }
}
