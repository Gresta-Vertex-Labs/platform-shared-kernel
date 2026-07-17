namespace SharedKernel.Storage.Abstractions.Models;

/// <summary>
/// The canonical, durable handle to a stored object, returned by
/// <see cref="Abstractions.IFileStorage.UploadAsync"/> and <see cref="Abstractions.IFileStorage.CopyAsync"/>.
/// </summary>
/// <remarks>
/// Persist this — not a presigned URL — as the long-lived pointer to a stored object. Presigned URLs
/// expire; a <see cref="FileReference"/> does not.
/// </remarks>
public sealed record FileReference
{
    /// <summary>The bucket the object is stored in.</summary>
    public required string Bucket { get; init; }

    /// <summary>The object's path/name within <see cref="Bucket"/>.</summary>
    public required string Key { get; init; }

    /// <summary>The provider-assigned entity tag for the stored object, when available.</summary>
    public string? ETag { get; init; }

    /// <summary>The provider-assigned version id, when bucket versioning is enabled; otherwise <see langword="null"/>.</summary>
    public string? VersionId { get; init; }
}
