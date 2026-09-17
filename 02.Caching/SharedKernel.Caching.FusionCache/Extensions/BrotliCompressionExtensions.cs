using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Serialization;
using ZiggyCreatures.Caching.Fusion.Serialization;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace SharedKernel.Caching.FusionCache.Extensions;

/// <summary>
/// <see cref="ICachingBuilder"/> extension methods for Brotli compression of distributed cache entries.
/// </summary>
public static class BrotliCompressionExtensions
{
    /// <summary>
    /// Compresses distributed cache entries at or above <see cref="CacheCompressionOptions.ThresholdBytes"/>
    /// with Brotli before they are written to the distributed layer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call after <c>AddSharedKernelCaching</c> and before <c>AddCacheEncryption</c>. Entries written
    /// before compression was enabled, and entries below the threshold, stay readable: compressed
    /// payloads carry a marker and anything else is read as it is. Memory-cache entries are never compressed.
    /// </para>
    /// <para>
    /// Compression pays off for large, repetitive values such as JSON documents or lists; for small
    /// values the threshold keeps the CPU cost away.
    /// </para>
    /// </remarks>
    /// <param name="builder">The caching builder.</param>
    /// <param name="configure">Optional changes to the threshold and level.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><see cref="CacheCompressionOptions.ThresholdBytes"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException"><c>AddCacheEncryption</c> was already called; compression must be registered first so values are compressed before they are encrypted.</exception>
    /// <example>
    /// <code>
    /// services.AddSharedKernelCaching(o => o.ServiceName = "catalog")
    ///         .AddRedisL2(connectionString)
    ///         .AddBrotliCompression(o => o.ThresholdBytes = 2048);
    /// </code>
    /// </example>
    public static ICachingBuilder AddBrotliCompression(
        this ICachingBuilder builder,
        Action<CacheCompressionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Services.Any(sd => sd.ServiceType == typeof(CacheEncryptionMarker)))
        {
            throw new InvalidOperationException(
                "Brotli compression must be registered before cache encryption — call "
                    + "AddBrotliCompression() before AddCacheEncryption(), never after.");
        }

        var options = new CacheCompressionOptions();
        configure?.Invoke(options);

        if (options.ThresholdBytes <= 0)
        {
            throw new ArgumentException("CacheCompressionOptions.ThresholdBytes must be greater than zero.", nameof(configure));
        }

        // Decorate the concrete serializer registered by AddSharedKernelCaching; resolving the concrete
        // type, not the interface, avoids resolving this factory from inside itself.
        builder.Services.Replace(ServiceDescriptor.Singleton<IFusionCacheSerializer>(sp =>
            new BrotliCacheSerializer(sp.GetRequiredService<FusionCacheSystemTextJsonSerializer>(), options)));

        return builder;
    }
}
