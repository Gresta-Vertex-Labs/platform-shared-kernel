using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Serialization;
using ZiggyCreatures.Caching.Fusion.Serialization;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace SharedKernel.Caching.FusionCache.Extensions;

/// <summary>
/// <see cref="ICachingBuilder"/> extension methods for enabling opt-in Brotli compression on the
/// L2 Redis distributed cache path.
/// </summary>
public static class BrotliCompressionExtensions
{
    /// <summary>
    /// Wraps the registered <see cref="IFusionCacheSerializer"/> with
    /// <see cref="BrotliCacheSerializer"/> so that payloads at or above the configured threshold
    /// are compressed with Brotli before being written to the L2 Redis cache.
    /// </summary>
    /// <param name="builder">The <see cref="ICachingBuilder"/> to configure.</param>
    /// <param name="configure">
    /// Optional delegate to customise <see cref="CachingOptions.CompressionOptions"/>.
    /// When <see langword="null"/> the defaults are used
    /// (<see cref="CachingOptions.CompressionOptions.L2ThresholdBytes"/> = 1024,
    /// <see cref="System.IO.Compression.CompressionLevel.Fastest"/>).
    /// </param>
    /// <returns>The same <see cref="ICachingBuilder"/> to allow further chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="CachingOptions.CompressionOptions.L2ThresholdBytes"/> is not
    /// greater than zero after applying <paramref name="configure"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Must be called <em>after</em> <c>AddSharedKernelCaching</c> so that the base
    /// <see cref="IFusionCacheSerializer"/> is already present in the service collection for
    /// replacement.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// services.AddSharedKernelCaching(o => { o.ServiceName = "my-service"; })
    ///         .AddRedisL2(connectionString)
    ///         .AddBrotliCompression(o => { o.L2ThresholdBytes = 2048; });
    /// </code>
    /// </para>
    /// </remarks>
    public static ICachingBuilder AddBrotliCompression(
        this ICachingBuilder builder,
        Action<CachingOptions.CompressionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var opts = new CachingOptions.CompressionOptions();
        configure?.Invoke(opts);

        if (opts.L2ThresholdBytes <= 0)
        {
            throw new ArgumentException(
                "CompressionOptions.L2ThresholdBytes must be greater than zero.",
                nameof(configure));
        }

        // Mark as enabled since the extension was explicitly called.
        opts.Enabled = true;

        // Replace the IFusionCacheSerializer registration with a BrotliCacheSerializer that
        // decorates the concrete FusionCacheSystemTextJsonSerializer registered by
        // AddSharedKernelCaching. Resolving the concrete type (not the interface) avoids a
        // circular DI dependency. FusionCache resolves its serializer from DI via
        // WithRegisteredSerializer(), so this replacement is picked up automatically.
        builder.Services.Replace(ServiceDescriptor.Singleton<IFusionCacheSerializer>(sp =>
        {
            var inner = sp.GetRequiredService<FusionCacheSystemTextJsonSerializer>();
            return new BrotliCacheSerializer(inner, opts);
        }));

        return builder;
    }
}
