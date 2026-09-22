# SharedKernel.Reporting.Pdf

[PdfSharp](https://docs.pdfsharp.net/)/[MigraDoc](https://docs.pdfsharp.net/MigraDoc/)-backed PDF report/data export for Platform.SharedKernel microservices, **scoped to simple tabular/statement layouts only**. `PdfReportExporter<TRow>` builds a single MigraDoc table row-by-row from the streamed `IAsyncEnumerable<TRow>` source and renders it via `PdfDocumentRenderer`; MigraDoc's own table renderer paginates automatically across page breaks.

See [`SharedKernel.Reporting.Abstractions`](../SharedKernel.Reporting.Abstractions/README.md) for the shared contract and column model.

## SCOPE BOUNDARY — read this before reaching for `.Pdf`

**MULTI-SECTION DOCUMENTS, IMAGES, CHARTS, AND HEADERS/FOOTERS BEYOND ONE OPTIONAL TITLE ARE EXPLICITLY OUT OF SCOPE.** This is not a general-purpose PDF authoring library — it supports exactly one flat statement-style table (an optional title, a header row, and one row per exported `TRow`). Nothing else.

## IMPORTANT — memory model

Same underlying constraint as [`SharedKernel.Reporting.Spreadsheet`](../SharedKernel.Reporting.Spreadsheet/README.md#important--memory-model): **MigraDoc's `Document`/`Table` object model materializes fully in memory before `PdfDocumentRenderer` runs, and PdfSharp has no incremental page-flush API either.** This is not separately re-litigated as a defect because this provider's scope (above) already bounds it — a simple tabular/statement layout is not the bulk-export use case `SharedKernel.Reporting.Csv` exists for. If you actually need a huge exported table, use [`.Csv`](../SharedKernel.Reporting.Csv/README.md) (or `.Spreadsheet`, with its own documented caveat) instead of `.Pdf`.

## DI quick start

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSharedKernelStorage().AddS3(builder.Configuration).AddStore("exports"); // any SharedKernel.Storage provider
builder.Services.AddPdfReportExporter<Invoice>(builder.Configuration);

var host = builder.Build();
await host.StartAsync();

var exporter = host.Services.GetRequiredService<IPdfReportExporter<Invoice>>();
```

## Configuration

```json
{
  "SharedKernel": {
    "Reporting": {
      "Pdf": {
        "PageFormat": "A4",
        "Orientation": "Portrait"
      }
    }
  }
}
```

`PageFormat`/`Orientation` bind to `MigraDoc.DocumentObjectModel.PageFormat`/`Orientation` — an unrecognized value fails host startup via `ValidateOnStart()`, not first use.

## Fonts

Rendered text always uses the **Roboto** font family, embedded directly in this package (`Fonts/Roboto-Regular.ttf`, `Fonts/Roboto-Bold.ttf`) — there is no configuration option to change it. PdfSharp 6.x performs no implicit OS font enumeration on any platform, so a font resolver reading a host-installed font (e.g. from `C:\Windows\Fonts`) would behave differently — or fail outright — between the Windows box this was authored on and the Linux containers this platform's services actually deploy to. Embedding one font eliminates that entire class of environment drift.

## Licence

Both **`PDFsharp`** and **`PDFsharp-MigraDoc`** are unconditional **MIT** — ratified in [`20.Reporting/CLAUDE.md`](../CLAUDE.md)'s licensing table, verified directly against the published nuspecs before pinning. **Roboto** (the embedded font) is **Apache License 2.0** — permissive and redistribution-friendly; the full licence text ships in this package at `Fonts/LICENSE.txt`.

QuestPDF (revenue-gated Community licence above a threshold) and iText7 (AGPL, copyleft) were evaluated and explicitly declined for this platform; never substitute either.

### A note on package IDs

This domain's original design named three separate NuGet package IDs (`PdfSharp` + `MigraDoc.DocumentObjectModel` + `MigraDoc.Rendering`), reflecting an older PDFsharp/MigraDoc package split. That three-way split no longer exists on nuget.org — verified directly against the live package index before implementation. The current, actively-maintained PDFsharp team packages are exactly two IDs: `PDFsharp` (core PDF primitives) and `PDFsharp-MigraDoc` (bundling the `MigraDoc.DocumentObjectModel`/`MigraDoc.Rendering` namespaces this design already named, plus a `PDFsharp` dependency pinned to the identical version) — both MIT, both from the same upstream project. This is a corrected package-ID mapping onto the same licence-ratified technology, never a substitution of a different library.
