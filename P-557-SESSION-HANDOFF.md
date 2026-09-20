# P-557 — Session Handoff (06.Persistence pre-first-publish pass)

**Session date:** 2026-09-19 → 2026-09-21
**Branch:** `main` · **Commits:** 50, from `0a498d7` (exclusive) to `HEAD`
**State:** all work committed and pushed. **Nothing is published.**

> **Who this is for:** the next AI session continuing work on `06.Persistence`. Read this first, then
> `06.Persistence/state-map.md` (phase `SK.06.P557`) for the task-level record and
> `06.Persistence/CLAUDE.md` for current architecture. This file is a point-in-time session summary —
> it will go stale. The state-map and the brain files are the living record; when they disagree with
> this file, **they win**.

---

## 1. What happened in one paragraph

`06.Persistence` was taken through a user-directed, full gold-standard pass before its first publish.
The domain was split from 4 packages into 7, its upward references to `05.Application.Behaviors` and
`12.Security` were replaced with local seams, and it gained a hash-chained audit ledger, a redesigned
field-encryption package, PostgreSQL row-level security, tenant-safe Dapper services and advisory
locks. Seven build waves were followed by **two independent adversarial reviews**, which found
defects the waves' own tests structurally could not catch — because each wave had tested its half
against a stub of the other. Four remediation waves closed every Critical and High finding, then a
cleanup wave and a documentation wave finished the pass.

## 2. Verification state at handoff

| Check | Result |
|---|---|
| `dotnet build Platform.SharedKernel.slnx -c Release` | Green **except 4 pre-existing `MSB3030`** errors |
| `dotnet test Platform.SharedKernel.slnx -c Release` | **10,098 passing**, 2 skipped, 83 test projects |
| Working tree | Clean |

**The 4 MSB3030 errors are the baseline, not a regression.** They are Windows path-length failures in
`SharedKernel.ServiceDefaults.Configuration.KeyVault.Tests` and
`SharedKernel.ServiceDefaults.Cryptography.KeyVault.Tests` (2 errors each). They predate this session.
Do not "fix" them as part of persistence work.

**Flaky container suites.** Under whole-solution load these intermittently fail and pass in isolation.
Always re-run a container failure alone before treating it as real:
`SharedKernel.Idempotency.Redis.Tests` (Redis script timeout), `SharedKernel.Messaging.MassTransit.Tests`
(RabbitMQ delivery), `SharedKernel.Caching.FusionCache.Tests`, and
`SharedKernel.Persistence.EfCore.Auditing.Tests`. The auditing one is worth understanding rather than
dismissing: under heavy concurrent writes to a single chain, the sequence-allocation retry loop
exhausts its 10 attempts and the write fails loudly with a unique-constraint violation. That is the
intended design (fail rather than fork the chain), but it is a real contention characteristic.

---

## 3. Package impact

### 3a. NEW — never published (7)

The whole `06.Persistence` domain. All 7 are packable, all have `PublicAPI.Shipped.txt` empty.

| Package | What it is |
|---|---|
| `SharedKernel.Persistence.Abstractions` | Contracts only: repositories, unit of work, audit ledger, the actor/tenant/lock seams |
| `SharedKernel.Persistence.EfCore` | EF Core implementation, interceptors, conventions, `PersistenceContextDependencies` |
| `SharedKernel.Persistence.EfCore.Auditing` | **New package.** Hash-chained append-only audit ledger |
| `SharedKernel.Persistence.EfCore.Encryption` | **New package.** Field encryption, blind indexes, resumable key rotation |
| `SharedKernel.Persistence.Npgsql` | **New package.** Npgsql-only — **no EF Core reference**. Data source, connection factory, tenant session binder, advisory locks |
| `SharedKernel.Persistence.PostgreSQL` | EF Core + Npgsql: conventions, row-level security, jsonb, pgvector |
| `SharedKernel.Persistence.Dapper` | Read/command services incl. tenant-safe variants. References only `.Abstractions` |

### 3b. ALREADY PUBLISHED — shipped code genuinely changed (2)

| Package | Change | API impact |
|---|---|---|
| `SharedKernel.Domain` | `Money` gained a private EF Core complex-type binding constructor + stored-precision rule | Private members — no public API change, but the assembly differs |
| `SharedKernel.Application.Behaviors` | New optional transactional seam (`ITransactionalUnitOfWork`, `IPersistenceTransaction`); `TransactionBehavior` opens a real transaction when the seam is present | **Public API addition** (additive; a consumer registering only the plain `IUnitOfWork` is unaffected) |

### 3c. Changed but ships nothing today

- `SharedKernel.ServiceDefaults.Persistence`, `SharedKernel.ServiceDefaults`, `SharedKernel.Analyzers` —
  real changes, but **`13.ServiceDefaults` and `00.Governance` are not published**.
- `SharedKernel.MultiTenancy` — 6 production files touched, **verified comment/whitespace only**; the
  assembly is byte-equivalent. Needs no republish on its own account.
- `SharedKernel.Testing` and `SharedKernel.Persistence.ConsumerVerify` — `IsPackable=false`.

### 3d. MUST BE PUBLISHED — 16 packages

7 new plus **9 republished at the same version height**. MinVer stamps dependency versions at build
height, so publishing anything means republishing its entire already-published dependency closure, or
the packed `.nuspec` will not resolve against the feed. This is the same mechanic P-556 hit.

The republish set falls out of the real project-reference graph:

| Why it is in the set | Packages |
|---|---|
| `06.Persistence` closure | `Primitives`, `Core`, `Configuration`, `Contracts`, `Domain`, `Cryptography` |
| Application chain | `Application`, `Application.Behaviors`, `Application.Behaviors.Caching` |

`Cryptography` enters via `EfCore.Auditing`/`EfCore.Encryption`. `Application.Behaviors.Caching` enters
not because it changed but because it depends on `Application.Behaviors`, which did.

**Publish order (dependency order matters — the gate step does a real restore):**

```
Primitives → Core, Configuration → Contracts, Domain → Cryptography
           → Application → Application.Behaviors → Application.Behaviors.Caching
           → Persistence.Abstractions → Persistence.EfCore → Persistence.Npgsql
           → Persistence.EfCore.Auditing, .EfCore.Encryption, .PostgreSQL, .Dapper
```

**How publishing works here:** the `publish-package.yml` workflow ("Publish Single Package") is a
`workflow_dispatch` taking `project`, `version`, `dry_run` (defaults to `true`). It has a gate step
that asserts every `SharedKernel.*` dependency already exists on the feed. There are no `v*` tags;
versions are `1.0.0-alpha.0.<MinVer height>`.

---

## 4. Do these BEFORE publishing

1. **Pack + consumer-verify against the real feed.** The 7 packages have only ever been verified
   against a local folder feed. That is not the same test. `dotnet pack` surfaces defects
   (`NU5039`, RS0026/RS0027) that `dotnet build` does not — W7 found 3 packages with no README at all
   this way.
2. **Audit chain-key rotation is still deferred.** Rotating `HmacKeyBase64` today makes the entire
   history report as tampered; changing `KeyId` makes it report "key id not known". A routine rotation
   is therefore indistinguishable from a breach. The hash format is unpublished right now, so fixing
   it is free — afterwards it is a migration. **This is the highest-value pre-publish item left.**
3. Decide whether `SharedKernel.Application.Behaviors`' public API addition warrants any extra review,
   since it is a published package.

---

## 5. Invariants a future maintainer must not break

- **A derived context takes exactly one `PersistenceContextDependencies` parameter.** The old
  9-parameter constructor let a consumer compile into a silently unprotected state — tenant write
  guard, RLS, audit guard and exception classifiers all lost, with reads still filtered so nothing
  looked wrong. Do not reintroduce per-capability constructor parameters.
- **An audited command requires a real ambient transaction.** `EfAuditTrailWriter` throws on a
  `Succeeded` outcome when `IAmbientDbTransaction.Current` is null, rather than standalone-committing
  an attestation that can outlive a failed business write. The happy path needs
  `.WithTransactionalUnitOfWork()` **and** `13.ServiceDefaults.Persistence`'s
  `.WithApplicationTransactionBehavior()`.
- **`.WithTransientFaultRetry()` cannot be combined with `.WithTransactionalUnitOfWork()`** — rejected
  at `Build()`, plus a startup check reading the live execution strategy so retry enabled directly
  inside `configureDb` is caught too. Use `ExecuteInTransactionAsync`, which is retry-safe.
- **Query filters are named and every production `IgnoreQueryFilters` is selective.** A bare
  `IgnoreQueryFilters()` drops the tenant filter along with soft-delete. There are none in production
  code; keep it that way.
- **Bulk setters fail closed.** A setter the inspector cannot resolve to a property name is rejected,
  because `EF.Property<T>(x, "TenantId")` reports as unresolvable while targeting a protected column.
- **Encryption guards cover complex types.** Any traversal looking for `.Encrypt(...)` annotations
  needs both `GetProperties()` **and** `GetComplexProperties()`. Missing the second loop is exactly
  how a silent-plaintext path was introduced once already.
- **`06.Persistence` references only `01`–`05`.** Now locked by an architecture test in
  `00.Governance` (`PersistenceLayeringRulesTests`) covering both compiled assemblies and a source
  scan of every `.cs` file, tests included.

## 6. Known limitations, deliberately open

- **H8 residual:** the advisory-lock self-deadlock no longer hangs (the lock takes a `timeout`,
  translates `55P03` to `TimeoutException`), but a `Succeeded` + `Failed` write on the same chain in
  one transaction still cannot both succeed — it now fails fast instead.
- **~95 low-confidence indentation candidates** in `.Tests` projects were deliberately left alone
  after the tag-stripping-script cleanup; the false-positive rate did not justify hand-triage. 103
  genuinely damaged sites were fixed.
- Encryption rotation does not support composite primary keys or `byte[]` properties (both guarded at
  model build, not at first call).

## 7. Working notes that will save you time

- **Docker must be running** for the Postgres/Testcontainers suites. Without it ~180 tests in
  `06.Persistence` fail as infrastructure errors, not real failures.
- **Build output is Turkish** on this machine (`Hata` = error, `Başarısız` = failed,
  `Başarılı` = passed). Grepping for the English word "error" silently misses everything, and `0 Hata`
  matched a naive grep for "0 errors" once. **Use exit codes and error-code greps** (`error CS`,
  `error MSB`).
- **CSharpier is NOT adopted** by these projects — only `00.Governance/SharedKernel.Linter`'s own
  projects reference it. Running a formatter over the tree would reformat far beyond any reasonable
  scope. Fix formatting surgically.
- **Never run `perl -i` with `local(@ARGV, ...)` in the replacement body** — it clobbers the file list
  `-i` is iterating and truncates the file to zero bytes. This destroyed
  `EfCorePersistenceExtensions.cs` in this session; it was recoverable only from a subagent
  transcript, because the git index still matched `HEAD` and all the work was unstaged. For anything
  structural in a large file, use the Edit tool.
- **Do not commit while background agents are still running.** Doing so captured a moving tree once
  and forced a full re-verification cycle.
- `06.Persistence/CLAUDE.archive.md` holds the pre-P-557 domain brain (~2,600 lines). It is
  **superseded and must not be used for routing** — it describes the 4-package shape — but it carries
  the reasoning behind ~150 ratified decisions and is worth consulting when revisiting one.

## 8. Where the detailed records live

| Record | Location |
|---|---|
| Task-level phase record, findings table | `06.Persistence/state-map.md`, phase `SK.06.P557` |
| Current architecture, seams, DI shape | `06.Persistence/CLAUDE.md` |
| Pre-P-557 brain (superseded) | `06.Persistence/CLAUDE.archive.md` |
| Root routing + phase backlog | `CLAUDE.md`, `state-map.md` |
| Per-wave implementation logs | Session scratchpad: `p557-plan.md`, `p557-w1..w7-log.md`, `p557-remA/remA2/remB/remC/remD-log.md`, `p557-cleanup-log.md` — **temp files, not in the repo; they will not survive** |

The scratchpad logs are the only place the wave-by-wave narrative and the two reviews' full findings
exist. If they matter to you, copy them into the repo before they are cleaned up — the state-map's
findings table is the durable summary, but it is a summary.
