---
name: project-messaging-domain
description: All WO-020/021/022/027/030 phases complete; WO-041 P-254 (logging retrofit) queued in SK.07.LoggingRetrofit
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

WO-030 (2026-06-24) added P-191 — a narrow Tests-phase retrofit, NOT new architecture. `SharedKernel.Messaging.Abstractions.Tests/ConsumerVerifyTests.cs` (the domain's own consumer-verify suite, P-121/WO-020) used raw `Substitute.For<IMessageBus>()`/`Substitute.For<IEventPublisher>()` instead of `16.Testing`'s purpose-built `InMemoryMessageBus`/`InMemoryEventPublisher` doubles — the exact duplication those doubles (added in WO-022, P-134-era testing infra) exist to prevent, surfacing inside `07.Messaging` itself. This demoted the previously-closed `SK.07.Tests` phase from ● back to ○ (17/20, tasks T-18→T-20 added). Pattern to watch for: when `16.Testing` ships a double for one of this domain's own interfaces, check this domain's own test projects for ad-hoc NSubstitute stubs of the same interface — they are candidates for the same retrofit.

WO-041 (2026-07-09) added P-254 — a logging-authoring retrofit dispatched as part of the platform-wide logging standard (root WO-041, P-249 in 01.Core, P-250 in 00.Governance). This domain had THREE confirmed internal `EventId` collisions (raw literals 1/2/3 each reused across unrelated hand-written `LoggerMessage.Define<>()` delegates in `ConsumerBase`, `BatchConsumerBase`, `FaultConsumerAdapter`, `RoutingSlipActivityBase`, `VersionTranslatingConsumer`) plus one raw `_logger.LogError(...)` call in `FaultConsumerAdapter` — distinct from the other WO-041 domains, which only had the pure authoring-style problem. New Phase: LoggingRetrofit (`SK.07.LoggingRetrofit`, LR-01–LR-18) queued, 0/18 as of 2026-07-09.

**Why this matters:** unlike other WO-041 domains, this one also had a second latent defect — four independently hand-rolled `BeginScope` dictionary constructions across the same four base types, which is *how* the EventId collisions accumulated (four authors, four copy-pasted patterns, no shared source of truth). The fix bundles both: EventId renumbering AND a new shared `MessagingLogScope.Create(Guid?)` helper.

**How to apply:** see [[project-arch-decisions]] for the final EventId table and the `MessagingLogScope` design — both are now also documented in `07.Messaging/CLAUDE.md` under "Logging authoring standard and EventId allocation" and "Shared log scope construction". This domain's reserved range is `7000-7999` (`07 * 1000`); since only `SharedKernel.Messaging.MassTransit` logs (Abstractions has zero logging deps), no sub-block division beyond `7000-7099` was needed. When a future phase adds more `[LoggerMessage]` methods to this package, continue sequentially from `7010`.

Related: [[project-arch-decisions]]
