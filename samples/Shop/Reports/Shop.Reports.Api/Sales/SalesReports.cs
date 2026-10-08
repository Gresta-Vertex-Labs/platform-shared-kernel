using System.Globalization;
using System.Net;
using System.Text;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting;

namespace Shop.Reports.Api.Sales;

public static class ReportsPermissions
{
    public const string Export = "reports.export";
}

/// <summary>The stores reports are written to: S3 for downloads, OBS as the archive (both MinIO in development).</summary>
public static class ReportStores
{
    public const string Downloads = "reports";
    public const string Archive = "reports-archive";

    public static readonly IReadOnlyList<string> All = [Downloads, Archive];
}

/// <summary>A report written to object storage, and where to fetch it.</summary>
public sealed record ReportFile(
    string Store,
    string Key,
    string Format,
    long Rows,
    long SizeBytes,
    Uri? DownloadUrl
);

/// <summary>
/// Exports the caller's sales as <paramref name="Format"/> (csv, xlsx, pdf) into <paramref name="Store"/>: the rows stream
/// from PostgreSQL through the exporter into the store (20.Reporting), and the answer carries a presigned download URL.
/// </summary>
[RequirePermission(ReportsPermissions.Export)]
public sealed record ExportSalesCommand(string Format, string Store) : ICommand<ReportFile>;

public sealed class ExportSalesHandler(
    IRequestContext caller,
    ISalesReader sales,
    IReportExporterFactory exporters,
    IClock clock
) : ICommandHandler<ExportSalesCommand, ReportFile>
{
    public static readonly ReportDefinition<Sale> Definition = ReportDefinition
        .For<Sale>()
        .Title("Sales")
        .Column("Paid at", s => s.PaidAt, format: "yyyy-MM-dd HH:mm")
        .Column("Order", s => s.OrderId)
        .Column("Payment", s => s.PaymentId)
        .Column("Amount", s => s.Amount, format: "N2")
        .Column("Currency", s => s.Currency)
        .Build();

    public async Task<Result<ReportFile>> Handle(ExportSalesCommand command, CancellationToken ct)
    {
        if (caller.TenantId is not { } tenant)
        {
            return Result<ReportFile>.Failure(ReportErrors.TenantRequired);
        }

        if (!ReportStores.All.Contains(command.Store))
        {
            return Result<ReportFile>.Failure(
                Error.Validation("reports.unknown_store", $"Unknown store '{command.Store}'.")
            );
        }

        var format = exporters.ParseFormat(command.Format);
        if (format.IsFailure)
        {
            return Result<ReportFile>.Failure(format.Error);
        }

        string fileName = $"sales-{clock.UtcNow:yyyyMMdd-HHmmss}{format.Value.FileExtension}";
        var exported = await exporters
            .GetExporter<Sale>(format.Value)
            .ExportAsync(
                sales.ReadAsync(ct),
                Definition,
                new ReportDestination
                {
                    Store = command.Store,
                    TenantId = tenant,
                    Key = $"sales/{Guid.CreateVersion7():D}/{fileName}",
                    DownloadFileName = fileName,
                    PresignedDownloadUrlExpiry = TimeSpan.FromMinutes(10),
                },
                ct
            );
        if (exported.IsFailure)
        {
            return Result<ReportFile>.Failure(exported.Error);
        }

        var outcome = exported.Value;
        return Result<ReportFile>.Success(
            new ReportFile(
                command.Store,
                outcome.StoredFile.Key,
                outcome.Format.Name,
                outcome.RowCount,
                outcome.SizeBytes,
                outcome.DownloadUrl?.Url
            )
        );
    }
}

/// <summary>
/// The caller's sales statement: an HTML page rendered to PDF by Gotenberg (headless Chromium), stored in the download
/// store behind a presigned URL. Every value is HTML-encoded; nothing in the page is fetched from the network.
/// </summary>
[RequirePermission(ReportsPermissions.Export)]
public sealed record SalesStatementCommand(string MerchantName) : ICommand<ReportFile>;

public sealed class SalesStatementHandler(
    IRequestContext caller,
    ISalesReader sales,
    IHtmlToPdfConverter converter,
    IClock clock
) : ICommandHandler<SalesStatementCommand, ReportFile>
{
    public async Task<Result<ReportFile>> Handle(
        SalesStatementCommand command,
        CancellationToken ct
    )
    {
        if (caller.TenantId is not { } tenant)
        {
            return Result<ReportFile>.Failure(ReportErrors.TenantRequired);
        }

        var html = new StringBuilder()
            .Append("<html><head><meta charset=\"utf-8\"><style>")
            .Append(
                "body{font-family:sans-serif} table{border-collapse:collapse;width:100%} td,th{border:1px solid #999;padding:4px}"
            )
            .Append("</style></head><body>")
            .Append($"<h1>Sales statement — {WebUtility.HtmlEncode(command.MerchantName)}</h1>")
            .Append($"<p>Issued {clock.UtcNow:yyyy-MM-dd HH:mm} UTC</p>")
            .Append("<table><tr><th>Paid at</th><th>Order</th><th>Amount</th></tr>");
        long rows = 0;
        await foreach (var sale in sales.ReadAsync(ct))
        {
            rows++;
            html.Append(
                CultureInfo.InvariantCulture,
                $"<tr><td>{sale.PaidAt:yyyy-MM-dd HH:mm}</td><td>{sale.OrderId:D}</td><td>{sale.Amount:N2} {WebUtility.HtmlEncode(sale.Currency)}</td></tr>"
            );
        }

        html.Append("</table></body></html>");

        string fileName = $"statement-{clock.UtcNow:yyyyMMdd-HHmmss}.pdf";
        var converted = await converter.ConvertAsync(
            html.ToString(),
            new ReportDestination
            {
                Store = ReportStores.Downloads,
                TenantId = tenant,
                Key = $"statements/{Guid.CreateVersion7():D}/{fileName}",
                DownloadFileName = fileName,
                PresignedDownloadUrlExpiry = TimeSpan.FromMinutes(10),
            },
            new HtmlToPdfOptions { FooterHtml = HtmlToPdfOptions.PageNumberFooter },
            ct
        );
        if (converted.IsFailure)
        {
            return Result<ReportFile>.Failure(converted.Error);
        }

        var outcome = converted.Value;
        return Result<ReportFile>.Success(
            new ReportFile(
                ReportStores.Downloads,
                outcome.StoredFile.Key,
                "pdf",
                rows,
                outcome.SizeBytes,
                outcome.DownloadUrl?.Url
            )
        );
    }
}

public static class ReportErrors
{
    public static readonly Error TenantRequired = Error.Forbidden(
        "reports.tenant_required",
        "Reports needs a caller with a tenant."
    );
}
