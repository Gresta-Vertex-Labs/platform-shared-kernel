namespace SharedKernel.Storage;

/// <summary>
/// Options for <see cref="IFileStorage.DownloadAsync"/>. The defaults read the current version whole.
/// </summary>
public sealed record FileDownloadOptions
{
    /// <summary>
    /// Gets the byte range to read, or <see langword="null"/> for the whole object. A range starting at or past the
    /// end of the object fails with <see cref="StorageErrorCodes.InvalidRange"/>.
    /// </summary>
    public ByteRange? Range { get; init; }

    /// <summary>
    /// Gets the ETag the object must have, with or without quotes; otherwise the download fails with
    /// <see cref="StorageErrorCodes.PreconditionFailed"/>. <see langword="null"/> reads whatever is current.
    /// </summary>
    /// <remarks>
    /// Pin the ETag of the first read across later range reads so they all read the same content, even if the
    /// object is replaced in between. Supported by every provider (it is a read condition).
    /// </remarks>
    public string? IfMatch { get; init; }

    /// <summary>
    /// Gets a specific version to read, when the bucket keeps versions (<see cref="FileReference.VersionId"/>);
    /// <see langword="null"/> reads the current version. A version that does not exist fails with
    /// <see cref="StorageErrorCodes.NotFound"/>; a malformed version id with
    /// <see cref="StorageErrorCodes.ProviderError"/>.
    /// </summary>
    public string? VersionId { get; init; }
}
