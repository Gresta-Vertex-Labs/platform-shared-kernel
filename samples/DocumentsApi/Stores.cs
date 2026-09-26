using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

namespace DocumentsApi;

/// <summary>The stores this service uses, and how a request picks one.</summary>
public static class Stores
{
    public const string Assets = "assets";
    public const string Documents = "documents";
    public const string Archive = "archive";

    /// <summary>
    /// The tenant of the request. A real service takes it from the authenticated principal
    /// (<c>IRequestContext.TenantId</c>); the sample reads a header so tests can play several tenants.
    /// </summary>
    public const string TenantHeader = "X-Tenant-Id";

    /// <summary>
    /// Returns the named store, or for a tenant store the view of the request's tenant. Never lets a caller reach a
    /// tenant store without a tenant.
    /// </summary>
    public static Result<IFileStorage> Resolve(IFileStorageFactory factory, StoreAddress store)
    {
        if (!factory.StoreNames.Contains(store.Name, StringComparer.OrdinalIgnoreCase))
        {
            return Error.NotFound("documents.unknown_store", $"Store '{store.Name}' does not exist.");
        }

        if (!factory.IsTenantScoped(store.Name))
        {
            return Result<IFileStorage>.Success(factory.GetStore(store.Name));
        }

        if (store.TenantId is not { } tenantId)
        {
            return Error.Validation("documents.tenant_required", $"Store '{store.Name}' needs a valid {TenantHeader} header.");
        }

        return Result<IFileStorage>.Success(factory.GetTenantStore(store.Name).ForTenant(tenantId));
    }
}

/// <summary>
/// The store a command or query addresses: its name from the route and, for a tenant store, the tenant from the
/// request's <see cref="Stores.TenantHeader"/> header. <see cref="Stores.Resolve"/> turns it into the store itself.
/// </summary>
/// <param name="Name">The store's name.</param>
/// <param name="TenantId">The request's tenant, or <see langword="null"/> when it sent none or an invalid one.</param>
public sealed record StoreAddress(string Name, TenantId? TenantId)
{
    /// <summary>The store <paramref name="name"/>, for the tenant <paramref name="request"/> names.</summary>
    /// <param name="name">The store's name.</param>
    /// <param name="request">The HTTP request.</param>
    /// <returns>The address.</returns>
    public static StoreAddress For(string name, HttpRequest request) =>
        new(name, SharedKernel.Execution.Tenancy.TenantId.TryParse(request.Headers[Stores.TenantHeader].ToString(), out var tenantId)
            ? tenantId
            : null);
}
