using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// EF Core value converter that transparently encrypts and decrypts <see langword="string"/> EF Core
/// properties by delegating all cryptographic operations to <see cref="ISymmetricEncryptionService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>CRYPTOGRAPHY DELEGATION (P-227):</strong> All AES-256-GCM cryptographic operations
/// (nonce generation, encrypt, tag-append, decrypt, tag verification) are delegated to the injected
/// <see cref="ISymmetricEncryptionService"/>. Zero direct <c>AesGcm</c> or <c>RandomNumberGenerator</c>
/// calls remain in this converter. <see cref="ISymmetricEncryptionService"/> is resolved from DI —
/// the consuming service must call <c>AddSharedKernelCryptography()</c> itself.
/// </para>
/// <para>
/// <strong>Wire format (unchanged, byte-for-byte):</strong>
/// <c>"v{version}:{Base64(nonce || ciphertext || 16-byte-auth-tag)}"</c>.
/// This converter retains exclusive ownership of wire-format packing/parsing. ISymmetricEncryptionService
/// knows nothing about column storage strings.
/// </para>
/// <para>
/// <strong>Encryption:</strong> Calls <c>symmetricEncryptionService.Encrypt(plaintextBytes)</c>
/// → receives an <see cref="EncryptedPayload"/> → packs
/// <c>"v{KeyId}:{Base64(Nonce||Ciphertext||Tag)}"</c>. KeyId is the EncryptionOptions version string
/// (e.g., <c>"v1"</c>).
/// </para>
/// <para>
/// <strong>Decryption (D-108/P-448):</strong> Parses the version prefix, reconstructs an
/// <see cref="EncryptedPayload"/>, and calls <c>symmetricEncryptionService.Decrypt(payload)</c>
/// directly — this converter no longer resolves keys itself, sync or async. A failed
/// <see cref="SharedKernel.Primitives.Results.Result{T}"/> whose
/// <c>Error.Code == CryptographyErrorCodes.UnknownKeyId</c> (the stored version is absent from
/// <see cref="EncryptionOptions.Keys"/>) is mapped to <see cref="EncryptionKeyNotFoundException"/>;
/// any other failure (auth-tag mismatch, tamper, wrong key) is mapped to the existing generic
/// <see cref="System.Security.Cryptography.CryptographicException"/>. This distinction was
/// previously made by a direct pre-check call to <see cref="IEncryptionKeyProvider.GetKey(string)"/>
/// before <c>01.Core</c>'s P-446 made that member asynchronous — <c>AesGcmEncryptionService</c>
/// already surfaced the same two error codes on its own <c>Result{T}</c>, so the pre-check was
/// redundant and removing it keeps this converter's pipeline synchronous without ever touching the
/// (now async-only) <see cref="IEncryptionKeyProvider"/> directly.
/// </para>
/// <para>
/// <strong>Associated data / AAD (P-491/D-128/WO-081):</strong> every encrypt/decrypt call this
/// converter performs binds a fixed <c>associatedData</c> byte array — supplied once at
/// construction by <see cref="EncryptionModelConvention"/>, computed from the annotated property's
/// stable table+column storage identity (or an explicit <c>associatedDataOverride</c>) — into the
/// AES-GCM authentication tag. This closes the column-splicing hazard: a ciphertext copied from one
/// encrypted column into a DIFFERENT encrypted column now fails authentication instead of silently
/// "decrypting" into garbage. AAD is bound to the COLUMN's identity, not row content, so a value
/// copied to another ROW of the SAME column still decrypts — a documented, accepted weaker bound
/// than full row-level binding. RENAMING a table/column that carries <c>.Encrypt()</c> changes this
/// value and makes every existing row's ciphertext for that column permanently undecryptable
/// (surfaces as the existing generic <see cref="System.Security.Cryptography.CryptographicException"/>
/// path) unless an explicit <c>associatedDataOverride</c> was supplied up front.
/// </para>
/// <para>
/// <strong>Legacy plaintext:</strong> Stored values without a <c>"v"</c> prefix are returned
/// unchanged — safe migration path from unencrypted columns.
/// </para>
/// <para>
/// <strong>Pass-through mode:</strong> When <c>EncryptionOptions.Enabled == false</c>, both
/// encrypt and decrypt directions return the value unchanged. No <c>ISymmetricEncryptionService</c>
/// calls are made.
/// </para>
/// <para>
/// <strong>Hot-reload safe:</strong> Key resolution reads <see cref="IOptionsMonitor{T}.CurrentValue"/>
/// on every call via <see cref="EncryptionOptionsKeyProvider"/>.
/// </para>
/// <para>
/// <strong>Do NOT instantiate directly</strong> in <c>IEntityTypeConfiguration</c> — use the
/// <c>.Encrypt()</c> extension on <c>PropertyBuilder&lt;T&gt;</c> instead (SK0304).
/// </para>
/// </remarks>
public sealed class EncryptedValueConverter : ValueConverter<string, string>
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    /// <summary>
    /// Initialises a new <see cref="EncryptedValueConverter"/> with cryptography delegation (P-227,
    /// simplified per D-108/P-448).
    /// </summary>
    /// <param name="optionsMonitor">Live options monitor for pass-through gating and hot-reload support.</param>
    /// <param name="symmetricEncryptionService">
    /// The cryptographic service that performs AES-256-GCM encrypt/decrypt operations.
    /// Registered by the consuming service via <c>AddSharedKernelCryptography()</c>.
    /// </param>
    /// <param name="associatedData">
    /// Additional authenticated data (AAD, P-491/D-128/WO-081) bound into the AES-GCM
    /// authentication tag for every encrypt/decrypt call this converter instance ever performs —
    /// computed once by <see cref="EncryptionModelConvention"/> at model-finalization time from the
    /// annotated property's stable table+column storage identity (or an explicit
    /// <c>associatedDataOverride</c> supplied to <c>.Encrypt()</c>), and reused unchanged for the
    /// lifetime of this converter instance. A ciphertext produced by one column's converter fails
    /// authentication (the existing generic <see cref="System.Security.Cryptography.CryptographicException"/>
    /// path) if fed through a DIFFERENT column's converter — see this type's class remarks.
    /// <strong>RENAME HAZARD:</strong> renaming the underlying table/column changes this value and
    /// makes every existing row's ciphertext for that column permanently undecryptable unless an
    /// explicit <c>associatedDataOverride</c> was supplied up front.
    /// </param>
    /// <param name="versionOverride">
    /// Optional scoped accessor allowing <see cref="EncryptionRotationService{TContext}"/> to direct
    /// this converter to encrypt with a specific target key version for the duration of a rotation
    /// batch. The override precedence rule <c>OverrideVersion ?? CurrentVersion</c> is resolved
    /// inside <see cref="EncryptionOptionsKeyProvider.GetCurrentKeyAsync"/>, which
    /// <paramref name="symmetricEncryptionService"/> consults internally.
    /// </param>
    /// <remarks>
    /// <strong>D-108/P-448 (breaking):</strong> this constructor no longer takes an
    /// <see cref="IEncryptionKeyProvider"/> parameter — this converter never resolves keys itself,
    /// it only calls <paramref name="symmetricEncryptionService"/>'s synchronous members, which
    /// resolve keys internally (bridging onto the now-asynchronous <see cref="IEncryptionKeyProvider"/>
    /// via <c>.GetAwaiter().GetResult()</c> — see <c>01.Core</c>'s own documented cost).
    /// </remarks>
    public EncryptedValueConverter(
        IOptionsMonitor<EncryptionOptions> optionsMonitor,
        ISymmetricEncryptionService symmetricEncryptionService,
        byte[] associatedData,
        IEncryptionVersionOverride? versionOverride = null)
        : base(
            value => Encrypt(value, optionsMonitor.CurrentValue, symmetricEncryptionService, associatedData),
            stored => Decrypt(stored, optionsMonitor.CurrentValue, symmetricEncryptionService, associatedData))
    {
    }

    private static string Encrypt(
        string value,
        EncryptionOptions options,
        ISymmetricEncryptionService symmetricEncryptionService,
        byte[] associatedData)
    {
        if (!options.Enabled)
        {
            return value;
        }

        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(value);
        var payload = symmetricEncryptionService.Encrypt(plaintextBytes, associatedData);

        // Pack into the on-disk wire format: "v{KeyId}:{Base64(Nonce||Ciphertext||Tag)}"
        var combined = new byte[NonceSizeBytes + payload.Ciphertext.Length + TagSizeBytes];
        payload.Nonce.CopyTo(combined, 0);
        payload.Ciphertext.CopyTo(combined, NonceSizeBytes);
        payload.Tag.CopyTo(combined, NonceSizeBytes + payload.Ciphertext.Length);

        return $"v{payload.KeyId}:{Convert.ToBase64String(combined)}";
    }

    private static string Decrypt(
        string stored,
        EncryptionOptions options,
        ISymmetricEncryptionService symmetricEncryptionService,
        byte[] associatedData)
    {
        if (!options.Enabled)
        {
            return stored;
        }

        // Legacy plaintext — no version prefix; return as-is.
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith('v'))
        {
            return stored;
        }

        var colonIndex = stored.IndexOf(':');
        if (colonIndex < 0)
        {
            // Malformed — no colon separator; treat as legacy plaintext.
            return stored;
        }

        var version = stored[1..colonIndex]; // Strip the leading 'v'
        var base64Payload = stored[(colonIndex + 1)..];

        var combined = Convert.FromBase64String(base64Payload);

        if (combined.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new System.Security.Cryptography.CryptographicException(
                "Stored ciphertext payload is too short to be valid.");
        }

        // Unpack the combined blob: nonce (12) || ciphertext (n) || tag (16)
        var nonce = combined[..NonceSizeBytes];
        var tag = combined[^TagSizeBytes..];
        var ciphertext = combined[NonceSizeBytes..^TagSizeBytes];

        var payload = new EncryptedPayload(version, nonce, ciphertext, tag);

        // D-108/P-448: no more direct pre-check against IEncryptionKeyProvider — the underlying
        // ISymmetricEncryptionService.Decrypt() call already distinguishes "unknown key id" from
        // "tamper/wrong key" via its own Result<T> error code.
        var result = symmetricEncryptionService.Decrypt(payload, associatedData);

        if (result.IsFailure)
        {
            if (result.Error?.Code == CryptographyErrorCodes.UnknownKeyId)
            {
                throw new EncryptionKeyNotFoundException(version);
            }

            // Auth-tag mismatch, tamper, or other crypto failure.
            throw new System.Security.Cryptography.CryptographicException(
                $"Decryption failed for ciphertext version '{version}': {result.Error?.Message}");
        }

        return System.Text.Encoding.UTF8.GetString(result.Value!);
    }
}
