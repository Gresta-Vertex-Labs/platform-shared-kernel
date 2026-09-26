# SharedKernel.Reporting.Spreadsheet

Excel (`.xlsx`) exports for [`SharedKernel.Reporting`](../SharedKernel.Reporting.Abstractions/README.md), streamed in
**constant memory**: rows are written to the output as they arrive, so a million-row workbook needs no more memory
than a small one. Built on [SpreadCheetah](https://github.com/sveinungf/spreadcheetah) (MIT, no dependencies).

```xml
<PackageReference Include="SharedKernel.Reporting.Spreadsheet" />
```

Versions come from the consumer's single `SharedKernelVersion`. **Tier: Adapter.**

## Register

```csharp
builder.Services.AddSharedKernelReporting().AddSpreadsheet(builder.Configuration);
```

One call serves every row type: inject `ISpreadsheetReportExporter<Order>` or get the `"xlsx"` exporter from
`IReportExporterFactory`.

## Options — `SharedKernel:Reporting:Spreadsheet`

| Setting | Default | |
|---|---|---|
| `DefaultSheetName` | `Report` | Used when the report has no title (≤ 31 characters, none of `\ / ? * [ ] :`). |
| `BoldHeaderRow` | `true` | |
| `FreezeHeaderRow` | `true` | The header stays visible while scrolling. |
| `AutoFilter` | `true` | Filter buttons on the header. |
| `MaxRows` | `1048575` | More rows fail with `reporting.row_limit_exceeded` and store nothing. Cannot exceed Excel's limit. |

## Typed cells

Values keep their type, so Excel can sort, filter and sum them:

| Value | Cell | Number format |
|---|---|---|
| `int`, `long`, `decimal`, `double`, … | number | the column's `Format`, translated (below), else General |
| `DateTime`, `DateTimeOffset` (its clock time) | date | `Format` translated, else `yyyy-mm-dd hh:mm:ss` |
| `DateOnly` | date | `Format` translated, else `yyyy-mm-dd` |
| `TimeOnly` | time | `hh:mm:ss` |
| `TimeSpan` | duration | `[h]:mm:ss` |
| `bool` | boolean | |
| anything else (`string`, `Guid`, enums…) | text | formatted by `Format` and the report's culture |

A column with a formatter function is always text. Text is never a formula, so `=HYPERLINK(…)` in a value is shown,
not run.

.NET formats are translated: `N2` → `#,##0.00`, `F3` → `0.000`, `D5` → `00000`, `P1` → `0.0%`, `E2` → `0.00E+00`,
`C2` → the culture's currency symbol and position; a custom numeric format (`#,##0.00;(#,##0.00)`) passes through.
Date patterns are converted (`dd.MM.yyyy HH:mm` → `dd.mm.yyyy hh:mm`); standard ones (`d`, `G`, …) use the report
culture's patterns.

## Layout

The title names the sheet (invalid characters replaced) and becomes the document title. Column widths follow the header
length times the column's `RelativeWidth`. An explicit `Left`/`Center`/`Right` alignment is applied; `Auto` leaves
Excel's natural alignment (numbers right).
