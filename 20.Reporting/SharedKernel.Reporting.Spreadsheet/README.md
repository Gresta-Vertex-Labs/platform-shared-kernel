# SharedKernel.Reporting.Spreadsheet

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-6f42c1)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
![Memory: constant](https://img.shields.io/badge/memory-constant-success)

> **Excel (`.xlsx`) exports for `SharedKernel.Reporting`, streamed in constant memory with typed cells — numbers,
> dates and booleans stay sortable and summable in Excel. Built on [SpreadCheetah](https://github.com/sveinungf/spreadcheetah)
> (MIT, no dependencies).**

| You get | So that |
| --- | --- |
| `AddSpreadsheet(configuration)` | One call serves every row type: `ISpreadsheetReportExporter<T>` or the `xlsx` exporter from the factory |
| Constant-memory streaming | A million-row workbook needs no more memory than a small one |
| Typed cells with translated number formats | `N2` becomes `#,##0.00`; dates are real dates, not text |
| Bold, frozen, filterable header row | The sheet is usable the moment it opens |
| Text is never a formula | `=HYPERLINK(…)` in a value is shown, not run |
| `MaxRows` guard | An export past Excel's row limit fails cleanly and stores nothing |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Configuration](#configuration)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)

## Install

```xml
<PackageReference Include="SharedKernel.Reporting.Spreadsheet" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Adapter — reference it from your **Infrastructure** (or Api) project |
| Depends on | `SharedKernel.Reporting.Abstractions`, `SharedKernel.Configuration`, SpreadCheetah (MIT) |
| Namespaces | `SharedKernel.Reporting` (`AddSpreadsheet`), `SharedKernel.Reporting.Spreadsheet` (`ISpreadsheetReportExporter<T>`, `SpreadsheetExportOptions`) |

## Quick start

```csharp
using SharedKernel.Reporting;

builder.Services.AddSharedKernelStorage().AddS3(builder.Configuration).AddStore("reports");
builder.Services.AddSharedKernelReporting().AddSpreadsheet(builder.Configuration);   // SharedKernel:Reporting:Spreadsheet
```

```json
{
  "SharedKernel": {
    "Reporting": {
      "Spreadsheet": { "DefaultSheetName": "Export", "AutoFilter": true }
    }
  }
}
```

```csharp
using SharedKernel.Reporting;
using SharedKernel.Reporting.Spreadsheet;

public sealed class ExportInvoices(ISpreadsheetReportExporter<Invoice> excel, IInvoiceQueries invoices)
{
    private static readonly ReportDefinition<Invoice> Definition = ReportDefinition.For<Invoice>()
        .Title("Invoices 2026-09")                                  // becomes the sheet name
        .Column("Number", i => i.Number)
        .Column("Issued", i => i.IssuedOn, format: "dd.MM.yyyy")   // a date cell, format translated
        .Column("Amount", i => i.Amount, format: "N2", relativeWidth: 1.5)
        .Column("Paid", i => i.IsPaid)                             // a boolean cell
        .Build();

    public Task<Result<ReportExportOutcome>> ExportAsync(CancellationToken ct) =>
        excel.ExportAsync(invoices.StreamAsync(ct), Definition,
            new ReportDestination { Store = "reports", Key = "invoices/2026-09.xlsx", DownloadFileName = "Invoices.xlsx" },
            ct);
}
```

## How it works

- **Streaming.** SpreadCheetah writes the workbook as rows arrive, asynchronously, into the storage upload pipe or
  your stream.
- **Layout.** The title names the sheet (invalid characters replaced; `DefaultSheetName` when there is no title) and
  becomes the document title. Column width is the larger of 10 and the header length + 2, times the column's
  `RelativeWidth`, clamped to 4–100. An explicit `Left`/`Center`/`Right` alignment is applied; `Auto` leaves Excel's
  natural alignment (numbers right).
- **Row limit.** When the export reaches `MaxRows`, it fails with `reporting.row_limit_exceeded` and nothing is stored.

### Typed cells

| Value | Cell | Number format |
| --- | --- | --- |
| `int`, `long`, `decimal`, `double`, … | number | the column's `Format`, translated, else General |
| `DateTime`, `DateTimeOffset` (its clock time) | date | `Format` translated, else `yyyy-mm-dd hh:mm:ss` |
| `DateOnly` | date | `Format` translated, else `yyyy-mm-dd` |
| `TimeOnly` | time | `hh:mm:ss` |
| `TimeSpan` | duration | `[h]:mm:ss` |
| `bool` | boolean | |
| anything else (`string`, `Guid`, enums, …) | text | formatted by `Format` and the report's culture |

A column with a formatter function is always text. .NET numeric formats are translated — `N2` → `#,##0.00`,
`F3` → `0.000`, `D5` → `00000`, `P1` → `0.0%`, `E2` → `0.00E+00`, `C2` → the culture's currency symbol and position; a
custom numeric format (`#,##0.00;(#,##0.00)`) passes through. Date patterns are converted (`dd.MM.yyyy HH:mm` →
`dd.mm.yyyy hh:mm`); standard ones (`d`, `G`, …) use the report culture's patterns.

## Configuration

Section `SharedKernel:Reporting:Spreadsheet`, validated when the host starts.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SharedKernel:Reporting:Spreadsheet:DefaultSheetName` | `string` | `Report` | Sheet name when the report has no title; at most 31 characters, none of `\ / ? * [ ] :` |
| `SharedKernel:Reporting:Spreadsheet:BoldHeaderRow` | `bool` | `true` | Bold header row |
| `SharedKernel:Reporting:Spreadsheet:FreezeHeaderRow` | `bool` | `true` | Header stays visible while scrolling |
| `SharedKernel:Reporting:Spreadsheet:AutoFilter` | `bool` | `true` | Filter buttons on the header |
| `SharedKernel:Reporting:Spreadsheet:MaxRows` | `int` | `1048575` | Data-row cap, 1 – 1,048,575 (`SpreadsheetExportOptions.ExcelMaxDataRows`, Excel's limit) |

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `IReportingBuilder.AddSpreadsheet(IConfiguration)` | `SpreadsheetExportOptions` (validated); `ISpreadsheetReportExporter<>` (open generic, singleton); the `xlsx` exporter in `IReportExporterFactory` and as keyed `IReportExporter<T>` |

`ReportFormat.Xlsx`: name `xlsx`, content type `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`,
extension `.xlsx`.

### Errors

| Code | Type | When |
| --- | --- | --- |
| `reporting.row_limit_exceeded` | Validation | More rows than `MaxRows`; nothing is stored |

Plus the shared `reporting.*` definition and destination codes.

### Logging

No events of its own (EventId block 20200–20299 is reserved); the base class logs EventIds 20000–20006.

## Testing

Unit tests of code that exports use
[`SharedKernel.Reporting.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Reporting.Testing/README.md)
(`AddInMemoryReporting()`, `InMemoryReportExporter<T>`). To check the workbook itself, export to a `MemoryStream` and
open it with any `.xlsx` reader (this repository's own tests use ClosedXML).

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Use a formatter function for numbers or dates | Use `format:` | A formatter makes the cell text; Excel can no longer sum or sort it |
| Put `\ / ? * [ ] :` in `DefaultSheetName` | Use a plain name of at most 31 characters | Excel rejects it; the host fails to start |
| Expect time-zone conversion of `DateTimeOffset` | Convert before exporting | The cell holds the value's clock time |
| Export more than Excel's row limit | Split the export, or use CSV | The export fails with `reporting.row_limit_exceeded` |

## Design decisions

**Why SpreadCheetah?** It is MIT-licensed, dependency-free and writes a workbook as an async stream. ClosedXML builds
the whole workbook in memory and saves synchronously; EPPlus is not free for commercial use.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Reporting domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/20.Reporting/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
