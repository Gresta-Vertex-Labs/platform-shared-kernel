using System.IO.Compression;
using Microsoft.Extensions.Options;
using SharedKernel.Compression.Options;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Compression;

/// <summary>
/// Compresses and decompresses byte payloads using Brotli (<see cref="BrotliStream"/>) — the
/// default, best-ratio choice for the JSON/text-shaped payloads this platform mostly moves.
/// </summary>
/// <remarks>
/// Registered by <see cref="Extensions.CompressionServiceCollectionExtensions.AddSharedKernelCompression"/>
/// as both the unkeyed <see cref="IPayloadCompressor"/> default and the "Brotli"-keyed singleton.
/// Stateless and thread-safe.
/// </remarks>
public sealed class BrotliPayloadCompressor : IPayloadCompressor
{
    private readonly CompressionLevel _level;

    /// <summary>Creates a new <see cref="BrotliPayloadCompressor"/>.</summary>
    /// <param name="options">Supplies the <see cref="CompressionOptions.Level"/> to compress with.</param>
    public BrotliPayloadCompressor(IOptions<CompressionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _level = options.Value.Level;
    }

    /// <inheritdoc />
    public byte[] Compress(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, _level, leaveOpen: true))
        {
            brotli.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    /// <inheritdoc />
    public void Compress(Stream input, Stream output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        using var brotli = new BrotliStream(output, _level, leaveOpen: true);
        input.CopyTo(brotli);
    }

    /// <inheritdoc />
    public async Task CompressAsync(Stream input, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        var brotli = new BrotliStream(output, _level, leaveOpen: true);
        await using (brotli.ConfigureAwait(false))
        {
            await input.CopyToAsync(brotli, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Result<byte[]> Decompress(byte[] compressed)
    {
        ArgumentNullException.ThrowIfNull(compressed);

        try
        {
            using var input = new MemoryStream(compressed);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress, leaveOpen: true);
            using var output = new MemoryStream();
            brotli.CopyTo(output);
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
            using var brotli = new BrotliStream(input, CompressionMode.Decompress, leaveOpen: true);
            brotli.CopyTo(output);
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
            var brotli = new BrotliStream(input, CompressionMode.Decompress, leaveOpen: true);
            await using (brotli.ConfigureAwait(false))
            {
                await brotli.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
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
