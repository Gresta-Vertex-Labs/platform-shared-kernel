using System.Text.Json;

namespace SharedKernel.Caching.FusionCache.Serialization;

/// <summary>
/// Internal DI holder for the <see cref="System.Text.Json.JsonSerializerOptions"/>
/// <c>CachingServiceCollectionExtensions.AddSharedKernelCaching</c> constructs for FusionCache's
/// own System.Text.Json serializer (Phase 15/18's AOT-combine rule).
/// </summary>
/// <remarks>
/// Added Phase 46/WO-081 so <c>Encryption.EncryptedCacheService</c> can reuse the exact same
/// <see cref="JsonSerializerOptions"/> instance for its own T-to-plaintext-bytes serialization step
/// instead of re-deriving a second, possibly divergent one — see AA-05 in
/// <c>02.Caching/state-map.md</c>'s <c>SK.02.CacheEncryptionAadBinding</c> phase. Registered
/// unconditionally by <c>AddSharedKernelCaching</c>, regardless of whether cache encryption is ever
/// opted in — a plain options POCO with negligible registration cost.
/// </remarks>
internal sealed class CacheSerializationOptions
{
    /// <summary>
    /// The shared <see cref="JsonSerializerOptions"/> instance. When the consuming service supplied
    /// no <c>CachingOptions.SerializerContext</c>, this falls back to a reflection-based default
    /// (<see cref="JsonSerializerDefaults.Web"/>) — a genuinely separate instance from whatever
    /// FusionCache's own serializer uses internally in that case, since that path deliberately
    /// passes no explicit options at all (see <c>AddSharedKernelCaching</c>).
    /// </summary>
    public required JsonSerializerOptions Value { get; init; }
}
