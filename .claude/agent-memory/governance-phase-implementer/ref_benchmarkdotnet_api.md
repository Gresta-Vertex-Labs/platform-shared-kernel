---
name: ref_benchmarkdotnet_api
description: BenchmarkDotNet 0.15.8 API decisions — Job.Short removed, explicit short-run form
metadata:
  type: reference
---

## Version

`BenchmarkDotNet` **0.15.8** pinned in `SharedKernel.Benchmarks.csproj`.

## Job.Short does not exist

`Job.Short` was removed in BenchmarkDotNet 0.15.x. Do NOT use it.

## Correct short-run job construction

```csharp
AddJob(
    Job.Default
        .WithWarmupCount(1)
        .WithIterationCount(3)
        .WithId("ShortRun")
);
```

## Exporter

Use `MarkdownExporter.GitHub` (not `MarkdownExporter.Default`) for deterministic CI artifact comparison.

## HideColumns

Use `HideColumns(Column.RatioSD, Column.Median, Column.Gen0, Column.Gen1, Column.Gen2)` in `ManualConfig` constructor for deterministic column order.

## HardwareCounters

Do NOT add HardwareCounters — unstable in CI containers. Simply omit `AddHardwareCounters(...)`.
