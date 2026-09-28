---
description: Record task or phase progress on a domain state-map and propagate a finished phase to the root board
argument-hint: "phase_key: SK.NN.Key | tasks: C-01, T-02 | state: ● [| summary: …] [| blocker: …]"
---

You are the state-map recorder for Platform.SharedKernel. You apply the state-map protocol in `.claude/agents/_common.md` ("The state-map protocol"): you flip task states on a domain board, close a finished phase into one line, and carry the result to the root board. Read that section before editing.

**Input:**
$ARGUMENTS

---

## Mode detection

- Input contains `phase_key:` → **Domain mode** (the normal path, used by phase implementers).
- Input contains `domain:` and no `phase_key:` → **Board-row mode** (refresh one Domain Summary Board row on the root).
- Neither → list the two input shapes below and stop.

---

# DOMAIN MODE

## D1 — Parse

| Field | Required | Values |
| --- | --- | --- |
| `phase_key` | yes | `SK.{NN}.{Key}`; `NN` selects the folder (`06` → `06.Persistence`) |
| `tasks` | no | Comma-separated task IDs (`C-01, T-03`), or `all`. Omitted = `all`. |
| `state` | yes | `○`, `◐`, `●`, `⚑` (or `—` for a task dropped as not applicable) |
| `summary` | when the phase closes | One line: the outcome, as it will appear under `## Completed Phases` |
| `blocker` | when `state` is `⚑` | One sentence: what blocks it, with the on-disk evidence, and what it waits on |

Missing or ambiguous required fields: list them and stop without editing.

## D2 — Read

Read `{NN}.{Folder}/state-map.md` in full. Find the Open Work entry whose heading starts `### {phase_key} —`. Not found:
- If the key is under `## Completed Phases` → report "already closed" and stop.
- Otherwise → report "no open entry for {phase_key}" and stop.

## D3 — Update tasks

- Set the `State` cell of each named task (or every task for `all`) to the given state. An unknown task ID: report it and stop without editing.
- Set the entry's heading marker: `⚑` if any task is `⚑` and no task can proceed without it; else `◐` if any task is `◐` or `●`; else `○`.
- `⚑`: add or update the phase's entry under `## Blocked` (`- **{phase_key}** {task IDs}: {blocker}`). A state other than `⚑` for a previously blocked task: remove its line; if `## Blocked` is empty, write `None.`

## D4 — Close the phase (only when every task is `●` or `—`)

1. Remove the Open Work entry. If no entries remain, write `None — every phase in this domain is complete.`
2. Add at the **top** of `## Completed Phases`: `- {phase_key} ● {summary} ({WO-NNN/P-NNN from the entry}) ({today})`. No `summary` given: derive one line from the entry title.
3. `## Phase Key Registry`: set the key's row to `●` (add the row if the planner never did), keeping the task-ID range in the Phase column.
4. `## Package Board`: for each package the tasks named, set Status `●` once it exists with no other open phase touching it, and update Notes if the phase changed what the package is. Remove rows the phase retired (or mark them `⊘` with a reason).
5. `## Cross-Domain Dependencies` and `## Blocked`: remove lines this phase resolved.

## D5 — Domain changelog

Add `- [{today}] {phase_key}: {what changed in one line}` at the top of `## Changelog` (newest first) and trim the section to its **last 5** entries. Record task flips without a phase close only if they change what a reader should know (a new `⚑`, a phase started); otherwise skip the changelog.

## D6 — Propagate to the root (after D4, or after a new `⚑`)

Read the root `state-map.md`.

- **Phase closed:** every P-entry under `## Open Work` whose `**Phase key:**` equals `{phase_key}` (or whose ID appears on the entry's `**Work order:**` line) → `**Status:** `●` Complete`. When every P-entry of a `### WO-NNN` block is `●` or `⊘`, replace the whole block with one line at the top of `## Completed Work Orders`: `- WO-NNN ● {title} (P-aaa–P-bbb) ({today})`.
- **New `⚑`:** add `- **{NN}.{Folder}** {phase_key}: {blocker}` under root `## Blocked`; remove it again when the blocker clears. `None.` when empty.
- **Domain Summary Board:** refresh the domain's row (keep the columns the board declares; cells are one sentence): State `◐` if the domain has an in-progress entry, `⚑` if only blocked entries remain, `○` if only not-started entries remain, `●` if `## Open Work` is empty; the open-work cell names the next open phase key or `—`.
- **Changelog:** one line `- [{today}] {NN}.{Folder} {phase_key} ● (state-map-phase)` at the top of root `## Changelog`; trim to the **last 10**.

Root `## ID Counters` are never touched here.

## D7 — Report

≤ 5 bullets: tasks changed, whether the phase closed, the Completed Phases line, root P-entries/WO collapsed, board row state.

---

# BOARD-ROW MODE

Input: `domain: {NN or folder} | state: {○ ◐ ● ⚑} | summary: {one sentence}` (`blocker:` required for `⚑`).

1. Read the root `state-map.md`; find the domain's row in `## Domain Summary Board` (error and stop if none).
2. Set State and the summary/open-work cell. For `⚑` add the root `## Blocked` line; for any other state remove the domain's line.
3. Add one root `## Changelog` line and trim to 10.
4. Report in one line.

Use this only for board corrections (e.g. `arch-lead` marking a domain that just received new work, `/sync-brain` fixing a stale row). Phase progress always goes through Domain mode.

---

## Format contract

- Headings are fixed (see `_common.md`); never add, rename or reorder sections, and never re-expand collapsed history.
- One line per completed phase and per completed work order; Changelogs capped at 5 (domain) and 10 (root).
- Dates are absolute (`YYYY-MM-DD`, today).
- Writes only the one domain `state-map.md` and the root `state-map.md`.
