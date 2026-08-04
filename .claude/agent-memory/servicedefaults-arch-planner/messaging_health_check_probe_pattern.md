---
name: messaging-health-check-probe-pattern
description: WO-054/P-351 finding — dependency-specific health checks must wrap the owning domain's real configured connection, never build a second independent one from caller-supplied config
metadata:
  type: project
---

**The defect (found 2026-08-04, WO-054/P-351):** `AddRabbitMqMessagingHealthCheck(string amqpUri)` and
`AddAzureServiceBusMessagingHealthCheck(string connectionStringOrNamespace)` — both shipped since
WO-020/P-122 — each built their own second connection entirely from a caller-supplied config string
(`RabbitMQ.Client.ConnectionFactory`, `Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient`),
with zero reference to whatever `07.Messaging.MassTransit`'s `MessagingBusBuilder` actually configured for
the service. A passing check proved nothing about the real bus's health; a failing check could point at a
broker the service doesn't even use. This is worse than no health check — it actively misleads an operator
during an incident.

**Root cause pattern:** this domain's established rule "the owning domain ships the probe primitive,
`13.ServiceDefaults` ships the `IHealthCheck` adapter" (see `06.Persistence`/`08.Storage`/`09.Search`/
`10.Intelligence`/`17.Workflows` precedents) was *violated silently* for messaging back in WO-020, before
that rule was as firmly established across the domain. Instead of wrapping a probe `07.Messaging` itself
exposed, the two messaging checks reimplemented connectivity checking independently, using
community `AspNetCore.HealthChecks.*` packages directly against caller-supplied connection strings.

**The fix:** `07.Messaging` was dispatched a new bus-backed readiness-probe primitive as part of the same
WO-054 review — `IMessageBusProbe.ProbeAsync(ct) → Task<MessageBusHealth>` (`MessageBusHealth { bool
IsHealthy; string? Description; }`), registered unconditionally as a singleton by `MessagingBusBuilder
.Build()`. `13.ServiceDefaults` retired BOTH transport-specific methods outright (not deprecated — no
signature-compatible fix exists, since the connection-value parameter each accepted IS the defect) and
replaced them with one unified `AddMessagingReadinessCheck(string name = HealthCheckNames.Messaging)`
that resolves `IMessageBusProbe` from DI and takes **no connection or identifier parameter at all** —
`IMessageBusProbe` is a per-host singleton reflecting whichever single transport the service configured,
so there is nothing left for a call site to supply.

**Generalizable lesson for future phase review in this domain:** whenever auditing an existing
dependency-specific health check, check whether it constructs its own connection from caller-supplied
config, or whether it resolves a probe/handle the owning domain's own builder already registered in DI.
If it's the former, that's a structural defect regardless of whether the check "passes" in normal
operation — a configuration-drift scenario (health check pointed at a different broker/DB/cache than the
real one) must be impossible *by construction*, not merely by convention/documentation ("connection
details should be sourced from the resolved Options" was the old, insufficient wording — it only advised
against duplication, it didn't forbid a second connection outright).

**Test-proof pattern for this class of fix:** the test that proves "genuinely reflects the real bus, not
an independent connection" is structural, not just behavioral — register a stub for the probe interface
in a DI container with ZERO transport-specific packages/config wired in at all. If the check still passes
predictably, that's proof by construction that no independent connection path remains (there's nothing
else it could be exercising). This is a stronger proof than merely asserting Healthy/Unhealthy mapping,
and is worth calling out explicitly as its own acceptance-criterion test, not folded into a generic
calibration test.

**Breaking-change handling:** this is the *first* phase in this domain that both (a) is blocked on a
cross-domain dependency landing (`07.Messaging`'s `IMessageBusProbe`) AND (b) requires deleting
already-shipped public API. Precedent for "documented ahead of implementation, Core blocked" was
previously always additive-only (new methods, nothing removed). Handled it by: writing the CLAUDE.md
Interface Contracts entry for the target/locked contract as primary content (per the "CLAUDE.md is
forward-looking" rule), but with an explicit "IMPLEMENTATION STATUS" note stating the old methods
REMAIN LIVE IN SHIPPED CODE until the blocked Core task lands and deletes them — so a reader doesn't
assume the current compiled package already matches the documented target state. Also flagged the
SemVer-major repack implication in a dedicated Published-phase note, distinct from this domain's routine
"no new Published task, purely additive" note chain (WO-041 through WO-051 precedents) — since this one
genuinely isn't purely additive.

See [[health_check_tag_calibration]] for the updated tag/calibration table row.
