---
name: feedback_phase_text_mechanism_is_hypothesis
description: when prescribing a specific implementation mechanism against a third-party library's contract, phrase it as a verify-against-real-source hypothesis, not a settled fact
type: feedback
---

When a Phase Backlog entry's "What is needed" prescribes a specific mechanism — not just "make X async" but
"X receives Y and derives Z from it," or "wrapping A in B satisfies check C" — that prescription is only as
good as my own (possibly stale, possibly never-verified) understanding of a third-party library's or an
earlier phase's own mechanism's real behavior. In WO-081 (see [[project_wo081_083_core_audit]]), 8 of 14
dispatched phases had their prescribed mechanism corrected by the domain planner after reading/reflecting
against real source — `IFusionCacheSerializer` doesn't receive the cache key, MassTransit's serializer
contracts are hard-sync despite the async-sounding interface name, `Temporalio`'s context types carry no
`RunId`, and my own `IsGenuinelySynchronous` marker's non-recursive-through-a-decorator behavior tripped up a
later phase I wrote myself.

**Why:** none of these were discoverable from documentation or general knowledge of the library — they
required someone to actually read or reflect against the installed package version. I don't have that
verification step available when writing phase text at the architecture layer; the domain planner does.

**How to apply:** when writing a phase's "What is needed" against a specific third-party contract or an
earlier phase's own mechanism, either (a) verify it myself first if the source is readily available, or (b)
phrase the prescription as intent to be verified — "the design intent is X; verify against the real compiled
[library]/[earlier phase's actual implementation] before building, and correct if it doesn't hold" — rather
than asserting a specific method signature or data-flow path as settled fact. This doesn't reduce how much
design work goes into the phase text; it changes the epistemic status of the mechanism section specifically,
so a planner finding it wrong is expected and cheap to record, not a surprise. Also: when a later phase's
acceptance criteria depends on an earlier phase's mechanism (mine or another domain's), re-derive the
implication from that mechanism's actual specified behavior at write time — don't assert the intuitive-sounding
outcome (e.g. "wrapping in a caching decorator satisfies a synchronous-capability check" needed to be checked
against what that check actually inspects, not assumed from the decorator's name).
