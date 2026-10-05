namespace SharedKernel.Storage;

/// <summary>Options for <see cref="IFileStorage.CreateDownloadUrlAsync"/>.</summary>
/// <example>
/// <code>
/// PresignedRequest link = (await store.CreateDownloadUrlAsync(key, new PresignedDownloadOptions
/// {
///     Expiry = TimeSpan.FromMinutes(5),
///     ContentDisposition = "attachment; filename=\"report.csv\"",
/// }, ct)).Value;
/// </code>
/// </example>
public sealed record PresignedDownloadOptions
{
    /// <summary>
    /// Gets how long the URL stays valid. Required; positive and at most the store's maximum presign expiry
    /// (1 hour unless configured, never more than 7 days), otherwise <see cref="StorageErrorCodes.ExpiryTooLong"/>.
    /// </summary>
    public required TimeSpan Expiry { get; init; }

    /// <summary>
    /// Gets the <c>Content-Disposition</c> the response should carry instead of the stored one, e.g.
    /// <c>attachment; filename="report.csv"</c>; 1 to 1024 printable ASCII characters. <see langword="null"/>
    /// keeps the stored header. Part of the signature.
    /// </summary>
    public string? ContentDisposition { get; init; }

    /// <summary>
    /// Gets the <c>Content-Type</c> the response should carry instead of the stored one; a MIME type as for
    /// <see cref="FileUploadOptions.ContentType"/>. <see langword="null"/> keeps the stored type. Part of the
    /// signature.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Gets a specific version to download, when the bucket keeps versions; <see langword="null"/> serves whatever
    /// is current when the URL is used.
    /// </summary>
    public string? VersionId { get; init; }
}
