---
name: technical_pdfsharp_migradoc_gotchas
description: Real-world PdfSharp 6.x / PDFsharp-MigraDoc implementation gotchas discovered building SharedKernel.Reporting.Pdf
type: reference
---

Discovered implementing `SharedKernel.Reporting.Pdf` (20.Reporting, WO-077, 2026-09-04). All three
are things a design/plan written from documentation alone would not surface — they only showed up
when the code actually ran.

**1 — The real NuGet package IDs are not what older docs/plans assume.** The pre-6.x split
(`PdfSharp` + `MigraDoc.DocumentObjectModel` + `MigraDoc.Rendering` as three separate package IDs)
no longer exists on nuget.org — verified directly against the flat-container/search API, not
assumed. The current, actively-maintained PDFsharp-team packages are exactly two IDs: **`PDFsharp`**
(core, MIT) and **`PDFsharp-MigraDoc`** (bundles `MigraDoc.DocumentObjectModel`/`MigraDoc.Rendering`,
depends on `PDFsharp` at the identical version, MIT). Both confirmed MIT by reading the published
nuspec directly. Before trusting a design doc's exact package IDs for this library family, re-verify
against `https://api.nuget.org/v3-flatcontainer/{id}/index.json` (lowercase id) — a 404/empty
`versions` array means the ID doesn't exist, don't guess a near-miss casing and move on.

**2 — PdfSharp 6.x performs NO implicit OS font enumeration, on any platform, including Windows.**
Calling `PdfDocumentRenderer.RenderDocument()` without first setting
`PdfSharp.Fonts.GlobalFontSettings.FontResolver` throws
`System.InvalidOperationException: No appropriate font found for family name '...'` even for common
names like "Verdana"/"Courier New" on a real Windows box. You must implement `IFontResolver` and
assign it before the first render. Reading fonts from the host filesystem (e.g.
`C:\Windows\Fonts`) is a trap: it silently behaves differently — or fails outright — between local
Windows dev and the Linux containers most services actually deploy to. **Embed a font as a package
resource instead.** We embedded Roboto Regular+Bold (Apache License 2.0, from
`github.com/googlefonts/roboto-2`, NOT `github.com/google/fonts` — that repo path 404s; the correct
raw URL is
`https://raw.githubusercontent.com/googlefonts/roboto-2/main/src/hinted/Roboto-{Regular,Bold}.ttf`)
as `EmbeddedResource` items and read them via `Assembly.GetManifestResourceStream`.

**3 — `PdfDocument.Save(Stream, bool)` requires a stream with a working `Position` getter**, to
compute xref byte offsets while writing sequentially. A `System.IO.Pipelines.PipeWriter.AsStream()`
stream does NOT support `Position` at all (getter throws `NotSupportedException`), so saving
directly into a pipe-backed destination (the shape `StorageStreamingWriter` hands every provider in
this domain) fails with `System.NotSupportedException: Specified method is not supported.` from deep
inside `PdfWriter.get_Position()`. Fix: render into a local, fully-seekable `MemoryStream` first,
`Position = 0`, then `CopyToAsync` into the real destination. This costs nothing beyond what
MigraDoc/PdfSharp's own in-memory document-model materialization already concedes (see
[[technical_closedxml_streaming]] for the sibling provider's equivalent, unaffected case — ClosedXML's
`SaveAs(Stream)` has no such requirement and works fine directly against a pipe stream).

**How to apply:** if a future PDF-touching phase tries to "optimize" by saving straight to whatever
stream it's given, or swaps the embedded-font approach for a host-font resolver "to save package
size," both will reintroduce a bug that only reproduces at runtime, never at compile time.
