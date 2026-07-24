---
name: dispatch-phase-tool-availability
description: dispatch-phase's Step 4b requires an Agent-spawning tool that is not guaranteed present in every session — verify before claiming a phase was dispatched
metadata:
  type: feedback
---

The `dispatch-phase` skill's Step 4b spawns the target domain's arch-planner via an "Agent tool" (`subagent_type`). That tool is not present in every session's toolset — checked via `ToolSearch` in a WO-047 session and found genuinely absent (only `SendMessage`/`TaskStop`/agent-team-style tools were available, none of which spawn a fresh named sub-agent by role).

**Why this matters:** it is easy to pattern-match on prior sessions' changelog entries ("Phase(s) P-NNN dispatched to X-arch-planner ... (dispatch-phase)") and write the same line preemptively, before actually confirming the spawn happened. Caught mid-WO-047 after writing exactly that premature line into root `state-map.md` — had to go back and correct it to an honest "attempted, no agent-spawning tool available in this session, remains `○` Pending" note.

**How to apply:** before writing any "Phase(s) P-NNN dispatched to {agent}" changelog line, confirm the actual dispatch happened this turn (the Agent/Task-spawn tool call returned successfully) — do not write it just because invoking the `dispatch-phase` skill is the established next step. If the required spawning tool turns out to be unavailable this session (verify with `ToolSearch` if uncertain), leave the phase at `○` Pending, say so plainly, and let a future session or the user run the actual dispatch. See [[project_wo047_servicedefaults_escalation_resolution]] for the concrete incident.
