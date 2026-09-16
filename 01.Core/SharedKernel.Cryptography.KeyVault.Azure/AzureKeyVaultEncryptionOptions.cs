using System.ComponentModel.DataAnnotations;
using SharedKernel.Configuration;

namespace SharedKernel.Cryptography.KeyVault.Azure;

/// <summary>
/// Settings for <see cref="AzureKeyVaultEncryptionKeyProvider"/>, bound from
/// <c>SharedKernel:Cryptography:KeyVault:Azure:Encryption</c>.
/// </summary>
/// <example>
/// <code>
/// "Encryption": {
///   "VaultUri": "https://contoso-prod.vault.azure.net/",
///   "MasterKeyName": "orders-kek",
///   "DataKeySecretName": "orders-data-keys",
///   "RefreshInterval": "00:05:00"
/// }
/// </code>
/// </example>
public sealed class AzureKeyVaultEncryptionOptions : ISectionBoundOptions
{
    /// <inheritdoc />
    public static string SectionName => "SharedKernel:Cryptography:KeyVault:Azure:Encryption";

    /// <summary>The vault URI, for example <c>https://contoso.vault.azure.net/</c>.</summary>
    [Required]
    public Uri? VaultUri { get; set; }

    /// <summary>
    /// The name of the Key Vault key that wraps new data keys. It must be an RSA key (wrapped with RSA-OAEP-256) or,
    /// in a managed HSM, an AES key (wrapped with AES key wrap).
    /// </summary>
    [Required]
    [RegularExpression(KeyVaultNames.NamePattern)]
    public string? MasterKeyName { get; set; }

    /// <summary>
    /// Earlier master key names still accepted when unwrapping, after <see cref="MasterKeyName"/> moves to a new key.
    /// Remove a name once no stored data key uses it.
    /// </summary>
    public List<string> PreviousMasterKeyNames { get; set; } = [];

    /// <summary>
    /// The name of the Key Vault secret whose versions hold the wrapped data keys. Each version is one data key; the
    /// latest enabled version is the current key, and disabling a version retires its key.
    /// </summary>
    [Required]
    [RegularExpression(KeyVaultNames.NamePattern)]
    public string? DataKeySecretName { get; set; }

    /// <summary>
    /// How often the current data key and the list of data key versions are read again from Key Vault. Defaults to
    /// five minutes; a rotation reaches every replica within this interval. Must be between one second and one day.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);
}
