using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Serialization;
using SharedKernel.Cryptography.Symmetric;
using ZiggyCreatures.Caching.Fusion.Serialization;

namespace SharedKernel.Caching.FusionCache.Extensions;

/// <summary>
/// <see cref="ICachingBuilder"/> extension methods for enabling opt-in AES-GCM encryption of
/// serialized cache-entry payloads.
/// </summary>
public static class CacheEncryptionCachingBuilderExtensions
{
    /// <summary>
    /// Wraps whichever <see cref="IFusionCacheSerializer"/> is currently registered with
    /// <see cref="CacheEncryptionSerializer"/>, so that every payload is AES-GCM encrypted before
    /// being written to L1/L2, and transparently decrypted on read.
    /// </summary>
    /// <param name="builder">The <see cref="ICachingBuilder"/> to configure.</param>
    /// <returns>The same <see cref="ICachingBuilder"/> to allow further chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="ISymmetricEncryptionService"/> is not already registered (call
    /// <c>AddSharedKernelCryptography()</c> from <c>01.Core/SharedKernel.Cryptography</c> first),
    /// or when no <see cref="IFusionCacheSerializer"/> is registered yet (call
    /// <c>AddSharedKernelCaching()</c> first).
    /// </exception>
    /// <remarks>
    /// <para>
    /// Must be called <em>after</em> <c>AddBrotliCompression()</c> when both are used, so this
    /// decorator becomes the outermost wrapper — producing compress-then-encrypt on write and
    /// decrypt-then-decompress on read. Calling <c>AddBrotliCompression()</c> after this method has
    /// already been called throws <see cref="InvalidOperationException"/>, structurally enforcing
    /// the correct ordering rather than leaving it to call-order chance.
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

        ServiceDescriptor? existing = builder.Services.LastOrDefault(
            sd => sd.ServiceType == typeof(IFusionCacheSerializer));

        if (existing is null)
        {
            throw new InvalidOperationException(
                "AddCacheEncryption requires an IFusionCacheSerializer to already be registered. "
                    + "Call AddSharedKernelCaching() before AddCacheEncryption().");
        }

        // Capture how to resolve the currently-registered serializer BEFORE it is replaced below,
        // so the new factory can wrap it as its inner without recursing back into itself.
        Func<IServiceProvider, object> resolveInner = ResolveExistingFactory(existing);

        builder.Services.Replace(ServiceDescriptor.Singleton<IFusionCacheSerializer>(sp =>
        {
            var inner = (IFusionCacheSerializer)resolveInner(sp);
            var encryptionService = sp.GetRequiredService<ISymmetricEncryptionService>();
            return new CacheEncryptionSerializer(inner, encryptionService);
        }));

        // Marker so AddBrotliCompression() can detect, at registration time, that encryption has
        // already been applied and refuse to silently discard it by rewrapping the raw base
        // serializer out from underneath it.
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
                "Unable to resolve the currently-registered IFusionCacheSerializer implementation.");

        return sp => ActivatorUtilities.CreateInstance(sp, implementationType);
    }
}
