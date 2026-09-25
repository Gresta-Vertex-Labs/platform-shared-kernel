using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Reporting.Abstractions.Models;

/// <summary>
/// Describes where <see cref="Exporters.IReportExporter{TRow}.ExportAsync"/> delivers its output: a key in
/// one of the service's named <c>SharedKernel.Storage</c> stores, resolved through <c>IFileStorageFactory</c>.
/// </summary>
public sealed record ReportDestination
{
    /// <summary>
    /// The name of the registered store to write to (e.g. <c>"reports"</c>). Required, non-empty. A store
    /// name that is not registered is a configuration error and throws.
    /// </summary>
    public required string Store { get; init; }

    /// <summary>
    /// The tenant the export belongs to, or <see langword="null"/> for a store shared by all tenants.
    /// Required exactly when <see cref="Store"/> is a tenant-scoped store: the export is then written through
    /// that tenant's view, under the tenant's own key prefix. Take it from the request's authenticated
    /// tenant, never from input the caller controls.
    /// </summary>
    public TenantId? TenantId { get; init; }

    /// <summary>The destination object's key, relative to the store (and tenant). Required, non-empty.</summary>
    public required string Key { get; init; }

    /// <summary>Optional user-supplied metadata to store alongside the exported object.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// The time-to-live of a presigned download URL to generate for the exported object once upload
    /// completes. <see langword="null"/> (the default) means no presigned URL is requested. It must not
    /// exceed the store's maximum presign expiry, or the export fails with <c>storage.expiry_too_long</c>
    /// after the object has been stored.
    /// </summary>
    public TimeSpan? PresignedDownloadUrlExpiry { get; init; }
}
