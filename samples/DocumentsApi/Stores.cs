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
    public static Result<IFileStorage> Resolve(IFileStorageFactory factory, string storeName, HttpContext http)
    {
        if (!factory.StoreNames.Contains(storeName, StringComparer.OrdinalIgnoreCase))
        {
            return Error.NotFound("documents.unknown_store", $"Store '{storeName}' does not exist.");
        }

        if (!factory.IsTenantScoped(storeName))
        {
            return Result<IFileStorage>.Success(factory.GetStore(storeName));
        }

        if (!TenantId.TryParse(http.Request.Headers[TenantHeader].ToString(), out TenantId tenantId))
        {
            return Error.Validation("documents.tenant_required", $"Store '{storeName}' needs a valid {TenantHeader} header.");
        }

        return Result<IFileStorage>.Success(factory.GetTenantStore(storeName).ForTenant(tenantId));
    }
}
