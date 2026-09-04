using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Bridges <see cref="EncryptionOptions"/> (and the rotation-scoped <see cref="IEncryptionVersionOverride"/>)
/// to <see cref="IEncryptionKeyProvider"/>, enabling <see cref="ISymmetricEncryptionService"/> to resolve
/// the correct AES-256 key for <see cref="EncryptedValueConverter"/> operations.
/// </summary>
/// <remarks>
/// <para>
/// <strong>GetCurrentKeyAsync() precedence (P-227, preserved from P-147):</strong>
/// The target version is resolved as <c>versionOverride.OverrideVersion ?? optionsMonitor.CurrentValue.CurrentVersion</c>
/// — identical to the rule that previously lived inside <c>EncryptedValueConverter.Encrypt</c>.
/// This is how <see cref="EncryptionVersionOverride"/> (the rotation seam from P-147) continues to
/// direct which key a rotation batch encrypts with, without mutating <see cref="EncryptionOptions.CurrentVersion"/>.
/// </para>
/// <para>
/// <strong>GetKeyAsync(keyId, ct) deliberately ignores <see cref="IEncryptionVersionOverride"/>:</strong>
/// Decryption always targets the exact <c>KeyId</c> recorded in the stored ciphertext's version prefix
/// — never the current or override version. Returns <see langword="null"/> (not a throw) when the
/// requested <paramref name="keyId"/> is absent from <see cref="EncryptionOptions.Keys"/>, per the
/// <see cref="IEncryptionKeyProvider"/> documented contract.
/// </para>
/// <para>
/// <strong>Hot-reload safe:</strong> Both methods call <c>optionsMonitor.CurrentValue</c> on every
/// invocation — never a captured snapshot — so changes to <see cref="EncryptionOptions.Keys"/> or
/// <see cref="EncryptionOptions.CurrentVersion"/> take effect on the next call without a service restart.
/// </para>
/// <para>
/// <strong>D-110/P-448 (breaking, cascading from <c>01.Core</c>'s P-446):</strong> this provider
/// performs no genuine I/O — it reads an already-loaded <see cref="IOptionsMonitor{T}.CurrentValue"/>
/// plus the synchronous, in-memory <see cref="EncryptionKeyByteCache"/> decode-once cache. Both
/// members below therefore return an ALREADY-COMPLETED <see cref="ValueTask{TResult}"/>
/// (<c>IsCompletedSuccessfully == true</c>) via <c>new ValueTask&lt;T&gt;(value)</c> — never a
/// genuinely suspended one. Consequently <c>AesGcmEncryptionService</c>'s internal
/// <c>.GetAwaiter().GetResult()</c> bridge NEVER actually blocks a thread on real I/O when THIS
/// config-based provider is registered, in contrast with a genuinely network-bound provider (e.g. a
/// future <c>SharedKernel.Cryptography.KeyVault.Azure</c>, P-447) where the same bridge would block
/// a real thread on a cache miss.
/// </para>
/// <para>
/// <strong>Registration:</strong> Registered as scoped (matching <see cref="IEncryptionVersionOverride"/>'s
/// existing scoped lifetime) by <c>EfCorePersistenceBuilder.WithEncryption()</c> as
/// <see cref="IEncryptionKeyProvider"/>. A consuming service that separately uses
/// <c>SharedKernel.Cryptography</c> for general-purpose encryption and registers its own
/// <see cref="IEncryptionKeyProvider"/> must be aware both registrations target the same interface type.
/// </para>
/// </remarks>
internal sealed class EncryptionOptionsKeyProvider : IEncryptionKeyProvider
{
    private readonly IOptionsMonitor<EncryptionOptions> _optionsMonitor;
    private readonly IEncryptionVersionOverride _versionOverride;
    private readonly EncryptionKeyByteCache _keyByteCache;

    /// <summary>
    /// Initialises a new <see cref="EncryptionOptionsKeyProvider"/>.
    /// </summary>
    /// <param name="optionsMonitor">Live options monitor for hot-reload support.</param>
    /// <param name="versionOverride">Rotation-scoped version override seam.</param>
    /// <param name="keyByteCache">
    /// Singleton decode-once-per-config-value cache for key bytes (WO-051/P-323).
    /// </param>
    public EncryptionOptionsKeyProvider(
        IOptionsMonitor<EncryptionOptions> optionsMonitor,
        IEncryptionVersionOverride versionOverride,
        EncryptionKeyByteCache keyByteCache)
    {
        _optionsMonitor = optionsMonitor;
        _versionOverride = versionOverride;
        _keyByteCache = keyByteCache;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Resolves the target version as <c>versionOverride.OverrideVersion ?? CurrentVersion</c>,
    /// then resolves the decoded key bytes via <see cref="EncryptionKeyByteCache.GetOrDecode"/>
    /// (WO-051/P-323 — previously called <see cref="Convert.FromBase64String(string)"/> directly on
    /// every call). All work is synchronous/in-memory (D-110/P-448) — the returned
    /// <see cref="ValueTask{TResult}"/> is always already completed.
    /// </remarks>
    public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default)
    {
        var options = _optionsMonitor.CurrentValue;
        var version = _versionOverride.OverrideVersion ?? options.CurrentVersion;
        var keyBytes = _keyByteCache.GetOrDecode(version, options.Keys[version]);
        return new ValueTask<CryptographicKey>(new CryptographicKey(version, keyBytes));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Ignores <see cref="IEncryptionVersionOverride"/> — decryption always targets the exact
    /// <paramref name="keyId"/> from the stored ciphertext. Returns <see langword="null"/> when
    /// <paramref name="keyId"/> is absent from <see cref="EncryptionOptions.Keys"/>. All work is
    /// synchronous/in-memory (D-110/P-448) — the returned <see cref="ValueTask{TResult}"/> is
    /// always already completed.
    /// </remarks>
    public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default)
    {
        var options = _optionsMonitor.CurrentValue;
        if (options.Keys.TryGetValue(keyId, out var base64Key))
        {
            var keyBytes = _keyByteCache.GetOrDecode(keyId, base64Key);
            return new ValueTask<CryptographicKey?>(new CryptographicKey(keyId, keyBytes));
        }
        return new ValueTask<CryptographicKey?>((CryptographicKey?)null);
    }
}
