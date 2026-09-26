# SharedKernel.Reporting.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**In-memory fakes for [`SharedKernel.Reporting`](../../20.Reporting/SharedKernel.Reporting.Abstractions/README.md),
so export and PDF code is tested without a format provider, a browser or storage.**

| Fake | Replaces | Records |
|---|---|---|
| `InMemoryReportExporter<TRow>` | `IReportExporter<TRow>` | the rows, definition and destination of each export |
| `InMemoryReportExporterFactory` | `IReportExporterFactory` | one fake exporter per format and row type |
| `InMemoryHtmlToPdfConverter` | `IHtmlToPdfConverter` | every HTML document, its options and destination |

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.Reporting.Testing" />
```

Namespace: `SharedKernel.Testing.Reporting`.

## A handler that picks the format at runtime

```csharp
var reports = new InMemoryReportExporterFactory();              // CSV, Excel and PDF; or pass your own formats
var handler = new ExportOrdersHandler(reports, orders, caller);

Result<ReportExportOutcome> result = await handler.Handle(new ExportOrders(Format: "xlsx"), ct);

result.Value.Format.Should().Be(ReportFormat.Xlsx);
reports.Exporter<Order>(ReportFormat.Xlsx).ShouldHaveExported(rows => rows.Count == 3);
reports.ParseFormat("docx").Error.Code.Should().Be(ReportingErrorCodes.UnsupportedFormat);
```

## One exporter

```csharp
var exporter = new InMemoryReportExporter<Invoice>(ReportFormat.Csv, new FakeClock());

await service.ExportAsync(exporter, ct);

exporter.LastRows.Should().HaveCount(2);
exporter.LastDestination!.Key.Should().EndWith(".csv");
exporter.ExportCount.Should().Be(1);
```

- `ExportAsync` fabricates the outcome from the destination — `StoredFile`, and a `DownloadUrl` expiring at the clock's
  now plus `PresignedDownloadUrlExpiry` — without touching storage.
- `ExportToStreamAsync` writes one tab-separated line per row, header first, so an HTTP download test sees content.
- `SimulateFailure = true` makes every export fail after reading the rows — with `storage.unavailable`, or the
  `SimulatedError` you set (e.g. `ReportingErrors.RowLimitExceeded(ReportFormat.Pdf, 10_000)`); `Reset()` forgets everything.
- It keeps every row so a test can assert on them — never infer a real provider's memory behaviour from it.

## HTML to PDF

```csharp
var converter = new InMemoryHtmlToPdfConverter();

await service.SendInvoiceAsync(invoice, converter, ct);

converter.LastConversion!.Html.Should().Contain(invoice.Number);
converter.LastConversion.Options.FooterHtml.Should().NotBeNull();
converter.LastConversion.Destination!.Condition.Should().Be(WriteCondition.IfNotExists);
```

It writes `InMemoryHtmlToPdfConverter.PlaceholderPdf` (a tiny `%PDF-` placeholder) to streams, and returns a
`PdfDocumentOutcome` for storage destinations. `SimulateFailure` returns `reporting.converter_unavailable`, or your
`SimulatedError` (e.g. `ReportingErrors.ConversionTimeout(TimeSpan.FromSeconds(60))`).

## In a test host

```csharp
services.AddSharedKernelReporting()…;                  // or the real registration of the app under test
services.AddInMemoryReporting();                       // replaces the factory and the converter

var reports = provider.GetRequiredService<InMemoryReportExporterFactory>();
var pdfs = provider.GetRequiredService<InMemoryHtmlToPdfConverter>();
```

The factory uses the host's `IClock` when one is registered.
