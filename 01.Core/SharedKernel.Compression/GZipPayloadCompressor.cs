using System.Buffers;
using Microsoft.Extensions.Options;
using SharedKernel.Compression.Internal;
using SharedKernel.Compression.Options;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Compression;

/// <summary>
/// Compresses and decompresses payloads using gzip (<see cref="System.IO.Compression.GZipStream"/>)
/// — the keyed alternate to <see cref="BrotliPayloadCompressor"/>, for interop with systems that
/// specifically require the gzip format.
/// </summary>
/// <remarks>
/// <para>
/// Registered by <see cref="Extensions.CompressionServiceCollectionExtensions.AddSharedKernelCompression"/>
/// under two keys, framed and raw — never as the unkeyed default, mirroring
/// <c>SharedKernel.Cryptography</c>'s keyed-only <c>EcdsaSignatureService</c>. Stateless and
/// thread-safe. All behaviour is documented on <see cref="IPayloadCompressor"/>.
/// </para>
/// <para>
/// <b>For external interop, use the raw registration.</b> Framed output carries a 13-byte platform
/// header, so it is not a gzip file any standard tool can open — which defeats the only reason to
/// choose gzip over Brotli here. Brotli compresses this platform's payloads better: measured on 283 KB
/// of JSON, 15.6 KB with Brotli against 35.0 KB with gzip. gzip also inflates very small payloads
/// noticeably, turning 1 byte into 21.
/// </para>
/// </remarks>
public sealed class GZipPayloadCompressor : IPayloadCompressor
{
    private readonly CodecSettings _settings;

    /// <summary>Creates a new <see cref="GZipPayloadCompressor"/>.</summary>
    /// <param name="options">
    /// Supplies <see cref="CompressionOptions.Level"/> and
    /// <see cref="CompressionOptions.MaxDecompressedSize"/>.
    /// </param>
    /// <param name="framing">
    /// Whether to wrap output in the platform's frame. Defaults to
    /// <see cref="CompressionFraming.Framed"/>, which is what makes a truncated payload detectable;
    /// pass <see cref="CompressionFraming.Raw"/> only for interop with a system outside this platform.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="framing"/> is not a defined value.</exception>
    public GZipPayloadCompressor(
        IOptions<CompressionOptions> options,
        CompressionFraming framing = CompressionFraming.Framed)
    {
        ArgumentNullException.ThrowIfNull(options);
        _settings = CodecSettingsFactory.Create(CompressionAlgorithm.GZip, framing, options.Value);
    }

    /// <inheritdoc />
    public CompressionAlgorithm Algorithm => _settings.Algorithm;

    /// <inheritdoc />
    public CompressionFraming Framing => _settings.Framing;

    /// <inheritdoc />
    public byte[] Compress(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return CompressionCodec.Compress(_settings, data);
    }

    /// <inheritdoc />
    public byte[] Compress(ReadOnlySpan<byte> data) => CompressionCodec.Compress(_settings, data);

    /// <inheritdoc />
    public void Compress(ReadOnlySpan<byte> data, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        CompressionCodec.Compress(_settings, data, output);
    }

    /// <inheritdoc />
    public void Compress(Stream input, Stream output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        CompressionCodec.Compress(_settings, input, output);
    }

    /// <inheritdoc />
    public ValueTask CompressAsync(Stream input, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        return CompressionCodec.CompressAsync(_settings, input, output, cancellationToken);
    }

    /// <inheritdoc />
    public Result<byte[]> Decompress(byte[] compressed)
    {
        ArgumentNullException.ThrowIfNull(compressed);
        return CompressionCodec.Decompress(_settings, compressed);
    }

    /// <inheritdoc />
    public Result<byte[]> Decompress(ReadOnlySpan<byte> compressed) => CompressionCodec.Decompress(_settings, compressed);

    /// <inheritdoc />
    public Result Decompress(ReadOnlySpan<byte> compressed, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return CompressionCodec.Decompress(_settings, compressed, output);
    }

    /// <inheritdoc />
    public Result Decompress(Stream input, Stream output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        return CompressionCodec.Decompress(_settings, input, output);
    }

    /// <inheritdoc />
    public ValueTask<Result> DecompressAsync(Stream input, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        return CompressionCodec.DecompressAsync(_settings, input, output, cancellationToken);
    }
}
