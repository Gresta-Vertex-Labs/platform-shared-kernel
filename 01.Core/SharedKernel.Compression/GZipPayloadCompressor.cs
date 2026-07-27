using System.IO.Compression;
using Microsoft.Extensions.Options;
using SharedKernel.Compression.Options;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Compression;

/// <summary>
/// Compresses and decompresses byte payloads using gzip (<see cref="GZipStream"/>) — a keyed
/// alternate to <see cref="BrotliPayloadCompressor"/> for interop with systems that specifically
/// require the gzip format.
/// </summary>
/// <remarks>
/// Registered by <see cref="Extensions.CompressionServiceCollectionExtensions.AddSharedKernelCompression"/>
/// only as the "GZip"-keyed singleton — mirrors <c>SharedKernel.Cryptography</c>'s
/// <c>EcdsaSignatureService</c> keyed-only registration; there is no unkeyed registration for this
/// type. Stateless and thread-safe.
/// </remarks>
public sealed class GZipPayloadCompressor : IPayloadCompressor
{
    private readonly CompressionLevel _level;

    /// <summary>Creates a new <see cref="GZipPayloadCompressor"/>.</summary>
    /// <param name="options">Supplies the <see cref="CompressionOptions.Level"/> to compress with.</param>
    public GZipPayloadCompressor(IOptions<CompressionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _level = options.Value.Level;
    }

    /// <inheritdoc />
    public byte[] Compress(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, _level, leaveOpen: true))
        {
            gzip.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    /// <inheritdoc />
    public void Compress(Stream input, Stream output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        using var gzip = new GZipStream(output, _level, leaveOpen: true);
        input.CopyTo(gzip);
    }

    /// <inheritdoc />
    public async Task CompressAsync(Stream input, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        var gzip = new GZipStream(output, _level, leaveOpen: true);
        await using (gzip.ConfigureAwait(false))
        {
            await input.CopyToAsync(gzip, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Result<byte[]> Decompress(byte[] compressed)
    {
        ArgumentNullException.ThrowIfNull(compressed);

        try
        {
            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            return DecompressionFailedError();
        }
    }

    /// <inheritdoc />
    public Result Decompress(Stream input, Stream output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        try
        {
            using var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
            gzip.CopyTo(output);
            return Result.Success();
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            return DecompressionFailedError();
        }
    }

    /// <inheritdoc />
    public async Task<Result> DecompressAsync(Stream input, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        try
        {
            var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
            await using (gzip.ConfigureAwait(false))
            {
                await gzip.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            return DecompressionFailedError();
        }
    }

    private static Error DecompressionFailedError() =>
        Error.Unexpected(
            CompressionErrorCodes.DecompressionFailed,
            "Decompression failed: the compressed payload is corrupt or truncated.");
}
