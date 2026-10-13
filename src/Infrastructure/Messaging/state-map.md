# 07.Messaging — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Messaging.Abstractions` | Abstractions | ● | `IMessageBus`/`IEventPublisher` (every verb returns `Result`), `IMessageScheduler`, `ISendEndpointResolver`, `IMessageHeaderPropagator`, `IFaultConsumer`, `IMessageVersionTranslator`, `MessagingOptions`. Open: two new `MessagingErrorCodes` (`SK.07.MessagingErrorSafety`), the `MessagingErrors` factory moves out (`SK.07.ObservabilityConformance`). |
| `SharedKernel.Messaging.MassTransit` | Adapter | ● | `AddSharedKernelMessaging(configuration).Use{Transport}(…)…Build()` — retry, circuit breaker, dead-letter policy, delayed delivery, consumer idempotency over `IIdempotencyStore`, ordered delivery, payload transform, telemetry, `messaging` probe, `WithInboundRequestContext()`. Pinned to MassTransit 8.5.x (last Apache-2.0 line). Open: log-scope allow-list, rethrow and scheduler fixes, two exceptions become `SharedKernelException`s (`SK.07.MessagingErrorSafety`); telemetry, logging and error conformance (`SK.07.ObservabilityConformance`). |
| `SharedKernel.Messaging.MassTransit.RabbitMq` | Adapter | ● | `UseRabbitMq(…)` transport satellite. Edge → MassTransit. Logs nothing; its sub-block is reserved (`SK.07.ObservabilityConformance`). |
| `SharedKernel.Messaging.MassTransit.AzureServiceBus` | Adapter | ● | `UseAzureServiceBus(…)` transport satellite. Edge → MassTransit. Open: its one log moves to its own sub-block (`SK.07.ObservabilityConformance`). |
| `SharedKernel.Messaging.MassTransit.EfCore` | Adapter | ● | `WithEntityFrameworkOutbox<TDbContext>()` outbox satellite. Edge → MassTransit. Logs nothing; its sub-block is reserved. |
| `SharedKernel.Messaging.Testing` | Testing | ● | `InMemoryMessageBus`, `InMemoryEventPublisher`. No contract changes in either open phase, so no double change; the `throw` texts of both doubles follow the E3 sweep (`SK.07.ObservabilityConformance`). |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

**Inbound**
- **01.Core, `SK.01.ObservabilityHelpers` (P-580):** `TelemetryVersion.Of`, `ActivityFailureExtensions` (`RecordFailure`, `RecordResult`, `RecordException`, `AddExceptionEvents`), `WellKnownOutcomes`, `WellKnownTagKeys.Outcome`, `IHasError`, `LoggingEventIdRanges.SubBlockStart`/`IsInSubBlock`. `SK.07.MessagingErrorSafety` needs only `SharedKernelException`, which exists today.
- **01.Core, `SK.01.CoreObservabilityFixes` (P-584):** adds an `Execution` to `Core` reference; this domain does not depend on it (it references `SharedKernel.Core` explicitly in `SK.07.MessagingErrorSafety` C-01).
- **01.Core, `RequestContextPropagation`/`PropagatedRequestContext`:** `SK.07.ObservabilityConformance` D-06 builds on the public `WriteHeaders`; if the always-present actor-kind header is refused, a tenant-and-correlation-only overload is the fallback request to 01.Core.
- **13.ServiceDefaults, `SK.13.WildcardTelemetry` (P-582):** exports the `SharedKernel.Messaging` meter by wildcard and narrows `WithMessagingTelemetry()` to the `MassTransit` source and meter. The source and meter names in this domain do not change, so there is no ordering constraint between P-582 and P-605; the host proof in `SK.07.MessagingErrorSafety` T-06 needs P-582 on disk.
- **16.Testing, `SK.16.TelemetryCapture` (P-581):** `ActivityRecorder`, `MetricRecorder`, `ActivityAssertions`, `MetricAssertions`, `LoggerAssertions` conventions for `SK.07.ObservabilityConformance`. `InMemoryLogger` scope capture (exists) covers `SK.07.MessagingErrorSafety` T-01.
- **00.Governance, `SK.00.ObservabilityReportOnly` (P-583):** the per-domain finding count that `SK.07.ObservabilityConformance` must bring to zero; SK0043 will flag any later exception here that exposes an `Error` without deriving `SharedKernelException`.

**Outbound (notes only; nothing here is a task of another domain)**
- **13.ServiceDefaults:** after both phases the instrument names `messaging.*` and the span names `Consumer.Consume` etc. change; dashboards built on them break (release note). The Shop's `WithMessagingTelemetry()` call sites stay valid.
- **14.Presentation / 11.Communication:** the two exceptions now carry an `Error` (`Conflict` and `Unexpected`); the HTTP (409/500) and gRPC (`Aborted`/`Internal`) status assertions belong in 14's tests if it wants them. They rarely reach an edge (consume side only).
- **16.Testing:** `SharedKernel.Messaging.Testing` needs no contract change; the E3 text rewrite of its `InvalidOperationException` messages is done in this domain (it lives in this folder) and `src/Testing/CLAUDE.md` catalogue wording needs no change unless it quotes those texts.
- **`samples/Shop`:** report line only: consumers or log queries keyed on `x-sk-actor-kind` as a scope key, or on `Consumer.Consume`/`messaging.*` names, need updating; the e2e flows (`samples/Shop/build.sh --e2e`) are the check.
- **Root `CLAUDE.md` (arch-lead / `/sync-brain`):** root T1 says "Meter through `IMeterFactory` where DI exists"; this domain keeps a static `Meter` for the reason in `SK.07.ObservabilityConformance` D-04 and C2 says the errors factory is internal (the Messaging factory moves into the core). If arch-lead prefers an explicit exception line in the standard, that is a root edit.
