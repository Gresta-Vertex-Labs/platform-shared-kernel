---
name: feedback_reverify_blocker_even_same_day
description: Independently re-verify an upstream blocker on disk even when a prior state-map note is dated "today" — don't shortcut verification just because the timestamp looks fresh
type: feedback
---

When re-dispatched on a phase whose state-map already documents a blocker "verified on disk" earlier the same day, still re-run the actual checks (Glob for the expected file, Read the csproj for the package reference, Grep the upstream state-map for the task IDs) rather than trusting the existing note verbatim — even though the date matches "today."

**Why:** A same-day timestamp doesn't prove the note is current within *this* session — it could be stale from hours earlier, or the prior session could have been wrong/sloppy about what it actually checked. The phase brief for `08.Storage`'s Tests phase explicitly required re-verification "not this brief's prose or the state-map's note" before touching the blocked tasks, and the instructions elsewhere in this domain (`08.Storage/CLAUDE.md`'s own Test Rules) repeat the same standing rule: "verified on disk, not assumed." Trusting a same-day note without re-checking would violate that rule even if it happened to still be correct.

**How to apply:** When a phase brief hands you a state-map that already contains a "Blocked" section with an evidence trail, always re-run the specific verification steps (file existence via Glob, package references via Read, task states via Grep) yourself before acting on the blocked/unblocked determination — regardless of how recent the existing note claims to be. If your independent check confirms the same result, say so explicitly and cite your own commands/evidence (not the old note) in the refreshed changelog entry. If nothing changed and no task's state actually flips, do not call `state-map-phase` (nothing to promote) and do not call `sync-brain` (nothing substantive changed) — a pure re-verification pass with an unchanged outcome only needs a state-map changelog entry recording what was checked and confirming the result, not a brain sync.
