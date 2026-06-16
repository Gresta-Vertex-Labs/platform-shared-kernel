using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Communication.Rest.Handlers;

/// <summary>
/// Injects the <c>x-tenant-id</c> header into outgoing HTTP requests by resolving
/// <c>IUserContext</c> from the current request scope via <see cref="IHttpContextAccessor"/>.
/// Silent no-op when <see cref="IHttpContextAccessor.HttpContext"/> is null,
/// when <c>IUserContext</c> is not registered, or when <c>IUserContext.TenantId</c> is null.
/// Never throws. Registered as transient to avoid cross-request state capture.
/// </summary>
internal sealed class TenantIdDelegatingHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    internal const string HeaderName = "x-tenant-id";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // TODO: implement
        throw new NotImplementedException();
    }
}
