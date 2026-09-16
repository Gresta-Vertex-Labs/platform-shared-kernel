using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Serves the AES-256 keys configured in <see cref="EncryptionOptions"/> (and the rotation-scoped
/// <see cref="IEncryptionVersionOverride"/>) to the synchronous encryption service behind
/// <see cref="EncryptedValueConverter"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Current key:</strong> the version is <c>versionOverride.OverrideVersion ?? CurrentVersion</c>, so a
/// rotation batch encrypts with its target version without mutating <see cref="EncryptionOptions.CurrentVersion"/>.
/// A version absent from <see cref="EncryptionOptions.Keys"/> throws <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// <strong>Historical keys:</strong> <see cref="GetKey"/> ignores the override, because decryption always targets
/// the key id recorded in the stored payload. An absent id returns <see langword="null"/>, which the encryption
/// service reports as an unknown key.
/// </para>
/// <para>
/// <strong>Hot reload:</strong> both members read <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> on every
/// call. Each decoded <see cref="CryptographicKey"/> is cached per version together with the Base64 string it was
/// decoded from, and reused only while that string is unchanged, so a reload that changes a key's material or adds
/// a version is picked up on the next call without a cache-invalidation subscription.
/// </para>
/// <para>
/// This type performs no I/O, which is why it can implement <see cref="ISynchronousEncryptionKeyProvider"/>.
/// It is the config-backed default registered by <c>EfCorePersistenceBuilder.WithEncryption()</c>, under this
/// package's internal keyed-DI slot (<see cref="PersistenceEncryptionKeys"/>), never the ambient unkeyed slot.
/// </para>
/// </remarks>
internal sealed class EncryptionOptionsKeyProvider : ISynchronousEncryptionKeyProvider
{
    private readonly IOptionsMonitor<EncryptionOptions> _optionsMonitor;
    private readonly IEncryptionVersionOverride _versionOverride;
    private readonly ConcurrentDictionary<string, CachedKey> _keys = new(StringComparer.Ordinal);

    /// <summary>
    /// Initialises a new <see cref="EncryptionOptionsKeyProvider"/>.
    /// </summary>
    /// <param name="optionsMonitor">Live options monitor for hot-reload support.</param>
    /// <param name="versionOverride">Rotation-scoped version override seam.</param>
    public EncryptionOptionsKeyProvider(
        IOptionsMonitor<EncryptionOptions> optionsMonitor,
        IEncryptionVersionOverride versionOverride)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(versionOverride);
        _optionsMonitor = optionsMonitor;
        _versionOverride = versionOverride;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The resolved version (override or <see cref="EncryptionOptions.CurrentVersion"/>) is not in
    /// <see cref="EncryptionOptions.Keys"/>.
    /// </exception>
    public CryptographicKey GetCurrentKey()
    {
        var options = _optionsMonitor.CurrentValue;
        var version = _versionOverride.OverrideVersion ?? options.CurrentVersion;

        return Resolve(options, version)
            ?? throw new InvalidOperationException(
                $"Encryption key version '{version}' is not configured in EncryptionOptions.Keys. " +
                "Add the key before making it the current or rotation-target version.");
    }

    /// <inheritdoc />
    public CryptographicKey? GetKey(string keyId)
    {
        ArgumentNullException.ThrowIfNull(keyId);
        return Resolve(_optionsMonitor.CurrentValue, keyId);
    }

    private CryptographicKey? Resolve(EncryptionOptions options, string version)
    {
        if (string.IsNullOrWhiteSpace(version) || !options.Keys.TryGetValue(version, out var base64Key))
        {
            return null;
        }

        if (_keys.TryGetValue(version, out var cached) && string.Equals(cached.Source, base64Key, StringComparison.Ordinal))
        {
            return cached.Key;
        }

        var key = Decode(version, base64Key);
        _keys[version] = new CachedKey(base64Key, key);
        return key;
    }

    private static CryptographicKey Decode(string version, string base64Key)
    {
        var material = Convert.FromBase64String(base64Key);
        try
        {
            return new CryptographicKey(version, material);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    private sealed record CachedKey(string Source, CryptographicKey Key);
}
