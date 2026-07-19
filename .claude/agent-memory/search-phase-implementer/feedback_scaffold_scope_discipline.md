---
name: feedback_scaffold_scope_discipline
description: How to determine exactly what belongs in a Scaffold-phase session vs. later phases in this repo's WO-driven task plans
type: feedback
---

When a phase spec's task table (in a domain's `state-map.md`) enumerates concrete deliverables (S-01, S-02, ...), treat that table as the literal scope boundary — not the domain's `CLAUDE.md` Technology Stack / Implementation Rules prose, which describes the domain's *eventual* full shape across all six phases (Design/Scaffold/Core/Tests/Docs/Published), not what's due *this* phase.

**Why:** `09.Search/CLAUDE.md`'s Technology Stack table lists a full NuGet packaging metadata block (`PackageId`, `GenerateDocumentationFile`, `TreatWarningsAsErrors`, README pack pair, etc.) as part of "XML doc enforcement / NuGet packaging" for the domain overall. It would be easy to read that and add it during Scaffold. But the Scaffold phase's own task table (S-01/S-05/S-08) only asks for `ProjectReference`/`PackageReference` wiring and nested-test-exclusion items — nothing about packaging metadata. Cross-checking against `09.Search/state-map.md`'s Docs-phase table confirmed DO-04 owns the full metadata block explicitly. Adding it early would have been "more than the phase specifies," a documented hard rule for phase-implementer agents in this repo ("implement only what the phase specifies — nothing more, nothing less").

**How to apply:** Before writing any file in a phase-implementer session, grep the target domain's `state-map.md` for every later phase's task list (search for `## Phase:` headers) and confirm a candidate addition isn't already owned by a specific later task ID. If it is, skip it now even if the domain brain's general description makes it look due. This generalizes beyond NuGet metadata — it applies to XML docs (`GenerateDocumentationFile`/`TreatWarningsAsErrors`, also Docs-phase), README files (also Docs-phase), and NuGet packing itself (Published-phase).

See also [[project_09search_scaffold]].
