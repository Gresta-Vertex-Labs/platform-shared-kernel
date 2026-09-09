---
name: feedback-verify-positional-arg-compat-claims
description: a design's "adding optional params keeps this call site source-compatible" claim must be checked against how that call site actually passes its trailing argument (named vs. positional) before trusting it
type: feedback
---

When a phase inserts new optional parameters before an existing trailing parameter (commonly
`CancellationToken ct = default`) and claims a named downstream call site "stays source-compatible
because the new params are optional," that claim is only true if the call site passes the trailing
parameter BY NAME. If it passes it POSITIONALLY, the new parameter silently steals that position and
the call fails to compile (a real CS1503-class break, not a behavior change).

**Why this matters:** on SK.01.P514 (`01.Core`, WO-083, 2026-09-09), the locked design (D-88) asserted
`12.Security.Totp`'s `TotpChallengeService.VerifyAsync` stayed "source-compatible and unaffected" by
`TotpVerifier.VerifyAsync` gaining four new optional parameters before its trailing `ct`. Grepping the
real call site (`_totpVerifier.VerifyAsync(identityKey, secret, code, ct)`) showed `ct` passed
positionally as the 4th argument — which now binds to the new `int digits` parameter instead, a genuine
compile break. This was a THIRD affected consumer beyond the two already-flagged test fakes, and the
design's own compatibility claim was verified false only by reading the actual downstream source, not
by trusting the design doc's prose (even though that same design doc had already corrected two *other*
false premises in its own dispatched brief).

**How to apply:** whenever a phase's compatibility argument rests on "the new params are optional and
inserted before an existing trailing param, so old callers keep compiling," grep every real call site of
that method across the repo (not just the ones the design already named) and check whether the trailing
argument is passed by name or position. If any real call site is positional, the claim is false for that
site — report it explicitly (as a new finding, distinct from whatever the design already flagged) rather
than silently accepting the design's compatibility framing. Do not edit the affected out-of-jurisdiction
file yourself; report it for that domain's own migration phase, and make sure any README/sample code you
own demonstrates the safe named-argument form going forward.
