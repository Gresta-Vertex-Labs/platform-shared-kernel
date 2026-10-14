---
proven-by:
  - samples/Shop/Reports/Shop.Reports.Api/Sales/SalesReports.cs
  - samples/Shop/Reports/Shop.Reports.Api/Program.cs
  - samples/Shop/Reports/Shop.Reports.Tests/ReportsTests.cs
unproven:
  - Exporting to a caller-supplied stream instead of a store: the reference platform always delivers to object storage
  - Injecting one IReportExporter<TRow> for a fixed format: the reference platform picks the format per request through the factory
---
# Add report export

**Use when:** a use case streams rows into a CSV, Excel (.xlsx) or tabular PDF file, or renders an HTML page to PDF,
and hands the caller a presigned download link. **Not when:** you store a file the user uploaded (add-file-storage.md)
or return a page of rows as JSON (a query with `PagedList<T>`, add-http-endpoint.md).

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Application | `SharedKernel.Reporting.Abstractions` | `IReportExporterFactory`, `IReportExporter<TRow>`, `ReportDefinition`, `ReportDestination`, `IHtmlToPdfConverter` |
| Application | `SharedKernel.Storage.Abstractions` | The stores the files are delivered to (add-file-storage.md) |
| Infrastructure | `SharedKernel.Reporting.Csv` | CSV, streamed, with a CSV-injection guard |
| Infrastructure | `SharedKernel.Reporting.Spreadsheet` | Excel with typed cells |
| Infrastructure | `SharedKernel.Reporting.Pdf` | A title and one paged table |
| Infrastructure | `SharedKernel.Reporting.Gotenberg` | HTML to PDF through a Gotenberg container |
| Infrastructure | `SharedKernel.Storage.S3` (or `SharedKernel.Storage.Obs`) | The bucket behind the store |
| Tests | `SharedKernel.Reporting.Testing` | In-memory exporters and converter |

Reference only the format packages you use. The reference platform's Reports is a single-project host; in a
four-project service the handler lives in Application and the registration in Infrastructure.

## Steps

1. **Application: declare the columns once.** A `ReportDefinition<TRow>` is the header, value and format of each
   column; every format uses the same one.

```csharp
using SharedKernel.Reporting;

public static readonly ReportDefinition<Sale> Definition = ReportDefinition
    .For<Sale>()
    .Title("Sales")
    .Column("Paid at", s => s.PaidAt, format: "yyyy-MM-dd HH:mm")
    .Column("Order", s => s.OrderId)
    .Column("Payment", s => s.PaymentId)
    .Column("Amount", s => s.Amount, format: "N2")
    .Column("Currency", s => s.Currency)
    .Build();
```

2. **Application: stream the rows into the caller's tenant store.** Rows are an `IAsyncEnumerable<TRow>`, so the
   export never holds them all in memory. The factory parses the requested format and refuses an unknown one; the
   destination names the store, the tenant, the key and how long the download link lives.

```csharp
using SharedKernel.Application.Messaging;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting;

public sealed class ExportSalesHandler(IRequestContext caller, ISalesReader sales, IReportExporterFactory exporters, IClock clock)
    : ICommandHandler<ExportSalesCommand, ReportFile>
{
    public async Task<Result<ReportFile>> Handle(ExportSalesCommand command, CancellationToken ct)
    {
        if (caller.TenantId is not { } tenant)
        {
            return Result<ReportFile>.Failure(ReportErrors.TenantRequired);
        }

        var format = exporters.ParseFormat(command.Format);   // "csv", "xlsx", "pdf"
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

        // exported.Value: StoredFile.Key, Format.Name, RowCount, SizeBytes, DownloadUrl?.Url
    }
}
```

   Check a store name that comes from the request against your list of stores before exporting.

3. **Application: render HTML to PDF.** `IHtmlToPdfConverter` takes the HTML, the same `ReportDestination` and the
   page options. HTML-encode every value you put in the page; the converter fetches nothing from the network.

```csharp
using System.Net;
using SharedKernel.Reporting;

html.Append($"<h1>Sales statement — {WebUtility.HtmlEncode(command.MerchantName)}</h1>");
// ...one encoded table row per sale...
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
```

4. **Infrastructure: register the stores and the formats.** The stores are ordinary tenant stores
   (add-file-storage.md); the reporting builder adds one call per format and the converter.

```csharp
using SharedKernel.Reporting;
using SharedKernel.Storage;

var storage = builder.Services.AddSharedKernelStorage();
storage.AddS3(builder.Configuration).AddTenantStore(ReportStores.Downloads);
storage.AddObs(builder.Configuration).AddTenantStore(ReportStores.Archive);

builder
    .Services.AddSharedKernelReporting()
    .AddCsv(builder.Configuration)
    .AddSpreadsheet(builder.Configuration)
    .AddPdf(builder.Configuration)
    .AddGotenberg(builder.Configuration);
```

5. **Api/Worker:** `builder.Services.AddHealthChecks().AddSharedKernelReadiness()`. Gotenberg registers a readiness
   probe (not a liveness one): an outage takes the service out of readiness without restarting it.

6. **Configuration:** `SharedKernel:Reporting:Csv`, `SharedKernel:Reporting:Spreadsheet`, `SharedKernel:Reporting:Pdf`
   and `SharedKernel:Reporting:Gotenberg`, plus the storage sections. The keys are in each package's README.

## Test it

The in-memory factory records what each exporter received, where it was delivered and for which tenant:

```csharp
using FluentAssertions;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Execution;
using SharedKernel.Testing.Reporting;

var exporters = new InMemoryReportExporterFactory();
var handler = new ExportSalesHandler(TestRequestContext.ForTenant(Contoso), new FixedSales(Sales), exporters, new FakeClock());

var exported = await handler.Handle(new ExportSalesCommand("csv", ReportStores.Archive), default);

exported.IsSuccess.Should().BeTrue();
var exporter = exporters.Exporter<Sale>(exporters.ParseFormat("csv").Value);
exporter.ShouldHaveExported(rows => rows.Count == 2);
exporter.LastDestination!.TenantId.Should().Be(Contoso);
```

`InMemoryHtmlToPdfConverter` exposes `LastConversion` (the HTML and the destination) and `SimulateFailure`, so an
unencoded value or a converter outage is a unit test. The real formats and Gotenberg belong in an end-to-end test.

## Check

- **SK0001**: `DateTime.UtcNow` in a file name; inject `IClock`.
- **SK0030**: an `ExportAsync` or `ConvertAsync` result nobody reads.
- An unknown format is the factory's failure (`reporting.format_not_supported`), not an exception.
- Exporting to a store without `AddSharedKernelStorage()` throws `InvalidOperationException` on first use: a
  configuration error, not a `Result`.
- Gotenberg unreachable is `reporting.converter_unavailable`, and `/health/ready` reports it.

## Read next

- `SharedKernel.Reporting.Abstractions`
- `SharedKernel.Reporting.Csv`
- `SharedKernel.Reporting.Spreadsheet`
- `SharedKernel.Reporting.Pdf`
- `SharedKernel.Reporting.Gotenberg`
- `SharedKernel.Reporting.Testing`
