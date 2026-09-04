# SharedKernel.Reporting.Spreadsheet

[ClosedXML](https://github.com/ClosedXML/ClosedXML)-backed `.xlsx` report/data export for Platform.SharedKernel microservices. `SpreadsheetReportExporter<TRow>` writes the header row and one row per streamed `TRow` directly into worksheet cells as the `IAsyncEnumerable<TRow>` source is enumerated — it never buffers rows into an intermediate `List<TRow>` first.

See [`SharedKernel.Reporting.Abstractions`](../SharedKernel.Reporting.Abstractions/README.md) for the shared contract and column model.

## IMPORTANT — memory model

**CLOSEDXML ITSELF EXPOSES NO INCREMENTAL/STREAMING WRITE PATH.** `XLWorkbook.SaveAs` builds the complete in-memory workbook object graph and only serializes it to the destination stream at `SaveAs` time — a documented real-world case saw a 32 MB `.xlsx` output consume 1+ GB of process memory ([`ClosedXML/ClosedXML#1180`](https://github.com/ClosedXML/ClosedXML/issues/1180)).

**This provider is therefore NOT memory-bounded**, despite never materializing `TRow` into a `List<TRow>` itself — the workbook's own cell object graph is O(rows × columns) regardless. This is a **permanent, accepted characteristic of the ClosedXML dependency**, not a defect awaiting a fix — no evaluated MIT-licensed alternative offers true `.xlsx` streaming at an acceptable ergonomic/implementation-risk cost.

**For a genuinely large row count, use [`SharedKernel.Reporting.Csv`](../SharedKernel.Reporting.Csv/README.md) instead.**

`workbook.SaveAs(stream)` is also a synchronous, blocking call — ClosedXML has no async `SaveAs` overload. It is called directly inside this exporter's async method body; accepted because report generation is expected to run inside a background/worker context (a `19.Scheduling` job or `17.Workflows` activity), never on a request thread.

## DI quick start

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSharedKernelS3Storage(builder.Configuration);
builder.Services.AddSpreadsheetReportExporter<Invoice>(builder.Configuration);

var host = builder.Build();
await host.StartAsync();

var exporter = host.Services.GetRequiredService<ISpreadsheetReportExporter<Invoice>>();
```

## Configuration

```json
{
  "SharedKernel": {
    "Reporting": {
      "Spreadsheet": {
        "DefaultSheetName": "Report",
        "BoldHeaderRow": true
      }
    }
  }
}
```

`DefaultSheetName` is used only when `ReportDefinition<TRow>.Title` is `null`. Sheet names are sanitized (invalid Excel characters replaced, truncated to 31 characters) regardless of source.

## Licence

ClosedXML is unconditional **MIT** — ratified in [`20.Reporting/CLAUDE.md`](../CLAUDE.md)'s licensing table, verified directly against the published nuspec before pinning. EPPlus (PolyForm Noncommercial) was evaluated and explicitly declined for this platform; never substitute it.
