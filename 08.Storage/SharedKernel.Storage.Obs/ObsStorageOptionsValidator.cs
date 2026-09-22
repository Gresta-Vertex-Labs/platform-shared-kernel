using Microsoft.Extensions.Options;

namespace SharedKernel.Storage.Obs;

/// <summary>Validates <see cref="ObsStorageOptions"/> at host startup.</summary>
internal sealed class ObsStorageOptionsValidator : IValidateOptions<ObsStorageOptions>
{
    private const string StandardHostPrefix = "obs.";
    private const string StandardHostSuffix = ".myhuaweicloud.com";

    public ValidateOptionsResult Validate(string? name, ObsStorageOptions options)
    {
        var failures = new List<string>();

        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme is not ("http" or "https"))
        {
            failures.Add($"{nameof(ObsStorageOptions.Endpoint)} must be an absolute http or https URL, e.g. https://obs.tr-west-1.myhuaweicloud.com.");
        }
        else if (ResolveRegion(options) is null)
        {
            failures.Add(
                $"{nameof(ObsStorageOptions.Region)} is required: it cannot be read from endpoint '{endpoint.Host}', which is not obs.{{region}}.myhuaweicloud.com.");
        }

        if (string.IsNullOrWhiteSpace(options.AccessKeyId) || string.IsNullOrWhiteSpace(options.SecretAccessKey))
        {
            failures.Add($"{nameof(ObsStorageOptions.AccessKeyId)} and {nameof(ObsStorageOptions.SecretAccessKey)} are required.");
        }

        if (options.MaxRetries is < 0 or > 10)
        {
            failures.Add($"{nameof(ObsStorageOptions.MaxRetries)} must be between 0 and 10.");
        }

        if (options.RequestTimeout < TimeSpan.FromSeconds(1) || options.RequestTimeout > TimeSpan.FromHours(1))
        {
            failures.Add($"{nameof(ObsStorageOptions.RequestTimeout)} must be between 1 second and 1 hour.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>The configured region, or the one in a standard <c>obs.{region}.myhuaweicloud.com</c> endpoint.</summary>
    internal static string? ResolveRegion(ObsStorageOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Region))
        {
            return options.Region;
        }

        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out Uri? endpoint))
        {
            return null;
        }

        string host = endpoint.Host;
        return host.StartsWith(StandardHostPrefix, StringComparison.OrdinalIgnoreCase)
            && host.EndsWith(StandardHostSuffix, StringComparison.OrdinalIgnoreCase)
            && host.Length > StandardHostPrefix.Length + StandardHostSuffix.Length
                ? host[StandardHostPrefix.Length..^StandardHostSuffix.Length]
                : null;
    }
}
