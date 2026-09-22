using Microsoft.Extensions.Options;

namespace SharedKernel.Storage.S3.Internal;

/// <summary>Validates <see cref="S3StorageOptions"/> at host startup.</summary>
internal sealed class S3StorageOptionsValidator : IValidateOptions<S3StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, S3StorageOptions options)
    {
        var failures = new List<string>();
        string connection = string.IsNullOrEmpty(name) ? "S3 connection" : $"S3 connection '{name}'";

        if (string.IsNullOrWhiteSpace(options.ServiceUrl) && string.IsNullOrWhiteSpace(options.Region))
        {
            failures.Add($"{nameof(S3StorageOptions.Region)} is required when {nameof(S3StorageOptions.ServiceUrl)} is not set.");
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl)
            && (!Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")))
        {
            failures.Add($"{nameof(S3StorageOptions.ServiceUrl)} must be an absolute http or https URL.");
        }

        if (string.IsNullOrWhiteSpace(options.AccessKeyId) != string.IsNullOrWhiteSpace(options.SecretAccessKey))
        {
            failures.Add(
                $"Set both {nameof(S3StorageOptions.AccessKeyId)} and {nameof(S3StorageOptions.SecretAccessKey)}, or neither to use the default AWS credential chain.");
        }

        if (!string.IsNullOrWhiteSpace(options.SessionToken) && string.IsNullOrWhiteSpace(options.AccessKeyId))
        {
            failures.Add($"{nameof(S3StorageOptions.SessionToken)} requires {nameof(S3StorageOptions.AccessKeyId)}.");
        }

        if (options.MaxRetries is < 0 or > 10)
        {
            failures.Add($"{nameof(S3StorageOptions.MaxRetries)} must be between 0 and 10.");
        }

        if (options.RequestTimeout < TimeSpan.FromSeconds(1) || options.RequestTimeout > TimeSpan.FromHours(1))
        {
            failures.Add($"{nameof(S3StorageOptions.RequestTimeout)} must be between 1 second and 1 hour.");
        }

        if (options.Compatibility is null)
        {
            failures.Add($"{nameof(S3StorageOptions.Compatibility)} is required.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures.Select(f => $"{connection}: {f}"));
    }
}

/// <summary>Validates every named <see cref="S3StoreOptions"/> at host startup.</summary>
internal sealed class S3StoreOptionsValidator : IValidateOptions<S3StoreOptions>
{
    internal static readonly TimeSpan MaxPresignExpiryLimit = TimeSpan.FromDays(7);
    private const long MinPartSize = 5L * 1024 * 1024;
    private const long MaxPartSize = 5L * 1024 * 1024 * 1024;

    public ValidateOptionsResult Validate(string? name, S3StoreOptions options)
    {
        var failures = new List<string>();
        string store = $"Storage store '{name}'";

        if (!IsValidBucketName(options.Bucket))
        {
            failures.Add(
                $"{store}: {nameof(S3StoreOptions.Bucket)} '{options.Bucket}' must be 3 to 63 lower-case letters, digits, '.' and '-', starting and ending with a letter or digit (configure {S3StoreOptions.SectionFor(name ?? string.Empty)}:Bucket).");
        }

        if (options.KeyPrefix is { Length: > 0 } prefix
            && (StorageValidation.ValidateKey(prefix) is not null || !prefix.EndsWith('/')))
        {
            failures.Add($"{store}: {nameof(S3StoreOptions.KeyPrefix)} must be a valid key ending with '/'.");
        }

        if (!Enum.IsDefined(options.Encryption))
        {
            failures.Add($"{store}: {nameof(S3StoreOptions.Encryption)} '{options.Encryption}' is not defined.");
        }

        if (!string.IsNullOrWhiteSpace(options.KmsKeyId) && options.Encryption != S3Encryption.Kms)
        {
            failures.Add($"{store}: {nameof(S3StoreOptions.KmsKeyId)} requires {nameof(S3StoreOptions.Encryption)} = Kms.");
        }

        if (!Enum.IsDefined(options.DefaultTier))
        {
            failures.Add($"{store}: {nameof(S3StoreOptions.DefaultTier)} '{options.DefaultTier}' is not defined.");
        }

        if (options.MaxPresignExpiry <= TimeSpan.Zero || options.MaxPresignExpiry > MaxPresignExpiryLimit)
        {
            failures.Add($"{store}: {nameof(S3StoreOptions.MaxPresignExpiry)} must be positive and at most 7 days.");
        }

        if (options.MultipartPartSize is < MinPartSize or > MaxPartSize)
        {
            failures.Add($"{store}: {nameof(S3StoreOptions.MultipartPartSize)} must be between 5 MiB and 5 GiB.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsValidBucketName(string? bucket) =>
        bucket is { Length: >= 3 and <= 63 }
        && (char.IsAsciiLetterLower(bucket[0]) || char.IsAsciiDigit(bucket[0]))
        && (char.IsAsciiLetterLower(bucket[^1]) || char.IsAsciiDigit(bucket[^1]))
        && bucket.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-')
        && !bucket.Contains("..", StringComparison.Ordinal);
}
