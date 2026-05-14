You are the root state-map phase recorder for Platform.SharedKernel.

Your job: parse the input below, then make the minimum necessary edits to `state-map.md` at the repo root to reflect the new or updated phase state. You operate on exactly one file. You do not touch sub state-maps or any other file.

---

**Input:**
$ARGUMENTS

---

## Step 1 — Parse the input

Extract the following fields from the input. All are required; if any are missing or ambiguous, output an error listing what is missing and stop — do not edit anything.

| Field | Expected values |
|-------|----------------|
| `domain` | A number 00–17 (e.g. `01`, `06`) or the folder name (e.g. `Core`, `Persistence`) |
| `phase` | One of: `Design`, `Scaffold`, `Core`, `Tests`, `Docs`, `Published` |
| `state` | One of: `○` (not started), `◐` (in progress), `●` (complete), `⚑` (blocked) |
| `summary_done` | One sentence describing what has been completed. Use `—` if nothing is done yet. |
| `summary_next` | One sentence describing what the next action is. Use `—` if nothing follows. |
| `blocker` | _(Only required when state = `⚑`)_ One sentence describing what is blocking and which domain or external dependency it waits on. |

Resolve a domain name to its canonical folder prefix (e.g. `Persistence` → `06`, `Core` → `01`) using the folder map in CLAUDE.md if needed.

---

## Step 2 — Read the current state-map

Read `state-map.md` at the repo root in full. Never read any sub state-map or any other file.

---

## Step 3 — Update the Domain Summary Board

Find the row for the parsed domain in the `## Domain Summary Board` table. Update these columns in-place:

- **Current Phase** → set to the parsed `phase`
- **State** → set to the parsed `state` symbol (wrapped in backticks)
- **Summary: Done** → set to the parsed `summary_done`
- **Summary: Next** → set to the parsed `summary_next`

Do not add or remove rows. Do not reorder rows. Do not change any other row.

---

## Step 4 — Update Active Work

Apply the following logic — only one outcome per call:

| Condition | Action |
|-----------|--------|
| `state = ◐` | Replace the placeholder text (or add a row) in `## Active Work` for this domain. Format: `\| [NN.Name](NN.Name/state-map.md) \| Phase \| summary_next \|` |
| `state = ●` or `state = ○` | Remove this domain's row from `## Active Work` if it exists. If no rows remain, restore the placeholder `_Nothing in progress — all domains at ○ Not Started._` |
| `state = ⚑` | Remove this domain's row from `## Active Work` if it exists |

When the Active Work table is populated for the first time, replace the placeholder text with the table header and the new row:

```
| Domain | Current Phase | Focus (one line) |
|--------|---------------|-----------------|
| [NN.Name](NN.Name/state-map.md) | Phase | summary_next |
```

---

## Step 5 — Update Blocked

Apply the following logic:

| Condition | Action |
|-----------|--------|
| `state = ⚑` | Replace the placeholder text (or add a row) in `## Blocked` for this domain. Format: `\| [NN.Name](NN.Name/state-map.md) \| Phase \| blocker \|` |
| `state ≠ ⚑` | Remove this domain's row from `## Blocked` if it exists. If no rows remain, restore the placeholder `_No blockers._` |

When the Blocked table is populated for the first time, replace the placeholder text with the table header and the new row:

```
| Domain | Blocked Phase | Blocker |
|--------|--------------|---------|
| [NN.Name](NN.Name/state-map.md) | Phase | blocker |
```

---

## Step 6 — Recalculate Overall Progress

Re-count the Domain Summary Board rows and update every count in `## Overall Progress`. The counts must always sum to 18.

Rules:
- **○ Not Started** = rows where State is `○`
- **◐ In Progress** = rows where State is `◐`
- **⚑ Blocked** = rows where State is `⚑`
- **● Design / Scaffold / Core / Tests / Docs / Published** = rows where State is `●`, grouped by their Current Phase value

---

## Step 7 — Append changelog entry

Append exactly one line to `## Changelog`:

```
- [YYYY-MM-DD] {domain} → {phase} ({state symbol}) — {summary_done if ● else summary_next} (state-map-phase)
```

Use today's date. Keep the line under 120 characters.

---

## Step 8 — Report

Output a short summary (≤ 5 bullet points) of exactly what changed and in which section. If you stopped early due to a parse error, list the missing fields and a corrected example call.

---

## Format contract (never violate these)

- This command operates on exactly one file: `state-map.md` at the repo root. No sub state-map, no CLAUDE.md, no other file is read, written, or modified.
- Never add new sections, rename sections, or reorder sections.
- Never edit the `## Legend` or the phase list — they are reference-only.
- The `## Domain Summary Board` is always exactly 18 rows — no insertions, no deletions.
- State symbols in the board must always be wrapped in backticks: `` `○` ``, `` `◐` ``, `` `●` ``, `` `⚑` ``.
- Active Work and Blocked tables use the exact column headers shown above — never alter them.
- The Changelog is append-only — never edit or remove existing entries.
- The commented `<!-- Format when active: ... -->` blocks are documentation — leave them untouched.
- Overall Progress counts must always sum to 18 after every call.
