using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// EF Core value converter that transparently encrypts and decrypts <see langword="string"/> EF Core
/// properties using AES-256-GCM.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Encryption:</strong> Uses a random 12-byte nonce per operation. Ciphertext format:
/// <c>"v{version}:{Base64(nonce || ciphertext || 16-byte-auth-tag)}"</c>.
/// </para>
/// <para>
/// <strong>Decryption:</strong> Parses the version prefix, looks up the AES key from
/// <c>EncryptionOptions.Keys</c>, extracts the nonce (first 12 bytes) and authentication tag
/// (last 16 bytes), then decrypts the middle bytes.
/// </para>
/// <para>
/// <strong>Legacy plaintext:</strong> Stored values without a <c>"v"</c> prefix are returned
/// unchanged — safe migration path from unencrypted columns.
/// </para>
/// <para>
/// <strong>Pass-through mode:</strong> When <c>EncryptionOptions.Enabled == false</c>, both
/// encrypt and decrypt directions return the value unchanged. No AES operations are performed.
/// </para>
/// <para>
/// <strong>Hot-reload safe:</strong> Reads <see cref="IOptionsMonitor{T}.CurrentValue"/> on every
/// call so key and version changes take effect without a service restart.
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
    /// Initialises a new <see cref="EncryptedValueConverter"/>.
    /// </summary>
    /// <param name="optionsMonitor">Live options monitor for hot-reload support.</param>
    /// <param name="versionOverride">
    /// Optional scoped accessor allowing <see cref="EncryptionRotationService{TContext}"/> to direct
    /// this converter to encrypt with a specific target key version for the duration of a rotation
    /// batch. When <see langword="null"/> or when <see cref="IEncryptionVersionOverride.OverrideVersion"/>
    /// is <see langword="null"/>, <see cref="EncryptionOptions.CurrentVersion"/> is used.
    /// </param>
    public EncryptedValueConverter(
        IOptionsMonitor<EncryptionOptions> optionsMonitor,
        IEncryptionVersionOverride? versionOverride = null)
        : base(
            value => Encrypt(value, optionsMonitor.CurrentValue, versionOverride),
            stored => Decrypt(stored, optionsMonitor.CurrentValue))
    {
    }

    private static string Encrypt(string value, EncryptionOptions options, IEncryptionVersionOverride? versionOverride)
    {
        if (!options.Enabled)
        {
            return value;
        }

        var version = versionOverride?.OverrideVersion ?? options.CurrentVersion;
        var keyBytes = Convert.FromBase64String(options.Keys[version]);

        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var plaintext = System.Text.Encoding.UTF8.GetBytes(value);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(keyBytes, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Combine: nonce (12) || ciphertext (n) || tag (16)
        var combined = new byte[NonceSizeBytes + ciphertext.Length + TagSizeBytes];
        nonce.CopyTo(combined, 0);
        ciphertext.CopyTo(combined, NonceSizeBytes);
        tag.CopyTo(combined, NonceSizeBytes + ciphertext.Length);

        return $"v{version}:{Convert.ToBase64String(combined)}";
    }

    private static string Decrypt(string stored, EncryptionOptions options)
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

        if (!options.Keys.TryGetValue(version, out var base64Key))
        {
            throw new EncryptionKeyNotFoundException(version);
        }

        var keyBytes = Convert.FromBase64String(base64Key);
        var combined = Convert.FromBase64String(base64Payload);

        if (combined.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("Stored ciphertext payload is too short to be valid.");
        }

        var nonce = combined[..NonceSizeBytes];
        var tag = combined[^TagSizeBytes..];
        var ciphertext = combined[NonceSizeBytes..^TagSizeBytes];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(keyBytes, TagSizeBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return System.Text.Encoding.UTF8.GetString(plaintext);
    }
}
