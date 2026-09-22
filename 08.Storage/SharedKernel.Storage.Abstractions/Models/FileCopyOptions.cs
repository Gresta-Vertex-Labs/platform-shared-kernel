namespace SharedKernel.Storage;

/// <summary>
/// Options for <see cref="IFileStorage.CopyAsync"/> and <see cref="IFileStorage.CopyToAsync"/>. Every member is
/// optional; the defaults copy content, stored headers and metadata unchanged, unconditionally.
/// </summary>
/// <remarks>
/// Setting <see cref="Condition"/> makes the copy stream through the process (a conditional upload) instead of
/// running server-side, because several S3-compatible services ignore conditions on a server-side copy.
/// </remarks>
public sealed record FileCopyOptions
{
    /// <summary>
    /// Gets the ETag the source must have, with or without quotes; otherwise the copy fails with
    /// <see cref="StorageErrorCodes.PreconditionFailed"/>. <see langword="null"/> copies whatever is current.
    /// </summary>
    public string? SourceIfMatch { get; init; }

    /// <summary>
    /// Gets a condition on the destination key, such as <see cref="WriteCondition.IfNotExists"/>;
    /// <see langword="null"/> overwrites. Needs conditional-write support on the destination's provider, otherwise
    /// the copy fails with <see cref="StorageErrorCodes.NotSupported"/>.
    /// </summary>
    public WriteCondition? Condition { get; init; }

    /// <summary>
    /// Gets a replacement MIME type, e.g. <c>application/pdf</c>; <see langword="null"/> keeps the source's.
    /// Same rules as <see cref="FileUploadOptions.ContentType"/>.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Gets replacement user metadata, which replaces the source's entries as a whole; <see langword="null"/> keeps
    /// the source's. Same rules as <see cref="FileUploadOptions.Metadata"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Gets the tier of the copy. Defaults to <see cref="StorageTier.Default"/>, which uses the destination store's
    /// default tier (not the source object's tier).
    /// </summary>
    public StorageTier Tier { get; init; }
}
