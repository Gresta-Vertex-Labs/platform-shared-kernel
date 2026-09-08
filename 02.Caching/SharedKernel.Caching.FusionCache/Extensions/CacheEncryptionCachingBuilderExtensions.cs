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
/// <see cref="ICachingBuilder"/> extension methods for enabling opt-in AES-GCM encryption of
/// cached values.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 46/WO-081:</b> this method now wraps the registered <see cref="ICacheService"/> with
/// <see cref="EncryptedCacheService"/> — not the registered <c>IFusionCacheSerializer</c> with the
/// (now retired) <c>CacheEncryptionSerializer</c>. See <see cref="EncryptedCacheService"/>'s own
/// remarks, and "Cache-value encryption rules" in <c>02.Caching/CLAUDE.md</c>, for the structural
/// reason: <c>IFusionCacheSerializer</c> never receives the cache key, so a serializer-level
/// decorator cannot derive key-bound associated data (AAD) — only an <see cref="ICacheService"/>-level
/// decorator can.
/// </para>
/// </remarks>
public static class CacheEncryptionCachingBuilderExtensions
{
    /// <summary>
    /// Wraps the currently-registered <see cref="ICacheService"/> with
    /// <see cref="EncryptedCacheService"/>, so that every cached value is AES-GCM encrypted with
    /// associated data derived from its cache key before being written to L1/L2, and transparently
    /// decrypted (with the same key-derived AAD) on read.
    /// </summary>
    /// <param name="builder">The <see cref="ICachingBuilder"/> to configure.</param>
    /// <returns>The same <see cref="ICachingBuilder"/> to allow further chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="ISymmetricEncryptionService"/> is not already registered (call
    /// <c>AddSharedKernelCryptography()</c> from <c>01.Core/SharedKernel.Cryptography</c> first),
    /// or when no <see cref="ICacheService"/> is registered yet (call <c>AddSharedKernelCaching()</c>
    /// first).
    /// </exception>
    /// <remarks>
    /// <para>
    /// Must be called <em>after</em> <c>AddBrotliCompression()</c> when both are used. When the
    /// currently-registered <c>IFusionCacheSerializer</c> is a <c>BrotliCacheSerializer</c>, this
    /// method unwraps it back to its inner serializer (compression duty moves to
    /// <see cref="EncryptedCacheService"/>, which compresses plaintext before encrypting it and
    /// decompresses after decrypting) — producing compress-then-encrypt on write and
    /// decrypt-then-decompress on read, identical to Phase 42's guarantee. Calling
    /// <c>AddBrotliCompression()</c> after this method has already been called still throws
    /// <see cref="InvalidOperationException"/> — that guard is unchanged.
    /// </para>
    /// <para>
    /// Disabled by default — <c>AddSharedKernelCaching</c>/<c>AddRedisL2</c> behavior is unchanged
    /// unless this method is explicitly called.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// services.AddSharedKernelCryptography(configuration);
    /// services.AddSharedKernelCaching(o => { o.ServiceName = "my-service"; })
    ///         .AddRedisL2(connectionString)
    ///         .AddBrotliCompression(o => { o.L2ThresholdBytes = 2048; })   // compression first (innermost)
    ///         .AddCacheEncryption();                                       // encryption last (outermost)
    /// </code>
    /// </para>
    /// </remarks>
    public static ICachingBuilder AddCacheEncryption(this ICachingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!builder.Services.Any(sd => sd.ServiceType == typeof(ISymmetricEncryptionService)))
        {
            throw new InvalidOperationException(
                "AddCacheEncryption requires ISymmetricEncryptionService to be registered first. "
                    + "Call AddSharedKernelCryptography() (SharedKernel.Cryptography, 01.Core) before AddCacheEncryption().");
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

            // Re-inspect the ORIGINAL pre-replacement serializer registration (not the one just
            // replaced above) to determine whether compression duty needs to move here.
            var originalSerializer = (IFusionCacheSerializer)resolveExistingSerializer(sp);
            bool compressionEnabled = originalSerializer is BrotliCacheSerializer;

            return new EncryptedCacheService(innerCache, encryptionService, jsonOptions, compressionEnabled, logger);
        }));

        // Marker so AddBrotliCompression() can detect, at registration time, that encryption has
        // already been applied and refuse to silently discard it by rewrapping the raw base
        // serializer out from underneath it. Unchanged from Phase 42.
        builder.Services.TryAddSingleton(new CacheEncryptionOptions());

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
