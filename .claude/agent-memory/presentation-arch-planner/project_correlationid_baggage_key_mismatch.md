---
name: project_correlationid_baggage_key_mismatch
description: 13.ServiceDefaults's own P-251 test design hardcodes the wrong CorrelationId baggage-key literal — discovered during P-256, not yet fixed
metadata:
  type: project
---

> WO-086 (2026-09): `CorrelationIdMiddleware` (and its `BaggageKey` constant) was deleted; the mismatch was fixed in WO-042 by `WellKnownBaggageKeys`, and the inbound correlation id is now owned by `app.UseSharedKernelRequestContext()` (`SharedKernel.ServiceDefaults.Security`). Kept for the general lesson in point 3.

`14.Presentation`'s `CorrelationIdMiddleware` sets `Activity.Current?.SetBaggage("correlation.id", value)` (lowercase, dotted — this is the actual, documented contract, confirmed in `14.Presentation/CLAUDE.md` since WO-031).

While designing WO-041 P-256, I found that `13.ServiceDefaults/state-map.md`'s T-27 test (verifying `BaggageLogRecordProcessor` surfaces CorrelationId onto `LogRecord.Attributes`, P-251) describes itself as calling `Activity.Current?.SetBaggage("CorrelationId", someValue)` — i.e. it hardcodes a **different** literal (`"CorrelationId"`, PascalCase, no dot) than the one `CorrelationIdMiddleware` actually uses. `13.ServiceDefaults`'s own test claims this is "the exact mechanism 14.Presentation's middleware itself uses per WO-031," which is incorrect as written.

**Why this happened:** Neither domain takes a `ProjectReference` on the other (by design — see [[feedback_correlation_id_ownership]]), so nothing mechanically caught the drift. `13.ServiceDefaults`'s test author hand-copied the concept ("simulate the correlation-id baggage key") without checking the exact literal against `14.Presentation`'s real source.

**How to apply:**
1. `14.Presentation`'s own P-256 work fixed its half: promoted the literal to a public `CorrelationIdMiddleware.BaggageKey` constant (value unchanged: `"correlation.id"`) precisely so a future cross-domain consumer references the constant instead of re-typing the literal.
2. I flagged the `13.ServiceDefaults` side as a correction item (`14.Presentation/state-map.md` DO-07) for `servicedefaults-arch-planner`/`servicedefaults-phase-implementer` — it is **out of this agent's jurisdiction to edit `13.ServiceDefaults` files directly**. If a future conversation is asked to reconcile this, point to this memory and to `14.Presentation/CLAUDE.md`'s "Logging EventId assignment & correlation verification" subsection.
3. General lesson: when one domain's test suite claims to simulate "the exact mechanism another domain uses," treat that claim as unverified until the literal/contract is checked against the other domain's actual shipped source — a documented intention to mirror a contract is not the same as mirroring it correctly.
