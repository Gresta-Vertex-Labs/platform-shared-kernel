# Memory Index

- [20.Reporting domain shipped WO-077](project_20reporting_wo077_shipped.md) — all four packages implemented/tested/packed 2026-09-04; what's still open and why
- [PdfSharp 6.x / MigraDoc implementation gotchas](technical_pdfsharp_migradoc_gotchas.md) — font resolver required, Save() needs seekable stream, real NuGet package IDs
- [ClosedXML streaming write behavior](technical_closedxml_streaming.md) — no incremental write path, SaveAs(Stream) works fine over a Pipe-backed stream unlike PdfSharp
- [Memory-boundedness test technique](technical_memory_boundedness_test_technique.md) — GC.GetTotalAllocatedBytes(precise:true) ratio test across a 20x row-count increase
- [Verify state-map claims against live source before trusting them](feedback_verify_stale_state_map_claims.md) — a dispatch's "not yet shipped" claim about 01.Core was already false by the time this session ran
