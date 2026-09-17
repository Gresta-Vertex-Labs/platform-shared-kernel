using System.Text.Json;

namespace SharedKernel.Caching.FusionCache.Serialization;

/// <summary>
/// The one <see cref="JsonSerializerOptions"/> instance, built from <c>CachingOptions</c>, used by
/// FusionCache's distributed serializer and by the plaintext step of <c>Encryption.EncryptedCacheService</c>.
/// </summary>
internal sealed class CacheSerializationOptions
{
    public required JsonSerializerOptions Value { get; init; }
}
