---
name: ambient-logging-enrichment
description: WO-041/P-251's Activity.Baggage-based mechanism for making CorrelationId/TenantId ambient to every log record without cross-domain references
metadata:
  type: project
---

WO-041/P-251 (2026-07-09) added OTLP log export to `AddSharedKernelTelemetry` (a `.WithLogging(...)`
registration, additive to the existing tracing/metrics chain — no new public method) plus a generic
ambient-enrichment mechanism: `BaggageLogRecordProcessor` (sealed `BaseProcessor<LogRecord>` in
`Telemetry/`) copies every `Activity.Current?.Baggage` entry onto `LogRecord.Attributes` at `OnEnd`,
never overwriting an attribute already present at the same key.

**Key design decision — generic, not name-specific:** `BaggageLogRecordProcessor` must never hardcode
a baggage key name ("CorrelationId", "TenantId", etc.). This is what let it pick up `14.Presentation`'s
pre-existing CorrelationId `Activity`-baggage convention (WO-031) with **zero `13.ServiceDefaults` →
`14.Presentation` reference** — 13.ServiceDefaults doesn't need to know 14.Presentation's baggage key
exists at all. Any future domain that sets its own `Activity` baggage automatically gets free ambient
log enrichment with no `13.ServiceDefaults` change required. `SharedKernel.MultiTenancy`'s
`TenantResolutionMiddleware` uses the identical mechanism: calls
`Activity.Current?.SetBaggage(TenantBaggageKeys.TenantId, tenantId.ToString())` right after resolving
(or confirming `Guid.Empty`) — the new `TenantBaggageKeys` constants class holds the key name.

**Guid.Empty sentinel is set explicitly, not omitted.** The baggage value is written even when no
tenant resolves, so log aggregation can distinguish "no tenant for this request" (explicit empty-guid
string) from "TenantId enrichment was never wired at all" (key genuinely absent). This mirrors the
`Guid.Empty` no-tenant-sentinel philosophy already established for `TenantedDbContext`'s query filter
(06.Persistence P-092) — see [[health_check_tag_calibration]] for the sibling calibration-decision
pattern in this domain.

**Cross-domain acceptance-criterion testing without a layering violation:** when a phase's acceptance
criteria reference another domain's behavior (here: "CorrelationId set by 14.Presentation's middleware
must appear on log records") but `13.ServiceDefaults` cannot take a `ProjectReference` to that domain,
the test simulates the exact BCL-level mechanism the other domain is *documented* to use
(`Activity.Current?.SetBaggage("CorrelationId", ...)` called directly, since 14.Presentation's own
correlation-id middleware is documented in WO-031 as calling this same BCL API) rather than skipping
verification or reaching across the layering boundary. This pattern is reusable any time a phase's
acceptance criteria span a domain this planner cannot reference.

**Scope boundary, explicitly documented, not silently assumed:** the whole mechanism only covers the
HTTP-request path (wired through ASP.NET Core's per-request `Activity` and `TenantResolutionMiddleware`).
A message-consumption-scope equivalent (tenant/correlation ambient logging during MassTransit consumer
execution) would need a parallel mechanism in `07.Messaging`'s own consumer pipeline — explicitly out of
this domain's jurisdiction to dispatch. Don't try to solve it here; just document the gap.

**No new Published-phase task for additive internals.** Confirmed precedent from `WithApplicationTelemetry`
(WO-040/P-247): an additive internal type/method inside an already-published package does not need a new
`P-xx` Published task or version bump — only new consumer-facing NuGet-level surface changes do. Applied
the same reasoning here for `BaggageLogRecordProcessor`/`TenantBaggageKeys`.

See [[phase_sequencing]] and [[health_check_tag_calibration]] for the domain's other established
calibration/sequencing conventions this phase followed.

**Confirmed defect, found via ground-truth grep, not assumption (WO-042/P-261, 2026-07-14):** the
"CorrelationId" literal `BaggageLogRecordProcessorTests`/`AmbientLoggingEnrichmentAcceptanceTests`
hardcode never actually matched what `14.Presentation.CorrelationIdMiddleware.BaggageKey` writes in
production (`"correlation.id"`, lowercase-dotted, not `"CorrelationId"`). Confirmed by grepping
`14.Presentation`'s source directly rather than trusting either file's doc comments. Fix: `01.Core`
added `WellKnownHeaders`/`WellKnownBaggageKeys` (`SK.01.P259`) as the single shared source of truth
for cross-service propagation literals (header names + baggage keys) — this domain's retrofit
(`HeaderTenantResolutionStrategy.DefaultHeaderName` → `WellKnownHeaders.TenantId`;
`BaggageLogRecordProcessorTests`/`AmbientLoggingEnrichmentAcceptanceTests` → `WellKnownBaggageKeys.
CorrelationId`) is tracked as WO-042/P-261 in state-map.md (D-06/S-12/C-34/C-35/T-29–T-31/DO-05).
