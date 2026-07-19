---
name: feedback_docs_phase_pattern
description: The repo-wide Docs-phase convention (GenerateDocumentationFile+TreatWarningsAsErrors+NuGet metadata bundled together) — apply it directly, don't re-derive from scratch
type: feedback
---

When a Docs phase says "XML doc comments on every public type/member... zero missing-doc warnings," the
established repo-wide pattern (already proven in `05.Application`, `06.Persistence`, `07.Messaging`, and
now `08.Storage`, 2026-07-17) is to add to **every production `.csproj`** (never the `.Tests` csproj) in
one PropertyGroup addition:

```xml
<PackageId>SharedKernel.X</PackageId>
<Version>1.0.0</Version>
<PackageVersion>1.0.0</PackageVersion>
<Authors>Gresta-Vertex-Labs</Authors>
<Company>Gresta-Vertex-Labs</Company>
<Product>Platform.SharedKernel</Product>
<Description>...</Description>
<PackageTags>...</PackageTags>
<PackageLicenseExpression>MIT</PackageLicenseExpression>
<RepositoryType>git</RepositoryType>
<RepositoryUrl>https://github.com/Gresta-Vertex-Labs/platform-shared-kernel</RepositoryUrl>
<PackageProjectUrl>https://github.com/Gresta-Vertex-Labs/platform-shared-kernel</PackageProjectUrl>
<Copyright>Copyright © 2026 Gresta-Vertex-Labs</Copyright>
<GenerateDocumentationFile>true</GenerateDocumentationFile>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
```

**Why bundled together, not just the two doc flags:** the full NuGet metadata technically belongs to the
Published phase's P-01/P-02 tasks, but adding it during Docs is harmless and means Published finds it
already done — this is deliberate precedent, not scope creep. See
`.claude/agent-memory/application-phase-implementer/docs_phase_xml_doc_enforcement.md` and
`.claude/agent-memory/presentation-phase-implementer/feedback_docs_phase_generatedocfile.md` for the two
prior sessions that established this.

**Critical gotcha (confirmed twice elsewhere, absent in 08.Storage — see below):**
`GenerateDocumentationFile` MUST actually be set for the compiler to validate `///` comments at all —
CS1591 (missing doc) and CS1574/CS1580 (unresolved `cref`) are silently skipped without it, even when
prose-complete comments already exist from Core phase. Never accept "the comments look complete" as
satisfying a Docs-phase acceptance criterion — turn the flag on and rebuild first.

**How to apply:** at the start of any 08.Storage (or other domain) Docs phase, go straight to adding this
block to all production `.csproj` files, then `dotnet build -c Release` each one. In the 08.Storage
2026-07-17 session this surfaced **zero** warnings across all three packages on the first build — the
Core-phase XML docs were genuinely complete, unlike `05.Application` (1 cref fix + 7 `<inheritdoc/>`
additions needed) and `14.Presentation` (4 cref fixes needed). Don't assume zero-warnings will always be
the outcome — always actually run the build and read the output before declaring the task done.

**Gap discovered at Published-phase `dotnet pack` time (2026-07-18), not caught by `dotnet build`:** the
metadata block above is missing one pair that `dotnet build` never surfaces a warning for —
`<PackageReadmeFile>README.md</PackageReadmeFile>` plus `<None Include="README.md" Pack="true"
PackagePath="\" />`. Without both lines, `dotnet pack` (not `build`) emits a `NU5039` "missing a readme"
warning even when the package's `README.md` exists on disk and is content-complete (08.Storage wrote all
three READMEs during Docs but never wired them into the pack). `01.Core/SharedKernel.Configuration.csproj`
already had this pair — it should have been copied into the Docs-phase block template above from the
start. **How to apply:** add `PackageReadmeFile` + the `None Include` line to the metadata block at Docs
time, alongside everything else — don't wait for Published's `dotnet pack` to catch it. If a future
session is doing a Published-phase P-01/P-02 task and finds `dotnet pack` prints `NU5039`, this is the
fix, and it means a prior Docs-phase session (in any domain, not just 08.Storage) skipped it.
