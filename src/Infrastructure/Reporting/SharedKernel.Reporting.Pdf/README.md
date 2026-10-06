# SharedKernel.Reporting.Pdf

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-6f42c1)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
![Memory: capped by MaxRows](https://img.shields.io/badge/memory-capped%20by%20MaxRows-yellow)

> **Tabular PDF exports for `SharedKernel.Reporting` — statements, lists, extracts: an optional title and one table
> that always fits the page, with repeated headers and page numbers. Built on [PDFsharp/MigraDoc](https://docs.pdfsharp.net/)
> (MIT) with an embedded font, so it renders identically on Windows and in Linux containers.**

| You get | So that |
| --- | --- |
| `AddPdf(configuration)` | One call serves every row type: `IPdfReportExporter<T>` or the `pdf` exporter from the factory |
| Columns sized by `RelativeWidth` to the usable page width | However many columns, the table never runs off the page; long values wrap |
| Bold, shaded header repeated on every page, "3 / 12" footer | Multi-page statements stay readable |
| Embedded Roboto (Apache-2.0) | No dependency on fonts installed in the container |
| `MaxRows` cap | A runaway export fails cleanly instead of exhausting memory |

For free-form documents (invoices, letters, anything with a layout), render HTML with
[`SharedKernel.Reporting.Gotenberg`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Reporting/SharedKernel.Reporting.Gotenberg/README.md) instead.

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
<PackageReference Include="SharedKernel.Reporting.Pdf" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Adapter — reference it from your **Infrastructure** (or Api) project |
| Depends on | `SharedKernel.Reporting.Abstractions`, `SharedKernel.Configuration`, PDFsharp + PDFsharp-MigraDoc (MIT) |
| Namespaces | `SharedKernel.Reporting` (`AddPdf`), `SharedKernel.Reporting.Pdf` (`IPdfReportExporter<T>`, `PdfExportOptions`, `PdfPaperSize`) |

## Quick start

```csharp
using SharedKernel.Reporting;

builder.Services.AddSharedKernelStorage().AddS3(builder.Configuration).AddStore("statements");
builder.Services.AddSharedKernelReporting().AddPdf(builder.Configuration);   // SharedKernel:Reporting:Pdf
```

```json
{
  "SharedKernel": {
    "Reporting": {
      "Pdf": { "PaperSize": "A4", "Landscape": true, "MaxRows": 5000 }
    }
  }
}
```

```csharp
using SharedKernel.Reporting;
using SharedKernel.Reporting.Pdf;

public sealed class RenderStatement(IPdfReportExporter<StatementLine> pdf, IStatementQueries statements)
{
    private static readonly ReportDefinition<StatementLine> Definition = ReportDefinition.For<StatementLine>()
        .Title("Account statement — September 2026")
        .Column("Date", l => l.BookedOn, format: "d")
        .Column("Description", l => l.Description, relativeWidth: 4)
        .Column("Amount", l => l.Amount, format: "N2")            // right-aligned automatically
        .Build();

    public Task<Result<ReportExportOutcome>> StoreAsync(Guid accountId, CancellationToken ct) =>
        pdf.ExportAsync(statements.StreamAsync(accountId, ct), Definition,
            new ReportDestination
            {
                Store = "statements",
                Key = $"{accountId:N}/2026-09.pdf",
                Condition = WriteCondition.IfNotExists,
                PresignedDownloadUrlExpiry = TimeSpan.FromMinutes(15),
            },
            ct);
}
```

## How it works

- **Layout.** The title is a heading on the first page and the PDF's document title. Columns share the usable width
  in proportion to `RelativeWidth`; long values wrap inside their cell. The header row is bold, shaded and repeated on
  every page. Numbers are right-aligned, everything else left-aligned, unless the column sets its alignment. Values are
  formatted by the column's `Format` and the report's `Culture`.
- **Memory.** MigraDoc builds the whole document, PDFsharp renders it to a `MemoryStream` (it needs a seekable
  stream), and the bytes are then copied to the destination. Memory and time grow with the row count — which is what
  `MaxRows` bounds. When the limit is reached the export fails with `reporting.row_limit_exceeded` and nothing is
  stored.
- **Fonts.** PDFsharp uses no installed fonts. On first use the package installs its embedded Roboto resolver
  (Regular, Bold) as the process-wide `GlobalFontSettings.FontResolver`, unless one is already set. Roboto covers
  Latin (including Turkish), Greek and Cyrillic; the licence text ships in the package (`Fonts/LICENSE.txt`).

## Configuration

Section `SharedKernel:Reporting:Pdf`, validated when the host starts.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SharedKernel:Reporting:Pdf:PaperSize` | `PdfPaperSize` | `A4` | `A4`, `A3`, `A5`, `Letter`, `Legal` |
| `SharedKernel:Reporting:Pdf:Landscape` | `bool` | `false` | Landscape orientation — the usual choice for wide tables |
| `SharedKernel:Reporting:Pdf:MarginMillimeters` | `double` | `15` | Margin on every side, 0–100 mm |
| `SharedKernel:Reporting:Pdf:FontSize` | `double` | `9` | Body font size in points, 5–24; the title is 1.8× larger |
| `SharedKernel:Reporting:Pdf:ShowPageNumbers` | `bool` | `true` | "3 / 12" centered at the bottom of every page |
| `SharedKernel:Reporting:Pdf:AlternateRowShading` | `bool` | `true` | Every other row lightly shaded |
| `SharedKernel:Reporting:Pdf:MaxRows` | `int` | `10000` | Row cap, 1 – 1,000,000 |

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `IReportingBuilder.AddPdf(IConfiguration)` | `PdfExportOptions` (validated); `IPdfReportExporter<>` (open generic, singleton); the `pdf` exporter in `IReportExporterFactory` and as keyed `IReportExporter<T>` |

`ReportFormat.Pdf`: name `pdf`, content type `application/pdf`, extension `.pdf`.

### Errors

| Code | Type | When |
| --- | --- | --- |
| `reporting.row_limit_exceeded` | Validation | More rows than `MaxRows`; nothing is stored |

Plus the shared `reporting.*` definition and destination codes.

### Logging

No events of its own (EventId block 20300–20399 is reserved); the base class logs EventIds 20000–20006.

## Testing

Unit tests of code that exports use
[`SharedKernel.Reporting.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Reporting/SharedKernel.Reporting.Testing/README.md)
(`AddInMemoryReporting()`, `InMemoryReportExporter<T>`). To check the document itself, export to a `MemoryStream` and
open it with PDFsharp's reader or any PDF text extractor.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Export bulk data as PDF | CSV or Excel for tens of thousands of rows | PDF is built in memory; nobody reads a 50,000-row PDF |
| Raise `MaxRows` to "make it work" | Keep the cap near what a reader needs | The cap is the memory guard |
| Install another PDFsharp font resolver without Roboto | Serve the `Roboto` family from it too | The resolver is process-wide; the table font is Roboto |
| Expect CJK or Arabic glyphs | Use the Gotenberg HTML path with suitable web fonts | Embedded Roboto covers Latin, Greek and Cyrillic only |
| Build invoices from a table definition | Render HTML and use `IHtmlToPdfConverter` | This package produces one title and one table |

## Design decisions

**Why PDFsharp/MigraDoc?** Both are MIT-licensed. QuestPDF is revenue-gated and iText7 is AGPL, so neither can ship in
the kernel.

**Why embed a font?** Containers usually have no fonts installed, and PDFsharp does not enumerate them anyway; an
embedded font makes the output byte-for-byte portable.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Reporting domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Reporting/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
