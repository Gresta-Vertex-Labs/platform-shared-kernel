using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Hashing;

namespace SharedKernel.Cryptography.Options;

/// <summary>Validates the parts of <see cref="CryptographyOptions"/> that Data Annotations cannot express.</summary>
/// <remarks>
/// It depends on the registered <see cref="IOneWayHashAlgorithm"/> instances, so no algorithm may depend on
/// <c>IOptions&lt;CryptographyOptions&gt;</c>: that would form a dependency cycle. Algorithms bind their own options
/// types, such as <see cref="Pbkdf2Options"/>.
/// </remarks>
internal sealed class CryptographyOptionsValidator(IEnumerable<IOneWayHashAlgorithm> algorithms)
    : IValidateOptions<CryptographyOptions>
{
    public ValidateOptionsResult Validate(string? name, CryptographyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        OneWayHashingOptions hashing = options.OneWayHashing;

        if (hashing is null)
        {
            return ValidateOptionsResult.Fail($"{nameof(CryptographyOptions.OneWayHashing)} must not be null.");
        }

        string[] registered = [.. algorithms.Select(a => a.AlgorithmId)];
        if (!string.IsNullOrEmpty(hashing.Algorithm) && !registered.Contains(hashing.Algorithm, StringComparer.Ordinal))
        {
            failures.Add(
                $"OneWayHashing.Algorithm '{hashing.Algorithm}' is not registered. Registered: {string.Join(", ", registered)}.");
        }

        foreach ((string pepperId, string value) in hashing.Peppers ?? [])
        {
            if (!PepperKeys.IsValidId(pepperId))
            {
                failures.Add($"OneWayHashing.Peppers id '{pepperId}' must be 1-32 letters, digits or hyphens.");
            }

            if (!PepperKeys.TryDecode(value, out byte[]? bytes))
            {
                failures.Add(
                    $"OneWayHashing.Peppers['{pepperId}'] must be Base64 of at least {OneWayHashingOptions.MinimumPepperBytes} bytes.");
            }
            else
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
            }
        }

        if (hashing.CurrentPepperId is not null && hashing.Peppers?.ContainsKey(hashing.CurrentPepperId) != true)
        {
            failures.Add($"OneWayHashing.CurrentPepperId '{hashing.CurrentPepperId}' does not name an entry in Peppers.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
