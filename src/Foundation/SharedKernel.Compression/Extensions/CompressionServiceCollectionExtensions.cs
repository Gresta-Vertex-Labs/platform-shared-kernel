using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Compression.Options;
using SharedKernel.Configuration.Extensions;

namespace SharedKernel.Compression.Extensions;

/// <summary>
/// DI extension methods for registering <c>SharedKernel.Compression</c> services.
/// </summary>
public static class CompressionServiceCollectionExtensions
{
    /// <summary>
    /// Key for the framed <see cref="BrotliPayloadCompressor"/> — the same instance shape as the
    /// unkeyed default, addressable by name.
    /// </summary>
    public const string BrotliPayloadCompressorKey = "Brotli";

    /// <summary>Key for the framed <see cref="GZipPayloadCompressor"/>.</summary>
    /// <remarks>There is no unkeyed registration for gzip; resolve it by this key.</remarks>
    public const string GZipPayloadCompressorKey = "GZip";

    /// <summary>
    /// Key for the <see cref="BrotliPayloadCompressor"/> that emits a bare Brotli stream with no
    /// platform frame.
    /// </summary>
    /// <remarks>
    /// Reading a payload back through a raw compressor cannot detect truncation — see
    /// <see cref="CompressionFraming.Raw"/>. Use it only where an external system dictates the bytes.
    /// </remarks>
    public const string RawBrotliPayloadCompressorKey = "Brotli.Raw";

    /// <summary>
    /// Key for the <see cref="GZipPayloadCompressor"/> that emits a bare gzip stream with no platform
    /// frame — an ordinary <c>.gz</c> body any standard tool can read.
    /// </summary>
    /// <remarks>This is the registration to use for gzip interop; see <see cref="CompressionFraming.Raw"/>.</remarks>
    public const string RawGZipPayloadCompressorKey = "GZip.Raw";

    /// <summary>
    /// Registers <see cref="CompressionOptions"/> (validated, eagerly checked at startup) and the
    /// <see cref="IPayloadCompressor"/> implementations as singletons.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">
    /// The root <see cref="IConfiguration"/>. <see cref="CompressionOptions"/> binds from the section it
    /// declares through <see cref="SharedKernel.Configuration.ISectionBoundOptions"/>, so no call site
    /// names the path.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>What gets registered:</para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// Unkeyed <see cref="IPayloadCompressor"/> — framed Brotli. The right default for anything this
    /// platform writes and reads back.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="BrotliPayloadCompressorKey"/> and <see cref="GZipPayloadCompressorKey"/> — the framed
    /// compressors, addressable by name.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="RawBrotliPayloadCompressorKey"/> and <see cref="RawGZipPayloadCompressorKey"/> — the
    /// unframed compressors, for interop with systems outside this platform only.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// Every registration uses <c>TryAdd</c>, so calling this more than once never double-registers and
    /// a consumer registration made <b>before</b> this call wins over the platform default.
    /// </para>
    /// <para>
    /// A payload can only be read back by a compressor matching the one that wrote it, so record which
    /// key produced a stored payload. A framed payload carries its own algorithm, so a mismatch fails
    /// loudly rather than returning wrong bytes.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelCompression(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<CompressionOptions>(configuration);

        services.TryAddSingleton<IPayloadCompressor>(sp => Brotli(sp, CompressionFraming.Framed));
        services.TryAddKeyedSingleton<IPayloadCompressor>(
            BrotliPayloadCompressorKey,
            (sp, _) => Brotli(sp, CompressionFraming.Framed));
        services.TryAddKeyedSingleton<IPayloadCompressor>(
            GZipPayloadCompressorKey,
            (sp, _) => GZip(sp, CompressionFraming.Framed));
        services.TryAddKeyedSingleton<IPayloadCompressor>(
            RawBrotliPayloadCompressorKey,
            (sp, _) => Brotli(sp, CompressionFraming.Raw));
        services.TryAddKeyedSingleton<IPayloadCompressor>(
            RawGZipPayloadCompressorKey,
            (sp, _) => GZip(sp, CompressionFraming.Raw));

        return services;
    }

    private static BrotliPayloadCompressor Brotli(IServiceProvider services, CompressionFraming framing) =>
        new(services.GetRequiredService<IOptions<CompressionOptions>>(), framing);

    private static GZipPayloadCompressor GZip(IServiceProvider services, CompressionFraming framing) =>
        new(services.GetRequiredService<IOptions<CompressionOptions>>(), framing);
}
