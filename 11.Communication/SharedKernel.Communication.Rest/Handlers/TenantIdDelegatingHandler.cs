using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Communication.Rest.Handlers;

/// <summary>
/// Injects the <c>x-tenant-id</c> header into outgoing HTTP requests by resolving
/// <c>ITenantProvider</c> from the current request scope via <see cref="IHttpContextAccessor"/>.
/// Silent no-op when <see cref="IHttpContextAccessor.HttpContext"/> is null,
/// when <c>ITenantProvider</c> is not registered, or when <c>TenantId</c> is <see cref="Guid.Empty"/>.
/// Never throws. Registered as transient to avoid cross-request state capture.
/// </summary>
internal sealed class TenantIdDelegatingHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    // Sourced from 01.Core's WellKnownHeaders (P-259/P-260) — never an independently-declared literal.
    internal const string HeaderName = WellKnownHeaders.TenantId;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(HeaderName))
        {
            try
            {
                var httpContext = httpContextAccessor.HttpContext;
                if (httpContext is not null)
                {
                    var tenantProvider = httpContext.RequestServices.GetService<ITenantProvider>();
                    if (tenantProvider is not null && tenantProvider.TenantId != Guid.Empty)
                    {
                        request.Headers.TryAddWithoutValidation(
                            HeaderName,
                            tenantProvider.TenantId.ToString());
                    }
                }
            }
            catch
            {
                // Best-effort propagation — silently swallow all exceptions.
                // Never let a header-injection failure disrupt the outgoing request.
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
