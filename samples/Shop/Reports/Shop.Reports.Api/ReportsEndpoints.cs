using SharedKernel.Application.Messaging;
using SharedKernel.Presentation.WebApi;
using Shop.Reports.Api.Sales;

namespace Shop.Reports.Api;

public sealed record StatementRequest(string MerchantName);

public sealed class ReportsEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var reports = app.MapGroup("/reports").RequireAuthorization();

        // ?format=csv|xlsx|pdf&store=reports|reports-archive
        reports.MapPost(
            "/sales",
            (string format, string? store, ISender sender, CancellationToken ct) =>
                sender
                    .Send(new ExportSalesCommand(format, store ?? ReportStores.Downloads), ct)
                    .ToOk()
        );

        reports.MapPost(
            "/sales/statement",
            (StatementRequest body, ISender sender, CancellationToken ct) =>
                sender.Send(new SalesStatementCommand(body.MerchantName), ct).ToOk()
        );
    }
}
