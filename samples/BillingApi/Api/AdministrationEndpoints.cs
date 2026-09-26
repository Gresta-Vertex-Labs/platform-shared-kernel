using BillingApi.Features.Reports;
using BillingApi.Features.Tenants;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Presentation.WebApi;

namespace BillingApi.Api;

/// <summary>
/// <c>/admin</c>, the back office. Everything here requires <c>billing.admin</c>, declared once on each query and command
/// with <c>[RequirePermission]</c> and enforced by the pipeline on every path the use case can take, so the endpoints do
/// not repeat it. An anonymous caller gets 401 and a caller without the permission 403, both as problems.
/// </summary>
public sealed class AdministrationEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin");

        admin.MapGet("/reports/revenue-by-tenant", (ISender sender, CancellationToken ct) =>
            sender.Send(new GetRevenueByTenant(), ct).ToOk());

        // GDPR/KVKK erasure of a whole tenant: destroys its data key, so every encrypted value becomes unreadable.
        admin.MapPost("/tenants/{tenantId:guid}/erase", (Guid tenantId, ISender sender, CancellationToken ct) =>
            sender.Send(new EraseTenant(new TenantId(tenantId)), ct).ToOk());
    }
}
