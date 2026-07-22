You are the next-phase implementation launcher for Platform.SharedKernel.

This command scans the Phase Backlog in the root `state-map.md` for the first `◐ Dispatched` phase (lowest Phase ID), maps its domain to the registered `implement-phase-{domain}` skill, and invokes it.

---

**Input:**
$ARGUMENTS

> Ignored — this command always targets the single lowest dispatched Phase ID.

---

## Step 1 — Locate dispatched phases (read-efficient)

The root `state-map.md` is large. Do NOT read it in full. Use the Grep tool with context to extract only what is needed.

Search `state-map.md` using output_mode `content`, pattern:

```
Status.*Dispatched
```

Set `-B 3` (3 lines of context before each match) and `-A 3` (3 lines after) so each result includes both the `### P-NNN` header and the `**Domain:**` field.

If no matches are found, output:
```
implement-next-phase: No dispatched phases found in the Phase Backlog.
Run /dispatch-phase to dispatch pending phases to their arch-planner agents first.
```
Then stop.

---

## Step 2 — Select the target phase

From the grep output, parse each match block to extract:
- **Phase ID**: from the `### P-{NNN}` header line (the numeric part after `P-`)
- **Title**: text after the `—` on the header line
- **Domain**: from the `**Domain:**` field (e.g. `11.Communication`)

If multiple dispatched phases are found, sort by Phase ID number ascending. Select the **lowest Phase ID** — that is the target.

Display one line:
```
implement-next-phase: Found dispatched phase {Phase ID} ("{Title}") in domain {Domain}.
```

---

## Step 3 — Map domain to implement skill

Look up the domain in this registry:

| Domain | Skill |
|--------|-------|
| 00.Governance | `implement-phase-governance` |
| 01.Core | `implement-phase-core` |
| 02.Caching | `implement-phase-caching` |
| 03.Domain | `implement-phase-domain` |
| 04.Contracts | `implement-phase-contracts` |
| 05.Application | `implement-phase-application` |
| 06.Persistence | `implement-phase-persistence` |
| 07.Messaging | `implement-phase-messaging` |
| 08.Storage | `implement-phase-storage` |
| 09.Search | `implement-phase-search` |
| 10.Intelligence | `implement-phase-intelligence` |
| 11.Communication | `implement-phase-communication` |
| 12.Security | `implement-phase-security` |
| 13.ServiceDefaults | `implement-phase-servicedefaults` |
| 14.Presentation | `implement-phase-presentation` |
| 15.Integration | `implement-phase-integration` |
| 16.Testing | `implement-phase-testing` |
| 17.Workflows | `implement-phase-workflow` |

If the domain is **not in the registry**, output:
```
implement-next-phase: Skipped {Phase ID} — domain {Domain} has no implement-phase command registered yet.
To enable auto-dispatch for this domain, create .claude/commands/implement-phase-{lowercase-name}.md first.
```
Then stop. Do not process any other phase.

---

## Step 4 — Invoke the implement skill

Display:
```
implement-next-phase: Triggering implement-phase-{domain} for {Phase ID} ({Domain}).
```

Use the Skill tool to invoke the matched skill (e.g. `implement-phase-communication`).
Pass no arguments — the domain skill auto-detects its next actionable phase from its own state-map.

---

## Step 5 — Report

After the skill returns, output:
```
implement-next-phase: Done. {Phase ID} ({Domain}) handed to implement-phase-{domain}. See output above for results.
```

---

## Format Contract

- Never reads `state-map.md` in full — uses a single targeted Grep call only.
- Never modifies any file directly — all writes are done by the invoked domain skill and its sub-agents.
- Processes exactly **one phase per invocation** — the lowest dispatched Phase ID.
- If the domain is not in the registry, stops and reports — does not fall through to the next dispatched phase.
- If the Skill tool call fails, reports the error and leaves state unchanged.
