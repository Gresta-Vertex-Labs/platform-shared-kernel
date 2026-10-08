---
name: dispatch-phase
description: Hand the pending P-entries of the root state-map's work orders to their domain {slug}-arch-planner agents, in dependency order, and mark them dispatched. Use after /arch has written a work order and the user wants the domains to plan their phases.
argument-hint: "[P-NNN | WO-NNN]"
disable-model-invocation: true
---

You are the cross-domain phase dispatcher for Platform.SharedKernel. You take the `○` Pending P-entries from the root `state-map.md` `## Open Work`, hand each domain's entries to its `{slug}-arch-planner` agent (one domain at a time, producers before consumers), and mark them `◐` Dispatched with the phase key the planner created.

**Input:**
$ARGUMENTS

> Optional: a Phase ID (`P-583`) or Work Order ID (`WO-091`) to dispatch only that subset. Empty: every `○` Pending P-entry.

---

## Step 1 — Read the pending P-entries

Read the root `state-map.md`: `## ID Counters`, `## Open Work`, `## Blocked`. Each work order is a `### WO-NNN — {title}` block holding P-entries:

```
#### P-NNN — {capability}
**Status:** `○` Pending
**Domain:** {NN}.{Name}
**Depends on:** {None | P-NNN, …}
**Phase key:** —
{what, why, acceptance}
```

Keep the entries with `**Status:**` `○` Pending, filtered by `$ARGUMENTS` when given (`P-\d+` → that entry; `WO-\d+` → that work order's entries). None left:
```
dispatch-phase: no pending phases on the root board.
Run /arch with a capability request to create work orders.
```
Stop.

---

## Step 2 — Check dependencies

For each entry, every `**Depends on:**` P-entry must be `◐`, `●`, no longer on the board (finished work orders are deleted), or `○` but in this same batch (Step 3 orders it first). Otherwise hold the entry back for this run and report it as blocked.

---

## Step 3 — Order the domains

Group the remaining entries by `**Domain:**` and order the groups:
1. **Dependencies first**: if an entry in domain A depends on one in domain B, B goes before A.
2. **Then tier order** of the packages the entries change: Foundation → Model → Abstractions → Adapter → Host → Testing/Tooling (e.g. `SharedKernel.Execution` is Foundation although it sits in `01.Core`). A group spanning tiers takes its lowest.
3. **Then domain number** as the tie-breaker.

The planner for a domain is `{slug}-arch-planner`, with the slug and folder read from the domain table in the root `CLAUDE.md` (match the entry's `**Domain:**` id against the Domain column). Never keep a copy of that table here.

A Domain of `eng` is build work for `devops-lead`: do not dispatch it; report it as "run /devops".

---

## Step 4 — Dispatch, one domain at a time

For each group, in order:

**4a. Spawn the planner** (Agent tool, `subagent_type` = the planner, foreground) with:

```
Read .claude/agents/_common.md, then plan the following P-entries for {NN}.{Name}, in order.
For each: follow the planner method in _common.md — analyse, give a verdict, design one phase
(key SK.{NN}.{PascalName}), write it under ## Open Work in {folder}/state-map.md with its
Phase Key Registry row, and refresh {folder}/CLAUDE.md. A declined entry writes no board
entry; give the verdict and the rule. Report the phase key per P-entry.

---
{full text of P-NNN}
---
{full text of the next P-NNN, if any}
---
```

**4b. Record the result** in the root `state-map.md`, only after the planner returns successfully:
- For each planned entry: `**Status:** `○` Pending` → `**Status:** `◐` Dispatched`, and `**Phase key:** —` → the key the planner reported.
- An entry the planner declined: `**Status:** `⊘` Declined` with the reason in one line under it. If every entry of the work order is now `●` or `⊘`, delete the WO block (the outcome belongs in the commit message, not the board).

If the planner fails or reports no phase key, leave its entries `○` Pending and record the failure for the report.

---

## Step 5 — Report

```
## /dispatch-phase

Dispatched
| Phase | Domain | Planner | Phase key |
| --- | --- | --- | --- |

Declined
| Phase | Domain | Reason |

Held back (open dependencies)
| Phase | Domain | Waiting on |

Not dispatched
| Phase | Reason (build work, planner failure, filtered out) |
```

Omit empty sections.

---

## Format contract

- Writes only the root `state-map.md` (`## Open Work` statuses and phase keys; deletes a finished WO block). Domain boards and `CLAUDE.md` files are written by the planners.
- Status transitions here: `○` Pending → `◐` Dispatched or `⊘` Declined. Never re-dispatches a `◐`/`●` entry.
- Never dispatches out of dependency order.
