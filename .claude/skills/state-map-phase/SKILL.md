---
name: state-map-phase
description: Record task or phase progress on a domain state-map and carry a finished phase to the root board, following the state-map protocol in .claude/agents/_common.md. Used by the phase implementers; use it directly to fix a board row.
disable-model-invocation: true
argument-hint: "phase_key: SK.NN.Key | tasks: C-01, T-02 | state: ● [| summary: …] [| blocker: …]"
---

You are the state-map recorder for Platform.SharedKernel. You apply the state-map protocol in `.claude/agents/_common.md` ("The state-map protocol"): you flip task states on a domain board, remove a finished phase, and carry the result to the root board. Boards keep no history; `git log` is the record. Read that section before editing.

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
| `phase_key` | yes | `SK.{NN}.{Key}`; `NN` selects the domain (`06` → `06.Persistence`, whose folder the root `CLAUDE.md` domain table gives) |
| `tasks` | no | Comma-separated task IDs (`C-01, T-03`), or `all`. Omitted = `all`. |
| `state` | yes | `○`, `◐`, `●`, `⚑` (or `—` for a task dropped as not applicable) |
| `summary` | no | One line: the outcome, used in the report (and suggested for the commit message) |
| `blocker` | when `state` is `⚑` | One sentence: what blocks it, with the on-disk evidence, and what it waits on |

Missing or ambiguous required fields: list them and stop without editing.

## D2 — Read

Read `{folder}/state-map.md` in full. Find the Open Work entry whose heading starts `### {phase_key} —`. Not found:
- If `git log --oneline -S"{phase_key}"` finds it → report "already closed" and stop.
- Otherwise → report "no open entry for {phase_key}" and stop.

## D3 — Update tasks

- Set the `State` cell of each named task (or every task for `all`) to the given state. An unknown task ID: report it and stop without editing.
- Set the entry's heading marker: `⚑` if any task is `⚑` and no task can proceed without it; else `◐` if any task is `◐` or `●`; else `○`.
- `⚑`: add or update the phase's entry under `## Blocked` (`- **{phase_key}** {task IDs}: {blocker}`). A state other than `⚑` for a previously blocked task: remove its line; if `## Blocked` is empty, write `None.`

## D4 — Close the phase (only when every task is `●` or `—`)

1. Remove the Open Work entry. If no entries remain, write `None — every phase in this domain is complete.`
2. `## Phase Key Registry`: delete the key's row. If no rows remain, write the line `No open phase keys. Closed keys live in git log: check a new key is unused with git log --oneline -S"SK.NN.Key".`
3. `## Package Board`: for each package the tasks named, set Status `●` once it exists with no other open phase touching it, and update Notes if the phase changed what the package is. Remove rows the phase retired.
4. `## Cross-Domain Dependencies` and `## Blocked`: remove lines this phase resolved.

## D5 — Propagate to the root (after D4, or after a new `⚑`)

Read the root `state-map.md`.

- **Phase closed:** every P-entry under `## Open Work` whose `**Phase key:**` equals `{phase_key}` (or whose ID appears on the entry's `**Work order:**` line) → `**Status:** `●` Complete`. When every P-entry of a `### WO-NNN` block is `●` or `⊘`, delete the whole block.
- **New `⚑`:** add `- **{NN}.{Name}** {phase_key}: {blocker}` under root `## Blocked`; remove it again when the blocker clears. `None.` when empty.
- **Domain Summary Board:** refresh the domain's row (keep the columns the board declares; cells are one sentence): State `◐` if the domain has an in-progress entry, `⚑` if only blocked entries remain, `○` if only not-started entries remain, `●` if `## Open Work` is empty; the open-work cell names the next open phase key or `—`.

Root `## ID Counters` are never touched here.

## D6 — Report

≤ 5 bullets: tasks changed, whether the phase closed, root P-entries closed and WO blocks deleted, board row state, and a one-line commit-message suggestion for a closed phase.

---

# BOARD-ROW MODE

Input: `domain: {NN or folder} | state: {○ ◐ ● ⚑} | summary: {one sentence}` (`blocker:` required for `⚑`).

1. Read the root `state-map.md`; find the domain's row in `## Domain Summary Board` (error and stop if none).
2. Set State and the summary/open-work cell. For `⚑` add the root `## Blocked` line; for any other state remove the domain's line.
3. Report in one line.

Use this only for board corrections (e.g. `arch-lead` marking a domain that just received new work, `/sync-brain` fixing a stale row). Phase progress always goes through Domain mode.

---

## Format contract

- Headings are fixed (see `_common.md`); never add, rename or reorder sections, and never add history (no completed lists, no changelogs).
- A finished phase and a finished work order are deleted from the boards.
- Writes only the one domain `state-map.md` and the root `state-map.md`.
