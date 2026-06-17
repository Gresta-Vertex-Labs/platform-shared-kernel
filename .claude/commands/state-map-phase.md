You are the state-map phase recorder for Platform.SharedKernel.

This command operates in two modes depending on the input:

- **Root mode** — updates a domain row directly in the root `state-map.md`
- **Sub-map mode** — updates a task inside a domain's own `{NN}.{Name}/state-map.md`, then propagates to root when the phase key is fully complete

Read the input below and determine which mode applies before doing anything else.

---

**Input:**
$ARGUMENTS

---

## Mode Detection

If the input contains `phase_key:` (e.g. `phase_key: SK.02.Scaffold`), use **Sub-map mode**.
Otherwise use **Root mode**.

---

# ROOT MODE

## Step R1 — Parse the input

Extract the following fields. All are required; if any are missing or ambiguous, list what is missing and stop — do not edit anything.

| Field | Expected values |
|-------|----------------|
| `domain` | A number 00–17 (e.g. `01`, `06`) or the folder name (e.g. `Core`, `Persistence`) |
| `phase` | One of: `Design`, `Scaffold`, `Core`, `Tests`, `Docs`, `Published` |
| `state` | One of: `○` (not started), `◐` (in progress), `●` (complete), `⚑` (blocked) |
| `summary_done` | One sentence describing what has been completed. Use `—` if nothing is done yet. |
| `summary_next` | One sentence describing what the next action is. Use `—` if nothing follows. |
| `blocker` | _(Only required when state = `⚑`)_ One sentence describing what is blocking and which domain or external dependency it waits on. |

Resolve a domain name to its canonical folder prefix (e.g. `Persistence` → `06`, `Core` → `01`) using the folder map in CLAUDE.md if needed.

## Step R2 — Read root state-map

Read `state-map.md` at the repo root in full. Never read any sub state-map or any other file.

## Step R3 — Update the Domain Summary Board

Find the row for the parsed domain. Update in-place:
- **Current Phase** → parsed `phase`
- **State** → parsed `state` symbol (wrapped in backticks)
- **Summary: Done** → parsed `summary_done`
- **Summary: Next** → parsed `summary_next`

Do not add or remove rows. Do not reorder rows. Do not change any other row.

## Step R4 — Update Active Work

| Condition | Action |
|-----------|--------|
| `state = ◐` | Replace placeholder (or add row) for this domain. Format: `\| [NN.Name](NN.Name/state-map.md) \| Phase \| summary_next \|` |
| `state = ●` or `state = ○` | Remove this domain's row. If no rows remain, restore `_Nothing in progress — all domains at ○ Not Started._` |
| `state = ⚑` | Remove this domain's row from Active Work if it exists |

When first populating the table, replace the placeholder with header + row:
```
| Domain | Current Phase | Focus (one line) |
|--------|---------------|-----------------|
| [NN.Name](NN.Name/state-map.md) | Phase | summary_next |
```

## Step R5 — Update Blocked

| Condition | Action |
|-----------|--------|
| `state = ⚑` | Replace placeholder (or add row) for this domain. Format: `\| [NN.Name](NN.Name/state-map.md) \| Phase \| blocker \|` |
| `state ≠ ⚑` | Remove this domain's row. If no rows remain, restore `_No blockers._` |

When first populating the table, replace the placeholder with header + row:
```
| Domain | Blocked Phase | Blocker |
|--------|--------------|---------|
| [NN.Name](NN.Name/state-map.md) | Phase | blocker |
```

## Step R6 — Recalculate Overall Progress

Re-count the Domain Summary Board and update every count. Counts must always sum to 18.
- **○ Not Started** = rows where State is `○`
- **◐ In Progress** = rows where State is `◐`
- **⚑ Blocked** = rows where State is `⚑`
- **● Design / Scaffold / Core / Tests / Docs / Published** = rows where State is `●`, grouped by Current Phase

## Step R7 — Append changelog entry

Append exactly one line to `## Changelog`:
```
- [YYYY-MM-DD] {domain} → {phase} ({state symbol}) — {summary_done if ● else summary_next} (state-map-phase)
```
Use today's date. Keep under 120 characters.

## Step R8 — Report

Output ≤ 5 bullet points: what changed and in which section. If stopped due to parse error, list missing fields and a corrected example call.

---

# SUB-MAP MODE

Updates a single task inside a domain's own state-map, recalculates phase progress, and propagates to the root state-map when the phase key's promotion condition is met.

## Step S1 — Parse the input

Extract the following fields. All are required; stop and list missing fields if any are absent.

| Field | Expected values |
|-------|----------------|
| `phase_key` | Format `SK.{NN}.{Phase}` — e.g. `SK.02.Scaffold`. Identifies domain number and root phase. |
| `task_id` | The task ID from the phase table — e.g. `S-01`, `C-04`, `T-03` |
| `state` | One of: `○`, `◐`, `●`, `⚑` |
| `blocker` | _(Only required when state = `⚑`)_ One sentence describing the blocker. |

Derive the domain number (`NN`) and root phase name from `phase_key` (e.g. `SK.02.Scaffold` → domain `02`, root phase `Scaffold`).

Resolve the sub state-map path: look up the domain number in the root CLAUDE.md folder map to get the folder name (e.g. `02` → `02.Caching`), then target `{folder}/state-map.md`.

## Step S2 — Read the sub state-map

Read the resolved sub state-map file in full. Never read the root state-map in this step.

## Step S3 — Update the task row

Find the phase section tagged `<!-- phase-key: {phase_key} -->` in the sub state-map.
Within that section's task table, find the row with matching `ID` = `task_id`.
Update the `State` column to the parsed `state` symbol (wrapped in backticks).

Do not add or remove task rows. Do not change any other row or section.

## Step S4 — Update sub Active Work and Blocked

Apply the same logic as Root mode Steps R4 and R5, but on the sub state-map's `## Active Work` and `## Blocked` sections.

Active Work format (sub-map variant):
```
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| {task description} | {phase_key} | {package} | {state} |
```

## Step S5 — Recalculate sub Overall Progress

Re-count all task rows in each phase section. Update the `● Done` and `State` columns in `## Overall Progress` for the affected phase key row.

Phase key state rules:
- `○` if Done = 0
- `◐` if Done > 0 and Done < Total
- `●` if Done = Total
- `⚑` if any task in that phase is `⚑`

## Step S6 — Append sub-map changelog entry

Append exactly one line to the sub state-map's `## Changelog`:
```
- [YYYY-MM-DD] {task_id} → {state} in {phase_key} — {brief description} (state-map-phase)
```

## Step S7 — Check promotion condition

Read the `## Phase Key Registry` table in the sub state-map.
Find the row for the updated `phase_key` and check its **Promotion Condition**.
Then check `## Overall Progress`: is the phase key's `State` now `●`?

- **If yes (all tasks ●):** proceed to Step S8 to propagate to root.
- **If no:** skip S8 and go directly to Step S9.

## Step S8 — Propagate to root state-map

Read the root `state-map.md` in full.

Determine the correct values to write:
- `domain` → derived from phase key (e.g. `SK.02.*` → domain `02` = `Caching`)
- `phase` → the root phase name from the Phase Key Registry row
- `state` → `●`
- `summary_done` → one sentence summarizing what the completed phase delivered
- `summary_next` → one sentence describing the next phase to begin (or `—` if Published)

Then apply Root mode Steps R3 through R7 on the root `state-map.md` using these values.
Append the root changelog entry as:
```
- [YYYY-MM-DD] {domain} → {phase} (●) — promoted from {phase_key} (state-map-phase)
```

## Step S8a — Close individual Phase Backlog entries

**Only execute this step if promotion fired in Step S7** (i.e., the phase key's Overall Progress state became `●`).
**Skip this step if `phase` = `Published`** — Step S8b performs the domain-wide bulk-close at that milestone.

Purpose: close the individual `### P-NNN` Phase Backlog entries in the root `state-map.md` that correspond to the just-completed `phase_key`. This makes every WO-specific or capability-extension phase self-closing without waiting for the domain to reach Published.

### Determining which Phase Backlog IDs to close

Using the Phase Key Registry row for the completed `phase_key` (already in memory from Step S7 — do not re-read the sub state-map):

**Case 1 — `Root Backlog ID` column present and non-empty**
If the Phase Key Registry table has a `Root Backlog ID` column and the row's value is not `—` or blank, parse it as a comma-separated list of Phase Backlog IDs (e.g. `P-125, P-126`). Proceed to **Closing the entries**.

**Case 2 — `Maps to Root Phase` begins with a `P-NNN` token**
If Case 1 does not apply, check whether the `Maps to Root Phase` value begins with `P-` followed by digits (e.g. `P-042 Error.BusinessRule Factory`). If yes, extract that leading `P-NNN` token as the single Phase Backlog ID. Proceed to **Closing the entries**.

**Case 3 — Standard lifecycle phase**
If `Maps to Root Phase` is exactly one of `Design`, `Scaffold`, `Core`, `Tests`, `Docs`, `Published`: **skip this step entirely**. Standard lifecycle phases update only the Domain Summary Board — they have no individual Phase Backlog entry.

**Case 4 — No match**
If none of Cases 1–3 apply: skip silently. This covers WO-specific phases whose Phase Key Registry row does not yet have a `Root Backlog ID` column. To activate automatic closing for such a phase, add the `Root Backlog ID` column to the sub state-map's Phase Key Registry and fill in the P-NNN value.

### Closing the entries

Using the **already-read** root `state-map.md` (Step S8 already read it — do not re-read the file again):

For each Phase Backlog ID identified above:
1. Find the heading line `### {ID} — ` in `## Phase Backlog`.
2. Check its `**Status:**` line:
   - `` `●` Complete `` — skip (nothing to update).
   - `` `◐` Dispatched `` or `` `○` Pending `` — change `**Status:**` to `` `●` Complete ``.
3. Do not modify any other field in that entry.

After updating at least one entry, append exactly one line to the root `## Changelog`:
```
- [YYYY-MM-DD] Phase Backlog {comma-separated closed IDs} → ● Complete — {phase_key} done (state-map-phase)
```
If all identified entries were already `●` Complete, skip the changelog line silently.

## Step S8b — Close Phase Backlog entries (Published phase only)

**Only execute this step if `phase` = `Published`** (i.e. the domain just reached its final milestone).

In the root `state-map.md`, scan every entry under `## Phase Backlog`.
For each entry whose **Domain** field matches the domain that just completed:
- If its **Status** is `○` Pending or `◐` Dispatched, update it to `●` Complete.
- Do not change entries for other domains.
- Do not add or remove entries — only update the `**Status:**` line in-place.

Append exactly one changelog line per closed batch:
```
- [YYYY-MM-DD] Phase Backlog entries for {domain} closed → ● Complete — {domain} reached Published (state-map-phase)
```

If no entries match (Phase Backlog is empty or all already `●`), skip silently — do not append a changelog line.

## Step S9 — Report

Output ≤ 5 bullet points: task updated, phase key state after update, whether root propagation fired and what changed if it did. If stopped due to parse error, list missing fields and a corrected example.

---

## Format contract (never violate these)

- Root mode operates on exactly one file: `state-map.md` at the repo root.
- Sub-map mode operates on one sub state-map file, plus the root `state-map.md` only if promotion fires.
- Never add new sections, rename sections, or reorder sections in either file.
- Never edit the `## Legend`, `## Phase Key Registry`, or phase list during command execution — they are maintained by arch-planner agents, not by this command at runtime.
- Sub state-map Phase Key Registries may include an optional fourth column `Root Backlog ID`. When present and non-empty (not `—`), Step S8a uses it to resolve which `### P-NNN` Phase Backlog entries in the root state-map to close when that phase key completes. Add this column when creating new WO-specific phase keys so they self-close correctly.
- The root `## Domain Summary Board` is always exactly 18 rows — no insertions, no deletions.
- State symbols in tables must always be wrapped in backticks: `` `○` ``, `` `◐` ``, `` `●` ``, `` `⚑` ``.
- Active Work and Blocked tables use the exact column headers shown above — never alter them.
- Both Changelogs are append-only — never edit or remove existing entries.
- The commented `<!-- Format when active: ... -->` blocks are documentation — leave them untouched.
- Root Overall Progress counts must always sum to 18 after every call.
- Sub-map Overall Progress `● Done` counts must always equal the number of `●` task rows in that phase section.
