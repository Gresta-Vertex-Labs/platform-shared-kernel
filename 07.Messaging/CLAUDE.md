# 07.Messaging — Message Bus and Event Publishing

## What This Domain Is

The messaging capability domain. Provides transport-agnostic abstractions for command/event publishing and an opinionated MassTransit wiring layer for RabbitMQ and Azure Service Bus. The domain owns the transactional outbox pattern via MassTransit's EF Core outbox integration — no outbox types exist in `06.Persistence`.

Philosophy: **Abstraction-first. Transport-swappable. Outbox-native. CloudEvents-compliant.**

> **Outbox ownership:** The outbox pattern is owned entirely by `07.Messaging` via `MassTransit.EntityFrameworkCoreIntegration`. `OutboxMessage`, `IOutboxWriter`, and any outbox interceptor types must never be defined in `06.Persistence`. The consuming service's `DbContext` is passed as a generic type parameter to `WithEntityFrameworkOutbox<TDbContext>()` — no compile-time dependency on `SharedKernel.Persistence.EfCore` is introduced by this package. The consuming service bridges the gap at its own composition root.

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Messaging.Abstractions` | `IMessageBus`, `IEventPublisher`, `PublishContext`, `IMessagingBuilder`, `MessagingOptions` — pure interface library; no transport NuGet dependencies | `Microsoft.Extensions.DependencyInjection.Abstractions` only |
| `SharedKernel.Messaging.MassTransit` | Concrete MassTransit bus wiring: `MassTransitMessageBus`, `MassTransitEventPublisher` (envelope construction now exclusively via `EventEnvelope.Wrap<TEvent>()` with tenant identity flowing through `PublishContext.TenantId`, shipped P-340/WO-054), `ConsumerBase<TMessage>`, `MessagingBusBuilder`, transport adapters (RabbitMQ, ASB), retry policy, EF Core outbox integration — gaining a bus-backed readiness probe, symmetric header propagation across all three dispatch verbs, per-consumer concurrency controls, dead-letter policy, ordered delivery, an ambient correlation/tenant propagator pair, opt-in payload compression/encryption, and full diagnostics coverage (queued P-341–P-349, WO-054) | `SharedKernel.Messaging.Abstractions`, `SharedKernel.Contracts` (for `EventEnvelope<TEvent>` in publisher implementation), `MassTransit` 9.1.2, `MassTransit.RabbitMQ` 9.1.2, `MassTransit.Azure.ServiceBus.Core` 9.1.2, `MassTransit.EntityFrameworkCore` 9.1.2 (note: NOT `MassTransit.EntityFrameworkCoreIntegration`), `Microsoft.EntityFrameworkCore` 10.x (outbox `TDbContext` constraint only), `Microsoft.Extensions.Logging.Abstractions` 10.x — gaining `SharedKernel.Compression`/`SharedKernel.Cryptography` (`01.Core`) for opt-in payload transform (queued P-346, WO-054) |

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

---

## Interface Contracts

### `SharedKernel.Messaging.Abstractions` — public surface

> Zero transport NuGet dependencies. References only `Microsoft.Extensions.DependencyInjection.Abstractions`.

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
        Publishes a CloudEvents-compliant integration event.
        The MassTransit implementation wraps TEvent in EventEnvelope<TEvent> (04.Contracts)
        before sending to the transport, constructed EXCLUSIVELY via EventEnvelope.Wrap<TEvent>()
        (04.Contracts's own mandated factory) — never a raw object-initializer/constructor call
        (P-340/WO-054, fixing a confirmed prior violation of 04.Contracts's own construction rule).
        CorrelationId and CausationId are propagated from Activity.Current?.TraceId when available.
        SourceService is sourced from MessagingOptions.ServiceName.
        TenantId is sourced from PublishContext.TenantId when explicitly set (or from the ambient
        TenantHeaderPropagator when MessagingBusBuilder.WithTenantContext<T>() is registered,
        P-345/WO-054); otherwise omitted (null), exactly like an unset CorrelationId/CausationId.

    .PublishAsync<TEvent>(TEvent integrationEvent, Action<PublishContext> configure, CancellationToken ct) → Task
        Overload for explicit envelope metadata override (CorrelationId, CausationId, custom headers).

    NOTE: IEventPublisher is for integration events only — events that cross service boundaries.
          In-process domain events are dispatched by IDomainEventDispatcher (03.Domain), not IEventPublisher.
          The correct flow: domain event → IDomainEventDispatcher → application event handler → IEventPublisher.
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
    .Headers                                                → IReadOnlyDictionary<string, string>
    .WithCorrelationId(Guid correlationId)                  → PublishContext  (fluent, returns this)
    .WithCausationId(Guid causationId)                      → PublishContext  (fluent, returns this)
    .WithTenantId(Guid tenantId)                            → PublishContext  (fluent, returns this — P-340/WO-054)
    .WithPartitionKey(string partitionKey)                  → PublishContext  (fluent, returns this — P-344/WO-054)
    .WithHeader(string key, string value)                   → PublishContext  (fluent, returns this)
    NOTE: Passed as an Action<PublishContext> callback to PublishAsync overloads.
          Callers configure the instance; the implementation owns the lifetime.
          Header keys must be non-null, non-empty strings. Duplicate keys overwrite silently.
          TenantId flows into EventEnvelope<TEvent>.TenantId (04.Contracts, P-331) via the
          IEventPublisher path only — IMessageBus has no envelope to carry it, so TenantId on a
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
        Invoked automatically at publish time for every IMessageBus.PublishAsync and
        IEventPublisher.PublishAsync call when one or more propagators are registered.
    PRECEDENCE RULE: Propagators run before the explicit Action<PublishContext> configure
        callback. When a caller supplies an explicit configure callback AND a propagator
        sets the same key, the explicit callback wins. This means per-call explicit overrides
        always take precedence over propagated ambient values.
    NOTE: Propagators are registered as scoped services. Multiple propagators are applied
          in registration order. Implement IMessageHeaderPropagator in the consuming service's
          composition root (referencing SharedKernel.Messaging.Abstractions); do not implement
          propagators inside SharedKernel — they require service-specific ambient context.
          Zero transport NuGet dependencies in the interface definition.
          EXCEPTION (P-345/WO-054): AmbientCorrelationHeaderPropagator and TenantHeaderPropagator
          (both in SharedKernel.Messaging.MassTransit) are the two narrow, by-name exceptions to
          "do not implement propagators inside SharedKernel" — see their own contract entries below.
    APPLIES TO ALL THREE DISPATCH VERBS (P-341/WO-054): registered propagators run identically
          before IMessageBus.PublishAsync, IMessageBus.SendAsync, IMessageBus.RequestAsync, and
          IEventPublisher.PublishAsync — not publish alone. The explicit-callback-wins precedence
          rule holds identically across all four call shapes.
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

DeadLetterOptions  (sealed class, DI options section "SharedKernel:Messaging:DeadLetter") — P-343/WO-054
    .QueueNameSuffix        → string  (default "_error" — matches MassTransit's own RabbitMQ default,
                                        so the platform default changes nothing until overridden)
    .MessageTimeToLive      → TimeSpan?  (default null — no expiry, unbounded retention)
    NOTE: RabbitMQ-only. Consumed by MessagingBusBuilder.WithDeadLetterPolicy(). Azure Service Bus
          dead-lettering is transport-native and unaffected by this options class — see "Dead-letter
          and poison-message policy" below. Calling WithDeadLetterPolicy() under an ASB transport
          logs an advisory Warning at startup rather than throwing (mirrors the WithVersionTranslator
          advisory-Warning pattern).

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
                messaging.publish.count    (Counter<long>)  — tagged messaging.event_type/message_type
                messaging.consume.count    (Counter<long>)  — tagged messaging.message_type
                messaging.consume.duration (Histogram<double>, ms) — tagged messaging.message_type
                messaging.retry.count      (Counter<long>)  — tagged messaging.message_type
                messaging.fault.count      (Counter<long>)  — tagged messaging.message_type
              Consumed by 13.ServiceDefaults.WithMessagingTelemetry() via .AddMeter(...), mirroring
              the ActivitySource registration split above.
```

#### Consumer and publisher instrumentation (`Consumers/`, `EventPublisher/`) — P-172

```text
ConsumerBase<TMessage>.Consume()  (sealed entry point — updated)
    Starts a child Activity via MessagingDiagnostics.ActivitySource.StartActivity("Consumer.Consume")
    before delegating to ConsumeAsync. Tags:
        messaging.message_type = typeof(TMessage).Name
    The activity is disposed after ConsumeAsync completes (success or exception) — standard
    `using` disposal scope wrapping the existing CorrelationId-propagation and log-then-rethrow logic.
    Log scope (ILogger.BeginScope) is enriched (additive to existing CorrelationId and x-sk-* header
    scope values from P-135) with:
        messaging.destination    = ConsumeContext.DestinationAddress?.AbsolutePath  (omitted if null)
        messaging.message_type   = typeof(TMessage).Name

MassTransitEventPublisher.PublishAsync<TEvent>()  (updated)
    Starts a child Activity via MessagingDiagnostics.ActivitySource.StartActivity("EventPublisher.Publish")
    before delegating to IPublishEndpoint. Tags:
        messaging.event_type = typeof(TEvent).Name
    The activity is disposed after the publish call completes (success or exception).
    Increments MessagingDiagnostics.Meter's messaging.publish.count instrument (P-348/WO-054).
    NOTE: This activity is independent of the EventEnvelope<TEvent> CloudEvents CorrelationId field —
          the Activity's own TraceId/SpanId comes from .NET's ambient Activity.Current chain; the
          envelope's CorrelationId is still sourced per the CloudEvents compliance rule below.

MassTransitMessageBus.SendAsync<T>() / .RequestAsync<TRequest,TResponse>() / .ExecuteRoutingSlipAsync()  — P-348/WO-054
    Each starts and disposes its own child Activity, matching the Consume/Publish shape exactly:
        SendAsync              → "MessageBus.Send",              tag messaging.message_type
        RequestAsync            → "MessageBus.Request",           tags messaging.request_type, messaging.response_type
        ExecuteRoutingSlipAsync → "MessageBus.ExecuteRoutingSlip", tag messaging.routing_slip.activity_count
    Prior to P-348, none of these three verbs produced any activity at all — a completeness gap,
    not a design gap, now closed to full dispatch-surface coverage. Increments
    MessagingDiagnostics.Meter's messaging.consume.count/messaging.consume.duration (consumer side)
    and messaging.fault.count (FaultConsumerAdapter) instruments per the Meter contract above.
```

#### Bus readiness probe implementation (`MessageBus/`) — P-347/WO-054

```text
MassTransitMessageBusProbe  (internal sealed class, implements IMessageBusProbe)
    .ProbeAsync(CancellationToken ct)  → Task<MessageBusHealth>
        Wraps the real, already-registered IBusControl/IBus instance this builder configures
        for the consuming service. Queries MassTransit's own bus-health surface — never opens
        a second, independently constructed transport connection built from separately supplied
        configuration.
    NOTE: Registered as a singleton by MessagingBusBuilder.Build() unconditionally, matching
          MassTransit's own singleton IBus/IBusControl lifetime. Transport-agnostic from the
          caller's perspective — behaves identically whether the consuming service configured
          RabbitMQ or Azure Service Bus.
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

    .WithDeadLetterPolicy(Action<DeadLetterOptions>? configure = null)  — P-343/WO-054
        — RabbitMQ only. Applies QueueNameSuffix to the receive endpoint's error-queue naming and
          MessageTimeToLive as a dead-letter-queue TTL argument.
        — null uses default DeadLetterOptions (QueueNameSuffix="_error", MessageTimeToLive=null).
        — When called while UseAzureServiceBus() is the configured transport, logs an advisory
          Warning at startup (same IHostedService-deferred-advisory pattern as WithVersionTranslator)
          rather than throwing — ASB dead-lettering is transport-native and unaffected by this option.
        — Optional. Omitting preserves MassTransit's own default RabbitMQ error-queue behavior.
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
| Header-Propagator Symmetry Across Publish, Send, and Request | P-341 | `SK.07.PropagationSymmetry` | MassTransit (`MassTransitMessageBus.SendAsync<T>`/`RequestAsync<TRequest,TResponse>` propagator invocation) |
| Working Per-Consumer Concurrency Controls (RabbitMQ + ASB) | P-342 | `SK.07.ConsumerConcurrency` | MassTransit (`AzureServiceBusOptions.MaxConcurrentCalls` wiring fix, `RabbitMqBusOptions.ConcurrentMessageLimit`, `ConsumerDefinitionBase<TConsumer>.ConcurrentMessageLimit`) |
| Dead-Letter and Poison-Message Delivery Policy | P-343 | `SK.07.DeadLetter` | MassTransit (`DeadLetterOptions`, `MessagingBusBuilder.WithDeadLetterPolicy()`) |
| Ordered Delivery via Partition Key / Session Affinity | P-344 | `SK.07.OrderedDelivery` | Abstractions (`PublishContext.PartitionKey`/`WithPartitionKey`) + MassTransit (RabbitMQ routing-key / ASB session-identity mapping) |
| Built-In Ambient Header Propagator and Tenant-Context Seam | P-345 | `SK.07.AmbientPropagation` | Abstractions (`ITenantContextAccessor`) + MassTransit (`AmbientCorrelationHeaderPropagator`, `TenantHeaderPropagator`, `WithAmbientCorrelationPropagation()`, `WithTenantContext<T>()`) |
| Opt-In Payload Compression and Encryption Before Transport | P-346 | `SK.07.PayloadTransform` | MassTransit (`PayloadTransformOptions`, compressing/encrypting serializer decorator, `WithPayloadTransform()`) |
| Bus-Backed Readiness Probe Primitive | P-347 | `SK.07.ReadinessProbe` | Abstractions (`IMessageBusProbe`, `MessageBusHealth`) + MassTransit (`MassTransitMessageBusProbe`) |
| Complete Distributed-Tracing and Metrics Coverage Across All Dispatch Verbs | P-348 | `SK.07.DiagnosticsCoverage` | MassTransit (`MessagingDiagnostics.Meter` + Send/Request/ExecuteRoutingSlip activity instrumentation) |
| NuGet Packaging Completeness and Reference-Implementation Quick-Start Recipes | P-349 | `SK.07.PackagingRecipes` | Abstractions + MassTransit (`PackageReadmeFile` wiring, `IIdempotencyStore`/tenant-context recipes) |

---

### Hard violations (never do these)

- Injecting `IBus`, `IPublishEndpoint`, or `ISendEndpointProvider` from MassTransit directly in application handlers, command/query handlers, domain services, or any type outside `07.Messaging` infrastructure — use `IMessageBus` or `IEventPublisher` exclusively.
- Publishing domain events via `IEventPublisher` or `IMessageBus` — domain events (`IDomainEvent`) are dispatched internally by `IDomainEventDispatcher` (from `03.Domain`); only integration events cross service boundaries via `IEventPublisher`. The boundary is: domain event fires domain handlers, a domain handler maps to an integration event, the integration event is published via `IEventPublisher`.
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
- Any static mutable state. **Exception (P-172):** a static `readonly ActivitySource` (and, if ever needed, a static `readonly Meter`) instance is the platform-standard .NET diagnostics pattern — it carries no mutable business state and the BCL diagnostics API is explicitly designed around process-lifetime static instrument instances (the same shape as a static logger category). `MessagingDiagnostics.ActivitySource` is the only sanctioned static field in this domain; do not add additional ad-hoc static fields under cover of this exception.
- Creating an `ActivitySource` or custom `Meter` in `13.ServiceDefaults` on behalf of `07.Messaging` — the source is owned and constructed here (`MessagingDiagnostics.ActivitySource`, P-172); `13.ServiceDefaults` only registers the already-existing source name with the host's `TracerProvider`/`MeterProvider` via `WithMessagingTelemetry()` (P-132). This was a latent cross-domain phase violation discovered during P-132 review — P-132 incorrectly assumed this source already existed.
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
- Constructing `EventEnvelope<TEvent>` via a raw object initializer or constructor call anywhere in `SharedKernel.Messaging.MassTransit` — always `SharedKernel.Contracts.Events.EventEnvelope.Wrap<TEvent>()`, `04.Contracts`'s own mandated factory (added in P-340; this was a confirmed, previously-shipped violation in `MassTransitEventPublisher.PublishEnvelope<TEvent>` that also caused `TenantId` to always be `null`).
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

### MassTransit 9.x API notes (discovered during Core implementation)

- **`PublishContext` name conflict:** `MassTransit.PublishContext` clashes with `SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext` inside the MassTransit package. Always add `using MessagingPublishContext = SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext;` at the top of any file in `SharedKernel.Messaging.MassTransit` that references both types.
- **`MassTransitEventPublisher` constraint bridge:** `IEventPublisher.PublishAsync<TEvent>` is `where TEvent : class`; `EventEnvelope<TEvent>` requires `where TEvent : IDomainEvent`. Bridge via a static `ConcurrentDictionary<Type, Delegate>` cache keyed by event type — `MethodInfo.MakeGenericMethod` once per type to produce a closed generic satisfying IDomainEvent. The runtime guard `typeof(IDomainEvent).IsAssignableFrom(typeof(TEvent))` fires first; any type that passes is safe to cast.
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

### CloudEvents compliance rule

`MassTransitEventPublisher.PublishAsync<TEvent>` wraps `TEvent` in `EventEnvelope<TEvent>` (from `04.Contracts`) before publishing to the transport. As of P-340/WO-054, the envelope is constructed **exclusively** via `SharedKernel.Contracts.Events.EventEnvelope.Wrap<TEvent>(domainEvent, sourceService, correlationId, causationId, tenantId)` — `04.Contracts`'s own mandated factory — never a raw object initializer. The envelope fields are populated as follows (corrected against the real shipped `EventEnvelope<TEvent>`/`EventEnvelope.Wrap<TEvent>` source, whose `CorrelationId`/`CausationId` are `string?`, not `Guid`, and whose version/timestamp fields are named `EventVersion`/`OccurredOn`, not `SchemaVersion`/`TimestampUtc`):

| Envelope field | Source |
| --- | --- |
| `CorrelationId` (`string?`) | `PublishContext.CorrelationId` (`Guid`) when explicitly set via `WithCorrelationId`, formatted `"D"`; else `Activity.Current?.TraceId.ToString()` if an ambient trace exists; else a new `Guid.NewGuid().ToString("D")`. |
| `CausationId` (`string?`) | `PublishContext.CausationId` when explicitly set via `WithCausationId`, formatted `"D"`; otherwise omitted (`null`) — no ambient fallback. |
| `TenantId` (`Guid?`) | `PublishContext.TenantId` when explicitly set via `WithTenantId` (or populated ambiently by `TenantHeaderPropagator` when `MessagingBusBuilder.WithTenantContext<T>()` is registered, P-345); otherwise omitted (`null`). Added in P-340/WO-054 — previously always `null` on every published message due to the raw-object-initializer defect this phase fixes. |
| `SourceService` | `MessagingOptions.ServiceName` resolved from `IOptions<MessagingOptions>`. |
| `EventVersion` | `DomainEventVersionHelper.GetVersion(typeof(TEvent))` (from `03.Domain`); defaults to `1` when `[DomainEventVersionAttribute]` is absent. |
| `OccurredOn` | Copied from the domain event's own `OccurredOn` property (when the business fact occurred, not when the envelope was built or published). |
| `EventId` | Copied from the domain event's own `Id` property — not a new envelope-level identity; used for transport-level deduplication. |

The MassTransit message envelope maps to the CloudEvents HTTP binding:

- `specversion` = `"1.0"`
- `type` = `typeof(TEvent).FullName` (or the MassTransit message URN)
- `source` = `MessagingOptions.ServiceName`
- `id` = `CorrelationId.ToString("D")` (lowercase hyphenated GUID)
- `datacontenttype` = `"application/json"`

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

### Dead-letter and poison-message policy (P-343/WO-054)

A message becomes "poison" and is routed to dead-letter only at one of two decision points:

1. **Immediate**: the endpoint-level `ConsumerDefinitionBase<TConsumer>.NonRetryableExceptions` filter classifies the thrown exception type as fatal — no retry attempts occur at all.
2. **After exhaustion**: the global `RetryOptions` attempt budget (configured via `WithRetry()`) is exhausted without a successful delivery.

An open `CircuitBreakerOptions` breaker (`WithCircuitBreaker()`) is a **distinct** failure path — it short-circuits delivery entirely while open, failing fast with `CircuitBreakerException` until `ResetInterval` elapses and the breaker moves to `HalfOpen`. A message rejected by an open breaker is not itself dead-lettered by the breaker; whether it is retried again depends on the retry/non-retryable classification above once the breaker allows delivery through again.

**RabbitMQ** — configurable via `DeadLetterOptions`/`MessagingBusBuilder.WithDeadLetterPolicy()`:

- `QueueNameSuffix` (default `"_error"`, matching MassTransit's own RabbitMQ default naming — the platform default changes nothing until explicitly overridden).
- `MessageTimeToLive` (default `null` — unbounded retention).
- Omitting `WithDeadLetterPolicy()` entirely preserves MassTransit's own default RabbitMQ error-queue behavior — this domain adds a configuration surface, it does not change the unconfigured default.

**Azure Service Bus** — dead-lettering is entirely transport-native and outside this domain's configuration surface: ASB moves a message to its built-in `$DeadLetterQueue` once `MaxDeliveryCount` (an ASB queue/subscription-level setting, configured at the Azure resource, not through `AzureServiceBusOptions`) is exceeded. `WithDeadLetterPolicy()` may still be called under an ASB transport for consistency across a multi-transport codebase, but it has no effect — `Build()` logs an advisory `Warning` rather than throwing, mirroring the `WithVersionTranslator` advisory pattern. There is currently no platform-level configuration surface for ASB's `MaxDeliveryCount`; it is set at the Azure resource/Bicep/ARM level.

### Ordered delivery via partition key (P-344/WO-054)

`PublishContext.PartitionKey` (`string?`) maps to each transport's genuine native ordered-delivery mechanism — never a platform-invented substitute:

- **RabbitMQ**: `PartitionKey` drives routing-key affinity so all messages sharing a key traverse the same queue-binding path in publish order.
- **Azure Service Bus**: `PartitionKey` is applied as the outgoing message's session identifier (`SessionId`). The receiving endpoint must have sessions enabled for the ordering guarantee to hold — enabling sessions on an endpoint is a consuming-service/infrastructure responsibility this builder does not silently apply retroactively.

**Ordering caveat**: ordering is guaranteed only among messages sharing the same `PartitionKey` **and** consumed by a single active consumer instance on that endpoint. Multiple concurrent consumer instances processing the same partitioned endpoint (horizontal scale-out) break the ordering guarantee even with a correctly-set key — this is inherent to both transports' native mechanisms, not a platform limitation. Omitting `PartitionKey` leaves publish/send behavior exactly as it was before this feature — no ordering guarantee beyond the transport's own default.

### Opt-in payload compression and encryption (P-346/WO-054)

`MessagingBusBuilder.WithPayloadTransform(Action<PayloadTransformOptions>?)` wires an opt-in serializer decorator around the platform's default STJ serializer, built entirely on `01.Core`'s existing primitives — `IPayloadCompressor` (`SharedKernel.Compression`) and `ISymmetricEncryptionService` (`SharedKernel.Cryptography`). Never a new bespoke compression/cryptography primitive in this domain.

- **Fixed ordering, not caller-configurable**: publish-side is always compress-then-encrypt; consume-side is always decrypt-then-decompress — matching `01.Core`'s platform-wide convention (compressing already-encrypted, high-entropy ciphertext wastes CPU for no size benefit).
- **Disabled by default**: `EnableCompression` and `EnableEncryption` both default to `false`. Enabling either changes the wire format — this is a deliberate, explicit, per-service opt-in, never ambient behavior a consuming service could be surprised by.
- **`Build()`-time dependency guard**: enabling a flag without its corresponding `01.Core` service already registered in DI throws `InvalidOperationException` at `Build()`, mirroring the `WithIdempotency()` missing-`IIdempotencyStore` guard.
- **Loud failure on mismatch**: a consumer without the matching transform enabled must fail clearly at deserialization (`PayloadTransformMismatchException` or equivalent) — never silently misinterpret compressed/encrypted bytes as plaintext JSON.

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
// Idempotency (P-134) — consuming service provides IIdempotencyStore implementation
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
    ctx.WithPartitionKey(orderPlacedEvent.OrderId.ToString()); // ordered delivery per order
}, ct);

// Per-consumer concurrency override (P-342) — in the consuming service
public sealed class OrderPlacedConsumerDefinition : ConsumerDefinitionBase<OrderPlacedConsumer>
{
    protected override int? ConcurrentMessageLimit => 4; // overrides the global RabbitMQ/ASB default
}

// Dead-letter policy (P-343) — RabbitMQ only
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .WithDeadLetterPolicy(o =>
    {
        o.QueueNameSuffix = "-poison";
        o.MessageTimeToLive = TimeSpan.FromDays(7);
    })
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
- `EventEnvelope<TEvent>` (`04.Contracts`) must have an entry in the consuming service's source-generated STJ context for NativeAOT builds.
- `MassTransit.EntityFrameworkCore` uses EF Core 10.x which is AOT-compatible with compiled models. Verify on each major upgrade.
- Transport packages (`MassTransit.RabbitMQ`, `MassTransit.Azure.ServiceBus.Core`) — verify AOT status on each major upgrade; the `MessagingBusBuilder` abstraction contains the blast radius to the composition layer.
- `DomainEventVersionHelper.GetVersion(Type)` (used by `MassTransitEventPublisher` to populate `SchemaVersion`) reads `[DomainEventVersion]` attribute metadata — attribute reading is preserved by the trimmer and is startup-time only, not a hot path.
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
- The compressing/encrypting serializer decorator (P-346) delegates to `01.Core`'s `IPayloadCompressor`/`ISymmetricEncryptionService` — both already AOT-preferred per `01.Core`'s own brain; this package adds no additional reflection on top of them. STJ serialization of the (pre-transform) message payload is subject to the same NativeAOT source-generated-`JsonSerializerContext` requirement already stated above for `EventEnvelope<TEvent>`.

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
- **CloudEvents envelope tests:** publish via `IEventPublisher`; intercept the outgoing `EventEnvelope<TEvent>` via `TestHarness`; assert `SourceService`, `CorrelationId`, `EventVersion`, and `TenantId` are populated correctly (`TenantId` set when `WithTenantId` is called, `null` otherwise — P-340/WO-054). A whole-envelope record-equality assertion against an independently-`EventEnvelope.Wrap<TEvent>()`-constructed instance is the strongest proof that construction stayed factory-only — MassTransit's in-memory `TestHarness` delivers the payload by reference (no JSON round-trip) when no consumer forces deserialization, so this comparison is safe; do not assume the same holds once a real broker transport is in play.
- **`MessagingBusBuilder` guard tests:** verify `IMessageBus` resolves after `.Build()`; verify `IEventPublisher` resolves; verify startup validation throws when `MessagingOptions.ServiceName` is null (throws `OptionsValidationException` from `MessagingOptionsValidator`, not `InvalidOperationException` — assert `.Throw<Exception>().Where(e => e.Message.Contains("ServiceName"))`); verify `InvalidOperationException` when `.Build()` called without a transport configured.
- **Consumer endpoint convention tests:** verify queue name follows `{service-name}-{consumer-type}` kebab-case via `new KebabCaseEndpointNameFormatter(prefix, false).Consumer<TConsumer>()` directly — `IConsumerTestHarness<T>` in MassTransit 9.x does not expose `.Consumer.InputAddress`.
- **`RequestAsync` timeout tests:** verify `RequestAsync<TRequest, TResponse>` throws (or cancels) when no responder is registered and the cancellation token expires.
- **Standard test package set:** `xunit` 2.9.3, `xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.13.0, `coverlet.collector` 6.0.4, `FluentAssertions` 8.4.0, `NSubstitute` 5.3.0, `MassTransit.TestFramework` 9.1.2 (NOT `MassTransit.Testing` — package was renamed in MassTransit 9.x).
- **GlobalUsings.cs required** — every test project must include `global using Xunit;`.
- **SQLite for outbox unit tests** — no Testcontainers needed; SQLite covers EF Core outbox row lifecycle. Use Testcontainers only for broker-level integration tests.
- **Never use the `file` modifier on consumer, message, or DbContext types in test files** — C# `file` types generate mangled CLR names containing `<` and hash characters (e.g., `<ConsumerBaseTests>F15BB...RecordingConsumer`). MassTransit type matching splits on `<`; the mangled names cause type resolution failures for `GetConsumerHarness<T>()`, `harness.Consumed.Select<T>()`, and outbox entity model building. Always use `internal` (with a unique name per file to avoid collisions).
- **`await using` for `ServiceProvider` in tests** — `MassTransit.UsageTracking.UsageTracker` (registered by MassTransit 9.x startup) only implements `IAsyncDisposable`, not `IDisposable`. Using `using var sp` causes a synchronous disposal path that throws. Always use `await using var sp = services.BuildServiceProvider(...)` and declare test methods as `async Task`.
- **SQLite keep-alive connection for in-memory database persistence** — when using a SQLite in-memory database across multiple `ServiceScope` instances in the same test, open a `SqliteConnection("Data Source=:memory:")` and keep it open for the test's lifetime. Pass that connection to `UseSqlite(connection)`. If the connection closes, the in-memory database is dropped and subsequent scopes see an empty schema.
- **`ActivitySource` / `Activity` assertion pattern (P-172):** subscribe an `ActivityListener` with `ShouldListenTo = source => source.Name == "SharedKernel.Messaging"` and `Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData` BEFORE invoking `Consume()` or `PublishAsync()` — without an attached listener, `ActivitySource.StartActivity` returns `null` (fast-path no-op) and no activity is created to assert against. Collect started activities into a `List<Activity>` via `listener.ActivityStarted = act => list.Add(act)`. Always call `ActivitySource.AddActivityListener(listener)` and dispose/remove it at test teardown to avoid cross-test listener leakage (listeners are process-global, not scoped to a `ServiceProvider`).
- **`ActivityListener` parallel-test-isolation hazard:** the listener registered via `ActivitySource.AddActivityListener` is process-wide, not scoped to the test method or class. Under xUnit's default parallel test-class execution, other test classes in the same run that also drive `ConsumerBase<T>.Consume()` or `MassTransitEventPublisher.PublishAsync<T>()` emit their own activities on the same `"SharedKernel.Messaging"` source while your listener is attached. Asserting `capturedActivities.Should().ContainSingle(a => a.OperationName == "...")` is flaky — it can capture activities from unrelated concurrently-running tests. Always filter by the test's own unique tag value (e.g. `a.GetTagItem("messaging.message_type") == nameof(MyTestMessage)`) in addition to `OperationName`, never by `OperationName` alone.
- **Propagator-symmetry tests (P-341):** any test asserting `IMessageHeaderPropagator` output must cover all three dispatch verbs it applies to (`PublishAsync`, `SendAsync`, `RequestAsync`) when the scenario is verb-agnostic — a propagator test that only exercises `PublishAsync` is exactly the gap that produced the P-341 defect in the first place.
- **`IMessageBusProbe` health tests (P-347):** assert `IsHealthy = true` against a started `TestHarness`/in-memory bus, and `IsHealthy = false` (with a non-null `Description`) against a bus that was never started or whose underlying transport connection was torn down (e.g. a Testcontainers RabbitMQ container stopped mid-test). Never assert health by independently pinging the broker outside the probe — the whole point of the probe is that it reflects the real, already-configured bus instance.
- **Payload transform round-trip and mismatch tests (P-346):** a round-trip test must exercise both `EnableCompression` and `EnableEncryption` together (the fixed compress-then-encrypt/decrypt-then-decompress order) as well as each flag independently. A mismatch test (publisher transform-enabled, consumer not, or vice versa) must assert a loud, typed failure (`PayloadTransformMismatchException` or equivalent) — never assert on a generic `Exception`, since a generic assertion would also pass for an unrelated failure mode and mask a regression to silent misinterpretation.
- **Ordered-delivery Testcontainers tests (P-344):** publish interleaved messages under at least two distinct `PartitionKey` values against a single active consumer instance; assert each key's own messages are observed in publish order — asserting order across the *combined* stream (ignoring key) is not the guarantee this feature makes and will produce a flaky/meaningless test.
- **Dead-letter Testcontainers tests (P-343):** configure a consumer whose `ConsumerDefinitionBase.NonRetryableExceptions` (or an exhausted `WithRetry()` budget) routes a thrown exception to dead-letter; assert the message is observable at the configured `DeadLetterOptions.QueueNameSuffix` destination — do not assert merely that the original queue is empty, which is also true for a successfully-processed message.
- **`MeterListener` assertion pattern (P-348):** subscribe a `MeterListener` with `InstrumentPublished = (instrument, listener) => { if (instrument.Meter.Name == "SharedKernel.Messaging") listener.EnableMeasurementEvents(instrument); }` BEFORE invoking the operation under test; `Counter<T>`/`Histogram<T>` measurements are only observed while a listener is actively enabled for that specific instrument. Like `ActivityListener` (below), `MeterListener` subscription is process-wide — apply the same per-test unique-tag-value filtering discipline to avoid cross-test-class flakiness under xUnit's default parallel execution.
- **`[LoggerMessage]` assertions use `EventId`, never message-text substring matching (P-254):** when a test needs to assert that a specific log statement fired, capture via a test `ILogger`/`ILoggerFactory` double (`16.Testing`) and assert on the structured `EventId.Id` (e.g. `7001` for `ConsumerBase`'s consume-error log) rather than parsing the rendered message string — the message template text is not a stable contract, the `EventId` is. **`MessagingLogScope`-seeded scope assertions:** any test asserting `BeginScope` contents on `ConsumerBase`, `BatchConsumerBase`, `FaultConsumerAdapter`, or `RoutingSlipActivityBase` must assert the `"CorrelationId"` key is present and formatted as `Guid.ToString("D")` (or empty string when unavailable) — this is the one shape `MessagingLogScope.Create` guarantees identically across all four types. Tests assert against the literal string `"CorrelationId"` (the key's runtime value), not against `MessagingLogScope.CorrelationIdKey` — the constant (P-263) is production-side authoring hygiene only; test code has no obligation to reference it.

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
