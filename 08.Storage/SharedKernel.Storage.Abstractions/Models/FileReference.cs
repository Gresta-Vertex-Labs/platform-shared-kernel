using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Storage;

/// <summary>
/// The durable handle to a stored object: store name, tenant and key, returned by every write. Persist this —
/// never a presigned URL, which expires, and never a bucket name, which is configuration.
/// </summary>
/// <remarks>
/// Open the object again with <see cref="IFileStorageFactory.Open(FileReference)"/>, which picks the store and
/// the tenant view. A reference into a tenant-scoped store carries its <see cref="TenantId"/>: store it with the
/// row that owns it and load it through the same tenant filter, so one tenant's reference cannot be replayed by
/// another. A plain record: it serializes as JSON as it is.
/// </remarks>
public sealed record FileReference
{
    /// <summary>
    /// Gets the name of the store holding the object, as registered (<see cref="IFileStorage.StoreName"/>).
    /// </summary>
    public required string Store { get; init; }

    /// <summary>Gets the tenant of a tenant-scoped store, or <see langword="null"/> for a shared store.</summary>
    public TenantId? TenantId { get; init; }

    /// <summary>Gets the object key, relative to the store and tenant.</summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the entity tag the provider assigned to this content (quoted on S3), when known. Pass it to
    /// <see cref="WriteCondition.IfMatch(string)"/> to update only this version. Treat it as opaque: for multipart
    /// uploads and encrypted objects it is not an MD5 of the content.
    /// </summary>
    public string? ETag { get; init; }

    /// <summary>
    /// Gets the provider's version id when the bucket keeps versions; otherwise <see langword="null"/>.
    /// </summary>
    public string? VersionId { get; init; }
}
