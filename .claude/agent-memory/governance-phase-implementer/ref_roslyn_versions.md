---
name: ref_roslyn_versions
description: Roslyn version pin, correct AnalyzerReleases.*.md release-tracking setup (never suppress RS2008), and Roslyn version conflict in test projects for 00.Governance
metadata:
  type: reference
---

## Microsoft.CodeAnalysis.CSharp version pin: 4.14.0

`SharedKernel.Analyzers.csproj` pins `Microsoft.CodeAnalysis.CSharp` at **4.14.0** with `PrivateAssets="all"`.

## RS2008 release tracking — CORRECTED 2026-09-10

`EnforceExtendedAnalyzerRules=true` triggers RS2008 (release tracking) for every DiagnosticDescriptor.

**This entry previously said release tracking "does NOT suppress it reliably" and prescribed
`<NoWarn>$(NoWarn);RS2008</NoWarn>`. That was wrong, and the wrongness hid a broken setup for
several work orders.** The real cause was the *filenames*: Roslyn's release-tracking analyzer
only recognises `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md`. The files were
named `AnalyzerReleaseTracking.Shipped.txt`/`.Unshipped.txt`, so they were registered as
`AdditionalFiles` correctly but nothing ever read them — RS2008 fired for all 41 rules regardless
of their contents.

**Correct setup** (in place since WO-082 follow-up, verified with RS2008 enabled):

- Files are named `AnalyzerReleases.Shipped.md` / `AnalyzerReleases.Unshipped.md`, registered via
  `<AdditionalFiles Include="..." />` in `SharedKernel.Analyzers.csproj`.
- `Shipped.md` carries every shipped rule under a `## Release X.Y` header, in a
  `Rule ID | Category | Severity | Notes` table. All 41 rules are recorded under `## Release 1.0`.
- `Unshipped.md` holds only its header comments once everything has shipped; a NEW rule goes there
  first and moves to `Shipped.md` when a release is cut.
- **There is no `NoWarn` for RS2008 and there must not be one.** With correct filenames the build
  is 0 warnings. Adding a rule without recording it makes RS2008 fire — which is the whole point.

**Never suppress RS2008 to make a build quiet.** Verified non-vacuous: deleting one rule row from
`Shipped.md` produces exactly one RS2008 warning; restoring it returns to zero.

## Test project Roslyn conflict

`Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit` 1.1.2 pulls Roslyn **1.0.1** transitively.
This conflicts with the 4.14.0 used by the Analyzers project, causing CS1705 at build time.
**Fix**: Add explicit `<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.14.0" />` to `SharedKernel.Analyzers.Tests.csproj`.

## NU1701 warnings

The old testing package also pulls `Microsoft.CodeAnalysis.CSharp.Workspaces 1.0.1`, `Microsoft.CodeAnalysis.Workspaces.Common 1.0.1`, and `Microsoft.Composition 1.0.27` — all targeting .NET Framework.
These produce NU1701 (compatibility) warnings. They are benign and do not affect test execution on net10.0.
