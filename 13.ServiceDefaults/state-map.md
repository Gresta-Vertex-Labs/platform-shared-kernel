# 13.ServiceDefaults — State Map

> **What this file is:** Phase and task tracker for all work within `13.ServiceDefaults`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.13.{Phase}` to propagate that milestone to the root state-map.

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
| `SK.13.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.13.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.13.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.13.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.13.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.13.Published` | Published | All tasks in Phase: Published are `●` |

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement AddServiceDefaults() | SK.13.Core | SharedKernel.ServiceDefaults | ◐ |
-->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| Example blocked task | SK.13.Core | Waiting on upstream decision |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
|---------|---------------|:-----:|-------|
| `SharedKernel.ServiceDefaults` | — | `○` | Not yet started — scaffold `.csproj` exists with no implementation; will host `AddServiceDefaults()`, OTel wiring, and the liveness/readiness health check split |
| `SharedKernel.MultiTenancy` | — | `○` | Not yet started — scaffold `.csproj` exists with no implementation; will host the Header/Claim/DB-isolation `ITenantProvider` resolution strategies |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
|---------------|------------------|------|--------|
| `SK.13.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference | Available |
| `SK.13.Core` | `02.Caching` | `SharedKernel.Caching.Abstractions` (`ICacheService`) + concrete `SharedKernel.Caching.Redis*` providers for health checks and telemetry | Available |
| `SK.13.Core` | `06.Persistence` | `SharedKernel.Persistence.Abstractions` (`IDbConnectionFactory`, `DatabaseReadinessResult`) + `SharedKernel.Persistence.EfCore` (`SharedKernelDbContext.CheckReadinessAsync`) | Available |
| `SK.13.Core` | `07.Messaging` | `SharedKernel.Messaging.Abstractions` + concrete `SharedKernel.Messaging.MassTransit` transport options for health checks and OTel wiring | Available |
| `SK.13.Core` | `12.Security` | `SharedKernel.Security.Abstractions` (`ITenantProvider`) + `SharedKernel.Security.Oidc` (`OidcTenantProvider`, delegated to by `ClaimTenantResolutionStrategy`) | Available |

---

## Phase: Design <!-- phase-key: SK.13.Design -->

| ID | Task | Work Order | Package(s) | State |
|----|------|-----------|-----------|:-----:|
| _No tasks defined yet._ | | | | |

---

## Phase: Scaffold <!-- phase-key: SK.13.Scaffold -->

| ID | Task | Work Order | Package(s) | State |
|----|------|-----------|-----------|:-----:|
| _No tasks defined yet._ | | | | |

---

## Phase: Core <!-- phase-key: SK.13.Core -->

| ID | Task | Work Order | Package(s) | State |
|----|------|-----------|-----------|:-----:|
| _No tasks defined yet._ | | | | |

---

## Phase: Tests <!-- phase-key: SK.13.Tests -->

| ID | Task | Work Order | Package(s) | State |
|----|------|-----------|-----------|:-----:|
| _No tasks defined yet._ | | | | |

---

## Phase: Docs <!-- phase-key: SK.13.Docs -->

| ID | Task | Work Order | Package(s) | State |
|----|------|-----------|-----------|:-----:|
| _No tasks defined yet._ | | | | |

---

## Phase: Published <!-- phase-key: SK.13.Published -->

| ID | Task | Work Order | Package(s) | State |
|----|------|-----------|-----------|:-----:|
| _No tasks defined yet._ | | | | |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
|-----------|-------|:-----:|:------:|:---------:|:-----:|
| `SK.13.Design` | Design | 0 | 0 | 0 | `○` |
| `SK.13.Scaffold` | Scaffold | 0 | 0 | 0 | `○` |
| `SK.13.Core` | Core | 0 | 0 | 0 | `○` |
| `SK.13.Tests` | Tests | 0 | 0 | 0 | `○` |
| `SK.13.Docs` | Docs | 0 | 0 | 0 | `○` |
| `SK.13.Published` | Published | 0 | 0 | 0 | `○` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-06-19] Sub state-map initialized — phase key registry and 6 phases scaffolded at `○`, no tasks yet; Package Board lists `SharedKernel.ServiceDefaults` and `SharedKernel.MultiTenancy` as not started; Cross-Domain Dependencies pre-populated against already-available abstractions/providers in `01`/`02`/`06`/`07`/`12`; no phases promoted to root — root backlog items P-010 (WO-003), P-122 (WO-020), and P-132 (WO-021) remain pending formal Design-phase task breakdown (arch-lead)
