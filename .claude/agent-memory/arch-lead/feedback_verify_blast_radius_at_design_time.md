---
name: feedback_verify_blast_radius_at_design_time
description: a phase's stated blast radius (who else is affected, whether zero-migration-cost claims hold) is a claim about current repo state and must be re-verified against real source, not inherited from prose
type: feedback
---

When I write a phase's rationale claiming a breaking change has "zero blast radius" or "no downstream
consumers yet" because some other package/domain is "still queued" or "not yet shipped," that claim is a
statement about CURRENT repo state at the moment I write it — and it goes stale the instant that other
package ships, silently, with nothing forcing a re-check. In WO-083 (see
[[project_wo081_083_core_audit]]), P-514 asserted `ITotpReplayGuard`'s breaking change was zero-blast-radius
because `12.Security.Totp` (P-452) was "still queued" — but `12.Security.Totp` had already shipped by the
time P-514 was dispatched, and two already-shipped fakes (one of them a *shared*, cross-domain test double)
broke as a result. `01.Core`'s own planner caught it during design and correctly flagged it as outside its
jurisdiction to dispatch.

**Root cause, confirmed to have bitten repeatedly, not a one-off:** stale prose (root `CLAUDE.md` describing
an already-shipped package as still-pending, in two separate places) fed the false premise into the phase text
I wrote, which then propagated into the dispatch brief a domain planner received. This happened again — a
second, independent instance the same session — when `00.Governance`'s compress-then-encrypt lock went
vacuous after `02.Caching` deleted the type it composed against, surfacing only because a compile error
happened to sit next to it. [[feedback_verify_shipped_code_not_docs]] already covers "don't trust CLAUDE.md
prose over real source" for reading state; this is the write-side companion: don't let a blast-radius or
zero-migration-cost CLAIM ship in phase text without checking it against real, current source myself, and
flag it to be re-checked again at dispatch/design time even if I did check it when I wrote it, since the gap
between writing and dispatch is exactly where staleness accumulates.

**How to apply:**
- Before asserting "zero blast radius," "no consumers yet," or "safe because X is still queued" in a phase's
  rationale, grep the real repo for actual implementers/consumers of the interface/type being changed — don't
  infer package-shipped-status from CLAUDE.md prose alone, which the platform's own history shows drifts from
  reality after a concurrent multi-implementer session withholds root propagation.
- Treat this as a *recurring* check, not a one-time fact established when the phase was first written — a
  phase can sit in the backlog for a while before dispatch, and the repo keeps moving underneath it. If a
  domain planner (correctly) flags a stale blast-radius claim at dispatch time, that is the system working as
  designed, not a planning failure to be defensive about — but it should still trigger going back and checking
  whether the SAME staleness pattern (a "queued"/"not yet shipped" claim about some other package) appears
  anywhere else in currently-open phase text, since one instance found this way is a signal, not a coincidence.
- Companion to [[feedback_phase_text_mechanism_is_hypothesis]]: that memory is about the prescribed FIX
  mechanism being a hypothesis; this one is about the stated BLAST RADIUS (who is affected, whether a change
  is safe) being an equally perishable claim, checked the same way — against real source, not prose, and not
  just once.
