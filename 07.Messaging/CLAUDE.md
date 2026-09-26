# 07.Messaging — Message Bus and Event Publishing

## What This Domain Is

The messaging capability domain: transport-neutral contracts for publishing events and sending commands, and one
opinionated MassTransit wiring layer behind them, with the broker transports and the EF Core outbox as optional
satellite packages.

Philosophy: **Abstraction-first. Transport-swappable. Outbox-native. CloudEvents-compliant. Failures are values.**

> **Outbox ownership.** The transactional outbox is MassTransit's EF Core outbox, wired by
> `SharedKernel.Messaging.MassTransit.EfCore` over the consuming service's own `DbContext`, passed as a generic
> type parameter. No outbox type (`OutboxMessage`, `IOutboxWriter`, an outbox interceptor) is ever defined in
> `06.Persistence`, and no messaging package references `06.Persistence`.

---

## Packages and tiers

Every project declares a `<SharedKernelTier>`; the build enforces the tier rules (`eng/SharedKernelTiers.targets`,
SKTIER001–006 are errors). An Adapter → Adapter edge must be listed in `<SharedKernelAllowedAdapterReferences>`.

| Package | Tier | Role | References |
| --- | --- | --- | --- |
| `SharedKernel.Messaging.Abstractions` | Abstractions | What application code injects: `IMessageBus`, `IEventPublisher` (every verb returns `Result`), `PublishContext`, `IMessageScheduler`, `IMessageHeaderPropagator`, `IFaultConsumer<T>`/`FaultExceptionInfo`, `IMessageVersionTranslator<TOld,TNew>`, `ISendEndpointResolver`, `IInboundMessageContextAccessor`, `IMessagingBuilder`, `MessagingOptions`, `IdempotencyOptions`, `MessagingErrorCodes`/`MessagingErrors`. No transport dependency, no configuration binder | `SharedKernel.Primitives`, `SharedKernel.Execution` (`IRequestContext`, `TenantId`), `SharedKernel.Contracts` (`IIntegrationEvent`), `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `SharedKernel.Messaging.MassTransit` | Adapter | The bus: `AddSharedKernelMessaging` + `MessagingBusBuilder`, `MassTransitMessageBus`, `MassTransitEventPublisher`, `ConsumerBase<T>`/`BatchConsumerBase<T>`/`ConsumerDefinitionBase<T>`, retry, circuit breaker, dead-letter options, delayed delivery, consumer idempotency, ordered delivery, payload transform, the built-in propagators, the inbound caller filter, `MessagingDiagnostics`, the `messaging` readiness probe, and the `MessagingTransport` extension point. **No broker client and no EF Core** | Messaging.Abstractions, `SharedKernel.Idempotency.Abstractions`, Contracts, Configuration, Compression, Cryptography, `MassTransit` 8.5.10, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` (types only) |
| `SharedKernel.Messaging.MassTransit.RabbitMq` | Adapter | `UseRabbitMq(...)`, `RabbitMqBusOptions`, the delayed-message exchange, dead-letter queue TTL, routing-key partitioning | MassTransit core (declared edge), `MassTransit.RabbitMQ` |
| `SharedKernel.Messaging.MassTransit.AzureServiceBus` | Adapter | `UseAzureServiceBus(...)`, `AzureServiceBusOptions`, managed identity, native scheduled enqueue, session-id partitioning, the dead-letter advisory warning | MassTransit core (declared edge), `MassTransit.Azure.ServiceBus.Core`, `Azure.Identity` |
| `SharedKernel.Messaging.MassTransit.EfCore` | Adapter | `WithEntityFrameworkOutbox<TDbContext>()`, `OutboxOptions` | MassTransit core (declared edge), `MassTransit.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore` |

- Application and domain-service projects reference **only** `SharedKernel.Messaging.Abstractions`. The startup
  project references the MassTransit core and exactly the satellites it uses.
- The satellites add extension methods to `MessagingBusBuilder` declared **in the builder's own namespace**
  (`SharedKernel.Messaging.MassTransit.Extensions`), so `AddSharedKernelMessaging(...).UseRabbitMq(...)` needs no
  extra `using`. Their options types keep the `SharedKernel.Messaging.MassTransit.Options` namespace.
- Transports plug in through `MessagingBusBuilder.UseTransport(MessagingTransport)`: a transport overrides
  `Configure(IBusRegistrationConfigurator, MessagingTransportSettings)` and optionally `ConfigureServices` and
  `ApplyPartitionKey`. Other integrations use `ConfigureMassTransit(Action<IBusRegistrationConfigurator>)`.
  A new broker is a new satellite, never a branch inside the core.
- Locked by governance: `OptionalDependencySatelliteRulesTests` (the core references no transport, Azure or EF
  Core package and loads no broker client; each satellite is an Adapter with a declared edge to the core),
  `RedisTopologyRules.MessagingNeverReferencesCaching`, `MessagingArchitectureRules.NoDirectBusInjectionOutsideMessaging`
  / `.NoEventPublisherInDomainLayer`, `ExtendedMessagingArchitectureRules.NoDirectMassTransitSchedulerInjection` (SK0706).
- **No messaging package may reference any `SharedKernel.Caching.*` package, and no caching package may reference
  messaging.** Loss-tolerant Redis Pub/Sub stays in `02.Caching` (`SharedKernel.Caching.Redis.PubSub`); it is not a
  durable-delivery transport and must never move here.
- **No messaging package references MediatR, `SharedKernel.Application` or `SharedKernel.Application.Pipeline`.**
  The caller contract (`IRequestContext`, `ActorKind`, `PropagatedRequestContext`, `RequestContextScope`) is
  `SharedKernel.Execution`, a Foundation package every tier may reference.

All projects target `net10.0` with `Nullable` and `ImplicitUsings` enabled. Each package's public API is tracked in
`PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt`; an untracked change fails the build. All packages ship with the
repo-wide release train (one MinVer version from a `v*` tag); never add a `<Version>` to a csproj.

> ### The MassTransit version is a licensing decision, not a preference
>
> **Pinned to 8.5.x because that is the last Apache-2.0 MassTransit release.** 9.x carries a bare `licenseUrl`
> pointing at massient.com, no SPDX expression and a "Massient, Inc." copyright. Every package here declares MIT,
> so a 9.x pin would ship a package that claims MIT while imposing a commercial obligation on every consumer.
> 8.5.10 has a native `net10.0` target. **Do not bump to 9.x without a licensing decision recorded in the root
> brain** — a silent bump is a licence change disguised as a dependency update (P-560).

---

## Technology Stack

| Concern | Technology | Version |
| --- | --- | --- |
| Message bus | `MassTransit` | 8.5.10 |
| RabbitMQ transport | `MassTransit.RabbitMQ` (`.RabbitMq` satellite) | 8.5.10 |
| Azure Service Bus transport | `MassTransit.Azure.ServiceBus.Core` + `Azure.Identity` (`.AzureServiceBus` satellite) | 8.5.10 / central pin |
| Transactional outbox | `MassTransit.EntityFrameworkCore` — NOT `MassTransit.EntityFrameworkCoreIntegration` (`.EfCore` satellite) | 8.5.10 |
| Options binding and validation | `SharedKernel.Configuration` (`AddValidatedOptions`) | — |
| CloudEvents envelope | `EventEnvelope<TEvent>` from `SharedKernel.Contracts` | — |
| Caller context | `SharedKernel.Execution` (`IRequestContext`, `RequestContextScope`, `RequestContextPropagation`, `CorrelationIds`) | — |
| Idempotency store contract | `SharedKernel.Idempotency.Abstractions` (`IIdempotencyStore`, purpose `Message`) | — |
| Readiness | `SharedKernel.Primitives.Health` (`IReadinessProbe`, `AddReadinessProbe<T>()`) | — |
| Serialization | System.Text.Json through MassTransit's STJ serializer | BCL |
| Test framework | `MassTransit.TestFramework` (NOT `MassTransit.Testing`), xUnit, FluentAssertions, NSubstitute, Testcontainers.RabbitMq | central pins |

---

## Interface Contracts

### `SharedKernel.Messaging.Abstractions`

```text
IMessageBus  (scoped)
    PublishAsync<T>(T message, CancellationToken)                              → Task<Result>   fan-out, nobody named
    PublishAsync<T>(T message, Action<PublishContext> configure, CancellationToken) → Task<Result>
    SendAsync<T>(T command, CancellationToken)                                 → Task<Result>   one queue, one consumer
        The queue comes from WithSendEndpointRoute<T>(queue), else ConventionSendEndpointResolver.

IEventPublisher  (scoped)
    PublishAsync<TEvent>(TEvent evt, CancellationToken)                         → Task<Result>
    PublishAsync<TEvent>(TEvent evt, Action<PublishContext> configure, CancellationToken) → Task<Result>
        where TEvent : class, IIntegrationEvent — wrapped in EventEnvelope<TEvent> by EventEnvelope.Wrap.

PublishContext  (sealed mutable builder; the callback runs AFTER every propagator, so an explicit value wins)
    CorrelationId Guid? · CausationId Guid? · TenantId TenantId? · Subject string? · PartitionKey string?
    Headers IReadOnlyDictionary<string,string>
    WithCorrelationId · WithCausationId · WithTenantId(TenantId) · WithSubject · WithPartitionKey · WithHeader
    Subject and CausationId reach only the IEventPublisher envelope; TenantId and CorrelationId travel as transport
    headers on every verb; PartitionKey maps to the transport's ordered-delivery mechanism.

IMessageScheduler  (scoped; registered by WithDelayedDelivery())
    ScheduleAsync<T>(T message, DateTimeOffset deliverAt, CancellationToken) → Task<Guid>   the BROKER holds it
    CancelAsync(Guid token, CancellationToken)                              → Task          no-op for unknown/delivered

IMessageHeaderPropagator  (scoped; WithHeaderPropagator<T>())
    Propagate(PublishContext context) → void — runs on every verb (PublishAsync, SendAsync, IEventPublisher), in
    registration order, before the caller's callback.

IFaultConsumer<TMessage>  (AddFaultConsumer<TMessage, TConsumer>() only — never services.AddScoped)
    HandleAsync(Guid faultId, DateTimeOffset faultTimestamp, TMessage message,
                IReadOnlyList<FaultExceptionInfo> exceptions, CancellationToken) → Task
    An observer: by the time it runs the message is already in the error queue.

IMessageVersionTranslator<TOld, TNew>  (singleton; WithVersionTranslator<TOld, TNew, T>())
    Translate(TOld old) → TNew — synchronous, pure, no I/O.

ISendEndpointResolver  — Resolve<T>() → queue name; default "{service-name}-{kebab-type}". Internal plumbing.

IInboundMessageContextAccessor  (scoped; WithInboundRequestContext())
    Current → IRequestContext? — the delivery's rebuilt caller, null outside a consume. Most code injects
    IRequestContext instead; inject this only to branch on whether the work arrived over the bus.

MessagingOptions   section "SharedKernel:Messaging"; ServiceName: lowercase slug ^[a-z0-9]+(-[a-z0-9]+)*$, ≤100 chars.
IdempotencyOptions section "SharedKernel:Messaging:Idempotency"; LeaseDuration (30 s), ExpiryWindow (24 h);
                   Build() requires 0 < LeaseDuration < ExpiryWindow.
MessagingErrorCodes / MessagingErrors — messaging.unavailable, .endpoint_not_found, .serialization_failed,
                   .publish_rejected, .invalid_message, .contract_violation.
```

> **Every verb returns `Result` (P-560).** An unreachable broker, a missing endpoint or an unserializable payload
> is an operational condition with a `messaging.*` code. `MessagingExceptionClassifier` maps only recognised
> transport faults; anything else is rethrown, so a genuine bug is never laundered into a failed `Result`. Only
> cancellation throws.

### `SharedKernel.Messaging.MassTransit`

```text
AddSharedKernelMessaging(IServiceCollection, IConfiguration, Action<MessagingOptions>? = null) → MessagingBusBuilder
AddSharedKernelMessaging(IServiceCollection, Action<MessagingOptions>? = null)                 → MessagingBusBuilder

MessagingBusBuilder  (sealed, IMessagingBuilder; nothing is wired until Build())
  Transport (exactly one; Build() throws without it, a second call throws)
    UseTransport(MessagingTransport)              — the extension point the satellites call
    .UseRabbitMq(connectionString | Action<RabbitMqBusOptions>)          [.RabbitMq]
    .UseAzureServiceBus(connectionString | Action<AzureServiceBusOptions>) [.AzureServiceBus]
  Integration
    ConfigureMassTransit(Action<IBusRegistrationConfigurator>) — additive, after consumers, before the transport
    .WithEntityFrameworkOutbox<TDbContext>(Action<OutboxOptions>?)       [.EfCore]
  Consumers
    AddConsumer<T>() · AddConsumer<T, TDefinition>() · AddBatchConsumer<T>(Action<BatchOptions>?)
    AddFaultConsumer<TMessage, TFaultConsumer>()
  Resilience
    WithRetry(Action<RetryOptions>?)            — global; default 3 attempts, 1 s / +1 s, max 30 s
    WithCircuitBreaker(Action<CircuitBreakerOptions>?) — global only; retry inner, breaker outer
    WithDeadLetterPolicy(Action<DeadLetterOptions>?)   — RabbitMQ error/dead-letter queue TTL
  Delivery
    WithDelayedDelivery()                       — broker-side deferral; registers IMessageScheduler
    WithSendEndpointRoute<T>(queueName)         — the only approved cross-service command routing
    WithVersionTranslator<TOld, TNew, TTranslator>()
    WithPayloadTransform(Action<PayloadTransformOptions>?)
  Consumer idempotency
    WithIdempotency() · WithIdempotency(Action<IdempotencyOptions>)
  Caller and headers
    WithHeaderPropagator<T>()
    WithAmbientCorrelationPropagation()         — AmbientCorrelationHeaderPropagator
    WithTenantContext()                         — TenantHeaderPropagator (tenant only)
    WithInboundRequestContext()                 — both directions, see "Caller identity across the bus"
  Build() → IServiceCollection
    Registers IMessageBus/IEventPublisher (scoped), the route map, ConventionSendEndpointResolver, the readiness
    probe "messaging" (always), IMessageScheduler (if delayed delivery), the version-translator advisory hosted
    service (if translators), and the MassTransit bus. Fails fast, with the fix in the message, on: no transport,
    an invalid inline ServiceName, WithIdempotency() without a store for IdempotencyPurpose.Message, invalid
    IdempotencyOptions, payload compression/encryption without the matching 01.Core service. Never calls
    Services.BuildServiceProvider() (P-130). With only IConfiguration-bound options, ServiceName is validated
    by ValidateOnStart instead.

Options (namespace SharedKernel.Messaging.MassTransit.Options)
  RetryOptions            Attempts 3 · InitialInterval 1 s · IntervalIncrement 1 s · MaxInterval 30 s · ImmediateAttempts 0
  CircuitBreakerOptions   TripThreshold 5 · ActiveThreshold 10 · ResetInterval 60 s · TrackingPeriod 60 s
  BatchOptions            MessageLimit 10 · TimeLimit 1 s · ConcurrencyLimit 1 (per endpoint)
  DeadLetterOptions       MessageTimeToLive null (unbounded) · QueueNameSuffix "_error" (accepted, NO effect — see below)
  PayloadTransformOptions EnableCompression false · EnableEncryption false
  RabbitMqBusOptions      Host · Username/Password ("guest" — override) · VirtualHost "/" · Prefetch 16 ·
                          RequestedHeartbeat 60 s · ConcurrentMessageLimit null            [.RabbitMq]
  AzureServiceBusOptions  exactly one of ConnectionString | FullyQualifiedNamespace (DefaultAzureCredential) ·
                          MaxConcurrentCalls 1 · TransportType AmqpTcp                    [.AzureServiceBus]
  OutboxOptions           BatchSize 100 · QueryDelay 1 s · DuplicateDetectionWindow 30 min [.EfCore]

ConsumerBase<TMessage>        abstract ConsumeAsync(TMessage, CancellationToken); ctor(ILogger). The sealed-by-doc
                              Consume() starts "Consumer.Consume", builds the log scope (CorrelationId, message type,
                              destination, every x-sk-* header), records metrics, logs and RETHROWS.
BatchConsumerBase<TMessage>   ConsumeAsync(IReadOnlyList<TMessage>, ct); register with AddBatchConsumer only.
ConsumerDefinitionBase<T>     sealed Configure → EndpointName, PrefetchCount, ConcurrentMessageLimit (per-consumer
                              wins over the bus default), NonRetryableExceptions (r.Ignore), then abstract
                              ConfigureConsumer. Never override IConsumerDefinition.Configure directly.
Propagators (public)          AmbientCorrelationHeaderPropagator, TenantHeaderPropagator, RequestContextHeaderPropagator
MessagingReadinessProbeNames  Bus = "messaging"
MessagingTransport / MessagingTransportSettings — the transport extension point (DeadLetterPolicy,
                              DelayedDelivery, ConfigureBus<T>(ctx, bus) applies the core's pipeline configuration).
```

**Internal types worth knowing:** `IdempotentConsumerBehavior<T>`, `InboundRequestContextFilter<T>`,
`MessageAwareRequestContext` + `HostRequestContextSource`, `InboundMessageContextAccessor`,
`RequestContextRegistrationAdvisoryHostedService`, `PublishContextPipe` (the one mapping of `PublishContext` onto a
MassTransit send/publish context, shared by both verbs), `MessagingExceptionClassifier`,
`ConventionSendEndpointResolver`, `MassTransitMessageScheduler`, `MassTransitMessageBusProbe`, `FaultConsumerAdapter`,
`VersionTranslatingConsumer`, `MessagingLogScope`, `MessagingDiagnostics`, `MessagingTagKeys`, the payload-transform
serializer trio.

---

## Implementation Rules

### Hard violations (never do these)

- Injecting MassTransit's `IBus`, `IPublishEndpoint`, `ISendEndpointProvider` or `IMessageScheduler` outside this
  domain's packages — use `IMessageBus`, `IEventPublisher`, `IMessageScheduler` (SK0706, architecture rules).
- Configuring MassTransit directly (`AddMassTransit(x => x.UsingRabbitMq(...))`) in a consuming service — all bus
  configuration flows through `AddSharedKernelMessaging()`; a MassTransit-specific need goes through
  `ConfigureMassTransit(...)`.
- Adding a broker client, the Azure SDK or EF Core to `SharedKernel.Messaging.MassTransit` — they belong in a
  satellite. Adding a satellite → satellite reference — satellites reference only the core.
- Referencing `06.Persistence`, any `SharedKernel.Caching.*`, MediatR, `SharedKernel.Application` or
  `SharedKernel.Application.Pipeline` from any messaging package.
- Publishing domain events through `IEventPublisher`/`IMessageBus`, or calling either from `03.Domain` types. Domain
  events go through `IDomainEventDispatcher`; a handler maps them to an `IIntegrationEvent` and publishes that.
- Deriving an event's wire name, routing key or `messaging.event_type` from `typeof(TEvent).Name` — always
  `IntegrationEventDescriptor.For<TEvent>().Name`.
- Swallowing an exception in `ConsumeAsync` — it acknowledges the message and loses the work. Declare genuinely
  non-retryable types in `ConsumerDefinitionBase.NonRetryableExceptions`.
- Hand-rolling deduplication in a consumer body — use `WithIdempotency()` with a registered store.
- Registering `IMessageBus`/`IEventPublisher` as singleton; starting or stopping `IBusControl` manually.
- Sending to a hard-coded address (`GetSendEndpoint(new Uri("queue:..."))`); a route in `WithSendEndpointRoute<T>()`
  that is not `{target-service-name}-{command-type}`.
- Registering `IFaultConsumer<T>` with `services.AddScoped`, a `BatchConsumerBase<T>` with `AddConsumer<T>()`, or a
  circuit breaker per consumer.
- `Task.Delay` in a consumer as deferred delivery — use `IMessageScheduler`.
- Implementing `IMessageHeaderPropagator` inside `SharedKernel.*` — except the three named built-ins
  (`AmbientCorrelationHeaderPropagator`, `TenantHeaderPropagator`, `RequestContextHeaderPropagator`), safe because
  their values come from `SharedKernel.Execution`'s caller contract. Do not add a fourth under that cover.
- Skipping propagators on any dispatch verb, or giving `IMessageBus` and `IEventPublisher` two different mappings of
  the transport context — both go through `PublishContextPipe`.
- Reading `Activity.Current`'s trace or span id as the correlation id. The correlation id is the caller's
  (`CorrelationIds.Current`), carried unchanged under `WellKnownHeaders.CorrelationId`.
- Trusting a permission carried by a message; treating inbound caller headers as authorization.
- Making payload-transform ordering configurable (always compress-then-encrypt / decrypt-then-decompress), adding a
  bespoke compression or crypto primitive, using the async `ISymmetricEncryptionService` in the serializer trio
  (MassTransit's serializer interfaces are synchronous), enabling encryption with only a KMS-backed key provider, or
  decrypting a message without its `PayloadTransformHeaders.MessageTypeAad` header under guessed associated data.
- Implementing the readiness probe over a second, independently constructed connection; shipping an `IHealthCheck`
  from this domain.
- Inventing an ordered-delivery substitute (sequencing buffer, sequence header) instead of the transport's native
  mechanism; hard-coding a dead-letter name or TTL instead of `WithDeadLetterPolicy()`.
- Implementing `IMessageVersionTranslator.Translate` with I/O or side effects.
- Static mutable state. Sole exception: `MessagingDiagnostics.ActivitySource`, `.Meter` and its instrument fields.
  `13.ServiceDefaults` never constructs these; `WithMessagingTelemetry()` only registers their names.
- A production log statement not written as `[LoggerMessage]` with an explicit `EventId` in 7000–7999; a
  hand-built `BeginScope` dictionary in the consumer bases instead of `MessagingLogScope.Create(correlationId)`.
- Committing transport credentials to `appsettings.json`; bumping MassTransit to 9.x.

### CloudEvents compliance rule

`MassTransitEventPublisher` wraps every event with `EventEnvelope.Wrap(evt, source:, subject:, tenantId:,
correlationId:, causationId:)` — the only construction path; `EventEnvelope<TEvent>` has no public constructor or
setter.

| Envelope member | Source |
| --- | --- |
| `specversion` | `"1.0"` |
| `id` | the event's `EventId` (deduplicate on `id` + `source`) |
| `source` | `MessagingOptions.ServiceName` |
| `type` / `dataversion` | the event's `[IntegrationEvent]` name and `Version` (default 1) |
| `time` | the event's `OccurredOn` |
| `subject` | `PublishContext.Subject`, else omitted |
| `datacontenttype` | `"application/json"` |
| `tenantid` (`Guid?`, wire contract) | `PublishContext.TenantId` — set explicitly or by a propagator — else omitted |
| `correlationid` | `PublishContext.CorrelationId` ("D") → a propagated `X-Correlation-Id` header → the ambient caller's correlation id (`CorrelationIds.Current(RequestContextScope.Current)`) → `CorrelationIds.New()`. Never an Activity id |
| `causationid` | `PublishContext.CausationId`, else omitted |
| `data` | the event, serialized as its runtime type |

`Wrap` throws `ArgumentException` for a mismatched runtime type or unset `EventId`/`OccurredOn`, and
`InvalidOperationException` without a valid `[IntegrationEvent]`; the publisher maps both to `messaging.invalid_message`
/ `messaging.contract_violation` before any telemetry or transport work. The tenant and correlation id are also
written as transport headers, so a consume filter can read them without deserializing the body.

### Naming, retry and outbox

- **Queues:** `{service-name}-{consumer}` in kebab-case; `KebabCaseEndpointNameFormatter` drops a `Consumer` suffix
  (`OrderPlacedConsumer` → `order-service-order-placed`) but not `Event`. Override via a consumer definition.
- **Retry:** `WithRetry()` is global. Business/validation failures are excluded through
  `NonRetryableExceptions`, never swallowed.
- **Outbox:** with `WithEntityFrameworkOutbox<TDbContext>()`, messages published during a unit of work are written
  to the outbox tables in the same transaction and delivered by MassTransit's background worker after commit.
  Delivery is at-least-once, so consumers must be idempotent. The `DbContext` maps the outbox entities
  (`AddInboxStateEntity()`, `AddOutboxMessageEntity()`, `AddOutboxStateEntity()`) and the service owns the
  migration; the tables must exist before the bus starts.

### Dead-letter and poison messages

A message dead-letters **immediately** when its exception type is in `NonRetryableExceptions`, or **after** the
retry budget is exhausted. An open circuit breaker fails fast while open; it does not dead-letter by itself.

- **RabbitMQ** (`WithDeadLetterPolicy()`): `MessageTimeToLive` becomes `x-message-ttl` on MassTransit's derived
  `_error` and `_skipped` queues (`SendTopology.ConfigureErrorSettings`/`ConfigureDeadLetterSettings`).
  `QueueNameSuffix` is accepted but has **no effect**: MassTransit exposes no hook to rename those queues (the
  suffixes are literals in `MassTransit.RabbitMqTransport`). `BindDeadLetterQueue` is not a substitute — it wires
  the broker's native NACK/TTL dead-lettering, which a consumer exception never reaches. Omitting the call keeps
  MassTransit's default.
- **Azure Service Bus:** dead-lettering is native (`MaxDeliveryCount` on the entity, set in infrastructure).
  `WithDeadLetterPolicy()` under ASB logs an advisory warning at startup (EventId 7010) and does nothing.

### Ordered delivery via partition key

`PublishContext.WithPartitionKey(key)` maps to the transport's native mechanism through
`MessagingTransport.ApplyPartitionKey`: the base (and RabbitMQ) calls `TrySetRoutingKey` for routing-key affinity;
Azure Service Bus additionally calls `SetSessionId`, which needs sessions enabled on the receiving entity (an
infrastructure responsibility). A `null` key leaves the context untouched. Ordering holds only per key and only
with a single active consumer instance on the endpoint.

```csharp
await events.PublishAsync(orderPlaced, ctx => ctx.WithPartitionKey(orderPlaced.OrderId.ToString()), ct);
```

### Opt-in payload compression and encryption

`WithPayloadTransform(o => { o.EnableCompression = true; o.EnableEncryption = true; })` decorates MassTransit's STJ
serializer with `PayloadTransformSerializerFactory`/`PayloadTransformMessageSerializer`/`PayloadTransformMessageDeserializer`,
built on `01.Core`'s `IPayloadCompressor` and `ISynchronousSymmetricEncryptionService`.

- Order is fixed: compress-then-encrypt on publish, decrypt-then-decompress on consume. Both flags default `false`;
  enabling either changes the wire format, so publisher and consumer must agree.
- `Build()` requires the matching `01.Core` registration (`AddSharedKernelCompression(...)`,
  `AddSharedKernelCryptography(configuration).AddSynchronousSymmetricEncryption()`); a missing
  `ISynchronousEncryptionKeyProvider` surfaces at bus start with the same guidance. A KMS-only key provider is not
  supported: MassTransit's `IMessageSerializer`/`IMessageDeserializer` have no async members.
- The body is `EncryptedPayload.ToBytes()`. Associated data is the message's CLR type name, sent in the plaintext
  transport header `PayloadTransformHeaders.MessageTypeAad` (`x-payload-transform-message-type`, domain-local,
  never encrypted or stored in the payload). A missing header, a body that does not parse, or any
  decrypt/decompress/deserialize failure is a `PayloadTransformMismatchException` — loud, never a fallback.

### Readiness and the window before ready

`Build()` always registers `MassTransitMessageBusProbe` as the `IReadinessProbe` named `messaging`
(`MessagingReadinessProbeNames.Bus`). It runs MassTransit's own `BusHealthCheck` over the registered `IBusInstance`,
resolved inside `ProbeAsync` so constructing the probe is cheap. The host maps every probe with
`services.AddHealthChecks().AddSharedKernelReadiness()` (ServiceDefaults base); this domain ships no `IHealthCheck`.

> **The bus starts in the background.** MassTransit's hosted service returns before the broker connection exists
> and before any queue is declared. A message published in that window goes to an exchange with nothing bound and
> is **dropped by the broker — successfully**. Gate traffic on readiness, in tests as well as in production
> (`samples/ShippingApi` waits for it). Never substitute a sleep.

### Caller identity across the bus

A consumer has no HTTP request. `WithInboundRequestContext()` carries the caller both ways:

- **Publish** — `RequestContextHeaderPropagator` writes the caller through `SharedKernel.Execution`'s
  `RequestContextPropagation`: correlation id, tenant and actor under `WellKnownHeaders` (`X-Correlation-Id`,
  `X-Tenant-Id`, `x-sk-actor-id`, `x-sk-actor-kind`, `x-sk-client-id`). The caller is the ambient
  `IRequestContextAccessor.Current`, else the scope's `IRequestContext`. It writes nothing it does not know.
- **Consume** — `InboundRequestContextFilter<T>` rebuilds a `PropagatedRequestContext` (`SharedKernel.Execution`),
  stores it on the delivery scope (`IInboundMessageContextAccessor`) and runs the consumer inside a
  `RequestContextScope`, so outbound REST/gRPC/bus calls made by the consumer forward the same tenant and
  correlation id. `MessageAwareRequestContext` makes `IRequestContext` answer from the message inside a consume and
  from the service's own registration everywhere else — one handler serves both paths. It runs ahead of every other
  consume filter, including idempotency. A malformed tenant header yields no tenant (fails closed in persistence),
  never a retry loop; a missing correlation id is replaced by a new one.

Rules:

1. **Register it after the service's own `IRequestContext`** (`AddSharedKernelRequestContext()`,
   `SharedKernel.ServiceDefaults.Security`). The container resolves the last registration; the wrong order logs
   EventId 7011 at startup.
2. **Attribution, not authorization.** `PropagatedRequestContext.HasPermissionAsync` always answers `false`.
   Headers are writable by anyone who can reach the broker. A consumer that must authorize re-resolves the caller
   from the identity provider.
3. **Trust the values as far as you trust broker access.** Where that matters, encrypt the payload and derive
   tenancy from signed content.

Published from inside a consumer, the original caller and correlation id are carried onward.
`WithTenantContext()` (tenant only) and `WithAmbientCorrelationPropagation()` (correlation only) are the narrower
alternatives for a service that does not want the full round trip.

### Consumer idempotency

`WithIdempotency()` adds `IdempotentConsumerBehavior<T>` as a global consume filter (MassTransit runs it once per
consumer, in that consumer's message pipe, after `IdempotentConsumerIdentityFilter<T>` names the consumer) over the
`[FromKeyedServices(IdempotencyPurpose.Message)] IIdempotencyStore` (`SharedKernel.Idempotency.Abstractions`).
Register a store first — `AddRedisIdempotency(p => p.ForMessages())`, `AddEfCoreIdempotency(..., p => p.ForMessages())`
or `AddIdempotencyStore<T>(IdempotencyPurpose.Message)` — or `Build()` throws.

1. No `MessageId` → pass through.
2. `TryBeginAsync(Message, key, fingerprint "message", LeaseDuration)` with key
   `{MessageId:D}:{sha256-hex("{receive-endpoint path}|{consumer full type name}")}` (101 chars), so each consumer
   deduplicates its own deliveries; the store scopes the key by the ambient tenant (`IdempotencyTenantScope`), which
   `WithInboundRequestContext()` sets first.
3. `Completed` → return without consuming (acknowledges). `InProgress` → throw `ConcurrentMessageDeliveryException`
   (expected, keeps the message unacknowledged — do not alert on it). `FingerprintMismatch` → a store defect, throws.
4. `Started` → consume; on exception `ReleaseAsync` then rethrow; on success `CompleteAsync(..., ExpiryWindow)`.
   Both with `CancellationToken.None`.

### Configuration entry point

`AddSharedKernelMessaging(IConfiguration)` binds `MessagingOptions` from `MessagingOptions.SectionName` through
`AddValidatedOptions` (never `services.Configure<T>(section)`). `ServiceName` prefixes every queue and exchange and is
the CloudEvents `source`, so it is validated as a lowercase slug — stricter than either broker, so switching
transports never renames every queue. `SharedKernel.Configuration` is referenced by the MassTransit core, not by
`.Abstractions`.

### Logging and EventId allocation

`[LoggerMessage]` only, explicit `EventId`, PascalCase placeholders, no correlation/trace/tenant placeholders.
Range 7000–7999 (`LoggingEventIdRanges.Messaging`); one sub-block, 7000–7099, shared by the MassTransit core and its
satellites.

| EventId | Name | Level | Declaring type |
| --- | --- | --- | --- |
| 7001 | `ConsumerConsumeError` | Error | `ConsumerBase<T>` |
| 7002 | `BatchConsumeEntry` | Information | `BatchConsumerBase<T>` |
| 7003 | `BatchConsumeError` | Error | `BatchConsumerBase<T>` |
| 7004 | `FaultConsumerHandling` | Error | `FaultConsumerAdapter<,>` |
| 7005 | `FaultConsumerError` | Error | `FaultConsumerAdapter<,>` |
| 7006, 7007 | retired (routing slips, P-560) — never reuse | — | — |
| 7008 | `VersionTranslating` | Debug | `VersionTranslatingConsumer<,>` |
| 7009 | `VersionTranslatorNoConsumer` | Warning | `TranslatorRegistrationValidator` |
| 7010 | `DeadLetterPolicyIgnoredUnderAzureServiceBus` | Warning | `DeadLetterPolicyAdvisoryHostedService` (`.AzureServiceBus`) |
| 7011 | `RequestContextOverridden` | Warning | `RequestContextRegistrationAdvisoryHostedService` |
| 7012 | `RequestContextNotResolvable` | Debug | `RequestContextRegistrationAdvisoryHostedService` |

`7013–7099` are free. The `[LoggerMessage]` generator only discovers an `ILogger` **field**; the consumer bases
expose a `Logger` **property**, so their log methods are `private static partial void LogXxx(ILogger logger, ...)`
called with `Logger`. Prefer a field in new types.

### Diagnostics

`ActivitySource` and `Meter`, both `SharedKernel.Messaging`, wired by `13.ServiceDefaults`' `WithMessagingTelemetry()`.

| Verb | Activity | Instruments |
| --- | --- | --- |
| `ConsumerBase.Consume` | `Consumer.Consume` | `messaging.retry.count` (when `GetRetryAttempt() > 0`), `messaging.consume.count` (success), `messaging.consume.duration` (always) |
| `IEventPublisher.PublishAsync` | `EventPublisher.Publish` (tag `messaging.event_type`) | `messaging.publish.count` after success |
| `IMessageBus.PublishAsync` | `MessageBus.Publish` | `messaging.publish.count` after success |
| `IMessageBus.SendAsync` | `MessageBus.Send` | `messaging.send.count` after success |
| `FaultConsumerAdapter.Consume` | — | `messaging.fault.count` (always) |

Tags come from `MessagingTagKeys`. MassTransit's `IRetryObserverConnector` is unreachable from the bus
configurators, so retries are observed per invocation through `ConsumeContext.GetRetryAttempt()`.

### MassTransit API notes

- `SystemTextJsonMessageSerializerFactory` is parameterless on 8.5 (the only v9→v8 change, P-560).
- `MassTransit.PublishContext` clashes with ours: alias `using MessagingPublishContext = SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext;` in production and test files that see both.
- `MassTransit.IMessageScheduler` clashes with ours: alias `MtScheduler`, and fully qualify the abstraction in registrations. `SchedulePublish` returns `ScheduledMessage<T>` (`TokenId`), takes `DateTime` (use `.UtcDateTime`); cancel with the non-generic `CancelScheduledPublish(Type, Guid, ct)` from a token→type map. MassTransit's scheduler is scoped — resolve it from a scope in tests.
- `KebabCaseEndpointNameFormatter.SanitizeName` is an instance method (`KebabCaseEndpointNameFormatter.Instance.SanitizeName(...)`).
- A global open-generic consume filter: `busCfg.UseConsumeFilter(typeof(MyFilter<>), ctx)` with `services.AddScoped(typeof(MyFilter<>))`; MassTransit closes it per message type.
- Iterate headers with `context.Headers.GetAll()`; `Headers` is not an `IDictionary`. For a test `Headers`, construct `MassTransit.Serialization.DictionarySendHeaders` (it implements both `SendHeaders` and `Headers`).
- Azure Service Bus: `TransportType` lives on the host configurator (`cfg.Host(uri, h => h.TransportType = ...)`); managed identity is `new Uri($"sb://{ns}")` with `h.TokenCredential = new DefaultAzureCredential()`. `IServiceBusEndpointConfigurator.MaxConcurrentCalls` is obsolete — set the shared `IBusFactoryConfigurator.ConcurrentMessageLimit`, which is write-only at bus level (assert it with NSubstitute's `Received(1).ConcurrentMessageLimit = n`).
- A custom `ISerializerFactory` needs `busCfg.ClearSerialization()`, then `AddSerializer(factory, isSerializer: true)` **and** `AddDeserializer(factory, isDefault: true)`; otherwise the old deserializer keeps running on receive, or configuration fails with "more than one deserializer".
- The receive pipeline calls `IMessageDeserializer.Deserialize(ReceiveContext)`; implement it as `new BodyConsumeContext(rc, Deserialize(rc.Body, rc.TransportHeaders, rc.InputAddress))`.
- A header a serializer decorator sets **after** the inner `GetMessageBody` reaches the deserializer's raw `Headers` parameter (the transport headers) but not `ConsumeContext.Headers`, which is the envelope's earlier snapshot.
- Substituting `SendContext<T>` for serializer tests: stub `SupportedMessageTypes` with `MessageUrn.ForTypeString<T>()` (a raw type name makes `TryGetMessage<T>` return `false` silently), and use a `public` message type.
- `BusHealthCheck(IBusInstance)` needs `HealthCheckContext.Registration` set, or it throws `NullReferenceException`. Resolving `IBusInstance` from a plain `AddMassTransit` container builds the bus and hits MassTransit's license gate; test the probe under `AddMassTransitTestHarness()`, and assert plain registrations by `ServiceDescriptor`.
- There is no stable cross-type deserializer hook, so version translation is a `VersionTranslatingConsumer<TOld,TNew>` that republishes `TNew`.
- Advisory checks that need an `ILogger` run in a startup `IHostedService`, never inside `Build()`.
- `IRequestClient.GetResponse` only takes a `RequestPipeConfiguratorCallback`; reach the `SendContext` with `cfg.UseExecute(...)`.
- To answer an API-shape question, compile a throwaway usage (the compiler names the declaring type and accessor) or reflect over the cached 8.5.10 assemblies.

---

## DI Registration (expected shape)

```csharp
// The service's own IRequestContext first: WithInboundRequestContext() shadows whatever is registered.
builder.Services.AddSharedKernelRequestContext();                     // SharedKernel.ServiceDefaults.Security

// The consumer-idempotency store (18.Idempotency), keyed by purpose.
builder.Services.AddRedisConnection(builder.Configuration);           // SharedKernel.Caching.Redis.Core
builder.Services.AddRedisIdempotency(p => p.ForMessages());           // SharedKernel.Idempotency.Redis

builder.Services
    .AddSharedKernelMessaging(builder.Configuration)                  // SharedKernel:Messaging
    .UseRabbitMq(builder.Configuration.GetConnectionString("rabbitmq")!)   // SharedKernel.Messaging.MassTransit.RabbitMq
    .WithRetry()
    .WithDelayedDelivery()
    .WithIdempotency()
    .WithInboundRequestContext()
    .AddConsumer<OrderPlacedConsumer>()
    .AddFaultConsumer<ChargeCard, ChargeCardFaultConsumer>()
    .WithSendEndpointRoute<ChargeCard>("billing-api-charge-card")
    .Build();

builder.Services.AddHealthChecks().AddSharedKernelReadiness();        // maps the "messaging" probe
builder.WithMessagingTelemetry();

var app = builder.Build();
app.UseSharedKernelRequestContext();                                  // first in the pipeline
```

```csharp
// Azure Service Bus with managed identity, plus the EF Core outbox.
services
    .AddSharedKernelMessaging(configuration)
    .UseAzureServiceBus(o => o.FullyQualifiedNamespace = "my-namespace.servicebus.windows.net")
    .WithEntityFrameworkOutbox<OrdersDbContext>(o => o.QueryDelay = TimeSpan.FromSeconds(2))  // .EfCore
    .WithRetry()
    .AddConsumer<OrderPlacedConsumer, OrderPlacedConsumerDefinition>()
    .Build();

// A consumer (in the consuming service).
public sealed class OrderPlacedConsumer(IOrderRepository orders, ILogger<OrderPlacedConsumer> log)
    : ConsumerBase<EventEnvelope<OrderPlaced>>(log)
{
    protected override Task ConsumeAsync(EventEnvelope<OrderPlaced> envelope, CancellationToken ct) =>
        orders.MarkPlacedAsync(envelope.Data.OrderId, ct);
}

// A definition: non-retryable exceptions, endpoint name, concurrency.
public sealed class OrderPlacedConsumerDefinition : ConsumerDefinitionBase<OrderPlacedConsumer>
{
    protected override IReadOnlyList<Type> NonRetryableExceptions => [typeof(ValidationException)];
    protected override int? ConcurrentMessageLimit => 4;
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpoint, IConsumerConfigurator<OrderPlacedConsumer> consumer,
        IBusRegistrationContext context) { }
}

// A service-specific propagator (the consuming service's own — not in SharedKernel).
public sealed class CheckoutVariantPropagator(CheckoutExperiment experiment) : IMessageHeaderPropagator
{
    public void Propagate(PublishContext context) => context.WithHeader("x-sk-checkout-variant", experiment.Variant);
}

// Schema evolution during a rolling deploy.
services.AddSharedKernelMessaging(configuration)
    .UseRabbitMq(connectionString)
    .WithVersionTranslator<OrderPlacedV1, OrderPlacedV2, OrderPlacedV1ToV2>()
    .AddConsumer<OrderPlacedConsumer>()    // consumes OrderPlacedV2
    .Build();
```

---

## AOT Compatibility

AOT is preferred where it costs nothing; the contracts package is interfaces, sealed records and POCOs. The
MassTransit core adds no `Activator.CreateInstance`, `Assembly.Load` or `MakeGenericMethod` in a hot path; consumers,
filters and definitions are closed generics registered at startup. `[LoggerMessage]`, `ActivitySource` and `Meter` are
AOT-safe. `Enum.TryParse<ActorKind>` uses the generic overload. MassTransit, its transports and EF Core's outbox are
third-party and not verified for NativeAOT; `EventEnvelope<TEvent>` uses reflection-based STJ, so a NativeAOT service
must supply its own `JsonSerializerContext` for every message and envelope type. Re-check on each MassTransit upgrade.

---

## Test Rules

- Tests live in `{Package}.Tests` inside each package folder. The core's tests are in the Unit lane; the RabbitMQ
  satellite's `IntegrationTests/` (Testcontainers, `[Trait("Category","Integration")]`) are in the Integration lane.
- Application-level tests use `16.Testing`'s `SharedKernel.Messaging.Testing` (`InMemoryMessageBus`,
  `InMemoryEventPublisher`, `AddInMemoryMessageBus()`, `AddInMemoryEventPublisher()`, namespace
  `SharedKernel.Testing.Messaging`) rather than `Substitute.For<IMessageBus>()`. Consumer idempotency without a real
  store uses `SharedKernel.Idempotency.Testing`'s `AddFakeIdempotencyStore(IdempotencyPurpose.Message)`. Harness setup
  for this repo's own tests is `SharedKernel.Testing.Internal`'s `TestHarnessFactory` (not packable).
- Consumer and pipeline tests run on MassTransit's in-memory `AddMassTransitTestHarness()`; wait with
  `harness.InactivityTask` and assert on `harness.Consumed`/`Published`. Test through the real builder
  (`AddSharedKernelMessaging(...).With...()`), not by registering internals by hand.
- **Round trips, not halves:** `InboundRequestContextTests` and `AmbientPropagationTests` prove publish and consume
  agree on header names; `DispatchContextParityTests` proves `IMessageBus` and `IEventPublisher` write the same
  transport context — a new context field goes in `PublishContextPipe` and gets a parity test.
- Propagator tests cover every verb they apply to (`PublishAsync`, `SendAsync`, `IEventPublisher.PublishAsync`).
- Idempotency tests cover `Completed`, `InProgress` (exception, message not acknowledged), release-on-throw and a
  null `MessageId`, with a `public` message type.
- Payload transform: round-trip with both flags and each alone; mismatch in both directions; AAD tests via one
  `DictionarySendHeaders` instance passed to serializer and deserializer.
- Ordered delivery (Testcontainers): interleave at least two keys against one consumer and assert order per key.
- Dead-letter (Testcontainers): assert the message reaches MassTransit's real `_error` queue, and the TTL argument.
- Satellite builder tests assert what the transport configures (`ConcurrencyLimitConfigurationTests`,
  `DeadLetterPolicyConfigurationTests`, `PartitionKeySessionIdTests`); ASB has no live-broker tests.
- Outbox (`.EfCore`): SQLite with a kept-open `SqliteConnection("Data Source=:memory:")`; outbox rows are written
  during `SaveChangesAsync`.
- Diagnostics: an `ActivityListener`/`MeterListener` filtered on `SharedKernel.Messaging`, registered before the
  action. Listeners are process-wide — filter by a test-unique tag value, not the operation name alone.
- Logs are asserted by `EventId` through `16.Testing`'s logger double, never by message text.
- Readiness: healthy against a started harness bus, unhealthy with a description otherwise.
- Never use the `file` modifier on consumer, message or `DbContext` types (MassTransit splits mangled names on `<`).
  NSubstitute cannot proxy a MassTransit generic interface closed over an `internal` type — make it `public`.
  Dispose MassTransit service providers with `await using`. Call `services.AddLogging()` before resolving
  `IHostedService`s against a real broker.
- **The samples are part of the test surface.** `samples/ShippingApi` runs the packed packages against a real
  RabbitMQ (`masstransit/rabbitmq`, which ships the delayed-exchange plugin). It found three defects no unit test had;
  run it when a change touches dispatch. `07.Messaging/consumer-verify` proves the injectable surface resolves from one
  registration chain.

---

## Open Items

None. (Consumer idempotency keys now include the receive endpoint and the consumer — see "Consumer idempotency".)

---

## Changelog

> History of this domain lives in [`state-map.md`](state-map.md) (phase tables and its Changelog) and in git.

- [2026-09-26] WO-086 (P-564–P-575): tiers replace numbered layering; the 07 → `Application.Abstractions` grant and
  `MessagingLayeringRules` are gone (`IRequestContext` is `SharedKernel.Execution`); `ITenantContextAccessor`,
  `MessageRequestContext`, `MessageContextHeaders`, `IMessageBusProbe`/`MessageBusHealth` and the messaging
  `IIdempotencyStore` removed; transports and the outbox split into the `.RabbitMq`, `.AzureServiceBus` and `.EfCore`
  satellites; the bus registers an `IReadinessProbe` named `messaging`. This brain was rewritten to the final state.
