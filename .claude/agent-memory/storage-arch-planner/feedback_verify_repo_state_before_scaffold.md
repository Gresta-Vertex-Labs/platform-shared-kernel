---
name: feedback_verify_repo_state_before_scaffold
description: Bare placeholder .csproj files can already exist and be registered in the .slnx even when state-map.md shows a package at `○` — always check the filesystem before writing Scaffold tasks
metadata:
  type: feedback
---

When planning the very first Scaffold phase for a domain/package that `08.Storage/state-map.md`
shows at `○` (not started), do not assume zero files exist. During the WO-043 P-265/266/267
planning pass, `08.Storage/SharedKernel.Storage.Abstractions/SharedKernel.Storage.Abstractions.csproj`
and `SharedKernel.Storage.S3/SharedKernel.Storage.S3.csproj` (+ its `.Tests.csproj`) already existed
on disk — bare `<TargetFramework>net10.0</TargetFramework>`/`ImplicitUsings`/`Nullable` only, zero
`ProjectReference`/`PackageReference`, zero folder structure — and were already registered in
`Platform.SharedKernel.slnx`. These were leftover artifacts from an earlier repo-wide bootstrap
pass, not real Scaffold-phase output.

**Why:** Writing Scaffold tasks as "create X.csproj from scratch" when the file already exists
(even if empty) produces a task description that doesn't match reality, which the phase-implementer
would have to silently correct. It also risks the `.slnx` registration being duplicated or
conflicting.

**How to apply:** Before drafting Scaffold-phase tasks for any package, run a filesystem check
(`find`/`Glob`) under the domain folder for existing `.csproj` files, and grep the `.slnx` for
whether they're already registered. Phrase Scaffold tasks as "flesh out the existing empty
placeholder" vs. "create from scratch" accordingly — this distinction matters for the
phase-implementer that consumes the plan next. This generalizes beyond 08.Storage: any domain
that hasn't had its own Scaffold phase executed yet may still have these bootstrap placeholders.
