using System.Text.Json.Serialization;

namespace SharedKernel.Caching.FusionCache.Serialization;

/// <summary>
/// STJ source-generated serializer context for the <see cref="T:byte[]"/> entries
/// <c>Encryption.EncryptedCacheService</c> stores: each is an encrypted payload in its storage format.
/// </summary>
/// <remarks>
/// Combined into the shared <see cref="System.Text.Json.JsonSerializerOptions"/>
/// <c>CachingServiceCollectionExtensions.AddSharedKernelCaching</c> constructs — see
/// <see cref="CacheSerializationOptions"/> — combined with the service's own context,
/// so FusionCache's L2 serializer can write encrypted entries without reflection when the consuming
/// service supplies a <c>JsonSerializerContext</c> via <c>CachingOptions.SerializerContext</c>. When no
/// <c>SerializerContext</c> is supplied, FusionCache falls back to reflection-based serialization and
/// this context has no effect.
/// </remarks>
[JsonSerializable(typeof(byte[]))]
internal sealed partial class EncryptedCacheEntryJsonContext : JsonSerializerContext;
