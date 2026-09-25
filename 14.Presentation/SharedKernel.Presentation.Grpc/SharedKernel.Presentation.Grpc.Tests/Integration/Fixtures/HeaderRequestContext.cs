using Microsoft.AspNetCore.Http;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;

/// <summary>
/// Test-only <see cref="IRequestContext"/> whose <see cref="TenantId"/> is read directly from the
/// current call's <see cref="WellKnownHeaders.TenantId"/> metadata/header value.
/// </summary>
/// <remarks>
/// Simulates a realistic composition: <c>GrpcTenantContextInterceptor</c> itself only ever reads
/// <see cref="IRequestContext"/> — it never reads gRPC metadata directly. In production the tenant is
/// typically resolved from a validated JWT tenant claim. This fixture instead reads the raw
/// <c>x-tenant-id</c> header (gRPC metadata is transported as HTTP/2 headers, so
/// <see cref="HttpRequest.Headers"/> sees the exact value
/// <c>SharedKernel.Communication.Grpc.Interceptors.TenantIdInterceptor</c> wrote) — letting T-71's
/// round-trip test prove end-to-end metadata-key agreement across both packages.
/// </remarks>
internal sealed class HeaderRequestContext(IHttpContextAccessor httpContextAccessor) : IRequestContext
{
    public bool IsAuthenticated => true;

    public string? UserId => "header-caller";

    public TenantId? TenantId =>
        Execution.Tenancy.TenantId.TryParse(
            httpContextAccessor.HttpContext?.Request.Headers[WellKnownHeaders.TenantId].ToString(),
            out var tenantId)
            ? tenantId
            : null;

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
}
