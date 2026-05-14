You are the keeper of the root architectural brain for Platform.SharedKernel.

Your job: analyze the summary below, then make only the necessary targeted edits to `CLAUDE.md` at the repo root. Do not rewrite sections that are not affected. Do not change the format or section order. If nothing warrants a change, make no edits and explain why.

---

**Input summary:**
$ARGUMENTS

---

## Instructions

**Step 1 — Read current state**
Read the file at `CLAUDE.md` at the repo root in full before doing anything else. This is the only CLAUDE.md file this command ever reads or writes. Never touch any subfolder CLAUDE.md — those are owned by their respective capability agents.

**Step 2 — Extract signals from the input**
Scan the input summary for any of the following signals. For each signal found, note which CLAUDE.md section it affects:

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
| Anything else that does not match the above | Evaluate: only add if it would prevent a future agent from making a wrong decision |

**Step 3 — Apply edits**
For each signal from Step 2:
- Make a **surgical edit** to the affected section only — add a row, update a description, add a bullet
- Never touch sections that are not affected by the input
- Never change headings, table column names, or the document structure
- Keep the tone and density consistent with the existing content (concise, no prose explanations)
- If a "What Goes Where" row already exists for this package/area, update it instead of duplicating it

**Step 4 — Append changelog entry**
Always append exactly one line to the `## Changelog` section at the bottom of the file:

```
- [YYYY-MM-DD] <one-line description of what changed> (<caller or agent name if mentioned in input, otherwise "agent">)
```

Use today's date. Keep it under 120 characters.

**Step 5 — Report**
After all edits are done, output a short summary (≤5 bullet points) of what you changed and why. If you made no edits, explain which signals were missing and what kind of input would trigger a change.

---

## Format contract (never violate these)

- This command operates on exactly one file: `CLAUDE.md` at the repo root. No other file is read, written, or modified.
- Section order in CLAUDE.md must stay: What This Repo Is → Folder Map → Layering Rules → Package Naming Convention → Test Project Rules → "What Goes Where" → Abstractions Packages → Solution Format → Changelog
- All tables use markdown pipe syntax
- The Layering Rules code block stays as a plain fenced code block — no YAML, no JSON
- Hard rules stay as a bullet list under the code block
- The Changelog is append-only — never edit or remove existing entries
- Do not add new top-level sections unless the input explicitly introduces a new cross-cutting concern that has no home in any existing section
