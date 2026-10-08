---
name: implement-next-phase
description: Implement the lowest-numbered dispatched P-entry on the root state-map by handing it to /implement-phase. Use when the user says "implement the next phase" without naming a domain.
disable-model-invocation: true
---

You are the next-phase implementation launcher for Platform.SharedKernel. You find the lowest-numbered dispatched P-entry on the root board and hand it to `/implement-phase`. You never edit a file yourself.

**Input:**
$ARGUMENTS

> Ignored: this skill always targets the lowest dispatched Phase ID.

---

## Step 1 — Find dispatched P-entries

Read the `## Open Work` section of the root `state-map.md` (it is short; the board keeps only open work). Each P-entry looks like:

```
#### P-NNN — {capability}
**Status:** `◐` Dispatched
**Domain:** {NN}.{Name}
**Depends on:** {None | P-NNN}
**Phase key:** SK.{NN}.{Key}
```

Collect the entries whose `**Status:**` is `◐` Dispatched. If there are none:
```
implement-next-phase: no dispatched phases on the root board.
Run /dispatch-phase to plan pending work orders first.
```
Stop.

---

## Step 2 — Select the target

Sort by Phase ID ascending. Take the first entry whose `**Depends on:**` P-entries are all `●` or no longer on the board (finished work orders are deleted). If none qualifies, list each dispatched entry with the dependency it waits on and stop.

Output one line:
```
implement-next-phase: P-NNN ("{capability}") in {NN}.{Name}, phase {phase key}.
```

If `**Phase key:**` is `—`, the planner's key was never recorded: pass no phase, so `/implement-phase` picks the domain's next open phase.

---

## Step 3 — Map the domain to its slug

Look the entry's `**Domain:**` id up in the domain table of the root `CLAUDE.md` and take its Slug column. Never keep a copy of that table here.

A domain of `eng` (build work) has no implementer: output `implement-next-phase: P-NNN is build work; run /devops with its text.` and stop.

---

## Step 4 — Invoke `/implement-phase`

Use the Skill tool to invoke `implement-phase` with arguments `{slug} {phase key}` (or `{slug}` alone when the key is `—`).

---

## Step 5 — Report

After the skill returns:
```
implement-next-phase: P-NNN ({NN}.{Name}) handed to /implement-phase {slug} {phase key}.
```

---

## Format contract

- Reads the root `state-map.md` only. Writes nothing; every write is done by the implementer through `/state-map-phase`.
- One phase per run: the lowest dispatched Phase ID whose dependencies are done.
- If the Skill call fails, report the error; the boards are unchanged.
