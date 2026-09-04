using SharedKernel.MultiTenancy.Resolution;

namespace SharedKernel.MultiTenancy.Catalog;

/// <summary>
/// The first real default implementation of <see cref="ITenantStatusValidator"/> — backed by an
/// <see cref="ITenantCatalog"/> lookup.
/// </summary>
/// <remarks>
/// Not registered automatically by any DI extension method in this package — a consuming service
/// wires it explicitly, e.g. <c>services.AddScoped&lt;ITenantStatusValidator, CatalogTenantStatusValidator&gt;()</c>,
/// after registering an <see cref="ITenantCatalog"/> implementation.
/// </remarks>
public sealed class CatalogTenantStatusValidator(ITenantCatalog catalog) : ITenantStatusValidator
{
    /// <summary>
    /// Determines whether <paramref name="tenantId"/> is active, per <see cref="ITenantCatalog.GetByIdAsync"/>.
    /// </summary>
    /// <remarks>
    /// <b>Fail-closed:</b> a tenant absent from the catalog entirely is treated identically to
    /// <see cref="TenantStatus.Suspended"/>/<see cref="TenantStatus.Offboarded"/> — never treated
    /// as active. There is no "unknown, assume active" outcome.
    /// </remarks>
    /// <param name="tenantId">The tenant identifier to check.</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task<bool> IsActiveAsync(Guid tenantId, CancellationToken ct)
    {
        var descriptor = await catalog.GetByIdAsync(tenantId, ct).ConfigureAwait(false);
        return descriptor?.Status == TenantStatus.Active;
    }
}
