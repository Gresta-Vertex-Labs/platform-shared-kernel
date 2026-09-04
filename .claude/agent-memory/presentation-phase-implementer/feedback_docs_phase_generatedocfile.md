---
name: docs-phase-generatedocfile
description: GenerateDocumentationFile is now centralized repo-wide in Directory.Build.targets — no longer a per-project Docs-phase gotcha, but XML-doc/cref completeness is still validated on every build, every phase
type: feedback
---

**UPDATED 2026-09-04 (WO-074/P-468 session):** As of some point after this memory was first
written, `devops-lead` centralized `<GenerateDocumentationFile>` in the root
`Directory.Build.targets` (`PropertyGroup Condition="'$(IsTestProject)' != 'true' and
'$(OutputType)' != 'Exe' and '$(GenerateDocumentationFile)' == ''"` → sets it `true`). Verified by
reading `Directory.Build.targets` directly and by observing that a brand-new package
(`SharedKernel.Presentation.Grpc`, scaffolded from scratch this session with no explicit
`<GenerateDocumentationFile>` in its own `.csproj`) built 0 warnings/0 errors on the very first
`dotnet build` — CS1591/CS1574 validation was active from the start, no separate "Docs phase" flag
flip was needed or possible to demonstrate.

**Old guidance (superseded, kept for historical context):** In WO-031's `14.Presentation` Docs
phase (before centralization), the flag had to be added per-project and turning it on for the
first time surfaced 4 real unresolved-`cref` warnings that had been sitting invisible since Core.

**How to apply now:** Do not add `<GenerateDocumentationFile>` to a new `.csproj` — it is already
on by default for every non-test, non-`Exe` project via `Directory.Build.targets`. This means XML
doc completeness (CS1591) and `cref` resolution (CS1574/CS1580) are validated on the FIRST build of
new production code, not deferred to a later phase — write complete `///` docs from the start,
since "zero warnings" is a Core-phase expectation now, not a Docs-phase discovery. If a future
session sees CS1591/CS1574 warnings pass silently, first check whether `IsTestProject`/`OutputType`
detection in `Directory.Build.props` is misclassifying the project, rather than assuming the flag
needs adding by hand.
