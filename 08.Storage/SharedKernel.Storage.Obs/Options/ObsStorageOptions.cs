using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Storage.Obs.Options;

/// <summary>
/// Configures the Huawei Cloud OBS object-storage provider, consumed over its S3-compatible endpoint.
/// </summary>
/// <remarks>
/// Bound to configuration section <see cref="SectionName"/> and registered via
/// <c>SharedKernel.Configuration</c>'s <c>AddValidatedOptions&lt;TOptions&gt;(IConfigurationSection)</c>,
/// which calls <c>.Bind(section).ValidateDataAnnotations().ValidateOnStart()</c> — a misconfigured
/// service fails at <c>IHost.StartAsync()</c>, not at first upload.
/// </remarks>
public sealed class ObsStorageOptions
{
    /// <summary>The configuration section path this options type binds to.</summary>
    public const string SectionName = "SharedKernel:Storage:Obs";

    /// <summary>
    /// The region OBS S3-compatible endpoint (e.g. <c>"obs.ap-southeast-1.myhuaweicloud.com"</c>).
    /// Required.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string Endpoint { get; set; }

    /// <summary>The OBS access key (AK) used to authenticate against the provider.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string AccessKeyId { get; set; }

    /// <summary>The OBS secret key (SK) used to authenticate against the provider.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string SecretAccessKey { get; set; }

    /// <summary>
    /// When <see langword="true"/>, addresses buckets as path segments
    /// (<c>https://host/bucket/key</c>) rather than subdomains. Defaults to <see langword="false"/>.
    /// </summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>An optional convenience default bucket for single-bucket services.</summary>
    public string? DefaultBucket { get; set; }
}
