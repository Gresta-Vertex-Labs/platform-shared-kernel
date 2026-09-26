# P-558 — Session Handoff (persistence gold-standard pass 2)

**Session date:** 2026-09-21
**Branch:** `persistence-gold-pass-2` (from `d11f376f` on `main`) · **Not pushed, not merged, nothing published.**
**Replaces:** `P-557-SESSION-HANDOFF.md` (in git history).

> **Who this is for:** the next session continuing `06.Persistence`, or whoever publishes it. Read this first, then
> `06.Persistence/CLAUDE.md` (current architecture and invariants) and `06.Persistence/README.md` (the consumer path).
> This file is a point-in-time summary; where it disagrees with the brains or the code, they win.

---

## 1. What happened

A second, user-directed pre-publish pass over `06.Persistence`. Five independent reviewers found 34 correctness and
security defects in the P-557 result — among them a session-scoped row-level-security binding that leaks tenants
behind PgBouncer, retry that could not coexist with audited transactions, silently plaintext nested encrypted
columns, a checkpoint anchor whose content could be edited while verifying Intact, and domain events skipped on most
save paths — plus heavy DX debt (22 usings, a builder that needed `.Build()`, adapters between 05 and 06). The owner
chose a redesign rather than patches; it was built in waves (shared contracts, PostgreSQL-only, five parallel
streams, polish), reviewed again adversarially (correctness, security, DX), remediated, and documented.

Records: `06.Persistence/docs/p558/` — [`p558-review-findings.md`](../../06.Persistence/docs/p558/p558-review-findings.md)
(initial review), [`p558-design.md`](../../06.Persistence/docs/p558/p558-design.md) (binding decisions),
[`p558-final-review-findings.md`](../../06.Persistence/docs/p558/p558-final-review-findings.md) (second review),
[`waves.md`](../../06.Persistence/docs/p558/waves.md) (what each wave did, with commits).

## 2. Decisions made by the owner

| Decision | Consequence |
| --- | --- |
| **Audit ledger = async sealer** | The request path is one INSERT (no lock, no sequence, no retry); a background sealer builds the per-(tenant, resource type) HMAC chains in commit-safe xid order. Removes the P-557 advisory-lock self-deadlock (H8) and dropped `Failed` records |
| **Merge the 05 and 06 contracts** | New `SharedKernel.Application.Abstractions` (MediatR-free) owns the one `IUnitOfWork`, `IRequestContext`, `IAuditTrailWriter`; persistence implements them directly; every bridge in `13.ServiceDefaults.Persistence` deleted; `SharedKernel.ServiceDefaults.Security` provides `IRequestContext` over `12.Security` |
| **PostgreSQL-only** | `SharedKernel.Persistence.PostgreSQL` merged into `.EfCore` and deleted; `xmin` concurrency on every aggregate root; snake_case via `EFCore.NamingConventions` |
| **All additions** | One-line setup + DX, encryption migration tools (maintenance modes), multiple DbContexts, per-tenant crypto-shredding, the specification API in `03.Domain` |
| **`SharedKernel.Persistence.Testing` package** | Published test helpers for consuming services (fakes + PostgreSQL role-split fixtures), guarded against production references |
| **No state-map phasing** | Deliberately no `state-map.md` phase entries for P-558; the records above and the brains are the history |

## 3. Packages

### New (never published)

| Package | Folder |
| --- | --- |
| `SharedKernel.Application.Abstractions` | `05.Application` |
| `SharedKernel.ServiceDefaults.Security` | `13.ServiceDefaults` |
| `SharedKernel.Persistence.Testing` | `16.Testing` (packable; test projects only) |

### Deleted

`SharedKernel.Persistence.PostgreSQL` (merged into `.EfCore`). Its leftover `bin/obj` folders under
`06.Persistence/SharedKernel.Persistence.PostgreSQL/` are untracked build output and can be deleted.

### Changed, by package (`git diff --stat d11f376f..HEAD`, production files)

| Package | Published? | Change |
| --- | --- | --- |
| `SharedKernel.Core` (01) | yes | Additive: `ValidationException(Error, Exception innerException)` |
| `SharedKernel.Domain` (03) | yes | **Breaking**: specification API (`Spec.For<T>()`, typed `ThenInclude`, `ProjectionSpecification`; `Paged`/`Keyset`/`ReadOnlySpecification`, `AsNoTracking`, `ApplyNoTracking` removed; `ApplyThenBy(key)` ascending only; composites throw instead of dropping ordering), `Restore()`/`OnRestore()`, `OnDelete` virtual, `[TenantShared]`, `IProjectionSpecification` moved in from 06 |
| `SharedKernel.Contracts` (04) | yes | XML doc text only (no API change) |
| `SharedKernel.Application` (05) | yes | Context types moved to `.Abstractions` with `[TypeForwardedTo]` (binary-compatible forward, new dependency) |
| `SharedKernel.Application.Behaviors` (05) | yes | **Breaking**: its `IUnitOfWork`/`ITransactionalUnitOfWork`/`IPersistenceTransaction`/`IAuditTrailWriter`/`AuditEntry` deleted (now `.Abstractions`); `TransactionBehavior` via `ExecuteInTransactionAsync`; auditing split in two halves; rollback-only joins |
| `SharedKernel.Application.Behaviors.Caching` (05) | yes | No code change (depends on `.Behaviors`) |
| All six `SharedKernel.Persistence.*` (06) | no | Redesigned (see the brain) |
| `SharedKernel.ServiceDefaults`, `.Persistence` (13) | no | `HealthCheckNames` additions, telemetry list, bridges deleted, three new readiness checks |
| `SharedKernel.Analyzers`, `SharedKernel.ArchitectureTests` (00) | not per P-557 handoff — check the feed | SK0042 targets `IDbSession`; SK0201 base-call only; `UnitOfWorkSeamRules`, `ReadOnlyRepositoriesNeverTrack`, `PersistenceNamespaceConventionRules`, persistence layering, testing-package guard |
| `SharedKernel.Idempotency.EfCore` (18) | not recorded as published | Retry off for its context; new API names |
| `SharedKernel.Testing` (16) | never (`IsPackable=false`) | Fakes moved out to `.Persistence.Testing` |

## 4. Publishing

Nothing was published. Versions are MinVer lockstep (`1.0.0-alpha.0.<height>` without a tag): **publishing any package
means republishing its already-published dependency closure at the same height**, or the packed nuspec will not
resolve against the feed (the P-556/P-557 lesson). `publish-package.yml` (workflow_dispatch, `dry_run` default true)
gates on every `SharedKernel.*` dependency already being on the feed.

**Publish set for the persistence stack** (derived from project references): Primitives, Core, Configuration,
Domain, Contracts, Cryptography, Caching.Abstractions, Application.Abstractions, Application, Application.Behaviors,
Application.Behaviors.Caching, Persistence.Abstractions, Persistence.Npgsql, Persistence.EfCore,
Persistence.EfCore.Auditing, Persistence.EfCore.Encryption, Persistence.Dapper, Persistence.Testing — plus, when the
host packages are published, ServiceDefaults, ServiceDefaults.Security (needs Security.Abstractions) and
ServiceDefaults.Persistence. `Caching.Abstractions` is in the set only because `.Behaviors.Caching` depends on it.

**Order** (each step's dependencies must already be on the feed):

```text
Primitives → Core, Configuration → Domain, Contracts, Cryptography, Caching.Abstractions
  → Application.Abstractions → Application → Application.Behaviors → Application.Behaviors.Caching
  → Persistence.Abstractions → Persistence.Npgsql → Persistence.EfCore
  → Persistence.EfCore.Auditing, Persistence.EfCore.Encryption, Persistence.Dapper
  → Persistence.Testing
  → (Security.Abstractions →) ServiceDefaults → ServiceDefaults.Security, ServiceDefaults.Persistence
```

`EfCore.Auditing`/`EfCore.Encryption` pin `Persistence.EfCore` and `EfCore` pins `Persistence.Npgsql` to the **exact**
version (they use each other's internals): publish them from the same build.

Before publishing: run the packed ConsumerVerify (§5) and the BillingApi sample (§7) against the real feed, not only
the local folder feed. Both are wired into CI's packaging-verify job (the 06 ConsumerVerify step and the BillingApi
end-to-end tests; both need Docker).

## 5. Verification state

| Check | Command | Result |
| --- | --- | --- |
| Build | `dotnet build Platform.SharedKernel.slnx -c Release` | exit 0, 0 `error CS/RS/MSB` (docs commit, 2026-09-21) |
| Unit + integration suites | per project, Docker running | all green at `340c5fa0`: Analyzers 362, ArchitectureTests 364, Linter 36, Domain 505, Contracts 105, Application.Abstractions 25, Application 33, Behaviors 80, Behaviors.Caching 51, Persistence.Abstractions 33, EfCore 375, EfCore.Integration 111, Auditing 107, Encryption 92, Npgsql 116, Dapper 23, ServiceDefaults 96 (+ integration packages: .Persistence 16, .Security 8, .Security.Mtls 51, …), MultiTenancy 85, Testing.SelfTests 1318, Persistence.Testing.Tests 79, Idempotency.EfCore 36, Idempotency.Redis 35 |
| Domain README sample | `dotnet test 13.ServiceDefaults/SharedKernel.ServiceDefaults.Persistence/SharedKernel.ServiceDefaults.Persistence.Tests -c Release` | 18/18 incl. the two new `PersistenceReadmeSampleTests` (MediatR + RLS + audit + Money on PostgreSQL; fakes) |
| Packed consumers | `dotnet pack Platform.SharedKernel.slnx -c Release -o ./nupkgs -p:MinVerVersionOverride=1.0.0-p558.local.4`, then `dotnet test 06.Persistence/SharedKernel.Persistence.ConsumerVerify -p:SharedKernelPackageVersion=1.0.0-p558.local.4` (same for `05.Application/SharedKernel.Application.ConsumerVerify`) | 06: 9/9 (composed multi-tenancy + RLS + audit + encryption + retry scenario, testing-package tests); 05: 6/6 |
| Sample service, packed feed (2026-09-22) | pack as `1.0.0-p558.local.8`; `dotnet test samples/BillingApi/BillingApi.Tests -p:SharedKernelPackageVersion=1.0.0-p558.local.8` | 16/16 (15 end-to-end over HTTP in the Production environment on Testcontainers PostgreSQL 17 with the role split and real migrations; 1 unit test over the fakes) |
| Sample service in Docker (2026-09-22) | `dotnet publish samples/BillingApi -t:PublishContainer …`, `docker compose up -d`, `./smoke-test.sh` | 28/28 checks; ready in Production with RLS privilege + coverage checks and the audit self-check at `Fail` |

## 6. Known limitations, deliberately open

- No two-phase commit: a context on another database or role never joins the scope's transaction (refused at commit
  if it holds changes).
- A nested audited command's `Succeeded` entry rolls back with a failing outer command; no separate `Failed` entry.
- `IRequestContext.ImpersonatorId` is not populated (`IUserContext` has no impersonation claim).
- Seeding under RLS needs `RowLevelSecurity:CrossTenantConnectionString`.
- `EnableTenantRowLevelSecurityForModel` in a later migration re-creates existing policies — use the per-table helper.
- Encryption: no specification form of `WhereEncryptedEquals`; no tenant data-key rotation; Dapper/raw-SQL writes
  are not tombstone-checked; another process may decrypt a shredded tenant's values from cache for up to
  `TenantKeyCacheDuration`; the rotation CAS race has no dedicated test; `byte[]` and composite keys unsupported.
- Audit: a link forged for one record looks sealed until verification — prevented only by the separate sealer role;
  retention partitioning, signed export bundles, `IAuditContext` not built.
- EfCore public surface ≈ 140 lines (target was ~70) — accepted (subclassable repositories).
- No real PgBouncer container test (simulated); `FakeRepository` writes are not rolled back by `FakeUnitOfWork`.
- Unit-lane `TestPersistenceRegistration` (SQLite over the real registration) stays by necessity.
- A context applies **every** `IEntityTypeConfiguration` in its assembly; a second context's configuration in the same
  assembly breaks a `TenantedDbContext` model build unless `ShouldApplyConfiguration` filters (documented; the README
  sample test needed it).

## 7. Pre-publish verification through a real service (2026-09-22)

`samples/BillingApi` uses all six persistence packages, `SharedKernel.Persistence.Testing`, the pipeline behaviors,
`ServiceDefaults.Security`/`.Persistence` and `Presentation.WebApi` from the **packed** feed, through an HTTP API, against
PostgreSQL with the canonical role script (`docker/init-roles.sql`), in Docker Compose and in its tests. Building it the
way the READMEs say found five defects the package suites had not, all fixed with regression tests:

| # | Package | Defect | Fix |
| --- | --- | --- | --- |
| 1 | EfCore | `PostgresDesignTimeDbContextFactory` built the model without capability conventions: `dotnet ef migrations add` failed for any `.Encrypt()` model, and would otherwise have produced a migration without the blind-index columns / encrypted widths the runtime model has | virtual `ConfigurePersistence(EfCorePersistenceBuilder<TContext>)`; the factory takes the model conventions/configurators of that registration (nothing else is resolved); the guard message names it; `DesignTimeFactoryTests` proves design model == runtime model |
| 2 | EfCore | **Security.** `migrationBuilder.EnableTenantRowLevelSecurityForModel(TargetModel!)` — the call every README prescribes — created **no policy** in a real migration: a `TargetModel` is property bags without CLR types, so the `IHasTenant` test never matched. Dapper SQL relying on RLS then returned another tenant's rows (observed: tenant B's revenue report showed tenant A's invoice). Hidden because tests passed the live model and the coverage check only warns in Development | tenant entity types carry `SharedKernel:Persistence:Tenant` (into the Designer file); `TenantTables` reads it; `ForModel` throws on zero tables; `MigrationTargetModelTests` compiles a scaffolded migration and uses its own `TargetModel` (fails without the fix) |
| 3 | EfCore | `ConcurrencyVersion.Get` on an untracked entity (the documented read path, `IReadRepository`) returned version **0**: GET → `ETag: "0"` → every PUT with that `If-Match` answered 412 | throws with guidance for an untracked shadow-`xmin` entity; README: read the ETag from a tracked instance |
| 4 | Persistence.Testing | `FakeUnitOfWork` rollback (failed `Result`, exception, `TransientFailures` replay) left `FakeRepository` writes in place: the README's own handler pattern failed the documented re-runnability proof with "already exists", and failed commands left data behind | fake repositories enlist in the fake transaction (either registration order) and are restored on rollback |
| 5 | Npgsql | Npgsql's default GSS encryption `Prefer` probed Kerberos on every new physical connection; in the standard ASP.NET image it printed `libgssapi_krb5.so.2: cannot open shared object file` | GSS encryption `Disable` unless the connection string sets `GSS Encryption Mode` |

Also: `Directory.Packages.props` gained `PackageVersion` entries for `SharedKernel.Security.Abstractions`,
`SharedKernel.ServiceDefaults.Persistence` and `.Security` (consumers of the packed feed need them); the domain README's
migration step and the EfCore/Testing/Npgsql READMEs describe the new behavior; the README sample test compiles the
design-time factory. Every migration generated before fix 2 must be regenerated (none is published).

## 8. Working notes

- **Windows path length.** Deep test paths overflow 260 characters (`MSB3030` in the KeyVault integration test
  projects is a pre-existing baseline). Parallel streams used short worktrees under `C:\wt\<stream>` (`git worktree
  add C:\wt\e1 -b p558-w2-e1`); keep that pattern for parallel work, and remove the worktrees afterwards.
- **Turkish build output** (`Hata` = error, `Başarısız` = failed, `Başarılı` = passed). Judge by exit code and
  `error CS`/`error MSB`/`error RS` greps; a naive "0 errors" grep matched `0 Hata` once. `RS0026/27` only show on a
  real recompile (`--no-incremental` or pack).
- **Packed ConsumerVerify procedure:** pack the whole solution with `-p:MinVerVersionOverride=1.0.0-<tag>.local.N`
  into `./nupkgs` (gitignored; already holds `.local.1`–`.4`), then restore/build/test the ConsumerVerify project with
  `-p:SharedKernelPackageVersion=` the same version. Bump `N` every run — NuGet caches a version forever.
- **Docker** is required for every integration-lane suite; re-run a failing container suite alone before treating it
  as real.
- Never `perl -i` with `local(@ARGV, ...)`; use the Edit tool for structural edits of large files.
- `06.Persistence/CLAUDE.archive.md` now holds both superseded brains (pre-P-557 first, P-557 appended) — reasoning
  only, never routing.
