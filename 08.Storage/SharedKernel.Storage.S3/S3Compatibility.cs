namespace SharedKernel.Storage.S3;

/// <summary>
/// The S3 features an endpoint supports (<see cref="S3StorageOptions.Compatibility"/>). A request needing a
/// missing feature fails with <see cref="StorageErrorCodes.NotSupported"/> before it is sent, rather than being
/// silently degraded. Every flag defaults to <see langword="true"/>.
/// </summary>
/// <remarks>
/// Amazon S3 and current MinIO releases support everything. Switch off only what the service verifiably lacks or
/// ignores — a service that accepts a header and ignores it (Huawei Cloud OBS with <c>If-None-Match</c>) is worse
/// than one that rejects it. Provider packages such as <c>SharedKernel.Storage.Obs</c> set their profile
/// themselves. The flags are read once, when the connection's client is created.
/// </remarks>
/// <example>
/// <code>
/// "S3": { "ServiceUrl": "https://s3.example.com",
///         "Compatibility": { "ConditionalWrites": false, "ObjectTags": false } }
/// </code>
/// </example>
public sealed class S3Compatibility
{
    /// <summary>
    /// Gets or sets a value indicating whether <c>If-None-Match: *</c> and <c>If-Match</c> writes are supported.
    /// When <see langword="false"/>, these fail with <see cref="StorageErrorCodes.NotSupported"/>:
    /// <see cref="FileUploadOptions.Condition"/>, <see cref="FileCopyOptions.Condition"/>, a condition on
    /// <see cref="IFileStorage.CompleteMultipartUploadAsync"/>, <see cref="FileDeleteOptions.IfMatch"/> and
    /// <see cref="PresignedUploadOptions.CreateOnly"/>. Read conditions (<see cref="FileDownloadOptions.IfMatch"/>,
    /// <see cref="FileCopyOptions.SourceIfMatch"/>) are unaffected.
    /// </summary>
    public bool ConditionalWrites { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether <c>x-amz-checksum-sha256</c> checksums are supported. When
    /// <see langword="false"/>, <see cref="FileUploadOptions.ChecksumSha256"/> and
    /// <see cref="PresignedUploadOptions.ChecksumSha256"/> fail with <see cref="StorageErrorCodes.NotSupported"/>,
    /// no SHA-256 is stored for other uploads, and <see cref="FileProperties.ChecksumSha256"/> is always
    /// <see langword="null"/>.
    /// </summary>
    public bool Sha256Checksums { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether object tags are supported. When <see langword="false"/>,
    /// <see cref="FileUploadOptions.Tags"/> and <see cref="MultipartUploadOptions.Tags"/> fail with
    /// <see cref="StorageErrorCodes.NotSupported"/>.
    /// </summary>
    public bool ObjectTags { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether SSE-KMS encryption is supported. When <see langword="false"/>, every
    /// write of a store with <see cref="S3Encryption.Kms"/> fails with <see cref="StorageErrorCodes.NotSupported"/>;
    /// reads still work.
    /// </summary>
    public bool KmsEncryption { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether presigned <c>POST</c> form uploads are supported. When
    /// <see langword="false"/>, <see cref="IFileStorage.CreateUploadFormAsync"/> fails with
    /// <see cref="StorageErrorCodes.NotSupported"/>.
    /// </summary>
    public bool PresignedPost { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the ETag of a single-part object is always the MD5 of its content,
    /// as on Amazon S3. The AWS SDK checks full downloads against it. Set this to <see langword="false"/> for a
    /// service whose ETags differ (OBS with server-side encryption): full downloads are then requested as
    /// <c>bytes=0-</c>, which the SDK does not check that way, and <see cref="FileDownload.Range"/> stays
    /// <see langword="null"/> for them.
    /// </summary>
    public bool ETagIsContentMd5 { get; set; } = true;
}
