namespace SharedKernel.Storage;

/// <summary>
/// Options for <see cref="IFileStorage.UploadAsync"/>. Every member is optional. All values are validated before
/// any I/O; an invalid one fails the upload with <see cref="StorageErrorCodes.InvalidRequest"/>.
/// </summary>
/// <example>
/// <code>
/// // Create-only, with an integrity check the provider enforces.
/// await store.UploadAsync(key, file, new FileUploadOptions
/// {
///     ContentType = "application/pdf",
///     ContentDisposition = "attachment; filename=\"invoice.pdf\"",
///     Metadata = new Dictionary&lt;string, string&gt; { ["invoice-id"] = invoiceId },
///     Condition = WriteCondition.IfNotExists,
///     ChecksumSha256 = sha256Base64,
/// }, ct);
///
/// // A request body cannot report its length: pass it, so small bodies go in one request and checksums work.
/// await store.UploadAsync(key, Request.Body, new FileUploadOptions
/// {
///     ContentType = Request.ContentType,
///     ContentLength = Request.ContentLength,
/// }, ct);
/// </code>
/// </example>
public sealed record FileUploadOptions
{
    /// <summary>
    /// Gets the MIME type to store, e.g. <c>application/pdf</c>: <c>type/subtype</c>, printable ASCII, at most 1024
    /// characters. Defaults to <c>application/octet-stream</c>. Browsers trust it when the object is served, so
    /// never pass a client-supplied type unchecked.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Gets the <c>Content-Disposition</c> header to store, e.g. <c>attachment; filename="invoice.pdf"</c>; 1 to
    /// 1024 printable ASCII characters (encode non-ASCII file names as <c>filename*=UTF-8''...</c>).
    /// </summary>
    public string? ContentDisposition { get; init; }

    /// <summary>
    /// Gets the <c>Cache-Control</c> header to store, e.g. <c>max-age=3600</c>; 1 to 1024 printable ASCII characters.
    /// </summary>
    public string? CacheControl { get; init; }

    /// <summary>
    /// Gets the <c>Content-Encoding</c> header to store, e.g. <c>gzip</c> for content you compressed yourself; 1 to
    /// 1024 printable ASCII characters. The store does not compress.
    /// </summary>
    public string? ContentEncoding { get; init; }

    /// <summary>
    /// Gets user metadata to store with the object. Keys are letters, digits, <c>-</c> and <c>_</c>, unique ignoring
    /// case, and are stored lower-case; values are printable ASCII; keys and values together are at most
    /// <see cref="StorageValidation.MaxMetadataBytes"/> bytes. Read back in <see cref="FileProperties.Metadata"/>.
    /// Metadata is visible to anyone who can read the object's properties; never put secrets in it.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Gets object tags, usable in lifecycle and access policies: at most <see cref="StorageValidation.MaxTags"/>,
    /// keys 1 to 128 and values 0 to 256 characters, without control characters. A provider without tag support
    /// fails the upload with <see cref="StorageErrorCodes.NotSupported"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Tags { get; init; }

    /// <summary>
    /// Gets the storage tier. Defaults to <see cref="StorageTier.Default"/>, which uses the store's configured default
    /// tier.
    /// </summary>
    public StorageTier Tier { get; init; }

    /// <summary>
    /// Gets a condition the write must meet, checked atomically by the provider:
    /// <see cref="WriteCondition.IfNotExists"/> fails with <see cref="StorageErrorCodes.AlreadyExists"/>,
    /// <see cref="WriteCondition.IfMatch(string)"/> with <see cref="StorageErrorCodes.PreconditionFailed"/>.
    /// <see langword="null"/> (the default) overwrites. A provider without conditional writes (Huawei Cloud OBS) fails
    /// the upload with <see cref="StorageErrorCodes.NotSupported"/>.
    /// </summary>
    public WriteCondition? Condition { get; init; }

    /// <summary>
    /// Gets the exact number of bytes the content stream will yield, for a stream that cannot report it — for
    /// example an ASP.NET Core request body (pass <c>Request.ContentLength</c>). Not negative. Required with
    /// <see cref="ChecksumSha256"/> on a non-seekable stream (otherwise
    /// <see cref="StorageErrorCodes.InvalidRequest"/>); otherwise it lets content up to the store's multipart part size
    /// go in one request instead of a multipart upload. <see langword="null"/> (the default) takes the length from a
    /// seekable stream.
    /// </summary>
    public long? ContentLength { get; init; }

    /// <summary>
    /// Gets the expected SHA-256 of the content, base64-encoded (44 characters). The provider verifies the received
    /// bytes against it and rejects the upload with <see cref="StorageErrorCodes.ChecksumMismatch"/> on a
    /// difference, so corrupted bytes are never stored. Forces a single request, so the content can be at most
    /// 5 GiB on S3. A provider without SHA-256 support (Huawei Cloud OBS) fails the upload with
    /// <see cref="StorageErrorCodes.NotSupported"/>.
    /// </summary>
    public string? ChecksumSha256 { get; init; }
}
