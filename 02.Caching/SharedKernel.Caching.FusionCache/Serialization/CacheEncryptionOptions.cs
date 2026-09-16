namespace SharedKernel.Caching.FusionCache.Serialization;

/// <summary>
/// Marker options registered by <c>AddCacheEncryption</c>, indicating that
/// <c>Encryption.EncryptedCacheService</c> is the currently active outermost decorator over the
/// registered <c>ICacheService</c>.
/// </summary>
/// <remarks>
/// <para>
/// Carries no secret material of its own — key material is owned and resolved entirely by the
/// registered <c>ISymmetricEncryptionService</c>'s own configuration (see
/// <c>01.Core/SharedKernel.Cryptography</c>'s <c>AddSharedKernelCryptography(configuration).AddSymmetricEncryption()</c>
/// and the registered <c>IEncryptionKeyProvider</c>). This type is
/// never duplicated or re-implemented as a key store; it exists purely as a DI-registration marker.
/// </para>
/// <para>
/// Presence of this type in the service collection is how <c>AddBrotliCompression()</c> detects,
/// at registration time, that cache-value encryption has already been applied — and fails fast
/// rather than silently discarding the encryption decorator by rewrapping the raw base serializer.
/// See "Cache-value encryption rules" in <c>02.Caching/CLAUDE.md</c>.
/// </para>
/// </remarks>
public sealed class CacheEncryptionOptions
{
    /// <summary>
    /// Gets a value indicating whether cache-value encryption is enabled. Always
    /// <see langword="true"/> once present in the service collection — this type is only ever
    /// registered as a result of calling <c>AddCacheEncryption()</c>.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
