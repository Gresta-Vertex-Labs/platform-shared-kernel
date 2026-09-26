# SharedKernel.Reporting.Pdf

Tabular PDF exports for [`SharedKernel.Reporting`](../SharedKernel.Reporting.Abstractions/README.md) — statements,
lists, extracts: an optional title and one table. Built on [PDFsharp/MigraDoc](https://docs.pdfsharp.net/) (MIT).

For free-form documents (invoices, letters, anything with a layout), render HTML with
[`SharedKernel.Reporting.Gotenberg`](../SharedKernel.Reporting.Gotenberg/README.md) instead.

```xml
<PackageReference Include="SharedKernel.Reporting.Pdf" />
```

Versions come from the consumer's single `SharedKernelVersion`. **Tier: Adapter.**

## Register

```csharp
builder.Services.AddSharedKernelReporting().AddPdf(builder.Configuration);
```

One call serves every row type: inject `IPdfReportExporter<Order>` or get the `"pdf"` exporter from
`IReportExporterFactory`.

## Options — `SharedKernel:Reporting:Pdf`

| Setting | Default | |
|---|---|---|
| `PaperSize` | `A4` | `A4`, `A3`, `A5`, `Letter`, `Legal`. |
| `Landscape` | `false` | The usual choice for wide tables. |
| `MarginMillimeters` | `15` | Every side. |
| `FontSize` | `9` | Points (5–24); the title is larger. |
| `ShowPageNumbers` | `true` | "3 / 12" centered at the bottom of every page. |
| `AlternateRowShading` | `true` | Every other row lightly shaded. |
| `MaxRows` | `10000` | More rows fail with `reporting.row_limit_exceeded` and store nothing. |

## Layout

- The title is a heading on the first page and the PDF's document title.
- Columns share the page's usable width by `RelativeWidth` — however many columns, the table never runs off the page.
  Long values wrap inside their cell.
- The header row is bold, shaded and repeated on every page.
- Numbers are right-aligned, everything else left-aligned, unless a column sets its alignment.
- Values are formatted by the column's `Format` and the report's `Culture`.

## Memory

MigraDoc builds the whole document before PDFsharp renders it, so memory and time grow with the row count — that is
what `MaxRows` bounds. A PDF of tens of thousands of rows is not something anyone reads; export those as CSV or Excel,
which stream in constant memory.

## Fonts

PDFsharp uses no installed fonts, so the package embeds **Roboto** (Regular, Bold; Apache License 2.0, text in
`Fonts/LICENSE.txt`) and renders identically on Windows and in Linux containers. Roboto covers Latin (including
Turkish), Greek and Cyrillic. The font resolver is process-wide (`GlobalFontSettings.FontResolver`): if your service
sets its own PDFsharp resolver, it must also serve the `Roboto` family.
