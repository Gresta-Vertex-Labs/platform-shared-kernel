---
name: feedback-verify-blast-radius-by-call-site
description: When implementing a cross-domain migration phase, grep for actual call sites (method invocations), not name matches — XML-doc cross-references look identical to grep and hide the real call
type: feedback
---

Never trust an upstream phase's stated "blast radius" (zero, or an enumerated list) for a breaking
interface/signature change without independently re-verifying against real, current source in this
domain. Grep for the pattern, then inspect every hit to see whether it is an actual call site (a method
invocation) or merely a name match (an XML `<see cref="..."/>`/`<c>...</c>` doc cross-reference, a DI
registration of a type, a string literal). Both look identical to a naive `grep "MethodName"`.

**Why:** WO-083's P-528 (`ITotpReplayGuard`/`TotpVerifier.VerifyAsync` migration, 2026-09-09) is the
concrete case. The interface's breaking change (P-514, `01.Core`) was asserted "zero blast radius,"
then corrected to "two consumers," then corrected a THIRD time when the actual implementer found a real
production call site (`SharedKernel.Security.Totp/Challenge/TotpChallengeService.cs:70`) that TWO
independent "verified against real source" passes had missed — both greps for `TotpVerifier.VerifyAsync`
hit only the XML-doc cross-references at lines 13 and 55 of that same file and stopped there, never
reaching the real call at line 70. The root state-map.md's own changelog records this as happening
*twice* in the same session across different phases (P-514's own Core-phase pass and the arch-lead design
pass both missed it) before a phase implementer (P-528) caught it by actually reading what each grep hit
resolved to, not just counting matches.

**How to apply:** On any phase whose job is "migrate onto a changed upstream contract" or "fix consumers
of a breaking change": (1) grep broadly for the old member/type name across the whole domain, not just the
files a prior planner's summary named; (2) for every hit, open it and classify it — real call, doc
reference, DI registration/type reference, or dead text — before deciding it's out of scope; (3) after
fixing, build the actual package (not a scratch/out-of-tree harness) and run its real test suite, then
verify anything downstream in the same domain that could compile against the changed signature; (4) record
the verification as an explicit pass/fail finding in the domain's own state-map changelog, naming exactly
what was checked — never assert "no other call sites exist" without showing the grep that proved it. This
generalizes past TOTP/security: any phase inheriting a "here's the blast radius" claim from a planner or a
sibling domain's dispatch text should treat that claim as a hypothesis to verify, not a fact to inherit —
see the companion `feedback_phase_text_mechanism_is_hypothesis`-style discipline noted in root
state-map.md's WO-083 entries.
