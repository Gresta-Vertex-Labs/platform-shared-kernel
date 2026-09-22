namespace SharedKernel.Storage;

/// <summary>
/// Options for <see cref="IFileStorage.StartMultipartUploadAsync"/>: the properties of the object being created.
/// Every member is optional; invalid values fail with <see cref="StorageErrorCodes.InvalidRequest"/> before any I/O.
/// </summary>
public sealed record MultipartUploadOptions
{
    /// <summary>
    /// Gets the MIME type to store; same rules as <see cref="FileUploadOptions.ContentType"/>. Defaults to
    /// <c>application/octet-stream</c>.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Gets the <c>Content-Disposition</c> header to store; same rules as
    /// <see cref="FileUploadOptions.ContentDisposition"/>.
    /// </summary>
    public string? ContentDisposition { get; init; }

    /// <summary>
    /// Gets the <c>Cache-Control</c> header to store; same rules as <see cref="FileUploadOptions.CacheControl"/>.
    /// </summary>
    public string? CacheControl { get; init; }

    /// <summary>Gets user metadata; same rules as <see cref="FileUploadOptions.Metadata"/>.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Gets object tags; same rules as <see cref="FileUploadOptions.Tags"/>. A provider without tag support fails
    /// with <see cref="StorageErrorCodes.NotSupported"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Tags { get; init; }

    /// <summary>
    /// Gets the storage tier. Defaults to <see cref="StorageTier.Default"/>, which uses the store's configured default
    /// tier.
    /// </summary>
    public StorageTier Tier { get; init; }
}
