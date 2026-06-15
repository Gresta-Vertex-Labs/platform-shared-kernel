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

**Status (2026-06-15):** All 6 phases of 03.Domain (Design, Scaffold, Core, Tests, Docs, Published) are now ● complete — SharedKernel.Domain 1.6.0 published. 03.Domain has no further pending phases unless a new WO is dispatched.

**Windows PowerShell 5.1 caveat:** `[System.IO.Compression.ZipFile]` requires `Add-Type -AssemblyName System.IO.Compression.FileSystem` first in PS 5.1 (the type is not auto-loaded). Also, PS 5.1 cannot `[System.Reflection.Assembly]::LoadFrom` a net10.0 DLL (no System.Runtime v10 in the PS5.1 host) — to verify exported types from a packed assembly, use a consumer-verify .NET project (`dotnet test`) instead of PowerShell reflection.

**Stale global NuGet cache hazard:** If `01.Core/SharedKernel.Core.csproj` source has changed since the cached `~/.nuget/packages/sharedkernel.core/{version}` was populated (same version number, different content — e.g. DomainException unsealed in WO-011 but cache predates that), consumer-verify tests fail with `TypeLoadException` on load (e.g. "parent type is sealed"). Fix: repack `SharedKernel.Core`/`SharedKernel.Primitives` to `nupkgs/`, delete the stale `~/.nuget/packages/sharedkernel.{core,primitives,domain}/{version}` cache dirs, then re-run `dotnet test` on the consumer-verify project so NuGet re-resolves from the refreshed local feed.

**ConsumerVerify PackageReference version drift:** `03.Domain/SharedKernel.Domain.ConsumerVerify/SharedKernel.Domain.ConsumerVerify.csproj` PackageReference for SharedKernel.Domain was still pinned at "1.0.0" going into P-10 (never bumped across P-06..P-09). Bump it to match the current pack version each time, and add tests for any new public surface introduced by the phase being published.
