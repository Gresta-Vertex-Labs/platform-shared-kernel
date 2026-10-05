# Contributing to Platform.SharedKernel

Thanks for your interest in contributing. Platform.SharedKernel is a mono-repo of .NET 10 NuGet
packages that form the shared kernel of a microservice ecosystem: reusable building blocks, no
business logic. This guide covers how to build it, how the repository is organized, the rules the
build enforces, and how changes get reviewed and released.

## Contents

- [Ground rules](#ground-rules)
- [Prerequisites](#prerequisites)
- [Build and test](#build-and-test)
- [Repository layout](#repository-layout)
- [Coding conventions](#coding-conventions)
- [Adding a package](#adding-a-package)
- [Package READMEs](#package-readmes)
- [Commits and pull requests](#commits-and-pull-requests)
- [Versioning and releases](#versioning-and-releases)
- [Security issues](#security-issues)
- [Code of Conduct](#code-of-conduct)
- [AI-assisted development](#ai-assisted-development)

## Ground rules

- **Open an issue before large changes.** A new package, a new public contract, or a change that
  crosses capability folders is easier to agree on before the code exists. The issue forms include an
  architecture proposal template for exactly this.
- **Keep business logic out.** Everything here is a reusable building block. A concept that belongs
  to one service stays in that service.
- **The build is the reviewer of first resort.** Tiers, packaging metadata, the package set and the
  test lanes are all checked mechanically. If the build or CI rejects something, fix the cause rather
  than suppressing the check.
- **Every change ships to everyone.** All packages share one version, so a change in any package is
  part of the next release of the whole set.
- **Security problems are never reported in public.** See [Security issues](#security-issues).

## Prerequisites

| Tool | Version | Needed for |
|------|---------|------------|
| .NET SDK | 10.0.300 or a later 10.0 feature band (pinned in [`global.json`](global.json): `rollForward: latestFeature`, no previews) | everything |
| Docker | any recent Docker Engine / Docker Desktop running Linux containers | the Integration test lane (Testcontainers) |
| Bash | Git Bash on Windows is fine | the `eng/*.sh` verification scripts |

## Build and test

All commands run from the repository root.

```bash
# Full build: every project in the solution
dotnet build Platform.SharedKernel.slnx -c Release

# Unit lane: every container-free test project, no Docker needed
dotnet test Platform.SharedKernel.Unit.slnf -c Release --no-build

# Integration lane: the Testcontainers-backed suites (Docker required)
dotnet test Platform.SharedKernel.Integration.slnf -c Release --no-build \
  -s eng/testsettings/integration.runsettings
```

The runsettings file caps how many container-backed test projects run at once; without it, many
suites starting containers simultaneously can time out on a small machine.

Optional checks that CI also runs:

```bash
bash eng/verify-solution-filters.sh     # every test project in exactly one lane
bash eng/verify-tier-errors.sh          # the tier check still rejects violations

dotnet pack Platform.SharedKernel.slnx -c Release --no-build   # writes to ./nupkgs
bash eng/verify-packages.sh nupkgs      # the packed set is complete and at one version
```

Run `verify-packages.sh` against a folder holding a single pack only. Build internals, including
what each of these scripts checks, are described in [`eng/README.md`](eng/README.md).

## Repository layout

### Capability folders

The source tree is grouped into zones that mirror the projects of a consuming service, with one
folder per capability domain inside each zone:

| Zone | Mainly referenced by | Capability folders |
| --- | --- | --- |
| `src/Foundation/` | every project | one folder (the 01.Core packages) |
| `src/Model/` | the Domain project | `Domain/`, `Contracts/` |
| `src/Application/` | the Application project | one folder (the 05.Application packages) |
| `src/Infrastructure/` | the Infrastructure project | `Caching/`, `Persistence/`, `Messaging/`, `Storage/`, `Search/`, `AI/`, `Communication/`, `Integration/`, `Workflows/`, `Idempotency/`, `Scheduling/`, `Reporting/` |
| `src/Hosting/` | the Api / Worker project | `Security/`, `ServiceDefaults/`, `Presentation/` |
| `src/Testing/` | test projects | one folder |
| `tools/Governance/` | the build | one folder |

Each capability folder has an overview `README.md`. A domain keeps its id (`06.Persistence`) for
work orders, phase keys and its EventId block; the root `CLAUDE.md` maps every id to its folder.
`samples/` holds reference services that consume the packages the way a real service would.

The zone is where a capability mainly belongs, not a rule: what a package may reference is decided by
its tier, and [`docs/packages.md`](docs/packages.md) lists every package with its tier.

### Tiers

Every production `.csproj` declares a `<SharedKernelTier>`. The tier says which project inside a
consuming service is expected to reference the package, and so what the package itself may
reference. The build checks every direct `ProjectReference` before compiling.

| Tier | May reference | Third-party packages | Consumed by (in a service) |
|------|---------------|----------------------|----------------------------|
| **Foundation** | Foundation | any | every project |
| **Model** | Foundation, Model | `Microsoft.Extensions.*.Abstractions` only | Domain project |
| **Abstractions** | Foundation, Model, Abstractions | `Microsoft.Extensions.*.Abstractions` only | Application project |
| **Adapter** | Foundation, Model, Abstractions, plus adapters listed in `<SharedKernelAllowedAdapterReferences>` | any except ASP.NET Core | Infrastructure project |
| **Host** | everything except Testing and Tooling | any | Api/Worker project |
| **Testing** | everything except Tooling | any | test projects only |
| **Tooling** | nothing | any | build / analyzers |

Violations are build errors:

| Code | Meaning |
|------|---------|
| `SKTIER000` | the project declares an unknown tier |
| `SKTIER001` | a reference to a tier this tier may not reference |
| `SKTIER002` | an Adapter → Adapter reference not declared in `<SharedKernelAllowedAdapterReferences>` |
| `SKTIER003` | a Model/Abstractions package takes a runtime NuGet package outside the allow-list |
| `SKTIER004` | a reference to a project that declares no tier |
| `SKTIER005` | a packable library declares no tier |
| `SKTIER006` | ASP.NET Core (`Microsoft.AspNetCore.App` or `Microsoft.AspNetCore.*`, transitively too) below Host/Testing |

A new Adapter → Adapter edge is a `.csproj` declaration and is reviewed like any other API change.
Rules the tiers cannot express (for example, `SharedKernel.Domain` and `SharedKernel.Contracts`
never referencing each other) are architecture tests in `tools/Governance/SharedKernel.ArchitectureTests`.
Test projects, consumer-verify harnesses and samples declare no tier; they are consumers.

### Tests

- Test projects are nested **inside the folder of the project they test**, e.g.
  `src/Infrastructure/Persistence/SharedKernel.Persistence.EfCore/SharedKernel.Persistence.EfCore.Tests/`. There is
  no top-level `tests/` folder.
- A test project references `SharedKernel.Testing` plus the capability's
  `SharedKernel.{Capability}.Testing` package, and `SharedKernel.Testing.Internal` when it needs
  Testcontainers fixtures.
- Every test project is in **exactly one** lane: `Platform.SharedKernel.Unit.slnf` (no Docker) or
  `Platform.SharedKernel.Integration.slnf` (Testcontainers). `eng/verify-solution-filters.sh` fails
  CI otherwise.

## Coding conventions

These conventions are part of the kernel's public contract. They ship to consuming services as
Roslyn analyzers (`SharedKernel.Analyzers`, rules `SK0001`–`SK0708`) and architecture tests
(`SharedKernel.ArchitectureTests`), both in `00.Governance`, and this repository's own code follows
them too.

### Logging

- Every production log statement uses the source-generated `[LoggerMessage]` pattern. Direct
  `ILogger.LogInformation(...)`-style calls and hand-written `LoggerMessage.Define<>()` delegates are
  not accepted.
- Every `[LoggerMessage]` sets an **explicit `EventId`** from its domain's block:
  `{domain number} × 1000` to `+999` (for example `05.Application` = 5000–5999). A domain with
  several packages gives each a 100-wide sub-block, recorded in that domain's `CLAUDE.md`. The
  registry of base values is `LoggingEventIdRanges` in `SharedKernel.Primitives`.
- Message placeholders are PascalCase named properties (`{RequestName}`), never interpolated.
- Correlation id, trace context and tenant id are never template placeholders; they flow from the
  request context.
- `SharedKernel.Domain` and `SharedKernel.Contracts` stay logging-free.

### Results and errors

- Expected failures are `Result`/`Result<T>` values, not exceptions.
- A `Result` is never silently discarded (`SK0030`); discard deliberately with `_ =`.

### No magic strings

A string literal used from more than one call site, or that is part of a wire contract (header
names, baggage and tag keys, configuration section names, claim types, cache-key components) is a
named constant (`SK0022`). Cross-package wire constants live in `SharedKernel.Primitives`
(`WellKnownHeaders`, `WellKnownBaggageKeys`, `WellKnownTagKeys`); package-local ones in a small
constants class in that package.

### Options

Register options with `AddValidatedOptions<TOptions>(configuration)` from
`SharedKernel.Configuration`, never `services.Configure<TOptions>(section)`. The options type
declares its own section path by implementing `ISectionBoundOptions`.

### Public API tracking

Shipping packages track their public surface with `Microsoft.CodeAnalysis.PublicApiAnalyzers`:
each has a `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`. A new or changed public member
goes into `PublicAPI.Unshipped.txt` (the IDE code fix adds it), so every public API change is a
visible diff in review. Public members need XML documentation comments.

### Style

- Nullable reference types and implicit usings are on everywhere (`Directory.Build.props`).
- Some projects opt into `TreatWarningsAsErrors`; keep a project warning-free when you touch it.
- Prefer AOT- and trim-friendly code (source-generated `JsonSerializerContext`, no reflection-based
  generic invocation) when it costs little. Don't add `<IsAotCompatible>` to a project.
- Object mapping is hand-written or Mapperly; AutoMapper and Mapster's runtime API are not used.

## Adding a package

1. **Pick the name.** Follow the naming convention:

   | Name | Purpose |
   |------|---------|
   | `SharedKernel.{Capability}` | main package (interfaces + default implementation when there is one provider) |
   | `SharedKernel.{Capability}.Abstractions` | interfaces only, Abstractions tier |
   | `SharedKernel.{Capability}.{Provider}` | a technology-specific implementation, Adapter tier |
   | `SharedKernel.{Capability}.Testing` | fakes for that capability, Testing tier, in `16.Testing` |
   | `SharedKernel.{Capability}.Tests` | the test project, nested inside the project folder |

   A capability with more than one provider splits into `.Abstractions` + `.{Provider}`.

2. **Check the path length.** Every file of the new project, its test project included, must stay
   within 250 characters when the repository is cloned at `C:\Github\platform-shared-kernel\`
   (Windows MAX_PATH). `bash eng/verify-path-lengths.sh` checks it, and CI runs it. Build output does
   not count: it goes to `artifacts/`.

3. **Create the project** in its capability folder. The `.csproj` declares:
   - `<SharedKernelTier>` (and `<SharedKernelAllowedAdapterReferences>` if it is an adapter built on
     another adapter);
   - `<Description>` and `<PackageTags>`: packing fails without them (`SKPKG001`, `SKPKG002`);
   - no `<Version>`: versions come from the git tag, and CI fails on a version in a project file;
   - `PackageReference`s without `Version=`: versions are central in `Directory.Packages.props`.

   Package metadata (authors, license, repository URL, symbols, README packing) is set centrally by
   `Directory.Build.props`.

4. **Add a `README.md`** next to the `.csproj`. It is packed into the package, and packing fails
   without it (`SKPKG003`). See [Package READMEs](#package-readmes).

5. **Register it everywhere it must appear:**
   - `Platform.SharedKernel.slnx`, in the solution folder named after its capability folder (for example `/src/Infrastructure/Caching/`);
   - `Platform.SharedKernel.Unit.slnf` (every production project belongs in the Unit lane);
   - its test project in exactly one lane `.slnf`;
   - `Directory.Packages.props`: one `<PackageVersion Include="SharedKernel.X" Version="$(SharedKernelPackageVersion)" />`
     per packable project. `eng/verify-packages.sh` fails when this list and the packable set differ;
   - then run `dotnet run eng/generate-package-index.cs` to refresh `docs/packages.md`,
     `docs/dependency-graph.md` and the per-tier filters in `eng/solution-filters/` (CI checks them).

6. **Test it.** Add the nested `.Tests` project; a name ending in `.Tests` can never pack (`SKPKG004`).

## Package READMEs

Every package README follows the
[package README standard](docs/package-readme-standard.md). The README is what a consumer sees on the
package feed, so it should answer "what is this, when do I use it, and how do I wire it up" without
reading the source.

## Commits and pull requests

### PR titles: Conventional Commits

Pull requests are **squash-merged**, so the PR title becomes the commit on `main`. CI lints the PR
title against [Conventional Commits](https://www.conventionalcommits.org/):

```text
type(optional-scope): summary
```

Allowed types: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`,
`revert`. The scope is optional; use the capability name when it helps (`feat(caching): ...`). Mark
a breaking change with `!`:

```text
feat(caching): add fencing token to renewable locks
fix(security)!: reject DPoP proofs without an ath claim
```

Individual commit messages inside a PR are not linted.

### The PR template

Fill in every section of the [pull request template](.github/pull_request_template.md): summary,
related issues, breaking changes, security impact, how it was tested, and the checklist. The
**PR template** check fails, with a bot comment listing what is missing, until every section is
complete; it re-runs when you edit the description.

### What must pass

| Check | What it covers |
|-------|----------------|
| **CI Gate** | tier-violation probes, solution-filter check, full build, Unit lane, consumer-verify harnesses, pack + package-set check, packed-package consumers and samples, workflow lint, PR title lint |
| **PR template** | the description is complete |

The Integration lane does not run on pull requests. It runs on every push to `main`, nightly and on
demand, and a red run opens a tracking issue. If your change touches container-backed code, run the
Integration lane locally before opening the PR. Every release tag runs it as a required gate.

## Versioning and releases

### One version for every package

Every package carries **the same version**, computed by [MinVer](https://github.com/adamralph/minver)
from the newest `v*` git tag. No project declares a version. Between tags, builds get a prerelease
version such as `1.2.4-alpha.0.N`.

### Releasing (maintainers)

A release is a SemVer tag on a commit on `main`:

```bash
git tag v1.2.0
git push origin v1.2.0
```

[`release.yml`](.github/workflows/release.yml) checks the tag is well formed and on `main`, runs
every gate (both test lanes included), packs **all** packages at that version, verifies the set is
complete, and publishes them together to GitHub Packages. There is no per-package publish.

### Consuming the packages

A consuming service pins **one** version property in its `Directory.Packages.props` and points every
`SharedKernel.*` package at it:

```xml
<!-- Directory.Packages.props (the consuming service) -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <!-- The one SharedKernel release this service builds against. Upgrade = change this line. -->
    <SharedKernelVersion>1.0.0</SharedKernelVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="SharedKernel.Primitives" Version="$(SharedKernelVersion)" />
    <PackageVersion Include="SharedKernel.Application" Version="$(SharedKernelVersion)" />
    <PackageVersion Include="SharedKernel.ServiceDefaults" Version="$(SharedKernelVersion)" />
    <!-- ...one line per SharedKernel package the service references, always $(SharedKernelVersion). -->
    <PackageVersion Include="SharedKernel.Testing" Version="$(SharedKernelVersion)" />
  </ItemGroup>
</Project>
```

Packages are published to GitHub Packages, which requires authentication to read. Map
`SharedKernel.*` to that feed so no other source can supply a package of that name, and read the
credentials from the environment:

```xml
<!-- NuGet.Config (the consuming service) -->
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="sharedkernel" value="https://nuget.pkg.github.com/Gresta-Vertex-Labs/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="sharedkernel"><package pattern="SharedKernel.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
  <packageSourceCredentials>
    <sharedkernel>
      <add key="Username" value="%GITHUB_ACTOR%" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </sharedkernel>
  </packageSourceCredentials>
</configuration>
```

Locally, `GITHUB_TOKEN` is a personal access token with `read:packages`; in GitHub Actions it is the
job token with `permissions: packages: read`. Never pin one `SharedKernel.*` package to a different
version from the rest, and never float the version. [`samples/README.md`](samples/README.md) shows
which package goes into which project of a service.

**Inside this repository** the samples and harnesses use the same shape with the repository's own
files: `Directory.Packages.props` points every `SharedKernel.*` package at
`$(SharedKernelPackageVersion)`, and `NuGet.Config` maps `SharedKernel.*` to the local `./nupkgs`
folder that `dotnet pack` writes to.

## Security issues

**Do not report security vulnerabilities through public issues, discussions or pull requests.**
Follow [SECURITY.md](SECURITY.md): use GitHub private vulnerability reporting or the email address
listed there.

## Code of Conduct

This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md). By participating you agree to
uphold it.

## AI-assisted development

The repository ships a root `CLAUDE.md`, a `CLAUDE.md` per capability folder and a `.claude/`
directory of agents and commands, which [Claude Code](https://claude.com/claude-code) uses to plan
and implement changes within the rules above. They are optional: you don't need Claude Code or any
AI tool to contribute, and contributions are reviewed the same way however they were written. The
`CLAUDE.md` files are also a dense reference for the rules of each capability folder, if you want the
detail.
