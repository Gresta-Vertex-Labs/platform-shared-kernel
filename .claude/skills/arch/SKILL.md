---
name: arch
description: Hand an architecture request to the arch-lead agent, which evaluates it against the SharedKernel architecture and writes a work order on the root state-map; it plans and never writes code. Use when the user proposes a new capability, package, pattern or architectural change.
argument-hint: <capability request>
---

Invoke the `arch-lead` agent with the input below. Do not analyze, plan, or add commentary — pass the input as-is and let the agent handle everything autonomously.

Standing instruction for the agent: read `.claude/agents/_common.md` and the root `CLAUDE.md` first; new work goes into the root `state-map.md` `## Open Work` as a work order with P-entries (ids from `## ID Counters`), ready for `/dispatch-phase`.

$ARGUMENTS
