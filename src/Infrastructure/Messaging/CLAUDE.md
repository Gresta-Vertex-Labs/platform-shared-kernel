# 07.Messaging — Domain Brain

> Transport-neutral contracts for publishing integration events and sending commands
> (`SharedKernel.Messaging.Abstractions`), and one opinionated MassTransit wiring layer behind them, with the broker
> transports (RabbitMQ, Azure Service Bus) and the EF Core outbox as optional satellite packages. Every dispatch verb
> returns `Result`; events travel in a CloudEvents `EventEnvelope<TEvent>`. This domain does **not** own sagas,
> routing slips or long-running coordination (`17.Workflows`), request/response over the bus (use `11.Communication`),
> loss-tolerant Pub/Sub (`02.Caching`'s `Caching.Redis.PubSub`), the idempotency store (`18.Idempotency`), the
> envelope type (`04.Contracts`) or any outbox type in `06.Persistence` — the outbox is MassTransit's, over the
> service's own `DbContext`. Consumers read the package `README.md` files and the folder `README.md`.

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Messaging.Abstractions` | Abstractions | What application code injects: `IMessageBus`, `IEventPublisher`, `PublishContext`, `IMessageScheduler`, `IMessageHeaderPropagator`, `IFaultConsumer<T>`/`FaultExceptionInfo`, `IMessageVersionTranslator<TOld,TNew>`, `ISendEndpointResolver`, `IInboundMessageContextAccessor`, `IMessagingBuilder`, `MessagingOptions`, `IdempotencyOptions`, `MessagingErrorCodes`/`MessagingErrors`. References `SharedKernel.Primitives`, `.Execution`, `.Contracts` |
| `SharedKernel.Messaging.MassTransit` | Adapter | The bus: `AddSharedKernelMessaging` + `MessagingBusBuilder`, consumer bases, retry, circuit breaker, dead-letter options, delayed delivery, consumer idempotency, ordered delivery, payload transform, the built-in propagators, the inbound caller filter, `MessagingDiagnostics`, the `messaging` readiness probe, the `MessagingTransport` extension point. **No broker client and no EF Core** |
| `SharedKernel.Messaging.MassTransit.RabbitMq` | Adapter | `UseRabbitMq(...)`, `RabbitMqBusOptions`, delayed-message exchange, dead-letter TTL, routing-key partitioning. Declared edge → core |
| `SharedKernel.Messaging.MassTransit.AzureServiceBus` | Adapter | `UseAzureServiceBus(...)`, `AzureServiceBusOptions`, managed identity, native scheduled enqueue, session-id partitioning, dead-letter advisory. Declared edge → core |
| `SharedKernel.Messaging.MassTransit.EfCore` | Adapter | `WithEntityFrameworkOutbox<TDbContext>()`, `OutboxOptions` (`MassTransit.EntityFrameworkCore`). Declared edge → core |

`consumer-verify/` (not packable, Unit lane) proves the injectable surface resolves from one registration chain.
Every package tracks its public API in `PublicAPI.*.txt`.

## Public Entry Points

```csharp
builder.Services.AddSharedKernelRequestContext();                    // the service's own IRequestContext FIRST
builder.Services.AddRedisIdempotency(p => p.ForMessages());          // store for IdempotencyPurpose.Message
builder.Services
    .AddSharedKernelMessaging(builder.Configuration)                 // MessagingOptions, "SharedKernel:Messaging"
    .UseRabbitMq(builder.Configuration.GetConnectionString("rabbitmq")!)   // or .UseAzureServiceBus(o => …)
    .WithEntityFrameworkOutbox<OrdersDbContext>()                    // optional, .EfCore
    .WithRetry().WithDelayedDelivery().WithIdempotency().WithInboundRequestContext()
    .AddConsumer<OrderPlacedConsumer, OrderPlacedConsumerDefinition>()
    .AddFaultConsumer<ChargeCard, ChargeCardFaultConsumer>()
    .WithSendEndpointRoute<ChargeCard>("billing-api-charge-card")
    .Build();
builder.Services.AddHealthChecks().AddSharedKernelReadiness();       // maps the "messaging" probe
builder.WithMessagingTelemetry();
```

- **Abstractions** — `IMessageBus` (scoped): `PublishAsync<T>` (fan-out), `SendAsync<T>` (one queue; route from `WithSendEndpointRoute<T>` else `{service-name}-{kebab-type}`). `IEventPublisher` (scoped): `PublishAsync<TEvent>` where `TEvent : class, IIntegrationEvent`. Both take an optional `Action<PublishContext>` (`WithCorrelationId`, `WithCausationId`, `WithTenantId`, `WithSubject`, `WithPartitionKey`, `WithHeader`) that runs **after** every propagator, so explicit values win. `IMessageScheduler` (`ScheduleAsync` → token, `CancelAsync`). `IInboundMessageContextAccessor.Current` (null outside a consume). Options: `MessagingOptions` (`SharedKernel:Messaging`; `ServiceName` lowercase slug `^[a-z0-9]+(-[a-z0-9]+)*$`, ≤ 100 chars), `IdempotencyOptions` (`SharedKernel:Messaging:Idempotency`; `LeaseDuration` 30 s < `ExpiryWindow` 24 h). Error codes: `messaging.unavailable`, `.endpoint_not_found`, `.serialization_failed`, `.publish_rejected`, `.invalid_message`, `.contract_violation`.
- **MassTransit core** — `AddSharedKernelMessaging(services, IConfiguration, Action<MessagingOptions>?)` / `(services, Action<MessagingOptions>?)` → `MessagingBusBuilder`; nothing is wired until `Build()`. Builder: `UseTransport(MessagingTransport)`; `ConfigureMassTransit(Action<IBusRegistrationConfigurator>)`; `AddConsumer<T>()`, `AddConsumer<T, TDefinition>()`, `AddBatchConsumer<T>(Action<BatchOptions>?)`, `AddFaultConsumer<TMessage, TConsumer>()`; `WithRetry`, `WithCircuitBreaker`, `WithDeadLetterPolicy`; `WithDelayedDelivery`, `WithSendEndpointRoute<T>`, `WithVersionTranslator<TOld, TNew, T>`, `WithPayloadTransform`; `WithIdempotency`; `WithHeaderPropagator<T>`, `WithAmbientCorrelationPropagation`, `WithTenantContext`, `WithInboundRequestContext`. Authoring bases: `ConsumerBase<T>` (`ConsumeAsync`), `BatchConsumerBase<T>`, `ConsumerDefinitionBase<T>` (`EndpointName`, `PrefetchCount`, `ConcurrentMessageLimit`, `NonRetryableExceptions`, `ConfigureConsumer`). Options (namespace `SharedKernel.Messaging.MassTransit.Options`): `RetryOptions` (3 attempts, 1 s + 1 s, max 30 s), `CircuitBreakerOptions`, `BatchOptions`, `DeadLetterOptions`, `PayloadTransformOptions`. Probe name constant `MessagingReadinessProbeNames.Bus`.
- **Satellites** — extension methods on `MessagingBusBuilder` declared in the builder's own namespace (`SharedKernel.Messaging.MassTransit.Extensions`), options in `SharedKernel.Messaging.MassTransit.Options`: `UseRabbitMq(connectionString | Action<RabbitMqBusOptions>)`; `UseAzureServiceBus(connectionString | Action<AzureServiceBusOptions>)` (exactly one of `ConnectionString` / `FullyQualifiedNamespace` with `DefaultAzureCredential`); `WithEntityFrameworkOutbox<TDbContext>(Action<OutboxOptions>?)`.

## Rules & Invariants

1. **MassTransit is pinned to 8.5.x (8.5.10), the last Apache-2.0 release.** 9.x ships under a commercial licence; every package here declares MIT. **Never bump to 9.x without a licensing decision recorded in the root brain.**
2. Application and domain-service projects reference only `SharedKernel.Messaging.Abstractions`; the startup project references the core and exactly the satellites it uses.
3. The core references no broker client, Azure SDK or EF Core — a new broker is a new satellite, never a branch in the core. Satellites reference only the core, never each other (`DependencyGraphRulesTests`, file `OptionalDependencySatelliteRulesTests.cs`).
4. No messaging package references `06.Persistence`, any `SharedKernel.Caching.*`, MediatR, `SharedKernel.Application` or `.Application.Pipeline` (and no caching package references messaging). The caller contract is `SharedKernel.Execution`.
5. **Every verb returns `Result`.** `MessagingExceptionClassifier` maps only recognised transport faults to `messaging.*` codes; anything else rethrows, so a bug is never laundered into a failed `Result`. Only cancellation throws. `messaging.unavailable` is `ErrorType.Unavailable` (503).
6. Never inject MassTransit's `IBus`, `IPublishEndpoint`, `ISendEndpointProvider` or `IMessageScheduler` outside this domain (SK0706, architecture rules); never configure MassTransit directly in a service — use `ConfigureMassTransit(...)`. Never start/stop `IBusControl` manually; `IMessageBus`/`IEventPublisher` are scoped, never singleton.
7. **CloudEvents**: `MassTransitEventPublisher` builds every envelope with `EventEnvelope.Wrap(evt, source:, subject:, tenantId:, correlationId:, causationId:)` — the only construction path. `source` = `ServiceName`; `type`/`dataversion` from `[IntegrationEvent]`; `id` = `EventId`. Wrap failures map to `messaging.invalid_message` / `.contract_violation` before any transport work. Tenant and correlation id are also written as transport headers.
8. Event wire names come from `IntegrationEventDescriptor.For<TEvent>().Name`, never `typeof(TEvent).Name`. Domain events never go through the bus; a domain-event handler maps them to an `IIntegrationEvent`.
9. **Correlation id** precedence: `PublishContext.CorrelationId` → propagated `X-Correlation-Id` → `CorrelationIds.Current(RequestContextScope.Current)` → `CorrelationIds.New()`. Never an `Activity` trace/span id.
10. Both verbs map `PublishContext` onto the transport through the one internal `PublishContextPipe`; propagators run on every verb, in registration order. A new context field goes in the pipe and gets a parity test.
11. Only three `IMessageHeaderPropagator`s live in `SharedKernel.*` (`AmbientCorrelationHeaderPropagator`, `TenantHeaderPropagator`, `RequestContextHeaderPropagator`) — do not add a fourth. A propagator never takes identity from `Activity` baggage; identity comes from `IRequestContext`.
12. **`WithInboundRequestContext()`**: publish writes correlation, tenant and actor through `RequestContextPropagation` (`WellKnownHeaders`); consume rebuilds a `PropagatedRequestContext`, opens a `RequestContextScope`, and `MessageAwareRequestContext` answers `IRequestContext` from the message inside a consume. It runs ahead of every other consume filter, including idempotency. Register it **after** the service's own `IRequestContext` (wrong order logs 7011). A malformed tenant header yields no tenant; a missing correlation id is replaced.
13. **Attribution, not authorization.** `PropagatedRequestContext.HasPermissionAsync` always answers `false`; broker headers are writable by anyone with broker access.
14. **Consumer idempotency** (`WithIdempotency()`) uses `[FromKeyedServices(IdempotencyPurpose.Message)] IIdempotencyStore`; `Build()` throws without one. Key `{MessageId:D}:{sha256-hex("{receive-endpoint path}|{consumer full type name}")}` so each consumer deduplicates its own deliveries; the store scopes by the ambient tenant. No `MessageId` → pass through; `Completed` → acknowledge without consuming; `InProgress` → `ConcurrentMessageDeliveryException` (expected; message stays unacknowledged); `FingerprintMismatch` → store defect, throws; exception → `ReleaseAsync` then rethrow. Never hand-roll deduplication in `ConsumeAsync`.
15. **Never swallow an exception in `ConsumeAsync`** — it acknowledges and loses the work. Non-retryable types go in `ConsumerDefinitionBase.NonRetryableExceptions`. `ConsumerBase.Consume` logs and **rethrows**. Never override `IConsumerDefinition.Configure` directly.
16. Registration shapes: `IFaultConsumer<T>` only via `AddFaultConsumer` (it is an observer — the message is already in the error queue); `BatchConsumerBase<T>` only via `AddBatchConsumer`; circuit breaker is global only (retry inner, breaker outer); no `Task.Delay` as deferral — use `IMessageScheduler` (broker-side).
17. Cross-service commands route only through `WithSendEndpointRoute<T>("{target-service}-{command-type}")`; never a hard-coded `queue:` address. Queues are `{service-name}-{consumer}` kebab-case (`Consumer` suffix dropped).
18. **Ordered delivery** uses only the transport's native mechanism via `MessagingTransport.ApplyPartitionKey` (RabbitMQ routing key; ASB session id — sessions enabled in infrastructure). Ordering holds per key with a single active consumer. No sequencing buffers.
19. **Dead-letter**: RabbitMQ `WithDeadLetterPolicy()` sets `x-message-ttl` on MassTransit's `_error`/`_skipped` queues; `DeadLetterOptions.QueueNameSuffix` is accepted but has **no effect** (MassTransit exposes no rename hook). Azure Service Bus uses native dead-lettering; the policy only logs an advisory (7010).
20. **Payload transform** is always compress-then-encrypt / decrypt-then-decompress (not configurable), using `01.Core`'s `IPayloadCompressor` and the **synchronous** `ISynchronousSymmetricEncryptionService` (MassTransit's serializer is synchronous — a KMS-only async key provider is unsupported). Associated data is the CLR type name in the plaintext header `PayloadTransformHeaders.MessageTypeAad`; any mismatch is a loud `PayloadTransformMismatchException`, never a fallback. `Build()` fails when the matching `01.Core` service is missing.
21. `IMessageVersionTranslator.Translate` is pure and synchronous (no I/O); translation is a `VersionTranslatingConsumer<TOld,TNew>` that republishes `TNew`.
22. **Readiness**: `Build()` always registers `MassTransitMessageBusProbe` as the `messaging` `IReadinessProbe`, over MassTransit's `BusHealthCheck` on the registered `IBusInstance` resolved inside `ProbeAsync` — never a second connection, never an `IHealthCheck` shipped here.
23. `Build()` fails fast with the fix in the message, never calls `BuildServiceProvider()`, and runs logger-needing advisories in startup `IHostedService`s.
24. `MessagingOptions` bind only through `AddValidatedOptions` (the core references `SharedKernel.Configuration`; `.Abstractions` does not).
25. No static mutable state except `MessagingDiagnostics.ActivitySource`/`.Meter` and its instruments. Consumer log scopes via `MessagingLogScope.Create(correlationId)`, never a hand-built dictionary.
26. The outbox is at-least-once: messages published in a unit of work are written in the same transaction and delivered after commit; the `DbContext` maps `AddInboxStateEntity()`, `AddOutboxMessageEntity()`, `AddOutboxStateEntity()` and the service owns the migration (tables must exist before the bus starts).
27. Never commit transport credentials to `appsettings.json` (`RabbitMqBusOptions` defaults to `guest`).
28. AOT is preferred but not claimed: MassTransit, transports and the outbox are not NativeAOT-verified; `EventEnvelope<TEvent>` uses reflection-based STJ.

## Decisions

| Decision | Why |
| --- | --- |
| MassTransit 8.5.x pin | Last Apache-2.0 release; a 9.x bump is a licence change disguised as a dependency update |
| Transports and outbox as satellites | The core carries no broker client or EF Core; a service restores only what it uses |
| Failures are `Result` values; unknown exceptions rethrow | Broker outages are operational conditions; bugs must stay loud |
| MassTransit's EF Core outbox, no kernel outbox type | One outbox implementation; `06.Persistence` stays messaging-free |
| No sagas, routing slips or request/response | Coordination belongs to `17.Workflows`; synchronous calls to `11.Communication` |
| Caller propagated for attribution only | Broker headers are unauthenticated |
| Idempotency key includes endpoint + consumer type | Two consumers of one message must each process it once |
| Native ordered delivery only | A kernel sequencing layer would duplicate and fight the broker |
| Declined: `DeadLetterOptions.QueueNameSuffix` via `BindDeadLetterQueue` | That wires broker NACK/TTL dead-lettering, which a consumer exception never reaches |

## Logging

Block **7000–7999** (`LoggingEventIdRanges.Messaging`). One sub-block, **7000–7099**, shared by the MassTransit core and its satellites; `.Abstractions` does not log.

| EventId | Method | Level | Declaring type |
| --- | --- | --- | --- |
| 7001 | `LogConsumeError` | Error | `ConsumerBase<T>` |
| 7002 | `LogBatchEntry` | Information | `BatchConsumerBase<T>` |
| 7003 | `LogBatchError` | Error | `BatchConsumerBase<T>` |
| 7004 | `LogFaultHandling` | Error | `FaultConsumerAdapter<,>` |
| 7005 | `LogFaultConsumerError` | Error | `FaultConsumerAdapter<,>` |
| 7006, 7007 | retired — never reuse | — | — |
| 7008 | `LogTranslating` | Debug | `VersionTranslatingConsumer<,>` |
| 7009 | translator without consumer | Warning | `TranslatorRegistrationValidator` |
| 7010 | dead-letter policy ignored under ASB | Warning | `DeadLetterPolicyAdvisoryHostedService` (`.AzureServiceBus`) |
| 7011 | `IRequestContext` registered after `WithInboundRequestContext()` | Warning | `RequestContextRegistrationAdvisoryHostedService` |
| 7012 | `LogRequestContextNotResolvable` | Debug | `RequestContextRegistrationAdvisoryHostedService` |

Next free: 7013. The `[LoggerMessage]` generator discovers only an `ILogger` **field**; the consumer bases expose a `Logger` property, so their log methods take `ILogger` as a parameter — prefer a field in new types.

Diagnostics: `ActivitySource` and `Meter` named `SharedKernel.Messaging` (tags from `MessagingTagKeys`), wired by `13.ServiceDefaults`' `WithMessagingTelemetry()`. Activities `Consumer.Consume`, `EventPublisher.Publish`, `MessageBus.Publish`, `MessageBus.Send`; instruments `messaging.consume.count`/`.duration`, `messaging.retry.count` (per invocation via `GetRetryAttempt()`), `messaging.publish.count`, `messaging.send.count`, `messaging.fault.count`.

## Cross-Domain Couplings

- **01.Core** — `SharedKernel.Execution` (`IRequestContext`, `RequestContextScope`, `RequestContextPropagation`, `CorrelationIds`, `PropagatedRequestContext`, `TenantId`); `SharedKernel.Primitives` (`WellKnownHeaders`, `IReadinessProbe`); `Configuration`, `Compression`, `Cryptography` (payload transform).
- **04.Contracts** — `IIntegrationEvent`, `[IntegrationEvent]`, `EventEnvelope.Wrap`, `IntegrationEventDescriptor`.
- **18.Idempotency** — `IIdempotencyStore` for `IdempotencyPurpose.Message` (`AddRedisIdempotency(p => p.ForMessages())`, `AddEfCoreIdempotency(…)`, `AddIdempotencyStore<T>(purpose)`); tenant scoping via `IdempotencyTenantScope`.
- **13.ServiceDefaults** — `AddSharedKernelRequestContext()` must precede `WithInboundRequestContext()`; `AddSharedKernelReadiness()` maps the `messaging` probe; `WithMessagingTelemetry()`; its log processor keeps only platform baggage keys from MassTransit's copied context.
- **06.Persistence** — none from this side; the `.EfCore` outbox binds to the service's own `DbContext` by type parameter.
- **16.Testing** — `SharedKernel.Messaging.Testing` fakes; `SharedKernel.Testing.Internal`'s `TestHarnessFactory`.
- **00.Governance** — `DependencyGraphRulesTests` (satellite rules in `OptionalDependencySatelliteRulesTests.cs`), `RedisTopologyRules.MessagingNeverReferencesCaching`, `MessagingArchitectureRules` (no direct bus injection, no event publisher in the domain layer), SK0706.

## Testing

- Lanes: `Messaging.Abstractions.Tests`, `.AzureServiceBus.Tests` (no live broker), `.EfCore.Tests` (SQLite with a kept-open `:memory:` connection) and `consumer-verify` are **Unit**; `SharedKernel.Messaging.MassTransit.Tests`, `.RabbitMq.Tests` (Testcontainers) and `.EfCore.Integration.Tests` are **Integration**.
- Consumer and pipeline tests use MassTransit's `AddMassTransitTestHarness()` (package `MassTransit.TestFramework`), waiting on `harness.InactivityTask`, and go through the real builder — never internals registered by hand.
- Round trips, not halves: `InboundRequestContextTests`, `AmbientPropagationTests` (header names agree both ways) and `DispatchContextParityTests` (`IMessageBus` and `IEventPublisher` write the same transport context).
- Idempotency: `Completed`, `InProgress` (not acknowledged), release-on-throw, null `MessageId`. Payload transform: each flag alone and both, mismatch both ways, AAD via one `DictionarySendHeaders`. Ordered delivery and dead-letter TTL run against real RabbitMQ.
- Diagnostics listeners are process-wide — filter by a test-unique tag value. Logs are asserted by `EventId`, never message text.
- MassTransit traps: never the `file` modifier on consumer/message/`DbContext` types; NSubstitute cannot proxy a MassTransit generic closed over an `internal` type; `BusHealthCheck` needs `HealthCheckContext.Registration`; resolving `IBusInstance` outside the test harness hits MassTransit's licence gate; alias `MassTransit.PublishContext`/`MassTransit.IMessageScheduler` where both are visible; a custom serializer factory needs `ClearSerialization()` plus both `AddSerializer` and `AddDeserializer`.
- The Shop (`samples/Shop`: Ordering's outbox and `OrderPlacedConsumer`, Billing's outbox, the Notify and Reports consumers) runs the packed packages against real RabbitMQ (`masstransit/rabbitmq` with the delayed-exchange plugin) — run its flows (`samples/Shop/build.sh --e2e`) when dispatch changes; a real broker has found defects no unit test caught.
- Consumer fakes: `SharedKernel.Messaging.Testing` (`InMemoryMessageBus`, `InMemoryEventPublisher`, `AddInMemoryMessageBus()`, `AddInMemoryEventPublisher()`); `SharedKernel.Idempotency.Testing`'s `AddFakeIdempotencyStore(IdempotencyPurpose.Message)`.

## Known Limitations

- **The bus starts in the background**: a message published before the broker connection and queue bindings exist is dropped by the broker — successfully. Gate traffic on readiness (tests too); never a sleep.
- `DeadLetterOptions.QueueNameSuffix` has no effect on RabbitMQ; ASB ignores `WithDeadLetterPolicy()` entirely.
- Payload encryption needs a synchronous key provider; KMS-only providers are unsupported.
- Ordering holds only per partition key with a single active consumer instance; ASB needs sessions enabled on the entity.
- Inbound caller headers are attribution only and can be forged by anyone with broker access; encrypt payloads and derive tenancy from signed content where that matters.
- MassTransit's retry observer is unreachable from the bus configurators, so retries are counted per invocation.
- NativeAOT services must supply their own `JsonSerializerContext` for every message and envelope type.
