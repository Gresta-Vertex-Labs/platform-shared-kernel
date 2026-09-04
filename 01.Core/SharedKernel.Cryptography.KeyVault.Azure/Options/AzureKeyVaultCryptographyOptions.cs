using System.ComponentModel.DataAnnotations;
using Azure.Core;

namespace SharedKernel.Cryptography.KeyVault.Azure.Options;

/// <summary>
/// Configuration for <see cref="AzureKeyVaultEncryptionKeyProvider"/>, registered by
/// <see cref="Extensions.AzureKeyVaultCryptographyServiceCollectionExtensions.AddSharedKernelAzureKeyVaultCryptography"/>.
/// </summary>
/// <remarks>
/// Bound via <c>SharedKernel.Configuration</c>'s <c>AddValidatedOptions</c> (Data Annotations,
/// eagerly checked at startup via <c>ValidateOnStart()</c>) plus the additional cross-field
/// invariants enforced by <see cref="AzureKeyVaultCryptographyOptionsValidator"/> that Data
/// Annotations alone cannot express (a non-empty <see cref="KeyNames"/> map, and
/// <see cref="CurrentKeyId"/> naming an entry that actually exists in it).
/// </remarks>
public sealed class AzureKeyVaultCryptographyOptions
{
    /// <summary>The configuration section name this options class binds to.</summary>
    public const string SectionName = "SharedKernel:Cryptography:KeyVault:Azure";

    /// <summary>
    /// A key identifier fragment (our own opaque <c>keyId</c>, and the separator used internally
    /// to pack a wrapped data key into a single <see cref="Symmetric.CryptographicKey.Id"/>
    /// string) must never contain — reserved as a wire-format separator.
    /// </summary>
    internal const char ReservedKeyIdSeparator = ':';

    /// <summary>The Azure Key Vault URI, e.g. <c>https://my-vault.vault.azure.net/</c>.</summary>
    [Required]
    public Uri? VaultUri { get; set; }

    /// <summary>
    /// The <see cref="KeyNames"/> entry that <see cref="AzureKeyVaultEncryptionKeyProvider.GetCurrentKeyAsync"/>
    /// and <see cref="AzureKeyVaultEncryptionKeyProvider.GenerateDataKeyAsync"/> wrap fresh data
    /// keys under. Must name a key present in <see cref="KeyNames"/> — enforced by
    /// <see cref="AzureKeyVaultCryptographyOptionsValidator"/>, not by Data Annotations alone.
    /// </summary>
    [Required]
    public string? CurrentKeyId { get; set; }

    /// <summary>
    /// Maps our own opaque <c>keyId</c> values (never containing <c>':'</c> — see
    /// <see cref="ReservedKeyIdSeparator"/>) to the corresponding Azure Key Vault key <em>name</em>
    /// (not a full URI — just the vault-local key name, e.g. <c>"tenant-data-key"</c>). Must
    /// contain at least one entry, and an entry for <see cref="CurrentKeyId"/> — enforced by
    /// <see cref="AzureKeyVaultCryptographyOptionsValidator"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> KeyNames { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The credential used to authenticate against the vault. Defaults to
    /// <see cref="Azure.Identity.DefaultAzureCredential"/> when <see langword="null"/> — never
    /// bound from <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> (a
    /// <see cref="TokenCredential"/> is not a configuration-bindable POCO), so a consumer wanting
    /// a specific credential sets this property programmatically after binding, e.g. via
    /// <c>services.PostConfigure&lt;AzureKeyVaultCryptographyOptions&gt;(o =&gt; o.Credential = myCredential)</c>.
    /// </summary>
    public TokenCredential? Credential { get; set; }
}
