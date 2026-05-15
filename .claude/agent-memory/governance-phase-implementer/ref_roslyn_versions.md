---
name: ref_roslyn_versions
description: Roslyn version pin, RS2008 suppression strategy, and Roslyn version conflict in test projects for 00.Governance
metadata:
  type: reference
---

## Microsoft.CodeAnalysis.CSharp version pin: 4.14.0

`SharedKernel.Analyzers.csproj` pins `Microsoft.CodeAnalysis.CSharp` at **4.14.0** with `PrivateAssets="all"`.

## RS2008 suppression

`EnforceExtendedAnalyzerRules=true` triggers RS2008 (release tracking) for every DiagnosticDescriptor.
The text-file approach (`AnalyzerReleaseTracking.Shipped.txt` / `Unshipped.txt`) does NOT suppress it reliably.
**Fix**: Add `<NoWarn>$(NoWarn);RS2008</NoWarn>` to the `<PropertyGroup>` in `SharedKernel.Analyzers.csproj`.

## Test project Roslyn conflict

`Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit` 1.1.2 pulls Roslyn **1.0.1** transitively.
This conflicts with the 4.14.0 used by the Analyzers project, causing CS1705 at build time.
**Fix**: Add explicit `<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.14.0" />` to `SharedKernel.Analyzers.Tests.csproj`.

## NU1701 warnings

The old testing package also pulls `Microsoft.CodeAnalysis.CSharp.Workspaces 1.0.1`, `Microsoft.CodeAnalysis.Workspaces.Common 1.0.1`, and `Microsoft.Composition 1.0.27` — all targeting .NET Framework.
These produce NU1701 (compatibility) warnings. They are benign and do not affect test execution on net10.0.
