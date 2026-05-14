# Platform.SharedKernel — Root State Map

> **What this file is:** Macro-level implementation tracker for the entire SharedKernel mono-repo.  
> **What it is not:** A detailed task or phase log — those live in each domain's own `state-map.md`.  
> **Update policy:** Updated by `/sync-brain` whenever a domain changes phase, active work shifts, or a cross-domain dependency is resolved.

---

## Legend

| Symbol | Meaning |
|--------|---------|
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

**Standard build phases (in order):**

```
Design → Scaffold → Core → Tests → Docs → Published
```

> Phase definitions, per-package breakdowns, and task-level detail live exclusively in the domain's `state-map.md`. The root only tracks the current phase and its state.

---

## Active Work

> Domains currently `◐ In Progress`. This section is the first thing to update when work starts or finishes in any domain.

_Nothing in progress — all domains at ○ Not Started._

<!--
Format when active:
| Domain | Current Phase | Focus (one line) |
|--------|---------------|-----------------|
| [01.Core](01.Core/state-map.md) | Scaffold | Creating project structure and .csproj files |
-->

---

## Blocked

> Domains at `⚑ Blocked`. State the blocker and which domain/external thing is blocking.

_No blockers._

<!--
Format when blocked:
| Domain | Blocked Phase | Blocker |
|--------|--------------|---------|
| [07.Messaging](07.Messaging/state-map.md) | Core | Waiting on 04.Contracts envelope type to be finalized |
-->

---

## Domain Summary Board

> One row per domain. **Summary: Done** and **Summary: Next** are single sentences max — detail goes in sub state-maps.

| # | Domain | Current Phase | State | Summary: Done | Summary: Next |
|---|--------|---------------|:-----:|---------------|---------------|
| 00 | [Governance](00.Governance/state-map.md) | — | `○` | — | — |
| 01 | [Core](01.Core/state-map.md) | — | `○` | — | — |
| 02 | [Caching](02.Caching/state-map.md) | — | `○` | — | — |
| 03 | [Domain](03.Domain/state-map.md) | — | `○` | — | — |
| 04 | [Contracts](04.Contracts/state-map.md) | — | `○` | — | — |
| 05 | [Application](05.Application/state-map.md) | — | `○` | — | — |
| 06 | [Persistence](06.Persistence/state-map.md) | — | `○` | — | — |
| 07 | [Messaging](07.Messaging/state-map.md) | — | `○` | — | — |
| 08 | [Storage](08.Storage/state-map.md) | — | `○` | — | — |
| 09 | [Search](09.Search/state-map.md) | — | `○` | — | — |
| 10 | [Intelligence](10.Intelligence/state-map.md) | — | `○` | — | — |
| 11 | [Communication](11.Communication/state-map.md) | — | `○` | — | — |
| 12 | [Security](12.Security/state-map.md) | — | `○` | — | — |
| 13 | [ServiceDefaults](13.ServiceDefaults/state-map.md) | — | `○` | — | — |
| 14 | [Presentation](14.Presentation/state-map.md) | — | `○` | — | — |
| 15 | [Integration](15.Integration/state-map.md) | — | `○` | — | — |
| 16 | [Testing](16.Testing/state-map.md) | — | `○` | — | — |
| 17 | [Workflows](17.Workflows/state-map.md) | — | `○` | — | — |

---

## Cross-Domain Dependencies

> Active coordination points between domains. Remove a row once the dependency is resolved.

_No active cross-domain dependencies._

<!--
Format when active:
| Waiting Domain | Needs From | What | Status |
|----------------|------------|------|--------|
| 05.Application | 03.Domain | IDomainEvent base type finalized | ◐ In progress in 03 |
-->

---

## Overall Progress

> Counts updated by `/sync-brain` whenever the board above changes.

| Phase | Domains |
|-------|---------|
| ● Published | 0 |
| ● Docs | 0 |
| ● Tests | 0 |
| ● Core | 0 |
| ● Scaffold | 0 |
| ● Design | 0 |
| ◐ In Progress | 0 |
| ⚑ Blocked | 0 |
| ○ Not Started | 18 |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} ({domain(s) affected}) — {trigger}`.

- [2026-05-14] Root state-map template created — `/sync-brain`
