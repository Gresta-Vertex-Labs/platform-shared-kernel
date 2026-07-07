---
name: project_phase_state
description: Current task-ID/phase numbering state of 16.Testing/state-map.md, updated each session
type: project
---

As of 2026-07-07 (after processing WO-040 / P-244+P-245):

- Last task IDs used per phase: D-71, S-18, C-53, T-44, DO-15, P-02 (Published untouched by WO-040).
- Total task count in `16.Testing/state-map.md`: 196 (173 before WO-040's +23).
- Six prior work orders are fully closed (all phases `●`): WO-008 (P-035), WO-012 (P-064), WO-029 (P-179–P-187), WO-030 (P-188/P-189), WO-036 (P-226). WO-040 (P-244/P-245) is the only in-progress work as of this date — all `○`.
- P-190 (WO-030, an `OutboxMessageFaker`/`OutboxAssertions` ask for 06.Persistence) was **rejected outright**, not deferred — its premise (a shipped 06.Persistence outbox contract) is false; outbox ownership belongs entirely to 07.Messaging via MassTransit. Do not resurrect this ask without independently re-verifying 06.Persistence's own CLAUDE.md hasn't changed that hard rule.
- The `Package Board` table's `SharedKernel.Testing` row still literally says "Current Phase: Scaffold" — this is stale leftover text from the very first pass and was never corrected across 5 subsequent work orders. I appended a parenthetical correction note in WO-040 rather than rewriting the whole row (matches this file's own convention of additive corrections over silent rewrites). Don't be misled by that row — check `Overall Progress` instead for real phase state.
- Folder/Namespace Map as of WO-040: `Clocks/`, `Caching/`, `Domain/`, `Contracts/`, `Security/`, `Messaging/`, `Persistence/`, `Containers/`, `Communication/`, `ServiceDefaults/`, `Fakers/`, `Application/` (newest, added WO-040). `Application/` is the first folder built on `05.Application` and the first-ever `16.Testing` → `05.Application` `ProjectReference`.

**How to apply:** At the start of a new phase-dispatch session, read `16.Testing/state-map.md`'s `Overall Progress` table and the last Changelog entry rather than assuming IDs from this memory — this file is a fast-orientation aid, not authoritative once new work has landed. Always re-derive the actual last ID per phase section by reading the file before appending new rows (the planner's own instructions require this too).
