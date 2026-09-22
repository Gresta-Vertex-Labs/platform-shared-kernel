namespace SharedKernel.Storage;

/// <summary>
/// Options for <see cref="IFileStorage.DeleteAsync"/>. The defaults delete the current object unconditionally.
/// </summary>
public sealed record FileDeleteOptions
{
    /// <summary>
    /// Gets the ETag the object must have to be deleted, with or without quotes. A different ETag, or a missing
    /// object, fails with <see cref="StorageErrorCodes.PreconditionFailed"/>; a provider without conditional writes
    /// (Huawei Cloud OBS) fails with <see cref="StorageErrorCodes.NotSupported"/>. <see langword="null"/> deletes
    /// unconditionally.
    /// </summary>
    public string? IfMatch { get; init; }

    /// <summary>
    /// Gets a specific version to delete permanently, when the bucket keeps versions
    /// (<see cref="FileReference.VersionId"/>). <see langword="null"/> deletes the current object, which in a versioned
    /// bucket leaves a delete marker.
    /// </summary>
    public string? VersionId { get; init; }
}
