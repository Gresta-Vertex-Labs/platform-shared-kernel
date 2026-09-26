# SharedKernel.Reporting.Csv

RFC 4180 CSV exports for [`SharedKernel.Reporting`](../SharedKernel.Reporting.Abstractions/README.md). Rows are
written straight to the destination one at a time, so memory stays **constant** — a million-row export costs no more
than a ten-row one. No third-party dependencies.

```xml
<PackageReference Include="SharedKernel.Reporting.Csv" />
```

Versions come from the consumer's single `SharedKernelVersion`. **Tier: Adapter** — referenced by the Infrastructure or
Api project; application code uses `IReportExporterFactory` or `ICsvReportExporter<TRow>`.

## Register

```csharp
builder.Services.AddSharedKernelReporting().AddCsv(builder.Configuration);
```

One call serves every row type: inject `ICsvReportExporter<Order>`, `ICsvReportExporter<Invoice>`, … or get the `"csv"`
exporter from `IReportExporterFactory`. Options are validated when the host starts.

## Options — `SharedKernel:Reporting:Csv`

| Setting | Default | |
|---|---|---|
| `Delimiter` | `,` | `;` for Excel in locales with a decimal comma (tr-TR, de-DE, fr-FR…). Not `"`, CR or LF. |
| `IncludeUtf8Bom` | `true` | Excel reads non-ASCII text correctly only with the BOM. |
| `IncludeHeaderRow` | `true` | The first line holds the column headers. |
| `EscapeFormulas` | `true` | CSV-injection guard, below. |

## Output

- RFC 4180: a field is quoted when it contains the delimiter, `"`, CR or LF; `"` is doubled. Lines end with CRLF.
- Values are formatted by the column's `Format` and the report's `Culture` — `1.234,50` under `de-DE` with `N2`; with the
  default `,` delimiter that field is quoted, with `;` it is not.
- `null` is an empty field. The title and column widths do not apply to CSV.

## CSV injection (CWE-1236)

A spreadsheet opening a CSV runs any field starting with `=`, `+`, `-` or `@` as a formula — `=HYPERLINK(…)` or a DDE
payload in a customer name becomes code on the reader's machine. With `EscapeFormulas` (the default), text starting
with `=`, `+`, `-`, `@`, a tab or a carriage return is prefixed with `'`, so it shows as text. **Numbers are never
prefixed**: `-5.25` from a `decimal` stays a number. Turn it off only for files no person opens in a spreadsheet.
