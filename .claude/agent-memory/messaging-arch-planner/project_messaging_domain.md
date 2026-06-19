---
name: project-messaging-domain
description: All WO-020/021/022 phases complete; WO-027 P-172 (OTel ActivitySource) queued and unblocks pending cross-domain P-132
metadata:
  type: project
---

# Project: SharedKernel Messaging Domain

WO-020 (initial 6-phase delivery: Design/Scaffold/Core/Tests/Docs/Published) and WO-021 gap-fill (Resilience, Scheduling, Saga, Batch, Routing, Core anti-pattern fix) and WO-022 (Idempotency, HeaderPropagation, ConsumerDefinition, VersionTranslation, RoutingSlip) are ALL complete (●) as of 2026-06-10. The domain's two packages (`SharedKernel.Messaging.Abstractions`, `SharedKernel.Messaging.MassTransit`) are published at 1.0.0 with full XML docs and passing tests.

WO-027 (2026-06-19) added P-172 — a single-phase gap fix discovered via cross-domain review of the still-pending P-132 (`13.ServiceDefaults` OTel wiring). P-132 had assumed a `"SharedKernel.Messaging"` `ActivitySource` already existed in `07.Messaging` for it to register with the host's `TracerProvider`/`MeterProvider`. It does not exist. Per `13.ServiceDefaults`'s own brain rule, that domain never creates an `ActivitySource` on behalf of another domain — so the source had to be created here first.

**Why this matters:** This is a recurring pattern to watch for — when another domain's pending phase assumes an instrumentation source/contract exists in `07.Messaging` that doesn't, the fix is a same-domain phase here, not a workaround in the dependent domain.

**How to apply:** P-172 (`SK.07.OTel` phase key) has no dependencies and is purely additive — `MessagingDiagnostics.ActivitySource` static field in the MassTransit package, instrumentation in `ConsumerBase<TMessage>.Consume()` and `MassTransitEventPublisher.PublishAsync<TEvent>()`. Once P-172 ships, P-132 in `13.ServiceDefaults` becomes unblockable (it only needs to call `.AddSource("SharedKernel.Messaging")` on the host's `TracerProvider`).

Phase completion states as of 2026-06-19:

- Design/Scaffold/Core/Tests/Docs/Published: all complete (●)
- Resilience/Scheduling/Saga/Batch/Routing: all complete (●)
- Idempotency/HeaderPropagation/ConsumerDefinition/VersionTranslation/RoutingSlip: all complete (●)
- OTel (`SK.07.OTel`, P-172): 0/8 (○) — newly queued, WO-027

Cross-domain notes:

- P-132 (`13.ServiceDefaults` — wires "SharedKernel.Messaging" into host TracerProvider/MeterProvider) and P-133 (`00.Governance` — extended rules MSG0105-MSG0108) remain tracked in this state-map's Pending Phases table but are owned by other domains' planners.

Related: [[project-arch-decisions]]
