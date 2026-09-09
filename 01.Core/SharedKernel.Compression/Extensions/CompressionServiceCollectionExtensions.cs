using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Compression.Options;
using SharedKernel.Configuration.Extensions;

namespace SharedKernel.Compression.Extensions;

/// <summary>
/// DI extension methods for registering <c>SharedKernel.Compression</c> services.
/// </summary>
public static class CompressionServiceCollectionExtensions
{
    /// <summary>
    /// The keyed-service key under which <see cref="BrotliPayloadCompressor"/> is additionally
    /// registered for <see cref="IPayloadCompressor"/>, alongside its unkeyed default registration.
    /// </summary>
    public const string BrotliPayloadCompressorKey = "Brotli";

    /// <summary>
    /// The keyed-service key under which <see cref="GZipPayloadCompressor"/> is registered for
    /// <see cref="IPayloadCompressor"/>. There is no unkeyed registration for this implementation.
    /// </summary>
    public const string GZipPayloadCompressorKey = "GZip";

    /// <summary>
    /// Registers <see cref="CompressionOptions"/> (validated, eagerly checked at startup via
    /// <c>ValidateOnStart()</c>) and the two stateless, thread-safe <see cref="IPayloadCompressor"/>
    /// implementations — <see cref="BrotliPayloadCompressor"/> and <see cref="GZipPayloadCompressor"/>
    /// — as singletons.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">
    /// The root <see cref="IConfiguration"/>. <see cref="CompressionOptions"/> is bound from the
    /// <see cref="CompressionOptions.SectionName"/> section.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="BrotliPayloadCompressor"/> is registered as both the unkeyed
    /// <see cref="IPayloadCompressor"/> default and the <see cref="BrotliPayloadCompressorKey"/>
    /// ("Brotli") keyed singleton. <see cref="GZipPayloadCompressor"/> is registered only as the
    /// <see cref="GZipPayloadCompressorKey"/> ("GZip") keyed singleton — resolve it explicitly via
    /// <c>provider.GetRequiredKeyedService&lt;IPayloadCompressor&gt;(GZipPayloadCompressorKey)</c>.
    /// This mirrors <c>SharedKernel.Cryptography</c>'s <c>EcdsaSignatureService</c> keyed-only
    /// registration pattern.
    /// </para>
    /// <para>
    /// Every registration uses <c>TryAddSingleton</c>/<c>TryAddKeyedSingleton</c> — calling this
    /// method more than once never double-registers, and a consumer registration made
    /// <b>before</b> this call always wins over the platform default.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelCompression(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<CompressionOptions>(
            configuration.GetSection(CompressionOptions.SectionName));

        services.TryAddSingleton<IPayloadCompressor, BrotliPayloadCompressor>();
        services.TryAddKeyedSingleton<IPayloadCompressor, BrotliPayloadCompressor>(BrotliPayloadCompressorKey);
        services.TryAddKeyedSingleton<IPayloadCompressor, GZipPayloadCompressor>(GZipPayloadCompressorKey);

        return services;
    }
}
