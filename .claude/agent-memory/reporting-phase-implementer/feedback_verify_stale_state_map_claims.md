---
name: feedback_verify_stale_state_map_claims
description: A dispatching agent's "not yet shipped elsewhere" claim can go stale between when it was written and when this session runs — always re-verify against the live source file before treating it as a hard blocker
type: feedback
---

The WO-077 dispatch for `20.Reporting` (2026-09-04 session) instructed: verify whether
`01.Core`'s `LoggingEventIdRanges.Reporting = 20000` is shipped in code, and if genuinely absent,
skip authoring any `[LoggerMessage]` methods rather than inventing the range. The dispatch text
itself, written 2026-08-26, asserted it was "design-locked... not yet shipped."

**Reading the actual file (`01.Core/SharedKernel.Primitives/Logging/LoggingEventIdRanges.cs`)
directly showed `Reporting = 20000` was already present in code** — `01.Core` had shipped it
sometime between the 2026-08-26 dispatch note and this session, and nothing had refreshed that
note. Trusting the dispatch text without the direct read would have caused a needless, incorrect
scope reduction (skipping C-02/C-09/C-13/C-17's `[LoggerMessage]` work for no real reason).

**Why:** work-order dispatch notes and state-map snapshots are written at a point in time and are
not automatically kept current as *other* domains ship in parallel sessions. A cross-domain
dependency claim ("X hasn't shipped Y yet") is exactly the kind of thing that goes stale fastest,
because the session writing it has no visibility into what happens in other domains afterward.

**How to apply:** whenever a phase spec or state-map claims a cross-domain dependency is
missing/blocked, always re-verify by reading the actual source file directly before treating it as
binding — never propagate a blocker claim on trust alone, even when the instructions explicitly
told you to check. When the direct read contradicts the claim, say so plainly and proceed on the
verified reality, not the stale note (and correct the note).
