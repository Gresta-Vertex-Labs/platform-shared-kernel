# 07.Messaging — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Messaging.Abstractions` | Abstractions | ● | `IMessageBus`/`IEventPublisher` (every verb returns `Result`), `IMessageScheduler`, `ISendEndpointResolver`, `IMessageHeaderPropagator`, `IFaultConsumer`, `IMessageVersionTranslator`, `MessagingOptions`. |
| `SharedKernel.Messaging.MassTransit` | Adapter | ● | `AddSharedKernelMessaging(configuration).Use{Transport}(…)…Build()` — retry, circuit breaker, dead-letter policy, delayed delivery, consumer idempotency over `IIdempotencyStore`, ordered delivery, payload transform, telemetry, `messaging` probe, `WithInboundRequestContext()`. Pinned to MassTransit 8.5.x (last Apache-2.0 line). |
| `SharedKernel.Messaging.MassTransit.RabbitMq` | Adapter | ● | `UseRabbitMq(…)` transport satellite. Edge → MassTransit. |
| `SharedKernel.Messaging.MassTransit.AzureServiceBus` | Adapter | ● | `UseAzureServiceBus(…)` transport satellite. Edge → MassTransit. |
| `SharedKernel.Messaging.MassTransit.EfCore` | Adapter | ● | `WithEntityFrameworkOutbox<TDbContext>()` outbox satellite. Edge → MassTransit. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |
| `SK.07.Design` | Design | ● |
| `SK.07.Scaffold` | Scaffold | ● |
| `SK.07.Core` | Core (incl. P-130) | ● |
| `SK.07.Tests` | Tests (incl. P-191) | ● |
| `SK.07.Docs` | Docs | ● |
| `SK.07.Published` | Published | ● |
| `SK.07.Resilience` | Resilience (P-125, P-126) | ● |
| `SK.07.Scheduling` | Scheduling (P-127) | ● |
| `SK.07.Saga` | Saga (P-128) — removed by P-560 (sagas belong to 17.Workflows) | ⊘ |
| `SK.07.Batch` | Batch (P-129) | ● |
| `SK.07.Routing` | Routing (P-131) | ● |
| `SK.07.OTel` | OTel (P-172) | ● |
| `SK.07.Idempotency` | Idempotency (P-134) | ● |
| `SK.07.HeaderPropagation` | HeaderPropagation (P-135) | ● |
| `SK.07.ConsumerDefinition` | ConsumerDefinition (P-136) | ● |
| `SK.07.VersionTranslation` | VersionTranslation (P-137) | ● |
| `SK.07.RoutingSlip` | RoutingSlip (P-139) — removed by P-560 | ⊘ |
| `SK.07.LoggingRetrofit` | LoggingRetrofit (P-254, P-263) | ● |
| `SK.07.EnvelopeTenancy` | EnvelopeTenancy (P-340) | ● |
| `SK.07.PropagationSymmetry` | PropagationSymmetry (P-341) | ● |
| `SK.07.ConsumerConcurrency` | ConsumerConcurrency (P-342) | ● |
| `SK.07.DeadLetter` | DeadLetter (P-343) | ● |
| `SK.07.OrderedDelivery` | OrderedDelivery (P-344) | ● |
| `SK.07.AmbientPropagation` | AmbientPropagation (P-345) | ● |
| `SK.07.PayloadTransform` | PayloadTransform (P-346) | ● |
| `SK.07.ReadinessProbe` | ReadinessProbe (P-347) | ● |
| `SK.07.DiagnosticsCoverage` | DiagnosticsCoverage (P-348) | ● |
| `SK.07.PackagingRecipes` | PackagingRecipes (P-349) | ● |
| `SK.07.PayloadTransformAad` | PayloadTransformAad (P-499) | ● |
| `SK.07.PrePublish` | PrePublish (P-560, P-561) | ● |
| `SK.07.Foundation` | Foundation (WO-086: P-564–P-570, P-575) | ● |

## Open Work

None — every phase in this domain is complete. The first public release ships with root P-577 (release train).

## Blocked

None.

## Cross-Domain Dependencies

None open.

## Completed Phases

- WO-086 ● `SK.07.Foundation` — tiers replace numbered layering; caller contract on `SharedKernel.Execution` (`WithInboundRequestContext()`); transport and outbox satellites split out; messaging `IIdempotencyStore` replaced by `18.Idempotency`'s (P-564–P-570, P-575) (2026-09-26)
- P-560, P-561 ● `SK.07.PrePublish` — pre-first-publish gold-standard pass (42 tasks): MassTransit pinned 8.5.10, request/response, Quartz, routing slips and sagas cut, every verb returns `Result`; first publish at `1.0.0-alpha.0.1171` (2026-09-23)
- P-499 ● Payload-transform encryption on async cryptography contracts with AAD (WO-081) (2026-09-08)
- P-340–P-349 ● WO-054 — envelope tenancy, propagation symmetry, consumer concurrency, dead-letter policy, ordered delivery, ambient propagation, payload transform, readiness probe, diagnostics coverage, packaging recipes (2026-08-07)
- P-254, P-263 ● Logging retrofit and log-scope correlation constant (WO-041, WO-042)
- P-191 ● Consumer-verify retrofit onto the in-memory doubles (WO-030)
- P-172 ● `ActivitySource` and consume/publish instrumentation (WO-027)
- P-134–P-139 ● WO-022 — consumer idempotency, header propagation, consumer definitions, version translation, routing slips (later removed)
- P-125–P-131 ● WO-021 — circuit breaker and fault consumers, scheduling, sagas (later removed), batch consumers, `Build()` fix, send-endpoint routing
- P-115–P-121 ● WO-020 — the domain build: scaffold, abstractions, MassTransit wiring, Azure Service Bus, retry and outbox, tests, docs and packaging

## Changelog

- [2026-09-28] State map slimmed to a living board; completed phase detail archived outside the repository — public-release cleanup
- [2026-09-26] Consumer idempotency key fix — key is now `{MessageId:D}:{sha256(endpoint path | consumer type)}`, so a second consumer of one message is no longer skipped
- [2026-09-26] SK.07.Foundation ● — WO-086 (P-564–P-570, docs P-575): tiers, `SharedKernel.Execution` caller contract, `ITenantContextAccessor`/`IMessageBusProbe`/messaging `IIdempotencyStore` removed, transport satellites
- [2026-09-23] SK.07.Published ● (P-06, P-07) — both packages published at `1.0.0-alpha.0.1171`, the domain's first publish
- [2026-09-23] SK.07.PrePublish ● — pre-first-publish gold-standard pass, 42 tasks over seven waves (P-560, P-561)
