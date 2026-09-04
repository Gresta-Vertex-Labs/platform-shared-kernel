---
name: project-01core-build-conventions
description: Build/packaging/versioning conventions specific to 01.Core, confirmed across the P-443 and P-444 implementation sessions
type: project
---

Confirmed conventions for implementing a new `01.Core` package end-to-end (most recently:
P-444/`SharedKernel.Validation.FluentValidation`, following the P-443/`SharedKernel.Validation`
precedent):

- **Versioning is repo-wide MinVer lockstep** (since the 2026-08-25 switch documented in root
  `CLAUDE.md`). Never add `<Version>`/`<PackageVersion>`/`<PackageReleaseNotes>` to any `.csproj`.
  NuGet metadata in a new package's `.csproj` is only `<Description>` and `<PackageTags>` — everything
  else (Authors, license, repo URLs, README packaging) comes from the root `Directory.Build.props`.
- **Local NuGet feed**: `NuGet.Config` routes every `SharedKernel.*` package name to the local
  `nupkgs/` feed via source mapping; `dotnet pack` writes there automatically
  (`PackageOutputPath` set in `Directory.Build.props`). A new package needs a
  `PackageVersion Include="SharedKernel.X" Version="$(SharedKernelPackageVersion)"` line added to
  `Directory.Packages.props` before `01.Core/SharedKernel.Consumer.Tests` can add a
  `PackageReference` to it.
- **Consumer verification pattern**: `01.Core/SharedKernel.Consumer.Tests` is the project that
  proves a packed package's dependency graph resolves correctly (not `ProjectReference` — real
  `PackageReference` against the local feed). Every new package gets a `PackageReference` there plus
  a couple of tests exercising its public surface. As of P-444: 56/56 passing (was 54/54 before
  P-444, 50/50 before P-443).
- **`global.json` pins SDK 10.0.300, which is not installed locally** — only 10.0.103/10.0.400 are
  present. Always run `dotnet build`/`test`/`pack` from a working directory OUTSIDE the repo tree
  (e.g. `/tmp` via the Bash tool, passing the full `.csproj` path) so `global.json` doesn't get
  picked up and force an SDK-not-found error. Never edit `global.json` to work around this.
- **Third-party NuGet dependency exception**: as of P-447, TWO `01.Core` packages carry a
  third-party NuGet dependency: `SharedKernel.Validation.FluentValidation` (`FluentValidation`) and
  `SharedKernel.Cryptography.KeyVault.Azure` (`Azure.Security.KeyVault.Keys` + `Azure.Identity`).
  Every other package in this domain — including `SharedKernel.DataPrivacy` (P-474) — is
  dependency-free by design, referencing only sibling `SharedKernel.*` packages. If a future phase
  needs another third-party library, check whether `Directory.Packages.props` already has it pinned
  before adding a new `PackageVersion` entry.
- **Zero-dependency claim verification**: don't just assert "zero third-party dependency" in prose —
  after `dotnet pack`, unzip the produced `.nupkg` and read the `.nuspec`'s `<dependencies>` block
  directly (`unzip -p X.nupkg X.nuspec`). For a single-dependency package this is a one-line check
  and catches a stray transitive reference immediately. Mirror it in `SharedKernel.Consumer.Tests`
  with `Assert.Single(dependencyIds)` + `Assert.Contains("SharedKernel.Primitives", ...)` — the
  `.KeyVault.Azure`-precedent pattern (proving Azure packages don't leak onto `SharedKernel.Cryptography`)
  generalizes cleanly to "prove this package has exactly N dependencies, not just prove one is present".
- **Test project shape**: nested `SharedKernel.X.Tests/` folder inside the package folder,
  `IsPackable=false`, xUnit + NSubstitute (NSubstitute is referenced even if unused — matches the
  sibling `SharedKernel.Validation.Tests` convention exactly).
- **README-sample compile-verification**: every package with a usage-sample README section gets a
  dedicated `ReadmeSampleCompileTests.cs` (or a throwaway scratch `.cs` deleted after the build
  passes) in its test project that compiles the *exact* code shown in the README, not a paraphrase.
  This has now caught real issues across at least four sessions — most recently P-474
  (`SharedKernel.DataPrivacy`): a `public sealed class` README sample implementing an internal
  consumer-local interface failed CS0051 (inconsistent accessibility) until the class was made
  `internal` to match — a one-line fix, but one a plain eyeball-read would have missed. Prefer a
  throwaway scratch file over a permanent test file when the sample is illustrative/consumer-side
  code (uses undefined `ICustomerRepository`/`Customer`-style stand-ins) rather than a real public API.
