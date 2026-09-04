---
name: project_20reporting_wo077_shipped
description: 20.Reporting (SharedKernel.Reporting.Abstractions/.Csv/.Spreadsheet/.Pdf) fully implemented, tested, and packed 2026-09-04 under WO-077
type: project
---

All four planned packages in `20.Reporting` (P-477–P-480, WO-077) are implemented, tested (54 tests
across four `.Tests` projects, all green), documented (XML docs + READMEs), and pack cleanly via
`dotnet pack`. A `20.Reporting/consumer-verify` harness proves all three providers
(`SharedKernel.Reporting.Csv`/`.Spreadsheet`/`.Pdf`) compose together in one `IHost` and each
round-trips a real export through `IFileStorage`.

**Why:** WO-077 dispatched the domain end to end in one session (60 tasks: Design/Scaffold/Core/
Tests/Docs/Published × 4 packages). The session ran under an explicit shared-file protocol because
other domain implementers were running concurrently against the same repo: root `state-map.md`,
root `CLAUDE.md`, and `Platform.SharedKernel.slnx` were all off-limits.

**What's still open, and why it's deliberate, not incomplete:**
- **S-05** — the four new `.csproj` paths are not yet registered in `Platform.SharedKernel.slnx`.
  Projects build/test fine without solution membership; this needs a session with `.slnx` clearance.
- **P-05** — root `state-map.md` promotion (`/state-map-phase` for `SK.20.Design`/`.Core`/`.Tests`/
  `.Docs`, all genuinely `●`) and closing root Phase Backlog P-477–P-480 were not performed — this
  session could not touch the root file. The sub state-map (`20.Reporting/state-map.md`) records the
  full 58/60 breakdown and is ready for whoever runs that command next.
- `16.Testing`'s in-memory `IReportExporter<TRow>` fake (P-481) is a separate domain's work, not
  blocked by anything here — the real, compiled `IReportExporter<TRow>` contract now exists for it
  to build against.

**How to apply:** if asked to touch `20.Reporting` again, read `20.Reporting/state-map.md` first —
it is current as of 2026-09-04, not the stale "nothing implemented" snapshot an earlier session's
dispatch text may still describe. See [[technical_pdfsharp_migradoc_gotchas]] and
[[technical_closedxml_streaming]] for implementation details worth knowing before touching the PDF
or Spreadsheet providers again.
