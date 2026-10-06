using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;

namespace Shop.Catalog.Application;

/// <summary>Reads the caller's tenant; a caller without one is refused.</summary>
public static class CallerTenant
{
    /// <summary>The caller's tenant, or a forbidden failure.</summary>
    public static Result<TenantId> Of(IRequestContext context) =>
        context.TenantId is { } tenant
            ? Result<TenantId>.Success(tenant)
            : Result<TenantId>.Failure(CatalogMessages.TenantRequired());
}
