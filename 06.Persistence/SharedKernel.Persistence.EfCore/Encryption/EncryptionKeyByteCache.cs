using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Caches Base64-decoded encryption key bytes per version, so the common no-rotation-in-flight
/// case decodes each key version at most once per underlying <see cref="EncryptionOptions"/> value.
/// </summary>
/// <remarks>
/// <para>
/// WO-051/P-323 — <see cref="EncryptionOptionsKeyProvider.GetCurrentKeyAsync"/>/<see cref="EncryptionOptionsKeyProvider.GetKeyAsync"/>
/// previously called <see cref="Convert.FromBase64String(string)"/> on every invocation, even though
/// the underlying Base64 string for a given key version is immutable until the next config/rotation
/// reload.
/// </para>
/// <para>
/// Registered as a SINGLETON — deliberately NOT scoped like <see cref="EncryptionOptionsKeyProvider"/>
/// itself, since decoded key bytes have no per-request variation, only per-config-value variation, so
/// a cross-scope cache is strictly more efficient than a per-scope one here. Subscribes ONCE, at
/// singleton-construction time, to <see cref="IOptionsMonitor{TOptions}.OnChange"/> for a coarse,
/// whole-cache-clear invalidation on ANY <see cref="EncryptionOptions"/> change (config reload, a
/// rotation adding a new key version, etc.) — simpler and safer than per-key diffing, since config
/// reloads are rare and re-decoding is cheap. No per-scope subscription is ever created, so there is
/// no subscription-leak risk from this cache's lifetime being singleton.
/// </para>
/// </remarks>
internal sealed class EncryptionKeyByteCache
{
    private readonly ConcurrentDictionary<string, byte[]> _cache = new();

    /// <summary>
    /// Initialises a new <see cref="EncryptionKeyByteCache"/> and subscribes to
    /// <paramref name="optionsMonitor"/> changes to clear the cache.
    /// </summary>
    /// <param name="optionsMonitor">The live options monitor to observe for changes.</param>
    public EncryptionKeyByteCache(IOptionsMonitor<EncryptionOptions> optionsMonitor)
    {
        optionsMonitor.OnChange(_ => _cache.Clear());
    }

    /// <summary>
    /// Returns the decoded key bytes for <paramref name="version"/>, decoding
    /// <paramref name="base64Value"/> only on the first call for that version (or after a cache
    /// clear triggered by an <see cref="EncryptionOptions"/> change).
    /// </summary>
    /// <param name="version">The key version (used as the cache key).</param>
    /// <param name="base64Value">The Base64-encoded key material to decode on a cache miss.</param>
    /// <returns>The decoded key bytes.</returns>
    public byte[] GetOrDecode(string version, string base64Value) =>
        _cache.GetOrAdd(version, static (_, value) => Convert.FromBase64String(value), base64Value);
}
