namespace SharedKernel.Storage.Abstractions.Models;

/// <summary>
/// Metadata describing a stored object, returned by <see cref="Abstractions.IFileStorage.GetMetadataAsync"/>
/// and streamed by <see cref="Abstractions.IFileStorage.ListAsync"/>. Never carries object bytes.
/// </summary>
public sealed record FileMetadata
{
    /// <summary>The bucket the object is stored in.</summary>
    public required string Bucket { get; init; }

    /// <summary>The object's path/name within <see cref="Bucket"/>.</summary>
    public required string Key { get; init; }

    /// <summary>The object's MIME type.</summary>
    public required string ContentType { get; init; }

    /// <summary>The object's size in bytes.</summary>
    public required long ContentLength { get; init; }

    /// <summary>The timestamp the object was last modified.</summary>
    public required DateTimeOffset LastModified { get; init; }

    /// <summary>The provider-assigned entity tag for the stored object, when available.</summary>
    public string? ETag { get; init; }
}
