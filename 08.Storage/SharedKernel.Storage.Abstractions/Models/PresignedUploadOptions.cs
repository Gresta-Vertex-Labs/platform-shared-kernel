namespace SharedKernel.Storage;

/// <summary>
/// Options for <see cref="IFileStorage.CreateUploadUrlAsync"/>. Every value set here becomes a header in
/// <see cref="PresignedRequest.Headers"/> that the client must send unchanged.
/// </summary>
/// <remarks>
/// The object is written with the store's default tier and encryption. Tags cannot be set through a presigned
/// URL. A presigned <c>PUT</c> cannot limit the upload size; use <see cref="PresignedPostOptions"/> for that.
/// </remarks>
public sealed record PresignedUploadOptions
{
    /// <summary>
    /// Gets how long the URL stays valid. Required; positive and at most the store's maximum presign expiry
    /// (1 hour unless configured), otherwise <see cref="StorageErrorCodes.ExpiryTooLong"/>.
    /// </summary>
    public required TimeSpan Expiry { get; init; }

    /// <summary>
    /// Gets the MIME type the client must send as <c>Content-Type</c>; it is stored with the object and is part
    /// of the signature. Required; same rules as <see cref="FileUploadOptions.ContentType"/>.
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// Gets a value indicating whether the upload may only create the object, never overwrite it (the client sends
    /// <c>If-None-Match: *</c>, and the provider answers 412 when the key exists). Defaults to
    /// <see langword="false"/>. A provider without conditional writes (Huawei Cloud OBS) fails with
    /// <see cref="StorageErrorCodes.NotSupported"/> when this is set.
    /// </summary>
    public bool CreateOnly { get; init; }

    /// <summary>
    /// Gets the base64 SHA-256 the uploaded bytes must have, pinning the URL to one exact file (the client sends
    /// <c>x-amz-checksum-sha256</c>). A provider without SHA-256 support (Huawei Cloud OBS) fails with
    /// <see cref="StorageErrorCodes.NotSupported"/> when this is set.
    /// </summary>
    public string? ChecksumSha256 { get; init; }

    /// <summary>
    /// Gets user metadata the client must send (as <c>x-amz-meta-*</c> headers); it is stored with the object and
    /// is part of the signature. Same rules as <see cref="FileUploadOptions.Metadata"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
