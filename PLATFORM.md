# Platform.SharedKernel — Build, Release & Runtime Platform Brain

## What This File Is

The brain file of the `devops-lead` agent, and the single source of truth for how this repository **builds, versions, packs, publishes, containerizes, deploys, and configures itself**. It is the build/release peer of the root `CLAUDE.md`: that file owns what the code *is* (folder map, layering rules, package naming, "What Goes Where"); this file owns everything about how it *ships*.

Root `CLAUDE.md`'s Solution Format section already delegates here — *"No Directory.Build.props or NuGet references are managed here yet — see PLATFORM.md for build-wide configuration decisions"* — and nothing else claims this file.

**Ownership.**

| Owner | Owns |
| --- | --- |
| `devops-lead` (this file) | `.github/**`, `Directory.Build.props`/`.targets`, `Directory.Packages.props`, `global.json`, `NuGet.Config`, version config, `Dockerfile*`, `.dockerignore`, `deploy/**`, `build/**`, `eng/**`, and this file. Plus **narrow** `.csproj` edits — removing metadata that moved into `Directory.Build.props`, or adding a build/packaging property. Never a `PackageReference`, never a `ProjectReference`, never a code-facing property. |
| `arch-lead` | Root `CLAUDE.md`, root `state-map.md`. `devops-lead` never writes to either; genuinely cross-cutting rules go through `sync-brain`. |
| Each domain arch-planner | Its own `{NN}.Domain/CLAUDE.md` and `{NN}.Domain/state-map.md`. |
| Domain phase-implementers | Every `.cs` file in the repo. |
| `governance-arch-planner` | `00.Governance`'s authored artifacts — analyzer source, `.editorconfig`, CSharpier config, NetArchTest rule source, benchmark harnesses, git hooks. `devops-lead` **wires these into CI as build steps and required checks; it never authors, edits, tunes, or suppresses one.** |

**Reading discipline.** Standing sections describe the state **after** the most recent change — this is a living reference, not a history. History lives in `## Changelog`. In-flight and deferred work lives in `## DevOps Work Log`. Every entry under `## Decided Standards` is tagged `DECIDED` (implemented and verified on disk) or `PROPOSED` (research-backed but not yet built). **Never act on a `PROPOSED` standard as though it were already in place.**

---

## Current Baseline

> **Verified on disk 2026-07-20.** Honest inventory. Almost none of the platform infrastructure described further down exists yet. Re-verify before acting on any line here.

### Build configuration — nothing centralized

- **No `Directory.Build.props`.** **No `Directory.Build.targets`.** **No `Directory.Packages.props`.** **No `global.json`.** Nowhere in the repo.
- **Every production `.csproj` hand-duplicates its full NuGet metadata block** — roughly 28 property lines across ~40 projects. Duplicated: `TargetFramework` (`net10.0`), `ImplicitUsings`, `Nullable`, `PackageId`, `Version` **and** `PackageVersion` (both hardcoded, typically `1.0.0`), `Authors` + `Company` (`Gresta-Vertex-Labs`), `Product` (`Platform.SharedKernel`), `PackageLicenseExpression` (MIT), `PackageReadmeFile`, `RepositoryType`, `RepositoryUrl`, `PackageProjectUrl`, `Copyright`, `GenerateDocumentationFile`, `TreatWarningsAsErrors`, `IncludeSymbols`, `SymbolPackageFormat` (snupkg). Only `Description` and `PackageTags` genuinely vary.
- Versions are **hand-maintained literals in source**. There is no version derivation, no tag-driven versioning, and no tooling that would notice if two packages drifted.
- No SDK pin. The build uses whatever SDK the machine happens to have.
- Solution file is `Platform.SharedKernel.slnx` (.NET 10 XML format). **There is no `.sln`** — keep it that way; both in one directory makes a bare `dotnet build` error out.

### CI/CD — none

- **There is no `.github/` directory at all.** Zero workflows, no `CODEOWNERS`, no `dependabot.yml`, no `renovate.json`, no issue or PR templates, no ruleset JSON.
- No branch protection or ruleset configuration is tracked in the repo.
- Nothing runs on push or pull request. All verification is manual and local.
- `13.ServiceDefaults/consumer-verify/` exists as a real packaging smoke test (it asserts `HealthCheckService` resolves and `StartupGate.IsReady` defaults false through a real host composition) and **nothing currently runs it**.

### Packaging & feeds — local only

- `NuGet.Config` exists at root. It `<clear/>`s all sources, then adds a **local folder feed** `local-shared-kernel` → `./nupkgs`, plus nuget.org. `packageSourceMapping` routes `SharedKernel.*` to the local feed and `*` to nuget.org.
- **No package has ever been published to a real registry.** There is no registry target, no API key, no Trusted Publishing policy, and no publish workflow.
- Packed artifacts land in `./artifacts/nupkg/`. A `./nupkgs/` local feed folder also exists.
- The `<clear/>` + `packageSourceMapping` posture is **correct supply-chain hygiene already** — it is the only thing currently preventing a dependency-confusion attack on the unreserved `SharedKernel.` prefix, and it must survive any registry change.

### Containers & deployment — none

- **No `Dockerfile`, no `.dockerignore`, no `deploy/` folder, no K8s manifests, no Helm charts, no kustomize overlays.**
- No container registry target. No cluster. No image has ever been built from this repo.

### Test topology — relevant to every CI decision

- Test projects are **nested inside the project folder they test** (e.g. `08.Storage/SharedKernel.Storage.Abstractions/SharedKernel.Storage.Abstractions.Tests/`), never in a top-level `tests/`. Production `.csproj` files therefore carry hand-written `<Compile Remove="*.Tests/**" />`-style excludes.
- This has two direct build consequences: `dotnet pack` on the solution would pack the test projects unless `IsPackable` is conditioned, and a `.dockerignore` needs `**/*.Tests/` in a way a conventional layout would not.
- xUnit is the runner. **Several suites are Testcontainers-backed and require a Docker daemon** — MinIO (`08.Storage`), Redis (`02.Caching`), PostgreSQL (`06.Persistence`). These force a Linux CI runner: Windows and macOS GitHub-hosted runners cannot run Linux containers.

### Agent environment permissions

`.claude/settings.json` grants Edit/Write/Read/Glob/Grep/Agent, `dotnet build|run|test|watch|restore|clean|format|tool|ef`, and local-only git. It grants **no `gh`, no `kubectl`, no `helm`, no `docker build|push|login|buildx`, no `dotnet nuget push`, no `git push`, no `git tag`, no `trivy`/`cosign`/`syft`**. `settings.local.json` adds `dotnet pack` and a few `docker version|info|run|rm` grants, but it is gitignored and not portable.

Consequence: **authoring** workflow YAML, props files, Dockerfiles, and manifests is always available; **executing** release tooling is not. Verification will frequently be partial, and partial verification must be reported as `⚑`, never `●`.

---

## Decided Standards

> Each entry is tagged `DECIDED` (implemented and verified on disk) or `PROPOSED` (research-backed, not yet built). As of 2026-07-20 **everything here is `PROPOSED`** — no build infrastructure has been implemented. Versions and SHAs are dated; re-resolve at execution time.

### Versioning — MinVer, lockstep, single `v` tag prefix · **PROPOSED**

One version for the whole kernel: tag `v1.4.0` → all ~40 packages ship `1.4.0`. MinVer declared once as a `GlobalPackageReference`; `MinVerTagPrefix=v`, `MinVerMinimumMajorMinor=1.0`, `MinVerAutoIncrement=patch`.

*Rationale.* `dotnet pack` converts a `ProjectReference` into a `PackageReference` **at the referenced project's current version**. Under independent versioning, packing a package while a dependency sits at an untagged prerelease emits a dependency on a version that was never published — and with this repo's layering graph (`13.ServiceDefaults` may reference layers 01–12) that would require a bespoke per-dependency tag resolver. Lockstep makes the problem vanish. The accepted cost is publishing no-op version bumps for untouched packages. Secondary: MinVer height increments on every commit anywhere in a monorepo, so per-project prefixes still produce coupled prereleases; and consumers of a *shared kernel* want a coherent set, not a 40-row compatibility matrix.

*Escape hatch, documented and unused:* a package that must diverge later adds its own `MinVerTagPrefix` without changing tools.

*Hard CI requirement:* `fetch-depth: 0` on checkout. Without full tag history MinVer silently stamps `0.0.0-alpha.0` and a green build publishes garbage. Pair every release with a guard asserting packed version == tag.

### Package metadata centralization · **PROPOSED**

`Directory.Build.props` + `Directory.Build.targets` at root, one of each, never a per-domain copy (a nested props file stops MSBuild's upward walk). `PackageId` deleted from all ~40 projects — MSBuild defaults it to the project name, which already equals the intended package ID. `Version`/`PackageVersion` deleted — MinVer supplies both. Only `Description` and `PackageTags` stay per-project, enforced mechanically by an `SKPKG001`/`SKPKG002` error target in `.targets`. See `## Build Configuration Map`.

### Central Package Management, transitive pinning OFF for shipping projects · **PROPOSED**

`Directory.Packages.props` with `ManagePackageVersionsCentrally=true`, `CentralPackageVersionOverrideEnabled=false`, and `CentralPackageTransitivePinningEnabled=false` for shipping projects (**true only for test projects**).

*Rationale.* When a package is transitively pinned and then packed, NuGet promotes the pinned transitive into an explicit `<dependency>` in the emitted `.nuspec` — one pin silently rewrites the public dependency graph of every package that touches it, the exact opposite of this repo's layering discipline. For a shipping package with a vulnerable transitive, the correct fix is an explicit top-level `PackageReference` in that csproj (deliberate, reviewable) or upgrading the direct dependency.

*Migration risk is the collapse, not the feature.* Inline versions across ~40 files are almost certainly not identical today. Snapshot the resolved graph before, inventory every distinct inline version per package ID, take the highest of each conflict, strip `Version=`, re-restore, and diff. Only deliberate upward unifications are acceptable diffs.

### SDK pinning · **PROPOSED**

`global.json` pinning the 10.0.3xx feature band with `rollForward: latestPatch` and `allowPrerelease: false`. Pins emitted IL behavior and protects against a reported `.slnx` build-perf regression between SDK patches. `latestFeature` would silently move feature bands.

### CI topology — one `ci.yml`, split test lanes, one aggregator gate · **PROPOSED**

Jobs: `changes` (path filter) → `build-test` (unit lane) + `integration-docker` (Testcontainers lane) + `arch-tests` + `consumer-verify` + `actionlint` → **`ci-gate`**, which runs `if: always()`, inspects `toJSON(needs)`, fails on any `failure`/`cancelled`, treats `skipped` as acceptable, and is the **only required status check** alongside `pr-title`.

*Rationale.* If a path-filtered job is itself a required check, a docs-only PR never produces that check and the ruleset waits forever — the most common self-inflicted outage after adopting path filters. An always-running aggregator also means adding, renaming, or removing a CI job never requires editing the ruleset.

Runner is `ubuntu-latest`, forced: Docker Engine is preinstalled and Testcontainers needs no setup, while Windows/macOS hosted runners cannot run Linux containers at all. No OS matrix — it would be cost with no signal for a `net10.0`-only, container-dependent library set.

Standing hygiene: `permissions: {}` at workflow root widened per job; `concurrency` keyed on PR number with `cancel-in-progress` **only** for `pull_request` (cancelling a `merge_group` run drops the PR from the queue; cancelling a release leaves a partial package set); `merge_group` in the `on:` of every required check; `timeout-minutes` on every job; `persist-credentials: false` on every checkout; `paths-ignore` covering `**/*.md` and `.claude/**`.

Caching via `actions/cache` keyed on `hashFiles('Directory.Packages.props', 'global.json')` — **not** `setup-dotnet`'s `cache: true`, which requires `packages.lock.json` and hard-errors without it (40 lock files churning on every dependency change). CPM concentrating all versions into one file makes that single hash a near-perfect key.

**Never emit a bare `dotnet build`/`dotnet test`** — always pass `Platform.SharedKernel.slnx` explicitly.

### `NuGetAudit` escalation split · **PROPOSED**

.NET 10 defaults `NuGetAuditMode` to `all` (was `direct`). Combined with this repo's existing repo-wide `TreatWarningsAsErrors=true`, a newly-disclosed CVE in any transitive dependency **breaks every developer's build with no code change**. Defuse locally via `WarningsNotAsErrors` for `NU1901`–`NU1904`; escalate deliberately in CI with a dedicated `dotnet restore -warnaserror:NU1901,NU1902,NU1903,NU1904` step. Red CI is visible and ownable; 40 broken laptops is not.

### API-compat gating, in this order · **PROPOSED**

1. **`Microsoft.CodeAnalysis.PublicApiAnalyzers` first.** With `TreatWarningsAsErrors` already repo-wide, `RS0016` turns an undeclared public API into a PR build failure and makes the `PublicAPI.Unshipped.txt` diff a mandatory review artifact. Free given existing settings.
2. **SDK `PackageValidation` second, and only after `1.0.0` is live on a feed.** Baseline validation *downloads* the baseline package; setting `PackageValidationBaselineVersion` before anything is published fails restore/pack with NU1101-class errors. This is the single most common way teams break their build enabling the feature. `CompatibilitySuppressions.xml` then becomes the version-controlled breaking-change review artifact.

### Publish authentication — nuget.org Trusted Publishing (OIDC) · **PROPOSED, availability unconfirmed**

Exchanges a GitHub OIDC token for a single-use API key valid ~1 hour, eliminating stored credentials. Policy fields: Repository Owner `Gresta-Vertex-Labs`, Repository `platform-shared-kernel`, Workflow File **filename only** (`release.yml`, not the path), Environment `nuget-publish`.

Three constraints: `id-token: write` is mandatory or the OIDC request fails **silently** and yields no key; a policy on a private repo is provisional for 7 days and goes inactive without a publish in that window; org-owned policies deactivate if the creating user leaves the org.

**Rollout is gradual — if the option is not visible in the nuget.org account, it is not available yet.** Fallback: a glob-scoped (`SharedKernel.*`) API key with a short expiry, stored only in the protected environment.

Release trigger: tag push matching `v[0-9]+.[0-9]+.[0-9]+*`, gated by a `nuget-publish` GitHub Environment with required reviewers and a `v*` tag restriction, plus a tag ruleset restricting tag *creation* to admins. That combination is what prevents any contributor from publishing publicly by pushing a tag. Push both `.nupkg` and `.snupkg` with `--skip-duplicate`.

**Register the `SharedKernel.` prefix on nuget.org before first publish.** Until then `packageSourceMapping` is the only defense against someone registering `SharedKernel.Storage.S3` publicly first.

### Package signing — skip author signing · **PROPOSED**

Author signing requires a CA-issued cert chaining to a Windows-trusted root (self-issued is rejected), account registration, and HSM/Key Vault key protection in CI — while nuget.org already repository-signs every accepted package, providing tamper-evidence at consumption. Trusted Publishing plus SLSA provenance attestation delivers more marginal security for far less lifecycle burden. Sigstore for NuGet remains an open proposal, **not implemented** — do not plan around it.

### Container base image policy · **PROPOSED**

This repo ships **libraries and has no runtime service**, so it must not grow a service Dockerfile. What it should own is a versioned reference template under `deploy/reference/` that consuming service repos copy, plus this policy.

- JIT ASP.NET Core services: `mcr.microsoft.com/dotnet/aspnet:10.0.x-noble-chiseled-extra`. Chiseled = no shell (no `exec` probe, no `sh -c` preStop, no package manager to pivot through) and non-root by construction. `-extra` because EF Core/Npgsql culture handling and search date handling need ICU + tzdata — `InvariantGlobalization` is **not** safe for a multi-tenant SaaS doing locale-aware sorting.
- Native AOT workers: `runtime-deps:10.0.x-noble-chiseled-extra`, built with `sdk:10.0.xxx-noble-aot`. **Note the .NET 10 naming shift — the `-aot` suffix moved to the SDK images; verify whether a `runtime-deps` `-aot` tag still exists before using one.**
- **Do not default to Alpine.** ~40–47% smaller, but musl's allocator is materially slower under ASP.NET Core's thread-contention profile, native interop is far less tested on musl, and `$APP_UID` uniformity is lost. Chiseled captures most of the size win on glibc.
- Non-root user is `$APP_UID` = **1654**; port is **8080**, never 80 (that needs `CAP_NET_BIND_SERVICE`).
- **Pinning rule: patch tag in Dockerfiles, digest in production manifests.** `latest` is banned outright.
- `ENTRYPOINT` must be exec form — shell form makes a shell PID 1 that does not forward SIGTERM, and chiseled has no shell anyway.
- Private-feed credentials via BuildKit `--mount=type=secret`, never a build ARG (which lands in image history).

### Deploy packaging — Kustomize base + components, not Helm · **PROPOSED**

Services are homogeneous: same probe paths, same port, same UID, same OTLP env, same headless-service discovery. Templating exists for variability and there is almost none. A Kustomize base is real YAML at rest, so `kubeconform` validates it, IaC scanners read it, and reviewers review it without mentally running `helm template`. Mirror `components/` on the capability domains (`postgres-egress`, `redis-egress`, `s3-egress`, `search-egress`, `migrations-on-startup`) so the K8s surface is isomorphic to the package surface. `configMapGenerator`'s name-hash suffix gives rolling restart on config change for free. Tag the base independently (`k8s-base-vX.Y.Z`) so K8s changes do not force package version churn. Helm remains the right tool for third-party charts this platform *consumes*.

**Verify Kustomize `oci://` support in the current `kubectl` before using it; the pinned-git-ref form is the proven fallback.**

### Probe wiring — against what `13.ServiceDefaults` actually exposes · **PROPOSED**

As surveyed 2026-07-20, `MapDefaultHealthCheckEndpoints()` maps exactly two endpoints: `/health/live` (checks tagged `Live`, process-alive only, never dependency-coupled) and `/health/ready` (checks tagged `Ready`). `StartupGateHealthCheck` is registered under the `Ready` tag and `StartupGate.IsReady` defaults false until `MarkReady()`.

**There is no `/health/startup` endpoint.** So `startupProbe` and `readinessProbe` must both target `/health/ready` today. That works — the gate holds readiness at 503 through migrations — but it conflates two concerns and makes readiness re-evaluate the full dependency chain forever rather than only at startup. Adding a `HealthCheckTags.Startup` and a third mapping is a **hand-off to `servicedefaults-arch-planner`**, not a `devops-lead` change.

Field values: `startupProbe` `initialDelaySeconds: 5` / `periodSeconds: 5` / `failureThreshold: 60` (a 300s budget absorbing migrations — while a startupProbe runs, liveness and readiness are suspended, which is the entire point). `readinessProbe` `periodSeconds: 10` / `failureThreshold: 3`, no `initialDelaySeconds`. `livenessProbe` on `/health/live`, `periodSeconds: 15`, deliberately slower and looser. **Liveness must never point at a dependency-aware endpoint** — a Redis blip must not restart the whole fleet at once; the `Live`/`Ready` tag split enforces this architecturally and the YAML must not undermine it.

**Re-read the `13.ServiceDefaults` source before writing any probe path. Do not trust this paragraph.**

### Pod hardening · **PROPOSED**

`runAsNonRoot: true` with numeric `runAsUser: 1654` (K8s requires a numeric UID, not a name). `readOnlyRootFilesystem: true` **requires** emptyDir mounts at `/tmp` and `$HOME/.dotnet` — omit them and the pod crashloops with a non-obvious IO error, the single most common .NET-pod hardening failure. `capabilities.drop: [ALL]`, `allowPrivilegeEscalation: false`, `seccompProfile: RuntimeDefault`, `automountServiceAccountToken: false`. Memory limit paired with `DOTNET_GCHeapHardLimitPercent`; **no CPU limit** — CFS throttling on a latency-sensitive ASP.NET Core service produces a documented tail-latency failure where the ThreadPool reads stalls as starvation and injects more threads.

Graceful shutdown: .NET installs a SIGTERM handler and needs no `tini`/`dumb-init` as PID 1, given exec-form `ENTRYPOINT`. Endpoint removal and SIGTERM delivery happen **concurrently, not in order**, which is why deploys 502 without a preStop delay. Use the native `lifecycle.preStop.sleep` action (GA in K8s 1.34 — essential because chiseled has no `sleep` binary) and respect `terminationGracePeriodSeconds ≥ preStopSleep + HostOptions.ShutdownTimeout + 5`. The grace-period clock starts **before** preStop, not after — the most commonly misconfigured field in the spec.

### Configuration model · **PROPOSED**

- **`SectionName` → env-var transform is mechanical:** `SharedKernel:Storage:S3` ⇒ `SharedKernel__Storage__S3__*`. `:` is not a valid identifier character in Bash, so `__` is the only correct form on Linux containers. The platform's existing `public const string SectionName` convention translates with zero code change.
- **Precedence is list order only — last provider added wins.** There is no priority metadata. If a mounted-ConfigMap JSON layer is inserted, `AddEnvironmentVariables()` must be re-added afterwards or the ConfigMap silently outranks per-pod overrides.
- **`AddKeyPerFile("/secrets")` is the canonical secret-injection path.** Filename becomes the key, `__` in the filename becomes the delimiter. Mount as a **directory** — never `subPath`, which by hard K8s rule never receives updates.
- **`IOptions<T>` + `ValidateOnStart` is the platform default; hot-reload is deliberately not a platform capability.** `ValidateOnStart` fires once at `IHost.StartAsync()`; a ConfigMap edited later is never re-validated. `IOptionsSnapshot` turns a bad edit into per-request 500s; `IOptionsMonitor.OnChange` fires with unvalidated data. Config changes therefore ship via **rolling restart**, which re-runs the full validation gate and provides a rollback boundary. This preserves the platform's fail-at-`StartAsync`-never-at-first-use invariant exactly — hot-reload is fundamentally incompatible with it, because a reload has no startup to fail at.
- If a ConfigMap file watch is ever needed anyway: kubelet swaps a `..data` symlink atomically rather than modifying files, so `IN_MODIFY`/`IN_CLOSE_WRITE` never fire and `IN_DELETE_SELF` fires exactly once — naive watchers go permanently deaf. Fix is `DOTNET_USE_POLLING_FILE_WATCHER=true` (fixed 4s interval, not configurable); realistic propagation is 60–90s including kubelet sync. **Claims that modern .NET handles the symlink swap natively are unverified — assume it does not.**

### Secret store — External Secrets Operator · **PROPOSED, no cluster exists**

For a genuinely multi-cloud platform (Azure B2C + AWS S3 + Huawei OBS), ESO's provider breadth under one `ClusterSecretStore` CRD beats the CSI driver's per-cloud provider installs with separate `SecretProviderClass` dialects, and it composes perfectly with `AddKeyPerFile`.

**Trade-off stated explicitly rather than discovered in an audit: ESO writes real Kubernetes `Secret` objects into etcd**, which the CSI driver's tmpfs-only model avoids. Mitigate with etcd encryption-at-rest and tight RBAC on `get secrets`.

Rejected: **Sealed Secrets** — cluster lock-in is disqualifying across separate dev/staging/prod clusters, and a cluster rebuild without the controller key is unrecoverable. **SOPS+age** is kept only for bootstrap secrets (the ESO credentials themselves), being the only option that works before a cluster exists.

**ESO appears to have no native Huawei Cloud provider — verify before committing.**

### Workload identity over stored credentials · **PROPOSED**

AKS Workload Identity via a ServiceAccount annotation (`DefaultAzureCredential` picks it up with no code change); **EKS Pod Identity over IRSA** for new clusters (simpler trust policy, decoupled from the cluster OIDC issuer). Huawei CCE does support workload identity — a pod presents an OIDC ID token to IAM and receives temporary AK/SK valid 15 min–24 h, with the `policy` parameter currently applying specifically to OBS, which is exactly this platform's use case. **Whether Huawei IAM accepts an external AKS/EKS OIDC issuer is unverified**; assume static AK/SK if an OBS workload runs off-CCE.

**Known blocker to flag, not to fix here:** storage/persistence options types declare `AccessKeyId`/`SecretAccessKey` as **required**. Under workload identity those values are never supplied and a `[Required]` validator fails at `StartAsync()` on a correctly-configured credential-free pod — the fail-fast guarantee inverts and blocks the *more secure* configuration. Moving from "these fields are required" to "a credential source must be resolvable" is a cross-cutting design decision belonging to `arch-lead`, not to `devops-lead`.

### GitHub secrets layout · **PROPOSED**

Nothing at organization level, nothing at repository level, everything scoped to a protected **Environment**. Org secrets have the broadest blast radius, and with OIDC almost none should be needed. Note the timing difference: org/repo secrets are read when a run is **queued**; environment secrets when the referencing job **starts** — after any required-reviewer gate. Non-sensitive identifiers (client IDs, tenant IDs, nuget.org profile name) belong in `vars`, not `secrets`.

### Repository governance — rulesets, not classic branch protection · **PROPOSED**

Rulesets compose additively (only one classic rule ever applies, making overlaps non-deterministic), support an `evaluate` dry-run mode, org-level scope, audit insights, and are exportable JSON — so the policy itself becomes reviewable configuration under `.github/rulesets/`. Roll out in `evaluate` for a week before flipping to `active`.

`main` ruleset: restrict deletions and force pushes, require linear history, require a PR with 1 approval + code-owner review + stale-review dismissal + conversation resolution, `allowed_merge_methods: [squash]`, required checks `ci-gate` and `pr-title`, plus a merge queue.

Four interlocks that will otherwise bite:
1. Merge queue + linear history forces `merge_method: SQUASH` or `REBASE` — `MERGE` produces a commit the linear-history rule then rejects, and the queue jams.
2. **Every required check must also trigger on `merge_group`** or the queue stalls forever.
3. Set `strict_required_status_checks_policy: false` with a queue — "require branches up to date" is redundant and forces pointless rebases.
4. `required_signatures` breaks any bot pushing with a plain git client; API-created commits are auto-signed and pass, runner `git push` commits do not.

A second ruleset targets `refs/tags/**`, restricting creation to admins and forbidding deletion and update. A release tag must never be re-pointed — it is the publish trigger.

`CODEOWNERS`: **the LAST matching pattern wins.** Catch-all `*` at top, per-domain globs next, cross-cutting overrides at the bottom. `!` negation, `[a-z]` ranges, and `\#` are unsupported; emulate negation by listing a path with **no owner** after the broader rule. Owners must have write access — a team without repo access is silently ignored and `require_code_owner_review` becomes unsatisfiable. Add a CI job hitting the `codeowners/errors` API, because malformed lines are skipped silently.

### Commit-convention enforcement — PR title blocking, commit lint advisory · **PROPOSED**

The existing `/commit` skill already emits compliant Conventional Commits, and with squash merges **the PR title becomes the commit on `main`** — the only string release automation ever parses. So PR-title lint is the required blocking check and commit-message lint is advisory (`continue-on-error`). Enforcing commitlint on every branch commit actively fights an agent workflow producing many small WIP commits, and is pointless when squash discards them. Derive the `types`/`scopes` allowlists from the `/commit` skill's actual vocabulary, not spec defaults, and cross-reference the two files so they stay in lockstep. Use `pull_request`, **never** `pull_request_target`.

**Note:** `/commit`'s path→scope table currently has **no row for `.github/`**. Adding one is part of creating that directory, or commit grouping silently misclassifies.

### Dependency updates — Renovate for NuGet, Dependabot for Actions · **PROPOSED**

Renovate explicitly supports `Directory.Packages.props`; Dependabot has a documented open gap writing CPM transitive updates back to that exact file. Dependabot natively understands SHA-pinned actions and bumps the pin plus its trailing comment together. Key rule for a shared kernel: `major` updates require dashboard approval, because a major bump of a dependency appearing in a shipped `.nuspec` is a breaking change for every downstream microservice and must be a human decision tied to a kernel major.

### Supply-chain hardening · **PROPOSED**

Every third-party action pinned to a full 40-character commit SHA with a trailing `# vX.Y.Z` comment — never a floating tag, never `@main`; tag re-pointing is a live RCE vector. Enforce at org level too (Actions policy supports a custom allowlist plus a require-SHA-pinning toggle). `actionlint` as a CI job over `.github/workflows/**`. `pull_request_target` **prohibited** — split into an unprivileged `pull_request` build plus a `workflow_run` consumer if secrets are genuinely needed. CodeQL v4 with `build-mode: manual` (autobuild heuristics are unreliable against `.slnx` and a 40-project graph). `dependency-review-action` with `deny-licenses` for copyleft, since this is an MIT-licensed kernel. SBOM via Syft; cosign keyless signing with a pinned `certificate-identity-regexp` — `cosign verify` without pinning identity and issuer proves only "someone signed this".

### Release automation — release-please, manifest mode · **PROPOSED, optional**

Its input format is exactly what `/commit` already emits, and the staged release PR is the right control surface for 40 blast-radius packages — semantic-release's fire-on-merge model would ship a major of `SharedKernel.Core` to every consumer with no human in the loop. Changesets is structurally an npm tool and would discard the `/commit` investment.

**Composes with, does not replace, MinVer:** release-please owns the tag, MinVer reads it. Whether `extra-files` supports globs is unverified — assume ~40 explicit csproj entries, generate the config with a script, and add a CI check that every packable csproj appears in it.

---

## Build Configuration Map

> Ownership plan for the centralized build files. **None of these files exist yet** (2026-07-20). This section defines what each will own once introduced.

### `global.json` — SDK identity

`sdk.version` pinned to the current 10.0.3xx band, `rollForward: latestPatch`, `allowPrerelease: false`.

### `Directory.Build.props` — properties evaluated *before* the project body

| Group | Properties |
| --- | --- |
| Computed identity | `IsTestProject` (from `$(MSBuildProjectName.EndsWith('.Tests'))`) — every condition below keys off it |
| Language / compilation, **all** projects | `TargetFramework` (`net10.0`), `ImplicitUsings`, `Nullable`, `LangVersion`, `EnforceCodeStyleInBuild`, `AnalysisLevel`, `TreatWarningsAsErrors`, `WarningsNotAsErrors` (NU1901–NU1904, see the NuGetAudit standard) |
| Docs, shipping only | `GenerateDocumentationFile` — conditioned off for test projects |
| Nested-test exclusion, shipping only | `DefaultItemExcludes` += `*.Tests\**` — **one line replacing the hand-written `<Compile/EmbeddedResource/None Remove>` trio in ~40 files**, because `DefaultItemExcludes` feeds the SDK's default-item globs and covers all three item types |
| Determinism | `Deterministic`, `ContinuousIntegrationBuild` (conditioned on `'$(GITHUB_ACTIONS)' == 'true'` — it normalizes PDB source paths and setting it locally breaks IDE debugging), `EmbedUntrackedSources` (so `[LoggerMessage]`-generated code is steppable), `DebugType portable`, `PublishRepositoryUrl` |
| Packaging identity, shipping only | `IsPackable`, `Authors`, `Company`, `Product`, `Copyright`, `PackageLicenseExpression`, `PackageReadmeFile` + the `<None Include="README.md" Pack="true">` item (with an `Exists()` guard), `RepositoryType`, `RepositoryUrl`, `PackageProjectUrl`, `IncludeSymbols`, `SymbolPackageFormat`, `PackageOutputPath` |
| Non-shipping | `IsPackable=false`, `IsPublishable=false`, `GeneratePackageOnBuild=false` for `.Tests` |
| Versioning | `MinVerTagPrefix`, `MinVerMinimumMajorMinor`, `MinVerAutoIncrement`, `MinVerDefaultPreReleaseIdentifiers` |
| Audit | `NuGetAudit`, `NuGetAuditMode`, `NuGetAuditLevel` |

**Source Link needs no package** — it has been in the SDK and on by default since .NET 8. Do **not** add `Microsoft.SourceLink.GitHub`; it is legacy and redundant.

### `Directory.Build.targets` — evaluated *after* the project body

Guard rails that must observe what the csproj set:

- `SKPKG001` — a packable project with an empty `Description` fails the pack.
- `SKPKG002` — a packable project with empty `PackageTags` fails the pack.
- `SKPKG003` — a packable project with no adjacent `README.md` fails the pack.
- `SKPKG004` — anything named `*.Tests` fails `Pack` outright, even if a future csproj sets `IsPackable=true` by hand.

These are the mechanical enforcement of "`Description`/`PackageTags` stay per-project".

### `Directory.Packages.props` — one version per package ID, repo-wide

`ManagePackageVersionsCentrally=true`; `CentralPackageTransitivePinningEnabled=false` (shipping) / `true` (tests, set in `Directory.Build.props`); `CentralPackageVersionOverrideEnabled=false` so nobody drifts without a deliberate edit here. MinVer declared as a `GlobalPackageReference` (applies everywhere, implicitly `PrivateAssets=all`, never appears in any `.nuspec`).

Note: `NU1507` fires under CPM when more than one source is configured without `packageSourceMapping`. This repo's `NuGet.Config` **already has mapping**, so it is compliant ahead of the curve.

### Deleted from every production `.csproj`

`PackageId` (defaults to the project name, which already matches), `Version`, `PackageVersion`, and every property centralized above — roughly 28 property lines and 3 item groups per file, on the order of a thousand lines of duplicated XML across the repo.

### Retained per-project

| Property | Why |
| --- | --- |
| `Description` | The package's semantic identity; NuGet quality gates want it unique |
| `PackageTags` | Genuinely varies per package |
| `PackageReference` / `ProjectReference` items | **Owned by the domain agents. `devops-lead` never edits these.** |
| Any deliberate per-project compiler property a domain agent set | Not uniform; centralizing would silently override a deliberate choice |

### Migration hazard specific to this repo

Because `*.Tests` projects are **nested inside** production project folders, MSBuild's upward walk from a test project passes through the production directory (which contains no props file) and correctly lands on the root one. That works today. But adding a per-domain `Directory.Build.props` would **stop the walk** and silently drop the root file unless explicitly re-imported via `$([MSBuild]::GetPathOfFileAbove(...))`. **Keep exactly one `Directory.Build.props`, one `.targets`, and one `Directory.Packages.props`, all at the root.**

---

## Open Questions

> Genuine unknowns that only the user can resolve. Each blocks or reshapes a `PROPOSED` standard above. Do not guess at these; ask.

1. **Is there a real NuGet registry target, and which one?** Today packages resolve only from a local `./nupkgs` folder feed and have never been published. nuget.org (public), GitHub Packages (org-private), or a private feed are materially different answers — they change the auth model, the `NuGet.Config` mapping, and whether consuming repos need a PAT.
2. **Is the repository public or private?** This decides whether GitHub secret scanning + push protection is free or a per-committer SKU, and whether a Trusted Publishing policy gets the 7-day provisional window.
3. **Is Trusted Publishing visible in the Gresta-Vertex-Labs nuget.org account?** Rollout is gradual. If not available, the scoped-API-key fallback must be designed instead.
4. **Which cloud is primary?** Azure B2C, AWS S3, and Huawei OBS all appear in the dependency graph. Workload-identity design, the ESO `ClusterSecretStore` topology, and the OIDC federation targets all follow from the answer.
5. **Does a Kubernetes cluster exist yet, and what version?** Several recommendations have hard version floors — the native `lifecycle.preStop.sleep` action is GA at K8s 1.34, and `trafficDistribution` needs 1.33+. With no cluster, all K8s work is template authoring with no possible `--dry-run=server` verification.
6. **Do the GitHub teams a `CODEOWNERS` design would reference actually exist, with write access?** A team without repo access is silently ignored and makes `require_code_owner_review` permanently unsatisfiable.
7. **Should this repo publish reusable workflows for consuming microservice repos?** That changes `.github/workflows/` from private plumbing into a versioned public contract with its own compatibility obligations, and requires org Actions access to be opened up.
8. **What is the intended `1.0.0` moment?** `PackageValidation` baselining, prefix reservation, and the first Trusted Publishing exchange all key off a real first publish and cannot be armed before it.
9. **Should the agent's `settings.json` allowlist be extended?** Currently no `gh`, `docker build`, `kubectl`, `helm`, `dotnet nuget push`, `git push`, or `git tag` grant exists, so release-side verification cannot run locally and will be reported as unproven.

---

## DevOps Work Log

> States: `○` Pending · `◐` In Progress · `●` Done & Verified · `⚑` Blocked or Unverified
> `●` requires an **observed passing verification**. Anything shipped but unproven is `⚑` with a `### Deferred / Blocked` entry.
> IDs are `DV-NNN`, monotonically increasing — read the table and take max+1, never reuse. Completed rows stay permanently; the log is cumulative.
> `Class` uses the seven work classes verbatim: CI Workflow · Packaging & Versioning · Feeds & Publishing · Container · K8s / Deploy · Config & Secrets · Repo Governance.
> This log never contains phase definitions for another domain. Cross-domain work is a named hand-off, never a phase written into someone else's state-map.

| ID | Item | Class | Artifacts | Verified By | State |
|----|------|-------|-----------|-------------|:-----:|
| — | *No work executed yet. This file was seeded 2026-07-20; every standard above is `PROPOSED`.* | — | — | — | — |

### Deferred / Blocked

*(none yet)*

---

## Changelog

> Maintained by the `devops-lead` agent. One line per significant change.

- [2026-07-20] File created and seeded — `devops-lead` agent introduced as the build/release peer of `arch-lead`, with `PLATFORM.md` established as its brain file per root `CLAUDE.md`'s existing delegation. Recorded an honest `## Current Baseline` (no `.github/`, no `Directory.Build.props`/`.targets`/`Directory.Packages.props`/`global.json`, no containers or manifests, local-folder-feed-only `NuGet.Config`, ~28 duplicated metadata properties across ~40 csproj files, Testcontainers-backed suites requiring a Linux CI runner, `consumer-verify` harness existing but unwired), plus `PROPOSED` standards for versioning (MinVer lockstep), metadata centralization, CPM with transitive pinning off for shipping projects, CI topology with an always-running aggregator gate, Trusted Publishing, chiseled base images, Kustomize deploy packaging, ESO secrets, and rulesets-based repo governance. Nothing implemented — every standard awaits execution and the `## Open Questions` answers (devops-lead, initial seed)
