# 07.Messaging — Message Bus and Event Publishing

## What This Domain Is

The messaging capability domain. Provides transport-agnostic abstractions for command/event publishing and an opinionated MassTransit wiring layer for RabbitMQ and Azure Service Bus. The domain owns the transactional outbox pattern via MassTransit's EF Core outbox integration — no outbox types exist in `06.Persistence`.

Philosophy: **Abstraction-first. Transport-swappable. Outbox-native. CloudEvents-compliant.**

> **Outbox ownership:** The outbox pattern is owned entirely by `07.Messaging` via `MassTransit.EntityFrameworkCoreIntegration`. `OutboxMessage`, `IOutboxWriter`, and any outbox interceptor types must never be defined in `06.Persistence`. The consuming service's `DbContext` is passed as a generic type parameter to `WithEntityFrameworkOutbox<TDbContext>()` — no compile-time dependency on `SharedKernel.Persistence.EfCore` is introduced by this package. The consuming service bridges the gap at its own composition root.

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Messaging.Abstractions` | `IMessageBus`, `IEventPublisher`, `PublishContext`, `IMessagingBuilder`, `MessagingOptions` — pure interface library; no transport NuGet dependencies; gained a real, working `README.md` (embedded in the packed `.nupkg` via `PackageReadmeFile`) with a copy-paste-ready `IIdempotencyStore` reference recipe, shipped P-349/WO-054 | `Microsoft.Extensions.DependencyInjection.Abstractions`, `SharedKernel.Contracts` (for the `IIntegrationEvent` constraint on `IEventPublisher`) |
| `SharedKernel.Messaging.MassTransit` | Concrete MassTransit bus wiring: `MassTransitMessageBus` (registered `IMessageHeaderPropagator`s now applied identically across `PublishAsync`/`SendAsync`/`RequestAsync`, shipped P-341/WO-054), `MassTransitEventPublisher` (envelope construction now exclusively via `EventEnvelope.Wrap<TEvent>()` with tenant identity flowing through `PublishContext.TenantId`, shipped P-340/WO-054), `ConsumerBase<TMessage>`, `MessagingBusBuilder` (`AzureServiceBusOptions.MaxConcurrentCalls` and `RabbitMqBusOptions.ConcurrentMessageLimit` now actually wired into the built bus, plus a `ConsumerDefinitionBase<TConsumer>.ConcurrentMessageLimit` per-consumer override, shipped P-342/WO-054; `DeadLetterOptions`/`WithDeadLetterPolicy()` RabbitMQ dead-letter/poison-message TTL wiring plus the ASB advisory-warning path, shipped P-343/WO-054; `PartitionKeySendContextExtensions.ApplyPartitionKey` mapping `PublishContext.PartitionKey` onto RabbitMQ routing-key affinity and Azure Service Bus session identity in `MassTransitMessageBus.PublishAsync`/`SendAsync` and `MassTransitEventPublisher.PublishEnvelopeAsync`, shipped P-344/WO-054; `AmbientCorrelationHeaderPropagator`/`TenantHeaderPropagator` — the two named, by-name exceptions to the "never implement `IMessageHeaderPropagator` inside `SharedKernel.*`" rule — plus `ITenantContextAccessor` (`SharedKernel.Messaging.Abstractions`) and `MessagingBusBuilder.WithAmbientCorrelationPropagation()`/`.WithTenantContext<TAccessor>()`, shipped P-345/WO-054; `PayloadTransformOptions`/`WithPayloadTransform()` wiring an opt-in compress-then-encrypt (publish) / decrypt-then-decompress (consume) `ISerializerFactory`/`IMessageSerializer`/`IMessageDeserializer` decorator trio (`Serialization/`) built entirely on `01.Core`'s `IPayloadCompressor`/`ISymmetricEncryptionService`, plus `PayloadTransformMismatchException` for the loud-failure mismatched-configuration path, shipped P-346/WO-054 — its encryption call sites now migrated onto `01.Core`'s WO-081 associated-data/synchronous-provider-gate contracts (type-derived AAD carried publish→consume via a new `PayloadTransformHeaders.MessageTypeAad` transport header, `Build()`-time best-effort `ISynchronousEncryptionKeyProvider` check), shipped P-499/WO-081; `MassTransitMessageBusProbe` — `IMessageBusProbe` implementation querying MassTransit's own `MassTransit.Monitoring.BusHealthCheck` against the real, DI-registered `IBusInstance`, registered as a singleton unconditionally by `Build()`, shipped P-347/WO-054; `MessagingDiagnostics.Meter` — a companion `Meter` alongside the existing `ActivitySource`, with `messaging.publish.count`/`messaging.consume.count`/`messaging.consume.duration`/`messaging.retry.count`/`messaging.fault.count` instruments, plus `Activity` instrumentation on `MassTransitMessageBus.SendAsync`/`.RequestAsync`/`.ExecuteRoutingSlipAsync` (the three dispatch verbs that previously produced no activity at all), shipped P-348/WO-054), transport adapters (RabbitMQ, ASB), retry policy, EF Core outbox integration — gained a real, working `README.md` (embedded in the packed `.nupkg` via `PackageReadmeFile`) with an ambient-correlation/tenant-context propagation reference recipe, shipped P-349/WO-054 | `SharedKernel.Messaging.Abstractions`, `SharedKernel.Contracts` (for `EventEnvelope<TEvent>` in publisher implementation), `MassTransit` 9.1.2, `MassTransit.RabbitMQ` 9.1.2, `MassTransit.Azure.ServiceBus.Core` 9.1.2, `MassTransit.EntityFrameworkCore` 9.1.2 (note: NOT `MassTransit.EntityFrameworkCoreIntegration`), `Microsoft.EntityFrameworkCore` 10.x (outbox `TDbContext` constraint only), `Microsoft.Extensions.Logging.Abstractions` 10.x, `SharedKernel.Compression`/`SharedKernel.Cryptography` (`01.Core`, for opt-in payload transform, shipped P-346/WO-054), `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` 10.0.5 (types only — `HealthCheckContext`/`HealthCheckResult`/`HealthStatus`/`HealthCheckRegistration` — never the full `Microsoft.Extensions.Diagnostics.HealthChecks` package's `AddHealthChecks()`/`HealthCheckService` machinery, which stays `13.ServiceDefaults`'s concern; used by `MassTransitMessageBusProbe`, shipped P-347/WO-054) |

All packages target `net10.0`. `ImplicitUsings` enabled. `Nullable` enabled. Test sub-folders live inside each project folder (never in a top-level `tests/`).

**Microservices must reference `SharedKernel.Messaging.Abstractions` for DI contracts. They reference `SharedKernel.Messaging.MassTransit` only at the composition root (startup project).**

---

## Technology Stack

| Concern | Technology | Version |
| --- | --- | --- |
| Message bus framework | `MassTransit` | 9.1.2 |
| RabbitMQ transport | `MassTransit.RabbitMQ` | 9.1.2 |
| Azure Service Bus transport | `MassTransit.Azure.ServiceBus.Core` | 9.1.2 |
| Transactional outbox | `MassTransit.EntityFrameworkCore` (NOT `MassTransit.EntityFrameworkCoreIntegration`) | 9.1.2 |
| CloudEvents envelope | `EventEnvelope<TEvent>` from `SharedKernel.Contracts` (`04.Contracts`) | — |
| Serialization | System.Text.Json with MassTransit STJ serializer | BCL `net10.0` |
| DI abstractions | `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.x |
| Logging | `Microsoft.Extensions.Logging.Abstractions` | 10.x |
| Bus readiness probe types | `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` (types only; no `AddHealthChecks()`/`HealthCheckService` wiring) | 10.0.5 |

---

## Interface Contracts

### `SharedKernel.Messaging.Abstractions` — public surface

> Zero transport NuGet dependencies. References only `Microsoft.Extensions.DependencyInjection.Abstractions` and `SharedKernel.Contracts` (which itself references only `SharedKernel.Primitives`).

#### Message bus interface (`MessageBus/`)

```text
IMessageBus
    .PublishAsync<T>(T message, CancellationToken ct)                              → Task
        Publishes a message to all consumers registered for T.
        Fan-out semantics — equivalent to topic/exchange publish.
        Use for integration events and broadcast notifications.

    .PublishAsync<T>(T message, Action<PublishContext> configure, CancellationToken ct) → Task
        Overload for explicit CorrelationId, CausationId, or custom transport headers.

    .SendAsync<T>(T command, CancellationToken ct)                                 → Task
        Sends a command to the registered endpoint for T.
        Point-to-point semantics — equivalent to queue send.
        Endpoint address is resolved by convention from the transport provider.
        Use for commands and work items with exactly one handler.
        Registered IMessageHeaderPropagators are applied before dispatch, identically to
        PublishAsync (P-341/WO-054 — this was previously a silent asymmetry; fixed).

    .RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct)     → Task<TResponse>
        Synchronous request/response pattern over the message bus.
        Uses a private temporary reply queue under the hood.
        Registered IMessageHeaderPropagators are applied before dispatch, identically to
        PublishAsync (P-341/WO-054 — this was previously a silent asymmetry; fixed).
        CAUTION: Adds latency and tight temporal coupling — prefer event-driven fire-and-forget.
                 Always pass a timeout-bound CancellationToken; never pass CancellationToken.None.

    .ExecuteRoutingSlipAsync(object routingSlip, CancellationToken ct)             → Task
        Dispatches a routing slip produced by IRoutingSlipBuilder.Build() to MassTransit Courier.
        The routingSlip argument must be the opaque object returned by IRoutingSlipBuilder.Build().
        Do not construct MassTransit RoutingSlipBuilder directly in application code — always use
        IRoutingSlipBuilder and pass the result here.
        CAUTION: Throws ArgumentException if the object is not a valid MassTransit RoutingSlip.

    NOTE: IMessageBus is registered as a scoped service. Never inject as singleton.
          Scoped lifetime matches MassTransit's IPublishEndpoint/ISendEndpointProvider scoping model.
```

#### Integration event publisher (`EventPublisher/`)

```text
IEventPublisher
    .PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)            → Task
        where TEvent : class, IIntegrationEvent   (04.Contracts)
        Publishes a CloudEvents-compliant integration event.
        The MassTransit implementation wraps TEvent in EventEnvelope<TEvent> (04.Contracts)
        before sending to the transport via EventEnvelope.Wrap — the only construction path;
        EventEnvelope<TEvent> has no public constructor or setter, so any other construction no
        longer compiles.
        Type and DataVersion come from the event's [IntegrationEvent] attribute; Id and Time from
        the event's EventId/OccurredOn. Source is sourced from MessagingOptions.ServiceName.
        CorrelationId: PublishContext.CorrelationId, else Activity.Current?.TraceId, else a new Guid.
        CausationId and Subject: PublishContext only; otherwise omitted.
        TenantId is sourced from PublishContext.TenantId when explicitly set (or from the ambient
        TenantHeaderPropagator when MessagingBusBuilder.WithTenantContext<T>() is registered,
        P-345/WO-054); otherwise omitted (null), exactly like an unset CorrelationId/CausationId.
        Throws ArgumentException when TEvent is not the event's runtime type or EventId/OccurredOn
        is unset; InvalidOperationException when TEvent has no valid [IntegrationEvent] attribute.

    .PublishAsync<TEvent>(TEvent integrationEvent, Action<PublishContext> configure, CancellationToken ct) → Task
        where TEvent : class, IIntegrationEvent
        Overload for explicit envelope metadata override (CorrelationId, CausationId, TenantId,
        Subject, partition key, custom headers).

    NOTE: IEventPublisher is for integration events only — events that cross service boundaries.
          In-process domain events are dispatched by IDomainEventDispatcher (03.Domain), not IEventPublisher.
          The correct flow: domain event → IDomainEventDispatcher → application event handler → map to an
          IIntegrationEvent → IEventPublisher.
          Never call IEventPublisher from domain entities, value objects, or aggregate roots.
          IEventPublisher is registered as a scoped service. Never inject as singleton.
```

#### Publish context (`EventPublisher/`)

```text
PublishContext  (sealed class — mutable builder, NOT a record or immutable type)
    .CorrelationId                                          → Guid?  (null = auto-populate from Activity.Current)
    .CausationId                                            → Guid?  (null = omitted from envelope)
    .TenantId                                                → Guid?  (null = omitted from envelope; added P-340/WO-054)
    .PartitionKey                                            → string?  (null = no ordered-delivery affinity; added P-344/WO-054)
    .Subject                                                 → string?  (null = omitted from envelope)
    .Headers                                                → IReadOnlyDictionary<string, string>
    .WithCorrelationId(Guid correlationId)                  → PublishContext  (fluent, returns this)
    .WithCausationId(Guid causationId)                      → PublishContext  (fluent, returns this)
    .WithTenantId(Guid tenantId)                            → PublishContext  (fluent, returns this — P-340/WO-054)
    .WithPartitionKey(string partitionKey)                  → PublishContext  (fluent, returns this — P-344/WO-054)
    .WithSubject(string subject)                            → PublishContext  (fluent, returns this; throws ArgumentException on null/whitespace)
    .WithHeader(string key, string value)                   → PublishContext  (fluent, returns this)
    NOTE: Passed as an Action<PublishContext> callback to PublishAsync overloads.
          Callers configure the instance; the implementation owns the lifetime.
          Header keys must be non-null, non-empty strings. Duplicate keys overwrite silently.
          TenantId and Subject flow into EventEnvelope<TEvent>.TenantId/.Subject (04.Contracts) via the
          IEventPublisher path only — IMessageBus has no envelope to carry them, so TenantId/Subject on a
          plain IMessageBus.PublishAsync/SendAsync call is a no-op today (queued for review if a
          future phase adds a message-bus-level envelope). PartitionKey maps to RabbitMQ routing-key
          affinity or Azure Service Bus session identity depending on the configured transport —
          see "Ordered delivery via partition key" below.
```

#### Messaging DI builder contract (`Extensions/`)

```text
IMessagingBuilder
    .Services                                               → IServiceCollection
    NOTE: Returned by AddSharedKernelMessaging(). Allows transport-specific and feature extensions
          to chain off the core registration. All MessagingBusBuilder methods return IMessagingBuilder
          or MessagingBusBuilder (covariant for fluent chaining).

MessagingOptions  (sealed class, DI options section "SharedKernel:Messaging")
    .ServiceName                                            → string  (required non-null, non-empty)
        Used as the CloudEvents "source" field and as the routing prefix for queue/topic names.
        Must be a lowercase slug (e.g., "order-service"). Startup validation fails on null/whitespace.
    NOTE: Registered in DI by AddSharedKernelMessaging(); consumed by MassTransitEventPublisher
          and MessagingBusBuilder. Sourced from IOptions<MessagingOptions>.
```

#### Fault consumer and circuit breaker contracts (`Faults/`) — P-125

```text
FaultExceptionInfo  (sealed record)
    .ExceptionType                                          → string  (CLR exception type full name)
    .Message                                                → string  (exception message text)
    NOTE: Value equality via record semantics. Populated from MassTransit Fault<T>.Exceptions by
          FaultConsumerAdapter. No MassTransit reference — safe to use in application fault handlers.

IFaultConsumer<TMessage>  (interface)
    .HandleAsync(Guid faultId, DateTimeOffset faultTimestamp,
                 TMessage faultedMessage, FaultExceptionInfo[] exceptions,
                 CancellationToken ct)                      → Task
        Invoked by FaultConsumerAdapter when a Fault<TMessage> is delivered (dead-lettered message).
        Implement to perform compensating actions, alerting, or dead-letter triage.
    NOTE: Must be registered via MessagingBusBuilder.AddFaultConsumer<TMessage, TConsumer>().
          Never register directly via services.AddScoped — the adapter wiring will be missing.
          Exceptions must not be swallowed — rethrow to allow MassTransit fault tracking.

CircuitBreakerOptions  (sealed class, DI options section "SharedKernel:Messaging:CircuitBreaker")
    .TripThreshold      → int       (default 5  — consecutive failures before tripping the breaker)
    .ActiveThreshold    → int       (default 10 — minimum active messages before evaluation begins)
    .ResetInterval      → TimeSpan  (default 60s — time in Open state before moving to HalfOpen)
    .TrackingPeriod     → TimeSpan  (default 60s — rolling window for failure counting)
    NOTE: Consumed by MessagingBusBuilder.WithCircuitBreaker(). Global policy only — not per-consumer.
          When both WithRetry() and WithCircuitBreaker() are called, retry is inner (applied first)
          and circuit breaker is outer (applied second). This is the correct resilience layering:
          retry within current breaker state, then breaker guards against sustained failure.
```

#### Deferred message scheduler (`Scheduling/`) — P-127

```text
SchedulingProvider  (enum)
    InMemory        — MassTransit in-memory scheduler; tokens do not survive process restart
    Quartz          — MassTransit Quartz.NET integration; durable, survives restarts
    Hangfire        — placeholder; not wired in this release; reserved for future integration

SchedulingOptions  (sealed class, DI options section "SharedKernel:Messaging:Scheduling")
    .Provider       → SchedulingProvider  (default InMemory)
    NOTE: Consumed by MessagingBusBuilder.WithInMemoryScheduler() and .WithQuartzScheduler().

QuartzSchedulerOptions  (sealed class)
    .ConnectionString   → string  (required; Quartz database connection string)
    .Schema             → string  (default "quartz"; Quartz schema name in the database)
    NOTE: Passed to MessagingBusBuilder.WithQuartzScheduler(). Build() throws at startup if
          ConnectionString is null or empty when WithQuartzScheduler() is called.

IMessageScheduler  (interface)
    .ScheduleAsync<T>(T message, DateTimeOffset deliverAt, CancellationToken ct)  → Task<Guid>
        Schedules message T for delivery at deliverAt (UTC). Returns a schedule token (Guid)
        that can be passed to CancelAsync to cancel before delivery.
        CAUTION: In-memory tokens do not survive process restarts. For durable scheduling,
                 configure WithQuartzScheduler(). Never use Task.Delay inside consumers
                 as a substitute — it blocks threads and cannot survive restarts.

    .CancelAsync(Guid scheduleToken, CancellationToken ct)  → Task
        Cancels a previously scheduled message by token. No-op if already delivered.
        No exception is thrown if the token is unrecognized.
    NOTE: IMessageScheduler is registered as scoped by MessagingBusBuilder.WithInMemoryScheduler()
          or .WithQuartzScheduler(). Never inject MassTransit.IMessageScheduler directly —
          use SharedKernel.Messaging.Abstractions.IMessageScheduler.
```

#### Idempotency store (`Idempotency/`) — P-134

```text
IIdempotencyStore  (interface)
    .HasProcessedAsync(Guid messageId, CancellationToken ct)  → Task<bool>
        Returns true when the messageId has already been successfully processed.
        The implementation decides the storage backend (Redis, SQL, etc.) and
        the retention window (hint: IdempotencyOptions.ExpiryWindow).

    .MarkProcessedAsync(Guid messageId, CancellationToken ct)  → Task
        Records messageId as successfully processed. Called by IdempotentConsumerBehavior
        ONLY after the consumer body completes without exception. Never called on failure
        or on duplicate short-circuit.
    NOTE: IIdempotencyStore is NOT provided by SharedKernel — the consuming service must
          register its own implementation (e.g., RedisIdempotencyStore, EfCoreIdempotencyStore).
          Register before calling WithIdempotency() on MessagingBusBuilder. If no implementation
          is registered, Build() throws InvalidOperationException with a diagnostic message.
          Never implement custom deduplication logic inside ConsumeAsync bodies — use
          WithIdempotency() instead (hard violation).

IdempotencyOptions  (sealed class, DI options section "SharedKernel:Messaging:Idempotency")
    .ExpiryWindow   → TimeSpan  (default 24h — advisory hint to time-windowed store implementations)
    NOTE: Consumed by the consuming service's IIdempotencyStore implementation.
          Not enforced by the platform — SharedKernel does not prune expired records.
```

#### Header propagator (`HeaderPropagation/`) — P-135

```text
IMessageHeaderPropagator  (interface)
    .Propagate(PublishContext context)  → void
        Reads values from ambient scope (IHttpContextAccessor, Activity.Current.Baggage,
        IOptions<T>, etc.) and populates PublishContext headers via context.WithHeader().
        Invoked automatically before dispatch for every IMessageBus.PublishAsync,
        IMessageBus.SendAsync, IMessageBus.RequestAsync, and IEventPublisher.PublishAsync
        call when one or more propagators are registered (P-341/WO-054 — SendAsync and
        RequestAsync previously skipped propagator invocation entirely; this was a
        confirmed silent asymmetry, now fixed. Propagation applies identically across all
        four call shapes; it was never "publish-only" by design).
    PRECEDENCE RULE: Propagators run before the explicit Action<PublishContext> configure
        callback. When a caller supplies an explicit configure callback AND a propagator
        sets the same key, the explicit callback wins. This means per-call explicit overrides
        always take precedence over propagated ambient values. SendAsync and RequestAsync have
        no Action<PublishContext> overload today, so "explicit callback" reduces to "none" on
        those two verbs — propagator output alone determines the resulting CorrelationId/headers.
    NOTE: Propagators are registered as scoped services. Multiple propagators are applied
          in registration order. Implement IMessageHeaderPropagator in the consuming service's
          composition root (referencing SharedKernel.Messaging.Abstractions); do not implement
          propagators inside SharedKernel — they require service-specific ambient context.
          Zero transport NuGet dependencies in the interface definition.
          EXCEPTION (P-345/WO-054): AmbientCorrelationHeaderPropagator and TenantHeaderPropagator
          (both in SharedKernel.Messaging.MassTransit) are the two narrow, by-name exceptions to
          "do not implement propagators inside SharedKernel" — see their own contract entries below.
```

#### Tenant-context seam (`TenantContext/`) — P-345/WO-054

```text
ITenantContextAccessor  (interface)
    .TenantId  → Guid?
        Reads the current ambient tenant identity from whatever source the consuming service
        actually uses (HTTP claim, gRPC metadata, background-job context, etc.). Returns null
        when no tenant context is available.
    NOTE: A locally-owned seam interface, referencing nothing outside 01.Core–04.Contracts —
          mirrors 05.Application.Behaviors' IAuthorizationContext/IUnitOfWork bridge pattern.
          The consuming service's composition root implements this against its real
          ITenantProvider (12.Security) or ICurrentTenantService (06.Persistence); this package
          never references either directly. Register the implementation and enable automatic
          tenant propagation via MessagingBusBuilder.WithTenantContext<TAccessor>(); when not
          registered, TenantHeaderPropagator (built on this seam) is a provable no-op.
```

#### Send endpoint resolver (`MessageBus/`) — P-131

```text
ISendEndpointResolver  (interface)
    .Resolve<T>()  → string
        Returns the queue name for message type T (e.g., "payment-service-process-payment").
        The default implementation (ConventionSendEndpointResolver) uses MessagingOptions.ServiceName
        as prefix and the kebab-case type name as suffix.
        Override per command type via MessagingBusBuilder.WithSendEndpointRoute<T>(queueName).
    NOTE: Internal to the MassTransit package. Injected into MassTransitMessageBus.
          Application code never calls ISendEndpointResolver directly.
```

#### Bus readiness probe (`MessageBus/`) — P-347/WO-054

```text
IMessageBusProbe  (interface)
    .ProbeAsync(CancellationToken ct)  → Task<MessageBusHealth>
        Reports the health of the actual, already-configured message bus this domain's
        MessagingBusBuilder constructs for the consuming service — never an independently
        constructed connection built from separately supplied configuration.
    NOTE: Registered as a singleton by MessagingBusBuilder.Build() unconditionally — no opt-in
          builder call required. 07.Messaging ships this probe primitive only; it ships no
          IHealthCheck implementation. Wiring into AddHealthChecks() is 13.ServiceDefaults's
          concern, mirroring the readiness-probe split already established by
          06.Persistence/08.Storage/09.Search/10.Intelligence/17.Workflows.

MessageBusHealth  (sealed record)
    .IsHealthy    → bool
    .Description  → string?  (human-readable detail; null when healthy)
    NOTE: Returned by IMessageBusProbe.ProbeAsync. Value equality via record semantics.
```

#### Batch options (`Batch/`) — P-129

```text
BatchOptions  (sealed class, DI options section "SharedKernel:Messaging:Batch")
    .MessageLimit       → int       (default 10 — maximum messages per batch delivery)
    .TimeLimit          → TimeSpan  (default 1s  — maximum wait before delivering a partial batch)
    .ConcurrencyLimit   → int       (default 1   — concurrent batch deliveries per endpoint)
    NOTE: Applied via MessagingBusBuilder.AddBatchConsumer<TConsumer>(). These values are applied
          per endpoint, not globally. Partial batches are delivered when TimeLimit elapses even if
          MessageLimit has not been reached.
```

---

### `SharedKernel.Messaging.MassTransit` — public surface

#### Consumer base (`Consumers/`)

```text
ConsumerBase<TMessage>  (abstract class, implements MassTransit.IConsumer<TMessage>)
    abstract .ConsumeAsync(TMessage message, CancellationToken ct) → Task
    NOTE: Consuming services implement ConsumeAsync with business logic only.
          The base class implements MassTransit's Consume(ConsumeContext<TMessage>) as sealed:
            — Propagates CorrelationId from ConsumeContext to Activity.Current when no active span.
            — Forwards ConsumeContext.CancellationToken to ConsumeAsync.
            — Catches unhandled exceptions, logs at Error level with CorrelationId context, then rethrows.
          Derived classes must NOT swallow exceptions in ConsumeAsync — re-throw or let propagate.
          Unhandled exceptions trigger MassTransit retry/fault policies configured via WithRetry().
          ILogger<TConsumer> is available via protected property. Additional dependencies are constructor-injected.
          DO NOT override MassTransit Consume(ConsumeContext<TMessage>) directly — override ConsumeAsync only.
```

#### Options (`Options/`)

```text
RabbitMqBusOptions  (sealed class, DI options section "SharedKernel:Messaging:RabbitMq")
    .Host                   → string  (AMQP URI, e.g. "rabbitmq://localhost" or "amqps://host/vhost")
    .Username               → string  (default "guest" — always override in non-local environments)
    .Password               → string  (default "guest" — always override in non-local environments)
    .VirtualHost            → string  (default "/")
    .Prefetch               → ushort  (default 16 — messages pre-fetched per consumer channel)
    .RequestedHeartbeat     → TimeSpan  (default 60s — AMQP heartbeat to detect stale connections)
    .ConcurrentMessageLimit → int?  (default null — no platform-imposed limit; P-342/WO-054)
    NOTE: Never embed credentials in appsettings.json committed to source control.
          Source credentials from environment variables, Kubernetes Secrets, or Azure Key Vault.
          Prefetch tuning: lower for slow consumers, higher for fast CPU-bound consumers.
          ConcurrentMessageLimit is DISTINCT from Prefetch: Prefetch bounds how many unacknowledged
          messages the broker delivers to the channel; ConcurrentMessageLimit bounds how many of
          those the consumer processes in parallel. Set globally here, or per consumer via
          ConsumerDefinitionBase<TConsumer>.ConcurrentMessageLimit (per-consumer wins).

AzureServiceBusOptions  (sealed class, DI options section "SharedKernel:Messaging:AzureServiceBus")
    .ConnectionString       → string?  (local/dev only; mutually exclusive with FullyQualifiedNamespace)
    .FullyQualifiedNamespace → string?  (K8s managed identity path; e.g. "my-ns.servicebus.windows.net")
    .MaxConcurrentCalls     → int  (default 1 — concurrent message processing per consumer)
    .TransportType          → ServiceBusTransportType  (Amqp or AmqpWebSockets; default Amqp)
    NOTE: Exactly one of ConnectionString or FullyQualifiedNamespace must be set; startup validation
          throws InvalidOperationException if both or neither are set.
          Managed identity via DefaultAzureCredential is strongly preferred in Kubernetes workloads.
          MaxConcurrentCalls is applied to the receive endpoint's configured concurrency by
          MessagingBusBuilder's internal ConfigureAzureServiceBus helper (P-342/WO-054, fixing a
          confirmed prior defect where this option was read into AzureServiceBusOptions but never
          consulted anywhere the bus was actually built — setting it had zero observable effect).

RetryOptions  (sealed class, configures MassTransit retry pipeline)
    .Attempts               → int  (default 3; total attempts including the first delivery)
    .InitialInterval        → TimeSpan  (default 1s — delay before the second attempt)
    .IntervalIncrement      → TimeSpan  (default 1s — added to delay per subsequent attempt)
    .MaxInterval            → TimeSpan  (default 30s — ceiling on delay; caps exponential growth)
    .ImmediateAttempts      → int  (default 0 — fast retries before interval-based retries begin)
    NOTE: Business validation failures (4xx-equivalent) should be filtered via IConsumerDefinition<T>
          rather than retried. RetryOptions configures the global default applied to all consumers.
          Consumer-specific retry overrides can be configured via AddConsumer<T, TDefinition>().

OutboxOptions  (sealed class, configures MassTransit EF Core outbox delivery worker)
    .BatchSize              → int  (default 100 — rows fetched per outbox delivery cycle)
    .QueryDelay             → TimeSpan  (default 1s — polling interval for new outbox messages)
    .DuplicateDetectionWindow → TimeSpan  (default 30min — MassTransit dedup window for at-least-once)
    NOTE: OutboxOptions configures MassTransit's built-in EF Core outbox delivery background service.
          The consuming service's DbContext must include MassTransit outbox tables (see outbox migration rule).
          Delivery is at-least-once — consumers must be idempotent.

DeadLetterOptions  (sealed class, DI options section "SharedKernel:Messaging:DeadLetter") — P-343/WO-054, shipped
    .QueueNameSuffix        → string  (default "_error" — matches MassTransit's own RabbitMQ default,
                                        so the platform default changes nothing until overridden)
                                CAPABILITY NOTE (confirmed during P-343 implementation via reflection
                                and IL user-string inspection against the installed
                                MassTransit.RabbitMqTransport 9.1.2 assembly): MassTransit exposes no
                                public API to rename its automatically-derived fault ("_error") /
                                dead-letter ("_skipped") queue — those suffixes are a fixed internal
                                convention in this MassTransit version. This property is accepted for
                                forward compatibility only; it currently has NO observable effect on
                                the destination's actual name. Only MessageTimeToLive is wired.
    .MessageTimeToLive      → TimeSpan?  (default null — no expiry, unbounded retention)
                                Applied as the RabbitMQ x-message-ttl argument on MassTransit's
                                automatically-derived fault/dead-letter queues via
                                IRabbitMqSendTopologyConfigurator.ConfigureErrorSettings/
                                .ConfigureDeadLetterSettings — genuinely wired and observable.
    NOTE: RabbitMQ-only. Consumed by MessagingBusBuilder.WithDeadLetterPolicy(). Azure Service Bus
          dead-lettering is transport-native and unaffected by this options class — see "Dead-letter
          and poison-message policy" below. Calling WithDeadLetterPolicy() under an ASB transport
          logs an advisory Warning at startup rather than throwing (mirrors the WithVersionTranslator
          advisory-Warning pattern), via a new DeadLetterPolicyAdvisoryHostedService.

PayloadTransformOptions  (sealed class, DI options section "SharedKernel:Messaging:PayloadTransform") — P-346/WO-054
    .EnableCompression      → bool  (default false)
    .EnableEncryption       → bool  (default false)
    NOTE: Consumed by MessagingBusBuilder.WithPayloadTransform(). Both flags are independently
          toggleable, but publish-side ordering is always compress-then-encrypt and consume-side
          ordering is always decrypt-then-decompress — never caller-configurable. Requires
          01.Core's IPayloadCompressor (SharedKernel.Compression) and/or ISymmetricEncryptionService
          (SharedKernel.Cryptography) to already be registered in DI; Build() throws
          InvalidOperationException if a required dependency for an enabled flag is missing.
```

#### Idempotency behavior (`Consumers/`) — P-134

```text
IdempotentConsumerBehavior<TMessage>  (internal sealed class, implements IFilter<ConsumeContext<TMessage>>)
    Applied as a global MassTransit consume pipeline filter when WithIdempotency() is called.
    Pipeline logic:
      1. Read ConsumeContext.MessageId as Guid?. If null, pass through without idempotency check.
      2. Call IIdempotencyStore.HasProcessedAsync(messageId, ct).
      3. If true (already processed): acknowledge the message to the broker without invoking the
         consumer body. Do NOT call MarkProcessedAsync on the duplicate short-circuit path.
      4. If false (novel message): call next.Send(context, ct) to invoke the consumer body.
      5. After next.Send returns successfully: call IIdempotencyStore.MarkProcessedAsync(messageId, ct).
      6. If next.Send throws: propagate the exception without calling MarkProcessedAsync
         (the consumer failed; the message should be retried, not marked as processed).
    NOTE: This is the ONLY approved deduplication mechanism. Never implement deduplication
          logic inside ConsumeAsync bodies (hard violation).
```

#### Built-in header propagators (`HeaderPropagation/`) — P-345/WO-054

```text
AmbientCorrelationHeaderPropagator  (sealed class, implements IMessageHeaderPropagator)
    .Propagate(PublishContext context)  → void
        Reads Activity.Current?.TraceId when an ambient Activity exists and calls
        context.WithCorrelationId(...). No-op when there is no ambient Activity.
    NOTE: Requires zero consumer-supplied dependency — register via
          MessagingBusBuilder.WithAmbientCorrelationPropagation(). This is one of two named,
          documented exceptions to "never implement IMessageHeaderPropagator inside SharedKernel.*"
          (see the IMessageHeaderPropagator contract entry above) — distributed-trace correlation
          identity is not service-specific ambient context; it is already available to any BCL
          code via System.Diagnostics.Activity.Current.

TenantHeaderPropagator  (sealed class, implements IMessageHeaderPropagator)
    .Propagate(PublishContext context)  → void
        Resolves ITenantContextAccessor? from DI (optional — GetService, not GetRequiredService).
        When present and its TenantId has a value, calls context.WithTenantId(value).
        When ITenantContextAccessor is not registered, this is a provable no-op.
    NOTE: Register via MessagingBusBuilder.WithTenantContext<TAccessor>() — registers both
          TAccessor as scoped ITenantContextAccessor and this propagator in one call. The second
          named exception to the "never implement propagators inside SharedKernel.*" rule — safe
          here because the seam (ITenantContextAccessor) that supplies the actual service-specific
          value is itself bridged by the consuming service, not by this class.
```

#### Consumer definition base (`Consumers/`) — P-136

```text
ConsumerDefinitionBase<TConsumer>  (abstract class, implements IConsumerDefinition<TConsumer>)
    Provides platform-standard per-consumer configuration with pre-wired retry exception filter.
    Sealed Configure(IReceiveEndpointConfigurator, IConsumerConfigurator<TConsumer>) entry:
      (a) Sets endpoint name from EndpointName if non-null (default: MassTransit convention)
      (b) Sets prefetch count from PrefetchCount if non-null (default: MassTransit default)
      (c) Wires retry exception filter: for each type in NonRetryableExceptions, calls
          r.Ignore(exceptionType) so those exceptions bypass retry and dead-letter immediately
      (d) Delegates to abstract ConfigureConsumer for subclass-specific configuration
      (e) Sets concurrency limit from ConcurrentMessageLimit if non-null (P-342/WO-054)

    protected abstract ConfigureConsumer(IReceiveEndpointConfigurator, IConsumerConfigurator<TConsumer>,
                                          IBusRegistrationContext)  → void
        Subclasses implement this for concurrency, additional filters, etc. Must NOT override
        IConsumerDefinition<TConsumer>.Configure directly.

    protected virtual NonRetryableExceptions  → IReadOnlyList<Type>
        Default: empty list (all exceptions are retried per global policy).
        Override to declare exception types that must bypass retry entirely.
        Typical entries: ValidationException (01.Core), NotFoundException.

    protected virtual EndpointName  → string?  (default null — MassTransit convention)
    protected virtual PrefetchCount  → int?     (default null — MassTransit default)
    protected virtual ConcurrentMessageLimit  → int?  (default null — no per-consumer override;
                                                        falls back to RabbitMqBusOptions.ConcurrentMessageLimit
                                                        or AzureServiceBusOptions.MaxConcurrentCalls; P-342/WO-054)
    NOTE: Register consuming-service definitions via AddConsumer<TConsumer, TDefinition>().
          Prefer this base over raw IConsumerDefinition<TConsumer>; the retry exception filter
          wiring is the platform's minimum standard for consumer configuration.
```

#### Version translator (`SchemaEvolution/`) — P-137

```text
IMessageVersionTranslator<TOld, TNew>  (interface)
    .Translate(TOld old)  → TNew
        Synchronous pure projection from old message schema to new message schema.
        SYNCHRONOUS-ONLY CONSTRAINT: Translation must not perform I/O, call external services,
        or produce side effects. It is a pure function called in the deserialization pipeline.
        Implementations should be stateless.
    NOTE: Registered as a singleton. WithVersionTranslator<TOld, TNew, TTranslator>() wires
          the MassTransit deserialization pipeline so that TOld messages arriving at the transport
          are transparently projected to TNew before consumer delivery. No consumer code change
          required. If WithVersionTranslator is registered but no consumer for TNew exists in the
          same service, Build() logs a Warning (advisory only — consumer may be in another service).
```

#### Messaging diagnostics (`Diagnostics/`) — P-172

```text
MessagingDiagnostics  (internal static class)
    .ActivitySource     → ActivitySource  (static readonly; "SharedKernel.Messaging", "1.0.0")
        NOTE: Single static ActivitySource instance for the whole SharedKernel.Messaging.MassTransit
              package — this is the platform-standard static-instrument pattern (the same shape as
              a static ILogger category name or a Meter instance), NOT a "static mutable state"
              hard violation. ActivitySource carries no mutable business state; the .NET diagnostics
              API is explicitly designed around process-lifetime static instrument instances.
        Consumed by ConsumerBase<TMessage>.Consume(), MassTransitEventPublisher.PublishAsync<TEvent>(),
        and — as of P-348/WO-054 — MassTransitMessageBus.SendAsync<T>(), .RequestAsync<TRequest,TResponse>(),
        and .ExecuteRoutingSlipAsync() to start child Activities. Consumed by
        13.ServiceDefaults.WithMessagingTelemetry() (P-132), which wires "SharedKernel.Messaging"
        into the host's TracerProvider via .AddSource(...) — 13.ServiceDefaults never constructs
        this ActivitySource itself; it only registers the already-existing source name with the
        host's TracerProvider/MeterProvider.

    .Meter              → Meter  (static readonly; "SharedKernel.Messaging", "1.0.0" — P-348/WO-054)
        NOTE: Companion static Meter instrument, added alongside ActivitySource under the same
              platform-standard static-instrument exception (see the ActivitySource NOTE above —
              identical reasoning applies to Meter). Hosts five instruments:
                messaging.publish.count    (Counter<long>)  — tagged messaging.event_type (EventPublisher)
                                                                or messaging.message_type (IMessageBus.PublishAsync)
                messaging.consume.count    (Counter<long>)  — tagged messaging.message_type; incremented only
                                                                after ConsumeAsync returns without throwing
                messaging.consume.duration (Histogram<double>, ms) — tagged messaging.message_type; recorded
                                                                unconditionally (success AND failure)
                messaging.retry.count      (Counter<long>)  — tagged messaging.message_type; incremented when
                                                                ConsumeContext.GetRetryAttempt() > 0 — see the
                                                                documented MassTransit 9.1.2 gap below
                messaging.fault.count      (Counter<long>)  — tagged messaging.message_type; incremented
                                                                unconditionally by FaultConsumerAdapter
              Consumed by 13.ServiceDefaults.WithMessagingTelemetry() via .AddMeter(...), mirroring
              the ActivitySource registration split above.

    RETRY OBSERVATION GAP (P-348/WO-054): MassTransit 9.1.2 ships IRetryObserver /
    IRetryObserverConnector in its public API surface, but reflection over the shipped assembly
    confirms NO reachable configurator (IBusFactoryConfigurator, IReceiveEndpointConfigurator,
    IBus, IBusControl) implements IRetryObserverConnector — ConnectRetryObserver is unreachable
    from MessagingBusBuilder's configuration-time API. This is undocumented in MassTransit's own
    XML doc comments; discovered by reflecting over MassTransit.dll directly. The verified working
    alternative — confirmed via a live TestHarness run with UseMessageRetry(r => r.Incremental(2,
    ...)) — is ConsumeContext.GetRetryAttempt() (MassTransit.RetryContextExtensions): 0 on the
    original delivery, 1/2/... on each subsequent retry-filter re-delivery, and safely 0 (never
    throws) when no retry middleware is configured at all. ConsumerBase<TMessage>.Consume() checks
    this per-invocation rather than subscribing to an observer callback — the best available
    observation point in 9.1.2, not the originally-anticipated IRetryObserver hook.
```

#### Full dispatch-surface verb coverage (`Consumers/`, `EventPublisher/`, `MessageBus/` — P-172/P-348/WO-054)

Prior to P-348, only `Consume` and `Publish` (via `IEventPublisher`) produced any `Activity` at all, and zero `Meter` instruments existed. The table below is the authoritative, complete picture after P-348:

| Verb | Activity Name | Activity Tags | Meter Instrument(s) Incremented/Recorded |
| --- | --- | --- | --- |
| `ConsumerBase<TMessage>.Consume()` | `"Consumer.Consume"` | `messaging.message_type` | `messaging.retry.count` (when `GetRetryAttempt() > 0`, before dispatch) → `messaging.consume.count` (on `ConsumeAsync` success) + `messaging.consume.duration` (always, in a `finally`) |
| `MassTransitEventPublisher.PublishAsync<TEvent>()` | `"EventPublisher.Publish"` | `messaging.event_type` | `messaging.publish.count` (tagged `messaging.event_type`, only after the underlying publish call succeeds) |
| `MassTransitMessageBus.PublishAsync<T>()` (both overloads) | — (no activity; unchanged since P-172) | — | `messaging.publish.count` (tagged `messaging.message_type`, only after the underlying publish call succeeds) |
| `MassTransitMessageBus.SendAsync<T>()` | `"MessageBus.Send"` | `messaging.message_type` | — |
| `MassTransitMessageBus.RequestAsync<TRequest,TResponse>()` | `"MessageBus.Request"` | `messaging.request_type`, `messaging.response_type` | — |
| `MassTransitMessageBus.ExecuteRoutingSlipAsync()` | `"MessageBus.ExecuteRoutingSlip"` | `messaging.routing_slip.activity_count` (`slip.Itinerary.Count`; populated only once the `object` argument is confirmed to be a `RoutingSlip`) | — |
| `FaultConsumerAdapter<TMessage,TFaultConsumer>.Consume()` | — (no activity; unchanged since P-125) | — | `messaging.fault.count` (tagged `messaging.message_type`, unconditionally, before invoking the registered `IFaultConsumer<TMessage>`) |

Every activity in the table above is started at the top of its method via a `using var activity = MessagingDiagnostics.ActivitySource.StartActivity(...)` and is therefore disposed when the method returns OR throws — including `ExecuteRoutingSlipAsync`'s two `ArgumentException` validation throws (the `messaging.routing_slip.activity_count` tag is populated only on the second throw onward, once the itinerary is known; it cannot be set on the "not a `RoutingSlip` at all" throw path). `MassTransitMessageBus.PublishAsync` deliberately gained no new `Activity` in P-348 — only `SendAsync`/`RequestAsync`/`ExecuteRoutingSlipAsync` had the completeness gap; `PublishAsync` gained only its `Meter` counter.

```text
ConsumerBase<TMessage>.Consume()  (sealed entry point — updated P-172/P-348)
    Starts a child Activity via MessagingDiagnostics.ActivitySource.StartActivity("Consumer.Consume")
    before delegating to ConsumeAsync. Tags:
        messaging.message_type = typeof(TMessage).Name
    The activity is disposed after ConsumeAsync completes (success or exception) — standard
    `using` disposal scope wrapping the existing CorrelationId-propagation and log-then-rethrow logic.
    Log scope (ILogger.BeginScope) is enriched (additive to existing CorrelationId and x-sk-* header
    scope values from P-135) with:
        messaging.destination    = ConsumeContext.DestinationAddress?.AbsolutePath  (omitted if null)
        messaging.message_type   = typeof(TMessage).Name
    P-348: also increments MessagingDiagnostics.RetryCounter when context.GetRetryAttempt() > 0
    (before dispatch), increments MessagingDiagnostics.ConsumeCounter after ConsumeAsync returns
    without throwing, and records MessagingDiagnostics.ConsumeDurationHistogram unconditionally in
    a `finally` block — all tagged messaging.message_type.

MassTransitEventPublisher.PublishAsync<TEvent>()  (updated P-172/P-348)
    Starts a child Activity via MessagingDiagnostics.ActivitySource.StartActivity("EventPublisher.Publish")
    before delegating to IPublishEndpoint. Tags:
        messaging.event_type = IntegrationEventDescriptor.For<TEvent>().Name
                               (the [IntegrationEvent] name — identical to the envelope's CloudEvents type)
    The name is resolved before the activity starts, so an event type without a valid attribute fails
    fast with no telemetry or transport work.
    The activity is disposed after the publish call completes (success or exception).
    P-348: increments MessagingDiagnostics.PublishCounter (tagged messaging.event_type) only after
    the underlying IPublishEndpoint.Publish call completes without throwing — a faulted publish is
    never counted. The private PublishEnvelopeAsync<TEvent> helper is async specifically so this
    increment happens after the awaited publish call, not merely after the Task is constructed.
    NOTE: This activity is independent of the EventEnvelope<TEvent> CloudEvents CorrelationId field —
          the Activity's own TraceId/SpanId comes from .NET's ambient Activity.Current chain; the
          envelope's CorrelationId is still sourced per the CloudEvents compliance rule below.

MassTransitMessageBus.PublishAsync<T>()  (both overloads — Meter-only, no Activity — P-348)
    Increments MessagingDiagnostics.PublishCounter (tagged messaging.message_type) only after the
    underlying IPublishEndpoint.Publish call completes without throwing. Both overloads became
    async for the same reason as MassTransitEventPublisher.PublishEnvelopeAsync above.

MassTransitMessageBus.SendAsync<T>() / .RequestAsync<TRequest,TResponse>() / .ExecuteRoutingSlipAsync()  — P-348/WO-054
    Each starts and disposes its own child Activity, matching the Consume/Publish shape exactly:
        SendAsync              → "MessageBus.Send",              tag messaging.message_type
        RequestAsync            → "MessageBus.Request",           tags messaging.request_type, messaging.response_type
        ExecuteRoutingSlipAsync → "MessageBus.ExecuteRoutingSlip", tag messaging.routing_slip.activity_count
    Prior to P-348, none of these three verbs produced any activity at all — a completeness gap,
    not a design gap, now closed to full dispatch-surface coverage. None of these three verbs
    increments a Meter instrument directly (Send/Request dispatch a single message with no
    publish/consume/retry/fault semantics of their own at the bus level; a request's underlying
    response delivery is itself a Consume on the responder's side, already covered by
    ConsumerBase.Consume's own instrumentation there).

FaultConsumerAdapter<TMessage,TFaultConsumer>.Consume()  (updated P-348 — no Activity, unchanged since P-125)
    Increments MessagingDiagnostics.FaultCounter (tagged messaging.message_type) unconditionally,
    before invoking the registered IFaultConsumer<TMessage> — regardless of whether that handler
    itself then succeeds or throws while handling the fault.
```

#### Bus readiness probe implementation (`MessageBus/`) — P-347/WO-054

```text
MassTransitMessageBusProbe  (internal sealed class, implements IMessageBusProbe)
    .ProbeAsync(CancellationToken ct)  → Task<MessageBusHealth>
        Constructs a MassTransit.Monitoring.BusHealthCheck against the real, DI-registered
        MassTransit.Transports.IBusInstance singleton this builder's Build() call configures for
        the consuming service (injected via the probe's constructor — never resolved manually),
        wraps it in a HealthCheckContext carrying a HealthCheckRegistration (required — see the
        MassTransit 9.x API notes below), and calls CheckHealthAsync. Maps
        HealthStatus.Healthy → MessageBusHealth(true, null); any other HealthStatus (Degraded or
        Unhealthy) → MessageBusHealth(false, <the check's own Description>). Never opens a second,
        independently constructed transport connection built from separately supplied configuration
        — BusHealthCheck is MassTransit's own shipped health-check surface (the same type MassTransit
        wires into a host's own AddHealthChecks() pipeline internally via
        MassTransit.Monitoring.ConfigureBusHealthCheckServiceOptions), consumed here directly as an
        implementation detail. This type never implements Microsoft.Extensions.Diagnostics.
        HealthChecks.IHealthCheck itself — 07.Messaging ships no IHealthCheck.
    NOTE: Registered as a singleton by MessagingBusBuilder.Build() unconditionally, matching
          MassTransit's own singleton IBus/IBusControl/IBusInstance lifetime. Transport-agnostic
          from the caller's perspective — behaves identically whether the consuming service
          configured RabbitMQ or Azure Service Bus, since IBusInstance/BusHealthCheck are both
          transport-neutral MassTransit core types.
```

#### Shared log scope construction (`Logging/`) — P-254

```text
MessagingLogScope  (internal static class)
    .CorrelationIdKey  → const string = "CorrelationId"                              — P-263
        The single named constant for the log-scope correlation entry's dictionary key.
        Package-local (internal): this key names a structured-log-scope entry that is
        this domain's own logging contract, not a cross-service wire format — it is
        deliberately NOT promoted to 01.Core's EventId/logging registry.
    .Create(Guid? correlationId)  → Dictionary<string, object?>
        Returns a new mutable dictionary seeded with exactly one entry:
            [CorrelationIdKey] = correlationId?.ToString("D") ?? string.Empty
        This is the ONLY approved construction path for the base entry of an
        ILogger.BeginScope(...) dictionary anywhere in this package. Callers add their
        own type-specific entries to the returned dictionary before passing it to BeginScope
        (e.g. MessageType, BatchSize, FaultId, routing_slip.tracking_number).
    NOTE: Consumed by ConsumerBase<TMessage>.Consume(), BatchConsumerBase<TMessage>.Consume(),
          FaultConsumerAdapter<TMessage,TFaultConsumer>.Consume(), and
          RoutingSlipActivityBase<TArguments,TLog>.Execute()/Compensate() — the four
          consumer/activity base types in this package that build a structured log scope.
          Guarantees an identical "CorrelationId" key name and identical null-handling
          across all four, instead of four independently hand-rolled dictionary literals
          drifting out of sync with each other (the exact defect P-254 fixed).
          The key's runtime string value ("CorrelationId") is unchanged by the P-263
          constant promotion — only its authoring path moved from a bare literal to
          CorrelationIdKey, so no test assertion against the captured scope value changes.
          VersionTranslatingConsumer and TranslatorRegistrationValidator do not use
          BeginScope and are out of scope for this helper.
```

#### Routing slip base (`RoutingSlips/`) — P-139

```text
RoutingSlipActivityBase<TArguments, TLog>  (abstract class, implements IActivity<TArguments, TLog>)
    Platform-standard base for MassTransit Courier activities.
    Sealed Execute(ExecuteContext<TArguments>) entry:
      — Propagates CorrelationId from routing slip tracking number into Activity.Current.
      — Enriches log scope with routing_slip.tracking_number and routing_slip.activity_name.
      — Delegates to abstract ExecuteAsync(TArguments, CancellationToken).
      — Catches unhandled exceptions from ExecuteAsync: logs at Error with tracking number, rethrows.
    Sealed Compensate(CompensateContext<TLog>) entry:
      — Same correlation-propagation and log-scope enrichment as Execute.
      — Delegates to abstract CompensateAsync(TLog, CancellationToken).
      — Same exception log-then-rethrow semantics.

    abstract .ExecuteAsync(TArguments arguments, CancellationToken ct)  → Task<ExecutionResult>
    abstract .CompensateAsync(TLog log, CancellationToken ct)           → Task<CompensationResult>

    Protected helpers:
        Complete(TLog log)         → ExecutionResult   (returns context.Completed(log))
        Faulted(Exception ex)      → ExecutionResult   (returns context.Faulted(ex))
        CompensationComplete()     → CompensationResult (returns context.Compensated())

    NOTE: Routing slips are for STATELESS multi-step coordination. When workflow state must survive
          process restarts or requires durable persistence, use SagaStateMachineBase<TSaga> instead.
          Do NOT override MassTransit Execute or Compensate directly — override ExecuteAsync and
          CompensateAsync only.
```

#### Routing slip builder (`RoutingSlips/`) — P-139

```text
IRoutingSlipBuilder  (interface)
    .AddActivity(string activityName, Uri executeAddress, object arguments) → IRoutingSlipBuilder
        Adds an activity step to the routing slip. activityName is a human-readable label.
        executeAddress is the MassTransit endpoint URI for the activity's execute endpoint.
        arguments is an object matching the activity's TArguments type.
        Returns this for fluent chaining.

    .Build()  → object
        Constructs and returns the opaque routing slip object (typed as object to avoid
        a MassTransit reference in the Abstractions package). The returned value must be
        passed directly to IMessageBus.ExecuteRoutingSlipAsync — do not cast or inspect it.
    NOTE: The concrete implementation (MassTransitRoutingSlipBuilder) lives in the MassTransit
          package. Resolve IRoutingSlipBuilder from DI; never construct MassTransit's
          RoutingSlipBuilder directly in application code.
```

#### Saga state machine base (`Sagas/`) — P-128

```text
SagaStateBase  (abstract record)
    .CorrelationId      → Guid            (primary key of the saga instance)
    .CurrentState       → string          (current state name, managed by MassTransit state machine)
    .CreatedAt          → DateTimeOffset  (UTC timestamp when the saga instance was created)
    .UpdatedAt          → DateTimeOffset  (UTC timestamp of the last state transition)
    .Version            → int             (implements ISagaVersion for EF Core optimistic concurrency)
    NOTE: Implementing ISagaVersion from MassTransit is why this type lives in the MassTransit
          package, not in Abstractions. EF Core mapping configuration lives in the consuming
          service's IEntityTypeConfiguration — no EF attributes on this record.

SagaStateMachineBase<TSaga>  (abstract class, where TSaga : SagaStateBase)
    Extends MassTransitStateMachine<TSaga>.
    Protected helpers:
        TransitionTo(State state)    — transitions saga to named state (delegates to MassTransit)
        Finalize()                   — marks saga as complete; MassTransit removes the instance
    NOTE: This is a thin ergonomic wrapper, NOT a complete abstraction. Consuming services that
          need advanced MassTransit state machine features (composite events, activities, routing
          slips) should reference MassTransit directly for those specific calls.
          Consuming services extend SagaStateMachineBase<TSaga> and declare their own states,
          events, and transitions using the protected helper surface.
```

#### Batch consumer base (`Consumers/`) — P-129

```text
BatchConsumerBase<TMessage>  (abstract class, implements IConsumer<Batch<TMessage>>)
    abstract .ConsumeAsync(IReadOnlyList<TMessage> messages, CancellationToken ct) → Task
    NOTE: The base class implements IConsumer<Batch<TMessage>>.Consume as the sealed entry point:
            — Logs structured entry with batch size and batch-level CorrelationId.
            — Extracts CancellationToken from ConsumeContext and forwards to ConsumeAsync.
            — Catches unhandled exceptions from ConsumeAsync, logs at Error level, then rethrows.
          Rethrow is mandatory — swallowed exceptions cause silent batch loss.
          Do NOT register BatchConsumerBase subclasses via AddConsumer<T>() — batch configuration
          will be missing. Use AddBatchConsumer<TConsumer>() exclusively.
```

#### DI extensions (`Extensions/`)

```text
AddSharedKernelMessaging(IServiceCollection services, Action<MessagingOptions>? configure = null)
    → returns MessagingBusBuilder

MessagingBusBuilder  (sealed class, implements IMessagingBuilder)

    .UseRabbitMq(string connectionString)
        — parses AMQP connection string; configures MassTransit RabbitMQ host.

    .UseRabbitMq(Action<RabbitMqBusOptions> configure)
        — configures MassTransit RabbitMQ host from an explicit options action.

    .UseAzureServiceBus(string connectionString)
        — parses connection string; configures MassTransit Azure Service Bus host.

    .UseAzureServiceBus(Action<AzureServiceBusOptions> configure)
        — configures MassTransit Azure Service Bus host from an explicit options action.

    .WithRetry(Action<RetryOptions>? configure = null)
        — configures MassTransit UseRetry pipeline with RetryOptions.
        — null uses default RetryOptions (3 attempts, 1s/1s/30s).
        — Optional. Omitting registers no retry policy (MassTransit default: no retry).

    .WithEntityFrameworkOutbox<TDbContext>(Action<OutboxOptions>? configure = null)
        — calls MassTransit AddEntityFrameworkOutbox<TDbContext>() with OutboxOptions.
        — where TDbContext : DbContext (Microsoft.EntityFrameworkCore constraint only).
        — The consuming service's TDbContext must include MassTransit outbox tables.
        — Run "dotnet ef migrations add AddMassTransitOutbox" after calling this method.
        — SharedKernel.Messaging.MassTransit provides no migrations — consuming service owns them.
        — Optional. Omitting publishes directly to the broker without transactional guarantee.

    .AddConsumer<TConsumer>()
        — registers a MassTransit consumer by convention.
        — TConsumer must implement IConsumer<TMessage> (directly or via ConsumerBase<TMessage>).

    .AddConsumer<TConsumer, TConsumerDefinition>()
        — registers consumer with an explicit IConsumerDefinition<TConsumer> for custom endpoint name,
          prefetch, retry override, or dead-letter configuration.

    .WithCircuitBreaker(Action<CircuitBreakerOptions>? configure = null)
        — configures MassTransit UseCircuitBreaker pipeline with CircuitBreakerOptions.
        — null uses default CircuitBreakerOptions (TripThreshold=5, ActiveThreshold=10, ResetInterval=60s).
        — ORDERING RULE: when both WithRetry() and WithCircuitBreaker() are called, retry is applied
          inner (first), circuit breaker is applied outer (second). This is the correct resilience
          layering — retry exhaustion within current breaker state, breaker guards sustained failure.
        — Global policy only. Do NOT configure circuit breakers per-consumer via IConsumerDefinition —
          always use WithCircuitBreaker() for the global policy.
        — Optional. Omitting registers no circuit breaker.

    .AddFaultConsumer<TMessage, TFaultConsumer>()
        — registers internal FaultConsumerAdapter<TMessage, TFaultConsumer> for Fault<TMessage>.
        — TFaultConsumer must implement IFaultConsumer<TMessage>.
        — The adapter translates ConsumeContext<Fault<TMessage>> to IFaultConsumer<TMessage>.HandleAsync,
          mapping Fault<TMessage>.Exceptions to FaultExceptionInfo[].
        — Returns MessagingBusBuilder for fluent chaining.

    .WithInMemoryScheduler()
        — For RabbitMQ: calls cfg.AddDelayedMessageScheduler() + busCfg.UseDelayedMessageScheduler().
        — For Azure Service Bus: calls cfg.AddServiceBusMessageScheduler() + busCfg.UseServiceBusMessageScheduler().
        — registers IMessageScheduler → MassTransitMessageScheduler as scoped.
        — WARNING: In-memory tokens do not survive process restarts. For production use
          WithQuartzScheduler() instead.
        — DO NOT call UseDelayedMessageScheduler() on an ASB bus configurator — use
          UseServiceBusMessageScheduler() instead (UseDelayedMessageScheduler is OBSOLETE on ASB).

    MassTransitMessageScheduler  (internal sealed class, implements IMessageScheduler)
        — ScheduleAsync<T>: delegates to MassTransit IMessageScheduler.SchedulePublish<T>(DateTime, T, ct);
          returns ScheduledMessage<T>.TokenId (Guid) as the schedule token; stores token→Type mapping in
          ConcurrentDictionary<Guid, Type> to support type-agnostic cancel.
        — CancelAsync: looks up message type from ConcurrentDictionary; calls
          MassTransit IMessageScheduler.CancelScheduledPublish(Type, Guid, ct) (non-generic overload);
          behaves as a no-op (does not throw) when the token is unrecognized or already delivered.
        — Registered as scoped by WithInMemoryScheduler() and WithQuartzScheduler().
        — Never inject MassTransit.IMessageScheduler directly — always inject via
          SharedKernel.Messaging.Abstractions.IMessageScheduler.

    .WithQuartzScheduler(Action<QuartzSchedulerOptions>? configure = null)
        — wires MassTransit Quartz.NET scheduler integration (MassTransit.Quartz package).
        — reads QuartzSchedulerOptions.ConnectionString (required) and Schema (default "quartz").
        — registers IMessageScheduler → MassTransitMessageScheduler as scoped.
        — Build() throws InvalidOperationException if ConnectionString is null/empty.

    .AddSaga<TSaga>()
        — registers a saga by type with in-memory saga repository (development/testing default).
        — TSaga must derive from SagaStateBase.
        — Returns MessagingBusBuilder for fluent chaining.

    .AddSaga<TSaga, TDefinition>()
        — registers saga with an explicit saga definition for custom endpoint, retry, or
          dead-letter configuration.
        — Returns MessagingBusBuilder for fluent chaining.

    .WithEntityFrameworkSagaRepository<TDbContext, TSaga>()
        — wires MassTransit EF Core saga repository for TSaga using TDbContext.
        — where TDbContext : DbContext (Microsoft.EntityFrameworkCore constraint only).
        — NO compile-time reference to SharedKernel.Persistence.* is introduced.
        — consuming service must add saga state entity to TDbContext and run EF migrations.
        — Returns MessagingBusBuilder for fluent chaining.

    .AddBatchConsumer<TConsumer>(Action<BatchOptions>? configure = null)
        — registers TConsumer (where TConsumer : BatchConsumerBase<TMessage>) with MassTransit
          batch endpoint configuration.
        — applies MessageLimit, TimeLimit, ConcurrencyLimit from BatchOptions.
        — null uses default BatchOptions (MessageLimit=10, TimeLimit=1s, ConcurrencyLimit=1).
        — Returns MessagingBusBuilder for fluent chaining.
        — NOTE: Use this method ONLY for batch consumers. Do NOT use AddConsumer<T>() for
          BatchConsumerBase subclasses — batch configuration will not be applied.

    .WithSendEndpointRoute<T>(string queueName)
        — registers a per-command-type queue name override for SendAsync<T>().
        — queueName must be non-null, non-empty; validated at call time.
        — MassTransitMessageBus.SendAsync<T>() checks this per-type route before falling back
          to ConventionSendEndpointResolver.
        — Returns MessagingBusBuilder for fluent chaining.
        — NOTE: This is the ONLY approved way to configure cross-service command routing.
          Never pass hardcoded queue URI strings to ISendEndpointProvider.GetSendEndpoint().

    .WithIdempotency()
        — registers IdempotentConsumerBehavior<TMessage> as a global MassTransit consume
          pipeline filter applied to all consumers.
        — At Build() time, if IIdempotencyStore is not registered in IServiceCollection,
          throws InvalidOperationException with a diagnostic message.
        — Returns MessagingBusBuilder for fluent chaining.
        — NOTE: The consuming service must register a concrete IIdempotencyStore before calling
          WithIdempotency(). SharedKernel does not provide an implementation.

    .WithIdempotency(Action<IdempotencyOptions>)
        — companion overload; configures IdempotencyOptions (ExpiryWindow) in addition to
          registering the behavior.
        — IdempotencyOptions is available via IOptions<IdempotencyOptions> to the consuming
          service's IIdempotencyStore implementation.
        — Returns MessagingBusBuilder for fluent chaining.

    .WithHeaderPropagator<T>()
        — registers T as a scoped IMessageHeaderPropagator in DI.
        — Multiple calls are additive; propagators are applied in registration order.
        — At every IMessageBus.PublishAsync, IMessageBus.SendAsync, IMessageBus.RequestAsync, and
          IEventPublisher.PublishAsync call, all registered propagators are invoked before the
          explicit Action<PublishContext> configure callback (P-341/WO-054 — SendAsync/RequestAsync
          previously skipped propagator invocation entirely; fixed. Applies identically across
          all three dispatch verbs now, not publish alone).
        — Returns MessagingBusBuilder for fluent chaining.

    .WithAmbientCorrelationPropagation()  — P-345/WO-054
        — registers AmbientCorrelationHeaderPropagator as a scoped IMessageHeaderPropagator.
        — Zero-argument — requires no consumer-authored class, unlike WithHeaderPropagator<T>().
        — Populates CorrelationId from ambient Activity.Current on every dispatch verb.
        — Returns MessagingBusBuilder for fluent chaining.

    .WithTenantContext<TAccessor>()  where TAccessor : class, ITenantContextAccessor  — P-345/WO-054
        — registers TAccessor as scoped ITenantContextAccessor AND registers TenantHeaderPropagator
          as a scoped IMessageHeaderPropagator, in one call.
        — The consuming service only writes the ITenantContextAccessor implementation bridging its
          real tenant source — it never writes a propagator by hand.
        — Returns MessagingBusBuilder for fluent chaining.

    .WithVersionTranslator<TOld, TNew, TTranslator>()
        — registers TTranslator as a singleton IMessageVersionTranslator<TOld, TNew> in DI.
        — Registers VersionTranslatingConsumer<TOld, TNew> (internal IConsumer<TOld>) via
          cfg.AddConsumer<>(). When a TOld message arrives, the consumer calls
          TTranslator.Translate(TOld) synchronously and republishes the result via
          ConsumeContext.Publish<TNew>(), so consumers registered for TNew receive the
          translated payload with no code change.
        — At host startup (not at Build() time), TranslatorRegistrationValidationHostedService
          runs TranslatorRegistrationValidator and logs a Warning if no consumer for TNew is
          registered in the same service (advisory — not a hard failure).
        — Returns MessagingBusBuilder for fluent chaining.

    .AddRoutingSlipActivity<TActivity>()
        — registers the Courier activity with MassTransit via cfg.AddActivity<TActivity, TArguments, TLog>().
        — TActivity must extend RoutingSlipActivityBase<TArguments, TLog> (or implement
          IActivity<TArguments, TLog> directly for advanced use cases).
        — Returns MessagingBusBuilder for fluent chaining.

    .WithDeadLetterPolicy(Action<DeadLetterOptions>? configure = null)  — P-343/WO-054, shipped
        — RabbitMQ only. Applies MessageTimeToLive as the x-message-ttl argument on MassTransit's
          automatically-derived fault/dead-letter queues via
          IRabbitMqSendTopologyConfigurator.ConfigureErrorSettings/.ConfigureDeadLetterSettings —
          confirmed via reflection to be the same settings RabbitMqReceiveEndpointBuilder uses to
          build the real fault transport a retry-exhausted message is routed to.
        — QueueNameSuffix is accepted but has NO observable effect in MassTransit 9.1.2 — see
          DeadLetterOptions.QueueNameSuffix's own capability note above.
        — null uses default DeadLetterOptions (QueueNameSuffix="_error", MessageTimeToLive=null).
        — When called while UseAzureServiceBus() is the configured transport, Build() registers
          DeadLetterPolicyAdvisoryHostedService, which logs an advisory Warning at host startup
          (same IHostedService-deferred-advisory pattern as WithVersionTranslator's
          TranslatorRegistrationValidationHostedService) rather than throwing — ASB dead-lettering is
          transport-native and unaffected by this option.
        — Optional. Omitting preserves MassTransit's own default RabbitMQ error-queue behavior.
        — Returns MessagingBusBuilder for fluent chaining.
        — Returns MessagingBusBuilder for fluent chaining.

    .WithPayloadTransform(Action<PayloadTransformOptions>? configure = null)  — P-346/WO-054
        — wires an opt-in compressing/encrypting serializer decorator around the platform's
          default STJ serializer. Publish-side order is always compress-then-encrypt; consume-side
          order is always decrypt-then-decompress — never caller-configurable.
        — null uses default PayloadTransformOptions (both flags false — effectively a no-op).
        — At Build() time, throws InvalidOperationException if EnableCompression is true and
          IPayloadCompressor is not registered, or if EnableEncryption is true and
          ISymmetricEncryptionService is not registered (register via 01.Core's
          AddSharedKernelCompression()/AddSharedKernelCryptography() first).
        — Changes wire format — a consumer without the matching transform enabled fails loudly
          at deserialization rather than silently misinterpreting the payload.
        — Optional. Omitting leaves publish/consume behavior unchanged (disabled by default).
        — Returns MessagingBusBuilder for fluent chaining.

    .Build() → IServiceCollection
        — Registers IMessageBus → MassTransitMessageBus (scoped).
        — Registers IEventPublisher → MassTransitEventPublisher (scoped).
        — Registers MessagingOptions via IOptions<MessagingOptions>.
        — Registers MassTransit IBus, IPublishEndpoint, ISendEndpointProvider (MassTransit-managed scoped).
        — Registers IHostedService for MassTransit bus lifecycle (start/stop via IBusControl).
        — Registers MassTransitRoutingSlipBuilder as scoped IRoutingSlipBuilder (always, even when
          AddRoutingSlipActivity has not been called — the interface is usable independently).
        — Registers IMessageBusProbe → MassTransitMessageBusProbe (singleton, always, unconditionally
          — P-347/WO-054; wraps the real registered bus, no opt-in call required).
        — Startup validation: MessagingOptions.ServiceName non-null/non-empty; transport configured.
        — If WithIdempotency() was called and IIdempotencyStore is not registered in IServiceCollection,
          throws InvalidOperationException with diagnostic message.
        — If WithVersionTranslator() was called, registers a singleton
          TranslatorRegistrationValidationHostedService that runs once at host startup. It calls
          TranslatorRegistrationValidator for each registration (advisory Warning log via the
          real ILogger<T> from the host's DI container, no exception) when no consumer for TNew
          is registered in this service.
        — ANTI-PATTERN FIX (P-130): Build() does NOT call Services.BuildServiceProvider() for
          validation. It applies the captured Action<MessagingOptions>? delegate inline to a
          local MessagingOptions instance, then calls MessagingOptionsValidator.Validate().
          No second root IServiceProvider is created. KebabCaseEndpointNameFormatter prefix
          is populated from the captured/validated ServiceName.
        — DEFERRED VALIDATION PATH: When MessagingOptions was configured via
          services.Configure<T>(config.GetSection(...)) with no inline action, Build() does not
          throw — it defers ServiceName validation to startup ValidateOnStart() and logs a warning
          that the naming convention prefix cannot be validated at build time.

    NOTE: Exactly one transport (.UseRabbitMq or .UseAzureServiceBus) must be called before .Build() —
          .Build() throws InvalidOperationException if no transport is configured.
          Multiple .AddConsumer<T>() and .AddBatchConsumer<T>() calls are additive.
          All fluent methods return MessagingBusBuilder for chaining.
          No outbox, retry, circuit-breaker, scheduler, or transport-specific wiring is done until
          .Build() is called.
```

---

## Implementation Rules

### Queued capabilities (WO-021 — task-planned, pending implementation dispatch)

The following capabilities have full task rows in `07.Messaging/state-map.md` and are documented in the Interface Contracts section above. Domain implementers must not implement these ahead of phase dispatch — phase dispatch is managed by the arch-lead. All contracts below are finalized and authoritative.

| Capability | Root Phase | Phase Key | Implementor Package(s) |
| --- | --- | --- | --- |
| Circuit Breaker + Fault Consumers | P-125, P-126 | `SK.07.Resilience` | Abstractions (`IFaultConsumer<T>`, `FaultExceptionInfo`, `CircuitBreakerOptions`) + MassTransit (`FaultConsumerAdapter`, `WithCircuitBreaker`, `AddFaultConsumer`) |
| Deferred Message Scheduling | P-127 | `SK.07.Scheduling` | Abstractions (`IMessageScheduler`, `SchedulingOptions`, `QuartzSchedulerOptions`) + MassTransit (`MassTransitMessageScheduler`, `WithInMemoryScheduler`, `WithQuartzScheduler`) |
| Saga State Machine Support | P-128 | `SK.07.Saga` | MassTransit (`SagaStateBase`, `SagaStateMachineBase<TSaga>`, `AddSaga`, `WithEntityFrameworkSagaRepository`) |
| Batch Consumer Support | P-129 | `SK.07.Batch` | Abstractions (`BatchOptions`) + MassTransit (`BatchConsumerBase<TMessage>`, `AddBatchConsumer`) |
| Build() Anti-Pattern Fix | P-130 | `SK.07.Core` | MassTransit (`MessagingBusBuilder.Build()` — remove BuildServiceProvider call) |
| Cross-Service Command Routing | P-131 | `SK.07.Routing` | Abstractions (`ISendEndpointResolver`) + MassTransit (`ConventionSendEndpointResolver`, `WithSendEndpointRoute<T>`) |
| Idempotency Abstraction | P-134 | `SK.07.Idempotency` | Abstractions (`IIdempotencyStore`, `IdempotencyOptions`) + MassTransit (`IdempotentConsumerBehavior<TMessage>`, `WithIdempotency`) |
| Header Propagation | P-135 | `SK.07.HeaderPropagation` | Abstractions (`IMessageHeaderPropagator`) + MassTransit (`WithHeaderPropagator<T>`, propagator invocation in bus/publisher, header extraction in `ConsumerBase`) |
| Per-Consumer Definition Base | P-136 | `SK.07.ConsumerDefinition` | MassTransit (`ConsumerDefinitionBase<TConsumer>`) |
| Message Schema Evolution | P-137 | `SK.07.VersionTranslation` | Abstractions (`IMessageVersionTranslator<TOld, TNew>`) + MassTransit (`WithVersionTranslator`, `TranslatorRegistrationValidator`) |
| Routing Slip Activity Base | P-139 | `SK.07.RoutingSlip` | Abstractions (`IRoutingSlipBuilder`, `IMessageBus.ExecuteRoutingSlipAsync`) + MassTransit (`RoutingSlipActivityBase<TArguments, TLog>`, `MassTransitRoutingSlipBuilder`, `AddRoutingSlipActivity`) |
| ActivitySource and Consume/Publish Instrumentation | P-172 | `SK.07.OTel` | MassTransit (`MessagingDiagnostics.ActivitySource`, `ConsumerBase<TMessage>.Consume()` and `MassTransitEventPublisher.PublishAsync<TEvent>()` instrumentation) |

---

### Hard violations (never do these)

- Injecting `IBus`, `IPublishEndpoint`, or `ISendEndpointProvider` from MassTransit directly in application handlers, command/query handlers, domain services, or any type outside `07.Messaging` infrastructure — use `IMessageBus` or `IEventPublisher` exclusively.
- Publishing domain events via `IEventPublisher` or `IMessageBus` — domain events (`IDomainEvent`) are dispatched internally by `IDomainEventDispatcher` (from `03.Domain`); only integration events (`IIntegrationEvent` with an `[IntegrationEvent]` attribute, `04.Contracts`) cross service boundaries via `IEventPublisher`, whose constraint rejects a domain event at compile time. The boundary is: domain event fires domain handlers, a domain handler maps to an integration event, the integration event is published via `IEventPublisher`.
- Calling `IMessageBus` or `IEventPublisher` from domain entities, value objects, `AggregateRoot<TId>`, or any type in the `03.Domain` package — messaging concerns must never leak into the domain layer.
- Defining `OutboxMessage`, `IOutboxWriter`, `OutboxInterceptor`, or any outbox-related type anywhere in `06.Persistence` — outbox infrastructure is owned by MassTransit in `07.Messaging`.
- Swallowing exceptions inside `ConsumerBase<TMessage>.ConsumeAsync` implementations — unhandled exceptions signal MassTransit to activate retry and fault policies. Silently swallowed exceptions cause silent message loss.
- Calling `IBusControl.StartAsync` or `IBusControl.StopAsync` manually — MassTransit's `IHostedService` manages the bus lifecycle; manual lifecycle management bypasses K8s-aware graceful drain.
- Configuring the MassTransit bus directly (`AddMassTransit(x => x.UsingRabbitMq(...))`) outside of `MessagingBusBuilder` in consuming services — all bus configuration must flow through `AddSharedKernelMessaging()`.
- Sending to a hardcoded queue address string via `ISendEndpointProvider.GetSendEndpoint(new Uri("queue:my-queue"))` — hardcoded addresses bypass convention-based routing and break across environments.
- Registering `IMessageBus` or `IEventPublisher` as singleton — both must be scoped; singleton lifetime breaks MassTransit's per-consume-scope semantics.
- Calling `RequestAsync<TRequest, TResponse>` with `CancellationToken.None` — this hangs indefinitely if the responder is unavailable; always pass a timeout-bound cancellation token.
- Calling `.WithEntityFrameworkOutbox<TDbContext>()` without running the required EF migrations — the outbox tables must exist before the bus starts or the delivery worker throws at startup.
- Placing transport credentials in `appsettings.json` files committed to source control — source credentials from environment variables, Kubernetes Secrets, or Azure Key Vault mappings only.
- Adding a project reference from `SharedKernel.Messaging.MassTransit` to `SharedKernel.Persistence.EfCore` or any `06.Persistence.*` package — the outbox is wired via generic type parameter `TDbContext`; no compile-time reference to the persistence package is needed or permitted.
- Adding domain logic to any type in this domain — this layer is pure messaging infrastructure.
- Any static mutable state. **Exception (P-172, extended P-348):** a static `readonly ActivitySource` and a static `readonly Meter` (plus its `Counter<long>`/`Histogram<double>` instrument fields) are the platform-standard .NET diagnostics pattern — they carry no mutable business state and the BCL diagnostics API is explicitly designed around process-lifetime static instrument instances (the same shape as a static logger category). `MessagingDiagnostics.ActivitySource` and `MessagingDiagnostics.Meter` (with its five instrument fields) are the only sanctioned static fields in this domain; do not add additional ad-hoc static fields under cover of this exception.
- Creating an `ActivitySource` or custom `Meter` in `13.ServiceDefaults` on behalf of `07.Messaging` — both are owned and constructed here (`MessagingDiagnostics.ActivitySource`/`.Meter`, P-172/P-348); `13.ServiceDefaults` only registers the already-existing source/meter names with the host's `TracerProvider`/`MeterProvider` via `WithMessagingTelemetry()` (P-132). This was a latent cross-domain phase violation discovered during P-132 review — P-132 incorrectly assumed this source already existed.
- Calling `Services.BuildServiceProvider()` inside `MessagingBusBuilder.Build()` for validation purposes — this creates a second root `IServiceProvider`, double-registers singletons, and silently discards scoped service state (see P-130 for the fix). Validation of `MessagingOptions.ServiceName` at build time must use the captured `Action<MessagingOptions>?` delegate directly.
- Authoring a production log statement in `SharedKernel.Messaging.MassTransit` via a hand-written `LoggerMessage.Define<>()` static delegate or a direct `ILogger.LogXxx()` extension-method call — always use the `[LoggerMessage]` source-generated partial-method pattern with an explicit `EventId` inside this domain's reserved `7000-7999` range (see the EventId allocation table above); enforced by `00.Governance`'s SK0020/SK0021 analyzer (P-250, added in P-254).
- Hand-building an ad hoc `Dictionary<string, object?>` for `ILogger.BeginScope` inside `ConsumerBase<TMessage>`, `BatchConsumerBase<TMessage>`, `FaultConsumerAdapter<TMessage,TFaultConsumer>`, or `RoutingSlipActivityBase<TArguments,TLog>` instead of seeding it via `MessagingLogScope.Create(correlationId)` — this is exactly how the domain accumulated three internal `EventId` collisions and a `CorrelationId`-scope shape that silently drifted out of sync across four independently-authored base types (added in P-254).
- Injecting `MassTransit.IMessageScheduler` directly in application handlers — use `SharedKernel.Messaging.Abstractions.IMessageScheduler` (added in P-127).
- Registering `IFaultConsumer<T>` via `services.AddScoped` — fault consumers must be registered via `MessagingBusBuilder.AddFaultConsumer<TMessage,TConsumer>()` (added in P-126).
- Registering `BatchConsumerBase<T>` subclasses via `AddConsumer<T>()` — batch consumers must be registered via `AddBatchConsumer<T>()` to apply batch configuration (added in P-129).
- Configuring circuit breakers per-consumer via `IConsumerDefinition<T>` — circuit breaker policy is global and must be configured via `WithCircuitBreaker()` on `MessagingBusBuilder`; per-consumer circuit-breaker overrides defeat the purpose of a shared failure-count window (added in P-126).
- Using `Task.Delay` inside consumers as a substitute for deferred delivery — this blocks thread-pool threads, cannot survive process restarts, and loses the message on crash; use `IMessageScheduler.ScheduleAsync` instead (added in P-127).
- Hardcoding queue names in `WithSendEndpointRoute<T>()` without namespacing by target service — the queue name must follow the `{target-service-name}-{command-type}` kebab-case convention matching the target service's `MessagingOptions.ServiceName` prefix (added in P-131).
- Implementing custom deduplication logic inside `ConsumeAsync` bodies (e.g., checking a local `HashSet<Guid>` or querying the database on every message) — the platform-standard deduplication mechanism is `WithIdempotency()` with a registered `IIdempotencyStore` implementation; ad-hoc in-consumer deduplication is non-standard and creates inconsistency across services (added in P-134).
- Calling `WithIdempotency()` on `MessagingBusBuilder` without first registering a concrete `IIdempotencyStore` — `Build()` throws `InvalidOperationException` at startup; SharedKernel does not provide a store implementation; the consuming service bridges to its own persistence layer (added in P-134).
- Implementing `IMessageHeaderPropagator` inside `SharedKernel.*` packages — propagators require access to service-specific ambient context (e.g., `IHttpContextAccessor`, tenant resolution, feature flag state) that does not exist in the SharedKernel; propagators belong in the consuming service's composition root (added in P-135). **Exception (P-345):** `AmbientCorrelationHeaderPropagator` (reads BCL `Activity.Current` — not service-specific) and `TenantHeaderPropagator` (reads the locally-owned `ITenantContextAccessor` seam, itself bridged by the consuming service) are the two, by-name-only, documented exceptions. Do not add a third ad hoc propagator inside `SharedKernel.*` under cover of this exception.
- Deriving an integration event's wire name, routing key or `messaging.event_type` tag from `typeof(TEvent).Name` — always `IntegrationEventDescriptor.For<TEvent>().Name` (the `[IntegrationEvent]` name). Construction outside `EventEnvelope.Wrap` needs no rule here: `EventEnvelope<TEvent>` has no public constructor or setter, so it no longer compiles.
- Skipping registered `IMessageHeaderPropagator` invocation on any of the four dispatch call shapes (`IMessageBus.PublishAsync`, `.SendAsync`, `.RequestAsync`, `IEventPublisher.PublishAsync`) — propagation must apply uniformly across all of them; a propagator silently applying to publish but not send/request is a confirmed prior defect, not an acceptable variance (added in P-341).
- Making `PayloadTransformOptions`' publish-side compress-then-encrypt / consume-side decrypt-then-decompress ordering caller-configurable — the order is fixed platform-wide to match `01.Core`'s established compress-then-encrypt convention; reversing it wastes CPU compressing high-entropy ciphertext for no size benefit (added in P-346).
- Introducing a new bespoke compression or cryptographic primitive inside `07.Messaging` for payload transform — always build on `01.Core`'s existing `IPayloadCompressor` (`SharedKernel.Compression`) and `ISymmetricEncryptionService` (`SharedKernel.Cryptography`) (added in P-346).
- Implementing `IMessageBusProbe` by constructing an independent, second transport connection from separately supplied configuration — the probe must query the real, already-registered `IBusControl`/`IBus` instance `MessagingBusBuilder` builds for the consuming service, otherwise a passing health check does not prove the service's actual bus connection is healthy (added in P-347).
- Adding an `IHealthCheck` implementation inside `07.Messaging` — this domain ships the `IMessageBusProbe` primitive only; wiring into `AddHealthChecks()` is `13.ServiceDefaults`'s concern (added in P-347).
- Inventing a platform-specific substitute for ordered delivery (e.g., an in-process sequencing buffer, a custom sequence-number header consumers must manually check) instead of mapping `PublishContext.PartitionKey` to each transport's genuine native ordered-delivery mechanism (RabbitMQ routing-key affinity, Azure Service Bus session identity) — added in P-344.
- Hardcoding a dead-letter queue name or TTL value inline instead of going through `DeadLetterOptions`/`MessagingBusBuilder.WithDeadLetterPolicy()` — mirrors the existing hardcoded-queue-address prohibition (added in P-343).
- Overriding `IConsumerDefinition<TConsumer>.Configure` directly in a `ConsumerDefinitionBase<TConsumer>` subclass — the base class seals this method to guarantee that retry exception filter wiring always runs; subclasses must implement `ConfigureConsumer` instead (added in P-136).
- Implementing `IMessageVersionTranslator<TOld, TNew>.Translate` with I/O, external service calls, or side effects — translation is called in the deserialization pipeline and must be a synchronous pure function; any async or stateful translation is a hard violation (added in P-137).
- Constructing `MassTransit.RoutingSlipBuilder` directly in application code — use `IRoutingSlipBuilder` (from `SharedKernel.Messaging.Abstractions`) so application code has no compile-time dependency on MassTransit types; pass the `IRoutingSlipBuilder.Build()` result to `IMessageBus.ExecuteRoutingSlipAsync` (added in P-139).
- Using `RoutingSlipActivityBase<TArguments, TLog>` for workflows requiring durable state persistence across process restarts — routing slips are stateless; use `SagaStateMachineBase<TSaga>` with an EF Core saga repository when persistent state is required (added in P-139).
- Overriding MassTransit `Execute(ExecuteContext<TArguments>)` or `Compensate(CompensateContext<TLog>)` directly on a `RoutingSlipActivityBase<TArguments, TLog>` subclass — override `ExecuteAsync` and `CompensateAsync` only; the base class seals the entry points for consistent correlation propagation and structured logging (added in P-139).
- Calling `ISymmetricEncryptionService.EncryptAsync`/`DecryptAsync` from `PayloadTransformMessageSerializer`/`PayloadTransformMessageDeserializer` — **this is structurally impossible, not merely discouraged**: `IMessageSerializer.GetMessageBody<T>`/`IMessageDeserializer.Deserialize` are hard-synchronous MassTransit interface members with no async overload anywhere in this pipeline stage (confirmed by reflection against `MassTransit.Abstractions` 9.1.2). The payload-transform trio must always call the sync `Encrypt`/`Decrypt` members, now AAD-aware per `01.Core`'s `ISynchronousEncryptionKeyProvider` gate (added in P-499/WO-081).
- Enabling `PayloadTransformOptions.EnableEncryption` against a KMS/HSM-backed `IEncryptionKeyProvider` (any provider not marked `ISynchronousEncryptionKeyProvider`) — since the payload-transform trio can never call the `*Async` overloads (see above), such a provider fails every encrypt/decrypt call with a structural `NotSupportedException` (`01.Core`'s `SK.01.P492` gate). Payload-transform encryption is scoped to the synchronous, config/environment-backed key provider only; there is no supported path to a remote-KMS-backed key for this feature (added in P-499/WO-081).
- Persisting or logging the `PayloadTransformHeaders.MessageTypeAad` header value as anything other than a plaintext transport header set at publish time and read at consume time before decryption — it is deliberately unencrypted (message-type identity is not secret; it is already visible via exchange/routing-key topology on most transports) and must never be packed into `EncryptedPayload`/`EncryptedPayloadWireCodec`'s own wire format, mirroring `01.Core`'s "AAD is authenticated but never encrypted, and never persisted" rule (added in P-499/WO-081).
- Treating a decrypt failure caused by a missing `PayloadTransformHeaders.MessageTypeAad` header (a message from a producer that has not yet migrated to this phase) as anything other than the documented rolling-deploy fallback path (`Array.Empty<byte>()` AAD) — do not add a silent retry-with-different-AAD loop or a configurable AAD scheme; the fallback is fixed and one-directional (old producer → new consumer only), never caller-configurable (added in P-499/WO-081).

### MassTransit 9.x API notes (discovered during Core implementation)

- **`PublishContext` name conflict:** `MassTransit.PublishContext` clashes with `SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext` inside the MassTransit package. Always add `using MessagingPublishContext = SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext;` at the top of any file in `SharedKernel.Messaging.MassTransit` that references both types.
- **`MassTransitEventPublisher` needs no constraint bridge:** `IEventPublisher.PublishAsync<TEvent>` and `EventEnvelope<TEvent>` share the same `where TEvent : class, IIntegrationEvent` constraint, so the publisher calls `EventEnvelope.Wrap` directly — no runtime type check, no `MakeGenericMethod`, no delegate cache.
- **`RequestAsync` implementation:** `IClientFactory.CreateRequestClient<TRequest>(CancellationToken)` does not exist in MassTransit 9.x. Use `IServiceProvider.CreateRequestClient<TRequest>()` (`MassTransit.RequestClientExtensions`), inject `IServiceProvider` into `MassTransitMessageBus`, then pass the caller `CancellationToken` to `GetResponse<TResponse>`.
- **Azure Service Bus `TransportType`:** `IServiceBusBusFactoryConfigurator` has no `TransportType`. Set it on `IServiceBusHostConfigurator` via `cfg.Host(uri, h => { h.TransportType = opts.TransportType; })`.
- **Azure Service Bus managed identity host URI:** `new Uri($"sb://{opts.FullyQualifiedNamespace}")` with `h.TokenCredential = new DefaultAzureCredential()` on the host configurator. `Host(string, Action<IServiceBusHostConfigurator>)` accepts connection string or `sb://` URI.
- **`BindConfiguration` not available:** `Microsoft.Extensions.Options.ConfigurationExtensions` is not in the transitive closure. Do not call `optionsBuilder.BindConfiguration(...)`. Consumers bind `MessagingOptions` from configuration independently: `services.Configure<MessagingOptions>(config.GetSection(MessagingOptions.SectionName))`.
- **`AddSharedKernelMessaging` and `MessagingBusBuilder` live in MassTransit package:** `SharedKernel.Messaging.Abstractions` is a pure interface library — zero DI extensions. The extension method and builder are in `SharedKernel.Messaging.MassTransit.Extensions`.
- **`MessagingOptionsValidator` lives in MassTransit package:** Abstractions cannot reference `Microsoft.Extensions.Options`; the `IValidateOptions<MessagingOptions>` implementation (`MessagingOptionsValidator`) is in `SharedKernel.Messaging.MassTransit.Extensions`.
- **`ConsumerBase.Consume` is not `sealed`:** Implements `IConsumer<TMessage>.Consume` — interface implementation, not a virtual override. `sealed` keyword does not apply. Use XML doc to direct subclasses to override `ConsumeAsync` only.
- **`IMessageScheduler` name collision:** Both `MassTransit.IMessageScheduler` and `SharedKernel.Messaging.Abstractions.Scheduling.IMessageScheduler` resolve in `MessagingBusBuilder.cs`. Fix: alias `using MtScheduler = MassTransit.IMessageScheduler;` in `MassTransitMessageScheduler.cs`; use fully qualified `SharedKernel.Messaging.Abstractions.Scheduling.IMessageScheduler` in `Services.AddScoped<>()` inside `Build()`.
- **`IMessageScheduler.SchedulePublish` returns `Task<ScheduledMessage<T>>`:** The returned object exposes `.TokenId` (Guid) via the base `ScheduledMessage` type. MassTransit uses `DateTime` not `DateTimeOffset` — convert via `.UtcDateTime`.
- **`CancelScheduledPublish` non-generic overload:** `MassTransit.IMessageScheduler.CancelScheduledPublish(Type, Guid, CancellationToken)` is the correct cancel API; avoids needing the generic type parameter at call time. Used by `MassTransitMessageScheduler.CancelAsync` via the stored token→Type map.
- **Obsolete scheduler APIs in MassTransit 9.x:** `UseInMemoryScheduler(IBusFactoryConfigurator, IBusRegistrationContext, string)` from `MassTransit.QuartzIntegration` is obsolete — do not use. `UseDelayedMessageScheduler()` on an ASB bus configurator is obsolete — use `UseServiceBusMessageScheduler()` instead.
- **Quartz durable scheduler wiring:** `cfg.AddQuartzConsumers(o => o.QueueName = queueName)` + `cfg.AddMessageScheduler(new Uri($"queue:{queueName}"))` for registration; `busCfg.UseMessageScheduler(new Uri($"queue:{queueName}"))` on the bus factory configurator. The `queueName` defaults to `QuartzSchedulerOptions.Schema` ("quartz").
- **Scoped `MassTransit.IMessageScheduler` in tests:** MassTransit registers `IMessageScheduler` as scoped. In tests, always resolve from a child scope: `using var scope = provider.CreateScope(); scope.ServiceProvider.GetRequiredService<global::MassTransit.IMessageScheduler>()`. Resolving from the root provider throws.
- **`KebabCaseEndpointNameFormatter.SanitizeName` is an instance method:** Call via `KebabCaseEndpointNameFormatter.Instance.SanitizeName(typeof(T).Name)` — it is NOT a static method. Calling `KebabCaseEndpointNameFormatter.SanitizeName(...)` directly causes CS0120 compile error.
- **Tests bypassing `MessagingBusBuilder.Build()` must register routing deps manually:** `MassTransitMessageBus` now requires `IReadOnlyDictionary<Type, string>` (route map) and `ConventionSendEndpointResolver` via constructor injection. Any test that registers `MassTransitMessageBus` directly (e.g. via `services.AddScoped<IMessageBus, MassTransitMessageBus>()`) must also register: `services.AddSingleton<IReadOnlyDictionary<Type, string>>(new ReadOnlyDictionary<Type, string>(new Dictionary<Type, string>()))` and `services.AddScoped<ConventionSendEndpointResolver>()`. Tests going through `MessagingBusBuilder.Build()` get these automatically.
- **Global consume pipeline filter wiring (idempotency, future cross-cutting filters):** `IBusFactoryConfigurator` implements `IConsumePipeConfigurator`. To apply an open-generic `IFilter<ConsumeContext<TMessage>>` to ALL consumers globally, call `busCfg.UseConsumeFilter(typeof(MyFilter<>), ctx)` where `typeof(MyFilter<>)` is the open generic type and `ctx` is the `IBusRegistrationContext`. MassTransit 9.x resolves the closed generic (e.g., `MyFilter<OrderPlacedEvent>`) from DI per message type at runtime. The filter must be registered as an open generic: `services.AddScoped(typeof(MyFilter<>))`. Tests wiring the filter directly (bypassing `MessagingBusBuilder`) must call `AddScoped<MyFilter<ConcreteMessageType>>()` (closed generic) alongside `AddMassTransitTestHarness` and then call `busCfg.UseConsumeFilter(typeof(MyFilter<>), ctx)` inside the `UsingInMemory` configurator.
- **`IEnumerable<IMessageHeaderPropagator>` resolution strategy:** `MassTransitEventPublisher` injects `IEnumerable<IMessageHeaderPropagator>` via constructor (always non-null — DI returns empty enumerable when none registered). `MassTransitMessageBus` resolves via `IServiceProvider.GetService<IEnumerable<IMessageHeaderPropagator>>()` at call time (also always non-null). Both approaches are safe; constructor injection is preferred when the dependency is always needed.
- **`ConsumeContext.Headers.GetAll()` for header iteration:** In MassTransit 9.x, iterate all message headers via `context.Headers.GetAll()` which returns `IEnumerable<KeyValuePair<string, object?>>`. Do not use `context.Headers` as `IDictionary` — it does not implement that interface. Used in `ConsumerBase.Consume` to extract `x-sk-*` headers into the log scope.
- **`PublishContext` alias required in test files:** Test files that import both `MassTransit` (via `MassTransit.Testing`) and `SharedKernel.Messaging.Abstractions.HeaderPropagation` must add `using MessagingPublishContext = SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext;` to resolve the `PublishContext` ambiguity, same as in production code files.
- **Version translation pattern (P-137) — "translating consumer", not a deserializer hook:** MassTransit 9.x has no documented, stable cross-type (`TOld` → `TNew`) message-alias/deserializer hook (`MassTransit.ITransformConfigurator<T>` only transforms properties of the *same* type `T`). `WithVersionTranslator<TOld, TNew, TTranslator>()` instead registers an internal `VersionTranslatingConsumer<TOld, TNew> : IConsumer<TOld>` that resolves `IMessageVersionTranslator<TOld, TNew>` (singleton), calls `Translate()` synchronously, and republishes via `ConsumeContext.Publish<TNew>(translated, ct)`. Consumers registered for `TNew` receive the translated payload as if `TNew` had been published directly. This is fully testable with `MassTransit.Testing.TestHarness` (`UsingInMemory`).
- **Advisory startup validation registered as `IHostedService`, not at `Build()` time:** `TranslatorRegistrationValidator.Validate(...)` needs an `ILogger`, but `Build()` must not call `Services.BuildServiceProvider()` (P-130). Solution: `Build()` registers a singleton `TranslatorRegistrationValidationHostedService` (only when `WithVersionTranslator` was called) that resolves `ILogger<TranslatorRegistrationValidationHostedService>` from the real host DI container and runs the validator once in `StartAsync`. This pattern (defer DI-dependent advisory checks to a startup `IHostedService`) is the template for any future `Build()`-time advisory validation that needs a real `ILogger` or other scoped/DI-resolved dependency.
- **Tracking registered consumer types for advisory checks:** `MessagingBusBuilder` maintains `_registeredConsumerTypes: List<Type>` populated by `AddConsumer<TConsumer>()`, `AddConsumer<TConsumer, TConsumerDefinition>()`, and `AddBatchConsumer<TConsumer>()`. `TranslatorRegistrationValidator.HasConsumerFor` reflects over `consumerType.GetInterfaces()` checking for `IConsumer<TNew>` or `IConsumer<Batch<TNew>>`.
- **`IRequestClient<TRequest>.GetResponse` header/CorrelationId configuration (P-341/WO-054):** unlike `IPublishEndpoint.Publish`/`ISendEndpoint.Send`, which accept a raw `Action<PublishContext<T>>`/`Action<SendContext<T>>` pipe callback directly, `IRequestClient<TRequest>.GetResponse<TResponse>` only exposes a `RequestPipeConfiguratorCallback<TRequest>` overload — `Task<Response<TResponse>> GetResponse<TResponse>(TRequest message, RequestPipeConfiguratorCallback<TRequest> callback, CancellationToken cancellationToken, RequestTimeout timeout)`, where the callback receives an `IRequestPipeConfigurator<TRequest>`. That configurator does not expose `CorrelationId`/`Headers` directly — it implements `IPipeConfigurator<SendContext<TRequest>>` (via `AddPipeSpecification`). Reach the underlying `SendContext<TRequest>` (which does have settable `CorrelationId` and a `Headers` with `.Set(key, value)`, identical shape to the `Send`/`Publish` pipe) via the `MassTransit.DelegateConfigurationExtensions.UseExecute<TContext>(this IPipeConfigurator<TContext> configurator, Action<TContext> callback)` extension method: `client.GetResponse<TResponse>(request, cfg => cfg.UseExecute(sendContext => { sendContext.CorrelationId = ...; sendContext.Headers.Set(...); }), ct)`.
- **`IServiceBusEndpointConfigurator.MaxConcurrentCalls` is obsolete in MassTransit 9.1.2 (P-342/WO-054):** compiles with `CS0618` ("Set ConcurrentMessageLimit instead (which is exactly what setting this property does)"). The correct, current API is the core `IBusFactoryConfigurator.ConcurrentMessageLimit` (`int?`) — the SAME property RabbitMQ's `IBusFactoryConfigurator` inherits, meaning both transports now share one bus-level concurrency-default property. `IServiceBusBusFactoryConfigurator` inherits `ConcurrentMessageLimit` transitively via `IBusFactoryConfigurator`, so `cfg.ConcurrentMessageLimit = opts.MaxConcurrentCalls;` works directly on the ASB bus configurator with no cast needed.
- **`IBusFactoryConfigurator.ConcurrentMessageLimit`/`.PrefetchCount` are write-only on the bus-level configurator (P-342/WO-054):** both properties compile for assignment (`cfg.ConcurrentMessageLimit = 5;`) but reading them back (`cfg.ConcurrentMessageLimit`) fails with `CS0154` ("lacks the get accessor") — the interface declares a setter only, no getter. This differs from the per-endpoint `IReceiveEndpointConfigurator.ConcurrentMessageLimit`/`.PrefetchCount`, which have both accessors. Tests asserting bus-level wiring must use NSubstitute's `substitute.Received(1).ConcurrentMessageLimit = expectedValue;` setter-call assertion — reading the property back is a compile error, not a runtime one.
- **Confirming exact MassTransit interface member/accessor shapes without guessing:** when an interface member's accessor shape (get-only, set-only, or both) or exact declaring interface in a deep inheritance chain (e.g., where `PrefetchCount` actually lives across `IBusFactoryConfigurator`/`IReceiveEndpointConfigurator`/`IRabbitMqQueueConfigurator`/`IServiceBusEndpointConfigurator`) is not obvious from XML docs, `dotnet build` on a throwaway usage is the fastest ground truth — the compiler error message (`CS0154`/`CS0618`/etc.) states the exact declaring type and accessor. A `System.Reflection`-based throwaway console app against the installed NuGet-cached DLLs (`~/.nuget/packages/masstransit*/9.1.2/lib/net10.0/*.dll`) is the fallback for questions a compile error alone cannot answer (e.g., "does this member exist on this interface at all", full inheritance chains) — register an `AppDomain.CurrentDomain.AssemblyResolve` handler that searches the NuGet cache by simple assembly name to resolve transitive dependencies (e.g., `Azure.Messaging.ServiceBus`) that `Assembly.LoadFrom` alone won't pull in.
- **RabbitMQ fault/dead-letter queue naming is not publicly renameable in MassTransit 9.1.2 (P-343/WO-054):** the full public configuration surface reachable from `IRabbitMqBusFactoryConfigurator` (`IRabbitMqSendTopologyConfigurator.ConfigureErrorSettings`/`.ConfigureDeadLetterSettings`, both typed `Action<IRabbitMqQueueBindingConfigurator>`) lets a caller configure the *arguments* of the queue MassTransit is about to declare (`SetQueueArgument`, `QueueExpiration`, `Lazy`, `SingleActiveConsumer`, `SetQuorumQueue`, `EnablePriority`, plus the underlying `IRabbitMqExchangeConfigurator` members) but never exposes a settable name/formatter — confirmed by exhaustively reflecting `IRabbitMqQueueConfigurator`/`IRabbitMqQueueBindingConfigurator`/`IRabbitMqQueueEndpointConfigurator`/`IRabbitMqExchangeConfigurator`/`IRabbitMqExchangeBindingConfigurator` (none has a `Name`/settable-string member) and by dumping `MassTransit.RabbitMqTransport.dll`'s IL user-string heap directly (via `System.Reflection.Metadata.MetadataReader`), which contains the literal constants `"_error"` and `"_skipped"` (and `"input_queue_error"`/`"input_queue_skipped"`, the bus's own default-endpoint-name-derived examples) — the fixed suffixes are baked into the assembly, not computed from a configurable format string. `IRabbitMqReceiveEndpointConfigurator.BindDeadLetterQueue(exchangeName, queueName, configure)` *does* take an explicit name, but it configures RabbitMQ's native `x-dead-letter-exchange` (NACK/TTL-expiry-triggered) mechanism, which a MassTransit consumer exception never engages — MassTransit's own fault pipeline republishes the message to its internal error transport and ACKs the original delivery, bypassing the broker's native dead-lettering entirely, so `BindDeadLetterQueue` cannot substitute for a real rename here. `MessageTimeToLive`, in contrast, maps correctly onto `x-message-ttl` via `SetQueueArgument(string, TimeSpan)`, and `ConfigureErrorSettings`/`ConfigureDeadLetterSettings` are confirmed (by method-name correspondence with `RabbitMqReceiveEndpointBuilder.CreateErrorTransport()`/`.CreateDeadLetterTransport()`, which call `IRabbitMqSendTopology.GetErrorSettings`/`.GetDeadLetterSettings`) to configure the SAME queues a retry-exhausted message is actually routed to — this is the real, correct wiring point for TTL, just not for renaming.
- **NSubstitute `Arg.Do<T>` capture on a property setter must be wired BEFORE the exercise call, not inside a later `Received()` assertion (P-343/WO-054):** `substitute.SomeSetOnlyProperty = Arg.Do<T>(x => captured = x);` configures a trigger that fires the next time the setter is actually invoked — placing this line *after* calling the method under test and wrapping it in `substitute.Received(1).SomeSetOnlyProperty = Arg.Do<T>(...)` compiles but the callback never fires (`captured` stays `null`), because `Received()` verifies against call history rather than re-invoking `Arg.Do` triggers retroactively. This differs from asserting a *known* value via `substitute.Received(1).Property = 25;` (works fine after the fact, as in `ConcurrencyLimitConfigurationTests`, P-342) — `Arg.Do` capture specifically needs to be armed ahead of time.
- **`ServiceCollection.GetServices<IHostedService>()` against a real broker requires `.AddLogging()` (P-343/WO-054):** MassTransit registers its own default health-check `IHostedService` (`DefaultHealthCheckService`), which constructor-injects `ILogger<T>`. A test `ServiceCollection` that never calls `.AddLogging()` throws `InvalidOperationException` ("Unable to resolve service for type ILogger<...>") the moment `GetServices<IHostedService>()` tries to construct it — this reproduces identically in the pre-existing `RabbitMqIntegrationTests.cs` (a known, documented, unrelated gap predating this phase). Calling `services.AddLogging();` before `AddSharedKernelMessaging(...)` resolves it; `RabbitMqIntegrationTests.cs` itself was left unmodified — fixing a pre-existing, out-of-scope test is not this phase's job, but the fix is now documented here for the next session that touches broker-backed integration tests.
- **Custom `ISerializerFactory` registration requires BOTH `ClearSerialization()` and an explicit `AddDeserializer(..., isDefault: true)` call, not just `AddSerializer(factory, isSerializer: true)` alone (P-346/WO-054):** `IBusFactoryConfigurator.AddSerializer(ISerializerFactory, bool isSerializer)` alone only changes which serializer *produces* outgoing messages — MassTransit's own already-registered default deserializer for the same content type remains active on the *receive* side, because serializer/deserializer registration inside `MassTransit.Configuration.SerializationConfiguration` is additive-by-content-type, not overwrite-by-content-type, and the bus's pre-existing default lives in a separate configuration-chain source (`_source`) that a bare `AddSerializer` call does not touch. Confirmed empirically during implementation: omitting `ClearSerialization()` produced a live, reproducible `System.Runtime.Serialization.SerializationException: An error occured while deserializing the message envelope` → `System.Text.Json.JsonException: '0x00' is an invalid start of a value` — MassTransit's own untouched default `SystemTextJsonMessageSerializer.Deserialize(ReceiveContext)` was still being invoked on the receive side, choking on the genuinely-transformed (compressed+encrypted) bytes the custom serializer had correctly produced on the send side. The fix is two calls, in this order, before `ConfigureEndpoints`: `busCfg.ClearSerialization();` then `busCfg.AddSerializer(factory, isSerializer: true); busCfg.AddDeserializer(factory, isDefault: true);` — `AddSerializer` alone (even after `ClearSerialization()`) still produced a second, different `MassTransit.ConfigurationException: No default content type specified and more than one deserializer was configured` until the explicit `AddDeserializer(..., isDefault: true)` call set `SerializationConfiguration`'s internal default-content-type flag directly. This ordering/pairing is the correct pattern for *any* future custom `ISerializerFactory` registration in this domain, not just payload transform.
- **`IMessageDeserializer.Deserialize(ReceiveContext)` — the entry point MassTransit's receive pipeline actually calls, not the `(MessageBody, Headers, Uri)` overload directly:** confirmed by reading MassTransit's own shipped `SystemTextJsonMessageSerializer.cs` source — `Deserialize(ReceiveContext receiveContext)` is implemented as `return new BodyConsumeContext(receiveContext, Deserialize(receiveContext.Body, receiveContext.TransportHeaders, receiveContext.InputAddress));`. `MassTransit.Serialization.BodyConsumeContext` is a public sealed class with a public `ctor(ReceiveContext, SerializerContext)` — the correct, real-type way for a custom `IMessageDeserializer` to implement the `ReceiveContext` overload, never a hand-rolled `ConsumeContext` substitute. A custom deserializer that implements only the 3-arg overload and leaves `Deserialize(ReceiveContext)` unimplemented (or implemented incorrectly) never gets invoked by the real receive pipeline at all.
- **MassTransit's own bus-health surface is `MassTransit.Monitoring.BusHealthCheck` (P-347/WO-054), constructed from `MassTransit.Transports.IBusInstance`, not from `IBusControl`/`IBus` directly:** confirmed via reflection against the installed `MassTransit` 9.1.2 assembly that `BusHealthCheck` is a public sealed class implementing `Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck` with a single public `ctor(IBusInstance)` and one member, `CheckHealthAsync(HealthCheckContext, CancellationToken) → Task<HealthCheckResult>`. `IBusInstance` (exposing `.Bus`/`.BusControl`/`.HostConfiguration`) is registered as a DI singleton automatically by `AddMassTransit`/`AddMassTransitTestHarness` — the same instance `MassTransitMessageBus`'s own scoped MassTransit-managed proxies (`IPublishEndpoint`/`ISendEndpointProvider`) sit on top of. `BusHealthCheck` is MassTransit's own internal building block for its `services.AddHealthChecks()` integration (wired via `MassTransit.Monitoring.ConfigureBusHealthCheckServiceOptions`, an `IConfigureOptions<HealthCheckServiceOptions>` MassTransit registers automatically) — no separate "probe" API exists on `IBusControl`/`IBus` themselves; `MassTransitMessageBusProbe` reuses this internal type directly rather than reinventing bus-readiness logic.
- **`BusHealthCheck.CheckHealthAsync` throws `NullReferenceException` unless `HealthCheckContext.Registration` is set:** `new HealthCheckContext()` alone is insufficient — `CheckHealthAsync` internally reads `context.Registration.Name`. The fix is `new HealthCheckContext { Registration = new HealthCheckRegistration(name, healthCheck, failureStatus: null, tags: null) }`, confirmed empirically via a throwaway `dotnet test` probe (not `dotnet run` — merely constructing/calling `BusHealthCheck` does not itself hit MassTransit's real-bus license gate, since it operates on an already-realized `IBusInstance`, but resolving that `IBusInstance` from a plain `AddMassTransit`-configured `ServiceCollection` outside `AddMassTransitTestHarness` does — see the next bullet).
- **Resolving `IBusInstance` from DI eagerly builds the bus and hits MassTransit's real-bus license gate — even under `UsingInMemory`, even without ever calling `StartAsync`:** confirmed via a throwaway `dotnet run` console probe that `sp.GetServices<IBusInstance>()` (or any resolution that reaches `IBusInstance`'s registered factory) throws `MassTransit.ConfigurationException` ("License must be specified...") the instant it is resolved — `TransportRegistrationBusFactory<T>.CreateBus` validates the license synchronously as part of building the `IBusControl` object graph, before any transport connection is attempted and regardless of transport (RabbitMQ/ASB/InMemory all share this code path). `AddMassTransitTestHarness()`'s harness bus factory is exempt from this gate (the established `[[project_masstransit_ordered_delivery]]` finding, reconfirmed here for a fourth capability area). Practical consequence for `MassTransitMessageBusProbe` guard tests: assert singleton *registration* via the raw `ServiceDescriptor` (`services.SingleOrDefault(d => d.ServiceType == typeof(IMessageBusProbe))`) rather than actually resolving `IMessageBusProbe` through a plain `UseRabbitMq(...).Build()` + `BuildServiceProvider()` guard-test pipeline with no real broker/license configured — functional `ProbeAsync` behavior is proven instead via `AddMassTransitTestHarness()` in a dedicated harness test file.
- **`Headers` (MassTransit's transport-header contract) extends `IEnumerable<HeaderValue>` and its `Get<T>` overload pair uses C# 9+ unconstrained-nullable generics, not two `struct`/`class`-constrained overloads as reflection tooling may suggest:** confirmed by direct compiler feedback (not reflection alone) while writing a hand-rolled test double — the real interface is `T Get<T>(string key, T defaultValue) where T : class` plus `T? Get<T>(string key, T? defaultValue = null) where T : struct`, AND a full `IEnumerable<HeaderValue>` implementation (`GetEnumerator()` both generic and non-generic) is required to implement `Headers` directly. For test code needing a `Headers` instance, prefer MassTransit's own public, directly-constructible implementations over hand-rolling one — `MassTransit.Serialization.DictionarySendHeaders` (public parameterless `ctor()`) for an empty/mutable instance, or `MassTransit.Serialization.EmptyHeaders` (no public constructor found — reachable only via MassTransit-internal code paths, not a viable direct-construction target despite being a public type).
- **`IMessageSerializer`/`IMessageDeserializer` expose no async member anywhere in MassTransit 9.1.2 (P-499/WO-081):** confirmed by direct .NET reflection against the installed `MassTransit.Abstractions` 9.1.2 assembly — `IMessageSerializer.GetMessageBody<T>(SendContext<T>)` returns `MessageBody` (not `Task<MessageBody>`/`ValueTask<MessageBody>`), and `IMessageDeserializer.Deserialize(MessageBody, Headers, Uri?)`/`.Deserialize(ReceiveContext)` both return synchronously (`SerializerContext`/`ConsumeContext`). There is no async overload, no `Task`-returning variant, and no other public extensibility point in the send/receive pipeline that would let a custom serializer/deserializer perform awaited I/O. This is the reason `01.Core`'s WO-081 AAD/async-cryptography migration cannot make the payload-transform trio (P-346) call `EncryptAsync`/`DecryptAsync` — it must keep calling the sync `Encrypt`/`Decrypt` members and lean on `01.Core`'s `ISynchronousEncryptionKeyProvider` capability gate instead. Any future feature considering a hook into this same `ISerializerFactory`/`IMessageSerializer`/`IMessageDeserializer` extensibility point inherits this same hard synchronous constraint — it is a property of MassTransit's pipeline architecture, not of this domain's own code.
- **`SendContext<T>.Headers` (`SendHeaders.Set(string, string)`/`.Set(string, object, bool)`) is mutable at the point `IMessageSerializer.GetMessageBody<T>` runs, and `IMessageDeserializer.Deserialize`'s `Headers headers` parameter is the same transport-header channel `ReceiveContext.TransportHeaders` already forwards (P-499/WO-081):** confirmed by reflection against `MassTransit.Abstractions` 9.1.2 — `SendContext<T>.Headers` is a settable `SendHeaders` property (not a snapshot), and `IMessageDeserializer.Deserialize(MessageBody, Headers, Uri?)` receives the exact `Headers` instance `IMessageDeserializer.Deserialize(ReceiveContext)` forwards from `receiveContext.TransportHeaders` — the identical channel `SK.07.HeaderPropagation`/`SK.07.AmbientPropagation` already rely on for cross-cutting header propagation. This is the mechanism `PayloadTransformHeaders.MessageTypeAad` uses to carry the producer's AAD-source string to the consumer, since `IMessageDeserializer.Deserialize` has no generic `T` and cannot otherwise know which CLR type name the producer bound as associated data.
- **`MassTransit.Serialization.DictionarySendHeaders` implements BOTH `SendHeaders` and `Headers` on the same instance (P-499/WO-081), confirmed by reflection against the shipped `MassTransit.dll` 9.1.2 (not the `.Abstractions` assembly — this concrete type lives in the main package):** one `DictionarySendHeaders` instance can therefore serve as `SendContext<T>.Headers` for a publish-side call AND be passed directly as the `Headers headers` parameter to `IMessageDeserializer.Deserialize(MessageBody, Headers, Uri?)` for a paired consume-side call in the same test, genuinely proving the AAD header round-trips through the exact same object a real transport would carry it across — this is the technique `PayloadTransformAadTests` (PA-11/PA-12/PA-13) uses for a unit-level proof without needing a full `TestHarness`. `Headers.Get<T>(string key, T defaultValue)` (unconstrained generic, no `where T : class` on this particular overload as of 9.1.2) is the read-side counterpart to `SendHeaders.Set(string, string)` — `headers.Get<string>(PayloadTransformHeaders.MessageTypeAad, null)` is the correct call shape, returning `null` when the key is absent (never throwing), which is exactly the header-absent fallback path this feature depends on.
- **`SendContext<T>.SupportedMessageTypes` must be stubbed with `MassTransit.MessageUrn.ForTypeString<T>()` output, NOT a raw `typeof(T).FullName` string, or `SerializerContext.TryGetMessage<T>(out T)` returns `false` even though deserialization itself succeeded with no exception (P-499/WO-081):** confirmed via a failing `dotnet test` run — a hand-substituted `SendContext<T>.SupportedMessageTypes.Returns([typeof(T).FullName!])` produces a JSON envelope whose `messageType`/`MessageUrn` metadata does not match what `SerializerContext.TryGetMessage<T>` checks against, so the method returns `false` silently (no exception) rather than surfacing the mismatch loudly. The fix is `sendContext.SupportedMessageTypes.Returns([MassTransit.MessageUrn.ForTypeString<T>()])` (`MassTransit.MessageUrn.ForTypeString<T>()`/`.ForType<T>()`/`.ForType(Type)`/`.ForTypeString(Type)` are all public static members). Any future test hand-substituting `SendContext<T>` and later asserting on the deserialized message via `TryGetMessage<T>` inherits this same requirement.
- **`SendContext<T>` is a genuinely awkward NSubstitute target for exercising `IMessageSerializer.GetMessageBody<T>` directly (P-499/WO-081):** it is a wide interface (`Message`, `Headers`, `SourceAddress`, `SupportedMessageTypes`, `ContentType`, and a dozen more members) inherited from `SendContext`/`PipeContext`, and MassTransit's own `SystemTextJsonMessageSerializer` implementation reads several of these while building the message envelope. A substitute with only `.Message`/`.Headers`/`.SupportedMessageTypes` stubbed is sufficient in practice (the remaining members default to `null`/`default` harmlessly for envelope construction), but any future test taking this approach should stub `SupportedMessageTypes` explicitly (`sendContext.SupportedMessageTypes.Returns([typeof(T).FullName!])`) — leaving it unstubbed (`null`) risks a `NullReferenceException` inside the inner STJ serializer depending on exactly which envelope fields it populates from that array. **A `SendContext<T>` closed over an `internal` message type hits the exact P-342 NSubstitute/strong-naming proxy failure** documented below — the message type must be `public` when a test substitutes `SendContext<T>` directly.
- **`ConsumeContext.Headers` reflects the JSON envelope's OWN embedded header snapshot, taken when the inner STJ serializer builds the envelope inside `_inner.GetMessageBody(context)` — NOT a live view of `SendContext<T>.Headers` as mutated afterward by an outer decorator (P-499/WO-081), confirmed empirically via a live `TestHarness` round trip:** `PayloadTransformMessageSerializer.GetMessageBody<T>` calls `_inner.GetMessageBody(context)` FIRST (which snapshots `context.Headers.GetAll()` into the JSON envelope's own `Headers` field at that point), and only THEN calls `context.Headers.Set(PayloadTransformHeaders.MessageTypeAad, aad)` on the same live `SendHeaders` object. That later mutation IS visible to `IMessageDeserializer.Deserialize(MessageBody, Headers, Uri?)`'s `Headers headers` parameter — confirmed because a full harness round trip with `EnableEncryption = true` decrypts successfully, which is cryptographically impossible under AES-GCM unless the deserializer reproduced the exact AAD the publisher used — because that parameter is the RAW transport-level headers (`ReceiveContext.TransportHeaders`), a channel independent of envelope content. But it is NOT visible via `ConsumeContext.Headers.Get<string>(...)` inside an ordinary `IConsumer<T>` — that property reflects the JSON envelope's own embedded header copy, captured too early to include a header set after `_inner.GetMessageBody` already ran. Practical consequence: a header set by a decorator AFTER calling the inner `IMessageSerializer.GetMessageBody<T>` reaches the deserializer's raw `Headers` parameter but not ordinary consumer-facing `ConsumeContext.Headers` — the two are genuinely different channels carrying different snapshots of the same underlying `SendHeaders` object at different points in time. `PayloadTransformAadHarnessTests` (PA-14) proves the AAD design's core structural claim via successful end-to-end decryption, not via a consumer independently reading `ConsumeContext.Headers` (an earlier draft of this test tried exactly that and failed with a `<null>` capture, which is what surfaced this finding). Any future header set by a serializer/deserializer decorator AFTER the inner serializer has already run inherits this same distinction — it is reliably visible to a paired custom deserializer's raw `Headers` parameter, but not necessarily to ordinary consumer code.

### CloudEvents compliance rule

`MassTransitEventPublisher.PublishAsync<TEvent>` wraps `TEvent` (`where TEvent : class, IIntegrationEvent`) in `EventEnvelope<TEvent>` (from `04.Contracts`) before publishing to the transport. `EventEnvelope<TEvent>` is itself a CloudEvents 1.0 structured JSON document. Its only construction path is `EventEnvelope.Wrap(integrationEvent, source:, subject:, tenantId:, correlationId:, causationId:)`; it has no public constructor or setter, so construction outside `Wrap` no longer compiles. The publisher passes every optional argument by name (`subject`, `correlationId` and `causationId` are all optional strings). The envelope members are populated as follows:

| Envelope member (JSON) | Source |
| --- | --- |
| `SpecVersion` (`specversion`) | Constant `"1.0"`. |
| `Id` (`id`) | The event's own `EventId` — not a new envelope-level identity; deduplicate on it together with `Source`. |
| `Source` (`source`) | `MessagingOptions.ServiceName` resolved from `IOptions<MessagingOptions>`. |
| `Type` (`type`) | The event's `[IntegrationEvent]` name (`IntegrationEventDescriptor.For<TEvent>().Name`), never the CLR class name. |
| `DataVersion` (`dataversion`) | The event's `[IntegrationEvent]` `Version` (default `1`). |
| `Time` (`time`) | The event's own `OccurredOn` (when the business fact occurred, not when the envelope was built or published). |
| `Subject` (`subject`) | `PublishContext.Subject` when set via `WithSubject`; otherwise omitted. |
| `DataContentType` (`datacontenttype`) | Constant `"application/json"`. |
| `TenantId` (`tenantid`, `Guid?`) | `PublishContext.TenantId` when explicitly set via `WithTenantId` (or populated ambiently by `TenantHeaderPropagator` when `MessagingBusBuilder.WithTenantContext<T>()` is registered); otherwise omitted. |
| `CorrelationId` (`correlationid`, `string?`) | `PublishContext.CorrelationId` (`Guid`) when explicitly set via `WithCorrelationId`, formatted `"D"`; else `Activity.Current?.TraceId.ToString()` if an ambient trace exists; else a new `Guid.NewGuid().ToString("D")`. |
| `CausationId` (`causationid`, `string?`) | `PublishContext.CausationId` when explicitly set via `WithCausationId`, formatted `"D"`; otherwise omitted — no ambient fallback. |
| `Data` (`data`) | The integration event, serialized as its runtime type. |

`Wrap` throws `ArgumentException` when `TEvent` is not the event's runtime type or `EventId`/`OccurredOn` is unset, and `InvalidOperationException` when the event type lacks a valid `[IntegrationEvent]` attribute. The publisher resolves the descriptor before starting its activity, so such an event fails before any telemetry or transport work. MassTransit carries the serialized envelope as the message body inside its own transport envelope.

### Consumer endpoint naming convention

MassTransit derives queue and subscription names from consumer type names by convention. The convention applied by `MessagingBusBuilder` is:

- Queue name: `{service-name}-{consumer-name}` in kebab-case (e.g., `order-service-order-placed`)
- `MessagingOptions.ServiceName` is used as the prefix
- `KebabCaseEndpointNameFormatter` (MassTransit 9.x) strips the `Consumer` suffix from the consumer type name by default — `OrderPlacedConsumer` → `order-placed`, not `order-placed-consumer`.
- The `Event` suffix is NOT stripped; `OrderPlacedEventConsumer` → `order-placed-event`.

Custom endpoint names are configured via `IConsumerDefinition<TConsumer>` passed to `AddConsumer<TConsumer, TDefinition>()`.

### Retry policy rule

When `.WithRetry()` is called, the retry pipeline is applied globally to all registered consumers. The default policy (`RetryOptions` with no configuration action): 3 total attempts, linear back-off from 1 s to 3 s. Business-rule violations and validation errors that should not be retried (4xx-equivalent) must be filtered out via a `RetryFilter` on the consumer's `IConsumerDefinition<T>` — never silently swallowed inside `ConsumeAsync`.

### Outbox transactional rule

When `.WithEntityFrameworkOutbox<TDbContext>()` is called:

- Message publishing is held in the outbox rows until the consuming service's `IUnitOfWork.SaveChangesAsync` commits the DB transaction.
- Application handlers must NOT call both `IUnitOfWork.SaveChangesAsync` AND `IEventPublisher.PublishAsync` independently in the same request — the outbox handles the dual-write atomically.
- The MassTransit outbox delivery background service polls for unsent rows and publishes to the broker.
- At-least-once delivery is guaranteed; all consumers must be idempotent.
- The consuming service owns and runs the EF migrations for outbox tables. `SharedKernel.Messaging.MassTransit` provides no migrations of its own.

### Dead-letter and poison-message policy (P-343/WO-054, shipped)

A message becomes "poison" and is routed to dead-letter only at one of two decision points:

1. **Immediate**: the endpoint-level `ConsumerDefinitionBase<TConsumer>.NonRetryableExceptions` filter classifies the thrown exception type as fatal — no retry attempts occur at all.
2. **After exhaustion**: the global `RetryOptions` attempt budget (configured via `WithRetry()`) is exhausted without a successful delivery.

An open `CircuitBreakerOptions` breaker (`WithCircuitBreaker()`) is a **distinct** failure path — it short-circuits delivery entirely while open, failing fast with `CircuitBreakerException` until `ResetInterval` elapses and the breaker moves to `HalfOpen`. A message rejected by an open breaker is not itself dead-lettered by the breaker; whether it is retried again depends on the retry/non-retryable classification above once the breaker allows delivery through again.

**RabbitMQ** — configurable via `DeadLetterOptions`/`MessagingBusBuilder.WithDeadLetterPolicy()`:

- `MessageTimeToLive` (default `null` — unbounded retention) is applied as the RabbitMQ `x-message-ttl` queue argument on MassTransit's automatically-derived fault (`"_error"`) and dead-letter (`"_skipped"`) queues, via `IRabbitMqSendTopologyConfigurator.ConfigureErrorSettings`/`.ConfigureDeadLetterSettings` — genuinely wired and observable.
- `QueueNameSuffix` (default `"_error"`, matching MassTransit's own RabbitMQ default naming) is accepted but **has no observable effect** in the installed MassTransit version (9.1.2). **Confirmed during P-343 implementation** via reflection across the full `IRabbitMqSendTopologyConfigurator`/`IRabbitMqReceiveEndpointConfigurator`/`IRabbitMqQueueConfigurator`/`IRabbitMqExchangeConfigurator` surface, plus direct IL user-string inspection of `MassTransit.RabbitMqTransport.dll` (confirming `"_error"`/`"_skipped"` are fixed internal literal constants): MassTransit's public RabbitMQ configuration surface lets a consuming service configure the *arguments* of its automatically-derived fault/dead-letter queue (TTL, quorum, priority, lazy, exchange type, etc.) but exposes no hook to rename the queue itself. `IRabbitMqReceiveEndpointConfigurator.BindDeadLetterQueue(exchangeName, queueName, configure)` *does* accept an explicit queue name, but it wires RabbitMQ's native NACK/TTL-expiry-triggered `x-dead-letter-exchange` mechanism — a different, unrelated path from the one a retry-exhausted/non-retryable consumer exception actually takes (MassTransit republishes to its own fault transport and ACKs the original delivery; it never lets the broker's native dead-lettering handle a consumer exception), so it cannot be substituted here without breaking the very poison-message-routing guarantee this option exists to configure. This is a genuine MassTransit 9.1.2 API gap, not an oversight — if a future MassTransit release adds a rename hook, wire `QueueNameSuffix` through it then.
- Omitting `WithDeadLetterPolicy()` entirely preserves MassTransit's own default RabbitMQ error-queue behavior — this domain adds a configuration surface, it does not change the unconfigured default.

**Azure Service Bus** — dead-lettering is entirely transport-native and outside this domain's configuration surface: ASB moves a message to its built-in `$DeadLetterQueue` once `MaxDeliveryCount` (an ASB queue/subscription-level setting, configured at the Azure resource, not through `AzureServiceBusOptions`) is exceeded. `WithDeadLetterPolicy()` may still be called under an ASB transport for consistency across a multi-transport codebase, but it has no effect — `Build()` registers `DeadLetterPolicyAdvisoryHostedService`, which logs an advisory `Warning` at host startup rather than throwing, mirroring the `WithVersionTranslator`/`TranslatorRegistrationValidationHostedService` advisory pattern. There is currently no platform-level configuration surface for ASB's `MaxDeliveryCount`; it is set at the Azure resource/Bicep/ARM level.

### Ordered delivery via partition key (P-344/WO-054, shipped)

`PublishContext.PartitionKey` (`string?`) maps to each transport's genuine native ordered-delivery mechanism — never a platform-invented substitute. `PublishContext.WithPartitionKey(string partitionKey)` sets it; the guard is identical in shape to `WithHeader`'s key guard — non-null, non-empty, validated at set time, throwing `ArgumentException` otherwise.

The mapping is implemented once, transport-agnostically, as an internal `SendContext` extension — `PartitionKeySendContextExtensions.ApplyPartitionKey(this SendContext context, string? partitionKey)` (`MessageBus/PartitionKeySendContextExtensions.cs`) — called from the outgoing pipe callback in `MassTransitMessageBus.PublishAsync<T>`/`.SendAsync<T>` and `MassTransitEventPublisher.PublishEnvelopeAsync<TEvent>`. It applies both halves of the mapping unconditionally, with no branching on which transport `MessagingBusBuilder` actually configured:

- **RabbitMQ**: `MassTransit.RoutingKeyExtensions.TrySetRoutingKey(context, partitionKey)` drives routing-key affinity so all messages sharing a key traverse the same queue-binding path in publish order. `TrySetRoutingKey` (not the throwing `SetRoutingKey`) is used deliberately — both were confirmed, empirically, against MassTransit 9.1.2 to never throw regardless of the configured transport (verified via an in-memory `AddMassTransitTestHarness` bus), but `TrySetRoutingKey`'s non-throwing contract is the one actually documented by MassTransit's own API surface, so it is the correct choice for code that runs unconditionally on every publish/send regardless of transport.
- **Azure Service Bus**: `MassTransit.ServiceBusSendContextExtensions.SetSessionId(context, partitionKey)` applies the outgoing message's session identifier. The receiving endpoint must have sessions enabled for the ordering guarantee to hold — enabling sessions on an endpoint is a consuming-service/infrastructure responsibility this builder does not silently apply retroactively. **Confirmed during P-344 implementation**: the real `Azure.Messaging.ServiceBus`-backed `ServiceBusSendContext` payload `SetSessionId` writes to is only materialized by MassTransit's actual ASB transport at send time — under RabbitMQ or the in-memory test transport, `SendContext.TryGetPayload<ServiceBusSendContext>` returns `false` and `SetSessionId` is a silent, safe no-op (confirmed empirically; it never throws). This is exactly why calling both halves of the mapping unconditionally, on every publish/send regardless of configured transport, is safe.

**Ordering caveat**: ordering is guaranteed only among messages sharing the same `PartitionKey` **and** consumed by a single active consumer instance on that endpoint. Multiple concurrent consumer instances processing the same partitioned endpoint (horizontal scale-out) break the ordering guarantee even with a correctly-set key — this is inherent to both transports' native mechanisms, not a platform limitation. Omitting `PartitionKey` leaves publish/send behavior exactly as it was before this feature — no ordering guarantee beyond the transport's own default (`PartitionKeySendContextExtensions.ApplyPartitionKey` returns immediately without touching the context when `partitionKey` is `null`).

**Usage:**

```csharp
await eventPublisher.PublishAsync(orderPlacedEvent, ctx =>
{
    ctx.WithPartitionKey(orderPlacedEvent.OrderId.ToString()); // ordered delivery per order
}, ct);
```

### Opt-in payload compression and encryption (P-346/WO-054, shipped)

`MessagingBusBuilder.WithPayloadTransform(Action<PayloadTransformOptions>?)` wires an opt-in serializer/deserializer decorator pair around the platform's default MassTransit STJ serializer, built entirely on `01.Core`'s existing primitives — `IPayloadCompressor` (`SharedKernel.Compression`) and `ISymmetricEncryptionService` (`SharedKernel.Cryptography`). Never a new bespoke compression/cryptography primitive in this domain.

- **Fixed ordering, not caller-configurable**: publish-side is always compress-then-encrypt; consume-side is always decrypt-then-decompress — matching `01.Core`'s platform-wide convention (compressing already-encrypted, high-entropy ciphertext wastes CPU for no size benefit). `PayloadTransformOptions.EnableCompression`/`.EnableEncryption` are independently toggleable booleans; the ordering itself is not exposed as a configurable value anywhere.
- **Disabled by default**: `EnableCompression` and `EnableEncryption` both default to `false`. Enabling either changes the wire format — this is a deliberate, explicit, per-service opt-in, never ambient behavior a consuming service could be surprised by. Existing tests (`CloudEventsEnvelopeTests`, `HarnessTests`, etc.) pass unchanged because `WithPayloadTransform()` is never called by them.
- **`Build()`-time dependency guard**: enabling a flag without its corresponding `01.Core` service already registered in DI (`services.AddSharedKernelCompression(...)`/`AddSharedKernelCryptography(...)`) throws `InvalidOperationException` at `Build()`, mirroring the `WithIdempotency()` missing-`IIdempotencyStore` guard. Checked via `Services.FirstOrDefault(d => d.ServiceType == typeof(IPayloadCompressor)/typeof(ISymmetricEncryptionService))` — no `BuildServiceProvider()` call (same anti-pattern-avoidance rule as every other `Build()`-time guard in this domain).
- **Loud failure on mismatch**: `PayloadTransformMessageDeserializer` wraps any exception thrown while reversing the transform (decrypt/decompress) **or** while the inner STJ deserializer subsequently parses the resulting bytes into `PayloadTransformMismatchException` — a consumer whose `PayloadTransformOptions` do not match the publisher's (either direction: publisher-transformed/consumer-not, or the reverse) fails loudly and clearly instead of MassTransit's own generic `SerializationException`/`JsonException` surfacing from parsing ciphertext or compressed bytes as plaintext JSON.

**Shipped implementation shape** (`Serialization/` folder, `SharedKernel.Messaging.MassTransit`):

- `PayloadTransformOptions` (`Options/PayloadTransformOptions.cs`) — sealed class, `EnableCompression`/`EnableEncryption` (`bool`, both default `false`), `SectionName = "SharedKernel:Messaging:PayloadTransform"`.
- `PayloadTransformSerializerFactory : ISerializerFactory` — decorates an inner `ISerializerFactory` (the real `MassTransit.Configuration.SystemTextJsonMessageSerializerFactory`, freshly constructed with default options — matches exactly what the bus would otherwise use unconfigured). `ContentType` delegates to the inner factory's `ContentType` — no new content type is introduced, so this decorator becomes the transport's sole (de)serializer for MassTransit's existing default content type.
- `PayloadTransformMessageSerializer : IMessageSerializer` — `GetMessageBody<T>(SendContext<T>)` delegates to the inner serializer, then (when enabled) `IPayloadCompressor.Compress` then `ISymmetricEncryptionService.Encrypt`, wrapping the final bytes in `MassTransit.BytesMessageBody`.
- `PayloadTransformMessageDeserializer : IMessageDeserializer` — `Deserialize(MessageBody, Headers, Uri?)` reverses the transform (decrypt-then-decompress) on the raw bytes, then delegates to the inner deserializer; `Deserialize(ReceiveContext)` (the entry point MassTransit's own receive pipeline actually calls) is implemented exactly like MassTransit's own `SystemTextJsonMessageSerializer.Deserialize(ReceiveContext)` — `new MassTransit.Serialization.BodyConsumeContext(receiveContext, Deserialize(receiveContext.Body, receiveContext.TransportHeaders, receiveContext.InputAddress))` — both public, real MassTransit types, not a hand-rolled substitute.
- `PayloadTransformMismatchException` — sealed `Exception` subclass wrapping the underlying decrypt/decompress/deserialize failure, per the loud-failure rule above.
- `EncryptedPayloadWireCodec` (internal) — packs/unpacks `SharedKernel.Cryptography.Symmetric.EncryptedPayload` (`KeyId`, `Nonce`, `Ciphertext`, `Tag`) to/from a single flat, length-prefixed byte array for the MassTransit message body — deliberately distinct from `ISymmetricEncryptionService.EncryptToString`'s Base64-packed string wire format (that one targets string-shaped call sites; this one targets the raw binary body a serializer produces, skipping Base64 inflation). Every segment is explicitly length-prefixed rather than assumed fixed-size, since `EncryptedPayload` itself makes no such guarantee.
- `MessagingBusBuilder.ConfigurePayloadTransform(IBusFactoryConfigurator, IBusRegistrationContext, PayloadTransformOptions)` — `internal static`, called from inside both the RabbitMQ and Azure Service Bus `Using{Transport}((ctx, busCfg) => {...})` callbacks (before `ConfigureEndpoints`), resolves `IPayloadCompressor`/`ISymmetricEncryptionService` from `ctx` (which implements `IServiceProvider`, resolving from the real, fully-built container — mirrors the existing `WithIdempotency()`/`UseConsumeFilter(..., ctx)` DI-resolution-at-transport-configuration-time pattern) only for each enabled flag, then wires the decorator factory.
- `PayloadTransformHeaders` (internal, `Serialization/PayloadTransformHeaders.cs`, P-499/WO-081) — domain-local constant, `MessageTypeAad = "x-payload-transform-message-type"`. Deliberately NOT `01.Core.WellKnownHeaders` (both setter and reader live inside this same package, never crossing a domain boundary) and deliberately NOT `x-sk-*`-prefixed (an internal implementation detail of this one pipeline stage, not a cross-cutting concern `ConsumerBase`'s log-scope enrichment or any consuming service should read directly).

See the MassTransit 9.x API notes below for the two non-obvious API requirements (`ClearSerialization()` + `AddDeserializer(..., isDefault: true)`) discovered empirically while implementing this feature — omitting either silently breaks the consume side while the publish side appears to work.

#### AAD and synchronous-provider migration (P-499/WO-081, shipped — `PA-01`→`PA-18` all `●`)

`01.Core`'s WO-081 cryptography wave makes `associatedData` a required parameter on every `ISymmetricEncryptionService` member (`SK.01.P491`, breaking) and gates the retained synchronous `Encrypt`/`Decrypt` members behind a new `ISynchronousEncryptionKeyProvider` capability marker (`SK.01.P492`). This section documents how the payload-transform trio migrated onto both.

**The migration cannot make this call site async — a structural fact, not a design choice.** `IMessageSerializer.GetMessageBody<T>`/`IMessageDeserializer.Deserialize` are hard-synchronous MassTransit interface members (see the new MassTransit 9.x API note above) — there is no async overload or extensibility point anywhere in this pipeline stage. `PayloadTransformMessageSerializer`/`PayloadTransformMessageDeserializer` therefore continue calling the sync `Encrypt`/`Decrypt` members, now passing the required `associatedData` argument, and become structurally dependent on `01.Core`'s `ISynchronousEncryptionKeyProvider` gate: **enabling `PayloadTransformOptions.EnableEncryption` against a KMS/HSM-backed key provider is not supported** — every `Encrypt`/`Decrypt` call fails with `NotSupportedException` (thrown inside `AesGcmEncryptionService` itself, per `SK.01.P492`). This domain additionally adds a best-effort `Build()`-time check (inspecting the registered `IEncryptionKeyProvider` `ServiceDescriptor`'s `ImplementationType`/`ImplementationInstance` where statically determinable) so a misconfiguration fails at startup with a payload-transform-specific message instead of at the first publish/consume call — but that check cannot cover an `ImplementationFactory`-based registration without invoking it (forbidden by this file's own anti-`BuildServiceProvider()` rule), so `01.Core`'s runtime `NotSupportedException` remains the guaranteed backstop in that case.

**AAD source and cross-process transmission.** The AAD is the message's own CLR type name (`typeof(T).FullName ?? typeof(T).Name`), matching `01.Core`'s own call-site-inventory candidate for this domain (`SK.01.P491`'s notes). Since `IMessageDeserializer.Deserialize` receives no generic `T` (unlike `IMessageSerializer.GetMessageBody<T>`), the consumer cannot derive this string independently — so the publish side additionally writes it into a new plaintext transport header, `PayloadTransformHeaders.MessageTypeAad`, via `SendContext<T>.Headers.Set(...)`; the consume side reads the identical value from the `Headers headers` parameter `IMessageDeserializer.Deserialize(MessageBody, Headers, Uri?)` already receives (the same channel `SK.07.HeaderPropagation`/`SK.07.AmbientPropagation` already rely on), before attempting decryption. The header value is never encrypted (message-type identity is not secret) and never persisted inside `EncryptedPayload`/`EncryptedPayloadWireCodec`, per `01.Core`'s own AAD rule.

**Rolling-deploy behavior, fixed and non-configurable:**

- Header present → `associatedData` = UTF-8 bytes of the header value.
- Header absent (a message published by a pre-`PA-*` producer) → `associatedData` = `Array.Empty<byte>()`, byte-identical to AES-GCM's own implicit "no AAD" semantics every producer used before this migration — so an **old producer's message stays decryptable by a new consumer** throughout a rolling deploy, with no operator-facing toggle.
- **THE REVERSE DIRECTION IS NOT COVERED AND CANNOT BE**: a message published by a **new** (post-`PA-*`) producer binds non-empty AAD; an **old** consumer still calling the pre-`SK.01.P491` single-argument `Decrypt(payload)` authenticates against implicit empty AAD and fails — a genuine AEAD authentication failure, not a bug to route around. **Operational requirement: every consumer of an encrypted payload-transform queue/topic must be upgraded to this phase before any producer is** — the mirror image of ordinary "expand before contract" schema-change discipline. Plan the rollout consumer-first.

### Bus-backed readiness probe (P-347/WO-054)

`IMessageBusProbe.ProbeAsync(CancellationToken)` reports the health of the actual, already-configured message bus `MessagingBusBuilder` builds for the consuming service — never an independently constructed connection built from separately supplied configuration. Registered as a singleton, unconditionally, by `Build()` — no opt-in call required. `07.Messaging` ships this probe primitive only; it ships **no** `IHealthCheck` implementation. Wiring `IMessageBusProbe` into `AddHealthChecks()` remains `13.ServiceDefaults`'s concern, mirroring the readiness-probe split already established by `06.Persistence`/`08.Storage`/`09.Search`/`10.Intelligence`/`17.Workflows`.

### Logging authoring standard and EventId allocation (P-254)

All production log statements in this domain follow the root-brain platform Logging Conventions: `[LoggerMessage]` source-generated partial methods only, explicit `EventId`, PascalCase named template placeholders, no ambient CorrelationId/TraceId/TenantId as an explicit placeholder. `07.Messaging`'s reserved range is `7000-7999` (`{07} * 1000`, per `SharedKernel.Primitives.Logging.LoggingEventIdRanges.Messaging` in `01.Core`, P-249). Only `SharedKernel.Messaging.MassTransit` logs today — `SharedKernel.Messaging.Abstractions` has zero transport/logging NuGet dependencies and does not log — so the domain currently uses a single 100-wide sub-block, `7000-7099`, rather than subdividing per package.

Final allocation (all in `SharedKernel.Messaging.MassTransit`):

| EventId | Name | Level | Declaring type | File |
| --- | --- | --- | --- | --- |
| 7001 | `ConsumerConsumeError` | Error | `ConsumerBase<TMessage>` | `Consumers/ConsumerBase.cs` |
| 7002 | `BatchConsumeEntry` | Information | `BatchConsumerBase<TMessage>` | `Consumers/BatchConsumerBase.cs` |
| 7003 | `BatchConsumeError` | Error | `BatchConsumerBase<TMessage>` | `Consumers/BatchConsumerBase.cs` |
| 7004 | `FaultConsumerHandling` | Error | `FaultConsumerAdapter<TMessage,TFaultConsumer>` | `Consumers/FaultConsumerAdapter.cs` |
| 7005 | `FaultConsumerError` | Error | `FaultConsumerAdapter<TMessage,TFaultConsumer>` | `Consumers/FaultConsumerAdapter.cs` |
| 7006 | `RoutingSlipExecuteError` | Error | `RoutingSlipActivityBase<TArguments,TLog>` | `RoutingSlips/RoutingSlipActivityBase.cs` |
| 7007 | `RoutingSlipCompensateError` | Error | `RoutingSlipActivityBase<TArguments,TLog>` | `RoutingSlips/RoutingSlipActivityBase.cs` |
| 7008 | `VersionTranslating` | Debug | `VersionTranslatingConsumer<TOld,TNew>` | `SchemaEvolution/VersionTranslatingConsumer.cs` |
| 7009 | `VersionTranslatorNoConsumer` | Warning | `TranslatorRegistrationValidator` | `SchemaEvolution/TranslatorRegistrationValidator.cs` |

This table resolves the three internal collisions that existed before P-254 (raw `EventId(1)`/`EventId(2)`/`EventId(3)` integer literals independently reused by unrelated hand-written `LoggerMessage.Define<>()` delegates across `ConsumerBase`, `BatchConsumerBase`, `FaultConsumerAdapter`, `RoutingSlipActivityBase`, and `VersionTranslatingConsumer`) and converts `FaultConsumerAdapter`'s one raw `_logger.LogError(...)` extension-method call (for the fault-consumer-handler-threw case) into `EventId` 7005. `7010-7099` remain reserved headroom for future `[LoggerMessage]` methods in this package before a second sub-block or package would be needed. Verified against `00.Governance`'s SK0020/SK0021 analyzer and `LoggingEventIdIntegrityAssertion` (P-250) with zero suppressions.

**`[LoggerMessage]` source-generator field-vs-property requirement (discovered during P-254):** the `[LoggerMessage]`-attributed partial method source generator only auto-discovers an `ILogger`-typed **field** on the containing type (`SYSLIB1019` if none is found) — it does not see an `ILogger`-typed **property**, even a simple auto-property. `ConsumerBase<TMessage>`, `BatchConsumerBase<TMessage>`, and `RoutingSlipActivityBase<TArguments,TLog>` all expose `protected ILogger Logger { get; }` (a property, chosen originally for subclass-overridability), so every `[LoggerMessage]` method on those three types must be declared `private static partial void LogXxx(ILogger logger, ...)` with an explicit `ILogger logger` parameter, and call sites must pass `Logger` explicitly (e.g. `LogConsumeError(Logger, typeof(TMessage).Name, ex);`) — an instance `private partial void LogXxx(...)` declaration on these three types fails to compile with `SYSLIB1019` followed by `CS8795`. `FaultConsumerAdapter<TMessage,TFaultConsumer>` and `VersionTranslatingConsumer<TOld,TNew>` use a private constructor-injected `ILogger<T>` **field**, so their `[LoggerMessage]` methods stay ordinary instance `private partial void` methods with no parameter workaround. `TranslatorRegistrationValidator` is a static class taking `ILogger` as a method parameter already, so it uses `private static partial void` with the `ILogger` parameter for the same underlying reason. When adding a new `ILogger`-consuming base type to this package, prefer a private `ILogger` **field** over a property unless subclass overridability of the logger itself is a genuine requirement — it avoids this workaround entirely.

---

## DI Registration (expected shape)

```csharp
// Minimal — RabbitMQ, no outbox
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .WithRetry()
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

// RabbitMQ with explicit options (production)
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq(o =>
    {
        o.Host = "amqps://rabbitmq.svc.cluster.local";
        o.Username = configuration["RabbitMq:Username"];
        o.Password = configuration["RabbitMq:Password"];
        o.Prefetch = 8;
    })
    .WithRetry(o =>
    {
        o.Attempts = 5;
        o.InitialInterval = TimeSpan.FromSeconds(2);
        o.MaxInterval = TimeSpan.FromSeconds(60);
    })
    .AddConsumer<OrderPlacedConsumer>()
    .AddConsumer<PaymentProcessedConsumer>()
    .Build();

// Azure Service Bus with managed identity (K8s production)
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseAzureServiceBus(o => o.FullyQualifiedNamespace = "my-namespace.servicebus.windows.net")
    .WithRetry()
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

// Azure Service Bus with connection string (local dev / CI)
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseAzureServiceBus(o => o.ConnectionString = configuration["AzureServiceBus:ConnectionString"])
    .WithRetry()
    .Build();

// With EF Core transactional outbox (consuming service passes its own DbContext type)
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .WithRetry()
    .WithEntityFrameworkOutbox<OrderDbContext>(o =>
    {
        o.BatchSize = 50;
        o.QueryDelay = TimeSpan.FromSeconds(2);
        o.DuplicateDetectionWindow = TimeSpan.FromMinutes(60);
    })
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

// Application layer injects the abstractions (never the MassTransit concrete types)
services.AddScoped<IPlaceOrderHandler, PlaceOrderHandler>();
// IPlaceOrderHandler constructor: (IMessageBus bus, IEventPublisher publisher)

// Consumer implementation (in the consuming service — not in SharedKernel.Messaging)
public sealed class OrderPlacedConsumer : ConsumerBase<OrderPlacedEvent>
{
    protected override Task ConsumeAsync(OrderPlacedEvent message, CancellationToken ct)
    {
        // Business logic only — no MassTransit concerns here
    }
}

// Circuit breaker + fault consumer (P-125, P-126)
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .WithRetry()                        // retry inner
    .WithCircuitBreaker(o =>            // circuit breaker outer
    {
        o.TripThreshold = 10;
        o.ResetInterval = TimeSpan.FromSeconds(30);
    })
    .AddConsumer<OrderPlacedConsumer>()
    .AddFaultConsumer<OrderPlacedEvent, OrderPlacedFaultConsumer>()
    .Build();

// OrderPlacedFaultConsumer (in the consuming service)
public sealed class OrderPlacedFaultConsumer : IFaultConsumer<OrderPlacedEvent>
{
    public Task HandleAsync(Guid faultId, DateTimeOffset faultTimestamp,
                            OrderPlacedEvent faultedMessage, FaultExceptionInfo[] exceptions,
                            CancellationToken ct)
    {
        // Compensate, alert, or triage dead-lettered message
    }
}

// Deferred scheduling (P-127) — inject IMessageScheduler from Abstractions
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .WithInMemoryScheduler()            // dev/test only
    .Build();
// Production: .WithQuartzScheduler(o => o.ConnectionString = "...")

// IMessageScheduler usage in application handler
public sealed class PlaceOrderHandler
{
    public PlaceOrderHandler(IMessageScheduler scheduler) { ... }
    public async Task Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var token = await _scheduler.ScheduleAsync(
            new PaymentReminderEvent { OrderId = cmd.OrderId },
            DateTimeOffset.UtcNow.AddHours(24), ct);
    }
}

// Cross-service command routing (P-131)
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .WithSendEndpointRoute<ProcessPaymentCommand>("payment-service-process-payment")
    .Build();

// Batch consumer (P-129)
services
    .AddSharedKernelMessaging(o => o.ServiceName = "ingest-service")
    .UseRabbitMq("rabbitmq://localhost")
    .AddBatchConsumer<AuditEventBatchConsumer>(o =>
    {
        o.MessageLimit = 50;
        o.TimeLimit = TimeSpan.FromSeconds(5);
    })
    .Build();

public sealed class AuditEventBatchConsumer : BatchConsumerBase<AuditEvent>
{
    protected override async Task ConsumeAsync(IReadOnlyList<AuditEvent> messages, CancellationToken ct)
    {
        // Process all messages in one database round-trip
    }
}

// Saga state machine (P-128)
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .AddSaga<OrderSagaState>()
    .WithEntityFrameworkSagaRepository<OrderDbContext, OrderSagaState>()
    .Build();

public sealed record OrderSagaState : SagaStateBase { }

public sealed class OrderSagaStateMachine : SagaStateMachineBase<OrderSagaState>
{
    public State Active { get; private set; } = null!;
    public Event<OrderPlacedEvent> OrderPlaced { get; private set; } = null!;
    public Event<PaymentConfirmedEvent> PaymentConfirmed { get; private set; } = null!;

    public OrderSagaStateMachine()
    {
        InstanceState(x => x.CurrentState);
        Event(() => OrderPlaced, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentConfirmed, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Initially(When(OrderPlaced).TransitionTo(Active));
        During(Active, When(PaymentConfirmed).Finalize());
    }
}
```

```csharp
// Idempotency (P-134) — reference IIdempotencyStore implementation (P-349/WO-054).
// SharedKernel does not ship this type; this is a complete, working example backed by
// IDistributedCache (e.g. Microsoft.Extensions.Caching.StackExchangeRedis), sourcing its
// retention window from IdempotencyOptions.ExpiryWindow. See
// SharedKernel.Messaging.Abstractions/README.md for the full worked recipe.
public sealed class RedisIdempotencyStore(
    IDistributedCache cache,
    IOptions<IdempotencyOptions> options) : IIdempotencyStore
{
    private const string KeyPrefix = "idempotency:";

    public async Task<bool> HasProcessedAsync(Guid messageId, CancellationToken ct)
    {
        var value = await cache.GetAsync(BuildKey(messageId), ct);
        return value is not null;
    }

    public Task MarkProcessedAsync(Guid messageId, CancellationToken ct) =>
        cache.SetAsync(
            BuildKey(messageId),
            value: [],
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = options.Value.ExpiryWindow },
            ct);

    private static string BuildKey(Guid messageId) => $"{KeyPrefix}{messageId:N}";
}

services.AddStackExchangeRedisCache(o => o.Configuration = configuration["Redis:ConnectionString"]);
services.AddScoped<IIdempotencyStore, RedisIdempotencyStore>(); // consuming service bridge
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .WithRetry()
    .WithIdempotency(o => o.ExpiryWindow = TimeSpan.FromHours(48))
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

// Header propagation (P-135) — a hand-rolled propagator for a service-specific concern
// not already covered by the built-in propagators below
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .WithHeaderPropagator<FeatureFlagHeaderPropagator>() // scoped; reads IFeatureManager, e.g.
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

// FeatureFlagHeaderPropagator example (in the consuming service — not in SharedKernel)
public sealed class FeatureFlagHeaderPropagator : IMessageHeaderPropagator
{
    public FeatureFlagHeaderPropagator(IFeatureManager featureManager) { ... }
    public void Propagate(PublishContext context)
        => context.WithHeader("x-sk-feature-set", _featureManager.CurrentSetName);
}

// Built-in ambient correlation + tenant-context propagation (P-345/WO-054) — the platform-provided
// alternative to hand-rolling correlation/tenant propagators from scratch
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .WithAmbientCorrelationPropagation()             // zero-argument — no consumer-authored class
    .WithTenantContext<AppTenantContextAccessor>()   // bridges the seam below; scoped
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

// AppTenantContextAccessor — the ONLY class the consuming service writes to get tenant
// identity flowing into every published EventEnvelope<TEvent>.TenantId automatically.
// Bridges this domain's local ITenantContextAccessor seam to the service's real tenant
// source (12.Security's ITenantProvider here), mirroring 05.Application's
// IAuthorizationContext/IUnitOfWork bridge pattern.
public sealed class AppTenantContextAccessor : ITenantContextAccessor
{
    private readonly ITenantProvider _tenantProvider;
    public AppTenantContextAccessor(ITenantProvider tenantProvider) => _tenantProvider = tenantProvider;
    public Guid? TenantId => _tenantProvider.TenantId;
}

// Per-consumer definition base (P-136) — in the consuming service
public sealed class OrderPlacedConsumerDefinition : ConsumerDefinitionBase<OrderPlacedConsumer>
{
    protected override IReadOnlyList<Type> NonRetryableExceptions =>
        [typeof(ValidationException), typeof(NotFoundException)];

    protected override string? EndpointName => "order-service-order-placed-v2";

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<OrderPlacedConsumer> consumerConfigurator,
        IBusRegistrationContext context) { /* additional config */ }
}

services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .WithRetry()
    .AddConsumer<OrderPlacedConsumer, OrderPlacedConsumerDefinition>()
    .Build();

// Message schema evolution (P-137)
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .WithVersionTranslator<OrderPlacedEventV1, OrderPlacedEventV2, OrderPlacedV1ToV2Translator>()
    .AddConsumer<OrderPlacedConsumer>() // registered for OrderPlacedEventV2
    .Build();

public sealed class OrderPlacedV1ToV2Translator
    : IMessageVersionTranslator<OrderPlacedEventV1, OrderPlacedEventV2>
{
    public OrderPlacedEventV2 Translate(OrderPlacedEventV1 old)
        => new() { OrderId = old.OrderId, CustomerId = old.CustomerId, TotalAmount = old.Amount };
}

// Routing slip (P-139) — two-activity distributed coordination
services
    .AddSharedKernelMessaging(o => o.ServiceName = "payment-service")
    .UseRabbitMq("rabbitmq://localhost")
    .AddRoutingSlipActivity<ValidatePaymentActivity>()
    .AddRoutingSlipActivity<ChargeCardActivity>()
    .Build();

// Activity implementation
public sealed class ValidatePaymentActivity
    : RoutingSlipActivityBase<ValidatePaymentArguments, ValidatePaymentLog>
{
    protected override async Task<ExecutionResult> ExecuteAsync(
        ValidatePaymentArguments args, CancellationToken ct)
    {
        // validate; return Complete(new ValidatePaymentLog { ... }) on success
        return Complete(new ValidatePaymentLog { IsValid = true });
    }

    protected override Task<CompensationResult> CompensateAsync(
        ValidatePaymentLog log, CancellationToken ct)
        => Task.FromResult(CompensationComplete());
}

// Dispatch a routing slip from an application handler
public sealed class ProcessPaymentHandler
{
    public ProcessPaymentHandler(IMessageBus bus, IRoutingSlipBuilder slipBuilder) { ... }

    public async Task Handle(ProcessPaymentCommand cmd, CancellationToken ct)
    {
        var slip = _slipBuilder
            .AddActivity("validate-payment",
                new Uri("queue:payment-service-validate-payment"),
                new ValidatePaymentArguments { OrderId = cmd.OrderId, Amount = cmd.Amount })
            .AddActivity("charge-card",
                new Uri("queue:payment-service-charge-card"),
                new ChargeCardArguments { OrderId = cmd.OrderId })
            .Build();

        await _bus.ExecuteRoutingSlipAsync(slip, ct);
    }
}

// Explicit tenant identity + partition key on a single publish call (P-340/P-344) —
// used when the ambient WithTenantContext<T>()/WithAmbientCorrelationPropagation() propagators
// (shown above) are not registered, or an explicit per-call override is needed
await _eventPublisher.PublishAsync(orderPlacedEvent, ctx =>
{
    ctx.WithTenantId(currentTenantId);           // → EventEnvelope<TEvent>.TenantId
    ctx.WithSubject($"order/{orderPlacedEvent.OrderId}"); // → EventEnvelope<TEvent>.Subject
    ctx.WithPartitionKey(orderPlacedEvent.OrderId.ToString()); // ordered delivery per order
}, ct);

// Per-consumer concurrency override (P-342) — in the consuming service
public sealed class OrderPlacedConsumerDefinition : ConsumerDefinitionBase<OrderPlacedConsumer>
{
    protected override int? ConcurrentMessageLimit => 4; // overrides the global RabbitMQ/ASB default
}

// Dead-letter policy (P-343) — RabbitMQ only. MessageTimeToLive is genuinely wired (x-message-ttl
// on MassTransit's own "_error"/"_skipped" queues); QueueNameSuffix is accepted but has no effect
// in MassTransit 9.1.2 — see "Dead-letter and poison-message policy" above.
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .WithDeadLetterPolicy(o => o.MessageTimeToLive = TimeSpan.FromDays(7))
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

// Opt-in payload compression + encryption (P-346) — requires 01.Core's compression/cryptography
// packages already registered
services.AddSharedKernelCompression();   // 01.Core — IPayloadCompressor
services.AddSharedKernelCryptography();  // 01.Core — ISymmetricEncryptionService
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .WithPayloadTransform(o =>
    {
        o.EnableCompression = true;
        o.EnableEncryption = true; // wire format changes — every consumer of these message types
                                    // must also call WithPayloadTransform with matching flags
    })
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

// Bus-backed readiness probe (P-347) — injected wherever K8s liveness/readiness needs it;
// 13.ServiceDefaults wraps this in an IHealthCheck via AddHealthChecks(), not shown here
public sealed class MessagingReadinessCheck // illustrative — the real IHealthCheck lives in 13.ServiceDefaults
{
    public MessagingReadinessCheck(IMessageBusProbe probe) { ... }
    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        var health = await _probe.ProbeAsync(ct);
        return health.IsHealthy;
    }
}
```

`SharedKernel.Messaging.Abstractions` ships **no DI extensions** — it is a pure interface library.

---

## AOT Compatibility

- `IMessageBus` and `IEventPublisher` are interfaces — AOT-safe by definition.
- `PublishContext` is a sealed class with no reflection in the hot path — AOT-safe.
- `MessagingOptions` is a plain POCO registered via the options system — AOT-safe.
- `IMessagingBuilder` is an interface — AOT-safe.
- `ConsumerBase<TMessage>` as a closed generic abstract class — AOT-safe. MassTransit consumer type scanning at startup is model-build time only (not a hot path); closed generics preserve type metadata without `[DynamicallyAccessedMembers]` at the call site.
- `MassTransitMessageBus` and `MassTransitEventPublisher` delegate to MassTransit `IBus`/`IPublishEndpoint`; MassTransit 9.x is AOT-compatible for core publish/send paths. Verify on each major upgrade.
- STJ serialization for message payloads: MassTransit 9.x supports source-generated STJ contexts. For NativeAOT builds, consuming services must supply a source-generated `JsonSerializerContext` covering all message and envelope types. Configure via `MessagingBusBuilder` when targeting NativeAOT.
- `EventEnvelope<TEvent>` (`04.Contracts`) is serialized with reflection-based STJ; `04.Contracts` ships no `JsonSerializerContext` and is not trimming-safe by design, so a NativeAOT build must supply its own metadata for every envelope instantiation it publishes or consumes.
- `MassTransit.EntityFrameworkCore` uses EF Core 10.x which is AOT-compatible with compiled models. Verify on each major upgrade.
- Transport packages (`MassTransit.RabbitMQ`, `MassTransit.Azure.ServiceBus.Core`) — verify AOT status on each major upgrade; the `MessagingBusBuilder` abstraction contains the blast radius to the composition layer.
- `IntegrationEventDescriptor.For<TEvent>()` (`04.Contracts`, used by `MassTransitEventPublisher` for the `messaging.event_type` tag and by `EventEnvelope.Wrap` for `Type`/`DataVersion`) reads `[IntegrationEvent]` attribute metadata once per type and caches it — attribute reading is preserved by the trimmer and is not a per-publish cost. The publisher itself uses no `MakeGenericMethod` or delegate cache.
- `FaultExceptionInfo` is a sealed record (value type semantics) — AOT-safe; no reflection in equality or construction path.
- `IFaultConsumer<TMessage>` is an interface — AOT-safe. `FaultConsumerAdapter<TMessage, TFaultConsumer>` is a closed generic; the trimmer preserves closed generic type metadata at startup registration time.
- `IMessageScheduler` is an interface — AOT-safe. `MassTransitMessageScheduler` delegates to MassTransit's scheduler which uses the registered transport; AOT safety depends on transport package — verify per transport on each major upgrade.
- `SagaStateBase` as an abstract record and `SagaStateMachineBase<TSaga>` as a closed generic abstract class — AOT-safe at the base type level. MassTransit saga state machine type scanning is model-build time only; closed generics preserve type metadata. Verify on each major upgrade that `MassTransitStateMachine<T>` retains AOT compatibility.
- `BatchConsumerBase<TMessage>` as a closed generic abstract class — AOT-safe; same pattern as `ConsumerBase<TMessage>`.
- `ISendEndpointResolver` is an interface — AOT-safe. `ConventionSendEndpointResolver` uses `KebabCaseEndpointNameFormatter.SanitizeName(typeof(T).Name)` which is a string transformation on the type name preserved by the trimmer (type metadata, not reflection-instantiation).
- The per-type route dictionary (`Dictionary<Type, string>`) in `MessagingBusBuilder` is populated at startup (build time) — AOT-safe; no runtime type resolution required.
- No `Activator.CreateInstance`, `Assembly.Load`, or dynamic reflection in hot paths within `07.Messaging` types.
- `IIdempotencyStore` is an interface — AOT-safe. `IdempotencyOptions` is a plain POCO — AOT-safe. `IdempotentConsumerBehavior<TMessage>` is a closed generic sealed class; the trimmer preserves closed generic metadata at startup registration time.
- `IMessageHeaderPropagator` is an interface — AOT-safe. The `IEnumerable<IMessageHeaderPropagator>` resolution at publish time relies on standard DI enumeration which is AOT-safe in `Microsoft.Extensions.DependencyInjection` on .NET 10.
- `ConsumerDefinitionBase<TConsumer>` is a generic abstract class — AOT-safe at the base type level; closed generic instantiation by MassTransit at startup is model-build time only.
- `IMessageVersionTranslator<TOld, TNew>` is a generic interface — AOT-safe. The MassTransit deserialization hook used by `WithVersionTranslator` relies on message type aliases; verify AOT compatibility of the specific MassTransit interception API on each major upgrade.
- `IRoutingSlipBuilder` is an interface — AOT-safe. `MassTransitRoutingSlipBuilder` delegates to MassTransit `RoutingSlipBuilder` — verify AOT status of MassTransit Courier on each major upgrade. `RoutingSlipActivityBase<TArguments, TLog>` is a generic abstract class; closed generic instantiation at startup is model-build time only.
- `MessagingDiagnostics.ActivitySource` and the `Activity` instances it produces (`System.Diagnostics`, BCL) are fully AOT-safe — no reflection, no dynamic code generation. `Activity.SetTag` uses object boxing for primitive tag values but performs no type scanning or `MakeGenericMethod` calls. Starting/disposing an `Activity` per consume/publish call is a hot-path allocation when a listener is attached (and a no-op fast path when no listener is attached) — acceptable for AOT and for steady-state throughput.
- `[LoggerMessage]`-attributed partial log methods (`Microsoft.Extensions.Logging.Abstractions`, P-254) are compiled by a Roslyn source generator at build time — zero reflection, zero `Activator.CreateInstance`, fully AOT-safe by construction; this is why the platform-wide logging standard mandates this pattern over hand-written `LoggerMessage.Define<>()` delegates or ad hoc `ILogger.LogXxx()` calls. `MessagingLogScope.Create(Guid?)` returns a plain `Dictionary<string, object?>` populated by direct indexer assignment — no reflection, AOT-safe.
- `PublishContext.TenantId`/`PartitionKey` (P-340/P-344) are plain nullable properties on the existing sealed mutable builder — no reflection added. `ITenantContextAccessor` is an interface — AOT-safe. `AmbientCorrelationHeaderPropagator`/`TenantHeaderPropagator` are sealed classes with no reflection in `Propagate` — AOT-safe (P-345).
- `IMessageBusProbe`/`MessageBusHealth` (P-347) are, respectively, an interface and a sealed record with no reflection in construction or equality — AOT-safe. `MassTransitMessageBusProbe` delegates to MassTransit's own bus-health surface; AOT safety follows the same "verify on each MassTransit major upgrade" caveat already stated for `MassTransitMessageBus`/`MassTransitEventPublisher` above.
- `DeadLetterOptions`/`PayloadTransformOptions` (P-343/P-346) are plain POCOs registered via the options system — AOT-safe, same shape as every other `*Options` class in this package.
- `MessagingDiagnostics.Meter` and its `Counter<long>`/`Histogram<double>` instruments (P-348, `System.Diagnostics.Metrics`, BCL) are fully AOT-safe by the same reasoning already stated for `ActivitySource` above — no reflection, no dynamic code generation, and the same static-instrument exception to the "no static mutable state" hard rule applies identically to `Meter` as it does to `ActivitySource`.
- The compressing/encrypting serializer decorator (P-346) delegates to `01.Core`'s `IPayloadCompressor`/`ISymmetricEncryptionService` — both already AOT-preferred per `01.Core`'s own brain; this package adds no additional reflection on top of them. STJ serialization of the (pre-transform) message payload is subject to the same NativeAOT source-generated-`JsonSerializerContext` requirement already stated above for message payloads.

---

## Test Rules

- Unit tests for each package live in the nested `.Tests/` folder inside that package's folder.
- **`IMessageBus` / `IEventPublisher` mock tests:** mock both interfaces with NSubstitute; verify application handlers call `PublishAsync` / `SendAsync` with the expected event type and arguments; do not test MassTransit internals.
- **Prefer `16.Testing`'s `InMemoryMessageBus`/`InMemoryEventPublisher` over ad-hoc `Substitute.For<IMessageBus>()`/`Substitute.For<IEventPublisher>()` whenever a test only needs to record-and-assert "was this published/sent" (P-191):** `SharedKernel.Testing` ships purpose-built doubles for these exact two interfaces (`AddInMemoryMessageBus()`/`AddInMemoryEventPublisher()`) with a richer, already-tested assertion surface — `ShouldHavePublished<T>()`, `ShouldHaveSent<T>()`, `ShouldHavePublishedOnce<T>()`, `ShouldNotHavePublished<T>()`, `PublishedOf<TEvent>()`. A raw NSubstitute stub that merely proves "was this interface method called" is a second, independently-maintained fake implementation of the same contract — including inside `07.Messaging`'s own `SharedKernel.Messaging.Abstractions.Tests` project, which is otherwise the canonical reference for these interfaces. Reach for NSubstitute only when the test needs to assert call arguments/exceptions/sequencing that the shared double does not expose, or to mock an unrelated dependency injected alongside `IMessageBus`/`IEventPublisher`. This reference is test-only — adding a `ProjectReference` to `SharedKernel.Testing.csproj` from a `.Tests` project never introduces a production dependency, since `SharedKernel.Testing` itself only references the abstraction packages it fakes, never their concrete providers.
- **`ConsumerBase<TMessage>` tests:** instantiate a concrete subclass via MassTransit `TestHarness`; publish a message; assert `ConsumeAsync` was called with the correct message; assert exceptions propagate without swallowing (NSubstitute throw-configured dependency).
- **Integration tests:** use MassTransit `TestHarness` (`MassTransit.Testing`) — in-memory bus, no broker required; `await harness.InactivityTask` to wait for consumer completion; verify `harness.Consumed.Select<TMessage>()` contains the expected messages.
- **Outbox integration tests:** wire `WithEntityFrameworkOutbox<TDbContext>` to SQLite; publish via `IEventPublisher`; call `SaveChangesAsync` — outbox rows are written atomically DURING `SaveChangesAsync` via `OutboxSaveChangesObserver` (not before); assert row count > 0 after `SaveChangesAsync`. **SQLite limitation:** the outbox delivery worker uses `RepeatableRead` isolation requiring nested transactions which SQLite does not support — do NOT start the delivery worker (TestHarness) in the row-insertion test; register the bus with `AddMassTransit` + `UsingInMemory` without `AddMassTransitTestHarness` so no delivery worker starts.
- **RabbitMQ integration tests:** use Testcontainers RabbitMQ from `16.Testing/SharedKernel.Testing`; configure `UseRabbitMq` with container connection string; publish and consume; assert end-to-end delivery; mark with `[Trait("Category","Integration")]` so CI can skip when Docker unavailable.
- **No tests against live Azure Service Bus** — use `MassTransit.TestFramework.TestHarness` for ASB consumer logic; use a Service Bus emulator or skip in CI.
- **Retry policy tests:** configure `UseMessageRetry(r => r.Immediate(3))`; consumer throws on first N-1 calls, succeeds on Nth; assert `harness.Consumed.Any<TMessage>()` is true (message eventually consumed) AND `harness.Published.Any<Fault<TMessage>>()` is false (no dead-letter). Note: `TestHarness.Consumed.Select<T>()` does not expose per-retry-attempt entries — cannot assert exact retry count via the harness.
- **CloudEvents envelope tests:** publish via `IEventPublisher`; intercept the outgoing `EventEnvelope<TEvent>` via `TestHarness`; assert `Source`, `Type`/`SpecVersion`/`DataContentType`, `DataVersion` (from the `[IntegrationEvent]` attribute, default `1`), `Id`/`Time`/`Data` (from the event), `CorrelationId`, `CausationId`, `TenantId` and `Subject` are populated correctly (`TenantId`/`Subject`/`CausationId` set when the matching `PublishContext` method is called, `null` otherwise). Test events declare a unique `[IntegrationEvent]` name+version pair. A whole-envelope record-equality assertion against an independently-`EventEnvelope.Wrap<TEvent>()`-constructed instance is the strongest proof that construction stayed factory-only — MassTransit's in-memory `TestHarness` delivers the payload by reference (no JSON round-trip) when no consumer forces deserialization, so this comparison is safe; do not assume the same holds once a real broker transport is in play.
- **`MessagingBusBuilder` guard tests:** verify `IMessageBus` resolves after `.Build()`; verify `IEventPublisher` resolves; verify startup validation throws when `MessagingOptions.ServiceName` is null (throws `OptionsValidationException` from `MessagingOptionsValidator`, not `InvalidOperationException` — assert `.Throw<Exception>().Where(e => e.Message.Contains("ServiceName"))`); verify `InvalidOperationException` when `.Build()` called without a transport configured.
- **Consumer endpoint convention tests:** verify queue name follows `{service-name}-{consumer-type}` kebab-case via `new KebabCaseEndpointNameFormatter(prefix, false).Consumer<TConsumer>()` directly — `IConsumerTestHarness<T>` in MassTransit 9.x does not expose `.Consumer.InputAddress`.
- **`RequestAsync` timeout tests:** verify `RequestAsync<TRequest, TResponse>` throws (or cancels) when no responder is registered and the cancellation token expires.
- **`IMessageBusProbe` tests (P-347/WO-054):** functional `ProbeAsync` behavior (healthy/unhealthy/description-populated) is proven via `AddMassTransitTestHarness()` — register `MassTransitMessageBusProbe` manually alongside it (mirroring the `AmbientPropagationTests.cs` manual-registration pattern, since the real `MessagingBusBuilder.Build()` configures a real transport incompatible with `AddMassTransitTestHarness()` in the same registration) and assert `IsHealthy`/`Description` before vs. after `harness.Start()`/`harness.Stop()`. Registration-shape guard tests (singleton, unconditional) go through the real `Build()` pipeline but assert against the raw `ServiceDescriptor`, never an actual resolved instance — resolving `IMessageBusProbe` requires MassTransit to build `IBusInstance`, which hits the real-bus license gate outside `AddMassTransitTestHarness()` (see the MassTransit 9.x API notes above).
- **Standard test package set:** `xunit` 2.9.3, `xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.13.0, `coverlet.collector` 6.0.4, `FluentAssertions` 8.4.0, `NSubstitute` 5.3.0, `MassTransit.TestFramework` 9.1.2 (NOT `MassTransit.Testing` — package was renamed in MassTransit 9.x).
- **GlobalUsings.cs required** — every test project must include `global using Xunit;`.
- **SQLite for outbox unit tests** — no Testcontainers needed; SQLite covers EF Core outbox row lifecycle. Use Testcontainers only for broker-level integration tests.
- **Never use the `file` modifier on consumer, message, or DbContext types in test files** — C# `file` types generate mangled CLR names containing `<` and hash characters (e.g., `<ConsumerBaseTests>F15BB...RecordingConsumer`). MassTransit type matching splits on `<`; the mangled names cause type resolution failures for `GetConsumerHarness<T>()`, `harness.Consumed.Select<T>()`, and outbox entity model building. Always use `internal` (with a unique name per file to avoid collisions).
- **NSubstitute cannot proxy a MassTransit generic interface (`IConsumerConfigurator<TConsumer>`, etc.) closed over an `internal` consumer type (P-342/WO-054):** Castle DynamicProxy throws `ArgumentException` ("...because assembly MassTransit.Abstractions is strong-named...") when the interface lives in a strong-named MassTransit assembly and the closed generic type argument is `internal` to the test assembly. This is a narrow exception to the file's other `internal`-by-default consumer-type guidance above: a consumer type (and its message type, since it appears in the consumer's public API) must be declared `public` specifically when a test directly substitutes a generic MassTransit configurator interface parameterized by that consumer type (e.g., calling `IConsumerDefinition<TConsumer>.Configure(...)` against `Substitute.For<IReceiveEndpointConfigurator>()`/`Substitute.For<IConsumerConfigurator<TConsumer>>()` directly, bypassing `TestHarness`). Tests that only go through `TestHarness`/`AddMassTransitTestHarness` never hit this — the harness does not ask NSubstitute to proxy anything.
- **`await using` for `ServiceProvider` in tests** — `MassTransit.UsageTracking.UsageTracker` (registered by MassTransit 9.x startup) only implements `IAsyncDisposable`, not `IDisposable`. Using `using var sp` causes a synchronous disposal path that throws. Always use `await using var sp = services.BuildServiceProvider(...)` and declare test methods as `async Task`.
- **SQLite keep-alive connection for in-memory database persistence** — when using a SQLite in-memory database across multiple `ServiceScope` instances in the same test, open a `SqliteConnection("Data Source=:memory:")` and keep it open for the test's lifetime. Pass that connection to `UseSqlite(connection)`. If the connection closes, the in-memory database is dropped and subsequent scopes see an empty schema.
- **`ActivitySource` / `Activity` assertion pattern (P-172):** subscribe an `ActivityListener` with `ShouldListenTo = source => source.Name == "SharedKernel.Messaging"` and `Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData` BEFORE invoking `Consume()` or `PublishAsync()` — without an attached listener, `ActivitySource.StartActivity` returns `null` (fast-path no-op) and no activity is created to assert against. Collect started activities into a `List<Activity>` via `listener.ActivityStarted = act => list.Add(act)`. Always call `ActivitySource.AddActivityListener(listener)` and dispose/remove it at test teardown to avoid cross-test listener leakage (listeners are process-global, not scoped to a `ServiceProvider`).
- **`ActivityListener` parallel-test-isolation hazard:** the listener registered via `ActivitySource.AddActivityListener` is process-wide, not scoped to the test method or class. Under xUnit's default parallel test-class execution, other test classes in the same run that also drive `ConsumerBase<T>.Consume()` or `MassTransitEventPublisher.PublishAsync<T>()` emit their own activities on the same `"SharedKernel.Messaging"` source while your listener is attached. Asserting `capturedActivities.Should().ContainSingle(a => a.OperationName == "...")` is flaky — it can capture activities from unrelated concurrently-running tests. Always filter by the test's own unique tag value (e.g. `a.GetTagItem("messaging.message_type") == nameof(MyTestMessage)`) in addition to `OperationName`, never by `OperationName` alone.
- **`Meter` / `MeterListener` assertion pattern (P-348):** subscribe a `System.Diagnostics.Metrics.MeterListener`, set `listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == "SharedKernel.Messaging") l.EnableMeasurementEvents(instrument); }`, register `SetMeasurementEventCallback<long>` and `SetMeasurementEventCallback<double>` callbacks to collect `(instrument.Name, measurement, tags)` tuples, then call `listener.Start()` BEFORE invoking the operation under test. `MeterListener.Start()` replays already-published instruments, so it correctly picks up `MessagingDiagnostics.Meter`'s five instruments even though they were published as static fields before the listener existed. `MessagingDiagnostics.Meter` (like `ActivitySource`) is process-wide — the same parallel-test-isolation hazard applies identically: always assert via `measurements.Should().Contain(m => m.InstrumentName == "..." && m.Tags.Any(t => t.Key == "messaging.message_type" && Equals(t.Value, nameof(MyUniqueTestMessage))))`, using a message/event type name unique to that test, never a bare instrument-name match.
- **`ConsumeContext.GetRetryAttempt()` retry-counter test pattern (P-348):** configure `busCfg.UseMessageRetry(r => r.Immediate(N))` and have the consumer under test throw on its first invocation only (track via a simple static bool, reset at test start) then succeed — `context.GetRetryAttempt()` is confirmed (via a live `TestHarness` probe) to return `0` on the original delivery and `1, 2, ...` on each retry-filter re-delivery, and to return `0` safely (never throw) when no retry middleware is configured. This is the verified alternative to MassTransit 9.1.2's unreachable `IRetryObserver`/`IRetryObserverConnector` — see `MessagingDiagnostics.RetryCounter`'s doc comment and the "RETRY OBSERVATION GAP" note above for the full reflection-based investigation.
- **Propagator-symmetry tests (P-341):** any test asserting `IMessageHeaderPropagator` output must cover all three dispatch verbs it applies to (`PublishAsync`, `SendAsync`, `RequestAsync`) when the scenario is verb-agnostic — a propagator test that only exercises `PublishAsync` is exactly the gap that produced the P-341 defect in the first place.
- **Built-in ambient propagator tests (P-345):** exercise `MessagingBusBuilder.WithAmbientCorrelationPropagation()`/`.WithTenantContext<TAccessor>()` through the real builder API (`services.AddSharedKernelMessaging(...).With...()`), not by manually registering `AmbientCorrelationHeaderPropagator`/`TenantHeaderPropagator` via `services.AddScoped<IMessageHeaderPropagator, T>()` directly — the builder methods themselves are part of the contract under test. Set an ambient `Activity` via `using var activity = new Activity("name").Start();` before the dispatch call under test (no `ActivityListener` is required for `Activity.Current` to populate — that machinery is only needed for the separate `MessagingDiagnostics.ActivitySource` tracing assertions in P-172/P-348) and compute the expected `CorrelationId` as `Guid.Parse(activity.TraceId.ToString())`, matching `AmbientCorrelationHeaderPropagator`'s own conversion. The "provable no-op" half of `TenantHeaderPropagator` is proven by a test that calls `AddSharedKernelMessaging(...)` **without** `.WithTenantContext<T>()` and asserts both "no exception" and "`EventEnvelope<TEvent>.TenantId` stays `null`" — a no-op that merely fails to throw is not sufficient proof by itself.
- **`IMessageBusProbe` health tests (P-347):** assert `IsHealthy = true` against a started `TestHarness`/in-memory bus, and `IsHealthy = false` (with a non-null `Description`) against a bus that was never started or whose underlying transport connection was torn down (e.g. a Testcontainers RabbitMQ container stopped mid-test). Never assert health by independently pinging the broker outside the probe — the whole point of the probe is that it reflects the real, already-configured bus instance.
- **Payload transform round-trip and mismatch tests (P-346):** a round-trip test must exercise both `EnableCompression` and `EnableEncryption` together (the fixed compress-then-encrypt/decrypt-then-decompress order) as well as each flag independently. A mismatch test (publisher transform-enabled, consumer not, or vice versa) must assert a loud, typed failure (`PayloadTransformMismatchException` or equivalent) — never assert on a generic `Exception`, since a generic assertion would also pass for an unrelated failure mode and mask a regression to silent misinterpretation.
- **Ordered-delivery Testcontainers tests (P-344):** publish interleaved messages under at least two distinct `PartitionKey` values against a single active consumer instance; assert each key's own messages are observed in publish order — asserting order across the *combined* stream (ignoring key) is not the guarantee this feature makes and will produce a flaky/meaningless test.
- **Dead-letter Testcontainers tests (P-343, shipped):** configure a consumer whose `ConsumerDefinitionBase.NonRetryableExceptions` (or an exhausted `WithRetry()` budget) routes a thrown exception to dead-letter; assert the message is observable at MassTransit's real, automatically-derived dead-letter/fault destination via a registered `IFaultConsumer<TMessage>` (`AddFaultConsumer<TMessage, TFaultConsumer>()`) — do not assert merely that the original queue is empty, which is also true for a successfully-processed message, and do not assert against a `DeadLetterOptions.QueueNameSuffix`-renamed queue, since that option has no observable effect in MassTransit 9.1.2 (see "Dead-letter and poison-message policy" above). The endpoint starting successfully and the fault consumer receiving the message together are indirect but real proof that a configured `MessageTimeToLive` was accepted by the broker as a valid RabbitMQ queue argument. A `ServiceCollection` driving `GetServices<IHostedService>()` for a real-broker test must call `.AddLogging()` first — MassTransit's default health-check hosted service constructor-injects `ILogger<T>`, which is otherwise unregistered and throws `InvalidOperationException` at resolve time (a pre-existing gap also present in `RabbitMqIntegrationTests.cs`, not introduced by this phase).
- **`MeterListener` assertion pattern (P-348):** subscribe a `MeterListener` with `InstrumentPublished = (instrument, listener) => { if (instrument.Meter.Name == "SharedKernel.Messaging") listener.EnableMeasurementEvents(instrument); }` BEFORE invoking the operation under test; `Counter<T>`/`Histogram<T>` measurements are only observed while a listener is actively enabled for that specific instrument. Like `ActivityListener` (below), `MeterListener` subscription is process-wide — apply the same per-test unique-tag-value filtering discipline to avoid cross-test-class flakiness under xUnit's default parallel execution.
- **`[LoggerMessage]` assertions use `EventId`, never message-text substring matching (P-254):** when a test needs to assert that a specific log statement fired, capture via a test `ILogger`/`ILoggerFactory` double (`16.Testing`) and assert on the structured `EventId.Id` (e.g. `7001` for `ConsumerBase`'s consume-error log) rather than parsing the rendered message string — the message template text is not a stable contract, the `EventId` is. **`MessagingLogScope`-seeded scope assertions:** any test asserting `BeginScope` contents on `ConsumerBase`, `BatchConsumerBase`, `FaultConsumerAdapter`, or `RoutingSlipActivityBase` must assert the `"CorrelationId"` key is present and formatted as `Guid.ToString("D")` (or empty string when unavailable) — this is the one shape `MessagingLogScope.Create` guarantees identically across all four types. Tests assert against the literal string `"CorrelationId"` (the key's runtime value), not against `MessagingLogScope.CorrelationIdKey` — the constant (P-263) is production-side authoring hygiene only; test code has no obligation to reference it.
- **Payload-transform AAD tests (P-499/WO-081, shipped):** `PayloadTransformAadTests` (unit-level, `SerializationTests/`) proves the headline scenarios directly against `PayloadTransformMessageSerializer`/`PayloadTransformMessageDeserializer` using a real `AesGcmEncryptionService` and a `MassTransit.Serialization.DictionarySendHeaders` instance shared between the publish and consume calls (it implements both `SendHeaders` and `Headers`, so the exact same object simulates the header traveling across the wire without needing a full harness): matching-AAD round trip succeeds; a header value swapped between two otherwise-identical payloads (mirroring `01.Core`'s own `T-66`) fails and surfaces as `PayloadTransformMismatchException`, never a raw `CryptographicException`; a header-absent payload (simulating a pre-`PA-*` producer, encrypted with empty AAD) falls back to `Array.Empty<byte>()` and decrypts successfully. `PayloadTransformAadHarnessTests` (`HarnessTests/`) supplies the one proof the unit-level test cannot — that the AAD header genuinely crosses the real transport, not a hand-constructed `SendContext`/`Headers` pair — by publishing through a real `MessagingBusBuilder.ConfigurePayloadTransform` pipeline against `AddMassTransitTestHarness()` and asserting the message is both consumed (`harness.Consumed.Any<T>()`) and not faulted (`harness.Consumed.Any<Fault<T>>()` is `false`); a successful decrypt here is only cryptographically possible under AES-GCM if the header crossed intact, since ordinary `ConsumeContext.Headers` does NOT reflect this specific header (see the dedicated `ConsumeContext.Headers` MassTransit 9.x API note above for why a consumer-facing assertion is the wrong proof technique here). `Build()`-time guard tests (`PayloadTransformConfigurationTests`) cover all three `IEncryptionKeyProvider` registration shapes: `ImplementationInstance`/`ImplementationType` statically provable synchronous (no throw) or not (`InvalidOperationException` naming `ISynchronousEncryptionKeyProvider`), and `ImplementationFactory` (not statically inspectable — no throw at `Build()`, with a separate runtime test proving `01.Core`'s own `NotSupportedException` fires on the first `Encrypt` call against `AesGcmEncryptionService` wired to `FakeRemoteEncryptionKeyProvider`, `16.Testing`'s deliberately-unmarked KMS-shaped fake).

---

## Changelog

> Maintained by the messaging domain agent. One line per significant change.

- [2026-06-05] Domain brain initialized — packages, interfaces, implementation rules, outbox ownership, CloudEvents compliance, AOT notes, test rules
- [2026-06-05] Core implementation discoveries: MassTransit 9.1.2 (not 8.x); EF outbox pkg MassTransit.EntityFrameworkCore; test pkg MassTransit.TestFramework; 9 MassTransit 9.x API notes added (sync-brain)
- [2026-06-08] Tests phase discoveries: `file` modifier breaks MassTransit type matching (use `internal`); `KebabCaseEndpointNameFormatter` strips `Consumer` suffix in 9.x; endpoint naming example corrected; outbox row timing corrected (written during SaveChangesAsync not before); SQLite nested-tx limitation for delivery worker documented; `await using` required for ServiceProvider; `OptionsValidationException` guard clarified; retry test assertion corrected to eventual-success+no-fault pattern; `IConsumerTestHarness<T>.Consumer.InputAddress` does not exist in 9.x (sync-brain)
- [2026-06-08] Docs phase complete — all public types in both packages carry full XML doc comments (applied during Core phase); 0 errors 0 warnings on both packages; no new implementation discoveries requiring CLAUDE.md rule additions
- [2026-06-08] WO-021 deep architectural review — 9 gaps identified and queued as P-125–P-133: circuit breaker + fault consumer abstraction, IMessageScheduler deferred delivery, saga state machine base, batch consumer base, Build() ServiceProvider anti-pattern (critical fix), ISendEndpointResolver cross-service routing, OTel ActivitySource wiring (ServiceDefaults P-132), extended governance rules MSG0105-MSG0108 (P-133); planned capabilities table and 4 new hard violation rules added to this CLAUDE.md (arch-lead)
- [2026-06-08] SK.07.Scheduling complete — MassTransitMessageScheduler description corrected (SchedulePublish/CancelScheduledPublish APIs); WithInMemoryScheduler transport-aware wiring documented; 7 new MassTransit 9.x scheduler API notes added (sync-brain)
- [2026-06-09] SK.07.Routing complete — 2 MassTransit 9.x API notes added: KebabCaseEndpointNameFormatter.SanitizeName is an instance method (not static); tests bypassing Build() must register IReadOnlyDictionary<Type,string> + ConventionSendEndpointResolver manually (sync-brain)
- [2026-06-09] WO-022 phase planning — 5 new capabilities added: IIdempotencyStore + IdempotentConsumerBehavior (P-134), IMessageHeaderPropagator + WithHeaderPropagator (P-135), ConsumerDefinitionBase (P-136), IMessageVersionTranslator + WithVersionTranslator (P-137), RoutingSlipActivityBase + IRoutingSlipBuilder + IMessageBus.ExecuteRoutingSlipAsync (P-139); interface contracts, builder methods, hard violation rules, AOT notes, and DI registration examples added; queued capabilities table extended (messaging-arch-planner)
- [2026-06-09] SK.07.Idempotency complete — global consume filter API note added: UseConsumeFilter(typeof(MyFilter<>), ctx) wires open-generic IFilter<ConsumeContext<TMessage>> globally; open-generic DI registration pattern AddScoped(typeof(MyFilter<>)) documented; test harness wiring pattern for closed-generic filter in UsingInMemory documented (sync-brain)
- [2026-06-09] SK.07.HeaderPropagation complete — 3 MassTransit 9.x API notes added: IEnumerable<IMessageHeaderPropagator> resolution strategy (constructor vs GetService); ConsumeContext.Headers.GetAll() for header iteration; PublishContext alias required in test files (sync-brain)
- [2026-06-10] SK.07.VersionTranslation complete — `IMessageVersionTranslator<TOld, TNew>` in Abstractions; `WithVersionTranslator<TOld, TNew, TTranslator>()` implemented as a "translating consumer" (`VersionTranslatingConsumer<TOld, TNew>` republishes via `ConsumeContext.Publish<TNew>`) rather than a deserializer hook; advisory `TranslatorRegistrationValidator` runs via a new `TranslatorRegistrationValidationHostedService` at host startup (`Build()`-must-not-`BuildServiceProvider` pattern for DI-dependent advisory checks); `_registeredConsumerTypes` tracking added to `MessagingBusBuilder`; 5 new tests (sync-brain)
- [2026-06-19] WO-027 / P-172 queued — `MessagingDiagnostics.ActivitySource` ("SharedKernel.Messaging", "1.0.0") added to interface contracts (MassTransit package); `ConsumerBase<TMessage>.Consume()` and `MassTransitEventPublisher.PublishAsync<TEvent>()` instrumentation contracts documented ("Consumer.Consume"/"EventPublisher.Publish" child activities, `messaging.message_type`/`messaging.event_type`/`messaging.destination` tags and log-scope enrichment); queued capabilities table extended; new hard-violation clarifying the static-`ActivitySource`-is-not-a-mutable-state-violation exception and the `13.ServiceDefaults`-never-creates-this-source rule; AOT note added (BCL `System.Diagnostics`, fully AOT-safe); `ActivityListener` test assertion pattern added to Test Rules. Corrects a latent cross-domain dependency error in pending P-132, which assumed this source already existed — P-172 has no dependency and unblocks P-132 with a true prerequisite (messaging-arch-planner)
- [2026-06-22] SK.07.OTel implemented — `MessagingDiagnostics` (internal static, `Diagnostics/MessagingDiagnostics.cs`) ships `SourceName = "SharedKernel.Messaging"`, `SourceVersion = "1.0.0"`, and the static readonly `ActivitySource`; `ConsumerBase<TMessage>.Consume()` starts/disposes a `"Consumer.Consume"` activity tagged `messaging.message_type` and enriches the log scope with `messaging.destination` (from `ConsumeContext.DestinationAddress?.AbsolutePath`, omitted when null) and `messaging.message_type`, additive to the existing CorrelationId/`x-sk-*` scope from P-135; `MassTransitEventPublisher`'s instance `PublishEnvelopeAsync` (not the static `BuildPublisher`/`PublishEnvelope` helpers, which have no `TEvent` constraint access at the right point) starts/disposes an `"EventPublisher.Publish"` activity tagged `messaging.event_type` around the cached-delegate publish call, including the non-domain-event throw path (tag is set before the `IDomainEvent` guard runs). Zero new NuGet references — pure BCL `System.Diagnostics`. 9 new tests in `OTelInstrumentationTests.cs` (106 total in `SharedKernel.Messaging.MassTransit.Tests`, up from 97); discovered and fixed a test-isolation hazard: `ActivityListener` subscription is process-wide, so asserting "exactly one activity with this `OperationName`" is flaky under xUnit's default parallel test-class execution (other classes' `ConsumerBase`/`MassTransitEventPublisher` calls emit activities on the same shared source) — assertions must filter by the test's unique tag value (`messaging.message_type`/`messaging.event_type`), not by operation name alone. Unblocks 13.ServiceDefaults C-19 (`WithMessagingTelemetry`) with a true dependency (messaging-phase-implementer)
- [2026-06-22] Test Rules: added `ActivityListener` parallel-test-isolation hazard bullet — process-wide listener, filter by tag value not OperationName alone (messaging-phase-implementer)
- [2026-06-24] WO-030 / P-191 queued — Test Rules gains a rule preferring `16.Testing`'s `InMemoryMessageBus`/`InMemoryEventPublisher` over ad-hoc `Substitute.For<IMessageBus>()`/`Substitute.For<IEventPublisher>()`; targets a confirmed duplication in `ConsumerVerifyTests.cs` inside this domain's own `SharedKernel.Messaging.Abstractions.Tests` project; new state-map tasks T-18→T-20 added to the (previously closed) `SK.07.Tests` phase, which is demoted from ● to ○ pending the retrofit; adds a test-only `16.Testing` cross-domain dependency row — never a production reference (messaging-arch-planner, WO-030)
- [2026-06-24] WO-030 / P-191 implemented — `ConsumerVerifyTests.cs` retrofitted onto `InMemoryMessageBus`/`InMemoryEventPublisher`; added 2 publish/send recording tests; `NSubstitute` package reference removed from `SharedKernel.Messaging.Abstractions.Tests.csproj` (no remaining consumer); `Microsoft.Extensions.DependencyInjection` bumped 10.0.5→10.0.9 in that csproj to satisfy a transitive floor from `SharedKernel.Testing`'s EF Core Sqlite chain (`Microsoft.EntityFrameworkCore.Sqlite` 10.0.5 → `Microsoft.Extensions.Logging` 10.0.9 → `Microsoft.Extensions.DependencyInjection` ≥10.0.9), otherwise `dotnet build` fails with NU1605; 50/50 tests pass incl. `AbstractionsAssembly_ReferencesNo_MassTransit_Assembly` (asserts against the production assembly, unaffected by the test project's new `SharedKernel.Testing` reference); SK.07.Tests → ● (messaging-phase-implementer)
- [2026-07-09] WO-041 / P-254 queued — new Phase: LoggingRetrofit (LR-01–LR-18). Audited every hand-written `LoggerMessage.Define<>()` delegate in `SharedKernel.Messaging.MassTransit` and confirmed the three internal `EventId` collisions the root brain flagged: raw literal `1` reused by `ConsumerBase.LogConsumeError` and `RoutingSlipActivityBase.LogExecuteError`; `2` reused by `BatchConsumerBase.LogBatchEntry`, `FaultConsumerAdapter.LogFaultHandling`, and `RoutingSlipActivityBase.LogCompensateError`; `3` reused by `BatchConsumerBase.LogBatchError` and `VersionTranslatingConsumer.LogTranslating`; plus one raw `_logger.LogError(...)` extension-method call in `FaultConsumerAdapter` (the fault-consumer-handler-threw path). New "Logging authoring standard and EventId allocation" section added documenting the final `7000-7099` sub-block allocation (`EventId`s 7001-7009 across 6 files/9 methods — this domain has only one logging package today so no further sub-division was needed). New "Shared log scope construction" interface-contract entry added for `MessagingLogScope.Create(Guid?) → Dictionary<string,object?>` (`Logging/MessagingLogScope.cs`), the single approved construction path for the `BeginScope` dictionary's `CorrelationId` entry, replacing four independently hand-rolled implementations in `ConsumerBase`, `BatchConsumerBase`, `FaultConsumerAdapter`, and `RoutingSlipActivityBase` — note `RoutingSlipActivityBase`'s retrofit is a deliberate behavior change: its scope gains a `CorrelationId` entry it never carried before (previously it only tagged `Activity.Current`, never `BeginScope`). Two new hard-violation rules added (no hand-written `LoggerMessage.Define`/raw `ILogger` calls; no ad hoc `BeginScope` dictionary bypassing `MessagingLogScope`). AOT note and Test Rules notes added (assert on `EventId`, not message text; assert the shared `CorrelationId` scope shape). Depends on `01.Core` P-249 (`LoggingEventIdRanges.Messaging` numeric constant — value already fixed by the root folder-map convention, not a hard blocker) and `00.Governance` P-250 (SK0020/SK0021 analyzer + `LoggingEventIdIntegrityAssertion`, verification-only, dev/test-time) (messaging-arch-planner, WO-041)
- [2026-07-10] WO-041 / P-254 implemented — all 18 LR tasks complete, verified against the pre-written CLAUDE.md spec (Interface Contracts, EventId allocation table, hard violations) with zero discrepancies found. `Logging/MessagingLogScope.cs` added exactly as documented. All 6 files retrofitted: `ConsumerBase<TMessage>` (EventId 7001), `BatchConsumerBase<TMessage>` (7002/7003), `FaultConsumerAdapter<TMessage,TFaultConsumer>` (7004/7005 — the raw `_logger.LogError` call converted to `LogFaultConsumerError`), `RoutingSlipActivityBase<TArguments,TLog>` (7006/7007, plus the documented `CorrelationId`-scope behavior change), `VersionTranslatingConsumer<TOld,TNew>` (7008), `TranslatorRegistrationValidator` (7009, converted to `internal static partial class` with a `static partial void` method). Implementation note not previously captured: `[LoggerMessage]` partial methods on a type whose only `ILogger` member is a *property* (not a field) trigger source-generator diagnostic SYSLIB1019 ("no ILogger field found") — `ConsumerBase`, `BatchConsumerBase`, and `RoutingSlipActivityBase` all expose `protected ILogger Logger { get; }`, so their `[LoggerMessage]` methods had to be declared `static partial` with an explicit `ILogger logger` parameter (passing `Logger` at each call site) rather than instance `partial` methods; `FaultConsumerAdapter` and `VersionTranslatingConsumer`/`TranslatorRegistrationValidator` use a private/injected `ILogger` *field*, so their methods stayed instance (or `static partial` on the static-class case) with no parameter workaround needed. LR-15 added a new `RoutingSlipTests.cs` test (`ExecuteRoutingSlip_LogScope_ContainsCorrelationIdEqualToTrackingNumber`) using a dedicated `RoutingSlipCapturingLogger`/`RoutingSlipLogScopeCaptureStore` pair to assert the full BeginScope dictionary contents. LR-16/LR-17 were run for real (not deferred) — both `00.Governance` P-250 (SK0020/SK0021 analyzer, `LoggingEventIdIntegrityAssertion`) and `01.Core` P-249 (`LoggingEventIdRanges`) had already shipped by the time this phase ran: added a `LoggingEventIdGovernanceTests.cs` test referencing `SharedKernel.Primitives` + `SharedKernel.ArchitectureTests` (new test-only ProjectReferences, mirroring the established cross-domain test-consumption precedent) confirming zero EventId collisions/zero range violations against the real built assembly; SK0020/SK0021 zero-diagnostics verified via a temporary analyzer ProjectReference on the production csproj (added, built, confirmed 0 warnings, then reverted — not a permanent wiring change, out of this phase's scope). `FluentAssertions` bumped 8.4.0→8.10.0 in the test csproj to satisfy `SharedKernel.ArchitectureTests`' floor. 108/108 `SharedKernel.Messaging.MassTransit.Tests` passing (up from 107 pre-phase + 1 new LR-16 governance test); one pre-existing Docker-dependent `RabbitMqIntegrationTests` failure (unrelated, `[Trait("Category","Integration")]`, excluded from the count) and one known flaky `ActivityListener` parallel-test-isolation failure (documented in Test Rules, self-resolved on rerun) observed during the session, neither caused by this retrofit. `SK.07.LoggingRetrofit` now 18/18 `●` (messaging-phase-implementer)
- [2026-07-14] WO-042 / P-263 queued — new `SK.07.LoggingRetrofit` tasks LR-19→LR-22: `MessagingLogScope`'s inline `"CorrelationId"` dictionary-key literal (written once, inside `Create(Guid?)`, restated three more times in the type's own XML docs) is promoted to a package-local `internal const string CorrelationIdKey`. Closes the platform-wide "no bare literal for a semantically significant, documented key name" rule (WO-042's core principle) for this one already-well-documented case. Confirmed by audit: all four consumer/activity base types already call through `MessagingLogScope.Create` rather than constructing the dictionary key themselves, so this is a single-file rename with zero call-site fan-out; `RoutingSlipTests.cs`'s existing `"CorrelationId"` key assertion is unaffected (it checks the key's runtime string value via `RoutingSlipLogScopeCaptureStore.CapturedState`, not the production-side authoring path). Deliberately kept local to `07.Messaging` rather than promoted to `01.Core` — a `BeginScope` dictionary key name is this domain's own structured-logging contract, not a cross-service wire format (`EventId` ranges are the `01.Core`-owned cross-domain concern; scope-dictionary key names are not). Interface Contracts' `MessagingLogScope` entry and the Test Rules `MessagingLogScope`-seeded scope assertion bullet both updated to reference `CorrelationIdKey`; `SK.07.LoggingRetrofit` demoted from ●(18/18) to ○(18/22) pending implementation (messaging-arch-planner, WO-042)
- [2026-07-16] WO-042 / P-263 implemented — LR-19→LR-22 complete. `MessagingLogScope.cs` gained `internal const string CorrelationIdKey = "CorrelationId";` with its own XML doc `<remarks>` explaining the deliberate non-promotion to `01.Core`; `Create(Guid?)`'s dictionary initializer now reads `[CorrelationIdKey] = ...` instead of the bare literal; the type's `<remarks>`/`<returns>` docs (three prior restatements of the literal) now reference `<see cref="CorrelationIdKey"/>`/`CorrelationIdKey` instead. Single-file change exactly as scoped — zero call-site fan-out, since all four consumer/activity base types already went through `MessagingLogScope.Create`. Verified against pre-existing `main` tip: `SharedKernel.Messaging.MassTransit.Tests` 108/108 passing both before and after the edit (`RoutingSlipTests`' `"CorrelationId"` key assertions unaffected, confirming the rename changed only the production authoring path, not the runtime value); the one failing `RabbitMqIntegrationTests.PublishAndConsume_RealBroker_MessageDelivered` reproduces identically pre- and post-change (Docker-gated `[Trait("Category","Integration")]` test, unrelated health-check DI resolution issue, not caused by this phase). Interface Contracts' `MessagingLogScope` entry required no edit — the arch-planner's WO-042 pass had already pre-written it to the post-implementation shape. `SK.07.LoggingRetrofit` now 22/22 `●` (messaging-phase-implementer)
- [2026-08-04] WO-054 (arch-lead, root P-340–P-352) — 10 new phases planned (P-340–P-349, all `○` Pending): EnvelopeTenancy, PropagationSymmetry, ConsumerConcurrency, DeadLetter, OrderedDelivery, AmbientPropagation (depends on EnvelopeTenancy+PropagationSymmetry), PayloadTransform, ReadinessProbe, DiagnosticsCoverage, PackagingRecipes (depends on AmbientPropagation). Every defect cited was verified directly against the real shipped `.cs` source before being written up, not assumed from this file's own prior prose — and in the process this pass also caught and corrected three pieces of already-stale documentation unrelated to the new phases: (1) the CloudEvents compliance rule table claimed `CorrelationId`/`CausationId` are `Guid` and named `SchemaVersion`/`TimestampUtc` — the real shipped `EventEnvelope<TEvent>` (`SharedKernel.Contracts` v2.0.0, confirmed by direct source read) types them `string?` and names them `EventVersion`/`OccurredOn`; corrected in place. (2) `IEventPublisher`'s interface-contract prose never mentioned `EventEnvelope.Wrap<TEvent>()` as the mandated construction path, silently tolerating the exact raw-object-initializer defect P-340 fixes. (3) the DI Registration section's header-propagation example defined a from-scratch `TenantHeaderPropagator` class reading `ITenantProvider` directly — a name that now collides with the real shipped `TenantHeaderPropagator` type from P-345 — replaced with a `FeatureFlagHeaderPropagator` example plus the new seam-based `WithAmbientCorrelationPropagation()`/`WithTenantContext<T>()` worked example. New sections added: "Dead-letter and poison-message policy", "Ordered delivery via partition key", "Opt-in payload compression and encryption", "Bus-backed readiness probe". Ten new hard-violation rules added (raw `EventEnvelope<TEvent>` construction, asymmetric propagator invocation, non-configurable-ordering violation, bespoke crypto/compression primitives, independently-constructed probe connections, `IHealthCheck` inside this domain, invented ordering substitutes, hardcoded dead-letter naming) plus one existing rule ("never implement `IMessageHeaderPropagator` inside `SharedKernel.*`") narrowed with a named two-type exception. Packages table, Queued Capabilities table, Interface Contracts (`PublishContext.TenantId`/`PartitionKey`, `IMessageBusProbe`/`MessageBusHealth`, `ITenantContextAccessor`, `AmbientCorrelationHeaderPropagator`/`TenantHeaderPropagator`, `MassTransitMessageBusProbe`, `DeadLetterOptions`/`PayloadTransformOptions`, `ConsumerDefinitionBase.ConcurrentMessageLimit`, `MessagingDiagnostics.Meter`), AOT Compatibility, Test Rules, and DI Registration sections all updated (messaging-arch-planner, WO-054, P-340–P-349)
- [2026-08-05] SK.07.EnvelopeTenancy implemented (P-340/WO-054) — verified against the arch-planner's pre-written spec with zero prose discrepancies found: the CloudEvents compliance rule table, the `IEventPublisher`/`MassTransitEventPublisher` interface-contract prose, the `PublishContext.TenantId`/`WithTenantId` contract block, and the raw-object-initializer hard-violation rule had all already been written to their exact post-implementation shape ahead of dispatch. `PublishContext` (`SharedKernel.Messaging.Abstractions`) gained `TenantId` (`Guid?`, default `null`) and `WithTenantId(Guid)`, mirroring `CorrelationId`/`WithCorrelationId` exactly. `MassTransitEventPublisher.PublishEnvelope<TEvent>` (`SharedKernel.Messaging.MassTransit`) now builds the envelope exclusively via `SharedKernel.Contracts.Events.EventEnvelope.Wrap(integrationEvent, sourceService, correlationId, causationId, ctx?.TenantId)` — the raw `new EventEnvelope<TEvent> { ... }` object initializer is gone. `TenantId` resolution is explicit-override-only (`ctx?.TenantId`), no ambient fallback, matching `CausationId`'s existing resolution shape exactly — the ambient `TenantHeaderPropagator` path documented in the CloudEvents table is a forward reference to P-345 and requires no code here, since any registered `IMessageHeaderPropagator` (including a future `TenantHeaderPropagator`) already runs against the same `PublishContext` instance before `PublishEnvelope` reads it. Test-run discovery: MassTransit's in-memory `TestHarness` delivers the published payload **by reference** (no JSON serialization round-trip) when no consumer forces deserialization — empirically confirmed via a throwaway probe test (`ReferenceEquals(evt, envelope.Payload) == true`) before committing to a whole-envelope record-equality test (`EventEnvelope<TEvent>` is a sealed record; had the harness round-tripped through JSON, `DomainEvent.Id`'s get-only, non-`init` auto-property would not survive deserialization, since it has no public setter for STJ to write into — this would have made a naive whole-record equality assertion fail for reasons unrelated to the fix). 4 new tests added to `PublishContextTests.cs` (`WithTenantId_SetsTenantId`, `FluentChain`/`DefaultState` extended) and 3 new tests to `CloudEventsEnvelopeTests.cs` (`PublishAsync_WithTenantId_UsesProvidedValue`, `PublishAsync_WithoutTenantId_TenantIdIsNull`, `PublishAsync_ConstructsEnvelopeExclusivelyViaWrapFactory` — the last independently calls `EventEnvelope.Wrap<TEvent>()` with the same inputs `PublishEnvelope<TEvent>` received and asserts whole-record equality against the captured published envelope); all pre-existing `CloudEventsEnvelopeTests.cs` assertions re-run unmodified and still pass (regression-free). Unrelated build-blocker fixed to unblock this session's required test run: `SharedKernel.Messaging.MassTransit.Tests.csproj` pinned `Testcontainers.RabbitMq` at `4.4.0`, now downgraded relative to `16.Testing/SharedKernel.Testing`'s `4.13.0` floor (bumped there during a concurrent WO-054/P-352 `16.Testing` session) — NU1605 treated as a build error by default; re-pinned to `4.13.0` to match. `SharedKernel.Messaging.Abstractions.Tests` 51/51 passing (was 44); `SharedKernel.Messaging.MassTransit.Tests` 111/111 passing excluding `[Trait("Category","Integration")]` (was 105 — 3 new envelope tests + the pre-existing suite, no regressions). Packages-table and Queued-Capabilities-table P-340 references updated from "queued"/listed-pending to shipped; P-340 row removed from the Queued Capabilities table. Unblocks `16.Testing`'s T-74 (`Messaging/` `PublishContext` capture retrofit, previously blocked exactly on `PublishContext.TenantId` not existing) — that retrofit itself remains `16.Testing`'s own jurisdiction, not touched here (messaging-phase-implementer)
- [2026-08-06] SK.07.PropagationSymmetry implemented (P-341/WO-054) — verified against the arch-planner's pre-written spec (Interface Contracts' `IMessageBus.SendAsync`/`RequestAsync` prose, `WithHeaderPropagator<T>()`'s builder doc, the hard-violation rule, and the Test Rules bullet) with zero prose discrepancies found — only the `IMessageHeaderPropagator` interface's own top-level `NOTE:` still read "Invoked automatically at publish time for every IMessageBus.PublishAsync and IEventPublisher.PublishAsync call", contradicted by the corrected addendum immediately below it; merged into one non-contradictory NOTE covering all four call shapes and removed the now-redundant trailing "APPLIES TO ALL THREE DISPATCH VERBS" block. `MassTransitMessageBus.SendAsync<T>()` and `RequestAsync<TRequest,TResponse>()` (`MessageBus/MassTransitMessageBus.cs`) now call the existing private `BuildContextFromPropagators(configure: null)` helper before dispatch — previously only both `PublishAsync` overloads did, a confirmed silent asymmetry. `SendAsync` passes the built context to `ISendEndpoint.Send(command, pipe => ..., ct)` (`MassTransit.SendExecuteExtensions`), the identical `Action<SendContext<T>>` pipe shape `PublishAsync` already uses against `IPublishEndpoint.Publish`. `RequestAsync` required a different API path, confirmed via .NET reflection against the installed `MassTransit.Abstractions` 9.1.2 assembly rather than assumed: `IRequestClient<TRequest>.GetResponse<TResponse>` has no raw pipe-callback overload — only `RequestPipeConfiguratorCallback<TRequest>`, whose `IRequestPipeConfigurator<TRequest>` argument exposes `CorrelationId`/`Headers` only indirectly, by implementing `IPipeConfigurator<SendContext<TRequest>>`; reached via `MassTransit.DelegateConfigurationExtensions.UseExecute<TContext>(Action<TContext> callback)`, landing on the same `SendContext<T>` shape used elsewhere. New "MassTransit 9.x API notes" bullet records this exact signature chain for future sessions. 2 new TestHarness tests added in a new `PropagationSymmetryTests.cs`: `SendAsync_WithPropagator_HeaderPresentOnConsumedMessage` (explicit `ReceiveEndpoint`/route-map pairing so the point-to-point queue name is deterministic, rather than relying on `KebabCaseEndpointNameFormatter` convention matching) and `RequestAsync_WithPropagator_CorrelationIdMatchesPropagatedValue` (a responding consumer captures `ConsumeContext.CorrelationId` and asserts it equals the propagator-set value). All 3 pre-existing `HeaderPropagationTests.cs` tests re-run unmodified and still pass, proving the fan-out publish path is unchanged. `SharedKernel.Messaging.Abstractions.Tests` 51/51 passing (unchanged — this phase touched only the MassTransit package); `SharedKernel.Messaging.MassTransit.Tests` 113/113 passing excluding `[Trait("Category","Integration")]` (was 111 — 2 new propagation-symmetry tests, no regressions). Packages-table and Queued-Capabilities-table P-341 references updated from "queued"/listed-pending to shipped; P-341 row removed from the Queued Capabilities table (messaging-phase-implementer)
- [2026-08-06] SK.07.ConsumerConcurrency implemented (P-342/WO-054) — verified against the arch-planner's pre-written spec (Interface Contracts' `RabbitMqBusOptions.ConcurrentMessageLimit`, `AzureServiceBusOptions.MaxConcurrentCalls`, and `ConsumerDefinitionBase<TConsumer>.ConcurrentMessageLimit` blocks, plus the DI-recipe example) with zero prose discrepancies found — only the top-level package summary and the Queued Capabilities table still listed P-342 as pending. Two real MassTransit 9.1.2 API discoveries surfaced only via `dotnet build`/reflection, not from XML docs: (1) `IServiceBusEndpointConfigurator.MaxConcurrentCalls` is itself obsolete (`CS0618`: "Set ConcurrentMessageLimit instead (which is exactly what setting this property does)") — the fix targets the core `IBusFactoryConfigurator.ConcurrentMessageLimit` (`int?`) instead, which `IServiceBusBusFactoryConfigurator` inherits transitively, meaning RabbitMQ and Azure Service Bus now share one bus-level concurrency-default property; (2) that same `ConcurrentMessageLimit`/`PrefetchCount` pair is write-only (setter, no getter) on the bus-level `IBusFactoryConfigurator` (`CS0154` on read), unlike the per-endpoint `IReceiveEndpointConfigurator` version which has both accessors — tests assert bus-level wiring via NSubstitute's `Received(1).Property = value` setter-call pattern instead of reading the property back. `MessagingBusBuilder`'s previously-inline RabbitMQ configurator lambda was extracted into a new `internal static ConfigureRabbitMq(IRabbitMqBusFactoryConfigurator, RabbitMqBusOptions)` helper, mirroring the existing `ConfigureAzureServiceBus` helper (also changed `private` → `internal` for the same reason) — both are directly unit-testable via a substituted configurator without needing a live broker or `Build()`+DI-resolve round-trip. `ConsumerDefinitionBase<TConsumer>` gained `protected virtual new int? ConcurrentMessageLimit => null` — the `new` keyword is required (not merely stylistic) because `MassTransit.ConsumerDefinition<TConsumer>` (the real base class) already declares its own `ConcurrentMessageLimit` (public get, protected set); the new step (e) in the sealed `ConfigureConsumer` reads this shadowing property and assigns directly to `IReceiveEndpointConfigurator.ConcurrentMessageLimit`, exactly mirroring step (b)'s `PrefetchCount` handling, rather than relying on MassTransit's own base-class field. 6 new tests: `ConcurrencyLimitConfigurationTests.cs` (4, bus-level ASB/RabbitMQ helpers) and 2 more appended to `ConsumerDefinitionTests.cs` (per-consumer override reaching the endpoint configurator, and the no-override case leaving it untouched) — the latter two required promoting their consumer/message types from `internal` to `public`, since NSubstitute/Castle DynamicProxy cannot proxy `MassTransit.Abstractions`'s (strong-named) `IConsumerConfigurator<TConsumer>` closed over an `internal` type without an explicit `InternalsVisibleTo` grant to Castle's dynamic proxy assembly; documented as a new, narrow Test Rules exception to the file's usual `internal`-consumer convention. Two new "MassTransit 9.x API notes" bullets record both API discoveries plus a general note on using `dotnet build` compiler errors (and, as a fallback, a throwaway `System.Reflection` console app against the NuGet-cached DLLs with an `AssemblyResolve` handler) as ground truth for uncertain MassTransit interface shapes instead of guessing. `SharedKernel.Messaging.Abstractions.Tests` untouched this phase (0 build/test runs — Abstractions carries no `ConcurrentMessageLimit` surface); `SharedKernel.Messaging.MassTransit.Tests` 119/119 passing excluding `[Trait("Category","Integration")]` (was 113 — 6 new tests, no regressions). Packages-table and Queued-Capabilities-table P-342 references updated from "queued"/listed-pending to shipped; P-342 row removed from the Queued Capabilities table (messaging-phase-implementer)
- [2026-08-06] Brain-sync verification pass for SK.07.ConsumerConcurrency (P-342) — confirmed the phase-implementer's direct edits (package summary, Queued Capabilities table, 4 API-notes bullets, 1 Test Rules bullet, changelog entry) are complete, correctly placed, and consistent with the domain's existing structure; no further sub-domain edits warranted (sync-brain)
- [2026-08-06] Brain-sync verification pass for SK.07.DeadLetter (P-343) — confirmed the phase-implementer's direct edits (package summary, `DeadLetterOptions`/`WithDeadLetterPolicy()` contract blocks, "Dead-letter and poison-message policy" section, DI example, Queued Capabilities table row removal, 3 API-notes bullets, 1 Test Rules bullet, changelog entry) are complete, correctly placed, and consistent with the domain's existing structure — no stray "queued" references to P-343 remain anywhere in the file. No further sub-domain edits warranted (sync-brain)
- [2026-08-06] Brain-sync verification pass for SK.07.OrderedDelivery (P-344) — confirmed the phase-implementer's direct edits (package summary line, `PublishContext.PartitionKey`/`WithPartitionKey` contract block header status, "Ordered delivery via partition key" section expanded with the confirmed `RoutingKeyExtensions.TrySetRoutingKey`/`ServiceBusSendContextExtensions.SetSessionId` API names and a usage snippet, Queued Capabilities table row removal, changelog entry) are complete, correctly placed, and consistent with the domain's existing structure — no stray "queued" references to P-344 remain anywhere in the file. No further sub-domain edits warranted (sync-brain)
- [2026-08-06] SK.07.OrderedDelivery implemented (P-344/WO-054) — verified against the arch-planner's pre-written spec (`PublishContext.PartitionKey`/`WithPartitionKey` contract block, the "Ordered delivery via partition key" section, the invented-substitute hard-violation rule, and the DI example) with zero prose discrepancies found. `PublishContext` (`SharedKernel.Messaging.Abstractions`) gained `PartitionKey` (`string?`, default `null`) and `WithPartitionKey(string)`, mirroring `WithHeader`'s null/empty guard exactly. The RabbitMQ/Azure-Service-Bus mapping is implemented once, transport-agnostically, as a new internal `SendContext` extension — `PartitionKeySendContextExtensions.ApplyPartitionKey(this SendContext, string?)` (`MessageBus/PartitionKeySendContextExtensions.cs`) — called from the outgoing pipe callback in `MassTransitMessageBus.PublishAsync<T>`/`.SendAsync<T>` and `MassTransitEventPublisher.PublishEnvelope<TEvent>` (the latter's existing `Headers.Count > 0` pipe-callback guard was widened to `Headers.Count > 0 || PartitionKey is not null`, since a partition-key-only publish previously skipped the pipe callback entirely and would have silently dropped the mapping). Two real MassTransit 9.1.2 API discoveries, both confirmed empirically via a throwaway `AddMassTransitTestHarness`-based probe before committing to the implementation (a plain `dotnet run` console host hits MassTransit's real commercial-license gate on `IBusControl` construction — `AddMassTransitTestHarness`'s harness bus factory does not): (1) `MassTransit.RoutingKeyExtensions.TrySetRoutingKey(SendContext, string)` (in `MassTransit.Abstractions`, transport-agnostic) is the correct RabbitMQ mapping — confirmed never to throw regardless of configured transport; (2) `MassTransit.ServiceBusSendContextExtensions.SetSessionId(SendContext, string)` (in `MassTransit.Azure.ServiceBus.Core`) is the correct ASB mapping — confirmed to be a safe, silent no-op under any non-ASB transport, since the `ServiceBusSendContext` payload it writes to (`context.TryGetPayload<ServiceBusSendContext>`) is only materialized by MassTransit's real ASB transport at send time; `ServiceBusSendContext.SessionId` and `RoutingKeySendContext.RoutingKey` are both setter-only (no getter), so both halves of the mapping are asserted the same way `ConcurrencyLimitConfigurationTests.cs` (P-342) already established for write-only MassTransit configurator properties — via NSubstitute `Received(1).Property = value` setter-call assertions against a substituted `SendContext`/`ServiceBusSendContext`/`RoutingKeySendContext` payload chain, not by reading the property back. 9 new tests: 3 appended to `PublishContextTests.cs` (`WithPartitionKey_SetsPartitionKey`, null/empty `ArgumentException` cases, plus `FluentChain`/`DefaultState` extended) in `SharedKernel.Messaging.Abstractions.Tests`; a new `HarnessTests/OrderedDeliveryTests.cs` (6, `SharedKernel.Messaging.MassTransit.Tests`) covering the NSubstitute-based SessionId/RoutingKey assignment proof, the missing-ASB-payload no-throw case, the null-key no-setter-calls regression proof, and two `AddMassTransitTestHarness` end-to-end delivery-unchanged proofs (with and without `PartitionKey`); and a new `IntegrationTests/OrderedDeliveryIntegrationTests.cs` (1, real RabbitMQ broker via Testcontainers) publishing 5+5 interleaved messages under two distinct `PartitionKey` values against a single consumer instance pinned to `ConsumerDefinitionBase<TConsumer>.ConcurrentMessageLimit = 1` (to prevent `ConsumeAsync` completions from racing and reordering relative to RabbitMQ's own FIFO delivery order), asserting each key's own messages are observed in publish order — deliberately not a claim of total order across the combined interleaved stream, per the documented caveat. `SharedKernel.Messaging.Abstractions.Tests` 54/54 passing (was 51 — 3 new tests, no regressions). `SharedKernel.Messaging.MassTransit.Tests` 138/138 passing excluding `[Trait("Category","Integration")]` (was 132 — 6 new tests); 140/141 passing including Integration (was 133/134 — the new `OrderedDeliveryIntegrationTests` passes for real against a live broker; the one failure remains the pre-existing, documented, unrelated `RabbitMqIntegrationTests.PublishAndConsume_RealBroker_MessageDelivered` health-check DI gap first documented in the P-343 changelog entry, confirmed still reproducing identically via a `git stash`-isolated baseline run before restoring this phase's changes — not introduced or touched by this phase). Packages-table and Queued-Capabilities-table P-344 references updated from "queued"/listed-pending to shipped; P-344 row removed from the Queued Capabilities table; "Ordered delivery via partition key" section expanded with the confirmed API names and a short usage snippet (messaging-phase-implementer)
- [2026-08-06] SK.07.AmbientPropagation implemented (P-345/WO-054) — verified against the arch-planner's pre-written spec (Interface Contracts' `ITenantContextAccessor`/`TenantContext/` block, the "Built-in header propagators" block for `AmbientCorrelationHeaderPropagator`/`TenantHeaderPropagator`, both `MessagingBusBuilder` builder-method docs, the narrowed hard-violation rule, and the DI Registration worked example) with zero prose discrepancies found — the arch-planner's WO-054 planning pass had already replaced the stale from-scratch `TenantHeaderPropagator`/`ITenantProvider` DI example with the `FeatureFlagHeaderPropagator` + `WithAmbientCorrelationPropagation()`/`WithTenantContext<AppTenantContextAccessor>()` worked example ahead of this session. Implemented exactly as documented: `ITenantContextAccessor` (`TenantContext/ITenantContextAccessor.cs`, `SharedKernel.Messaging.Abstractions`) — single `Guid? TenantId { get; }` member, zero new NuGet dependencies; `AmbientCorrelationHeaderPropagator`/`TenantHeaderPropagator` (`HeaderPropagation/`, `SharedKernel.Messaging.MassTransit`) — both sealed, both implement `IMessageHeaderPropagator`; `MessagingBusBuilder.WithAmbientCorrelationPropagation()`/`.WithTenantContext<TAccessor>()`. `AmbientCorrelationHeaderPropagator.Propagate` converts `Activity.Current.TraceId` to `PublishContext.CorrelationId` via `Guid.Parse(activity.TraceId.ToString())` — the same string-round-trip idiom `MassTransitEventPublisher.PublishEnvelope`'s existing ambient-fallback path already uses, chosen over a manual `ActivityTraceId.CopyTo(Span<byte>)`/`new Guid(ReadOnlySpan<byte>)` byte-copy for consistency with that precedent; no exact byte-mapping guarantee is implied or required by the contract, only that the same ambient trace consistently maps to the same `Guid`. `TenantHeaderPropagator` takes `ITenantContextAccessor? tenantContextAccessor = null` as a constructor parameter with a default value rather than manually resolving `IServiceProvider.GetService<T>()` in `Propagate` — confirmed this is the same "resolved via `GetService<T>()`, not `GetRequiredService<T>()`" behavior the spec calls for: .NET's built-in DI container treats an unregistered service type behind an optional (default-valued) constructor parameter as resolvable-to-`null` rather than throwing, exactly like `GetService<T>()` vs `GetRequiredService<T>()`; `MassTransitEventPublisher`'s existing constructor-injected `IEnumerable<IMessageHeaderPropagator>` pattern picks up both new propagators automatically once registered — no changes needed there. 6 new tests in a new `HarnessTests/AmbientPropagationTests.cs`, deliberately driven through the real `MessagingBusBuilder` fluent API (`services.AddSharedKernelMessaging(...).WithAmbientCorrelationPropagation()`/`.WithTenantContext<ApFakeTenantContextAccessor>()`) rather than manually registering the propagator types via `services.AddScoped<IMessageHeaderPropagator, T>()`, since the builder methods themselves are the surface under test, not just the propagators they wire up (unlike the pre-existing `HeaderPropagationTests.cs`/`PropagationSymmetryTests.cs`, which test hand-rolled propagators registered by hand and were left unmodified): `PublishAsync`/`SendAsync`/`RequestAsync_WithAmbientCorrelationPropagation_PopulatesCorrelationIdFromActivityTraceId` (all three dispatch verbs, `using var activity = new Activity(...).Start();` sets `Activity.Current` with no `ActivityListener` required — that machinery is only needed for the unrelated `MessagingDiagnostics.ActivitySource` tracing spans), `PublishAsync_WithTenantContextRegistered_PopulatesEnvelopeTenantIdFromAccessor`, `PublishAsync_WithoutTenantContextRegistered_EnvelopeTenantIdIsNull_NoException` (the provable-no-op proof — asserts both "no exception" and "`TenantId` stays `null`"), and `PublishAsync_ExplicitCallback_OverridesBothAmbientPropagators` (both propagators registered simultaneously; an explicit `Action<PublishContext>` wins on both `CorrelationId` and `TenantId`, consistent with the existing propagator-precedence rule). `SharedKernel.Messaging.Abstractions.Tests` untouched this phase (0 build/test runs — `ContractShapeTests.cs` is scoped to the original four T-01/T-02 core contracts only and was never extended for any later-added interface, e.g. `IIdempotencyStore`/`IMessageHeaderPropagator`/`IMessageBusProbe`; `ITenantContextAccessor` follows that same precedent and stays untested at the contract-shape level). `SharedKernel.Messaging.MassTransit.Tests` 144/144 passing excluding `[Trait("Category","Integration")]` (was 138 — 6 new tests, no regressions). Packages-table and Queued-Capabilities-table P-345 references updated from "queued"/listed-pending to shipped; P-345 row removed from the Queued Capabilities table; one new Test Rules bullet added documenting the builder-API-driven propagator test pattern and the `Activity.Start()`-without-a-listener technique (messaging-phase-implementer)
- [2026-08-06] SK.07.DeadLetter implemented (P-343/WO-054) — verified against the arch-planner's pre-written spec (Interface Contracts' `DeadLetterOptions`/`WithDeadLetterPolicy()` blocks, the "Dead-letter and poison-message policy" section, the hardcoded-naming hard-violation rule, and the DI example) and found ONE genuine discrepancy requiring a documented, evidence-based correction rather than a straight implementation: `QueueNameSuffix` — described as applying "to the receive endpoint's error-queue naming" — cannot be wired in MassTransit 9.1.2. Confirmed exhaustively via reflection (the complete `IRabbitMqSendTopologyConfigurator`/`IRabbitMqReceiveEndpointConfigurator`/`IRabbitMqQueueConfigurator`/`IRabbitMqQueueBindingConfigurator`/`IRabbitMqExchangeConfigurator`/`IRabbitMqExchangeBindingConfigurator` surface exposes no settable name/formatter) and via direct IL user-string inspection of `MassTransit.RabbitMqTransport.dll` (the `"_error"`/`"_skipped"` suffixes are fixed literal constants, not derived from a configurable format). `IRabbitMqReceiveEndpointConfigurator.BindDeadLetterQueue` does accept an explicit name but wires RabbitMQ's native NACK/TTL-triggered `x-dead-letter-exchange` mechanism, which a MassTransit consumer exception never engages (MassTransit republishes to its own fault transport and ACKs the original delivery) — using it here would silently produce a queue that never receives the poison messages this feature exists to route. Resolved by shipping the achievable half honestly rather than faking the rest: `DeadLetterOptions.QueueNameSuffix` is kept (forward-compatible, matches the already-cross-referenced test/DI-example naming) but its XML doc, the "Dead-letter and poison-message policy" section, `WithDeadLetterPolicy()`'s doc, the DI example, and a new "MassTransit 9.x API notes" bullet all now state plainly that it has no observable effect in this MassTransit version; `MessageTimeToLive` is genuinely wired via `IRabbitMqSendTopologyConfigurator.ConfigureErrorSettings`/`.ConfigureDeadLetterSettings` → `SetQueueArgument("x-message-ttl", TimeSpan)`, confirmed (by method-name correspondence with `RabbitMqReceiveEndpointBuilder.CreateErrorTransport()`/`.CreateDeadLetterTransport()`) to configure the actual queue a retry-exhausted/non-retryable-exception message is routed to. Implemented: `Options/DeadLetterOptions.cs` (`QueueNameSuffix` default `"_error"`, `MessageTimeToLive` default `null`, `SectionName`); `DeadLetter/DeadLetterPolicyAdvisoryHostedService.cs` (new `DeadLetter/` folder, mirroring `SchemaEvolution/`'s per-feature-folder convention) — logs `EventId` 7010 (next free slot after `VersionTranslatorNoConsumer`'s 7009) at `Warning` via `[LoggerMessage]` on a constructor-injected `ILogger<T>` field, registered by `Build()` only when `WithDeadLetterPolicy()` was called under the Azure Service Bus transport, mirroring `TranslatorRegistrationValidationHostedService`'s deferred-advisory-at-startup pattern (`Build()` must not call `BuildServiceProvider()`); `MessagingBusBuilder.WithDeadLetterPolicy(Action<DeadLetterOptions>?)` plus a new `internal static ConfigureDeadLetterPolicy(IRabbitMqBusFactoryConfigurator, DeadLetterOptions)` helper (mirroring `ConfigureRabbitMq`/`ConfigureAzureServiceBus`'s `internal`-for-testability precedent from P-342), invoked inside the existing `UsingRabbitMq` configurator lambda alongside the idempotency filter and resilience wiring. 19 new tests: `DeadLetterPolicyConfigurationTests.cs` (7 — `DeadLetterOptions` defaults, `ConfigureDeadLetterPolicy`'s TTL wiring on both `ConfigureErrorSettings`/`ConfigureDeadLetterSettings` via NSubstitute `Arg.Do<T>` delegate capture-and-invoke, the unset-TTL no-op case, and two `Build()` smoke tests), `DeadLetterPolicyAdvisoryTests.cs` (6 — direct hosted-service `StartAsync`/`StopAsync` unit tests via a capturing `ILogger<T>` double mirroring `VtCapturingLoggerOf<T>`, plus builder-level conditional-registration tests asserting the advisory service is registered under ASB+`WithDeadLetterPolicy()` and absent under every other combination), and a new `IntegrationTests/DeadLetterIntegrationTests.cs` (1, real RabbitMQ broker via Testcontainers) proving the achievable contract end-to-end: a consumer whose `ConsumerDefinitionBase.NonRetryableExceptions` classifies a thrown exception as fatal is routed to MassTransit's real dead-letter/fault destination, observed via a registered `IFaultConsumer<TMessage>` (`AddFaultConsumer`), with the endpoint starting successfully proving the configured `x-message-ttl` argument was accepted by the broker. Two reusable discoveries recorded as new "MassTransit 9.x API notes": NSubstitute's `Arg.Do<T>` capture on a property setter must be armed *before* the exercise call (placing it inside a later `Received()` assertion compiles but the callback never fires — cost one debugging round-trip on the first two tests); and `ServiceCollection.GetServices<IHostedService>()` against a real broker throws `InvalidOperationException` unless `.AddLogging()` was called first, because MassTransit's own default health-check hosted service constructor-injects `ILogger<T>` — this is a pre-existing gap also reproducible in `RabbitMqIntegrationTests.cs` (left unmodified, out of this phase's scope) but fixed locally in the new test so it proves real behavior rather than reproducing a known, unrelated failure. `SharedKernel.Messaging.Abstractions.Tests` untouched this phase (0 build/test runs — Abstractions carries no dead-letter surface); `SharedKernel.Messaging.MassTransit.Tests` 132/132 passing excluding `[Trait("Category","Integration")]` (was 119 — 13 new tests); 133/134 passing including Integration (was 1/1 pre-existing-failure-only — the new `DeadLetterIntegrationTests` passes for real against a live broker; the one failure is the pre-existing, documented, unrelated `RabbitMqIntegrationTests` health-check DI gap, reproduced identically, not introduced by this phase). Packages-table and Queued-Capabilities-table P-343 references updated from "queued"/listed-pending to shipped; P-343 row removed from the Queued Capabilities table (messaging-phase-implementer)
- [2026-08-06] Brain-sync verification pass for SK.07.AmbientPropagation (P-345) — confirmed the phase-implementer's direct edits (package summary line, Queued Capabilities table row removal, 1 new Test Rules bullet, changelog entry) are complete, correctly placed, and consistent with the domain's existing structure — no stray "queued"/pending references to P-345 remain anywhere in the file; `ITenantContextAccessor`/`AmbientCorrelationHeaderPropagator`/`TenantHeaderPropagator` all appear exactly where the Interface Contracts and DI Registration sections already documented them ahead of dispatch, with zero prose discrepancies. Root `CLAUDE.md` requires no update — P-345 introduced no new package, no new cross-domain abstraction/provider pair, and no layering-rule change; the root "What Goes Where" row for this capability was already updated in-place by the prior `[2026-08-05] P-340 shipped` root changelog entry and needs no further edit. No further sub-domain edits warranted (sync-brain)
- [2026-08-06] SK.07.PayloadTransform implemented (P-346/WO-054) — opt-in compress-then-encrypt (publish) / decrypt-then-decompress (consume) payload transform built entirely on `01.Core`'s `IPayloadCompressor` (`SharedKernel.Compression`) and `ISymmetricEncryptionService` (`SharedKernel.Cryptography`); zero new bespoke compression/cryptography primitive introduced in this domain. Implemented in a new `Serialization/` folder (`SharedKernel.Messaging.MassTransit`): `PayloadTransformOptions` (`Options/PayloadTransformOptions.cs`, `EnableCompression`/`EnableEncryption` both default `false`, `SectionName = "SharedKernel:Messaging:PayloadTransform"`); `PayloadTransformSerializerFactory : ISerializerFactory`, `PayloadTransformMessageSerializer : IMessageSerializer`, `PayloadTransformMessageDeserializer : IMessageDeserializer` — a decorator trio wrapping a freshly-constructed `MassTransit.Configuration.SystemTextJsonMessageSerializerFactory` (matches the bus's own unconfigured default exactly); `PayloadTransformMismatchException` (sealed, wraps the underlying decrypt/decompress/deserialize failure); `EncryptedPayloadWireCodec` (internal, length-prefixed flat-byte-array packing of `EncryptedPayload`, distinct from `ISymmetricEncryptionService.EncryptToString`'s Base64 string format); `MessagingBusBuilder.WithPayloadTransform(Action<PayloadTransformOptions>?)` and `internal static ConfigurePayloadTransform(IBusFactoryConfigurator, IBusRegistrationContext, PayloadTransformOptions)` (mirrors `ConfigureRabbitMq`/`ConfigureAzureServiceBus`/`ConfigureDeadLetterPolicy`'s `internal`-for-testability precedent), wired inside both the RabbitMQ and Azure Service Bus `Using{Transport}` configurator lambdas before `ConfigureEndpoints`. `Build()`-time guard added alongside the existing `WithIdempotency()` guard, checking `Services.FirstOrDefault(d => d.ServiceType == typeof(IPayloadCompressor)/typeof(ISymmetricEncryptionService))` per enabled flag — no `BuildServiceProvider()` call. Two genuine, non-obvious MassTransit 9.1.2 API discoveries, both confirmed empirically (not assumed from XML docs) via a scratch xUnit-project probe (`dotnet test`-based reflection, not a `dotnet run` console — the latter hits MassTransit's real commercial-license gate, per the established `[[project_masstransit_ordered_delivery]]` technique) and now recorded as new "MassTransit 9.x API notes" bullets: (1) `IBusFactoryConfigurator.AddSerializer(factory, isSerializer: true)` alone only changes the outgoing-message producer — MassTransit's own already-registered default deserializer for the same content type remains active on receive (registration is additive-by-content-type, not overwrite-by-content-type), reproduced live as a `SerializationException`/`JsonException` from the untouched default deserializer choking on genuinely-transformed bytes; fixed by `ClearSerialization()` followed by both `AddSerializer(factory, isSerializer: true)` AND an explicit `AddDeserializer(factory, isDefault: true)` (the latter alone resolves a second, distinct `ConfigurationException: No default content type specified and more than one deserializer was configured`); (2) `IMessageDeserializer.Deserialize(ReceiveContext)` — not the `(MessageBody, Headers, Uri)` overload — is the entry point MassTransit's real receive pipeline invokes; implemented by mirroring MassTransit's own shipped `SystemTextJsonMessageSerializer.Deserialize(ReceiveContext)` exactly, via the real public `MassTransit.Serialization.BodyConsumeContext(ReceiveContext, SerializerContext)` constructor. Test coverage (all in `SharedKernel.Messaging.MassTransit.Tests`, no `SharedKernel.Messaging.Abstractions.Tests` changes — this phase touches only the MassTransit package): `BuilderTests/PayloadTransformConfigurationTests.cs` (10 — options defaults, `Build()`-time guard both directions, default-disabled `Build()` success, and three `ConfigurePayloadTransform` DI-resolution wiring tests against a substituted `IBusFactoryConfigurator`/`IBusRegistrationContext`, mirroring `ConcurrencyLimitConfigurationTests`'s established NSubstitute pattern for internal `Configure*` helpers); `HarnessTests/PayloadTransformRoundTripTests.cs` (2 — a genuine end-to-end round trip via `AddMassTransitTestHarness`/`UsingInMemory` with REAL `BrotliPayloadCompressor` and AES-GCM `AesGcmEncryptionService` (backed by `16.Testing`'s `FakeEncryptionKeyProvider` for key material only — the encryption itself is genuine, not faked), plus the default-disabled regression proof); `SerializationTests/EncryptedPayloadWireCodecTests.cs` (6 — round trip incl. empty ciphertext/KeyId, and malformed-input `FormatException` cases incl. feeding arbitrary JSON bytes into the codec, the exact mismatch scenario); `SerializationTests/PayloadTransformMismatchTests.cs` (3 — both mismatch directions proven by exercising `PayloadTransformMessageDeserializer` directly against real transformed/untransformed bytes, using `MassTransit.Configuration.SystemTextJsonMessageSerializerFactory` and `MassTransit.Serialization.DictionarySendHeaders` — both real, public MassTransit types — rather than a hand-rolled `Headers`/serializer substitute). `SharedKernel.Messaging.MassTransit.Tests` 165/165 passing excluding `[Trait("Category","Integration")]` (was 144 — 21 new tests, no regressions); RabbitMQ-broker `[Trait("Category","Integration")]` tests unaffected (this phase adds no new integration test). Packages-table entry updated to describe the shipped `Serialization/` decorator trio and lists `SharedKernel.Compression`/`SharedKernel.Cryptography` as shipped (not queued) references; P-346 row removed from the Queued Capabilities table; "Opt-in payload compression and encryption" section rewritten from a design-only sketch to the full shipped implementation shape; two new hard-violation-adjacent MassTransit API-notes bullets added (messaging-phase-implementer)
- [2026-08-06] SK.07.ReadinessProbe implemented (P-347/WO-054) — verified against the arch-planner's pre-written spec (Interface Contracts' `IMessageBusProbe`/`MessageBusHealth` block, the "Bus readiness probe" MassTransit-package block, the DI Registration worked example, and the two P-347 hard-violation rules) with zero prose discrepancies found; the design was already written to the exact post-implementation shape ahead of dispatch. Implemented exactly as documented: `IMessageBusProbe` (`MessageBus/IMessageBusProbe.cs`) — single `Task<MessageBusHealth> ProbeAsync(CancellationToken)` member — and `MessageBusHealth` (`MessageBus/MessageBusHealth.cs`, sealed record, `IsHealthy`/`Description`) in `SharedKernel.Messaging.Abstractions`; `MassTransitMessageBusProbe` (`MessageBus/MassTransitMessageBusProbe.cs`, internal sealed) in `SharedKernel.Messaging.MassTransit`, registered as a singleton unconditionally inside `MessagingBusBuilder.Build()` alongside the existing `IMessageBus`/`IEventPublisher` registrations. The exact MassTransit 9.x bus-health API was not documented anywhere in this file ahead of time and was discovered this session via a dedicated `dotnet test`-based reflection probe (never `dotnet run`, per the established license-gate-avoidance technique): MassTransit's own bus-health surface is `MassTransit.Monitoring.BusHealthCheck` (implements `Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck`, `ctor(IBusInstance)`), the same internal type MassTransit wires into a host's own `AddHealthChecks()` pipeline via `ConfigureBusHealthCheckServiceOptions`; `CheckHealthAsync` throws `NullReferenceException` unless `HealthCheckContext.Registration` is explicitly set (`new HealthCheckContext { Registration = new HealthCheckRegistration(name, healthCheck, null, null) }`); and merely *resolving* `IBusInstance` from a plain `AddMassTransit`-configured `ServiceCollection` (no `AddMassTransitTestHarness`) hits the real-bus license gate immediately — confirmed via three throwaway `dotnet test` probes against a live `MassTransit.Testing.TestHarness` bus, which returned `Status=Healthy, Description="Ready"` when started, and `Status=Unhealthy, Description="Not ready: not started"`/`"Not ready: stopped"` for the two unhealthy scenarios respectively — these exact strings are MassTransit's own, not authored here. Added a new `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` 10.0.5 `PackageReference` to `SharedKernel.Messaging.MassTransit.csproj` — deliberately the Abstractions-only package (types only: `HealthCheckContext`/`HealthCheckResult`/`HealthStatus`/`HealthCheckRegistration`), never the full `Microsoft.Extensions.Diagnostics.HealthChecks` package's `AddHealthChecks()`/`HealthCheckService` machinery, which stays `13.ServiceDefaults`'s concern. `MessageBusHealth.IsHealthy` maps `HealthStatus.Healthy` → `true`; both `Degraded` and `Unhealthy` → `false` (a binary K8s-readiness-probe-shaped mapping — the real MassTransit bus health check reports `Degraded`/`Unhealthy` distinctly, but this domain's own two-state contract does not need the middle state). Unrelated build-blocker fixed to unblock this session's required test run: `16.Testing/SharedKernel.Testing.csproj` pinned `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` at `10.0.0` (used by its own `ServiceDefaults/HealthCheckAssertionExtensions.cs`), now below the `>=10.0.5` floor this phase's new MassTransit package reference pulls in transitively through `SharedKernel.Testing`'s existing `ProjectReference` to `SharedKernel.Messaging.MassTransit` — NU1605 treated as a build error by default; bumped to `10.0.5` to match, the same floor-bump pattern already applied repeatedly elsewhere on this platform (e.g. `Testcontainers.RabbitMq` 4.4.0→4.13.0 at P-340, `Microsoft.Extensions.DependencyInjection` 10.0.5→10.0.9 at WO-030). 3 new tests added to a new `HarnessTests/ReadinessProbeTests.cs` (`ProbeAsync_WithStartedBus_ReportsHealthy`, `ProbeAsync_WithBusNeverStarted_ReportsUnhealthyWithDescription`, `ProbeAsync_WithStoppedBus_ReportsUnhealthyWithDescription`, all via `AddMassTransitTestHarness()` with `MassTransitMessageBusProbe` registered manually) plus 1 new registration-shape guard test appended to `BuilderTests/MessagingBusBuilderGuardTests.cs` (`Build_AfterUseRabbitMq_RegistersIMessageBusProbe_AsSingleton_Unconditionally`, asserting against the raw `ServiceDescriptor` rather than a resolved instance, to avoid the real-bus license gate a `UseRabbitMq(...).Build()` + plain `BuildServiceProvider()` resolution of `IMessageBusProbe` would hit in this environment with no MassTransit license configured). `SharedKernel.Messaging.Abstractions.Tests` 54/54 passing (unchanged — `ContractShapeTests.cs` was never extended for `IMessageBusProbe`, consistent with its documented "scoped to the original four T-01/T-02 core contracts only" precedent, matching how `IIdempotencyStore`/`IMessageHeaderPropagator` were likewise never added there); `SharedKernel.Messaging.MassTransit.Tests` 169/169 passing excluding `[Trait("Category","Integration")]` (was 165 — 4 new tests, no regressions). Packages-table entry, Queued-Capabilities-table (P-347 row removed), Technology Stack table, "Bus readiness probe" MassTransit-package interface-contract block, and Test Rules all updated; two new "MassTransit 9.x API notes" bullets added (messaging-phase-implementer)
- [2026-08-06] Brain-sync verification pass for SK.07.ReadinessProbe (P-347) — confirmed the phase-implementer's direct edits (package summary line, Technology Stack table row, Queued Capabilities table row removal, "Bus readiness probe" interface-contract block naming the exact discovered MassTransit API, two new "MassTransit 9.x API notes" bullets, one new Test Rules bullet, changelog entry) are complete, correctly placed, and consistent with the domain's existing structure — no stray "queued"/pending references to P-347 remain anywhere in the file. No further sub-domain edits warranted (sync-brain)
- [2026-08-06] SK.07.DiagnosticsCoverage implemented (P-348/WO-054) — verified against the arch-planner's pre-written spec (the `MessagingDiagnostics.Meter` block, the P-348 line inside `Consumer and publisher instrumentation`, and the AOT/hard-violation notes already forward-referencing P-348) with zero prose discrepancies found; the design was already written to the exact post-implementation shape ahead of dispatch. Implemented exactly as documented: `MessagingDiagnostics.Meter` (`Diagnostics/MessagingDiagnostics.cs`, static readonly `Meter("SharedKernel.Messaging", "1.0.0")`) with its five instruments — `PublishCounter`/`ConsumeCounter`/`ConsumeDurationHistogram`/`RetryCounter`/`FaultCounter`; `MassTransitMessageBus.SendAsync<T>()`/`.RequestAsync<TRequest,TResponse>()`/`.ExecuteRoutingSlipAsync()` each now start/dispose their own child `Activity` (`"MessageBus.Send"`/`"MessageBus.Request"`/`"MessageBus.ExecuteRoutingSlip"`) via `MessagingDiagnostics.ActivitySource` — the three verbs that previously produced no activity at all; `ConsumerBase<TMessage>.Consume()` increments `RetryCounter` (before dispatch, when `GetRetryAttempt() > 0`), `ConsumeCounter` (after `ConsumeAsync` succeeds), and records `ConsumeDurationHistogram` unconditionally in a `finally` block; `MassTransitEventPublisher.PublishEnvelope<TEvent>()` and both `MassTransitMessageBus.PublishAsync<T>()` overloads increment `PublishCounter` only after their underlying MassTransit publish call completes without throwing (both methods became `async`/awaited internally specifically so the increment happens post-await, not merely post-Task-construction); `FaultConsumerAdapter<TMessage,TFaultConsumer>.Consume()` increments `FaultCounter` unconditionally, before invoking the registered `IFaultConsumer<TMessage>`. One genuine, non-obvious MassTransit 9.1.2 API investigation this session (not assumed from XML docs, which document neither `IRetryObserver` nor `RetryContextExtensions` at all): confirmed via reflection over the shipped `MassTransit.dll` that `IRetryObserver`/`IRetryObserverConnector` exist in the public API but NO configurator reachable from `MessagingBusBuilder` (`IBusFactoryConfigurator`, `IReceiveEndpointConfigurator`, `IBus`, `IBusControl`) implements `IRetryObserverConnector` in this build — `ConnectRetryObserver` is a dead end. The working alternative, confirmed via a live `dotnet test`-based `AddMassTransitTestHarness` probe (never `dotnet run`, per the established license-gate-avoidance technique — see `[[project_masstransit_ordered_delivery]]`): `MassTransit.RetryContextExtensions.GetRetryAttempt(ConsumeContext)` returns `0` on the original delivery and `1, 2, ...` on each subsequent retry-filter re-delivery (verified with `UseMessageRetry(r => r.Incremental(2, ...))`, three consumer invocations observed as `GetRetryAttempt=0/1/2`), and returns `0` safely — never throws — when no retry middleware is configured at all (verified separately with no `UseMessageRetry` call in the pipeline). This is a per-invocation context inspection, not a subscribed observer callback, and is documented as such (not as the originally-anticipated `IRetryObserver` hook) in both the `RetryCounter` XML doc and a new "RETRY OBSERVATION GAP" note in the Interface Contracts section. New `#### Full dispatch-surface verb coverage` section added with a 7-row markdown table (`Consume`, `EventPublisher.PublishAsync`, `MessageBus.PublishAsync`, `SendAsync`, `RequestAsync`, `ExecuteRoutingSlipAsync`, `FaultConsumerAdapter.Consume`) naming every `Activity`/`Meter` touch point — the DC-09 "full verb-coverage table" deliverable. Test coverage: 8 new tests in a new `HarnessTests/DiagnosticsCoverageTests.cs` — 3 `ActivityListener` tests (`SendAsync`/`RequestAsync`/`ExecuteRoutingSlipAsync` each produce their named activity with correct tags; `SendAsync`/`ExecuteRoutingSlipAsync` built via NSubstitute-mocked `ISendEndpointProvider`/`ISendEndpoint` following the existing `RoutingTests.cs`/`RoutingSlipTests.cs` mock patterns rather than a full `TestHarness`, since only activity emission — not real delivery — is under test; `RequestAsync` uses a real `TestHarness` with a responding consumer since `CreateRequestClient<TRequest>()` requires MassTransit's real DI-registered `IClientFactory`) and 5 `MeterListener` tests (one per instrument, `EventPublisher`/`MessageBus` publish paths both covered separately for `PublishCounter`, following `MeterListener.InstrumentPublished`/`SetMeasurementEventCallback<long/double>`/`.Start()`, confirmed to replay already-published static instruments). All new tests filter assertions by a unique message/event-type tag value per test (`Dc*` type-name prefix), following the same parallel-test-isolation-hazard mitigation the P-172 `ActivityListener` tests established — confirmed necessary here too since `MessagingDiagnostics.Meter` is the same kind of process-wide static instrument as `ActivitySource`. Two new Test Rules bullets added (`Meter`/`MeterListener` assertion pattern; `GetRetryAttempt()` retry-counter test pattern). `SharedKernel.Messaging.Abstractions.Tests` untouched this phase (0 build/test runs — this phase touches only the MassTransit package); `SharedKernel.Messaging.MassTransit.Tests` 177/177 passing excluding `[Trait("Category","Integration")]` (was 169 — 8 new tests, no regressions; confirmed via 8 additional full-suite runs plus 5 isolated re-runs of just the new test file, all clean, after one single-run flake that did not reproduce and was traced to none of the new tests when re-run in isolation). Packages-table entry, Queued-Capabilities-table (P-348 row removed), "Messaging diagnostics"/"Full dispatch-surface verb coverage"/"Consumer and publisher instrumentation" Interface-Contracts sections, the static-mutable-state hard-violation exception wording, and Test Rules all updated (messaging-phase-implementer)
- [2026-08-07] SK.07.PackagingRecipes implemented (P-349/WO-054) — zero production-code behavior change (PR-08); packaging metadata and documentation only. Verified against the arch-planner's pre-written spec and found ONE genuine premise discrepancy requiring an adapted approach rather than a straight edit: the spec described "embeds both packages' existing on-disk `README.md`" and instructed PR-05/PR-06 to add recipes "to `SharedKernel.Messaging.Abstractions/README.md`"/"`SharedKernel.Messaging.MassTransit/README.md`" — but neither per-package `README.md` existed on disk anywhere in `07.Messaging` (confirmed via `find`/`Glob`); only an empty domain-root `07.Messaging/README.md` existed. This diverges from the `02.Caching`/WO-050/P-301 precedent this phase cites, where 2 of 7 packages already had authored READMEs and the fix was purely mechanical wiring for the other 5. Resolved by authoring both package-level `README.md` files from scratch (matching the `02.Caching` package-README structure: title, description, Install, Usage, a Recipe section, Layering, and a doc-link footer) rather than treating PR-01/PR-04 as a pure wiring task — the phase's actual intent (ship a discoverable, embedded README with working recipes) is unaffected by the stale premise about pre-existing files. PR-02/PR-05: `SharedKernel.Messaging.Abstractions/README.md` gained a complete `RedisIdempotencyStore : IIdempotencyStore` reference implementation backed by `IDistributedCache`, sourcing its retention window from `IdempotencyOptions.ExpiryWindow` via `IOptions<IdempotencyOptions>`; the same class body was added to this file's own DI Registration section (replacing the prior bare `services.AddScoped<IIdempotencyStore, RedisIdempotencyStore>();` one-liner that referenced a type with no accompanying implementation anywhere). PR-03/PR-06: found the tenant-context recipe's own premise already stale in a different way — the "existing from-scratch `TenantHeaderPropagator`/`ITenantProvider` DI example" PR-03 describes replacing was already replaced by the `AppTenantContextAccessor`/`WithAmbientCorrelationPropagation()`/`WithTenantContext<T>()` worked example when `SK.07.AmbientPropagation` (P-345) shipped — confirmed via `git`-independent full-file grep for `ITenantProvider`, which returned only the two lines inside that already-correct example. No CLAUDE.md DI Registration change was needed for PR-03/PR-06; the identical recipe was added to the new `SharedKernel.Messaging.MassTransit/README.md`'s own "Recipe" section, worded for a package consumer rather than a domain-brain reader. PR-01/PR-04: `<PackageReadmeFile>README.md</PackageReadmeFile>` added to both `.csproj` `<PropertyGroup>`s and `<None Include="README.md" Pack="true" PackagePath="\" />` added to both, mirroring the `02.Caching.Redis.PubSub.csproj` wiring pattern exactly. PR-07: `dotnet pack --configuration Release --output ./nupkgs` run for both packages — zero `NU5039`/`NU5128` (missing-readme) warnings for either; confirmed via `unzip -l` that `README.md` is present at each `.nupkg`'s root and via `unzip -p ... *.nuspec` that `<readme>README.md</readme>` is present in both generated nuspecs. Packages-table entries for both packages and the Queued Capabilities table (P-349 row removed — its capability is now shipped, not queued) updated (messaging-phase-implementer)
- [2026-08-07] Brain-sync verification pass for SK.07.PackagingRecipes (P-349) — confirmed the phase-implementer's direct edits (both Packages-table entries, the expanded `RedisIdempotencyStore` DI Registration example, Queued Capabilities table row removal, changelog entry) are complete, correctly placed, and consistent with the domain's existing structure — no stray "queued"/pending references to P-349 remain anywhere in the file. This closes WO-054's entire `07.Messaging` scope end to end (P-340–P-349, 10 phase keys, 99/99 tasks). Root `CLAUDE.md`'s Folder Map row 07 still read "...design-locked, queued P-349/WO-054 (P-340–P-348 shipped)" — a genuine root-brain gap, corrected in a separate root-mode sync-brain pass (sync-brain)
- [2026-09-08] WO-081 / P-499 — new Phase: PayloadTransformAad queued (`PA-01`→`PA-18`, 18 tasks: 6 design-locked `●`, 12 implementation `○` blocked on `01.Core`'s upstream repack), migrating the P-346 payload-transform encryption path onto `01.Core`'s `SK.01.P491` (required AAD on `ISymmetricEncryptionService`) and `SK.01.P492` (`ISynchronousEncryptionKeyProvider` capability gate). Read `01.Core/CLAUDE.md`/`state-map.md`'s `SK.01.P491`/`SK.01.P492` sections (design-locked this session by `core-arch-planner`) before designing against them. **Refuted the phase brief's own literal acceptance criterion** ("no longer calls a synchronous member") on source-verified grounds, mirroring `02.Caching`'s/`06.Persistence`'s own earlier refutations in this wave: confirmed by direct .NET reflection against the installed `MassTransit.Abstractions` 9.1.2 assembly that `IMessageSerializer.GetMessageBody<T>`/`IMessageDeserializer.Deserialize` are both hard-synchronous with no async overload anywhere in this pipeline stage — there is no extensibility point that would let this decorator trio call `EncryptAsync`/`DecryptAsync`. Also discovered the already-shipped P-346 implementation already calls the sync `Compress`/`Encrypt`/`Decrypt`/`Decompress` members today despite this file's own historical `PT-02` design-task prose describing `CompressAsync`/`EncryptAsync` — that prose was inaccurate even when P-346 shipped; not corrected retroactively in the PT-02 row itself (historical record left intact per this file's own "do not reformat existing tasks" convention), but corrected everywhere forward-looking. Designed the AAD scheme: message CLR type name (`typeof(T).FullName ?? typeof(T).Name`) as the AAD source, carried from publish to consume via a new plaintext transport header (`PayloadTransformHeaders.MessageTypeAad`) set through `SendContext<T>.Headers.Set(...)` and read through `IMessageDeserializer.Deserialize`'s existing `Headers` parameter — confirmed reachable by reflection, and the same channel `SK.07.HeaderPropagation`/`SK.07.AmbientPropagation` already use — since `IMessageDeserializer.Deserialize` has no generic `T` and cannot otherwise reproduce the producer's AAD string. Designed the rolling-deploy-safe fallback (header absent → `Array.Empty<byte>()` AAD, matching pre-migration implicit behavior, so an old producer's message stays decryptable by a new consumer) and explicitly recorded the one direction it cannot cover — a new producer's non-empty-AAD ciphertext is not decryptable by an old, pre-migration consumer, a genuine AEAD auth failure — as an operational rollout requirement: consumers must upgrade before producers. Designed a `Build()`-time best-effort `ISynchronousEncryptionKeyProvider` check (type/instance-registration shapes only; factory-registration defers to `SK.01.P492`'s own runtime `NotSupportedException` backstop, since `Build()` must never call `Services.BuildServiceProvider()`). New "Opt-in payload compression and encryption" subsection (`#### AAD and synchronous-provider migration`), two new "MassTransit 9.x API notes" bullets (the sync-only serializer contract; the `SendContext<T>.Headers`/`Deserialize`'s `Headers` parameter channel), five new Hard Violations entries, one new Test Rules bullet, and a Queued Capabilities table row all added; package summary line flagged design-locked/implementation-pending (messaging-arch-planner, WO-081, P-499)
- [2026-09-08] SK.07.PayloadTransformAad implemented (`PA-07`→`PA-18`, P-499/WO-081) — verified upstream `01.Core` shipped on disk first (`ISymmetricEncryptionService`'s eight members all now require `byte[] associatedData`; `ISynchronousEncryptionKeyProvider`/`EncryptionKeyProviderCapabilities.IsGenuinelySynchronous` present and matching the design-locked shape exactly) before writing any code, per this wave's explicit "verify, don't trust the brief" instruction. Implemented exactly per the already-locked design (`PA-01`→`PA-06`, unchanged): new internal `PayloadTransformHeaders` (`Serialization/PayloadTransformHeaders.cs`, one constant `MessageTypeAad = "x-payload-transform-message-type"`); `PayloadTransformMessageSerializer.GetMessageBody<T>` now derives AAD from `typeof(T).FullName ?? typeof(T).Name`, writes it to `context.Headers.Set(...)`, and calls `_encryptionService!.Encrypt(bytes, aadBytes)`; `PayloadTransformMessageDeserializer.ReverseTransform` now takes a `Headers headers` parameter, reads `headers.Get<string>(PayloadTransformHeaders.MessageTypeAad, null)`, falls back to `[]` when absent, and calls `_encryptionService!.Decrypt(encrypted, aadBytes)`; `MessagingBusBuilder.Build()`'s existing `WithPayloadTransform()` guard block extended with a best-effort static check inspecting the registered `IEncryptionKeyProvider` `ServiceDescriptor` (`ImplementationInstance`/`ImplementationType` checked via `ISynchronousEncryptionKeyProvider`/`IsAssignableFrom`; `ImplementationFactory` skipped — not statically inspectable without invoking it, which `Build()` must never do), throwing `InvalidOperationException` naming `ISynchronousEncryptionKeyProvider` explicitly when statically provable `false`. Four new `MassTransit 9.x API notes` discovered while writing tests (not assumed, each confirmed by a failing `dotnet test` run before being fixed): `MassTransit.Serialization.DictionarySendHeaders` (in the main `MassTransit.dll`, not `.Abstractions`) implements BOTH `SendHeaders` and `Headers` on one instance, letting one object simulate the header traveling from a publish-side `SendContext<T>.Headers` mock to a consume-side `IMessageDeserializer.Deserialize(..., Headers, ...)` call without a full `TestHarness`; a `SendContext<T>` substitute closed over an `internal` message type hits the exact P-342 NSubstitute/strong-naming proxy failure (`PayloadTransformAadTestMessage` had to be made `public`); `SerializerContext.TryGetMessage<T>` silently returns `false` (no exception) unless `SendContext<T>.SupportedMessageTypes` is stubbed with `MassTransit.MessageUrn.ForTypeString<T>()`, not a raw `typeof(T).FullName`; and — the most consequential one — `ConsumeContext.Headers` reflects the JSON envelope's OWN embedded header snapshot taken when the inner STJ serializer builds the envelope, so a header set by an OUTER decorator AFTER calling the inner `IMessageSerializer.GetMessageBody<T>` (exactly what `PayloadTransformMessageSerializer` does) reaches the raw transport `Headers` parameter `IMessageDeserializer.Deserialize` receives — and is what actually authenticates the AES-GCM decrypt — but does NOT reach ordinary `ConsumeContext.Headers.Get<string>(...)` inside a plain `IConsumer<T>`. This was discovered because an earlier draft of the PA-14 harness test asserted the AAD header via a capturing consumer reading `ConsumeContext.Headers` and it consistently captured `null` despite the message decrypting successfully — the final `PayloadTransformAadHarnessTests.cs` instead proves PA-14 via the only channel that genuinely reflects the design's own internal contract: a full `TestHarness` round trip that only succeeds if AES-GCM authentication passed, which is cryptographically impossible unless the header crossed the real transport intact. Tests: `SerializationTests/PayloadTransformAadTests.cs` (3 new — matching-AAD round trip, tampered/swapped-AAD-header failure via `PayloadTransformMismatchException`, header-absent `Array.Empty<byte>()` fallback), `HarnessTests/PayloadTransformAadHarnessTests.cs` (1 new — the PA-14 structural proof via successful end-to-end decryption through a real `MessagingBusBuilder.ConfigurePayloadTransform`/`AddMassTransitTestHarness()` pipeline, plus a `Fault<T>`-absence assertion), and 6 new tests appended to `BuilderTests/PayloadTransformConfigurationTests.cs` (the four `IEncryptionKeyProvider` registration-shape guard tests plus the `ImplementationFactory`-skip case and its separate `NotSupportedException` runtime-backstop proof against `AesGcmEncryptionService`+`FakeRemoteEncryptionKeyProvider`). Existing `PayloadTransformMismatchTests.cs` updated for the new required-AAD `Encrypt` signature (`encryptionService.Encrypt(compressed, [])`) — the other two mismatch scenarios and all of `EncryptedPayloadWireCodecTests.cs` needed no changes (neither calls `Encrypt`/`Decrypt` directly). `16.Testing`'s `FakeEncryptionKeyProvider` (already `ISynchronousEncryptionKeyProvider`-marked, P-502) and `FakeRemoteEncryptionKeyProvider` (deliberately unmarked, P-502) supplied both halves of the guard-test matrix with zero new fakes needed in this domain. Confirmed unblocked by a concurrent, unrelated `06.Persistence` sibling-domain build breakage this session (its own `EncryptionModelConvention.cs`/EF Core API-surface fix, nothing to do with this phase) that transiently broke every test project in the repo via the `16.Testing` → `06.Persistence.EfCore` `ProjectReference` — waited for it to resolve rather than touching any file outside `07.Messaging`. Final run: `SharedKernel.Messaging.MassTransit.Tests` 187/187 passing (`Category!=Integration`; was 177 — 10 new tests, zero regressions). `#### AAD and synchronous-provider migration` section, the "Shipped implementation shape" bullet list, and the Test Rules payload-transform-AAD bullet all updated from design-locked/pending to shipped-and-described; four new MassTransit 9.x API notes added; no Hard Violations wording changes needed (the five P-499 entries added at design time already matched the shipped behavior exactly) (messaging-phase-implementer)
- [2026-09-09] CI reliability fix (no phase key, not a state-map item; unrelated to any P-NNN — this domain's WO-081 phase was P-499): fixed a flaky `SharedKernel.Messaging.MassTransit.Tests` `[Trait("Category","Integration")]` failure on `ubuntu-latest` — `OrderedDeliveryIntegrationTests.PublishAsync_WithPartitionKey_PreservesPerKeyPublishOrder_AcrossRealBroker` burned its full fixed 20-second `WaitAsync` ceiling and never reached its ordering assertions. Root cause was a starved wait budget, not an ordering defect: the integration lane runs 18 Testcontainers-backed projects with VSTest capped to 2 concurrent hosts on a 2-core runner (`eng/testsettings/integration.runsettings`), so the RabbitMQ container competes for CPU with the test host and everything else in the lane — a ceiling tuned on a full-size dev machine has zero margin there. Every `WaitAsync` call in this folder already resolves the instant its expected message(s) arrive (a `TaskCompletionSource`-backed bounded wait, not a blind sleep), so the fix sizes the ceiling generously rather than changing what is asserted — the per-key ordering assertions are untouched and just as strict. Added `IntegrationTests/IntegrationTestTimeouts.cs` (new internal static helper): `IsCi` detects a CI provider via the conventional `CI` environment variable; `Fixed(int localSeconds)` and `ScaledByMessageCount(int messageCount, int baseSeconds, double secondsPerMessage)` both multiply the local budget by a flat `CiMultiplier` (3x, a deliberately generous round number, not reverse-fit to the one observed failure) when `IsCi` is true; `BusConnectDelay` similarly CI-multiplies the pre-publish "let the bus connect and bind the queue" delay. Checked the two other `[Trait("Category","Integration")]` tests in this folder per this fix's own "don't leave a trap for the next slow runner" instruction and found both shared the identical fixed-ceiling pattern despite having passed on this run: `RabbitMqIntegrationTests`'s single-message `WaitAsync(TimeSpan.FromSeconds(10))` now uses `IntegrationTestTimeouts.Fixed(10)`; `DeadLetterIntegrationTests`'s fault-consumer `WaitAsync(TimeSpan.FromSeconds(15))` now uses `IntegrationTestTimeouts.Fixed(15)`; `OrderedDeliveryIntegrationTests`'s own wait now uses `ScaledByMessageCount(messagesPerKey * 2, baseSeconds: 10, secondsPerMessage: 1.0)` so a future change to `messagesPerKey` does not silently inherit a ceiling sized for fewer messages. All three tests' pre-publish `Task.Delay(500)` calls were likewise switched to `IntegrationTestTimeouts.BusConnectDelay` for the same contention reasoning — a starved connect/bind delay is the same class of hazard even though it wasn't the observed failure this time. Verified locally: all 3 pass in ~23s at local (non-CI) budgets, and again in ~25s with `CI=true` forcing every 3x-multiplied ceiling into scope, confirming the multiplier path is exercised and costs nothing extra on the happy path since every wait still resolves as soon as messages arrive. No production code touched — test-only change, confined to `07.Messaging/SharedKernel.Messaging.MassTransit/SharedKernel.Messaging.MassTransit.Tests/IntegrationTests/`. No state-map phase or root Phase Backlog entry created, per this fix's own explicit instruction that it is a reliability fix, not a phase (messaging-phase-implementer)
- [2026-09-15] Contracts redesign: `IEventPublisher.PublishAsync<TEvent>` now constrains on `class, IIntegrationEvent` (Abstractions references `SharedKernel.Contracts`), `MassTransitEventPublisher` drops its IDomainEvent runtime check and `MakeGenericMethod` cache, `PublishContext` gains `Subject`/`WithSubject`, the envelope is a CloudEvents 1.0 document whose `Type`/`DataVersion` and `messaging.event_type` tag come from the `[IntegrationEvent]` attribute, and construction outside `EventEnvelope.Wrap` no longer compiles (coordinator)
