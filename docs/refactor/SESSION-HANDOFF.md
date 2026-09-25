# WO-086 Foundation Refactor — Session Handoff

> **For the next Claude session.** Read this file first, then follow "How to resume". Everything you need is
> linked from here. Last updated: 2026-09-25, after P-565 (`8e64d774`) and P-569 (`1064694a`) were committed.

## 1. Where the plan lives

| What | Where | Role |
| --- | --- | --- |
| **The plan (authoritative spec)** | [`docs/refactor/FOUNDATION-PLAN.md`](FOUNDATION-PLAN.md) | Full target architecture, tiers, contracts, and Steps 0–15 in detail. Supersedes the root `CLAUDE.md` "Layering Rules" section until P-575 rewrites it. |
| **Progress tracker** | root [`state-map.md`](../../state-map.md), section **WO-086** (search `P-562`) | One row per phase P-562…P-578 with `●` done / `○` pending. Mark a phase `●` when it's finished. |
| **Baseline measurements** | [`docs/refactor/baseline.md`](baseline.md) | Test counts and warning counts measured before the refactor started. |
| **Tier-violation baseline** | [`eng/tier-baseline.txt`](../../eng/tier-baseline.txt) | Known violations, downgraded to warnings. Each step deletes the entries it fixes, and the program ends with this file empty. |
| **Tier enforcement** | `eng/SharedKernelTiers.targets` (SKTIER000–005), `<SharedKernelTier>` in every csproj, `DependencyGraphRulesTests` in `00.Governance` | Build-time enforcement of the tier rules. |
| Copy of the plan outside the repo | `C:\Users\dincd\.claude\plans\deep-dreaming-dawn.md` | Same content as FOUNDATION-PLAN.md. Use the repo copy. |

## 2. Decisions already made by the user (do not re-ask)

1. Keep the `00`–`20` folders. Folder numbers stop meaning layers; each csproj declares a **tier** (Foundation, Model, Abstractions, Adapter, Host, Testing, Tooling), and the build enforces it.
2. `TenantId` is a `readonly record struct` over a non-empty `Guid`; "no tenant" is `TenantId?` = `null`, never `Guid.Empty`.
3. MediatR goes behind a kernel-owned mediator abstraction. MediatR (pinned at 12.4.1, the last MIT release) may be referenced **only** by `SharedKernel.Application.Mediator.MediatR`.
4. Release train: one `v*` tag packs and publishes **every** package at one version. No manual per-package republish closures.
5. Scope is the full foundation: all findings, the four defects, governance, CI, docs and agents.
6. Breaking changes are fine; nothing is in production use.

**Still requires asking the user:**
- The exact release tag (P-577).
- Retiring or deprecating old package IDs on GitHub Packages (P-578). This is outward-facing and irreversible.

## 3. Status

| Phase | Step | Status |
| --- | --- | --- |
| P-562 | 0 — Preparation (branch, plan, baseline) | ● done |
| P-563 | 1 — Tier enforcement infrastructure | ● done (`9f508d8f`) |
| P-564 | 2 — `SharedKernel.Execution`; `Application.Abstractions` deleted | ● done (`053e5612`) |
| P-565 | 3 — Tenant and caller unification | ● done (`8e64d774`) |
| P-569 | 7 — `IReadinessProbe` contract, collapse probe-only ServiceDefaults packages | ● done (`1064694a`) |
| P-566 | 4 — Correlation and context propagation (fixes defects 1, 3, 4) | ○ **next** |
| P-567 | 5 — Application contracts and mediator abstraction | ○ |
| P-570 | 8 — Optional-dependency satellites | ○ (parallel with P-567) |
| P-568 | 6 — Unified idempotency abstractions | ○ |
| P-571 | 9 — Per-capability `*.Testing` packages | ○ |
| P-572 | 10 — Release train and CI | ○ |
| P-573 | 11 — Samples as the reference architecture | ○ |
| P-574 | 12 — Governance cleanup (tier baseline empty, SKTIER becomes an error) | ○ |
| P-575 | 13 — Documentation (root/domain CLAUDE.md, READMEs, PLATFORM.md, MIGRATION.md) | ○ |
| P-576 | 14 — Agents (`.claude/agents/*`) and commands (`.claude/commands/*`) | ○ |
| P-577 | 15a — First release train (**ask the user for the tag**) | ○ |
| P-578 | 15b — Retire old package IDs (**ask the user first**) | ○ |

**Order:** 3 & 7 in parallel → 4 → 5 & 8 in parallel → 6 → 9 → 10 → 11 → 12 → 13 → 14 → 15.
- Step 6 needs Step 4 (the accessor) and Step 5 (the pipeline).
- Step 9 needs the final contracts from Steps 5–8.
- Docs and agents come last because they describe the final state.

### What exists after P-564 (use it; don't recreate it)
`01.Core/SharedKernel.Execution` (Foundation tier, references Primitives only):
- `SharedKernel.Execution.Context`: `IRequestContext` (with `CorrelationId` defaulting to `null`), `ActorKind`, `SystemRequestContext(IEnumerable<string> permissions, string identity = "system", Guid? tenantId = null)`, `AnonymousRequestContext`, `RequestContextScope.Begin(ctx)` (AsyncLocal; disposing restores the previous context, exactly once), and `IRequestContextAccessor` / `RequestContextAccessor`.
- `SharedKernel.Execution.Transactions`: `IUnitOfWork`, `CommitOutcomeUnknownException`, `TransactionRolledBackException`.
- `SharedKernel.Execution.Auditing`: `IAuditTrailWriter`, `AuditEntry`, `AuditOutcome`.
- `SharedKernel.Execution.Tenancy`: `TenantId` (rejects `Guid.Empty`, "D" string form, JSON converter), `TenantScope` (`Global` = default, `For`, `FromNullable`).

### What P-565 and P-569 added (2026-09-25)
**P-565 — tenant and caller unification:**
- `IRequestContext.TenantId` is `TenantId?`. `IUserContext` uses `ActorKind` and `TenantId?`. `IdentityKind`, `ITenantProvider`, `UserContextTenantProvider`, `AmbientTenantProvider`, Messaging's `ITenantContextAccessor`, `TenantBaggageKeys`, the four local `TenantScope` copies, `FakeTenantProvider`/`StaticTenantProvider` and the idempotency stores' accessor startup validator are deleted (defect 2 fixed by construction).
- `IHasTenant`/`Tenanted*` use `TenantId`. EF maps it with a value converter; Dapper has `TenantIdTypeHandler`. Stored formats (RLS setting, encryption key ids and associated data) are byte-identical.
- **Design decision to review:** `AddSharedKernelRequestContext()` registers `IRequestContext` as **transient** = `RequestContextScope.Current ?? scoped SecurityRequestContext`, plus `IRequestContextAccessor`. MultiTenancy middleware, the SignalR hub filter and the gRPC tenant interceptor open a `RequestContextScope`.
- Deliberately left: `04.Contracts` `EventEnvelope.TenantId` stays `Guid?` (wire contract, Primitives-only); `DataPrivacy` `DataSubjectRequest.TenantId` stays `string?`; governance names/docs mentioning `ITenantProvider` → P-574; Communication Rest/Grpc still use `IHttpContextAccessor` → P-566. Until P-566, an HTTP request without the MultiTenancy middleware gives the idempotency stores no tenant.

**P-569 — readiness probes:**
- `SharedKernel.Primitives.Health`: `IReadinessProbe`, `ReadinessReport`, `ReadinessStatus`, `AddReadinessProbe<T>()` / `AddReadinessProbe(factory)` (one per target), `GetRequiredReadinessProbe(name)`. Probe constructors must be cheap; resolve clients inside `ProbeAsync`.
- Probes: `messaging`, `redis`, `cache`, `encryption-key-provider`, `field-encryption`, `audit-sealing`, `storage-{store}`, `search-{provider}-{index}`, `vector-store-{provider}-{collection}`, `workflows`, `scheduler`. The old probe interfaces and `*ReadinessHealthCheck` adapters are gone. Audit lag limit is now `AuditSealerOptions.MaxReadyLag`.
- `healthChecks.AddSharedKernelReadiness()` in the ServiceDefaults base maps every probe to a `ready` check. The base now references Foundation-tier packages only (`CompositionBaseIsolationTests`).
- Deleted packages: `ServiceDefaults.{AI, Caching, Caching.Redis, Messaging, Scheduling, Search, Storage, Workflows.Temporal, Cryptography.KeyVault}`. Kept: `.Persistence`, `.Security`, `.Security.Mtls`, `.Configuration.KeyVault`, `.Localization`. The 13→17/13→19 grants and their rules are gone.

### Known leftovers deliberately deferred to P-575 (docs)
- Domain `CLAUDE.md`/README text still mentions `SharedKernel.Application.Abstractions`: `05.Application/CLAUDE.md`, `00.Governance/CLAUDE.md`, the root `CLAUDE.md` Folder Map row 05, and the Hard rule about the 07→Application.Abstractions grant.
- A comment in `samples/ShippingApi/ShippingApi.csproj` still names Application.Abstractions.
- An empty folder `05.Application/SharedKernel.Application.Abstractions` may remain on disk (locked by the OS). Git ignores it, so it is harmless; delete it if possible.
- READMEs/CLAUDE.md still naming the deleted ServiceDefaults packages, `Add*ReadinessCheck` methods, probe interfaces, `ITenantProvider`, `IdentityKind` or the local `TenantScope` copies (13.ServiceDefaults, 08.Storage, 09.Search, 06 Auditing/Encryption, CatalogApi, 12.Security …).

### Remaining tier-baseline entries (`eng/tier-baseline.txt`)
```
SharedKernel.Application->package:MediatR                          ← removed by P-567
SharedKernel.Idempotency.EfCore->SharedKernel.Application.Behaviors ← removed by P-568
SharedKernel.Idempotency.Redis->SharedKernel.Application.Behaviors  ← removed by P-568
```

## 4. How to resume

1. `git checkout refactor/wo-086-foundation` and `git log --oneline -5`. The newest commit should be `053e5612` (or later).
2. Read [`FOUNDATION-PLAN.md`](FOUNDATION-PLAN.md): the "Target architecture" section plus the section for the step you're about to do.
3. Start **P-566** (Step 4). Then P-567 and P-570 can run in parallel. Each step's full task list is in FOUNDATION-PLAN.md. Parallel phases worked well in `C:\wt\<phase>` worktrees on `wo086/<phase>` branches, rebased onto each other afterwards; the only conflicts were additive `PublicAPI.Unshipped.txt` hunks (keep both sides).
4. For each step:
   1. Implement it directly, or with `general-purpose` agents given the plan as the spec. **Do not use the domain `*-phase-implementer` agents or domain brains yet**: they still describe the old numbered-layer rules until P-576.
   2. Delete any `eng/tier-baseline.txt` entries the step fixes.
   3. Verify it (section 5).
   4. Mark the phase `●` in root `state-map.md`.
   5. Commit one commit per phase: `refactor(<scope>): <summary> (P-5xx)`, ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
5. Update section 3 of this file after every phase, so the next session always starts from the truth.

## 5. Verification per phase

Run these from the repo root in the Bash tool. Build output is in Turkish: `Hata` = errors, `Uyarı` = warnings, `Başarılı!` = passed, `Başarısız!` = failed.

```bash
export NUGET_PACKAGES=/c/wt/nuget-wo086
dotnet build Platform.SharedKernel.slnx -c Release          # must be 0 errors; no new warnings
dotnet test  Platform.SharedKernel.Unit.slnf -c Release --no-build
dotnet test  Platform.SharedKernel.Integration.slnf -c Release --no-build   # Docker; for persistence/messaging/caching/idempotency steps
```

Counts after P-565 + P-569 (use these to spot regressions):
- **Build:** 0 errors, 38 warnings (same codes as the baseline).
- **Unit:** 57 assemblies / 7,488 tests (9 assemblies left with the deleted ServiceDefaults packages).
- **Architecture:** 364 tests.
- **Integration:** 21 assemblies / 2,907 tests.
- **Known flakes under full-suite load** (re-run alone before treating as a regression): `MeilisearchContainerFixtureTests`, `CacheLevelMetricsTests.SecondReadOnTheSameNode_IsAnL1Hit`, Redis socket errors in `Idempotency.Redis.Tests`, two `Messaging.MassTransit.Tests`, one `Testing.SelfTests`.
- **Docker** must be running for Integration and for `ServiceDefaults.Persistence.Tests`. Start Docker Desktop first; it was down at the start of the 2026-09-25 session.

Packaging-verify is for steps that change packages (see FOUNDATION-PLAN.md "Verification"):
1. Run `dotnet pack` to a local folder with a temporary `NUGET_PACKAGES`.
2. Restore and run the consumer-verify harnesses and the samples.

## 6. Environment gotchas (learned the hard way)

- **Windows MAX_PATH:** a test-assembly path `…\{Name}\{Name}.Tests\obj\Release\net10.0\{Name}.Tests.dll` must be ≤ 245 characters at `C:\Github\platform-shared-kernel`. Check every new or renamed package name before scaffolding it.
- **Parallel agents:** `isolation: "worktree"` fails on MAX_PATH. Create short-path worktrees under `C:\wt\…` manually.
- **Python is not installed.** Script-based file edits silently do nothing. Use the Edit/Write tools.
- **Never use PowerShell 5.1 `Get-Content`/`Set-Content` on repo files.** They garble UTF-8.
- **ripgrep has no look-behind.** Use `[^\w.]Prefix\.` style patterns instead.
- **`git rm` refuses modified files.** Use `git rm -qf`.
- **Don't chain destructive commands after one that may fail.**
- **Central package management:** every new `PackageReference` needs a `PackageVersion` in `Directory.Packages.props`. Every new project must be added to `Platform.SharedKernel.slnx` and to the right `.slnf` (Unit or Integration).
- **Commits:** commit only per finished phase on this branch; never push without asking.

## 7. Definition of done for WO-086

- `eng/tier-baseline.txt` is empty, and `SKTIER*` diagnostics are errors.
- `grep -r MediatR` over production code finds only `SharedKernel.Application.Mediator.MediatR`.
- No production csproj outside the Host tier references `Microsoft.AspNetCore.Http`.
- End-to-end tests prove that correlation id, tenant and idempotency key survive HTTP→REST/gRPC→bus→consumer→job.
- Docs, agents and commands contain no removed type or package names.
- A clean consumer project restores every package from GitHub Packages at the release-train version.
- WO-086 is closed in `state-map.md`, and this handoff file is archived or deleted.
