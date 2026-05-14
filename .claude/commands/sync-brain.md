You are the keeper of the architectural brain for Platform.SharedKernel.

This command operates in two modes depending on the input:

- **Root mode** — updates `CLAUDE.md` at the repo root (cross-domain rules, folder map, layering)
- **Sub-domain mode** — updates a specific domain's `{NN}.{Name}/CLAUDE.md` (packages, interfaces, rules for that capability only)

Read the input below and determine which mode applies before doing anything else.

---

**Input summary:**
$ARGUMENTS

---

## Mode Detection

If the input contains `domain:` (e.g. `domain: 02.Caching`), use **Sub-domain mode**.
Otherwise use **Root mode**.

---

# ROOT MODE

Your job: analyze the summary, then make only the necessary targeted edits to `CLAUDE.md` at the repo root. Do not rewrite sections that are not affected. If nothing warrants a change, make no edits and explain why.

## Step R1 — Read current state

Read `CLAUDE.md` at the repo root in full before doing anything else. This is the only file root mode ever reads or writes. Never touch any subfolder `CLAUDE.md`.

## Step R2 — Extract signals from the input

Scan the input summary for any of the following signals. For each signal found, note which section it affects:

| Signal | Target section |
|--------|---------------|
| New folder added or renamed | `## Folder Map` row + `## Layering Rules` entry |
| Folder removed or merged | `## Folder Map` row removed + `## Layering Rules` entry removed |
| New package added inside an existing folder | `## Folder Map` (update description) + `## "What Goes Where"` (add row if the package introduces a new placement rule) |
| New abstraction/provider pair | `## Abstractions Packages` table |
| Dependency rule changed (what can reference what) | `## Layering Rules` block |
| New hard rule (something that must never happen) | `## Layering Rules` — Hard rules list |
| New naming pattern or exception | `## Package Naming Convention` |
| Test project rule changed | `## Test Project Rules` |
| Solution format change | `## Solution Format` |
| Sub-domain `CLAUDE.md` initialized or updated with a new package, interface, or cross-cutting pattern | Check if a new `## "What Goes Where"` row, `## Abstractions Packages` row, or `## Folder Map` description update is warranted at the root level |
| Anything else that does not match the above | Evaluate: only add if it would prevent a future agent from making a wrong decision |

## Step R3 — Apply edits

For each signal from Step R2:
- Make a **surgical edit** to the affected section only — add a row, update a description, add a bullet
- Never touch sections that are not affected by the input
- Never change headings, table column names, or the document structure
- Keep the tone and density consistent with the existing content (concise, no prose explanations)
- If a "What Goes Where" row already exists for this package/area, update it instead of duplicating it

## Step R4 — Append changelog entry

Always append exactly one line to `## Changelog`:
```
- [YYYY-MM-DD] <one-line description of what changed> (<caller or agent name if mentioned in input, otherwise "agent">)
```
Use today's date. Keep under 120 characters.

## Step R5 — Report

Output ≤ 5 bullet points of what changed and why. If no edits were made, explain which signals were missing and what kind of input would trigger a change.

---

## Root mode format contract (never violate these)

- Operates on exactly one file: `CLAUDE.md` at the repo root. No other file is read, written, or modified.
- Sub-domain `CLAUDE.md` files are owned by their domain agents — **never read, write, or reference them directly**. Only their summaries, passed as input to this command, are valid signal sources.
- Section order must stay: What This Repo Is → Folder Map → Layering Rules → Package Naming Convention → Test Project Rules → "What Goes Where" → Abstractions Packages → Solution Format → Changelog
- All tables use markdown pipe syntax
- The Layering Rules code block stays as a plain fenced code block — no YAML, no JSON
- Hard rules stay as a bullet list under the code block
- The Changelog is append-only — never edit or remove existing entries
- Do not add new top-level sections unless the input explicitly introduces a new cross-cutting concern that has no home in any existing section

---

# SUB-DOMAIN MODE

Your job: analyze the summary, then make only the necessary targeted edits to the specified domain's `CLAUDE.md`. Do not rewrite sections that are not affected. If nothing warrants a change, make no edits and explain why.

## Step S1 — Parse the input

Extract the following fields. All are required; stop and list missing fields if any are absent.

| Field | Expected values |
|-------|----------------|
| `domain` | Folder name or number — e.g. `02.Caching`, `02`, `Caching`. Resolved to the canonical `{NN}.{Name}` form using the root CLAUDE.md folder map. |

Resolve the target file path: `{NN}.{Name}/CLAUDE.md`.

## Step S2 — Read current state

Read the resolved `{NN}.{Name}/CLAUDE.md` file in full before doing anything else. This is the only file sub-domain mode ever reads or writes. Never touch the root `CLAUDE.md` or any other domain's file.

## Step S3 — Extract signals from the input

Scan the input summary for any of the following signals. For each signal found, note which section it affects:

| Signal | Target section |
|--------|---------------|
| New package in this domain | `## Packages` table — add or update row |
| Package removed or renamed | `## Packages` table — update or remove row |
| Interface added, changed, or removed | `## Interface Contracts` block |
| New or changed implementation rule | `## Implementation Rules` bullet list |
| Technology added, swapped, or removed | `## Technology Stack` table row |
| DI registration signature added or changed | `## DI Registration` block |
| New AOT constraint discovered or resolved | `## AOT Compatibility` bullet |
| Test rule added or changed | `## Test Rules` bullet |
| Anything else that does not match the above | Evaluate: only add if it would prevent a future agent from making a wrong decision |

## Step S4 — Apply edits

For each signal from Step S3:
- Make a **surgical edit** to the affected section only — add a row, update a line, add a bullet
- Never touch sections that are not affected by the input
- Never change headings, table column names, or the document structure
- Keep the tone and density consistent with the existing content (concise, no prose explanations)
- Never remove content unless the input explicitly says something was removed or replaced

## Step S5 — Append changelog entry

Always append exactly one line to the domain's `## Changelog`:
```
- [YYYY-MM-DD] <one-line description of what changed> (<caller or agent name if mentioned in input, otherwise "agent">)
```
Use today's date. Keep under 120 characters.

## Step S6 — Report

Output ≤ 5 bullet points of what changed and why. If no edits were made, explain which signals were missing and what kind of input would trigger a change.

---

## Sub-domain mode format contract (never violate these)

- Operates on exactly one file: `{NN}.{Name}/CLAUDE.md`. No other file is read, written, or modified.
- Never touch the root `CLAUDE.md` or any other domain's `CLAUDE.md`.
- Section order must stay: What This Domain Is → Packages → Technology Stack → Interface Contracts → Implementation Rules → DI Registration → AOT Compatibility → Test Rules → Changelog
- All tables use markdown pipe syntax
- Interface Contracts and DI Registration blocks stay as plain fenced code blocks — no YAML, no JSON
- Implementation Rules and Test Rules stay as bullet lists — no tables
- The Changelog is append-only — never edit or remove existing entries
- Do not add new top-level sections unless the input explicitly introduces a concern that has no home in any existing section
