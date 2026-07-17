namespace SharedKernel.Storage.Abstractions.Models;

/// <summary>
/// Describes an object to upload via <see cref="Abstractions.IFileStorage.UploadAsync"/>.
/// </summary>
/// <remarks>
/// <see cref="Content"/> is caller-owned — <c>UploadAsync</c> never disposes it, and reads from the
/// stream's current position rather than rewinding it.
/// </remarks>
public sealed record FileUploadRequest
{
    /// <summary>The bucket the object is uploaded to. Required, non-empty.</summary>
    public required string Bucket { get; init; }

    /// <summary>The object's path/name within <see cref="Bucket"/>. Required, non-empty.</summary>
    public required string Key { get; init; }

    /// <summary>
    /// The object payload, read from its current position. Caller-owned — never disposed by
    /// <see cref="Abstractions.IFileStorage.UploadAsync"/>.
    /// </summary>
    public required Stream Content { get; init; }

    /// <summary>The MIME type of the payload (e.g. <c>"application/pdf"</c>). Required.</summary>
    public required string ContentType { get; init; }

    /// <summary>Optional user-supplied metadata to store alongside the object. <see langword="null"/> means none.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
