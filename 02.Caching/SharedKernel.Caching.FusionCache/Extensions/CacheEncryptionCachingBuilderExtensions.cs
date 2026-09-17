using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Encryption;
using SharedKernel.Caching.FusionCache.Serialization;
using SharedKernel.Cryptography.Symmetric;
using ZiggyCreatures.Caching.Fusion.Serialization;

namespace SharedKernel.Caching.FusionCache.Extensions;

/// <summary>
/// <see cref="ICachingBuilder"/> extension methods for opt-in AES-GCM encryption of cached values.
/// </summary>
public static class CacheEncryptionCachingBuilderExtensions
{
    /// <summary>
    /// Encrypts every cached value with AES-GCM before it is stored, in both cache layers, and
    /// decrypts it on read. The cache key is the associated data, so an entry cannot be replayed under
    /// another key or another tenant's key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use it when cached values are sensitive and the cache (typically Redis) is shared or managed by
    /// others. TLS already protects values in transit; this protects them at rest in the cache.
    /// </para>
    /// <para>
    /// Call it last, after <c>AddBrotliCompression</c> when both are used: values are then compressed
    /// before they are encrypted, with the configured threshold and level. An entry that fails to
    /// decrypt (tampered, or written under another key) is removed and treated as a miss.
    /// </para>
    /// <para>
    /// Changing to or from encryption changes the stored format, so existing distributed entries are
    /// recomputed once after the deployment.
    /// </para>
    /// </remarks>
    /// <param name="builder">The caching builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="ISymmetricEncryptionService"/> is not registered (call
    /// <c>AddSharedKernelCryptography(configuration).AddSymmetricEncryption()</c> first), or
    /// <c>AddSharedKernelCaching</c> has not been called.
    /// </exception>
    /// <example>
    /// <code>
    /// services.AddSingleton&lt;IEncryptionKeyProvider&gt;(keyProvider);
    /// services.AddSharedKernelCryptography(configuration).AddSymmetricEncryption();
    /// services.AddRedisConnection(configuration);
    /// services.AddSharedKernelCaching(o =&gt; o.ServiceName = "payments")
    ///         .AddRedisL2()
    ///         .AddBrotliCompression()
    ///         .AddCacheEncryption();
    /// </code>
    /// </example>
    public static ICachingBuilder AddCacheEncryption(this ICachingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!builder.Services.Any(sd => sd.ServiceType == typeof(ISymmetricEncryptionService)))
        {
            throw new InvalidOperationException(
                "AddCacheEncryption requires ISymmetricEncryptionService to be registered first. "
                    + "Register an IEncryptionKeyProvider and call services.AddSharedKernelCryptography(configuration).AddSymmetricEncryption() "
                    + "(SharedKernel.Cryptography, 01.Core) before AddCacheEncryption().");
        }

        ServiceDescriptor? existingCacheService = builder.Services.LastOrDefault(
            sd => sd.ServiceType == typeof(ICacheService));

        if (existingCacheService is null)
        {
            throw new InvalidOperationException(
                "AddCacheEncryption requires an ICacheService to already be registered. "
                    + "Call AddSharedKernelCaching() before AddCacheEncryption().");
        }

        ServiceDescriptor? existingSerializer = builder.Services.LastOrDefault(
            sd => sd.ServiceType == typeof(IFusionCacheSerializer));

        if (existingSerializer is null)
        {
            throw new InvalidOperationException(
                "AddCacheEncryption requires an IFusionCacheSerializer to already be registered. "
                    + "Call AddSharedKernelCaching() before AddCacheEncryption().");
        }

        // Capture how to resolve the currently-registered ICacheService and IFusionCacheSerializer
        // BEFORE either is replaced below, so the new factories can wrap/inspect them without
        // recursing back into themselves. Reused for both replacements — see ResolveExistingFactory.
        Func<IServiceProvider, object> resolveExistingCache = ResolveExistingFactory(existingCacheService);
        Func<IServiceProvider, object> resolveExistingSerializer = ResolveExistingFactory(existingSerializer);

        // Unwrap Brotli compression from the registered IFusionCacheSerializer when present — once
        // EncryptedCacheService takes over compression duty, FusionCache's own serializer must go
        // back to the plain (uncompressed) serializer, or a highly-compressible plaintext would be
        // compressed once by EncryptedCacheService and then pointlessly re-attempted a second time
        // by FusionCache against already-encrypted (incompressible) bytes.
        builder.Services.Replace(ServiceDescriptor.Singleton<IFusionCacheSerializer>(sp =>
        {
            var current = (IFusionCacheSerializer)resolveExistingSerializer(sp);
            return current is BrotliCacheSerializer brotli ? brotli.Inner : current;
        }));

        builder.Services.Replace(ServiceDescriptor.Singleton<ICacheService>(sp =>
        {
            var innerCache = (ICacheService)resolveExistingCache(sp);
            var encryptionService = sp.GetRequiredService<ISymmetricEncryptionService>();
            var jsonOptions = sp.GetRequiredService<CacheSerializationOptions>().Value;
            var logger = sp.GetRequiredService<ILogger<EncryptedCacheService>>();

            // Compression moves here when it was configured, so plaintext is compressed before it is
            // encrypted, with the same threshold and level.
            var originalSerializer = (IFusionCacheSerializer)resolveExistingSerializer(sp);
            CacheCompressionOptions? compression = (originalSerializer as BrotliCacheSerializer)?.Options;

            return new EncryptedCacheService(innerCache, encryptionService, jsonOptions, compression, logger);
        }));

        // Lets AddBrotliCompression detect that encryption is already in place and refuse to run after it.
        builder.Services.TryAddSingleton(new CacheEncryptionMarker());

        return builder;
    }

    /// <summary>
    /// Builds a factory that reproduces whatever <paramref name="descriptor"/> would have resolved
    /// to, regardless of whether it was registered as an instance, a factory, or a type.
    /// </summary>
    private static Func<IServiceProvider, object> ResolveExistingFactory(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is not null)
        {
            object instance = descriptor.ImplementationInstance;
            return _ => instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return descriptor.ImplementationFactory;
        }

        Type implementationType = descriptor.ImplementationType
            ?? throw new InvalidOperationException(
                $"Unable to resolve the currently-registered {descriptor.ServiceType.Name} implementation.");

        return sp => ActivatorUtilities.CreateInstance(sp, implementationType);
    }
}
