namespace SharedKernel.Storage;

/// <summary>
/// The properties and user metadata of a stored object, read without its content by
/// <see cref="IFileStorage.GetPropertiesAsync"/> and returned with every <see cref="FileDownload"/>.
/// </summary>
public sealed record FileProperties
{
    /// <summary>Gets the object key, relative to the store (and tenant).</summary>
    public required string Key { get; init; }

    /// <summary>Gets the size of the whole object in bytes, also for a range download.</summary>
    public required long ContentLength { get; init; }

    /// <summary>Gets the MIME type stored with the object, or <see langword="null"/> when none was stored.</summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Gets when the object was last written, in UTC, or <see langword="null"/> when the provider did not say.
    /// </summary>
    public DateTimeOffset? LastModified { get; init; }

    /// <summary>
    /// Gets the entity tag of the current content as the provider returns it (quoted on S3). Pass it to
    /// <see cref="WriteCondition.IfMatch(string)"/> or <see cref="FileDownloadOptions.IfMatch"/>. It identifies a
    /// version of the content; it is not necessarily an MD5 hash.
    /// </summary>
    public string? ETag { get; init; }

    /// <summary>Gets the version id when the bucket keeps versions; otherwise <see langword="null"/>.</summary>
    public string? VersionId { get; init; }

    /// <summary>Gets the stored <c>Cache-Control</c> header, or <see langword="null"/>.</summary>
    public string? CacheControl { get; init; }

    /// <summary>Gets the stored <c>Content-Disposition</c> header, or <see langword="null"/>.</summary>
    public string? ContentDisposition { get; init; }

    /// <summary>Gets the stored <c>Content-Encoding</c> header, or <see langword="null"/>.</summary>
    public string? ContentEncoding { get; init; }

    /// <summary>
    /// Gets the base64 SHA-256 checksum of the whole object when the provider stored one and returned it;
    /// otherwise <see langword="null"/>. One is stored only for uploads from a seekable stream or with
    /// <see cref="FileUploadOptions.ChecksumSha256"/>, and never on providers without SHA-256 support (Huawei Cloud
    /// OBS). MinIO does not return it, and range downloads do not carry it.
    /// </summary>
    public string? ChecksumSha256 { get; init; }

    /// <summary>
    /// Gets the storage tier the object is kept in: <see cref="StorageTier.InfrequentAccess"/> for S3
    /// <c>STANDARD_IA</c>, <see cref="StorageTier.Default"/> for every other storage class.
    /// </summary>
    public StorageTier Tier { get; init; }

    /// <summary>
    /// Gets the user metadata stored with the object, keyed without the <c>x-amz-meta-</c> prefix. Keys are
    /// lower-case and looked up ignoring case. Defaults to an empty dictionary, never <see langword="null"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = EmptyMetadata;

    internal static IReadOnlyDictionary<string, string> EmptyMetadata { get; } =
        new Dictionary<string, string>(0, StringComparer.OrdinalIgnoreCase);
}
