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
If `Maps to Root Phase` is exactly one of `Design`, `Scaffold`, `Core`, `Tests`, `Docs`, `Published`: **skip the targeted close in this step** — a lifecycle phase has no single Phase Backlog entry of its own.

This does **not** mean the domain's backlog entries can never auto-close. A work order dispatched into a domain that tracks it through the standard lifecycle keys (rather than a dedicated extension key) has its entries spread across Design/Scaffold/Core/Tests/Docs, so no individual phase completion is the right moment to close them — but the moment _every_ phase key completes is. Step S8c handles exactly that case; do not attempt to compensate here.

Do **not** "fix" this by adding a `Root Backlog ID` column listing those IDs on a lifecycle row: the IDs span several phases, so Case 1 would fire on the first of them to complete and close the entries while later phases are still open — premature closing, strictly worse than leaving them for S8c.

**Case 4 — No match**
If none of Cases 1–3 apply: skip silently. This covers WO-specific extension phase keys whose Phase Key Registry row does not yet have a `Root Backlog ID` column. To activate automatic closing for such a phase, add the `Root Backlog ID` column to the sub state-map's Phase Key Registry and fill in the P-NNN value — correct here precisely because an extension key maps to its own backlog entries one-to-one, unlike the lifecycle keys in Case 3.

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

## Step S8c — Close Phase Backlog entries (domain fully complete)

**Only execute this step if Step S8b did not already run** (i.e. `phase` is not `Published`).

Purpose: close the domain's remaining Phase Backlog entries when a work order was tracked through the standard lifecycle phase keys rather than a dedicated extension key. Case 3 in Step S8a deliberately skips those, and S8b fires only on a `Published` _transition_ — which never happens for a domain that was already Published before the work order arrived. Without this step those entries can never auto-close by any path.

### Trigger condition

Using the sub state-map's `## Overall Progress` table (already read in Step S7 — do not re-read it):

Check **every** row, not only the six lifecycle keys. Proceed only if every row's `State` is `●` or `—` (N/A). If even one row is `○`, `◐`, or `⚑`, **skip this step silently** — an open extension phase key may own the very entries this step would otherwise close.

### Closing the entries (guarded variant of Step S8b)

In the already-read root `state-map.md`, collect every `## Phase Backlog` entry whose **Domain** field matches this domain and whose **Status** is `○` Pending or `◐` Dispatched. Never consider another domain's entries.

For each candidate, read its `#### Acceptance criteria` list and apply one guard before closing:

- **If any criterion names a different domain** — a `{NN}.{Name}` folder token other than this domain's own (e.g. a `16.Testing` fake, a `01.Core` registry entry, a root `CLAUDE.md` row owned by `arch-lead`) — **do not close it.** That criterion cannot have been satisfied by this domain's phase keys, and closing would hide real outstanding work. Add the ID to the still-open list for Step S9 instead.
- **Otherwise** — update `**Status:**` to `` `●` Complete ``, changing nothing else in the entry.

This guard is what separates S8c from S8b's unconditional bulk-close. It is mechanical and deliberately conservative: a false skip costs one hand-close, a false close silently loses work.

Append exactly one line to the root `## Changelog`, naming the IDs actually closed:
```
- [YYYY-MM-DD] Phase Backlog {comma-separated closed IDs} → ● Complete — every {domain} phase key is now ●/— (state-map-phase)
```

If nothing was closed, skip the changelog line silently — but still report any skipped IDs in Step S9.

### Caveat — domain completion is the signal, and it is not a proof

The cross-domain guard above catches the failure mode that actually occurred: root Phase Backlog P-380, P-382, and P-391 each sat `◐` for months because their final criterion was owned by another domain, and each names that domain in its own criteria text — so the guard skips them, exactly as it should.

One case the guard does **not** catch: an entry dispatched into this domain but never planned into any phase key. If `arch-lead` adds a backlog entry and the domain's arch-planner has not yet authored tasks for it, every existing phase key can be `●` while that entry's real work has not started, and nothing in its criteria names another domain. Closing it would be wrong. When Step S9 reports what was closed, sanity-check any ID you do not recognise as work this domain actually did — if it has no corresponding tasks anywhere in the sub state-map, restore it to `◐` and record why.

Neither guard nor caveat makes this step a substitute for reading acceptance criteria when the stakes are high. It exists so routine, single-domain work orders stop needing a hand-close, not so nobody ever checks.

## Step S9 — Report

Output ≤ 5 bullet points: task updated, phase key state after update, whether root propagation fired and what changed if it did. If stopped due to parse error, list missing fields and a corrected example.

If promotion fired and any `## Phase Backlog` entry for this domain is still `○` Pending or `◐` Dispatched after Steps S8a–S8c, name those IDs in one additional bullet and state that they were left open. Do not close them — surface them. This is the signal that an entry is waiting on something outside the domain's own phase keys, which is otherwise invisible until someone audits the backlog by hand.

---

## Format contract (never violate these)

- Root mode operates on exactly one file: `state-map.md` at the repo root.
- Sub-map mode operates on one sub state-map file, plus the root `state-map.md` only if promotion fires.
- Never add new sections, rename sections, or reorder sections in either file.
- Never edit the `## Legend`, `## Phase Key Registry`, or phase list during command execution — they are maintained by arch-planner agents, not by this command at runtime.
- Sub state-map Phase Key Registries may include an optional fourth column `Root Backlog ID`. When present and non-empty (not `—`), Step S8a uses it to resolve which `### P-NNN` Phase Backlog entries in the root state-map to close when that phase key completes. Add this column when creating new WO-specific **extension** phase keys so they self-close correctly — never on a standard lifecycle row, where it would close entries before later phases finish (see Step S8a, Case 3).
- Exactly one of Steps S8a / S8b / S8c closes Phase Backlog entries on any given run: S8a for an extension phase key that maps to its own entries, S8b on a `Published` transition, S8c when every phase key in the domain is `●`/`—` and neither of the others applied. A domain tracking a work order through its lifecycle keys reaches closure via S8c, which is the only path for a domain that was already `Published` before that work order arrived.
- The root `## Domain Summary Board` is always exactly 18 rows — no insertions, no deletions.
- State symbols in tables must always be wrapped in backticks: `` `○` ``, `` `◐` ``, `` `●` ``, `` `⚑` ``.
- Active Work and Blocked tables use the exact column headers shown above — never alter them.
- Both Changelogs are append-only — never edit or remove existing entries.
- The commented `<!-- Format when active: ... -->` blocks are documentation — leave them untouched.
- Root Overall Progress counts must always sum to 18 after every call.
- Sub-map Overall Progress `● Done` counts must always equal the number of `●` task rows in that phase section.
