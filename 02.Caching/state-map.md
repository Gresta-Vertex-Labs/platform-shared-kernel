# 02.Caching — State Map

> **What this file is:** Phase and task tracker for all work within `02.Caching`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.02.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
|--------|---------|
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition |
|-----------|-------------------|-------------------|
| `SK.02.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.02.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.02.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.02.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.02.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.02.Published` | Published | All tasks in Phase: Published are `●` |

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Define ICacheService contract | SK.02.Design | SharedKernel.Caching | ◐ |
-->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| C-04 Redis L2 backplane | SK.02.Core | Waiting for StackExchange.Redis AOT verdict |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
|---------|--------------|:-----:|-------|
| `SharedKernel.Caching` | Core | `●` | ICacheService, CachePolicy, STJ context, AddSharedKernelCaching — all implemented |
| `SharedKernel.Caching.Redis` | Core | `●` | Redis L2 backplane, IDistributedLockService via RedLock, AddRedisL2, AddRedisDistributedLocking — all implemented |

---

## Phase: Design <!-- phase-key: SK.02.Design -->

> Finalize all interface contracts, policy shapes, and DI extension signatures before any implementation begins.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-01 | Define `ICacheService` method signatures | SharedKernel.Caching | `●` |
| D-02 | Define `CachePolicy` record shape and factory methods | SharedKernel.Caching | `●` |
| D-03 | Define `IDistributedLockService` method signatures | SharedKernel.Caching.Redis | `●` |
| D-04 | Define DI extension signatures (`AddSharedKernelCaching`, `AddRedisL2`, `AddRedisDistributedLocking`) | Both | `●` |
| D-05 | Confirm FusionCache NuGet version and AOT compatibility | SharedKernel.Caching | `●` |
| D-06 | Confirm RedLock.net NuGet version and AOT status | SharedKernel.Caching.Redis | `●` |

---

## Phase: Scaffold <!-- phase-key: SK.02.Scaffold -->

> Wire up .csproj NuGet references, folder structure, solution registration, and empty test stubs — no logic yet.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| S-01 | Add FusionCache + STJ NuGet refs to `SharedKernel.Caching.csproj` | SharedKernel.Caching | `●` |
| S-02 | Add StackExchange.Redis + FusionCache.Backplane.Redis + RedLock.net refs to `SharedKernel.Caching.Redis.csproj` | SharedKernel.Caching.Redis | `●` |
| S-03 | Add `SharedKernel.Caching` project reference to `SharedKernel.Caching.Redis.csproj` | SharedKernel.Caching.Redis | `●` |
| S-04 | Create folder structure (`Abstractions/`, `Policies/`, `Extensions/`) inside each project | Both | `●` |
| S-05 | Register both projects in `Platform.SharedKernel.slnx` under solution folder `02.Caching` | Both | `●` |
| S-06 | Stub empty `.Tests` projects with xUnit package reference | Both | `●` |

---

## Phase: Core <!-- phase-key: SK.02.Core -->

> Full implementation of all interfaces, FusionCache wiring, Redis L2 backplane, RedLock, and DI registration.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| C-01 | Implement `ICacheService` backed by FusionCache L1 | SharedKernel.Caching | `●` |
| C-02 | Implement `CachePolicy` sealed record with factory methods and tag support | SharedKernel.Caching | `●` |
| C-03 | Implement `AddSharedKernelCaching` DI extension | SharedKernel.Caching | `●` |
| C-04 | Implement Redis L2 backplane wiring (FusionCache + StackExchange.Redis) | SharedKernel.Caching.Redis | `●` |
| C-05 | Implement `AddRedisL2` DI extension (optional, additive) | SharedKernel.Caching.Redis | `●` |
| C-06 | Implement `IDistributedLockService` via RedLock.net | SharedKernel.Caching.Redis | `●` |
| C-07 | Implement `AddRedisDistributedLocking` DI extension | SharedKernel.Caching.Redis | `●` |
| C-08 | Add STJ source-gen context base class and serialization wiring | SharedKernel.Caching | `●` |
| C-09 | Verify L1-only silent fallback when Redis is not registered | Both | `●` |

---

## Phase: Tests <!-- phase-key: SK.02.Tests -->

> Unit and integration test coverage. Redis tests must use Testcontainers.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| T-01 | Unit: `ICacheService` get, set, remove, tag-eviction | SharedKernel.Caching.Tests | `●` |
| T-02 | Unit: `CachePolicy` factory methods and immutability | SharedKernel.Caching.Tests | `●` |
| T-03 | Stampede: parallel `GetOrSetAsync` — factory called exactly once | SharedKernel.Caching.Tests | `●` |
| T-04 | Integration: Redis L2 round-trip via Testcontainers | SharedKernel.Caching.Redis.Tests | `●` |
| T-05 | Integration: RedLock acquire success, timeout (returns null), release, expiry | SharedKernel.Caching.Redis.Tests | `●` |
| T-06 | Integration: L1-only fallback when Redis container is stopped mid-run | SharedKernel.Caching.Redis.Tests | `●` |

---

## Phase: Docs <!-- phase-key: SK.02.Docs -->

> XML doc comments on all public APIs, README with usage examples, configuration reference.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| DO-01 | XML doc all public interfaces and types | Both | `●` |
| DO-02 | Write `02.Caching/README.md` with usage examples | Both | `●` |
| DO-03 | Document `CachePolicy` presets and tag-based eviction pattern | SharedKernel.Caching | `●` |
| DO-04 | Document DI registration options with annotated code samples | Both | `●` |

---

## Phase: Published <!-- phase-key: SK.02.Published -->

> NuGet packaging metadata, pack, publish, and consumer verification.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| P-01 | Add NuGet metadata to both `.csproj` files (authors, description, version, license) | Both | `●` |
| P-02 | Pack and publish `SharedKernel.Caching` to feed | SharedKernel.Caching | `●` |
| P-03 | Pack and publish `SharedKernel.Caching.Redis` to feed | SharedKernel.Caching.Redis | `●` |
| P-04 | Verify dependency graph in a consumer test project | Both | `●` |

---

## Cross-Domain Dependencies

_No active cross-domain dependencies._

<!--
Format when active:
| This Phase Key | Needs From Domain | What | Status |
|---------------|------------------|------|--------|
| SK.02.Core | 01.Core | IClock type finalized | ◐ In progress |
-->

---

## Overall Progress

> Counts updated whenever a task state changes. Total tasks: 35.

| Phase Key | Phase | Total | ● Done | State |
|-----------|-------|:-----:|:------:|:-----:|
| `SK.02.Design` | Design | 6 | 6 | `●` |
| `SK.02.Scaffold` | Scaffold | 6 | 6 | `●` |
| `SK.02.Core` | Core | 9 | 9 | `●` |
| `SK.02.Tests` | Tests | 6 | 6 | `●` |
| `SK.02.Docs` | Docs | 4 | 4 | `●` |
| `SK.02.Published` | Published | 4 | 4 | `●` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-05-14] Sub state-map initialized — phase key registry, all 6 phases scaffolded at ○ (35 tasks total)
- [2026-05-15] D-01→D-06 → ● in SK.02.Design — all interface contracts, CachePolicy shape, DI signatures, and NuGet versions confirmed (state-map-phase)
- [2026-05-15] S-01→S-06 → ● in SK.02.Scaffold — NuGet refs, folder structure, solution registration, and xUnit test stubs all complete (state-map-phase)
- [2026-05-15] C-01→C-09 → ● in SK.02.Core — FusionCache L1/L2, RedLock, DI extensions, STJ context, L1 fallback all implemented (state-map-phase)
- [2026-05-15] T-01→T-06 → ● in SK.02.Tests — unit + integration tests all passing (80 tests, 0 failed) (state-map-phase)
- [2026-05-15] DO-01→DO-04 → ● in SK.02.Docs — XML docs verified, README with usage examples and config reference written (state-map-phase)
- [2026-05-15] P-01→P-04 → ● in SK.02.Published — NuGet metadata added, packages packed to local feed, consumer dependency graph verified (state-map-phase)
