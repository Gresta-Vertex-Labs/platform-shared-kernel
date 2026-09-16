using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// EF Core value converter that transparently encrypts and decrypts <see langword="string"/> EF Core
/// properties by delegating all cryptographic operations to <see cref="ISynchronousSymmetricEncryptionService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Synchronous by necessity:</strong> EF Core value converters have no asynchronous path, so this
/// converter uses <see cref="ISynchronousSymmetricEncryptionService"/>, whose key provider must answer from
/// memory (<see cref="ISynchronousEncryptionKeyProvider"/>). The config-backed default serves keys from
/// <see cref="EncryptionOptions"/>; a KMS-backed <see cref="IEncryptionKeyProvider"/> is bridged by
/// <see cref="PreWarmedEncryptionKeyProvider"/>, which is warmed asynchronously before reads and writes.
/// </para>
/// <para>
/// <strong>Stored format:</strong> the canonical <see cref="EncryptedPayload.ToString"/> encoding (unpadded
/// Base64Url of the versioned payload layout, which records the key id). No format of this package's own.
/// </para>
/// <para>
/// <strong>Decryption:</strong> a stored value is parsed with
/// <see cref="EncryptedPayload.TryParse(string, out EncryptedPayload)"/> and decrypted. A failed result whose
/// <c>Error.Code == CryptographyErrorCodes.UnknownKeyId</c> (the stored key id is not available) is mapped to
/// <see cref="EncryptionKeyNotFoundException"/>; any other failure (authentication tag mismatch, tamper, wrong key,
/// wrong associated data) is mapped to <see cref="CryptographicException"/>.
/// </para>
/// <para>
/// <strong>Associated data / AAD:</strong> every encrypt/decrypt call this converter performs binds a fixed
/// <c>associatedData</c> byte array — supplied once at construction by <see cref="EncryptionModelConvention"/>,
/// computed from the annotated property's stable table+column storage identity (or an explicit
/// <c>associatedDataOverride</c>) — into the AES-GCM authentication tag. This closes the column-splicing hazard: a
/// ciphertext copied from one encrypted column into a DIFFERENT encrypted column fails authentication instead of
/// silently "decrypting". AAD is bound to the COLUMN's identity, not row content, so a value copied to another ROW
/// of the SAME column still decrypts — a documented, accepted weaker bound than full row-level binding. RENAMING a
/// table/column that carries <c>.Encrypt()</c> changes this value and makes every existing row's ciphertext for
/// that column permanently undecryptable (surfaces as <see cref="CryptographicException"/>) unless an explicit
/// <c>associatedDataOverride</c> was supplied up front.
/// </para>
/// <para>
/// <strong>Fails closed:</strong> a stored value that is not a well-formed encrypted payload (plaintext written
/// directly to the database, a truncated or otherwise altered value) throws <see cref="CryptographicException"/>
/// naming the property, never the value — otherwise anyone with database write access could plant plaintext the
/// application reads as if it had been decrypted. Only while <see cref="EncryptionOptions.AllowUnencryptedValues"/>
/// is set (a temporary migration setting for columns that still hold unencrypted data) is such a value returned
/// unchanged.
/// </para>
/// <para>
/// <strong>Pass-through mode:</strong> When <c>EncryptionOptions.Enabled == false</c>, both encrypt and decrypt
/// directions return the value unchanged. No encryption service calls are made.
/// </para>
/// <para>
/// <strong>Do NOT instantiate directly</strong> in <c>IEntityTypeConfiguration</c> — use the
/// <c>.Encrypt()</c> extension on <c>PropertyBuilder&lt;T&gt;</c> instead (SK0304).
/// </para>
/// </remarks>
public sealed class EncryptedValueConverter : ValueConverter<string, string>
{
    /// <summary>
    /// Initialises a new <see cref="EncryptedValueConverter"/>.
    /// </summary>
    /// <param name="optionsMonitor">Live options monitor for pass-through gating and hot-reload support.</param>
    /// <param name="symmetricEncryptionService">
    /// The synchronous AES-256-GCM service that performs every encrypt/decrypt operation.
    /// </param>
    /// <param name="associatedData">
    /// Additional authenticated data (AAD) bound into the AES-GCM authentication tag for every encrypt/decrypt
    /// call this converter instance performs — computed once by <see cref="EncryptionModelConvention"/> at
    /// model-finalization time from the annotated property's stable table+column storage identity (or an explicit
    /// <c>associatedDataOverride</c> supplied to <c>.Encrypt()</c>). A ciphertext produced by one column's converter
    /// fails authentication (<see cref="CryptographicException"/>) if fed through a DIFFERENT column's converter.
    /// <strong>RENAME HAZARD:</strong> renaming the underlying table/column changes this value and makes every
    /// existing row's ciphertext for that column permanently undecryptable unless an explicit
    /// <c>associatedDataOverride</c> was supplied up front.
    /// </param>
    /// <param name="versionOverride">
    /// Optional rotation-target-version accessor. The converter itself does not read it: the precedence rule
    /// <c>OverrideVersion ?? CurrentVersion</c> is applied by the key provider behind
    /// <paramref name="symmetricEncryptionService"/>, which must share the same instance.
    /// </param>
    /// <param name="propertyName">
    /// The property this converter serves (e.g. <c>"Customer.Email"</c>), named in exception messages. Never the
    /// stored value. <see cref="EncryptionModelConvention"/> supplies it.
    /// </param>
    public EncryptedValueConverter(
        IOptionsMonitor<EncryptionOptions> optionsMonitor,
        ISynchronousSymmetricEncryptionService symmetricEncryptionService,
        byte[] associatedData,
        IEncryptionVersionOverride? versionOverride = null,
        string? propertyName = null)
        : base(
            value => Encrypt(value, optionsMonitor.CurrentValue, symmetricEncryptionService, associatedData),
            stored => Decrypt(stored, optionsMonitor.CurrentValue, symmetricEncryptionService, associatedData, propertyName))
    {
    }

    private static string Encrypt(
        string value,
        EncryptionOptions options,
        ISynchronousSymmetricEncryptionService symmetricEncryptionService,
        byte[] associatedData) =>
        options.Enabled ? symmetricEncryptionService.EncryptToString(value, associatedData) : value;

    private static string Decrypt(
        string stored,
        EncryptionOptions options,
        ISynchronousSymmetricEncryptionService symmetricEncryptionService,
        byte[] associatedData,
        string? propertyName)
    {
        if (!options.Enabled)
        {
            return stored;
        }

        if (!EncryptedPayload.TryParse(stored, out var payload))
        {
            if (options.AllowUnencryptedValues)
            {
                // Temporary migration setting: a value written before the column was encrypted.
                return stored;
            }

            // Fail closed. The message deliberately never includes the stored value.
            throw new CryptographicException(
                $"The stored value of encrypted property '{propertyName ?? "(unknown)"}' is not an encrypted payload. " +
                "It was written unencrypted, truncated or otherwise altered. To read columns that still hold " +
                "unencrypted data during a migration, set EncryptionOptions.AllowUnencryptedValues temporarily.");
        }

        var result = symmetricEncryptionService.Decrypt(payload, associatedData);

        if (result.IsFailure)
        {
            if (result.Error.Code == CryptographyErrorCodes.UnknownKeyId)
            {
                throw new EncryptionKeyNotFoundException(payload.KeyId);
            }

            throw new CryptographicException(
                $"Decryption failed for encrypted property '{propertyName ?? "(unknown)"}' " +
                $"(ciphertext version '{payload.KeyId}'): {result.Error.Message}");
        }

        var plaintext = result.Value;
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
