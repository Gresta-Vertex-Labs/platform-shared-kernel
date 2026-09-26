# Messaging Phase Implementer — Memory Index

> Most entries were recorded on MassTransit 9.1.2; the platform is pinned to 8.5.x (P-560). Each file carries a WO-086 banner naming what changed.

- [MassTransit TestHarness Patterns](project_masstransit_testing_patterns.md) — `file` modifier breaks type matching; Consumer suffix stripped; `await using` for ServiceProvider; SQLite outbox limitations; retry harness patterns; locale-safe decimals
- [MassTransit Scheduler API](project_masstransit_scheduler_api.md) — SchedulePublish/CancelScheduledPublish APIs; ConcurrentDictionary token-type pattern; `WithDelayedDelivery` wiring in the transport satellites; name collision fix; scoped resolution in tests
- [MassTransit Idempotency Filter Wiring](project_masstransit_idempotency.md) — UseConsumeFilter open-generic API; keyed `IIdempotencyStore` (`IdempotencyPurpose.Message`); Build() guard without BuildServiceProvider(); closed-generic test registration
- [MassTransit Header Propagation](project_masstransit_header_propagation.md) — IMessageHeaderPropagator wiring/precedence; ConsumerBase x-sk-* extraction; propagator symmetry fix (P-341); request context via `SharedKernel.Execution`
- [MassTransit OTel Instrumentation](project_masstransit_otel_instrumentation.md) — MessagingDiagnostics.ActivitySource/Meter (P-172, P-348); dispatch-verb Activity+Meter coverage; ActivityListener/MeterListener parallel-test-isolation hazard
- [LoggerMessage SYSLIB1019 field requirement](project_loggermessage_syslib1019_field_requirement.md) — [LoggerMessage] generator needs an ILogger FIELD not property; static-partial+explicit-param workaround (WO-041 P-254)
- [MassTransit Concurrency Wiring](project_masstransit_concurrency_wiring.md) — obsolete MaxConcurrentCalls→ConcurrentMessageLimit; bus-level props are write-only; ConsumerDefinition<T> name collisions need `new`; NSubstitute/Castle needs public consumer types (P-342)
- [MassTransit Ordered Delivery](project_masstransit_ordered_delivery.md) — RoutingKeyExtensions.TrySetRoutingKey/ServiceBusSendContextExtensions.SetSessionId APIs (P-344); real-bus license gate vs test-harness exemption (recorded on 9.x)
- [MassTransit Serializer Customization](project_masstransit_serializer_customization.md) — ClearSerialization()+AddDeserializer(isDefault:true) required alongside AddSerializer for custom ISerializerFactory; Deserialize(ReceiveContext)+BodyConsumeContext is the real entry point; DictionarySendHeaders for test Headers (P-346)
- [MassTransit Retry Observability Gap](project_masstransit_retry_observability_gap.md) — IRetryObserver/IRetryObserverConnector unreachable in 9.1.2 (reflection-confirmed); ConsumeContext.GetRetryAttempt() alternative (P-348)
