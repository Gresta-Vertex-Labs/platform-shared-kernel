---
name: feedback_build_tooling_gotchas
description: Two build/tooling gotchas discovered during WO-044 Core-phase implementation -- PowerShell UTF-8 mojibake on bulk text edits, and TreatWarningsAsErrors is not actually wired into this repo despite root CLAUDE.md's framing.
type: feedback
---

**Never use Windows PowerShell 5.1's `Get-Content -Raw`/`Set-Content -Encoding utf8` for bulk regex find/replace on a UTF-8 `.cs` file, especially one with em-dashes or other non-ASCII characters.**

**Why**: `Get-Content -Raw` in Windows PowerShell 5.1 does not reliably read UTF-8-with-BOM as UTF-8 by default in all contexts; round-tripping through `Set-Content -Encoding utf8` after a regex substitution corrupted every em-dash (`—`) into a 3-character mojibake sequence (`â€”`) across an entire file during WO-044's `InMemorySearchIndex.cs` qualification pass. The same blind regex also incorrectly matched `Result<` as a substring of `Result</c>` inside XML doc-comment closing tags, injecting a code-qualification string into prose text.

**How to apply**: For qualifying an ambiguous generic type (e.g. `Result<T>` colliding with `GreenDonut.Result<T>` -- see `feedback_greendonut_result_ambiguity`) across many call sites in one file, either (a) write the file correctly from the start via the `Write` tool with full qualification baked into the code from the first draft, or (b) use the `Edit` tool for per-call-site replacement (it handles UTF-8 correctly and its exact-match requirement naturally avoids matching inside doc comments by accident). If a file DOES get corrupted by a bad PowerShell round-trip, fix it with `perl -CSD -i -pe 's/\x{00e2}\x{20ac}\x{201d}/--/g'` (perl is available in this environment; `python3` is not) rather than trying another PowerShell pass.

---

**`TreatWarningsAsErrors` is NOT actually wired into any `.csproj` in this repo as of 2026-07-20, despite root `CLAUDE.md`'s aspirational "the platform runs TreatWarningsAsErrors" framing (referenced by an earlier session's own memory note about a Testcontainers CS0618 bump).**

**Why this matters**: confirmed directly -- no `Directory.Build.props` exists anywhere in the repo (checked at root and every domain root), and `16.Testing/SharedKernel.Testing.csproj` has no `TreatWarningsAsErrors`/`WarningsAsErrors` property. A `dotnet build` with genuine warnings (e.g. `CS8509` "switch not exhaustive" on the closed-8-node `SearchFilter` switch expression, copied verbatim from `09.Search`'s own real `MeilisearchFilterCompiler`/`ElasticSearchFilterCompiler`) still reports `Build succeeded` with 0 errors. Independently reproduced the SAME `CS8509` on a clean rebuild of `SharedKernel.Search.Meilisearch` itself (`rm -rf obj bin && dotnet build`) -- it's a pre-existing, unaddressed warning in the SHIPPED production code, just hidden by incremental-build caching (a `dotnet build` with no source changes doesn't re-emit warnings for untouched files).

**How to apply**: Do not assume a `CS0618`/`CS8509`-class warning will fail the build just because a prior session's memory note said so for a *different* warning in a *different* pass -- that prior note was about following the design intent (a genuine behavior-risk from an obsoleted constructor), not about TreatWarningsAsErrors actually being enforced. When you hit a warning that seems structurally unavoidable (e.g. Roslyn can't prove exhaustiveness over an abstract-class hierarchy even with every known subtype covered and no discard arm), check whether the REAL production code this fake/type mirrors has the identical warning before treating it as your own defect to fix -- a `rm -rf obj bin && dotnet build` clean rebuild of the reference file is the reliable way to check, since incremental builds hide warnings on unchanged files.
