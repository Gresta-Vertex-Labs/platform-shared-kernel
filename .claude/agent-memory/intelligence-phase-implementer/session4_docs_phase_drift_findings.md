---
name: session4_docs_phase_drift_findings
description: 10.Intelligence SK.10.Docs session — NuGet metadata/XML-doc packaging pass on Abstractions/Qdrant/SemanticKernel, and the DO-05 drift-check technique that catches stale doc comments a Core-phase correction never propagated back into the shipped code.
type: project
---

## The drift check must read the shipped code's OWN doc comments, not just CLAUDE.md's prose

When a Core-phase or Tests-phase session corrects a documented assumption (e.g. "Qdrant needs a
sentinel point" → "Qdrant has real collection metadata"), the correction reliably lands in
`CLAUDE.md`'s Technology Stack/changelog prose but does **not** reliably get back-ported into the
XML doc `<remarks>` blocks on the actual interface/type that made the original (wrong) claim. Found
two live instances of this in the same session:

1. `VectorFilter.cs`'s class-level `<remarks>` still said "Exists is `IsNull`-negation on Qdrant" —
   even though `QdrantFilterCompiler.cs` (Qdrant package) already had the corrected `IsEmpty`-negation
   doc, and `CLAUDE.md`'s Filter AST NOTE was already corrected. Only the *abstractions-level* type's
   own doc comment was stale.
2. `IVectorCollectionProvisioner.cs`'s XML doc still said Qdrant persists `Fingerprint` "via a reserved
   sentinel point's payload" — even though `QdrantCollectionProvisioner.cs`'s actual implementation and
   `CLAUDE.md`'s Technology Stack row were already corrected to describe the genuine collection-level
   `metadata` map (requires Qdrant server v1.16.0+).

**How to apply:** a Docs-phase drift check is not complete by diffing `CLAUDE.md` against itself, or
even against the *provider* package's code (which had already been fixed). It must specifically
re-read every `.Abstractions`-level type's own `<remarks>`/`<summary>` for any provider-specific claim
that a downstream Core-phase implementation session may have disproven — those claims get committed
once at Design/ratification time and are easy to forget when only the provider-side fix lands. Grep
for provider names (`Qdrant`, `Milvus`) or mechanism nouns ("sentinel", "IsNull") inside the
`.Abstractions` project specifically, cross-checked against the corresponding provider's actual
implementation and against `CLAUDE.md`'s own (usually-correct-by-then) prose.

## Enabling GenerateDocumentationFile+TreatWarningsAsErrors on an already-"fully-XML-doc'd" package still surfaces real defects

Every file in `SharedKernel.AI.Abstractions`/`.Qdrant`/`.SemanticKernel` already had XML doc comments
on every public member (written during Core phase per this domain's own "all public APIs carry XML
docs" rule) — yet enabling `GenerateDocumentationFile`+`TreatWarningsAsErrors` at Docs phase still
surfaced 8 real errors across the three packages:
- CS1573 (missing `<param>` tag when *some* params are documented) — two interface methods
  (`ISemanticKernel.CompleteStreamingAsync`, `IVectorCollection.ScrollAsync`) had only their
  `cancellationToken` param documented (because that's the one carrying the
  `[EnumeratorCancellation]`-omission explanation) and the compiler flags the omission of ANY other
  param's tag once ANY param tag exists on the member.
- CS1574 (unresolved `cref`) — `IntelligenceWellKnown.cs` referenced `Models.VectorQuery` but
  `VectorQuery` actually lives in the `Abstractions` sub-namespace, not `Models`; `VectorFieldKind.cs`
  referenced a bare `Kind` that doesn't exist as a member on the enum itself (the intended target was
  `VectorFieldDefinition.Kind`).
- CS1734 (`<paramref>` with no matching parameter) — a class-level `<remarks>` block in
  `QdrantVectorCollection.cs` used `<paramref name="tenantScope"/>` to refer to a concept shared across
  multiple methods, which is illegal outside a per-member doc comment; fix is `<c>tenantScope</c>`
  instead.

**How to apply:** never assume "every member already has an XML comment" means the Docs-phase
`GenerateDocumentationFile`+`TreatWarningsAsErrors` flip will be a no-op build. Budget time to actually
run the build and fix what it finds — this is the entire point of enabling it at Docs phase rather than
leaving it off indefinitely.

## Sanctioned CS8509/CS8524 downgrade survives TreatWarningsAsErrors correctly

Confirmed (not just assumed) that `SharedKernel.AI.Qdrant`'s pre-existing
`<WarningsNotAsErrors>$(WarningsNotAsErrors);CS8509;CS8524</WarningsNotAsErrors>` entry, added at
Core-phase for the `QdrantFilterCompiler` closed-hierarchy switch, correctly continues to appear as a
**visible warning** (not a build error) even after Docs phase adds `TreatWarningsAsErrors=true` to the
same `.csproj` — this is the entire design intent of `WarningsNotAsErrors` (narrowly overriding
`TreatWarningsAsErrors` for two specific diagnostic IDs while leaving every other warning fatal) and it
held up exactly as designed on the first build attempt, no adjustment needed.

## Pack-time NU5039 sanity check is cheap and worth doing even though it's formally a Published-phase task

Ran a scratch `dotnet pack` (all three packages → a `/tmp` scratch dir, deleted after) immediately after
adding the `PackageReadmeFile`/`<None Include="README.md">` pair, rather than waiting for the formal
`SK.10.Published` P-01/P-02 tasks to discover an `NU5039` miss. Six outputs (`.nupkg`+`.snupkg` × 3)
produced cleanly with zero `NU5039`/`NU5128`. This is the exact lesson the phase brief calls out
(`08.Storage` missed the pairing at Docs and paid for it at Published) — a 30-second scratch-pack
check at Docs-phase implementation time is cheap insurance against repeating that miss, even though the
formal verification still belongs to the Published phase.

## State-map root catch-up remains the recurring pattern for this domain

Third consecutive `10.Intelligence` implementer session (after Scaffold/Core and Tests) where the root
`state-map.md`'s Domain Summary Board / Blocked rows had drifted from the sub-map's actual current
phase name, and needed a manual catch-up update even though the sub-map's own promotion condition never
fired (Milvus keeps every phase `⚑`, so no phase key ever reaches the all-`●` state that would trigger
automatic root propagation). **How to apply**: for this domain specifically, always do a manual
Root-mode catch-up (update the Current Phase name, State symbol stays `⚑`, refresh the Summary
sentences) at the end of every session, regardless of whether `state-map-phase`'s sub-map-mode
promotion condition fires — it never will until Milvus unblocks or DO-03/T-05/T-06/T-07/C-06–C-08's
block is otherwise resolved.
