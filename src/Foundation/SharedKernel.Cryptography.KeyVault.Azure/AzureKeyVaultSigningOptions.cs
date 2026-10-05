using System.ComponentModel.DataAnnotations;
using SharedKernel.Configuration;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Cryptography.KeyVault.Azure;

/// <summary>
/// Settings for <see cref="AzureKeyVaultSigningKeyProvider"/>, bound from
/// <c>SharedKernel:Cryptography:KeyVault:Azure:Signing</c>.
/// </summary>
/// <example>
/// <code>
/// "Signing": {
///   "VaultUri": "https://contoso-prod.vault.azure.net/",
///   "Keys": {
///     "webhooks": { "KeyName": "webhook-signing", "Algorithm": "ES256" },
///     "tokens-2026": { "KeyName": "token-signing", "KeyVersion": "8f1c...", "Algorithm": "PS256" }
///   }
/// }
/// </code>
/// </example>
public sealed class AzureKeyVaultSigningOptions : ISectionBoundOptions
{
    /// <inheritdoc />
    public static string SectionName => "SharedKernel:Cryptography:KeyVault:Azure:Signing";

    /// <summary>The vault URI, for example <c>https://contoso.vault.azure.net/</c>.</summary>
    [Required]
    public Uri? VaultUri { get; set; }

    /// <summary>
    /// The signing keys by the key id callers use. Only these ids are ever looked up in Key Vault. Must not be empty.
    /// </summary>
    public Dictionary<string, AzureKeyVaultSigningKeyOptions> Keys { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// How often an unpinned key's latest version is read again. Defaults to one hour. Must be between one minute and
    /// one day.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>One Key Vault signing key.</summary>
public sealed class AzureKeyVaultSigningKeyOptions
{
    /// <summary>The Key Vault key name.</summary>
    [Required]
    [RegularExpression(KeyVaultNames.NamePattern)]
    public string? KeyName { get; set; }

    /// <summary>
    /// The key version to use, or <see langword="null"/> for the latest. Pin a version when signatures must stay
    /// verifiable after the key is rotated in Key Vault; give the new version its own key id.
    /// </summary>
    [RegularExpression(KeyVaultNames.VersionPattern)]
    public string? KeyVersion { get; set; }

    /// <summary>
    /// The algorithm the key signs with. An RSA key (at least 2048 bits) takes a <c>PS</c> or <c>RS</c> algorithm; an
    /// EC key takes the <c>ES</c> algorithm matching its curve.
    /// </summary>
    [Required]
    public SignatureAlgorithm? Algorithm { get; set; }
}

/// <summary>Key Vault naming rules.</summary>
internal static class KeyVaultNames
{
    public const string NamePattern = "^[0-9a-zA-Z-]{1,127}$";
    public const string VersionPattern = "^[0-9a-f]{32}$";

    public static bool IsVersion(string? value) =>
        value is { Length: 32 } && value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    public static bool IsName(string? value) =>
        value is { Length: >= 1 and <= 127 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
}
