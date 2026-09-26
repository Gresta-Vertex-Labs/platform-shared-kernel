using SharedKernel.Execution.Tenancy;
using SharedKernel.Storage;

namespace SharedKernel.Reporting;

/// <summary>
/// Where an export is stored: a key in one of the service's named <c>SharedKernel.Storage</c> stores.
/// </summary>
/// <example>
/// <code>
/// var destination = new ReportDestination
/// {
///     Store = "reports",
///     TenantId = requestContext.TenantId,              // for a tenant store
///     Key = ReportFormat.Xlsx.WithExtension($"orders/{DateTime.UtcNow:yyyyMMdd-HHmmss}"),
///     DownloadFileName = "Orders.xlsx",
///     PresignedDownloadUrlExpiry = TimeSpan.FromMinutes(15),
/// };
/// </code>
/// </example>
public sealed record ReportDestination
{
    /// <summary>
    /// Gets the name of the registered store to write to, e.g. <c>"reports"</c>. A store that is not registered is a
    /// configuration error and throws.
    /// </summary>
    public required string Store { get; init; }

    /// <summary>
    /// Gets the tenant the export belongs to, or <see langword="null"/> for a store shared by all tenants. Required
    /// exactly when <see cref="Store"/> is a tenant store; the export is then written under the tenant's prefix. Take it
    /// from <c>IRequestContext.TenantId</c>, never from input the caller controls.
    /// </summary>
    public TenantId? TenantId { get; init; }

    /// <summary>Gets the object's key, relative to the store (and tenant).</summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the file name a browser saves the download as, e.g. <c>"Orders September.xlsx"</c>. Stored as the
    /// object's <c>Content-Disposition: attachment</c>, so presigned download links use it; any Unicode is allowed.
    /// <see langword="null"/> (the default) sets no disposition.
    /// </summary>
    public string? DownloadFileName { get; init; }

    /// <summary>
    /// Gets an optional write condition, e.g. <see cref="WriteCondition.IfNotExists"/> so an issued statement is never
    /// overwritten. A failed condition returns the store's <c>storage.already_exists</c> or
    /// <c>storage.precondition_failed</c>.
    /// </summary>
    public WriteCondition? Condition { get; init; }

    /// <summary>Gets optional user metadata stored with the object.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Gets the lifetime of a presigned download link to create once the export is stored, or <see langword="null"/>
    /// (the default) for none. It must not exceed the store's maximum presign expiry; if it does, the export is stored
    /// and the call fails with <c>storage.expiry_too_long</c>.
    /// </summary>
    public TimeSpan? PresignedDownloadUrlExpiry { get; init; }
}
