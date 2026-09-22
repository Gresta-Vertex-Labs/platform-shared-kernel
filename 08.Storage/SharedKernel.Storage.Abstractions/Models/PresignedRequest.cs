namespace SharedKernel.Storage;

/// <summary>
/// A presigned HTTP request a client sends directly to the provider, returned by
/// <see cref="IFileStorage.CreateDownloadUrlAsync"/>, <see cref="IFileStorage.CreateUploadUrlAsync"/> and
/// <see cref="IFileStorage.CreateUploadPartUrlAsync"/>.
/// </summary>
/// <remarks>
/// Hand <see cref="Url"/>, <see cref="Method"/> and <see cref="Headers"/> to the client as they are. Anyone who
/// holds the URL can use it until it expires; never log or persist it.
/// </remarks>
public sealed record PresignedRequest
{
    /// <summary>Gets the URL, carrying the signature in its query string.</summary>
    public required Uri Url { get; init; }

    /// <summary>Gets the HTTP method: <c>GET</c> for downloads, <c>PUT</c> for uploads and parts.</summary>
    public required string Method { get; init; }

    /// <summary>
    /// Gets the headers the client must send with exactly these values; the signature covers them, so a missing
    /// or changed header makes the provider reject the request. Empty for downloads and part uploads.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>
    /// Gets when the URL stops working. A URL signed with temporary credentials (an IAM role) stops working
    /// earlier if those credentials expire first.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
