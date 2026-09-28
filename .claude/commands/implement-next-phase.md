---
description: Implement the lowest dispatched phase on the root board through /implement-phase
---

You are the next-phase implementation launcher for Platform.SharedKernel. You find the lowest-numbered dispatched P-entry on the root board and hand it to `/implement-phase`. You never edit a file yourself.

**Input:**
$ARGUMENTS

> Ignored: this command always targets the lowest dispatched Phase ID.

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

Sort by Phase ID ascending. Take the first entry whose `**Depends on:**` P-entries are all `●` (or listed under `## Completed Work Orders`). If none qualifies, list each dispatched entry with the dependency it waits on and stop.

Output one line:
```
implement-next-phase: P-NNN ("{capability}") in {NN}.{Name}, phase {phase key}.
```

If `**Phase key:**` is `—`, the planner's key was never recorded: pass no phase, so `/implement-phase` picks the domain's next open phase.

---

## Step 3 — Map the domain to its slug

| Folder | Slug |
| --- | --- |
| `00.Governance` | `governance` |
| `01.Core` | `core` |
| `02.Caching` | `caching` |
| `03.Domain` | `domain` |
| `04.Contracts` | `contracts` |
| `05.Application` | `application` |
| `06.Persistence` | `persistence` |
| `07.Messaging` | `messaging` |
| `08.Storage` | `storage` |
| `09.Search` | `search` |
| `10.Intelligence` | `intelligence` |
| `11.Communication` | `communication` |
| `12.Security` | `security` |
| `13.ServiceDefaults` | `servicedefaults` |
| `14.Presentation` | `presentation` |
| `15.Integration` | `integration` |
| `16.Testing` | `testing` |
| `17.Workflows` | `workflow` |
| `18.Idempotency` | `idempotency` |
| `19.Scheduling` | `scheduling` |
| `20.Reporting` | `reporting` |

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
