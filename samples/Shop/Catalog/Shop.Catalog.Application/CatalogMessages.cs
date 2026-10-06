using SharedKernel.Localization;
using SharedKernel.Primitives.Errors;

namespace Shop.Catalog.Application;

/// <summary>The catalog's user-facing messages; translations live in the Api's <c>Localization/*.json</c>.</summary>
public static class CatalogMessages
{
    public static readonly LocalizedMessage<Guid> ProductNotFound = LocalizedMessage.Define<Guid>(
        "catalog.product.not_found",
        "Product {productId} was not found.",
        "productId"
    );

    public static readonly LocalizedMessage<string> SkuTaken = LocalizedMessage.Define<string>(
        "catalog.product.sku_taken",
        "A product with SKU {sku} already exists.",
        "sku"
    );

    public static readonly LocalizedMessage NoTenant = LocalizedMessage.Define(
        "catalog.tenant_required",
        "The catalog is only available to a caller with a tenant."
    );

    public static readonly LocalizedMessage NoImage = LocalizedMessage.Define(
        "catalog.product.no_image",
        "The product has no image."
    );

    /// <summary>The caller has no tenant: the catalog fails closed.</summary>
    public static Error TenantRequired() => NoTenant.ToError(ErrorType.Forbidden);
}
