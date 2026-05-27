---
name: sk03-published-phase-pattern
description: Pattern for completing the Published phase in 03.Domain — version bump, pack, manifest verification, state-map update
metadata:
  type: project
---

SK.03.Published (P-05) completion pattern established on 2026-05-26:

1. Edit `SharedKernel.Domain.csproj`: bump `<Version>` and `<PackageVersion>` together; update `<PackageReleaseNotes>` with detailed WO change list (XML-escape `<` and `>` as `&lt;` and `&gt;` in release notes).
2. Run `dotnet pack ... -c Release -o nupkgs/` — output is `SharedKernel.Domain.{version}.nupkg` + `.snupkg`.
3. Verify manifest by reading the `.nuspec` inside the `.nupkg` using PowerShell `System.IO.Compression.ZipFile` — check `<dependencies>` group lists only `SharedKernel.Core` and `SharedKernel.Primitives`.
4. Update `03.Domain/state-map.md`: mark task `●`, update Overall Progress table (Done count + State).
5. Call `state-map-phase` with `phase_key: SK.03.Published` — it propagates to root, closes Phase Backlog entries for 03.Domain, updates Overall Progress counts.
6. Call `sync-brain` with `domain: 03.Domain` — if no new architectural signals exist, only a changelog entry is appended (no section edits).

**Why:** `dotnet nuget inspect` is not available in this version — use ZipFile to read the .nuspec directly.

**How to apply:** Repeat this pattern for any future re-publish (version bump) task under SK.03.Published.
