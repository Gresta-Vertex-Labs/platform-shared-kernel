# 03.Domain — State Map

> **What this file is:** Phase and task tracker for all work within `03.Domain`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.03.{Phase}` to propagate that milestone to the root state-map.

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
| `SK.03.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.03.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.03.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.03.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.03.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.03.Published` | Published | All tasks in Phase: Published are `●` |

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement Entity<TId> base class | SK.03.Core | SharedKernel.Domain | ◐ |
-->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| Example blocked task | SK.03.Core | Waiting on upstream decision |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
|---------|--------------|:-----:|-------|
| `SharedKernel.Domain` | — | `○` | References Primitives only |

---

## Cross-Domain Dependencies

_No active cross-domain dependencies. `03.Domain` references only `01.Core`._

<!--
Format when active:
| This Phase Key | Needs From Domain | What | Status |
|---------------|------------------|------|--------|
-->

---

## Overall Progress

> Counts updated whenever a task state changes. Total tasks: 0.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
|-----------|-------|:-----:|:------:|:---------:|:-----:|
| `SK.03.Design` | Design | 0 | 0 | 0 | `○` |
| `SK.03.Scaffold` | Scaffold | 0 | 0 | 0 | `○` |
| `SK.03.Core` | Core | 0 | 0 | 0 | `○` |
| `SK.03.Tests` | Tests | 0 | 0 | 0 | `○` |
| `SK.03.Docs` | Docs | 0 | 0 | 0 | `○` |
| `SK.03.Published` | Published | 0 | 0 | 0 | `○` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-05-22] Sub state-map initialized — phase key registry, 6 phases scaffolded at ○, no tasks yet
