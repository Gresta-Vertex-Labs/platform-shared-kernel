---
name: reference-local-nuget-feed
description: ./nupkgs is only the local pack / consumer-verify feed; real publishing is the one-tag release train to GitHub Packages (P-572)
metadata:
  type: reference
---

"Pack and publish" means two different things in this repo. Keep them apart:

- **Publishing** means the release train (P-572). You push one `v*` tag, and `.github/workflows/release.yml` runs `tag-guard` → `verify` → `publish` (environment `nuget-publish`). That run packs and pushes **every** package to GitHub Packages at one MinVer version. No per-package publish exists, no manual republish closure, and no `<Version>` in any csproj. `publish-package.yml` is a dry run only. A phase's "Published" task is closed by that train, never by a local `dotnet pack`.
- **Local verification** uses root `NuGet.Config`. Its `local-shared-kernel` source points at `./nupkgs`, and `packageSourceMapping` routes `SharedKernel.*` there. `dotnet pack … --output ./nupkgs`, with a temporary `NUGET_PACKAGES`, followed by restoring a `{domain}/consumer-verify/` harness or a sample, proves the packed dependency graph resolves. That is what `.github/workflows/verify.yml` does in CI. `eng/verify-packages.sh` checks that the packed set equals every packable csproj.

**Why:** an earlier note recorded `./nupkgs` as "the actual publish step — there is no real remote feed". That was true before P-572, and it is wrong now.

**How to apply:** for a consumer dependency-graph check, pack into `./nupkgs` at the repo root and restore the domain's `consumer-verify` harness. For a real release, hand off to devops-lead or the release train and never push packages by hand. XML documentation generation is configured centrally in `Directory.Build.props` (shipping library projects), not added per phase.
