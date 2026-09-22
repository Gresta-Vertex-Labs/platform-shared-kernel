namespace SharedKernel.Persistence.EfCore.Encryption.BlindIndex;

/// <summary>
/// Supplies the versioned HMAC keys blind indexes are computed with, independent of the encryption keys.
/// </summary>
/// <remarks>
/// <para>
/// Blind-index keys are deliberately separate from encryption keys: rotating the encryption key re-encrypts
/// values without touching their indexes, so lookups keep working throughout an encryption rotation. Each stored
/// index is prefixed with the version that produced it (<c>v1:3fa9…</c>).
/// </para>
/// <para>
/// To rotate the blind-index key, add the new version and make it current. Lookups match every version the
/// provider still returns, so nothing is missed while the maintenance job with
/// <c>EncryptionMaintenanceMode.RecomputeBlindIndexes</c> rewrites the stored indexes; remove the old version once
/// it reports no rows left on it.
/// </para>
/// <para>
/// The default implementation reads <c>SharedKernel:Persistence:Encryption:BlindIndexKeys</c>
/// (<c>CurrentVersion</c> and a base64 <c>Keys</c> map, at least 32 bytes each). Supply another with
/// <c>FieldEncryptionBuilder.UseBlindIndexKeys&lt;T&gt;()</c>.
/// </para>
/// </remarks>
public interface IBlindIndexKeyProvider
{
    /// <summary>The version new indexes are computed with. Lowercase letters and digits, at most 15 characters.</summary>
    string CurrentVersion { get; }

    /// <summary>Every version that lookups must still match, including <see cref="CurrentVersion"/>.</summary>
    IReadOnlyCollection<string> Versions { get; }

    /// <summary>Returns the key material for <paramref name="version"/>, or <see langword="null"/> when it is unknown.</summary>
    /// <param name="version">A version from <see cref="Versions"/>.</param>
    /// <returns>At least 32 bytes of key material, or <see langword="null"/>.</returns>
    ReadOnlyMemory<byte>? GetKey(string version);
}
