using SharedKernel.Configuration;

namespace SharedKernel.Storage.Obs;

/// <summary>
/// The connection to Huawei Cloud OBS: endpoint, region, credentials, retries and timeout. Bound from
/// <c>SharedKernel:Storage:Obs</c> by <c>AddObs</c>. Stores on it are configured under
/// <c>SharedKernel:Storage:Stores:{name}</c> (<see cref="S3.S3StoreOptions"/>), exactly like S3 stores.
/// </summary>
/// <remarks>
/// Validated at host startup (<c>OptionsValidationException</c> from <c>IHost.StartAsync</c>, naming the
/// setting). The values are read once, when the client is created on first use. Bind the credentials from a
/// secret store, never from a committed file.
/// </remarks>
/// <example>
/// <code>
/// "SharedKernel": { "Storage": {
///   "Obs": { "Endpoint": "https://obs.tr-west-1.myhuaweicloud.com", "AccessKeyId": "...", "SecretAccessKey": "..." },
///   "Stores": { "archive": { "Bucket": "acme-archive" } } } }
/// </code>
/// </example>
public sealed class ObsStorageOptions : ISectionBoundOptions
{
    /// <summary>Gets the configuration section: <c>SharedKernel:Storage:Obs</c>.</summary>
    public static string SectionName => "SharedKernel:Storage:Obs";

    /// <summary>
    /// Gets or sets the regional OBS endpoint, e.g. <c>https://obs.tr-west-1.myhuaweicloud.com</c>. Required: an
    /// absolute <c>http</c> or <c>https</c> URL.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the region requests are signed for, e.g. <c>tr-west-1</c>. Defaults to the region in a
    /// standard <c>obs.{region}.myhuaweicloud.com</c> endpoint; required for any other endpoint host.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>Gets or sets the access key (AK). Required.</summary>
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>Gets or sets the secret key (SK). Required.</summary>
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the security token of temporary credentials (an STS token from an agency); leave unset for a
    /// permanent AK/SK. The token is not refreshed: the connection stops working when it expires.
    /// </summary>
    public string? SecurityToken { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether buckets are addressed as a path segment rather than a host name.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>
    /// Gets or sets how many times a throttled or failed request is retried (standard retry mode). Defaults to 3;
    /// 0 to 10. A request still failing after the retries returns <see cref="StorageErrorCodes.Unavailable"/>.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets how long one HTTP request may take; each multipart part is its own request. Defaults to
    /// 100 seconds; 1 second to 1 hour.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(100);
}
