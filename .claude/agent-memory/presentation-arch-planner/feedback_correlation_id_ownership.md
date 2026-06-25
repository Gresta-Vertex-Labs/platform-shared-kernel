---
name: feedback_correlation_id_ownership
description: CorrelationIdMiddleware's baggage/Items keys are 14.Presentation's own contract — never a borrowed 13.ServiceDefaults convention
metadata:
  type: feedback
---

The original domain draft listed a Cross-Domain Dependency: `SK.14.Core` needs `13.ServiceDefaults` for "OTel ActivitySource/baggage conventions" so `CorrelationIdMiddleware` could "align its baggage key." WO-031 P-192 explicitly corrected this — the `correlation.id` baggage key and `HttpContext.Items["CorrelationId"]` key are `14.Presentation`'s own contract, defined here, with zero `ProjectReference` on `13.ServiceDefaults`.

**Why:** `14.Presentation` may only reference `01.Core`, `04.Contracts`, `12.Security`, `13.ServiceDefaults` per root layering rules — but "may reference" doesn't mean "should always take a dependency for every adjacent concern." Correlation-id is presentation's own inbound-HTTP-edge concept; OTel/tracing wiring in `13.ServiceDefaults` can choose to align with this domain's published key, but the dependency arrow must not point from `14.Presentation` back into `13.ServiceDefaults` for something this domain fully owns.

**How to apply:** When evaluating any future cross-domain dependency row for this domain, ask whether the "dependency" is actually this domain's own contract that another domain might *consume* (one-way, outward) versus a genuine implementation need (e.g., `ITenantProvider` from `12.Security.Abstractions`, which is a real `ProjectReference`). Don't list the former as a "Pending" blocking dependency — it isn't one.
