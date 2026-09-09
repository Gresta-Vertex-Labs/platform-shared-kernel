---
name: feedback-verify-positional-arg-compat-claims
description: an "adding an optional param is source-compatible" claim has TWO distinct failure modes — a trailing param passed positionally at a call site, and a method-group conversion whose arity changed — both must be checked before trusting it
type: feedback
---

When a phase inserts new optional parameters before an existing trailing parameter (commonly
`CancellationToken ct = default`) and claims a named downstream call site "stays source-compatible
because the new params are optional," that claim is only true if the call site passes the trailing
parameter BY NAME. If it passes it POSITIONALLY, the new parameter silently steals that position and
the call fails to compile (a real CS1503-class break, not a behavior change).

**Second, distinct failure mode — no call site involved at all:** adding a trailing optional parameter
also breaks any existing METHOD-GROUP CONVERSION of that method to a delegate type, because an optional
parameter still changes the method's arity for conversion purposes. `SharedKernel.Validation.FluentValidation`'s
`Attach(ruleBuilder, IbanValidator.Validate)` (`ValidationRuleBuilderExtensions.cs`) converted the bare
method group `IbanValidator.Validate` to `Func<string?, Result>` — this compiled fine when `Validate` was
`Validate(string? value)`. When P-521 (`01.Core`, WO-083, 2026-09-09) added
`allowFallbackForUnknownCountry = false`, the method group no longer matched the delegate's one-parameter
shape and the ENTIRE PROJECT failed to build with `CS1503: cannot convert from 'method group' to
'Func<...>'` — found only because the next phase's full-solution build broke, not because any individual
call site of `IbanValidator.Validate(x)` itself stopped compiling (those were all fine; `Validate(x)` still
resolves via the new optional parameter's default). This is a build-wide break, not a call-site break, and
grepping call sites of the underlying method will NOT find it — you have to grep for the method used as a
bare delegate/method-group value (`SomeMethod,` with no parens, typically inside `Attach(...)`,
`.Select(...)`, event-handler `+=`, or any `Func<>`/`Action<>`-typed parameter).

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
inserted before an existing trailing param, so old callers keep compiling," check BOTH failure modes:
(1) grep every real call site of that method across the repo (not just the ones the design already named)
and check whether the trailing argument is passed by name or position; (2) grep for the method used as a
bare method-group/delegate value, not as a call — these compile-break silently at the whole-project level,
invisible to a call-site-only grep. If either check finds a hit, the "source-compatible" claim is false for
that site — report it explicitly (as a new finding, distinct from whatever the design already flagged)
rather than silently accepting the design's compatibility framing. If it is inside your own domain (as the
method-group case above was, in the very next phase after the one that made the claim), just fix it: a
same-package sibling call is your jurisdiction, not a cross-domain report. If it is out-of-jurisdiction, do
not edit that file yourself — report it for that domain's own migration phase, and make sure any
README/sample code you own demonstrates the safe named-argument form going forward. Either way, a full
solution build (not just the one project the phase touched) is the only thing that actually proves neither
failure mode slipped through.
