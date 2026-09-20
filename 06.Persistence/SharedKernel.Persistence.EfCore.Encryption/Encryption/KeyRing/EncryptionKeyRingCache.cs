using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Persistence.EfCore.Encryption.KeyRing;

/// <summary>
/// Bridges an asynchronous (KMS/HSM-backed) <see cref="IEncryptionKeyProvider"/> to the synchronous
/// <see cref="ISynchronousEncryptionKeyProvider"/> contract <see cref="EncryptionInterceptor"/> needs, by serving
/// keys only from an in-memory snapshot refreshed periodically by <see cref="EncryptionKeyRingRefreshHostedService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered by <c>EfCorePersistenceBuilder{TContext}.WithEncryption()</c> ONLY when the consuming service
/// registered an asynchronous <see cref="IEncryptionKeyProvider"/> but no genuine
/// <see cref="ISynchronousEncryptionKeyProvider"/> — matching <c>01.Core</c>'s own documented pattern for bridging
/// an async-only key provider into synchronous code ("load them into memory ahead of time, for example with a
/// hosted service that refreshes a <see cref="StaticEncryptionKeyProvider"/>"). This is that hosted service, kept
/// intentionally minimal: one snapshot swap, no on-demand background warming, no per-key-id retry bookkeeping — the
/// operator declares which historical key ids must stay decryptable via
/// <see cref="EncryptionOptions.KeyRingRetiredKeyIds"/> instead of this type discovering them reactively.
/// </para>
/// <para>
/// <strong>Never awaits <see cref="Inner"/> from <see cref="GetCurrentKey"/>/<see cref="GetKey"/>.</strong> Both
/// read only the current in-memory <see cref="_snapshot"/>, swapped atomically by <see cref="RefreshAsync"/>. A key
/// id that is neither current nor listed in <see cref="EncryptionOptions.KeyRingRetiredKeyIds"/> is simply not
/// available through this bridge and surfaces as <see cref="EncryptionKeyNotFoundException"/> — fail closed, never
/// a blocking round trip to the key service from inside a materialization callback.
/// </para>
/// <para>Registered as a singleton — the snapshot is process-lifetime state, not per-request state.</para>
/// </remarks>
public sealed class EncryptionKeyRingCache : ISynchronousEncryptionKeyProvider
{
    private readonly IEncryptionKeyProvider _inner;
    private readonly IReadOnlyList<string> _retiredKeyIds;
    private volatile KeyRingSnapshot? _snapshot;

    /// <summary>Initialises a new <see cref="EncryptionKeyRingCache"/>.</summary>
    /// <param name="inner">The wrapped asynchronous provider — never awaited from <see cref="GetCurrentKey"/>/<see cref="GetKey"/>.</param>
    /// <param name="retiredKeyIds">Historical key ids to keep warm for decryption, alongside whichever key is current.</param>
    public EncryptionKeyRingCache(IEncryptionKeyProvider inner, IReadOnlyList<string> retiredKeyIds)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(retiredKeyIds);
        _inner = inner;
        _retiredKeyIds = retiredKeyIds;
    }

    /// <summary>The wrapped provider — exposed only so tests can prove it is never invoked from the synchronous members.</summary>
    internal IEncryptionKeyProvider Inner => _inner;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No refresh has completed yet.</exception>
    public CryptographicKey GetCurrentKey() =>
        _snapshot?.Current
        ?? throw new InvalidOperationException(
            "EncryptionKeyRingCache has not been warmed yet — no current encryption key is known. This should be " +
            "unreachable under normal operation: EncryptionKeyRingRefreshHostedService.StartAsync awaits the " +
            "first refresh before the host accepts traffic. If you see this, a startup-ordering invariant was " +
            "broken, or this cache is being used outside a host that registered that service.");

    /// <inheritdoc />
    public CryptographicKey? GetKey(string keyId)
    {
        ArgumentNullException.ThrowIfNull(keyId);
        return _snapshot?.ByKeyId.GetValueOrDefault(keyId);
    }

    /// <summary>
    /// Fetches the current key and every key listed in the constructor's <c>retiredKeyIds</c> from
    /// <see cref="Inner"/>, and atomically replaces the served snapshot. A key id <see cref="Inner"/> reports as
    /// unknown is simply absent from the new snapshot.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while resolving keys from <see cref="Inner"/>.</param>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var current = await _inner.GetCurrentKeyAsync(cancellationToken).ConfigureAwait(false);
        var byKeyId = new Dictionary<string, CryptographicKey>(StringComparer.Ordinal) { [current.Id] = current };

        foreach (var retiredKeyId in _retiredKeyIds)
        {
            if (byKeyId.ContainsKey(retiredKeyId))
                continue;

            var key = await _inner.GetKeyAsync(retiredKeyId, cancellationToken).ConfigureAwait(false);
            if (key is not null)
                byKeyId[key.Id] = key;
        }

        _snapshot = new KeyRingSnapshot(current, byKeyId);
    }

    private sealed record KeyRingSnapshot(CryptographicKey Current, IReadOnlyDictionary<string, CryptographicKey> ByKeyId);
}
