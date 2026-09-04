---
name: technical_memory_boundedness_test_technique
description: How to structurally prove a streaming code path is O(1)-memory in a unit test, without Testcontainers or process-level profiling
type: reference
---

Used to prove `CsvReportExporter<TRow>` streams genuinely constant-memory (20.Reporting T-04, the
load-bearing test the whole domain's memory-boundedness claim rests on).

**The technique:** run the same streaming operation at two very different row counts (e.g. 20,000
and 400,000 — a 20x spread), measuring allocated bytes via
`GC.GetTotalAllocatedBytes(precise: true)` around each run (force `GC.Collect()` +
`GC.WaitForPendingFinalizers()` + `GC.Collect()` first to reduce noise), then divide by row count to
get bytes-per-row for each run. Assert the large run's bytes-per-row is NOT roughly proportionally
higher than the small run's (e.g. `largeBytesPerRow.Should().BeLessThan(smallBytesPerRow * 4)` — a
generous bound that still catches real `List<TRow>`/`ToListAsync()` regressions, which would show
something close to the full 20x growth). A single absolute-bytes assertion at one row count is
weaker and more flaky; the *ratio across drastically different N* is what actually distinguishes
"streams" from "buffers."

**Why `GetTotalAllocatedBytes` and not `GetAllocatedBytesForCurrentThread`:** the streaming code
path is `async`/awaits internally and may resume on a different thread-pool thread after an `await`
(e.g. after `Task.Yield()` inside the row generator, or inside `StorageStreamingWriter`'s
`Task.WhenAll` composition). `GetAllocatedBytesForCurrentThread()` only sees allocations on the
calling thread and would silently under-report — or over-report — depending on where execution
actually lands. `GetTotalAllocatedBytes(precise: true)` is process-wide and thread-hop-safe, at the
cost of forcing a real GC pass (acceptable in a test, not something you'd want on a hot path).

**Destination stream matters too:** use a stream that discards or merely counts bytes (`Stream.Null`,
or a tiny custom `Stream` override that increments a counter and returns) rather than a
`MemoryStream` for the *measurement* runs — a growing `MemoryStream` would itself add O(n) allocation
independent of whether the exporter under test streams or buffers, contaminating the signal you're
trying to isolate.

Companion single-pass proof (used for `.Spreadsheet`/`.Pdf`, which cannot honestly claim
memory-boundedness — see `20.Reporting/CLAUDE.md`): a tiny `IAsyncEnumerable<T>` wrapper that throws
`InvalidOperationException` if `GetAsyncEnumerator` is called a second time. If a provider still
produces correct output against that source, it proves the provider never materialized the source
into a `List<T>` and re-iterated it — a structural proof, not a docstring claim.
