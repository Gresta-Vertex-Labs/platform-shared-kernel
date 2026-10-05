namespace SharedKernel.Caching.FusionCache.Serialization;

/// <summary>
/// Registered by <c>AddCacheEncryption</c> so <c>AddBrotliCompression</c> can detect, at registration
/// time, that encryption is already in place and refuse to be applied after it.
/// </summary>
internal sealed class CacheEncryptionMarker;
