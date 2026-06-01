# 06.Persistence — State Map

> **What this file is:** Phase and task tracker for all work within `06.Persistence`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.06.{Phase}` to propagate that milestone to the root state-map.

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

| Phase Key | Maps to Root Phase | Promotion Condition |
| --- | --- | --- |
| `SK.06.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.06.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.06.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.06.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.06.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.06.Published` | Published | All tasks in Phase: Published are `●` |

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement IRepository<TAggregate,TId> | SK.06.Core | SharedKernel.Persistence.Abstractions | ◐ |
-->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| Example blocked task | SK.06.Core | Waiting on upstream decision |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Persistence.Abstractions` | — | `○` | Zero ORM dependencies; references Primitives + Domain |
| `SharedKernel.Persistence.EfCore` | — | `○` | EF Core 10.x; references Abstractions + Domain |
| `SharedKernel.Persistence.PostgreSQL` | — | `○` | Npgsql + pgvector + JSONB; references EfCore |
| `SharedKernel.Persistence.Dapper` | — | `○` | Read-side micro-ORM; references Abstractions + Dapper |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
| --- | --- | --- | --- |
| `SK.06.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`Result<T>`, `Error`, `IClock`) | Available |
| `SK.06.Scaffold` | `01.Core` | `SharedKernel.Core` ProjectReference (`SharedKernelException`, `DomainException`) | Available |
| `SK.06.Scaffold` | `03.Domain` | `SharedKernel.Domain` ProjectReference (`IAggregateRoot<TId>`, `IHasDomainEvents`, `ISpecification<T>`, audit interfaces) | Available |

---

## Phase: Design <!-- phase-key: SK.06.Design -->

> Finalize all interface shapes, interceptor contracts, specification evaluator behavior, and DI extension signatures before any implementation begins.

| ID | Task | Package(s) | State |
| --- | --- | --- | --- |

---

## Phase: Scaffold <!-- phase-key: SK.06.Scaffold -->

> Wire up `.csproj` NuGet references, intra-domain project references, folder structure, solution registration, and empty test stubs — no logic yet.

| ID | Task | Package(s) | State |
| --- | --- | --- | --- |

---

## Phase: Core <!-- phase-key: SK.06.Core -->

> Full implementation of all interfaces, base classes, interceptors, evaluators, type converters, and DI registrations.

| ID | Task | Package(s) | State |
| --- | --- | --- | --- |

---

## Phase: Tests <!-- phase-key: SK.06.Tests -->

> Unit and integration test coverage for all packages. Integration tests use Testcontainers (PostgreSQL). No mocked database connections.

| ID | Task | Package(s) | State |
| --- | --- | --- | --- |

---

## Phase: Docs <!-- phase-key: SK.06.Docs -->

> XML doc comments on all public APIs, README with usage examples.

| ID | Task | Package(s) | State |
| --- | --- | --- | --- |

---

## Phase: Published <!-- phase-key: SK.06.Published -->

> NuGet packaging metadata, pack, publish, and consumer verification.

| ID | Task | Package(s) | State |
| --- | --- | --- | --- |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
| --- | --- | --- | --- | --- | --- |
| `SK.06.Design` | Design | 0 | 0 | 0 | `○` |
| `SK.06.Scaffold` | Scaffold | 0 | 0 | 0 | `○` |
| `SK.06.Core` | Core | 0 | 0 | 0 | `○` |
| `SK.06.Tests` | Tests | 0 | 0 | 0 | `○` |
| `SK.06.Docs` | Docs | 0 | 0 | 0 | `○` |
| `SK.06.Published` | Published | 0 | 0 | 0 | `○` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-06-01] Sub state-map initialized — phase key registry, 6 phases scaffolded at ○, no tasks yet
