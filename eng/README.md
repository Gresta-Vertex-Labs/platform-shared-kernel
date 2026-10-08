# Build internals

This folder, together with the root build files, is how Platform.SharedKernel is built, checked,
versioned and released. It is written for maintainers changing the build. For day-to-day
contribution (commands, conventions, adding a package) see [`CONTRIBUTING.md`](../CONTRIBUTING.md).

## Contents of `eng/`

| File | What it does |
|------|--------------|
| [`SharedKernelTiers.targets`](SharedKernelTiers.targets) | The tier check. Imported by `Directory.Build.targets`; runs before `CoreCompile` and emits `SKTIER000`–`SKTIER006`. See [The tier check](#the-tier-check). |
| [`generate-package-index.cs`](generate-package-index.cs) | A .NET file-based app: `dotnet run eng/generate-package-index.cs` writes `docs/packages.md` (every packable package by tier, with the service project that references it and its SharedKernel dependencies), `docs/dependency-graph.md` (Mermaid: folder-to-folder, then one graph per folder) and `eng/solution-filters/Platform.SharedKernel.{Tier}.slnf` (each tier's packages and their test projects, for the IDE). Reads only tracked `.csproj` files: `<SharedKernelTier>`, `<IsPackable>`, `ProjectReference`s (analyzer-only references excluded). `-- --check` writes nothing and fails when any output is stale; CI runs it in the Unit job. |
| [`solution-filters/`](solution-filters) | The generated per-tier solution filters. Not test lanes: `verify-solution-filters.sh` checks only the two root lane filters. |
| [`PackageInventory.proj`](PackageInventory.proj) | Writes the release train's expected package set: one `PackageId` per line for every `.csproj` on disk that packs (default output `artifacts/expected-packages.txt`). Asks each project through the `GetSharedKernelPackageIdentity` target, so the answer is MSBuild's evaluated `IsPackable`/`PackageId`, not a naming guess. Every `.csproj` is asked, not only those in the `.slnx`, so a packable project that fell out of the solution still fails the release. Run with `dotnet msbuild eng/PackageInventory.proj -nologo -v:q -p:InventoryFile=<path>`. |
| [`verify-packages.sh`](verify-packages.sh) | `eng/verify-packages.sh <package-dir> [expected-version]`. Fails when a package is missing or unexpected, when the packages carry more than one version or the `0.0.0` MinVer floor, when the version differs from the expected one (a `v`-prefixed tag is accepted), or when the `SharedKernel.*` `<PackageVersion>` entries in `Directory.Packages.props` differ from the packable set. On success prints the version and, under GitHub Actions, exports `SK_VERSION`. `EXPECTED_PACKAGES_FILE` reuses an earlier inventory; `WRITE_EXPECTED_TO` saves this one. |
| [`verify-solution-filters.sh`](verify-solution-filters.sh) | Build-free check of `Platform.SharedKernel.slnx` against the two lanes: every test project (`*.Tests`) in exactly one lane, every production project in the Unit lane, no lane entry missing from the solution, and every project in the solution folder named after its top-level directory. |
| [`verify-path-lengths.sh`](verify-path-lengths.sh) | Build-free check that every tracked path stays within 250 characters when the repository is cloned at `C:\Github\platform-shared-kernel\`, so a Windows checkout and build stay under the 260-character `MAX_PATH`. Build output is not measured: it lives in `artifacts/`. Prints the longest path on success. |
| [`verify-markdown-links.sh`](verify-markdown-links.sh) | Build-free check that every relative link in a tracked Markdown file (outside `.claude/`) points at an existing file or folder; anchors are ignored and placeholder links containing `{` are skipped. Catches READMEs left pointing at a moved or renamed folder. |
| [`verify-tier-errors.sh`](verify-tier-errors.sh) | Proves the tier check still fails the build. Generates two throw-away probe projects under `eng/.tier-probe` (deleted on exit): a Model project referencing an Abstractions project, which must fail with `SKTIER001`, and an Adapter taking `Microsoft.AspNetCore.App`, which must fail with `SKTIER006`. Catches a downgrade to a warning or a target that silently stopped running. |
| [`testsettings/integration.runsettings`](testsettings/integration.runsettings) | VSTest settings for the Integration lane only: `MaxCpuCount=2` caps concurrent test-project hosts so many Testcontainers suites don't start containers at once, and `TestSessionTimeout` is 25 minutes. The Unit lane needs no settings file. |

## Root build files

There is exactly one of each, at the repository root. Never add a per-folder
`Directory.Build.props`: MSBuild stops its upward search at the first one it finds, so everything
below it would silently lose the root file.

### `global.json`

Pins SDK `10.0.300` with `rollForward: latestFeature` and `allowPrerelease: false`: any installed
10.0 SDK at or above 10.0.300 resolves, across feature bands, but never a preview.

### `Directory.Build.props` (evaluated before the project body)

- **Build output:** `UseArtifactsOutput` sends every project's `bin` and `obj` to
  `artifacts/{bin,obj}/{project}/{pivot}/` (for example `artifacts/bin/SharedKernel.Primitives/release/`).
  Output path length then depends on the project name only, never on folder depth. The nine
  `consumer-verify` harnesses share a project name, so their `ArtifactsProjectName` is suffixed with
  their parent folder.
- **Identity:** `IsTestProject` is `true` for a project whose name ends in `.Tests`.
- **Language:** `net10.0`, `ImplicitUsings`, `Nullable`, `LangVersion=latest` for every project.
  `TreatWarningsAsErrors` is deliberately per-project, not central.
- **NuGet audit:** `NuGetAudit` on, mode `all`, level `low`. `NU1901`–`NU1904` are demoted to
  non-fatal locally; CI escalates them back to errors with a dedicated `dotnet restore -warnaserror`
  step.
- **Nested tests:** production projects exclude `*.Tests\**`, `*.ConsumerVerify\**` and
  `consumer-verify\**` from their default items, since test projects live inside them.
- **Determinism and Source Link:** `Deterministic`, `EmbedUntrackedSources`, portable PDBs,
  `PublishRepositoryUrl`; `ContinuousIntegrationBuild` only under GitHub Actions.
- **Versioning:** MinVer settings (`MinVerTagPrefix=v`, `MinVerMinimumMajorMinor=1.0`,
  `MinVerAutoIncrement=patch`). See [Versioning](#versioning-minver).
- **Package metadata** for non-test projects: authors, company, product, copyright, MIT license
  expression, `PackageReadmeFile=README.md`, repository and project URLs, `.snupkg` symbols, and
  `PackageOutputPath` = the root `nupkgs/` folder (matching the local feed in `NuGet.Config`).
  `Description` and `PackageTags` are the only metadata declared per project.
- **README packing:** an adjacent `README.md` is packed at the package root when it exists (a
  missing one is turned into a hard error by `SKPKG003`).
- **Non-shipping defaults:** test projects get `IsPackable`/`IsPublishable`/`GeneratePackageOnBuild`
  = `false` unless they set them.

### `Directory.Build.targets` (evaluated after the project body)

- `GenerateDocumentationFile=true` for shipping libraries (not test projects, not `Exe` harnesses),
  unless the project sets it.
- Packaging guards, run before `Pack` on packable projects:

  | Code | Rule |
  |------|------|
  | `SKPKG001` | a packable project must declare `<Description>` |
  | `SKPKG002` | a packable project must declare `<PackageTags>` |
  | `SKPKG003` | a packable project must have an adjacent `README.md` |
  | `SKPKG004` | a project named `*.Tests` must never be packable |

- Imports `eng/SharedKernelTiers.targets`.
- `GetSharedKernelPackageIdentity`: returns a project's `PackageId` and tier when it packs; queried
  by `eng/PackageInventory.proj`.

### `Directory.Packages.props` (Central Package Management)

- `ManagePackageVersionsCentrally=true`: no `PackageReference` carries a `Version=`.
- `CentralPackageTransitivePinningEnabled=false`: a transitive pin would be promoted into an
  explicit dependency of every packed package that touches it. A vulnerable transitive is fixed
  with an explicit reference in the owning project, or, when many unrelated projects reach it, a
  `<GlobalPackageReference>` (currently `SSH.NET` and `SQLitePCLRaw.bundle_e_sqlite3`).
- `CentralPackageVersionOverrideEnabled=false`: no per-project overrides.
- `GlobalPackageReference` **MinVer** applies to every project with `PrivateAssets="all"`, so it
  never appears in a `.nuspec`.
- One `<PackageVersion>` per packable kernel project, all on `$(SharedKernelPackageVersion)`. It
  defaults to the float `*-*` for local work (legal only because
  `CentralPackageFloatingVersionsEnabled=true`, and safe because `NuGet.Config` routes `SharedKernel.*`
  to the local `nupkgs/` feed only). CI always passes the exact packed version with
  `-p:SharedKernelPackageVersion=<version>`. `verify-packages.sh` fails when this list and the
  packable set differ.

### `NuGet.Config`

Clears inherited sources, then maps `SharedKernel.*` to the local folder feed `nupkgs/` and
everything else to nuget.org through `packageSourceMapping`.

## The tier check

Every production `.csproj` declares `<SharedKernelTier>` (Foundation, Model, Abstractions, Adapter,
Host, Testing, Tooling). `SharedKernelTiers.targets` runs `ValidateSharedKernelTier` before
`CoreCompile` (skipped for design-time builds and test projects) and checks only the project's
**direct** references: the list is captured at evaluation time, before the SDK appends the
transitive closure, so a chain of allowed edges is never reported as a shortcut. A reference with
`ReferenceOutputAssembly="false"` (build ordering or an analyzer load) is not a dependency and is
ignored.

| Code | Error |
|------|-------|
| `SKTIER000` | unknown tier value |
| `SKTIER001` | a `ProjectReference` to a tier this tier may not reference |
| `SKTIER002` | an Adapter → Adapter reference not named in `<SharedKernelAllowedAdapterReferences>` |
| `SKTIER003` | a Model/Abstractions project takes a runtime NuGet package other than `Microsoft.Extensions.*Abstractions` (`PrivateAssets="all"` references are exempt) |
| `SKTIER004` | a tiered project references a project with no tier |
| `SKTIER005` | a packable library (not an `Exe`) declares no tier |
| `SKTIER006` | a project below Host/Testing references `Microsoft.AspNetCore.App` (directly or as a transitive framework reference) or any `Microsoft.AspNetCore.*` package |

All are errors; there is no baseline or downgrade file. The full build shows the repository has no
violation; `verify-tier-errors.sh` (the CI `tier-check` job) shows a new one would still fail.
Test projects, consumer-verify harnesses and samples declare no tier and are not checked.

## Versioning (MinVer)

- MinVer computes `Version` from the newest reachable tag matching `v*`. No `.csproj` declares a
  `<Version>` or `<VersionPrefix>`, and CI fails if one does; MinVer would overwrite it anyway.
- With no tag reachable it stamps `1.0.0-alpha.0.N` (the `MinVerMinimumMajorMinor` floor); after tag
  `v1.2.3`, commits build as `1.2.4-alpha.0.N`.
- **Every package ships at one version.** `dotnet pack` turns a `ProjectReference` into a package
  dependency at the referenced project's version, and nearly every package depends on the Foundation
  tier, so independent versions would publish dependencies on versions that were never released.
- **CI must check out with `fetch-depth: 0`.** A shallow clone has no tags, and MinVer does not fail:
  it silently stamps the floor version. `verify-packages.sh` rejects a `0.0.0` version and a version
  that differs from the release tag.

## CI workflows

All live in `.github/workflows/`. Every third-party action is pinned to a full commit SHA and every
container image to a digest; `permissions: {}` at the root, widened per job; `ubuntu-latest` only
(Testcontainers needs a Linux Docker daemon).

| Workflow | Trigger | What it does |
|----------|---------|--------------|
| `verify.yml` | `workflow_call` only | The reusable gate set, switched by inputs: `tier-check` (`verify-tier-errors.sh`); `unit-test` (solution-filter, path-length, Markdown-link and generated-package-view checks, no-`<Version>` check, restore, escalated NuGet audit, full `.slnx` build, Unit lane, every in-solution consumer-verify harness); `integration-test` (Integration lane with `integration.runsettings`); `packaging-verify` (pack everything, `verify-packages.sh`, then restore, build and test every packed-package consumer and the Shop sample, run its unit tests, assert `SK0001` fires from the packed analyzer). Can upload the verified `.nupkg` set as the `packages` artifact. |
| `ci.yml` | PRs and pushes to `main` (Markdown and `.claude/` changes ignored), merge queue, nightly, manual | Calls `verify.yml` for the required lane (unit + packaging) and, outside PRs, the Integration lane; lints workflows with actionlint; lints the PR title as Conventional Commits; `CI Gate` aggregates the required jobs and is the one required CI status check. A red Integration run opens or updates a tracking issue. |
| `release.yml` | push of a `v<major>.<minor>.<patch>[-prerelease]` tag | `tag-guard` (SemVer tag on a commit in `main`) → `verify.yml` with every gate on and the version required to equal the tag → `publish`: re-verifies the downloaded set and pushes every `.nupkg` to GitHub Packages (`environment: nuget-publish`). |
| `publish-package.yml` | manual | Dry run for one package: restore, build, test, pack, print the `.nuspec` dependencies and check they resolve from GitHub Packages. Never publishes. |
| `pr-template.yml` | `pull_request_target`, merge queue | The **PR template** check: fails until every section of `.github/pull_request_template.md` is filled in, with one self-updating bot comment. Never checks out PR code. |
| `pr-labeler.yml` | `pull_request_target` | Adds `area:` and type labels from the changed paths (`.github/labeler.yml`). |
| `issue-triage.yml` | issue opened/edited | Adds `area:` labels from the issue form's affected-domain answer (`.github/scripts/issue-triage.cjs`). |
| `repo-automation.yml` | changes to labels, issue forms, the PR template or `.github/scripts` | Runs the Node tests in `.github/scripts` and syncs repository labels with `.github/labels.yml` (dry run on PRs, applied on `main`). |

Repository rulesets are kept as JSON in `.github/rulesets/`: `main-branch.json` (squash merges only,
linear history, required checks `CI Gate` and `PR template`) and `tag-protection.json` (who may
create `v*` tags). They take effect only once imported into the repository settings.

## Test run settings

- **Unit lane** (`Platform.SharedKernel.Unit.slnf`): every production project plus every
  container-free test project; runs with default settings.
- **Integration lane** (`Platform.SharedKernel.Integration.slnf`): the Testcontainers suites; always
  run with `-s eng/testsettings/integration.runsettings`.
- The full build always covers the whole `.slnx`; only test execution is split. A solution filter
  lists entry projects only, and their `ProjectReference`s resolve automatically.
