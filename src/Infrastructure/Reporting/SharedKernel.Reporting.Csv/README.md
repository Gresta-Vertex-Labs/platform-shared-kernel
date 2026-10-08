# SharedKernel.Reporting.Csv

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-6f42c1)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
![Memory: constant](https://img.shields.io/badge/memory-constant-success)

> **RFC 4180 CSV exports behind `IReportExporter<TRow>`, written one row at a time so a million-row export costs no
> more memory than a ten-row one, with a CSV-injection guard on by default and no third-party dependency.** Pick CSV
> for bulk data and machine feeds; use `SharedKernel.Reporting.Spreadsheet` when people want typed Excel cells, and
> `SharedKernel.Reporting.Pdf` for a printable table.

| You get | So that |
| --- | --- |
| `AddCsv(configuration)` | One call serves every row type: `ICsvReportExporter<Order>`, `ICsvReportExporter<Invoice>`, … |
| Constant-memory streaming | Bulk extracts never materialize the result set |
| Formula escaping (CWE-1236) | A customer name like `=HYPERLINK(…)` shows as text, not a live formula |
| Culture-aware values | `1.234,50` under `de-DE` with `N2`, quoted only when the delimiter requires it |
| Configurable delimiter, BOM and header row | Files open correctly in Excel in decimal-comma locales |

## Install

```xml
<PackageReference Include="SharedKernel.Reporting.Csv" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Adapter — reference it from your **Infrastructure** project |
| Depends on | `SharedKernel.Reporting.Abstractions`, `SharedKernel.Configuration` — no third-party packages |
| Namespaces | `SharedKernel.Reporting` (`AddCsv`), `SharedKernel.Reporting.Csv` (`ICsvReportExporter<T>`, `CsvExportOptions`) |

## Quick start

```csharp
using SharedKernel.Reporting;
using SharedKernel.Storage;

builder.Services.AddSharedKernelStorage().AddS3(builder.Configuration).AddStore("reports");
builder.Services.AddSharedKernelReporting().AddCsv(builder.Configuration);   // SharedKernel:Reporting:Csv
```

```json
{
  "SharedKernel": {
    "Reporting": {
      "Csv": { "Delimiter": ";", "IncludeUtf8Bom": true }
    }
  }
}
```

Application code uses the format-neutral contract (`IReportExporterFactory.GetExporter<T>(ReportFormat.Csv)`) or the
typed exporter:

```csharp
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting;
using SharedKernel.Reporting.Csv;

public sealed class ExportCustomers(ICsvReportExporter<Customer> csv, ICustomerQueries customers)
{
    private static readonly ReportDefinition<Customer> Definition = ReportDefinition.For<Customer>()
        .Column("Id", c => c.Id)
        .Column("Name", c => c.Name)
        .Column("Balance", c => c.Balance, format: "N2")
        .Build();

    public Task<Result<ReportStreamOutcome>> WriteAsync(Stream response, CancellationToken ct) =>
        csv.ExportToStreamAsync(customers.StreamAllAsync(ct), Definition, response, ct);
}
```

## How it works

- **RFC 4180.** A field is quoted when it contains the delimiter, `"`, CR or LF; `"` is doubled. Lines end with CRLF.
- **Streaming.** Each row is formatted into a reused buffer and written asynchronously to the destination — the
  storage upload pipe or your stream. Memory does not grow with the row count.
- **Values.** Each column's `format` is applied under the report's `Culture` (`ReportValueFormatting`). `null` is an
  empty field. The report title, column alignment and widths do not apply to CSV.
- **CSV injection guard.** With `EscapeFormulas` (default), a text field — header or value — that starts with `=`,
  `+`, `-`, `@`, a tab or a carriage return is prefixed with `'`, so a spreadsheet shows it as text. **Numbers are
  never prefixed**: `-5.25` from a `decimal` stays a number.
- **Encoding.** UTF-8, with a byte-order mark when `IncludeUtf8Bom` (Excel needs it to read non-ASCII text).

## Configuration

Section `SharedKernel:Reporting:Csv`, validated when the host starts.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SharedKernel:Reporting:Csv:Delimiter` | `char` | `,` | Field separator. `;` suits Excel in decimal-comma locales (tr-TR, de-DE, fr-FR). A tab is allowed; `"`, CR, LF and other control characters are rejected |
| `SharedKernel:Reporting:Csv:IncludeUtf8Bom` | `bool` | `true` | Write the UTF-8 byte-order mark |
| `SharedKernel:Reporting:Csv:IncludeHeaderRow` | `bool` | `true` | First line holds the column headers |
| `SharedKernel:Reporting:Csv:EscapeFormulas` | `bool` | `true` | Prefix formula-like text with `'` (see above) |

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `IReportingBuilder.AddCsv(IConfiguration)` | `CsvExportOptions` (validated); `ICsvReportExporter<>` (open generic, singleton); the `csv` exporter in `IReportExporterFactory` and as keyed `IReportExporter<T>` |

`ReportFormat.Csv`: name `csv`, content type `text/csv`, extension `.csv`.

### Errors

Only the shared `reporting.*` codes from `SharedKernel.Reporting.Abstractions` (for example
`reporting.invalid_definition`); CSV has no row limit.

### Logging

This package logs nothing of its own (EventId block 20100–20199 is reserved). Export start, completion and failure
are logged by the base class, EventIds 20000–20006.

## Testing

Unit tests of code that exports use
[`SharedKernel.Reporting.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Reporting/SharedKernel.Reporting.Testing/README.md):
`services.AddInMemoryReporting()` swaps the factory for `InMemoryReportExporterFactory`, whose exporters record the
rows, definition and destination. To test the file itself, export to a `MemoryStream` with the real exporter and
parse the text.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Turn `EscapeFormulas` off for files people open | Leave it on; turn it off only for machine-to-machine feeds | A leading `=`, `+`, `-` or `@` runs as a formula in a spreadsheet |
| Use `,` for decimal-comma locales opened in Excel | `Delimiter: ";"` | Excel splits `1.234,50` otherwise (the field is quoted, but Excel's locale still expects `;`) |
| Drop the BOM for Excel users | Keep `IncludeUtf8Bom` | Excel misreads non-ASCII text without it |
| Expect the title in the file | Put it in the file name (`DownloadFileName`) | CSV has no title row |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Reporting packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Reporting/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
