using System.Collections.Concurrent;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// An <see cref="IEncryptionKeyProvider"/> decorator that serves exclusively from an in-memory,
/// explicitly-warmed cache — HONESTLY earning <see cref="ISynchronousEncryptionKeyProvider"/> by
/// construction, even when wrapping a genuinely network-bound (KMS/HSM-backed) inner provider.
/// </summary>
/// <remarks>
/// <para>
/// <strong>D-129/P-498/WO-081 — corrects the phase's original refuted premise (D-126):</strong>
/// merely wrapping a KMS-backed <see cref="IEncryptionKeyProvider"/> in <c>01.Core</c>'s
/// <see cref="CachedEncryptionKeyProvider"/> does NOT satisfy
/// <see cref="EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(IEncryptionKeyProvider)"/> —
/// that check is a static provider-IDENTITY test, never a per-call cache-warmth test, and
/// <see cref="CachedEncryptionKeyProvider"/> never implements the marker itself. This type instead
/// makes the "genuinely, always, zero-I/O" claim TRUE BY CONSTRUCTION: its two
/// <see cref="IEncryptionKeyProvider"/> members (<see cref="GetCurrentKeyAsync"/>/
/// <see cref="GetKeyAsync"/>) NEVER touch <see cref="Inner"/> — they read ONLY from
/// <c>_warmCache</c>, populated exclusively by the two internal, ASYNC-ONLY warming methods
/// (<see cref="WarmCurrentAsync"/>/<see cref="WarmVersionAsync"/>), callable only from
/// <see cref="Interceptors.EncryptionKeyPreWarmingInterceptor"/> and the startup readiness gate
/// (<see cref="EncryptionKeyPreWarmingHostedService"/>).
/// </para>
/// <para>
/// <strong>Fails closed, never blocks:</strong> a cache MISS on <see cref="GetKeyAsync"/> returns an
/// already-completed <see langword="null"/> result (never throws) — exactly like the existing
/// unknown/removed-key case, surfacing downstream as the pre-existing
/// <see cref="EncryptionKeyNotFoundException"/> path, unchanged. A cache miss on
/// <see cref="GetCurrentKeyAsync"/> (never yet warmed, or an override pointing at an unwarmed
/// version) throws <see cref="InvalidOperationException"/> SYNCHRONOUSLY — before constructing any
/// <see cref="ValueTask{TResult}"/> — mirroring <see cref="NullEncryptionKeyProvider"/>'s existing
/// synchronous-throw precedent. Under normal operation this branch is unreachable: registering this
/// type via <c>EfCorePersistenceBuilder&lt;TContext&gt;.WithExternalEncryptionKeyProvider&lt;TProvider&gt;()</c>
/// also registers <see cref="EncryptionKeyPreWarmingHostedService"/>, which blocks host readiness
/// until the current key is warmed.
/// </para>
/// <para>
/// Registered as a SINGLETON — the warm cache is process-lifetime state, not per-request state.
/// </para>
/// </remarks>
internal sealed class PreWarmedEncryptionKeyProvider : ISynchronousEncryptionKeyProvider
{
    private readonly IEncryptionKeyProvider _inner;
    private readonly IEncryptionVersionOverride _versionOverride;
    private readonly ConcurrentDictionary<string, byte[]> _warmCache = new();
    private volatile string? _currentVersionTag;

    /// <summary>Initialises a new <see cref="PreWarmedEncryptionKeyProvider"/>.</summary>
    /// <param name="inner">
    /// The wrapped provider — never called from <see cref="GetCurrentKeyAsync"/>/
    /// <see cref="GetKeyAsync"/>, only from <see cref="WarmCurrentAsync"/>/<see cref="WarmVersionAsync"/>.
    /// </param>
    /// <param name="versionOverride">
    /// The same rotation-target-version accessor <see cref="EncryptedValueConverter"/> consults, so
    /// a rotation batch's <c>OverrideVersion</c> resolves consistently through this provider too.
    /// </param>
    public PreWarmedEncryptionKeyProvider(IEncryptionKeyProvider inner, IEncryptionVersionOverride versionOverride)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(versionOverride);
        _inner = inner;
        _versionOverride = versionOverride;
    }

    /// <summary>
    /// The wrapped provider — exposed only so tests can prove it is never invoked from the two
    /// <see cref="IEncryptionKeyProvider"/> members below.
    /// </summary>
    internal IEncryptionKeyProvider Inner => _inner;

    /// <inheritdoc />
    /// <remarks>
    /// NEVER TOUCHES <see cref="Inner"/>. Resolves the target tag as
    /// <c>versionOverride.OverrideVersion ?? _currentVersionTag</c>; if that tag has a warm cache
    /// entry, returns it via an already-completed <see cref="ValueTask{TResult}"/>. Otherwise throws
    /// <see cref="InvalidOperationException"/> SYNCHRONOUSLY — before constructing any
    /// <see cref="ValueTask{TResult}"/> — never a faulted one.
    /// </remarks>
    public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default)
    {
        var tag = _versionOverride.OverrideVersion ?? _currentVersionTag;

        if (tag is not null && _warmCache.TryGetValue(tag, out var material))
        {
            return new ValueTask<CryptographicKey>(new CryptographicKey(tag, material));
        }

        throw new InvalidOperationException(
            tag is null
                ? "PreWarmedEncryptionKeyProvider has not been warmed yet — no current encryption " +
                  "key version is known. This should be unreachable under normal operation: " +
                  "WithExternalEncryptionKeyProvider<TProvider>() registers a readiness gate " +
                  "(EncryptionKeyPreWarmingHostedService) that warms the current key before the " +
                  "host accepts traffic. If you see this, a startup-ordering invariant was broken."
                : $"PreWarmedEncryptionKeyProvider has no warm cache entry for encryption key " +
                  $"version '{tag}'. This provider NEVER performs I/O from GetCurrentKeyAsync — " +
                  "the version must be warmed first via WarmCurrentAsync (automatic, via " +
                  "EncryptionKeyPreWarmingInterceptor/EncryptionKeyPreWarmingHostedService).");
    }

    /// <inheritdoc />
    /// <remarks>
    /// NEVER TOUCHES <see cref="Inner"/>. A cache miss returns an already-completed
    /// <see langword="null"/> result — never throws — exactly like an unknown/retired key version
    /// today, surfacing downstream as the existing <see cref="EncryptionKeyNotFoundException"/> path.
    /// </remarks>
    public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        return _warmCache.TryGetValue(keyId, out var material)
            ? new ValueTask<CryptographicKey?>(new CryptographicKey(keyId, material))
            : new ValueTask<CryptographicKey?>((CryptographicKey?)null);
    }

    /// <summary>
    /// Warms the current key — resolved as <c>versionOverride.OverrideVersion ?? _currentVersionTag</c>
    /// when already known, or fetched fresh from <see cref="Inner"/> otherwise — updating
    /// <c>_currentVersionTag</c> only when resolving without an active override. No-op (never calls
    /// <see cref="Inner"/>) when the resolved tag is already warm.
    /// </summary>
    /// <param name="ct">A token to observe while resolving the current key from <see cref="Inner"/>.</param>
    /// <remarks>
    /// Callable ONLY from <see cref="Interceptors.EncryptionKeyPreWarmingInterceptor"/> and
    /// <see cref="EncryptionKeyPreWarmingHostedService"/> — never from
    /// <see cref="GetCurrentKeyAsync"/>/<see cref="GetKeyAsync"/> themselves.
    /// </remarks>
    public async ValueTask WarmCurrentAsync(CancellationToken ct = default)
    {
        var overrideTag = _versionOverride.OverrideVersion;
        if (overrideTag is not null)
        {
            if (_warmCache.ContainsKey(overrideTag))
            {
                return; // Already warm for the override-resolved tag — never touches Inner.
            }

            await WarmVersionAsync(overrideTag, ct).ConfigureAwait(false);
            return;
        }

        if (_currentVersionTag is { } currentTag && _warmCache.ContainsKey(currentTag))
        {
            return; // Already warm for the current tag — never touches Inner.
        }

        var key = await _inner.GetCurrentKeyAsync(ct).ConfigureAwait(false);
        _warmCache[key.Id] = key.Material;
        _currentVersionTag = key.Id;
    }

    /// <summary>
    /// Warms one specific historical key version — used to pre-warm a rotation batch's
    /// <c>toVersion</c>/<c>fromVersion</c> or a decrypt path expecting an older version. No-op
    /// (never calls <see cref="Inner"/>) when <paramref name="keyId"/> is already warm.
    /// </summary>
    /// <param name="keyId">The key version identifier to warm.</param>
    /// <param name="ct">A token to observe while resolving the key from <see cref="Inner"/>.</param>
    public async ValueTask WarmVersionAsync(string keyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        if (_warmCache.ContainsKey(keyId))
        {
            return; // Already warm — never touches Inner.
        }

        var key = await _inner.GetKeyAsync(keyId, ct).ConfigureAwait(false);
        if (key is not null)
        {
            _warmCache[key.Id] = key.Material;
        }
    }
}
