using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Storage;

/// <summary>
/// A store whose objects belong to tenants. Every operation goes through a view bound to one tenant, whose
/// keys live under that tenant's own prefix, so one tenant can never read, list, overwrite, copy or delete
/// another tenant's objects.
/// </summary>
/// <remarks>
/// <para>
/// Register with a provider's <c>AddTenantStore(name)</c> and resolve as
/// <c>[FromKeyedServices("documents")] ITenantFileStorage</c>, unkeyed when it is the only tenant store, or with
/// <see cref="IFileStorageFactory.GetTenantStore(string)"/>. A tenant store is never resolvable as a plain
/// <see cref="IFileStorage"/>, so it cannot be used without choosing a tenant. Instances are singletons and safe
/// for concurrent use.
/// </para>
/// <para>
/// Take the tenant from the request's authenticated identity, never from input the caller controls: the view
/// isolates tenants from each other, not a caller from a tenant id it chose.
/// </para>
/// <para>
/// Keys are stored as <c>{store key prefix}tenants/{tenantId}/{key}</c>. The view returns keys, listings,
/// references and error messages without that prefix. The tenant is written as <see cref="TenantId.ToString()"/>
/// (a lowercase GUID), so two different tenants always map to different prefixes. Deleting a tenant's
/// data is <see cref="IFileStorage.ListAsync"/> and <see cref="IFileStorage.DeleteManyAsync"/> on its view.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class ContractFiles(
///     [FromKeyedServices("documents")] ITenantFileStorage documents,
///     IRequestContext request)
/// {
///     public Task&lt;Result&lt;FileDownload&gt;&gt; OpenAsync(string key, CancellationToken ct) =>
///         documents.ForTenant(request.TenantId!.Value).DownloadAsync(key, cancellationToken: ct);
/// }
/// </code>
/// </example>
public interface ITenantFileStorage
{
    /// <summary>Gets the name this store was registered with, in its registered spelling.</summary>
    string StoreName { get; }

    /// <summary>Returns the view of this store bound to <paramref name="tenantId"/>.</summary>
    /// <param name="tenantId">The tenant. Must not be <see langword="default"/>.</param>
    /// <returns>
    /// An <see cref="IFileStorage"/> whose keys are relative to the tenant and whose
    /// <see cref="IFileStorage.TenantId"/> is <paramref name="tenantId"/>. Creating a view is cheap and does no
    /// I/O; the view can be kept or created per call.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="tenantId"/> is <see langword="default"/>.
    /// </exception>
    IFileStorage ForTenant(TenantId tenantId);
}
