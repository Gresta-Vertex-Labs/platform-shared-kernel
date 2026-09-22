namespace SharedKernel.Storage;

/// <summary>
/// An object returned by <see cref="IFileStorage.ListAsync"/> or in <see cref="FileListPage.Items"/>. Listings
/// carry no content type, headers or metadata; read <see cref="IFileStorage.GetPropertiesAsync"/> for those.
/// </summary>
public sealed record FileListItem
{
    /// <summary>Gets the object key, relative to the store (and tenant): pass it straight to other members.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the object size in bytes.</summary>
    public required long ContentLength { get; init; }

    /// <summary>
    /// Gets when the object was last written, in UTC, or <see langword="null"/> when the provider did not say.
    /// </summary>
    public DateTimeOffset? LastModified { get; init; }

    /// <summary>
    /// Gets the entity tag of the object as the provider returns it (quoted on S3), or <see langword="null"/>.
    /// Usable in an <c>If-Match</c> condition.
    /// </summary>
    public string? ETag { get; init; }
}
