using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace SharedKernel.Persistence.EfCore.Encryption.Configuration;

/// <summary>Validates <see cref="EncryptionOptions"/> when the host starts.</summary>
internal sealed partial class EncryptionOptionsValidator : IValidateOptions<EncryptionOptions>
{
    internal const int MaxBlindIndexVersionLength = 15;

    [GeneratedRegex("^[a-z0-9]{1,15}$")]
    internal static partial Regex BlindIndexVersionPattern { get; }

    public ValidateOptionsResult Validate(string? name, EncryptionOptions options)
    {
        var failures = new List<string>();

        if (options.KeyRefreshInterval <= TimeSpan.Zero)
            failures.Add($"{nameof(EncryptionOptions.KeyRefreshInterval)} must be positive.");
        if (options.MaxKeyStaleness <= TimeSpan.Zero)
            failures.Add($"{nameof(EncryptionOptions.MaxKeyStaleness)} must be positive.");
        if (options.TenantKeyCacheDuration <= TimeSpan.Zero)
            failures.Add($"{nameof(EncryptionOptions.TenantKeyCacheDuration)} must be positive.");
        if (options.AdditionalDecryptionKeyIds.Any(string.IsNullOrWhiteSpace))
            failures.Add($"{nameof(EncryptionOptions.AdditionalDecryptionKeyIds)} must not contain empty ids.");

        var keys = options.Keys;
        if (keys.Keys.Count > 0 || keys.CurrentKeyId is not null)
        {
            if (string.IsNullOrWhiteSpace(keys.CurrentKeyId) || !keys.Keys.ContainsKey(keys.CurrentKeyId))
                failures.Add("Keys:CurrentKeyId must name one of the configured Keys.");
            foreach (var (id, value) in keys.Keys)
            {
                if (DecodeLength(value) != 32)
                    failures.Add($"Keys:Keys:{id} must be base64 for exactly 32 bytes.");
            }
        }

        var blind = options.BlindIndexKeys;
        if (blind.Keys.Count > 0 || blind.CurrentVersion is not null)
        {
            if (string.IsNullOrWhiteSpace(blind.CurrentVersion) || !blind.Keys.ContainsKey(blind.CurrentVersion))
                failures.Add("BlindIndexKeys:CurrentVersion must name one of the configured BlindIndexKeys:Keys.");
            foreach (var (version, value) in blind.Keys)
            {
                if (!BlindIndexVersionPattern.IsMatch(version))
                    failures.Add($"BlindIndexKeys:Keys:{version}: a version is 1-{MaxBlindIndexVersionLength} lowercase letters or digits.");
                if (DecodeLength(value) < 32)
                    failures.Add($"BlindIndexKeys:Keys:{version} must be base64 for at least 32 bytes.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static int DecodeLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var buffer = new byte[value.Length];
        return Convert.TryFromBase64String(value, buffer, out var written) ? written : -1;
    }
}
