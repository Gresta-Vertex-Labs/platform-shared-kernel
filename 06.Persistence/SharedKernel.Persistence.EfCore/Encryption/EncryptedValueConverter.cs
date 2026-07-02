using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Options;
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
/// <strong>Decryption:</strong> Parses the version prefix. FIRST calls
/// <see cref="IEncryptionKeyProvider.GetKey(string)"/> on the injected key provider — if
/// <see langword="null"/> (version absent from <see cref="EncryptionOptions.Keys"/>), throws
/// <see cref="EncryptionKeyNotFoundException"/> immediately without invoking Decrypt. Otherwise
/// reconstructs an <see cref="EncryptedPayload"/> and calls
/// <c>symmetricEncryptionService.Decrypt(payload)</c>.
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
    /// Initialises a new <see cref="EncryptedValueConverter"/> with cryptography delegation (P-227).
    /// </summary>
    /// <param name="optionsMonitor">Live options monitor for pass-through gating and hot-reload support.</param>
    /// <param name="symmetricEncryptionService">
    /// The cryptographic service that performs AES-256-GCM encrypt/decrypt operations.
    /// Registered by the consuming service via <c>AddSharedKernelCryptography()</c>.
    /// </param>
    /// <param name="keyProvider">
    /// The key provider bridging <see cref="EncryptionOptions"/> to <see cref="IEncryptionKeyProvider"/>.
    /// Used for the pre-check on the decrypt path (unknown version → immediate
    /// <see cref="EncryptionKeyNotFoundException"/> without calling Decrypt).
    /// </param>
    /// <param name="versionOverride">
    /// Optional scoped accessor allowing <see cref="EncryptionRotationService{TContext}"/> to direct
    /// this converter to encrypt with a specific target key version for the duration of a rotation
    /// batch. The override precedence rule <c>OverrideVersion ?? CurrentVersion</c> is now resolved
    /// inside <see cref="EncryptionOptionsKeyProvider.GetCurrentKey()"/> via <paramref name="keyProvider"/>.
    /// </param>
    public EncryptedValueConverter(
        IOptionsMonitor<EncryptionOptions> optionsMonitor,
        ISymmetricEncryptionService symmetricEncryptionService,
        IEncryptionKeyProvider keyProvider,
        IEncryptionVersionOverride? versionOverride = null)
        : base(
            value => Encrypt(value, optionsMonitor.CurrentValue, symmetricEncryptionService),
            stored => Decrypt(stored, optionsMonitor.CurrentValue, symmetricEncryptionService, keyProvider))
    {
    }

    private static string Encrypt(
        string value,
        EncryptionOptions options,
        ISymmetricEncryptionService symmetricEncryptionService)
    {
        if (!options.Enabled)
        {
            return value;
        }

        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(value);
        var payload = symmetricEncryptionService.Encrypt(plaintextBytes);

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
        IEncryptionKeyProvider keyProvider)
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

        // Pre-check: fail fast with EncryptionKeyNotFoundException if the version is unknown.
        // This distinguishes "key removed" from "tamper/auth-tag mismatch" before calling Decrypt.
        if (keyProvider.GetKey(version) is null)
        {
            throw new EncryptionKeyNotFoundException(version);
        }

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
        var result = symmetricEncryptionService.Decrypt(payload);

        if (result.IsFailure)
        {
            // Auth-tag mismatch, tamper, or other crypto failure.
            throw new System.Security.Cryptography.CryptographicException(
                $"Decryption failed for ciphertext version '{version}': {result.Error?.Message}");
        }

        return System.Text.Encoding.UTF8.GetString(result.Value!);
    }
}
