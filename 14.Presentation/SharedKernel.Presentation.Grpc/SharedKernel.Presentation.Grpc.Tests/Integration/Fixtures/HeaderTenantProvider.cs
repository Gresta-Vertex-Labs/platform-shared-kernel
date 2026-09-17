using Microsoft.AspNetCore.Http;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;

/// <summary>
/// Test-only <see cref="ITenantProvider"/> that resolves <see cref="TenantId"/> directly from the
/// current call's <see cref="WellKnownHeaders.TenantId"/> metadata/header value.
/// </summary>
/// <remarks>
/// Simulates a realistic composition: <c>GrpcTenantContextInterceptor</c> itself only ever reads
/// <see cref="ITenantProvider"/> (D-72) — it never reads gRPC metadata directly. In production,
/// <see cref="ITenantProvider"/> is typically populated from a validated JWT tenant claim. This
/// fixture instead reads the raw <c>x-tenant-id</c> header (gRPC metadata is transported as HTTP/2
/// headers, so <see cref="HttpRequest.Headers"/> sees the exact value
/// <c>SharedKernel.Communication.Grpc.Interceptors.TenantIdInterceptor</c> wrote) — letting T-71's
/// round-trip test prove end-to-end metadata-key agreement across both packages without this
/// package's own contract needing to know about raw metadata.
/// </remarks>
internal sealed class HeaderTenantProvider(IHttpContextAccessor httpContextAccessor) : ITenantProvider
{
    public Guid TenantId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.Request.Headers[WellKnownHeaders.TenantId].ToString();
            return Guid.TryParse(value, out var tenantId) ? tenantId : Guid.Empty;
        }
    }
}
