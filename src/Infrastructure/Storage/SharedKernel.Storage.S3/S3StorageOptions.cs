using SharedKernel.Configuration;

namespace SharedKernel.Storage.S3;

/// <summary>
/// The connection to Amazon S3 or an S3-compatible endpoint: region or endpoint, credentials, retries, timeout
/// and feature support. Bound from <c>SharedKernel:Storage:S3</c> by the default <c>AddS3</c>, or from
/// <c>SharedKernel:Storage:S3:{connectionName}</c> by the named overload. Stores on the connection are configured
/// separately, under <c>SharedKernel:Storage:Stores:{name}</c> (<see cref="S3StoreOptions"/>).
/// </summary>
/// <remarks>
/// <para>
/// Leave <see cref="AccessKeyId"/> and <see cref="SecretAccessKey"/> unset on AWS: the SDK's default credential
/// chain then finds, in order, environment variables, the shared profile, a web identity token (IRSA), EKS Pod
/// Identity, the ECS task role or the EC2 instance profile, with rotating temporary credentials. Static keys are
/// for MinIO and local development; bind them from a secret store, never from a committed file.
/// </para>
/// <para>
/// Validated at host startup (<c>OptionsValidationException</c> from <c>IHost.StartAsync</c>, naming the
/// connection and setting). The values are read once, when the connection's client is created.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // AWS, credentials from the pod's IAM role:
/// "SharedKernel": { "Storage": { "S3": { "Region": "eu-central-1" } } }
///
/// // MinIO:
/// "SharedKernel": { "Storage": { "S3": {
///   "ServiceUrl": "http://minio:9000", "ForcePathStyle": true, "Region": "us-east-1",
///   "AccessKeyId": "...", "SecretAccessKey": "..." } } }
/// </code>
/// </example>
public sealed class S3StorageOptions : ISectionBoundOptions
{
    /// <summary>
    /// Gets the configuration section of the default connection: <c>SharedKernel:Storage:S3</c>. A named
    /// connection reads the sub-section <c>SharedKernel:Storage:S3:{connectionName}</c>.
    /// </summary>
    public static string SectionName => "SharedKernel:Storage:S3";

    /// <summary>
    /// Gets or sets the AWS region, e.g. <c>eu-central-1</c>. Required unless <see cref="ServiceUrl"/> is set; with
    /// <see cref="ServiceUrl"/>, it is the region requests are signed for (optional there). A bucket in another
    /// region than its connection fails with <see cref="StorageErrorCodes.ProviderError"/> and is logged as
    /// EventId 8105.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Gets or sets the endpoint of an S3-compatible service, e.g. <c>http://minio:9000</c>: an absolute
    /// <c>http</c> or <c>https</c> URL. Leave unset for AWS. With an <c>http</c> endpoint, presigned URLs use
    /// <c>http</c> too.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether buckets are addressed as a path segment
    /// (<c>https://host/bucket/key</c>) rather than a host name (<c>https://bucket.host/key</c>). Defaults to
    /// <see langword="false"/>; MinIO usually needs <see langword="true"/>.
    /// </summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>
    /// Gets or sets a static access key id. Leave unset to use the default AWS credential chain; when set,
    /// <see cref="SecretAccessKey"/> is required too.
    /// </summary>
    public string? AccessKeyId { get; set; }

    /// <summary>Gets or sets the secret for <see cref="AccessKeyId"/>; set both or neither.</summary>
    public string? SecretAccessKey { get; set; }

    /// <summary>
    /// Gets or sets the session token of temporary static credentials; requires <see cref="AccessKeyId"/>. Such
    /// credentials are not refreshed: prefer the default credential chain for anything long-running.
    /// </summary>
    public string? SessionToken { get; set; }

    /// <summary>
    /// Gets or sets how many times the SDK retries a throttled or failed request (standard retry mode, with
    /// backoff). Defaults to 3; 0 to 10. A request still failing after the retries returns
    /// <see cref="StorageErrorCodes.Unavailable"/>.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Gets or sets how long one HTTP request may take. Defaults to 100 seconds; 1 second to 1 hour.</summary>
    /// <remarks>
    /// Each part of a multipart upload is its own request, so this bounds a part, not a whole upload; a single
    /// <c>PUT</c> (for example with a checksum) must finish within it. A timeout counts as a failed attempt and
    /// ends in <see cref="StorageErrorCodes.Unavailable"/>.
    /// </remarks>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// Gets or sets which S3 features the endpoint supports. Defaults to everything, as on AWS and current MinIO.
    /// Required (not <see langword="null"/>).
    /// </summary>
    /// <remarks>
    /// Switch off what an S3-compatible service lacks, so a request that needs it fails with
    /// <see cref="StorageErrorCodes.NotSupported"/> instead of being silently ignored.
    /// </remarks>
    public S3Compatibility Compatibility { get; set; } = new();
}
