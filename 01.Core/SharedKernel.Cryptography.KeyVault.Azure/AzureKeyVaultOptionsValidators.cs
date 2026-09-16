using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Cryptography.KeyVault.Azure;

/// <summary>Validates <see cref="AzureKeyVaultEncryptionOptions"/> beyond its Data Annotations.</summary>
internal sealed class AzureKeyVaultEncryptionOptionsValidator : IValidateOptions<AzureKeyVaultEncryptionOptions>
{
    public ValidateOptionsResult Validate(string? name, AzureKeyVaultEncryptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (options.VaultUri is { } uri
            && (!uri.IsAbsoluteUri || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add("Encryption.VaultUri must be an absolute https URI.");
        }

        foreach (string previous in options.PreviousMasterKeyNames ?? [])
        {
            if (!KeyVaultNames.IsName(previous))
            {
                failures.Add($"Encryption.PreviousMasterKeyNames entry '{previous}' is not a valid Key Vault key name.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>Validates <see cref="AzureKeyVaultSigningOptions"/> and each configured key.</summary>
internal sealed class AzureKeyVaultSigningOptionsValidator : IValidateOptions<AzureKeyVaultSigningOptions>
{
    public ValidateOptionsResult Validate(string? name, AzureKeyVaultSigningOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (options.VaultUri is { } uri
            && (!uri.IsAbsoluteUri || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add("Signing.VaultUri must be an absolute https URI.");
        }

        if (options.Keys is null || options.Keys.Count == 0)
        {
            failures.Add("Signing.Keys must configure at least one key.");
        }

        foreach ((string keyId, AzureKeyVaultSigningKeyOptions key) in options.Keys ?? [])
        {
            if (string.IsNullOrWhiteSpace(keyId))
            {
                failures.Add("Signing.Keys must not contain an empty key id.");
            }

            if (key is null)
            {
                failures.Add($"Signing.Keys['{keyId}'] must not be null.");
                continue;
            }

            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(key, new ValidationContext(key), results, validateAllProperties: true))
            {
                failures.AddRange(results.Select(r => $"Signing.Keys['{keyId}']: {r.ErrorMessage}"));
            }

            if (key.Algorithm is { } algorithm && !Enum.IsDefined(algorithm))
            {
                failures.Add($"Signing.Keys['{keyId}'].Algorithm '{algorithm}' is not a supported {nameof(SignatureAlgorithm)}.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
