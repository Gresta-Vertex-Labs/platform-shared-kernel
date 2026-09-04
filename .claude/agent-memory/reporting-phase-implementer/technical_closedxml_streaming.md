---
name: technical_closedxml_streaming
description: ClosedXML's real memory/streaming behavior, confirmed by implementation, not just research
type: reference
---

`ClosedXML` (pinned `0.105.1` in `20.Reporting`, MIT) has **no incremental/streaming write path** —
`XLWorkbook.SaveAs(Stream)` builds the complete in-memory workbook object graph and only serializes
at `SaveAs` time. This was already known from research before implementation (see
`20.Reporting/CLAUDE.md`'s D-08), and implementation confirmed it changes nothing about API usage:
`cell.Value = someString` works via `XLCellValue`'s implicit string conversion, storing it as `Text`
type (no auto-number-sniffing of string content — a formatted `"99.5"` string round-trips back out
via `GetString()` exactly as written, not reinterpreted as a number).

**Unlike PdfSharp's `PdfDocument.Save`** (see [[technical_pdfsharp_migradoc_gotchas]]),
`XLWorkbook.SaveAs(Stream)` works completely fine when the destination is a
`System.IO.Pipelines.PipeWriter.AsStream()` stream with no `Position` support — no workaround needed
there. Confirmed by a passing `ExportAsync` (storage-delivered, pipe-based) test.

`workbook.SaveAs(stream)` is synchronous/blocking (no async overload exists) — call it directly
inside an otherwise-async method; accepted because report generation runs in background/worker
contexts, never on a request thread.
