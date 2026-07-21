---
name: upstream-contract-definition-gap
description: A new, stronger class of cross-domain blocker found in WO-045/P-285 — the owning domain's own brain internally contradicts itself about whether a member exists
metadata:
  type: project
---

WO-045/P-285 (2026-07-21, Vector-Store/Orchestration readiness checks for `10.Intelligence`) surfaced
a blocker category distinct from every prior one in [[feedback_verify_dependency_claims]]. All prior
cases (`08.Storage`/WO-043, `09.Search`/WO-044) were "design ● / implementation ○" — the owning
domain's `CLAUDE.md` fully specified a real method signature in its ratified Interface Contracts
section, but no compiled code existed yet. That pattern is safe to lock a Design task against
immediately (the D-07/D-08 precedent).

**This case was different: the owning domain's OWN brain file internally contradicted itself.**
`10.Intelligence/CLAUDE.md`'s "Domain Invariant #8" prose asserted `ICompletionProviderDescriptor
.ProbeAsync` exists as a `ProbeAsync`-shaped member. But that same file's own banner'd-RATIFIED,
member-by-member Interface Contracts listing for `ICompletionProviderDescriptor` (the section that
says "This is the locked, member-by-member surface") enumerates exactly four members — `ProviderName`,
`ContextWindowTokens`, `MaxOutputTokens`, `ValidateContextWindow` — and no `ProbeAsync` at all. One
section's prose claims a capability the domain's own canonical member listing does not grant.

**How to detect this class of gap:** when locking a Design task against another domain's ratified
contract, don't just grep for the one paragraph that mentions the member you need (e.g. "Domain
Invariant #N" or a "readiness is a probe primitive" callout) — also read the actual member-by-member
Interface Contracts listing for that exact interface and confirm the member is really there. A
domain's prose invariants and its formal interface listing can drift apart even within one ratified
pass, especially for a brand-new domain whose Design phase was authored in a single large sitting
(16 D-tasks landed same-day here).

**How to respond when found:** do not invent the missing member's signature/return-record shape on the
other domain's behalf — that is out of jurisdiction (this domain designs `13.ServiceDefaults`, not
`10.Intelligence`'s interfaces). Instead:
1. Lock only the outer, genuinely-ratified shape of the wrapping extension method (parameter list,
   tag/name constants) — whatever doesn't require knowing the missing member's exact contract.
2. Record the Design task itself as `⚑` Blocked (not `●`) — the first time in this domain's history a
   Design-phase row has carried that state instead of `●`/`○`. This is a legitimate, stronger signal
   than the routine "implementation lags design" `⚑` used for Core/Tests/Docs tasks.
3. Flag it explicitly for `arch-lead`/the owning domain's own arch-planner to resolve — out of this
   domain's jurisdiction to edit another domain's `CLAUDE.md`.
4. Distinguish this in every place the blocker is recorded (Cross-Domain Dependencies row, Blocked
   section, Implementation Rules) from the ordinary "not implemented yet" blocker, so a future reader
   doesn't assume it will clear the moment `SK.10.Core` finishes — it might not, if `10.Intelligence`
   never adds the member.

Contrast with [[provider_agnostic_adapter_pattern]] (a design decision this domain made about its own
method shape) and [[health_check_tag_calibration]] (a calibration decision) — this is neither; it's a
verification finding about the quality of another domain's own ratified documentation, surfaced by
reading it thoroughly rather than pattern-matching on the first paragraph that looked relevant.

See [[feedback_verify_dependency_claims]] for the general "verify, don't trust" discipline this extends.
