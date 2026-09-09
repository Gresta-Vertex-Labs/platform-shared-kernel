---
name: feedback-do-not-edit-forbidden-domains-even-temporarily
description: When a task explicitly scopes you out of a domain (e.g. "do not edit 01.Core"), that bars even temporary revert-and-restore verification edits — find a read-only alternative
metadata:
  type: feedback
---

When a brief says "do not edit domain X, even if you conclude the fix belongs there," treat that
as barring EVERY edit to X's files, including a temporary one you fully intend to revert before
finishing (the pattern this codebase's own `SecureDefaultsAssertion` test family normally uses:
"point the assertion at a deliberately-wrong expectation, confirm it fails, revert"). That
temporary-edit technique is fine when the file you're editing is one you own (a governance test
file); it is NOT fine when the file belongs to a domain you were told to stay out of, even for a
few seconds mid-session.

**Why it matters:** the instruction exists so responsibility stays cleanly separated — if you
"just briefly" touch another domain's file for verification, you've created exactly the ambiguity
("did an agent touch this file?") the boundary was meant to prevent, and a slip (forgetting to
revert, a crash mid-edit, a parallel agent reading the dirty file) leaves real damage.

**What to do instead, for an ABSENCE check with no expected-value parameter to perturb** (so the
"wrong expectation" trick doesn't apply):
1. Prove the CHECK ITSELF can fail using a contrived fixture compiled through the exact same
   predicate/assertion class — this proves the mechanism works, not that the specific real file is
   currently clean.
2. Separately, do a READ-ONLY inspection of the real, already-compiled build output (never the
   source) — e.g. a standalone Mono.Cecil script (run outside the repo, in scratchpad) that loads
   the real `.dll` and lists its IL instructions — to confirm the real code contains substantial,
   non-trivial content the rule is genuinely scanning, so a pass isn't vacuous ("found nothing
   because the method body was empty").
3. Document honestly which technique you used and why — don't claim a revert-based verification you
   didn't actually perform. (Caught myself writing exactly this false claim in a doc comment during
   WO-083/P-523 before fixing it — see [[ref-secure-defaults-assertion-disambiguation]].)

Applies whenever a task names files/domains as off-limits, not just this repo's `01.Core`.
