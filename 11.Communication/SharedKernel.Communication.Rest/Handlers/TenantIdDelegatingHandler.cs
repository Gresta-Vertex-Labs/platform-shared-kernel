using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Execution.Context;

namespace SharedKernel.Communication.Rest.Handlers;

/// <summary>
/// Injects the <c>x-tenant-id</c> header into outgoing HTTP requests by resolving
/// the tenant of the ambient <see cref="RequestContextScope.Current"/>, or else of the <see cref="IRequestContext"/>
/// registered in the current request scope (via <see cref="IHttpContextAccessor"/>).
/// Silent no-op when neither is available or the context has no tenant.
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
                var tenantId = RequestContextScope.Current?.TenantId
                    ?? httpContextAccessor.HttpContext?.RequestServices.GetService<IRequestContext>()?.TenantId;
                if (tenantId is { } tenant)
                {
                    request.Headers.TryAddWithoutValidation(HeaderName, tenant.ToString());
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
