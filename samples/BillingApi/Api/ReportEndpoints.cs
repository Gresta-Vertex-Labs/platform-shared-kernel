using BillingApi.Features.Audit;
using BillingApi.Features.Reports;
using MediatR;
using SharedKernel.Presentation.WebApi;

namespace BillingApi.Api;

/// <summary>The caller's own revenue report and the audit trail.</summary>
public sealed class ReportEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/reports/revenue", (ISender sender, CancellationToken ct) =>
            sender.Send(new GetRevenue(), ct).ToOk());

        app.MapGet("/audit/{resourceType}/{resourceId}", (string resourceType, string resourceId, ISender sender, CancellationToken ct) =>
            sender.Send(new GetAuditHistory(resourceType, resourceId), ct).ToOk());

        app.MapGet("/audit/{resourceType}", (string resourceType, ISender sender, CancellationToken ct) =>
            sender.Send(new VerifyAuditChain(resourceType), ct).ToOk());
    }
}
