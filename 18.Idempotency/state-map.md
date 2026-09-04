# 18.Idempotency — State Map

> **What this file is:** Phase and task tracker for all work within `18.Idempotency`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.18.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
| --- | --- |
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition | Root Backlog ID |
| --- | --- | --- | --- |
| `SK.18.Design` | Design | All tasks in Phase: Design are `●` | — |
| `SK.18.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` | — |
| `SK.18.Core` | Core | All tasks in Phase: Core are `●` | — |
| `SK.18.Tests` | Tests | All tasks in Phase: Tests are `●` | — |
| `SK.18.Docs` | Docs | All tasks in Phase: Docs are `●` | — |
| `SK.18.Published` | Published | All tasks in Phase: Published are `●` | — |

> **Root Backlog ID column:** left `—` on every lifecycle row deliberately. P-454/P-455 span Design→Published, so attaching them to any single lifecycle key would close them prematurely (see `/state-map-phase` Step S8a, Case 3). They close via Step S8c when every phase key is `●`.

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement IIdempotencyKeyStore | SK.18.Core | SharedKernel.Idempotency.Redis | ◐ |
-->

---

## Blocked

_Nothing blocked._

---

## Package Board

| Package | Current Phase | State | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Idempotency.Redis` | Design | `○` | P-454/WO-070. Design phase tasks authored 2026-08-26 (D-01–D-09). Atomic Redis-backed implementation of `IIdempotencyKeyStore`+`IIdempotencyResponseStore` (one combined class, `RedisIdempotencyKeyStore`) and `IIdempotencyStore` (`RedisIdempotencyMessageStore`) over shared internal key-building/tenant-scoping infrastructure. Built on `02.Caching.Redis.Core`. Not yet scaffolded. |
| `SharedKernel.Idempotency.EfCore` | Design | `○` | P-455/WO-070. Design phase tasks authored 2026-08-26 (D-01–D-09, shared with `.Redis`). EF Core/PostgreSQL sibling implementing the identical three contracts (`EfCoreIdempotencyKeyStore`, `EfCoreIdempotencyMessageStore`) for services that run PostgreSQL and do not want Redis solely for deduplication. Atomicity via `INSERT ... ON CONFLICT DO UPDATE ... WHERE <expired>`. Own minimal `IdempotencyDbContext`, deliberately not `SharedKernelDbContext`. Never references `02.Caching`. Not yet scaffolded. |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
| --- | --- | --- | --- |
| `SK.18.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`Result<T>`, `Result`, `Error`) | Available |
| `SK.18.Scaffold` | `01.Core` | `SharedKernel.Configuration` ProjectReference (`AddValidatedOptions`) | Available |
| `SK.18.Scaffold` | `01.Core` | `IClock` (via `SharedKernel.Primitives`) — sources every `ReservedAtUtc`/`ExpiresAtUtc` timestamp and expiry comparison in `.EfCore`; never `DateTime.UtcNow` (D-05/D-06) | Available |
| `SK.18.Scaffold` | `05.Application` | `SharedKernel.Application.Behaviors` ProjectReference — `IIdempotencyKeyStore`, `IIdempotencyResponseStore` | Available (shipped; `IIdempotencyResponseStore` since WO-039/P-242) |
| `SK.18.Scaffold` | `07.Messaging` | `SharedKernel.Messaging.Abstractions` ProjectReference — `IIdempotencyStore`, and `TenantContext.ITenantContextAccessor` reused as this domain's own tenant-resolution bridge (Design decision D-02 — no new contract authored in `18.Idempotency` itself) | Available (shipped) |
| `SK.18.Scaffold` | `02.Caching` | `SharedKernel.Caching.Redis.Core` ProjectReference — shared `IConnectionMultiplexer`, connection health, resilience pipeline — **`.Redis` package only** | Available |
| `SK.18.Scaffold` | `06.Persistence` | `SharedKernel.Persistence.EfCore` / `.PostgreSQL` ProjectReference — **`.EfCore` package only**; `IdempotencyDbContext` extends plain EF Core `DbContext`, deliberately not `SharedKernelDbContext` (D-06) | Available |
| `SK.18.Core` | `01.Core` | `EventId` range registry entry for `[LoggerMessage]` logging — domain base `18000`–`18999`, sub-blocks `18000`–`18099` (`.Redis`) and `18100`–`18199` (`.EfCore`) in declaration order | **Ratified, not yet in code.** `core-arch-planner` design-locked `Idempotency = 18000` in `01.Core`'s `LoggingEventIdRanges` this same session (phase key `SK.01.LoggingRangesNewDomains`, design `●`, implementation pending). Tasks C-05/C-10 (the fail-open Warning log statements) stay blocked on `01.Core` shipping the entry in code, but the sub-block split is now safe to treat as fixed rather than provisional. |
| `SK.18.Tests` | `16.Testing` | `RedisContainerFixture` and `PostgreSqlContainerFixture` for real round-trip concurrency tests | Available |
| `SK.18.Tests` | `16.Testing` | `FakeClock` (controllable `IClock` double) — usable for `.EfCore`'s expiry/reclaim tests where a deterministic clock is preferable to `Task.Delay`-based real-time waits | Available |

> **No `13.ServiceDefaults` readiness-probe grant is needed by this domain.** Backing-store health is already covered — Redis connectivity by `02.Caching.Redis.Core`'s existing health surface, database connectivity by `06.Persistence`'s readiness probe. A third probe over the same two connections would be duplicate signal, not new signal. Recorded here so a future session does not assume a grant is missing.

---

## Phase: Design <!-- phase-key: SK.18.Design -->

> Finalize store-class decomposition, the atomic-reservation protocol, the tenant-scoping seam, TTL/retention shape, and the fail-closed/fail-open option surface before any implementation begins. Covers P-454 (`.Redis`) and P-455 (`.EfCore`).

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-01 | **Store-class decomposition — corrects P-454's "three focused store classes" wording.** Lock two physical classes per package, not three: `RedisIdempotencyKeyStore`/`EfCoreIdempotencyKeyStore` implement **both** `IIdempotencyKeyStore` *and* `IIdempotencyResponseStore` on the same instance; `RedisIdempotencyMessageStore`/`EfCoreIdempotencyMessageStore` implement `IIdempotencyStore` alone. Reasoning, recorded here because it revises the work order's literal shape: `IIdempotencyResponseStore`'s own XML docs state that `IdempotentCommandBehavior<TRequest,TResponse>` detects replay support via a plain `is IIdempotencyResponseStore` pattern-match on the *injected* `IIdempotencyKeyStore` instance — response replay is structurally unreachable if the two interfaces are split across separate classes, regardless of DI wiring. Three interfaces are still implemented; two classes is the only shape that lets the documented `is`-check succeed. | Both | `●` |
| D-02 | **Tenant-resolution seam.** Reuse `07.Messaging.Abstractions.TenantContext.ITenantContextAccessor` (`Guid? TenantId`) as the sole tenant-resolution bridge for all four store classes across both packages — both packages already carry a legal `07.Messaging.Abstractions` reference for `IIdempotencyStore`, so this adds no new cross-domain dependency and authors no new contract in `18.Idempotency` (preserving "declares no contracts of its own"). Null-tenant policy: `TenantId == null` resolves to a single well-known non-tenant segment (a fixed internal constant, not a caller-suppliable value), never simply omitted from the composed key/row — this keeps legitimate system/background execution usable while making a null-tenant entry structurally unable to collide with any real tenant's entry, since the accessor is composition-root-controlled, never caller-controlled. | Both | `●` |
| D-03 | **Key-building seam, duplicated per package (no shared `.Core`).** `.Redis`: internal `RedisIdempotencyKeyBuilder` composes `{prefix}:{tenantSegment}:{kind}:{rawKey}` string keys (`kind` distinguishes the key-store namespace from the message-store namespace so the two never collide inside one Redis keyspace). `.EfCore`: no string composition at all — `TenantId` is its own mandatory column on both tables, joined with `Key`/`MessageId` in a real composite unique constraint; a concatenated string column standing in for tenant scoping is explicitly rejected. | Both | `●` |
| D-04 | **Redis atomic-reservation protocol.** Reservation (`HasProcessedAsync`, both store classes): `SET key <sentinel> NX PX <inFlightTtlMs>` — success (key absent) returns `false` (reservation created); failure (key exists, in-flight or confirmed) returns `true`. Confirmation (`MarkProcessedAsync`): `PEXPIRE key <fullRetentionTtlMs>` — extends TTL only, never rewrites the value, so it can never clobber a response `StoreResponseAsync` already wrote, regardless of call order. Response write (`StoreResponseAsync`, key-store only): a 3-line Lua script — write only if the reservation key still exists, using `SET ... KEEPTTL` so the confirmed TTL survives untouched. Response read (`TryGetStoredResponseAsync`): `GET key`; a value equal to the fixed non-JSON sentinel (chosen so it can never collide with real `System.Text.Json` output) or a missing key both mean "no stored response" → `null`. Never `WATCH`/`MULTI` anywhere in this protocol. | `SharedKernel.Idempotency.Redis` | `●` |
| D-05 | **EfCore atomic-reservation protocol.** Reservation (`HasProcessedAsync`): one raw-SQL upsert via `Database.ExecuteSqlInterpolatedAsync` — `INSERT ... ON CONFLICT (tenant_id, key) DO UPDATE SET reserved_at_utc = EXCLUDED.reserved_at_utc, expires_at_utc = EXCLUDED.expires_at_utc WHERE <table>.expires_at_utc < @now`. Affected-row count `1` (fresh insert, or a genuinely-expired row reclaimed) → returns `false`; `0` (a live, unexpired row already exists) → returns `true`. This single statement performs reservation and expiry-reclaim atomically — no `SELECT` is ever issued for correctness, and no plain `DbSet.Add` + caught-`DbUpdateException` control flow is used anywhere. Confirmation (`MarkProcessedAsync`): plain `UPDATE ... SET expires_at_utc = @now + @fullRetention WHERE tenant_id=@t AND key=@k` — extends expiry only, leaves `response` untouched. Response write/read mirror the Redis shape (`UPDATE ... SET response=@r`; `SELECT response ... WHERE expires_at_utc > @now`, so an expired row never surfaces a stale response). The message store follows the identical shape against its own `(tenant_id, message_id)`-keyed table. Every `@now` comes from the injected `IClock`, never `DateTime.UtcNow`. | `SharedKernel.Idempotency.EfCore` | `●` |
| D-06 | **EfCore schema and the reason this package does not extend `SharedKernelDbContext`.** `SharedKernelDbContext` (`06.Persistence.EfCore`) unconditionally wires `AuditInterceptor`/`SoftDeleteInterceptor`/`ConcurrencyInterceptor`; `SoftDeleteInterceptor` would silently rewrite the cleanup recipe's hard `DELETE FROM ... WHERE expires_at_utc < now()` into an update, defeating retention entirely, and `ConcurrencyInterceptor` assumes a row-version column this table has no reason to carry. `.EfCore` instead defines its own minimal `IdempotencyDbContext : DbContext` (plain EF Core, no SharedKernel base) owning exactly two entities: `IdempotencyKeyRecord` (`TenantId`, `Key`, `ReservedAtUtc`, `ExpiresAtUtc`, `Response` nullable; unique constraint `(TenantId, Key)`; non-unique index on `ExpiresAtUtc` for the cleanup scan) and `IdempotencyMessageRecord` (`TenantId`, `MessageId`, `ReservedAtUtc`, `ExpiresAtUtc`; unique constraint `(TenantId, MessageId)`; non-unique index on `ExpiresAtUtc`). | `SharedKernel.Idempotency.EfCore` | `●` |
| D-07 | **Fail-closed/fail-open option surface and exception classifiers.** `RedisIdempotencyOptions.AllowExecutionOnStoreUnavailable` / `EfCoreIdempotencyOptions.AllowExecutionOnStoreUnavailable`, both default `false`. Each provider gets a narrow internal `IsStoreUnavailableException` classifier recognizing only genuine connectivity/timeout exception types (`RedisConnectionException`/`RedisTimeoutException` for `.Redis`; the connectivity subset of `NpgsqlException`/`TimeoutException` for `.EfCore`) — never a blanket `catch (Exception)`. When fail-open is enabled and a classified exception is caught in `HasProcessedAsync`, log at Warning and return `false` (treat as not-yet-processed); the same classifier applies uniformly to `MarkProcessedAsync`/`StoreResponseAsync` so a mid-flight outage after fail-open let a call proceed does not then throw out of the confirmation step. | Both | `●` |
| D-08 | **TTL/retention option surface.** `RedisIdempotencyOptions`/`EfCoreIdempotencyOptions`: `InFlightTtl` (default 30s), `RetentionWindow` (default 24h), each with a `public const string SectionName` (SK0022) and `01.Core`'s `AddValidatedOptions` enforcing `InFlightTtl > TimeSpan.Zero && InFlightTtl < RetentionWindow`. The message-store classes read `07.Messaging.Abstractions.Idempotency.IdempotencyOptions.ExpiryWindow` (existing advisory hint, already defaulted to 24h there) as their full-retention value instead of duplicating a second retention knob, keeping `InFlightTtl` as their only own setting. | Both | `●` |
| D-09 | **DI registration surface.** `.Redis`: `AddSharedKernelRedisIdempotency(Action<RedisIdempotencyOptions>? configure = null)`, resolving the shared `IConnectionMultiplexer` from `02.Caching.Redis.Core` (never constructing its own), registering `RedisIdempotencyKeyStore` as both `IIdempotencyKeyStore` and `IIdempotencyResponseStore` (same singleton instance) and `RedisIdempotencyMessageStore` as `IIdempotencyStore`. `.EfCore`: `AddSharedKernelEfCoreIdempotency(Action<DbContextOptionsBuilder> configureDbContext, Action<EfCoreIdempotencyOptions>? configureOptions = null)`, registering `IdempotencyDbContext` via `AddDbContext` plus the equivalent three registrations. Both fail fast at `IHost.StartAsync()` (not first use) if no `ITenantContextAccessor` is registered, mirroring `07.Messaging`'s own missing-`IIdempotencyStore` fail-fast precedent. No static mutable state anywhere in either registration path — connection/context access is always through constructor-injected, DI-scoped instances. | Both | `●` |

---

## Phase: Scaffold <!-- phase-key: SK.18.Scaffold -->

> Wire up `.csproj` references, folder structure, solution registration, and empty test stubs — no logic yet.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| S-01 | Create `SharedKernel.Idempotency.Redis.csproj` (`net10.0`); `ProjectReference`s to `SharedKernel.Primitives`, `SharedKernel.Configuration`, `SharedKernel.Caching.Redis.Core`, `SharedKernel.Application.Behaviors`, `SharedKernel.Messaging.Abstractions`; register the project and its solution folder in `Platform.SharedKernel.slnx`. | `SharedKernel.Idempotency.Redis` | `●` |
| S-02 | Create `SharedKernel.Idempotency.EfCore.csproj` (`net10.0`); `ProjectReference`s to `SharedKernel.Primitives`, `SharedKernel.Configuration`, `SharedKernel.Persistence.EfCore`, `SharedKernel.Persistence.PostgreSQL`, `SharedKernel.Application.Behaviors`, `SharedKernel.Messaging.Abstractions`; register in `.slnx`. | `SharedKernel.Idempotency.EfCore` | `●` |
| S-03 | Folder skeleton for `.Redis`: `KeyStore/`, `MessageStore/`, `Options/`, `Extensions/`, `Internal/` (key builder, exception classifier). | `SharedKernel.Idempotency.Redis` | `●` |
| S-04 | Folder skeleton for `.EfCore`: `KeyStore/`, `MessageStore/`, `Context/`, `Entities/`, `Options/`, `Extensions/`, `Internal/`. | `SharedKernel.Idempotency.EfCore` | `●` |
| S-05 | Create `SharedKernel.Idempotency.Redis.Tests` nested inside `.Redis`'s folder, referencing `SharedKernel.Testing` for `RedisContainerFixture`; empty stub files matching the Tests-phase task list (T-01–T-05). | `SharedKernel.Idempotency.Redis` | `●` |
| S-06 | Create `SharedKernel.Idempotency.EfCore.Tests` nested inside `.EfCore`'s folder, referencing `SharedKernel.Testing` for `PostgreSqlContainerFixture`; empty stub files matching T-06–T-10. | `SharedKernel.Idempotency.EfCore` | `●` |
| S-07 | Confirm neither package introduces a *new* third-party NuGet dependency beyond what its referenced lower-layer packages already bring transitively (`StackExchange.Redis` via `.Redis.Core`; `Npgsql.EntityFrameworkCore.PostgreSQL` via `.PostgreSQL`) — no direct `PackageReference` added by this domain itself. | Both | `●` |
| S-08 | NuGet packaging metadata skeleton on both `.csproj`s (`PackageId`, `Description`, `PackageTags`) per the platform's `Directory.Build.props` convention; no `<Version>`/`<VersionPrefix>` element on either (root Package Versioning rule). | Both | `●` |

---

## Phase: Core <!-- phase-key: SK.18.Core -->

> Implement both provider packages against the contracts locked in Design.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| C-01 | Implement the tenant-resolution + key-composition internals per D-02/D-03 — `.Redis/Internal/RedisIdempotencyKeyBuilder`; `.EfCore`'s mandatory `TenantId` column wiring on both entity configurations. | Both | `●` |
| C-02 | Implement `RedisIdempotencyKeyStore` (`IIdempotencyKeyStore` + `IIdempotencyResponseStore`) per D-04. | `SharedKernel.Idempotency.Redis` | `●` |
| C-03 | Implement `RedisIdempotencyMessageStore` (`IIdempotencyStore`) per D-04. | `SharedKernel.Idempotency.Redis` | `●` |
| C-04 | Implement `RedisIdempotencyOptions` (+ `AddValidatedOptions` wiring) and `AddSharedKernelRedisIdempotency` DI extension per D-08/D-09. | `SharedKernel.Idempotency.Redis` | `●` |
| C-05 | Implement the Redis store-unavailability classifier + fail-open branch per D-07. `[LoggerMessage]` Warning entries at `18000`–`18099` — **blocked on `01.Core` shipping the `Idempotency = 18000` `LoggingEventIdRanges` entry in code** (design-locked, not yet implemented; see Cross-Domain Dependencies). The rest of this task (the classifier and the fail-open branch logic itself) is not blocked — only the logging call sites are. | `SharedKernel.Idempotency.Redis` | `●` |
| C-06 | Implement `IdempotencyKeyRecord`/`IdempotencyMessageRecord` entities, their `IEntityTypeConfiguration<T>`s (unique constraints, `ExpiresAtUtc` indexes), and `IdempotencyDbContext` per D-06. | `SharedKernel.Idempotency.EfCore` | `●` |
| C-07 | Implement `EfCoreIdempotencyKeyStore` (`IIdempotencyKeyStore` + `IIdempotencyResponseStore`) per D-05. | `SharedKernel.Idempotency.EfCore` | `●` |
| C-08 | Implement `EfCoreIdempotencyMessageStore` (`IIdempotencyStore`) per D-05. | `SharedKernel.Idempotency.EfCore` | `●` |
| C-09 | Implement `EfCoreIdempotencyOptions` (+ `AddValidatedOptions` wiring) and `AddSharedKernelEfCoreIdempotency` DI extension per D-08/D-09. | `SharedKernel.Idempotency.EfCore` | `●` |
| C-10 | Implement the EfCore store-unavailability classifier + fail-open branch per D-07. `[LoggerMessage]` Warning entries at `18100`–`18199` — same `01.Core` EventId-registry dependency as C-05. | `SharedKernel.Idempotency.EfCore` | `●` |

---

## Phase: Tests <!-- phase-key: SK.18.Tests -->

> Concurrency-proving tests are the point of this domain, not an afterthought — the atomicity criteria in P-454/P-455 are only satisfiable by real concurrent-execution tests against real backing stores. A mocked store never stands in for one here.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| T-01 | Redis: fire N parallel `HasProcessedAsync` calls with the identical key against a real `RedisContainerFixture`-backed store; assert exactly one observes `false`. | `SharedKernel.Idempotency.Redis` | `●` |
| T-02 | Redis: reserve a key, let the short `InFlightTtl` elapse without calling `MarkProcessedAsync`, assert a subsequent `HasProcessedAsync` returns `false` again (self-heal, Invariant 2). | `SharedKernel.Idempotency.Redis` | `●` |
| T-03 | Redis: order-independence of confirmation vs. response write — `HasProcessedAsync` → `StoreResponseAsync` → `MarkProcessedAsync`, then assert `TryGetStoredResponseAsync` still returns the original payload (proves `PEXPIRE`/`KEEPTTL` never clobber each other per D-04). | `SharedKernel.Idempotency.Redis` | `●` |
| T-04 | Redis: tenant isolation — two different `TenantId`s reserving the identical raw key/message id concurrently must not collide; both reservations succeed independently. | `SharedKernel.Idempotency.Redis` | `●` |
| T-05 | Redis: fail-open/fail-closed — simulate store unavailability; assert the default (`AllowExecutionOnStoreUnavailable=false`) rethrows, and `true` instead returns `false` with a Warning log recorded via `16.Testing`'s log-assertion double. | `SharedKernel.Idempotency.Redis` | `●` |
| T-06 | EfCore: fire N parallel `HasProcessedAsync` calls with the identical `(TenantId, Key)` against a real `PostgreSqlContainerFixture`-backed store; assert exactly one observes `false` — the concurrent-insert proof P-455's acceptance criteria require. | `SharedKernel.Idempotency.EfCore` | `●` |
| T-07 | EfCore: expired-row reclaim — insert a row with `ExpiresAtUtc` in the past directly, then call `HasProcessedAsync` for the same key and assert it returns `false` (reclaimed), not `true`. | `SharedKernel.Idempotency.EfCore` | `●` |
| T-08 | EfCore: `TryGetStoredResponseAsync` excludes an expired row's stored response (returns `null` even though a `Response` value is physically present). | `SharedKernel.Idempotency.EfCore` | `●` |
| T-09 | EfCore: tenant isolation, mirroring T-04, proving the mandatory `TenantId` column (not a composite string) actually partitions rows under concurrent access from two tenants. | `SharedKernel.Idempotency.EfCore` | `●` |
| T-10 | EfCore: fail-open/fail-closed, mirroring T-05, against a real Postgres connectivity failure. | `SharedKernel.Idempotency.EfCore` | `●` |

---

## Phase: Docs <!-- phase-key: SK.18.Docs -->

> XML docs, package `README.md` wired into the pack via `PackageReadmeFile`, and the fail-open opt-in documented in capitals.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| DO-01 | XML docs on every public type/member in both packages, cross-referencing the fault-vs-failure semantics already documented on `IIdempotencyKeyStore.MarkProcessedAsync` rather than restating them. | Both | `●` |
| DO-02 | `AllowExecutionOnStoreUnavailable` XML doc on both Options types states **in capitals** that enabling it increases duplicate-execution risk, per Invariant 4. | Both | `●` |
| DO-03 | `SharedKernel.Idempotency.Redis/README.md` — DI quick-start, the `ITenantContextAccessor` bridging requirement, TTL/retention configuration, the fail-open opt-out documented in capitals; wired into the pack via `PackageReadmeFile`. | `SharedKernel.Idempotency.Redis` | `●` |
| DO-04 | `SharedKernel.Idempotency.EfCore/README.md` — same shape, plus the migration-authoring recipe (consumer adds a design-time factory / migrations project referencing `IdempotencyDbContext`) and the documented cleanup-job recipe (a periodic `DELETE FROM ... WHERE expires_at_utc < now()` — a consumer-registered `IHostedService` snippet, or a future `19.Scheduling` `ScheduledCommandJob` snippet) — never implied as automatic. | `SharedKernel.Idempotency.EfCore` | `●` |
| DO-05 | A `consumer-verify`-style usage snippet in both READMEs showing DI registration resolving cleanly through a real `IHost.StartAsync()`, foreshadowing the Published-phase harness (P-03). | Both | `●` |
| DO-06 | Flag any wording drift between root `CLAUDE.md`'s two `18.Idempotency` rows (Folder Map, "What Goes Where") and what actually shipped, as a candidate follow-up note for whichever session next runs `/sync-brain` — root `CLAUDE.md` is out of this domain's jurisdiction to edit directly. | Both | `●` |

---

## Phase: Published <!-- phase-key: SK.18.Published -->

> Full NuGet metadata, clean `dotnet pack`, and a `consumer-verify` harness proving DI registration resolves through a real `IHost.StartAsync()`.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| P-01 | Finalize NuGet metadata on both `.csproj`s (`Description`, `PackageTags`, `PackageReadmeFile` pointing at each package's own `README.md`). | Both | `●` |
| P-02 | Clean `dotnet pack` of both packages with no warnings. | Both | `●` |
| P-03 | `consumer-verify` harness — a throwaway consumer project referencing both packages, proving `AddSharedKernelRedisIdempotency`/`AddSharedKernelEfCoreIdempotency` resolve through a real `IHost.StartAsync()` per the README recipes (DO-05), including a missing-`ITenantContextAccessor` fail-fast check. | Both | `○` |
| P-04 | Verify both packages carry zero `<Version>`/`<VersionPrefix>` elements and pack at the repo-wide MinVer-derived version (root Package Versioning rule). | Both | `●` |
| P-05 | Once every phase key in this file is `●`, run `/state-map-phase` to close P-454/P-455 in the root Phase Backlog (Step S8c — no single lifecycle key closes them individually, per the Phase Key Registry note above). | Both | `○` |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | ⚑ Blocked | State |
| --- | --- | :---: | :---: | :---: | :---: | :---: |
| `SK.18.Design` | Design | 9 | 9 | 0 | 0 | `●` |
| `SK.18.Scaffold` | Scaffold | 8 | 8 | 0 | 0 | `●` |
| `SK.18.Core` | Core | 10 | 10 | 0 | 0 | `●` |
| `SK.18.Tests` | Tests | 10 | 2 | 8 | 0 | `◐` |
| `SK.18.Docs` | Docs | 6 | 6 | 0 | 0 | `●` |
| `SK.18.Published` | Published | 5 | 3 | 2 | 0 | `◐` |

Design, Scaffold, Core, and Docs phases complete for both packages (2026-09-04 implementation session — both `SharedKernel.Idempotency.Redis` and `SharedKernel.Idempotency.EfCore` build/pack clean with `TreatWarningsAsErrors`, zero warnings). The `01.Core` `LoggingEventIdRanges.Idempotency = 18000` entry has shipped in code (verified this session), so C-05/C-10's prior soft block is resolved. Tests phase is `◐`: T-05 and T-10 (fail-open/fail-closed) are `●` — genuinely executed against a real unreachable endpoint, no Docker required. T-01–T-04 (`.Redis`) and T-06–T-09 (`.EfCore`) — the concurrent-reservation/tenant-isolation/expiry-reclaim proofs against real Testcontainers Redis/PostgreSQL — are written in full but **not executed**: no Docker daemon was reachable in this implementation session. 40 additional unit/DI-resolution tests beyond the T-0x list (key-builder, exception classifiers, options validation, `IHost.StartAsync()` DI resolution including the missing-`ITenantContextAccessor` fail-fast) were written and DID run green (20/20 per package). Published is `◐`: P-01/P-02/P-04 done (NuGet metadata, clean `dotnet pack` for both packages, zero `<Version>` elements verified); P-03 (`consumer-verify` harness) and P-05 (final `state-map-phase` closing P-454/P-455) remain — P-05 correctly waits on Tests reaching `●`, which itself waits on a Docker-available session running T-01–T-04/T-06–T-09.

---

## Changelog

- [2026-08-26] Domain founded — folder, state-map, and CLAUDE.md created ahead of WO-070 dispatch (P-454/P-455)
- [2026-08-26] First dispatch processed. All six phases populated end to end for both packages (D-01–D-09, S-01–S-08, C-01–C-10, T-01–T-10, DO-01–DO-06, P-01–P-05 — 48 tasks total). Key design corrections recorded against the work order's literal wording: (1) two physical store classes per package, not three — `IIdempotencyResponseStore`'s documented `is`-pattern-match replay detection requires `IIdempotencyKeyStore`/`IIdempotencyResponseStore` on the same instance (D-01); (2) tenant resolution reuses `07.Messaging.Abstractions`'s existing `ITenantContextAccessor` rather than inventing a new local contract (D-02); (3) `.EfCore` deliberately does not extend `SharedKernelDbContext` — its `SoftDeleteInterceptor` would silently defeat the retention cleanup recipe's hard deletes (D-06). Cross-Domain Dependencies updated to reflect that `01.Core`'s `Idempotency = 18000` `LoggingEventIdRanges` entry is now design-locked (`SK.01.LoggingRangesNewDomains`) though not yet shipped in code.
- [2026-09-04] Implementation session: `SK.18.Design`/`SK.18.Scaffold`/`SK.18.Core`/`SK.18.Docs` → `●` (state-map-phase). Both packages implemented, build/pack clean, zero warnings. Two further deliberate corrections recorded beyond D-01/D-02/D-06 above: (4) EfCore's `ON CONFLICT DO UPDATE` reservation SQL also sets `response = NULL` on reclaim — not in D-05's literal wording — because otherwise a reclaimed row's stale response from a fully-expired prior episode would incorrectly resurface (the only staleness guard on read is `ExpiresAtUtc`, which reclaim resets to a fresh value in the same statement); (5) both packages' store classes are registered `Scoped`, not the work order's literal "singleton instance" framing (D-09) — `ITenantContextAccessor` is conventionally `Scoped` in this platform and a singleton cannot safely consume a scoped dependency under `ValidateScopes=true`. `01.Core`'s `LoggingEventIdRanges.Idempotency = 18000` was confirmed already shipped in code, unblocking C-05/C-10. `SK.18.Tests`/`SK.18.Published` remain `◐`: T-05/T-10 (fail-open/fail-closed) and P-01/P-02/P-04 (NuGet metadata, `dotnet pack`, no `<Version>` element) are `●` with real execution evidence; T-01–T-04/T-06–T-09 (Testcontainers Redis/PostgreSQL concurrency proofs) are written but unexecuted — no Docker daemon reachable this session — and P-03 (`consumer-verify` harness)/P-05 (final close) remain open pending a Docker-available session. Root `state-map.md` Phase Backlog P-454/P-455 intentionally left `◐`/`○` — not yet propagated, since promotion only fires per Phase Key Registry note once every phase key including Tests/Published is `●` (Step S8c).
- [2026-09-04] Coordinator follow-up — rows left open by this domain's own implementation session because the blocker sat outside its lane are now closed: `.slnx` registration was performed centrally (149 projects, full-solution build 0 errors), the root Phase Backlog promotion ran, and the Docker daemon became available so every Testcontainers-backed proof executed for real. Verified this session: `SharedKernel.Idempotency.Redis.Tests` 25/25, `SharedKernel.Idempotency.EfCore.Tests` 25/25, `SharedKernel.Scheduling.Tests` 35/35 (including `MultiReplicaSingleExecutionTests`), `SharedKernel.Reporting.*.Tests` all green, full solution 6,945 passed / 0 failed / 2 skipped across 66 assemblies (coordinator)
