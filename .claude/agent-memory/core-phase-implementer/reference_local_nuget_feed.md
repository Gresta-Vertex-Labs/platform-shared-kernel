---
name: reference-local-nuget-feed
description: Where "pack and publish to feed" actually resolves to in this repo — root ./nupkgs/ local feed + NuGet.Config source mapping
metadata:
  type: reference
---

When a phase says "pack and publish `SharedKernel.X` to feed," this repo has a concrete, repo-wide convention — it is not aspirational:

- Root `NuGet.Config` defines a `local-shared-kernel` package source pointing at `./nupkgs` (relative to repo root), with `packageSourceMapping` routing every `SharedKernel.*` package id to that local feed and everything else to nuget.org.
- `dotnet pack {csproj} --configuration Release --output ./nupkgs` from the repo root is the actual "publish" step — there is no real remote feed.
- Consumer-verification projects (e.g. `01.Core/SharedKernel.Consumer.Tests/`) reference the packed `SharedKernel.*` packages via `<PackageReference>` (not project references) so `dotnet test` on that project proves the NuGet dependency graph resolves correctly, not just that the source compiles.
- This same `./nupkgs` folder is shared across all numbered domains (02.Caching, 03.Domain, 06.Persistence, etc. all pack into it too) — it is the one true local feed for the whole mono-repo, not per-domain.
- `GenerateDocumentationFile` is only added to a package's `.csproj` as part of its NuGet packaging metadata (P-0x "Published" tasks), not during the Core implementation phase — so a clean build during Core/Tests phases does NOT prove XML docs are warning-free. Only verify zero CS1591/CS0419 doc warnings after adding packaging metadata with this flag set.

**Why:** Discovered while implementing WO-033's P-209 (Cryptography Docs+Published closeout) — confirmed by finding `SharedKernel.Guards.1.0.0.nupkg` already in `./nupkgs/` and reverse-engineering the convention from `SharedKernel.Guards.csproj`'s packaging metadata block and `01.Core/SharedKernel.Consumer.Tests/SharedKernel.Consumer.Tests.csproj`.

**How to apply:** For any future "pack and publish" or "consumer dependency-graph verification" task in any domain, look for `{domain}/consumer-verify/` or `{domain}/SharedKernel.Consumer.Tests/` first — the pattern is already established per-domain. Pack into `./nupkgs` at repo root, never a domain-local folder, unless a domain's own state-map says otherwise (some domains, e.g. `02.Caching`, `06.Persistence`, have their own `nupkgs/`/`nupkg/` folders too — check before assuming root-only).
