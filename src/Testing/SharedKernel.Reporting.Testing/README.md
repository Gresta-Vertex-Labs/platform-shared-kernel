# SharedKernel.Reporting.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **In-memory doubles for `SharedKernel.Reporting.Abstractions`, so export and HTML-to-PDF code is tested without a
> format provider, a Gotenberg container or object storage.** Each double records what it was given and fabricates
> a realistic outcome.

| You get | So that |
| --- | --- |
| `InMemoryReportExporter<TRow>` (`IReportExporter<TRow>`) | You assert the rows, definition and destination of an export |
| `InMemoryReportExporterFactory` (`IReportExporterFactory`) | Code that picks the format at runtime (`ParseFormat` + `GetExporter<T>`) runs unchanged |
| `InMemoryHtmlToPdfConverter` (`IHtmlToPdfConverter`) | You assert the HTML and `HtmlToPdfOptions` a PDF was rendered from |
| `SimulateFailure` / `SimulatedError` | Failure paths return a real `Error`, as production would |
| `AddInMemoryReporting()` | One call swaps the factory and converter in a test host |

## Install

```xml
<PackageReference Include="SharedKernel.Reporting.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

Reference it from a **test project only** — the `TestingNeverReferencedByProduction` architecture rule fails any
production project that references a testing package.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.Testing`, `SharedKernel.Reporting.Abstractions`, `SharedKernel.Storage.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| Namespaces | `SharedKernel.Testing.Reporting` |

## Quick start

```csharp
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting;
using SharedKernel.Testing.Reporting;
using Xunit;

public sealed record Order(int Id, decimal Total);

// The code under test: exports orders in a format the user chose.
public sealed class OrderExport(IReportExporterFactory reports)
{
    private static readonly ReportDefinition<Order> Definition = ReportDefinition.For<Order>()
        .Column("Id", o => o.Id)
        .Column("Total", o => o.Total, format: "N2")
        .Build();

    public async Task<Result<ReportExportOutcome>> RunAsync(string format, IAsyncEnumerable<Order> orders, CancellationToken ct)
    {
        var parsed = reports.ParseFormat(format);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var destination = new ReportDestination
        {
            Store = "reports",
            Key = $"orders{parsed.Value.FileExtension}",
            PresignedDownloadUrlExpiry = TimeSpan.FromMinutes(15),
        };
        return await reports.GetExporter<Order>(parsed.Value).ExportAsync(orders, Definition, destination, ct);
    }
}

public sealed class OrderExportTests
{
    [Fact]
    public async Task Exports_every_order_as_xlsx_with_a_download_link()
    {
        var reports = new InMemoryReportExporterFactory();   // CSV, Xlsx and PDF
        var export = new OrderExport(reports);

        var result = await export.RunAsync("xlsx", Orders(3), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReportFormat.Xlsx, result.Value.Format);
        Assert.NotNull(result.Value.DownloadUrl);
        reports.Exporter<Order>(ReportFormat.Xlsx).ShouldHaveExported(rows => rows.Count == 3);
    }

    [Fact]
    public async Task Rejects_an_unknown_format()
    {
        var result = await new OrderExport(new InMemoryReportExporterFactory())
            .RunAsync("docx", Orders(1), CancellationToken.None);

        Assert.Equal(ReportingErrorCodes.UnsupportedFormat, result.Error.Code);
    }

    private static async IAsyncEnumerable<Order> Orders(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            yield return new Order(i, i * 10m);
        }

        await Task.CompletedTask;
    }
}
```

## How it works

- **Exporter.** `ExportAsync` and `ExportToStreamAsync` read every row (honouring cancellation), then record them in
  `LastRows` with `LastDefinition` and, for storage exports, `LastDestination`. `ExportAsync` fabricates a
  `ReportExportOutcome`: `StoredFile` from the destination's store, tenant and key, `RowCount`, `SizeBytes`, and —
  when `PresignedDownloadUrlExpiry` is set — a `DownloadUrl` (`https://reports.test/{store}/{key}`) expiring at the
  clock's now plus that expiry. Nothing is written to storage.
- **Stream rendering.** `ExportToStreamAsync` writes a tab-separated rendering — headers first, then one line per row
  formatted with the definition's culture and column formats — so an HTTP download test sees real content.
- **Failures.** With `SimulateFailure = true` the rows are still read and recorded, then the export fails with
  `SimulatedError`, or by default `storage.unavailable` (`ExportAsync`) / `reporting.invalid_destination`
  (`ExportToStreamAsync`).
- **Factory.** Supports the formats passed to its constructor, or CSV, Xlsx and PDF when none are. `ParseFormat`
  matches a format name, file extension (with or without the dot) or content type, case-insensitively, and returns
  `reporting.unsupported_format` otherwise. `GetExporter<TRow>` and `Exporter<TRow>` return the same cached
  exporter per format and row type; an unsupported format throws `InvalidOperationException`.
- **Converter.** Records each call as an `HtmlConversion` (`Html`, `Options` — `HtmlToPdfOptions.Default` when none
  given — and `Destination`, `null` for streams). `ConvertToStreamAsync` writes `PlaceholderPdf` (a tiny `%PDF-`
  document); `ConvertAsync` returns a `PdfDocumentOutcome` for the destination. `SimulateFailure` returns
  `SimulatedError` or `reporting.converter_unavailable`.
- **Clock.** Pass an `IClock` (a `FakeClock` makes `DownloadUrl.ExpiresAt` exact); without one each exporter uses a
  new `FakeClock`.

Where it simplifies:

- The definition and destination are not validated (no `reporting.invalid_definition` or
  `reporting.invalid_destination` for a missing store, key or column), and no provider limit such as the PDF
  `MaxRows` is enforced.
- `Condition`, `DownloadFileName` and `Metadata` on the destination are recorded, not applied.
- An exporter keeps only its **last** export (`ExportCount` counts them all) and holds every row in memory — never
  infer a real provider's streaming behaviour from it.
- The exporter is not synchronized; the factory's cache and the converter's record are thread-safe.

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddInMemoryReporting(this IServiceCollection)` | Removes existing `IReportExporterFactory` and `IHtmlToPdfConverter` registrations, then adds singletons `InMemoryReportExporterFactory` (with the host's `IClock` when registered) and `InMemoryHtmlToPdfConverter`, each also as its interface |

The per-format exporters injected directly (`ICsvReportExporter<T>`, `ISpreadsheetReportExporter<T>`,
`IPdfReportExporter<T>`) are **not** replaced — construct an `InMemoryReportExporter<TRow>` for code that takes one.

### `InMemoryReportExporter<TRow> : IReportExporter<TRow>`

| Member | Purpose |
| --- | --- |
| `InMemoryReportExporter(ReportFormat? format = null, IClock? clock = null)` | `Format` defaults to CSV |
| `LastRows`, `LastDefinition`, `LastDestination`, `ExportCount` | What the last export received; how many exports ran |
| `SimulateFailure`, `SimulatedError` | Make every export fail, with your `Error` or the default |
| `ShouldHaveExported(Predicate<IReadOnlyList<TRow>>? rowsPredicate = null)` | An export happened (and its rows match) |
| `Reset()` | Clears the record, the counter and the failure settings |

### `InMemoryReportExporterFactory : IReportExporterFactory`

| Member | Purpose |
| --- | --- |
| `InMemoryReportExporterFactory(params ReportFormat[] formats)`, `(IClock? clock, params ReportFormat[] formats)` | Supported formats (default CSV, Xlsx, PDF) |
| `Formats`, `ParseFormat(string? value)`, `GetExporter<TRow>(ReportFormat format)` | The production contract |
| `Exporter<TRow>(ReportFormat format)` | The same exporter, typed as `InMemoryReportExporter<TRow>` for assertions |

### `InMemoryHtmlToPdfConverter : IHtmlToPdfConverter`

| Member | Purpose |
| --- | --- |
| `Conversions`, `LastConversion` | Every `HtmlConversion(string Html, HtmlToPdfOptions Options, ReportDestination? Destination)` |
| `PlaceholderPdf` (`static ReadOnlyMemory<byte>`) | The bytes written to streams |
| `SimulateFailure`, `SimulatedError`, `Reset()` | As on the exporter |

### Errors returned

| Code | When |
| --- | --- |
| `reporting.unsupported_format` | `ParseFormat` with a format the factory does not support |
| `storage.unavailable` | `ExportAsync` with `SimulateFailure` and no `SimulatedError` |
| `reporting.invalid_destination` | `ExportToStreamAsync` with `SimulateFailure` and no `SimulatedError` |
| `reporting.converter_unavailable` | The converter with `SimulateFailure` and no `SimulatedError` |

Use `ReportingErrors` (e.g. `RowLimitExceeded(ReportFormat.Pdf, 10_000)`, `ConversionTimeout(TimeSpan.FromSeconds(60))`)
for `SimulatedError` to drive other paths.

## Testing

This package is the test double; its own self-tests live in
[`SharedKernel.Reporting.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Reporting.Testing/SharedKernel.Reporting.Testing.Tests),
proving each double against the `IReportExporter<TRow>`, `IReportExporterFactory` and `IHtmlToPdfConverter`
contracts. Pair it with [`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Testing/README.md)
for `FakeClock` and `TestRequestContext`, and with
[`SharedKernel.Storage.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Storage.Testing/README.md)
when the code under test also reads the stored report back.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference it from a production project | Reference it from test projects only | `TestingNeverReferencedByProduction` fails the build's architecture tests |
| Expect the stored file to exist after `ExportAsync` | Assert on `LastDestination` / the outcome | The exporter never writes to storage |
| Rely on it to reject a bad definition, destination or row count | Test validation and `MaxRows` against a real `20.Reporting` provider | The fake validates nothing |
| Expect `AddInMemoryReporting()` to replace `ICsvReportExporter<T>` and friends | Construct an `InMemoryReportExporter<TRow>` for code that injects one | Only the factory and the converter are swapped |
| Assert on an earlier export after a second one | Assert between exports, or use one exporter per export | `LastRows` holds only the last export |
| Parse `PlaceholderPdf` as a real PDF | Assert on `HtmlConversion.Html` and `Options` | It is a marker, not a rendered document |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · [16.Testing domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/README.md) · [MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
