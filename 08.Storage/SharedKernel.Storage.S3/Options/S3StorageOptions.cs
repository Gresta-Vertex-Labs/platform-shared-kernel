using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Storage.S3.Options;

/// <summary>
/// Configures the AWS S3 (or MinIO, via <see cref="ServiceUrl"/> + <see cref="ForcePathStyle"/>)
/// object-storage provider.
/// </summary>
/// <remarks>
/// Bound to configuration section <see cref="SectionName"/> and registered via
/// <c>SharedKernel.Configuration</c>'s <c>AddValidatedOptions&lt;TOptions&gt;(IConfigurationSection)</c>,
/// which calls <c>.Bind(section).ValidateDataAnnotations().ValidateOnStart()</c> — a misconfigured
/// service fails at <c>IHost.StartAsync()</c>, not at first upload.
/// </remarks>
public sealed class S3StorageOptions : IValidatableObject
{
    /// <summary>The configuration section path this options type binds to.</summary>
    public const string SectionName = "SharedKernel:Storage:S3";

    /// <summary>
    /// A custom S3-compatible endpoint URL. <see langword="null"/> targets real AWS S3 (using
    /// <see cref="Region"/>); set this to a MinIO (or other S3-compatible) endpoint to redirect the
    /// identical <c>S3FileStorage</c>/<c>S3BlobUriGenerator</c> code path there.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// The AWS region system name (e.g. <c>"eu-central-1"</c>). Required when <see cref="ServiceUrl"/>
    /// is <see langword="null"/>; ignored otherwise.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>The access key id used to authenticate against the provider.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string AccessKeyId { get; set; }

    /// <summary>The secret access key used to authenticate against the provider.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string SecretAccessKey { get; set; }

    /// <summary>
    /// When <see langword="true"/>, addresses buckets as path segments
    /// (<c>https://host/bucket/key</c>) rather than subdomains — required for MinIO and most
    /// self-hosted S3-compatible endpoints. Defaults to <see langword="false"/>.
    /// </summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>An optional convenience default bucket for single-bucket services.</summary>
    public string? DefaultBucket { get; set; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(ServiceUrl) && string.IsNullOrWhiteSpace(Region))
        {
            yield return new ValidationResult(
                $"{nameof(Region)} is required when {nameof(ServiceUrl)} is not set.",
                [nameof(Region)]);
        }
    }
}
