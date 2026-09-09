using Microsoft.Extensions.Options;

namespace SharedKernel.Cryptography.KeyVault.Azure.Options;

/// <summary>
/// Validates the cross-field invariants of <see cref="AzureKeyVaultCryptographyOptions"/> that
/// Data Annotations attributes alone cannot express.
/// </summary>
/// <remarks>
/// Registered by
/// <see cref="Extensions.AzureKeyVaultCryptographyServiceCollectionExtensions.AddSharedKernelAzureKeyVaultCryptography"/>
/// via <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;IValidateOptions&lt;AzureKeyVaultCryptographyOptions&gt;, AzureKeyVaultCryptographyOptionsValidator&gt;())</c>
/// — deliberately <c>TryAddEnumerable</c>, never a plain <c>TryAddSingleton</c>, since
/// <see cref="IValidateOptions{TOptions}"/> is a genuine multi-implementation collection: the
/// Data Annotations pass (<c>[Required]</c> on <see cref="AzureKeyVaultCryptographyOptions.VaultUri"/>/
/// <see cref="AzureKeyVaultCryptographyOptions.CurrentKeyId"/>) that <c>SharedKernel.Configuration</c>'s
/// <c>AddValidatedOptions</c> already applies registers against the identical service type, and
/// both validators must run at startup via <c>ValidateOnStart()</c> — not just whichever
/// registered first.
/// </remarks>
public sealed class AzureKeyVaultCryptographyOptionsValidator : IValidateOptions<AzureKeyVaultCryptographyOptions>
{
    /// <summary>
    /// Fails when <see cref="AzureKeyVaultCryptographyOptions.KeyNames"/> is empty, when any of
    /// its keys contains the reserved <c>':'</c> wire-format separator (see
    /// <see cref="AzureKeyVaultCryptographyOptions.ReservedKeyIdSeparator"/>), or when
    /// <see cref="AzureKeyVaultCryptographyOptions.CurrentKeyId"/> does not name an entry present
    /// in <see cref="AzureKeyVaultCryptographyOptions.KeyNames"/>.
    /// </summary>
    /// <param name="name">The named options instance being validated (unused — this options type is not named).</param>
    /// <param name="options">The <see cref="AzureKeyVaultCryptographyOptions"/> instance to validate.</param>
    public ValidateOptionsResult Validate(string? name, AzureKeyVaultCryptographyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.KeyNames.Count == 0)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(AzureKeyVaultCryptographyOptions)}.{nameof(AzureKeyVaultCryptographyOptions.KeyNames)} " +
                "must contain at least one keyId -> Azure Key Vault key name mapping.");
        }

        string[] reservedSeparatorViolations = [.. options.KeyNames.Keys
            .Where(keyId => keyId.Contains(AzureKeyVaultCryptographyOptions.ReservedKeyIdSeparator))];

        if (reservedSeparatorViolations.Length > 0)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(AzureKeyVaultCryptographyOptions)}.{nameof(AzureKeyVaultCryptographyOptions.KeyNames)} " +
                $"keyId(s) must not contain the reserved '{AzureKeyVaultCryptographyOptions.ReservedKeyIdSeparator}' " +
                $"character (used internally to encode a wrapped data key into a single key identifier " +
                $"string): {string.Join(", ", reservedSeparatorViolations)}.");
        }

        if (!string.IsNullOrWhiteSpace(options.CurrentKeyId) && !options.KeyNames.ContainsKey(options.CurrentKeyId))
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(AzureKeyVaultCryptographyOptions)}.{nameof(AzureKeyVaultCryptographyOptions.CurrentKeyId)} " +
                $"('{options.CurrentKeyId}') does not name an entry present in " +
                $"{nameof(AzureKeyVaultCryptographyOptions.KeyNames)}. Registered keyId(s): " +
                $"{string.Join(", ", options.KeyNames.Keys)}.");
        }

        return ValidateOptionsResult.Success;
    }
}
