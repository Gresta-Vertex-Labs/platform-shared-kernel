using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.MultiTenancy.Catalog;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.ServiceDefaults.Localization;

/// <summary>
/// The <see cref="LocalizationResolutionStrategy.TenantDefault"/> step: resolves a culture from
/// the current tenant's <see cref="TenantDescriptor.DefaultCulture"/>.
/// </summary>
/// <remarks>
/// Resolves <see cref="ITenantCatalog"/> <b>optionally</b> — via
/// <see cref="ServiceProviderServiceExtensions.GetService{T}"/>, never
/// <c>GetRequiredService</c> — and skips cleanly (never throws) when no
/// <see cref="ITenantCatalog"/> is registered in DI at all. This is the phase's own explicit
/// acceptance criterion: a host that adopts <see cref="LocalizationExtensions.AddSharedKernelLocalization"/>
/// without ever registering an <see cref="ITenantCatalog"/> falls through to the next configured
/// strategy exactly as if this step were absent.
/// </remarks>
internal sealed class TenantDefaultRequestCultureProvider : IRequestCultureProvider
{
    /// <inheritdoc/>
    public async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var catalog = httpContext.RequestServices.GetService<ITenantCatalog>();
        if (catalog is null)
        {
            return null;
        }

        var tenantId = httpContext.RequestServices.GetService<ITenantProvider>()?.TenantId ?? Guid.Empty;
        if (tenantId == Guid.Empty)
        {
            return null;
        }

        var descriptor = await catalog.GetByIdAsync(tenantId, httpContext.RequestAborted).ConfigureAwait(false);

        return descriptor?.DefaultCulture is { } culture
            ? new ProviderCultureResult(culture)
            : null;
    }
}
