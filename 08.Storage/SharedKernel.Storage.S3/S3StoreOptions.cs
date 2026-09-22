namespace SharedKernel.Storage.S3;

/// <summary>
/// One named store on an S3 or OBS connection: bucket, key prefix, encryption, default tier, presign limit and
/// part size. Bound from <c>SharedKernel:Storage:Stores:{name}</c> and then from the <c>configure</c> delegate of
/// <see cref="S3StorageBuilder.AddStore"/> / <see cref="S3StorageBuilder.AddTenantStore"/>, as a named options
/// instance under the store name.
/// </summary>
/// <remarks>
/// Validated at host startup (<c>OptionsValidationException</c> from <c>IHost.StartAsync</c>, naming the store and
/// setting). The values are read once, when the store is first used; changing configuration later has no effect.
/// </remarks>
/// <example>
/// <code>
/// "SharedKernel": { "Storage": { "Stores": {
///   "invoices":  { "Bucket": "acme-invoices", "Encryption": "Kms", "KmsKeyId": "alias/invoices" },
///   "documents": { "Bucket": "acme-shared", "KeyPrefix": "documents/", "MaxPresignExpiry": "00:15:00" } } } }
/// </code>
/// </example>
public sealed class S3StoreOptions
{
    /// <summary>Gets the configuration section of the store named <paramref name="storeName"/>.</summary>
    /// <param name="storeName">The store name.</param>
    /// <returns><c>SharedKernel:Storage:Stores:{storeName}</c>; the same for S3 and OBS stores.</returns>
    public static string SectionFor(string storeName) => $"SharedKernel:Storage:Stores:{storeName}";

    /// <summary>
    /// Gets or sets the bucket. Required: 3 to 63 lower-case letters, digits, <c>.</c> and <c>-</c>, starting and
    /// ending with a letter or digit, without <c>..</c>. The bucket must exist; the store never creates it.
    /// </summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a prefix every key of the store lives under, so several stores can share a bucket: a valid key
    /// ending with <c>/</c>, e.g. <c>exports/</c>. Defaults to none. Keys the application sees never include it,
    /// but it counts toward <see cref="StorageValidation.MaxKeyBytes"/>. Tenant stores put
    /// <c>tenants/{tenantId}/</c> after it.
    /// </summary>
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// Gets or sets the server-side encryption requested for every object the store writes — uploads, copies,
    /// multipart uploads, presigned uploads and forms. Defaults to <see cref="S3Encryption.BucketDefault"/>, which
    /// sends no encryption header.
    /// </summary>
    public S3Encryption Encryption { get; set; }

    /// <summary>
    /// Gets or sets the KMS key id, ARN or alias (<c>alias/name</c>) for <see cref="S3Encryption.Kms"/>; unset uses
    /// the account's AWS-managed <c>aws/s3</c> key. Setting it with any other <see cref="Encryption"/> fails
    /// validation.
    /// </summary>
    public string? KmsKeyId { get; set; }

    /// <summary>
    /// Gets or sets the tier objects are written to when a request leaves its tier at
    /// <see cref="StorageTier.Default"/>. Defaults to <see cref="StorageTier.Default"/> (S3 <c>STANDARD</c>);
    /// <see cref="StorageTier.InfrequentAccess"/> writes <c>STANDARD_IA</c>. Also applied to presigned uploads and
    /// forms.
    /// </summary>
    public StorageTier DefaultTier { get; set; }

    /// <summary>
    /// Gets or sets the longest expiry a presigned URL or form of this store may have. Defaults to 1 hour; positive
    /// and at most 7 days (the SigV4 limit). A longer request fails with <see cref="StorageErrorCodes.ExpiryTooLong"/>.
    /// </summary>
    public TimeSpan MaxPresignExpiry { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the AWS account id (12 digits) that must own the bucket. Every request then carries it, and
    /// Amazon S3 refuses the request with 403 — <see cref="StorageErrorCodes.AccessDenied"/> — if the bucket
    /// belongs to another account, for example after it was deleted and re-created by someone else. Defaults to
    /// none (no check). Presigned requests do not carry it.
    /// </summary>
    public string? ExpectedBucketOwner { get; set; }

    /// <summary>
    /// Gets or sets the part size of multipart uploads, in bytes. Defaults to 16 MiB; 5 MiB to 5 GiB.
    /// </summary>
    /// <remarks>
    /// <see cref="IFileStorage.UploadAsync"/> sends content with a known <see cref="FileUploadOptions.ContentLength"/>
    /// up to this size in one request; larger content, and streams of unknown length, go in parts of this size with
    /// at most one part buffered. A multipart upload has at most 10,000 parts, so for a stream of unknown length
    /// the part size bounds the largest upload (16 MiB parts: about 156 GiB). Presigned multipart uploads choose
    /// their own part sizes.
    /// </remarks>
    public long MultipartPartSize { get; set; } = 16 * 1024 * 1024;
}

/// <summary>
/// The server-side encryption a store applies to the objects it writes (<see cref="S3StoreOptions.Encryption"/>).
/// </summary>
public enum S3Encryption
{
    /// <summary>
    /// Send no encryption header: the bucket's default encryption applies (SSE-S3 on AWS unless the bucket is
    /// configured otherwise).
    /// </summary>
    BucketDefault = 0,

    /// <summary>
    /// SSE-S3 (<c>x-amz-server-side-encryption: AES256</c>): AES-256 with keys managed by the provider.
    /// </summary>
    S3Managed = 1,

    /// <summary>
    /// SSE-KMS (<c>aws:kms</c>): encrypted under a KMS key (<see cref="S3StoreOptions.KmsKeyId"/>), whose key policy
    /// also controls who can read the objects; the caller needs <c>kms:GenerateDataKey</c> and <c>kms:Decrypt</c>.
    /// On an endpoint without <see cref="S3Compatibility.KmsEncryption"/>, every write of the store fails with
    /// <see cref="StorageErrorCodes.NotSupported"/>.
    /// </summary>
    Kms = 2,
}
