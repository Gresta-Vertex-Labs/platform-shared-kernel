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
| `SharedKernel.Messaging.MassTransit` | Concrete MassTransit bus wiring: `MassTransitMessageBus`, `MassTransitEventPublisher`, `ConsumerBase<TMessage>`, `MessagingBusBuilder`, transport adapters (RabbitMQ, ASB), retry policy, EF Core outbox integration | `SharedKernel.Messaging.Abstractions`, `SharedKernel.Contracts` (for `EventEnvelope<TEvent>` in publisher implementation), `MassTransit` 9.1.2, `MassTransit.RabbitMQ` 9.1.2, `MassTransit.Azure.ServiceBus.Core` 9.1.2, `MassTransit.EntityFrameworkCore` 9.1.2 (note: NOT `MassTransit.EntityFrameworkCoreIntegration`), `Microsoft.EntityFrameworkCore` 10.x (outbox `TDbContext` constraint only), `Microsoft.Extensions.Logging.Abstractions` 10.x |

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

    .RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct)     → Task<TResponse>
        Synchronous request/response pattern over the message bus.
        Uses a private temporary reply queue under the hood.
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
        before sending to the transport.
        CorrelationId and CausationId are propagated from Activity.Current?.TraceId when available.
        SourceService is sourced from MessagingOptions.ServiceName.

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
    .Headers                                                → IReadOnlyDictionary<string, string>
    .WithCorrelationId(Guid correlationId)                  → PublishContext  (fluent, returns this)
    .WithCausationId(Guid causationId)                      → PublishContext  (fluent, returns this)
    .WithHeader(string key, string value)                   → PublishContext  (fluent, returns this)
    NOTE: Passed as an Action<PublishContext> callback to PublishAsync overloads.
          Callers configure the instance; the implementation owns the lifetime.
          Header keys must be non-null, non-empty strings. Duplicate keys overwrite silently.
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
    NOTE: Never embed credentials in appsettings.json committed to source control.
          Source credentials from environment variables, Kubernetes Secrets, or Azure Key Vault.
          Prefetch tuning: lower for slow consumers, higher for fast CPU-bound consumers.

AzureServiceBusOptions  (sealed class, DI options section "SharedKernel:Messaging:AzureServiceBus")
    .ConnectionString       → string?  (local/dev only; mutually exclusive with FullyQualifiedNamespace)
    .FullyQualifiedNamespace → string?  (K8s managed identity path; e.g. "my-ns.servicebus.windows.net")
    .MaxConcurrentCalls     → int  (default 1 — concurrent message processing per consumer)
    .TransportType          → ServiceBusTransportType  (Amqp or AmqpWebSockets; default Amqp)
    NOTE: Exactly one of ConnectionString or FullyQualifiedNamespace must be set; startup validation
          throws InvalidOperationException if both or neither are set.
          Managed identity via DefaultAzureCredential is strongly preferred in Kubernetes workloads.

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
        Consumed by ConsumerBase<TMessage>.Consume() and MassTransitEventPublisher.PublishAsync<TEvent>()
        to start child Activities. Consumed by 13.ServiceDefaults.WithMessagingTelemetry() (P-132),
        which wires "SharedKernel.Messaging" into the host's TracerProvider via .AddSource(...) —
        13.ServiceDefaults never constructs this ActivitySource itself; it only registers the
        already-existing source name with the host's TracerProvider/MeterProvider.
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
    NOTE: This activity is independent of the EventEnvelope<TEvent> CloudEvents CorrelationId field —
          the Activity's own TraceId/SpanId comes from .NET's ambient Activity.Current chain; the
          envelope's CorrelationId is still sourced per the CloudEvents compliance rule below.
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
        — At every IMessageBus.PublishAsync and IEventPublisher.PublishAsync call, all registered
          propagators are invoked before the explicit Action<PublishContext> configure callback.
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

    .Build() → IServiceCollection
        — Registers IMessageBus → MassTransitMessageBus (scoped).
        — Registers IEventPublisher → MassTransitEventPublisher (scoped).
        — Registers MessagingOptions via IOptions<MessagingOptions>.
        — Registers MassTransit IBus, IPublishEndpoint, ISendEndpointProvider (MassTransit-managed scoped).
        — Registers IHostedService for MassTransit bus lifecycle (start/stop via IBusControl).
        — Registers MassTransitRoutingSlipBuilder as scoped IRoutingSlipBuilder (always, even when
          AddRoutingSlipActivity has not been called — the interface is usable independently).
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
- Injecting `MassTransit.IMessageScheduler` directly in application handlers — use `SharedKernel.Messaging.Abstractions.IMessageScheduler` (added in P-127).
- Registering `IFaultConsumer<T>` via `services.AddScoped` — fault consumers must be registered via `MessagingBusBuilder.AddFaultConsumer<TMessage,TConsumer>()` (added in P-126).
- Registering `BatchConsumerBase<T>` subclasses via `AddConsumer<T>()` — batch consumers must be registered via `AddBatchConsumer<T>()` to apply batch configuration (added in P-129).
- Configuring circuit breakers per-consumer via `IConsumerDefinition<T>` — circuit breaker policy is global and must be configured via `WithCircuitBreaker()` on `MessagingBusBuilder`; per-consumer circuit-breaker overrides defeat the purpose of a shared failure-count window (added in P-126).
- Using `Task.Delay` inside consumers as a substitute for deferred delivery — this blocks thread-pool threads, cannot survive process restarts, and loses the message on crash; use `IMessageScheduler.ScheduleAsync` instead (added in P-127).
- Hardcoding queue names in `WithSendEndpointRoute<T>()` without namespacing by target service — the queue name must follow the `{target-service-name}-{command-type}` kebab-case convention matching the target service's `MessagingOptions.ServiceName` prefix (added in P-131).
- Implementing custom deduplication logic inside `ConsumeAsync` bodies (e.g., checking a local `HashSet<Guid>` or querying the database on every message) — the platform-standard deduplication mechanism is `WithIdempotency()` with a registered `IIdempotencyStore` implementation; ad-hoc in-consumer deduplication is non-standard and creates inconsistency across services (added in P-134).
- Calling `WithIdempotency()` on `MessagingBusBuilder` without first registering a concrete `IIdempotencyStore` — `Build()` throws `InvalidOperationException` at startup; SharedKernel does not provide a store implementation; the consuming service bridges to its own persistence layer (added in P-134).
- Implementing `IMessageHeaderPropagator` inside `SharedKernel.*` packages — propagators require access to service-specific ambient context (e.g., `IHttpContextAccessor`, tenant resolution, feature flag state) that does not exist in the SharedKernel; propagators belong in the consuming service's composition root (added in P-135).
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

`MassTransitEventPublisher.PublishAsync<TEvent>` wraps `TEvent` in `EventEnvelope<TEvent>` (from `04.Contracts`) before publishing to the transport. The envelope fields are populated as follows:

| Envelope field | Source |
| --- | --- |
| `CorrelationId` | `Activity.Current?.TraceId` as `Guid` if available; otherwise `Guid.NewGuid()`. Overrideable via `PublishContext.WithCorrelationId`. |
| `CausationId` | `PublishContext.CausationId` when explicitly set; otherwise omitted (`Guid.Empty`). |
| `SourceService` | `MessagingOptions.ServiceName` resolved from `IOptions<MessagingOptions>`. |
| `SchemaVersion` | `DomainEventVersionHelper.GetVersion(typeof(TEvent))` (from `03.Domain`); defaults to `1` when `[DomainEventVersion]` attribute is absent. |
| `TimestampUtc` | `DateTimeOffset.UtcNow` at publish time. |

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

// Header propagation (P-135) — propagators registered in consuming service
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .WithHeaderPropagator<TenantHeaderPropagator>()      // scoped; reads ITenantProvider
    .WithHeaderPropagator<CorrelationHeaderPropagator>() // scoped; reads Activity.Current
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

// TenantHeaderPropagator example (in the consuming service — not in SharedKernel)
public sealed class TenantHeaderPropagator : IMessageHeaderPropagator
{
    public TenantHeaderPropagator(ITenantProvider tenantProvider) { ... }
    public void Propagate(PublishContext context)
        => context.WithHeader("x-sk-tenant-id", _tenantProvider.TenantId.ToString());
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

---

## Test Rules

- Unit tests for each package live in the nested `.Tests/` folder inside that package's folder.
- **`IMessageBus` / `IEventPublisher` mock tests:** mock both interfaces with NSubstitute; verify application handlers call `PublishAsync` / `SendAsync` with the expected event type and arguments; do not test MassTransit internals.
- **`ConsumerBase<TMessage>` tests:** instantiate a concrete subclass via MassTransit `TestHarness`; publish a message; assert `ConsumeAsync` was called with the correct message; assert exceptions propagate without swallowing (NSubstitute throw-configured dependency).
- **Integration tests:** use MassTransit `TestHarness` (`MassTransit.Testing`) — in-memory bus, no broker required; `await harness.InactivityTask` to wait for consumer completion; verify `harness.Consumed.Select<TMessage>()` contains the expected messages.
- **Outbox integration tests:** wire `WithEntityFrameworkOutbox<TDbContext>` to SQLite; publish via `IEventPublisher`; call `SaveChangesAsync` — outbox rows are written atomically DURING `SaveChangesAsync` via `OutboxSaveChangesObserver` (not before); assert row count > 0 after `SaveChangesAsync`. **SQLite limitation:** the outbox delivery worker uses `RepeatableRead` isolation requiring nested transactions which SQLite does not support — do NOT start the delivery worker (TestHarness) in the row-insertion test; register the bus with `AddMassTransit` + `UsingInMemory` without `AddMassTransitTestHarness` so no delivery worker starts.
- **RabbitMQ integration tests:** use Testcontainers RabbitMQ from `16.Testing/SharedKernel.Testing`; configure `UseRabbitMq` with container connection string; publish and consume; assert end-to-end delivery; mark with `[Trait("Category","Integration")]` so CI can skip when Docker unavailable.
- **No tests against live Azure Service Bus** — use `MassTransit.TestFramework.TestHarness` for ASB consumer logic; use a Service Bus emulator or skip in CI.
- **Retry policy tests:** configure `UseMessageRetry(r => r.Immediate(3))`; consumer throws on first N-1 calls, succeeds on Nth; assert `harness.Consumed.Any<TMessage>()` is true (message eventually consumed) AND `harness.Published.Any<Fault<TMessage>>()` is false (no dead-letter). Note: `TestHarness.Consumed.Select<T>()` does not expose per-retry-attempt entries — cannot assert exact retry count via the harness.
- **CloudEvents envelope tests:** publish via `IEventPublisher`; intercept the outgoing `EventEnvelope<TEvent>` via `TestHarness`; assert `SourceService`, `CorrelationId`, and `SchemaVersion` are populated correctly.
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
