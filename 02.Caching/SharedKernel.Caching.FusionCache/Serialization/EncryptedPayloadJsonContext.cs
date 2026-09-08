using System.Text.Json.Serialization;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Caching.FusionCache.Serialization;

/// <summary>
/// STJ source-generated serializer context for <see cref="EncryptedPayload"/> (Phase 46/WO-081).
/// </summary>
/// <remarks>
/// Combined into the shared <see cref="System.Text.Json.JsonSerializerOptions"/>
/// <c>CachingServiceCollectionExtensions.AddSharedKernelCaching</c> constructs — see
/// <see cref="CacheSerializationOptions"/> — alongside the pre-existing
/// <c>CacheInvalidationMessageJsonContext.Default</c>, so both the L1/L2 wire storage of an
/// <see cref="EncryptedPayload"/> envelope (written by FusionCache's own registered
/// <c>IFusionCacheSerializer</c> when <c>Encryption.EncryptedCacheService</c> stores one) and that
/// same shared options instance's reuse for the application's own cached type <c>T</c> stay fully
/// NativeAOT-safe when the consuming service supplies a <c>JsonSerializerContext</c> via
/// <c>CachingOptions.SerializerContext</c>. When no <c>SerializerContext</c> is supplied, FusionCache
/// falls back to reflection-based serialization as it already does today — this context has no
/// effect in that case.
/// </remarks>
[JsonSerializable(typeof(EncryptedPayload))]
internal sealed partial class EncryptedPayloadJsonContext : JsonSerializerContext;
