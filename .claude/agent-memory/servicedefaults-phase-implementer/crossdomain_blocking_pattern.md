---
name: crossdomain_blocking_pattern
description: How 13.ServiceDefaults tasks get blocked on another domain's not-yet-landed instrument, and how to verify the block is actually resolved before implementing
metadata:
  type: project
---

> WO-086 (2026-09): readiness checks no longer block on another domain — providers self-register an `IReadinessProbe` and `AddSharedKernelReadiness()` maps whatever exists, so `Add*HealthCheck` wiring tasks per provider are gone. The pattern below still applies to `With*Telemetry()` wiring.

`13.ServiceDefaults` is the composition root — several of its tasks (`WithMessagingTelemetry`,
`WithCachingTelemetry`, and likely future `With*Telemetry`/`Add*HealthCheck` methods) are contractually
specified in `CLAUDE.md` *before* the upstream domain instrument they wire actually exists in code. This is
intentional — the contract is designed in advance so the wiring side can be implemented the moment the
upstream lands, with zero redesign. `13.ServiceDefaults/state-map.md` tracks this explicitly via a
`## Blocked` table row and a `## Cross-Domain Dependencies` table row, both naming the exact upstream
artifact (e.g. `MessagingDiagnostics.ActivitySource("SharedKernel.Messaging", "1.0.0")`) and the exact
upstream phase key (e.g. `07.Messaging`'s `SK.07.OTel`, tasks `OT-01`–`OT-08`).

**How to verify a block is actually resolved (do this before writing any code):**
1. Grep the upstream domain's `state-map.md` for the named phase key's task table — confirm every task is
   `●`, not just the summary row.
2. Grep the upstream domain's actual source tree for the literal construct named in the blocker (e.g.
   `ActivitySource("SharedKernel.Messaging"`) — confirm it exists on disk, not just in a state-map claim.
   State-map claims are a planning artifact; the actual file is ground truth.
3. Only then proceed — and even then, confirm via the upstream's `CLAUDE.md` whether the artifact is
   `public` or `internal`. If `internal` (the common case for diagnostics-only types like
   `MessagingDiagnostics`), the wiring side must use string-name-only registration — see
   [[otel_wiring_pattern]] — never attempt a type reference or request an `InternalsVisibleTo` grant.

**Resolving the blocker in both state-maps once implemented:**
- In `13.ServiceDefaults/state-map.md`: clear the `## Blocked` table row entirely (don't just mark
  resolved — remove it, matching the "no blockers" placeholder convention used elsewhere in the repo).
  Update the `## Cross-Domain Dependencies` row's Status column from "Blocked" to "Available" rather than
  deleting it (it documents the dependency existed and is now satisfied).
- The task's own row in the relevant `## Phase: {X}` table flips to `●`. Note: this domain's blocked tasks
  are *not* always marked `⚑` in their own task-table row while blocked — as of the WO-054/P-351 session
  (2026-08-07), the task rows themselves stayed `○` and the blocking fact lived only in the separate
  `## Blocked` section table plus inline prose in the task's own description ("**BLOCKED:** ..."). Don't
  assume a task showing `○` in its phase table is simply "not started yet" — always cross-check the
  `## Blocked` table too before treating a `○` task as unblocked/available.
- When this is the *last* `⚑` in that phase, the phase's Overall Progress row flips from `⚑` to `●` and
  the phase promotes to root via `/state-map-phase` — this also closes the root `state-map.md`'s
  `## Phase Backlog` entries for that root-phase's constituent backlog IDs (e.g. P-010, P-122, P-132,
  P-175, P-176, P-177 for `SK.13.Core`), which is easy to forget since the `state-map-phase` skill's
  automatic Step S8a only fires for phase keys that carry a `Root Backlog ID` column in their Phase Key
  Registry — `SK.13.Core` does not have one (it's a standard lifecycle phase, "Case 3" in that skill's
  logic), so closing those Phase Backlog entries must be done manually/explicitly when the phase
  completes, not assumed to happen automatically.
