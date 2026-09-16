using Microsoft.Extensions.Options;

namespace SharedKernel.Persistence.EfCore.Options;

/// <summary>
/// Configuration options for field-level AES-256-GCM transparent encryption.
/// Bound to the <c>SharedKernel:Encryption</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="Enabled"/> is <see langword="true"/>, startup validation enforces:
/// <list type="bullet">
///   <item><description><see cref="CurrentVersion"/> is non-null and non-empty.</description></item>
///   <item><description><see cref="CurrentVersion"/> exists as a key in <see cref="Keys"/>.</description></item>
///   <item><description>Every value in <see cref="Keys"/> decodes from Base64 to exactly 32 bytes (256-bit AES key).</description></item>
/// </list>
/// </para>
/// <para>
/// When <see cref="Enabled"/> is <see langword="false"/> (default), the
/// <see cref="SharedKernel.Persistence.EfCore.Encryption.EncryptedValueConverter"/> acts as a
/// pass-through — no encryption or decryption is performed.
/// </para>
/// </remarks>
public sealed class EncryptionOptions
{
    /// <summary>
    /// The configuration section path this type binds from —
    /// <c>"SharedKernel:Encryption"</c>. Used by
    /// <c>EfCorePersistenceBuilder.WithEncryption(IConfiguration, ...)</c>
    /// (WO-053/P-334) instead of a bare <c>GetSection("SharedKernel:Encryption")</c> literal at
    /// each call site.
    /// </summary>
    public const string SectionName = "SharedKernel:Encryption";

    /// <summary>
    /// Master on/off switch. Default is <see langword="false"/> (plaintext pass-through).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Version tag used for new encryptions, e.g. <c>"v1"</c>.
    /// Must exist as a key in <see cref="Keys"/> when <see cref="Enabled"/> is <see langword="true"/>.
    /// </summary>
    public string CurrentVersion { get; set; } = string.Empty;

    /// <summary>
    /// Map of version tag → Base64-encoded 32-byte AES-256 key.
    /// Old key versions must remain present until all rows encrypted with them have been rotated.
    /// </summary>
    public Dictionary<string, string> Keys { get; set; } = [];

    /// <summary>
    /// TEMPORARY MIGRATION SETTING. When <see langword="true"/>, a stored value in an encrypted column that is not
    /// an encrypted payload is returned unchanged instead of throwing. Default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Enable this only while migrating a column that already holds unencrypted data: new and updated values are
    /// still encrypted, and re-saving each row (for example with <c>IEncryptionRotationJob</c>) encrypts the rest.
    /// Turn it off as soon as every row is encrypted.
    /// </para>
    /// <para>
    /// <strong>SECURITY:</strong> while enabled, anyone who can write to the database can plant arbitrary
    /// plaintext that the application reads as if it had been decrypted, bypassing AES-GCM integrity entirely.
    /// With the default (<see langword="false"/>), such a value fails closed with a
    /// <see cref="System.Security.Cryptography.CryptographicException"/>.
    /// </para>
    /// </remarks>
    public bool AllowUnencryptedValues { get; set; }
}

/// <summary>
/// Startup validator for <see cref="EncryptionOptions"/>.
/// Fires during DI composition when <c>ValidateOnStart()</c> is registered.
/// </summary>
internal sealed class EncryptionOptionsValidator : IValidateOptions<EncryptionOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, EncryptionOptions options)
    {
        if (!options.Enabled)
        {
            // Disabled — nothing to validate.
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrEmpty(options.CurrentVersion))
        {
            failures.Add("EncryptionOptions.CurrentVersion must be non-null and non-empty when Enabled is true.");
        }
        else if (!options.Keys.ContainsKey(options.CurrentVersion))
        {
            failures.Add(
                $"EncryptionOptions.CurrentVersion '{options.CurrentVersion}' does not exist in EncryptionOptions.Keys.");
        }

        foreach (var (version, base64Key) in options.Keys)
        {
            // The version becomes the key id recorded in every stored payload.
            if (string.IsNullOrWhiteSpace(version)
                || System.Text.Encoding.UTF8.GetByteCount(version) > SharedKernel.Cryptography.Symmetric.CryptographicKey.MaxIdLength)
            {
                failures.Add(
                    $"EncryptionOptions.Keys version '{version}' must be non-whitespace and at most " +
                    $"{SharedKernel.Cryptography.Symmetric.CryptographicKey.MaxIdLength} UTF-8 bytes.");
            }

            var buffer = new byte[64];
            if (!Convert.TryFromBase64String(base64Key, buffer, out var bytesWritten) || bytesWritten != 32)
            {
                failures.Add(
                    $"EncryptionOptions.Keys['{version}'] must decode to exactly 32 bytes (AES-256). " +
                    $"Ensure the value is a valid Base64-encoded 32-byte key.");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
