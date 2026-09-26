# SharedKernel.Reporting.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**`InMemoryReportExporter<TRow>` implements `SharedKernel.Reporting.Abstractions`' `IReportExporter<TRow>` in
memory, so export code is tested without a CSV, spreadsheet or PDF provider.** It consumes the row stream as a real
exporter does (one pass, never re-enumerated), records the rows and the definition, and delivers the result to the
named storage store in the `ReportDestination`.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.Reporting.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespace: `SharedKernel.Testing.Reporting`.

## Contents

| Member | What it does |
| --- | --- |
| `ExportAsync(rows, definition, destination, ct)` | Streams the rows, records them, and writes the report to the destination store |
| `ExportToStreamAsync(rows, definition, stream, ct)` | The same into a caller-supplied stream |
| `LastRows`, `LastDefinition`, `LastDestination` | What the most recent export received |
| `ShouldHaveExported(rows => ...)` | Asserts an export happened, optionally matching its rows |
| `SimulateFailure`, `Reset()` | Failure path and cleanup |

The constructor takes an optional `IClock` (use `FakeClock`) for the timestamps the exporter stamps. There is no
`Add*` helper: register the instance as `IReportExporter<TRow>` yourself.

## Example

```csharp
var exporter = new InMemoryReportExporter<InvoiceRow>(new FakeClock());
var storage = InMemoryStorage.CreateFactory(new InMemoryFileStorage("reports"));
var job = new MonthlyInvoiceExport(exporter, repository);

await job.RunAsync(month, ct);

exporter.ShouldHaveExported(rows => rows.Count == 3);
exporter.LastDestination!.Store.Should().Be("reports");
```

## Related packages

- References `SharedKernel.Reporting.Abstractions`, `SharedKernel.Storage.Abstractions` and `SharedKernel.Testing`
  (`FakeClock`).
- [`SharedKernel.Storage.Testing`](../SharedKernel.Storage.Testing/README.md) — the in-memory store the report is
  delivered to.
