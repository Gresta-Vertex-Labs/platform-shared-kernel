# Messaging Phase Implementer — Memory Index

- [MassTransit 9.x Testing Patterns](project_masstransit_testing_patterns.md) — `file` modifier breaks type matching; Consumer suffix stripped; `await using` for ServiceProvider; SQLite outbox limitations; retry harness patterns
- [MassTransit 9.x Scheduler API](project_masstransit_scheduler_api.md) — SchedulePublish/CancelScheduledPublish APIs; ConcurrentDictionary token-type pattern; transport-aware wiring; obsolete APIs; name collision fix; scoped resolution in tests
- [MassTransit 9.x Idempotency Filter Wiring](project_masstransit_idempotency.md) — UseConsumeFilter open-generic API; open+closed DI registration; Build() guard without BuildServiceProvider(); test harness closed-generic registration pattern
- [MassTransit 9.x Header Propagation](project_masstransit_header_propagation.md) — IMessageHeaderPropagator wiring; propagator precedence (before explicit callback); ConsumerBase x-sk-* scope extraction; PublishContext alias in tests; static store test pattern
- [RoutingSlip TestHarness Patterns](project_routingslip_testharness_patterns.md) — Courier RoutingSlip TestHarness tests; GetExecuteActivityAddress; locale-safe decimal ToString; compensation assertions
- [MassTransit OTel Instrumentation](project_masstransit_otel_instrumentation.md) — MessagingDiagnostics.ActivitySource (SK.07.OTel/P-172); Consumer.Consume/EventPublisher.Publish activity points; ActivityListener parallel-test-isolation hazard
