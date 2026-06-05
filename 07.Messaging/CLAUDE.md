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
| `SharedKernel.Messaging.MassTransit` | Concrete MassTransit bus wiring: `MassTransitMessageBus`, `MassTransitEventPublisher`, `ConsumerBase<TMessage>`, `MessagingBusBuilder`, transport adapters (RabbitMQ, ASB), retry policy, EF Core outbox integration | `SharedKernel.Messaging.Abstractions`, `SharedKernel.Contracts` (for `EventEnvelope<TEvent>` in publisher implementation), `MassTransit` 8.x, `MassTransit.RabbitMQ` 8.x, `MassTransit.Azure.ServiceBus.Core` 8.x, `MassTransit.EntityFrameworkCoreIntegration` 8.x, `Microsoft.EntityFrameworkCore` 10.x (outbox `TDbContext` constraint only), `Microsoft.Extensions.Logging.Abstractions` 10.x |

All packages target `net10.0`. `ImplicitUsings` enabled. `Nullable` enabled. Test sub-folders live inside each project folder (never in a top-level `tests/`).

**Microservices must reference `SharedKernel.Messaging.Abstractions` for DI contracts. They reference `SharedKernel.Messaging.MassTransit` only at the composition root (startup project).**

---

## Technology Stack

| Concern | Technology | Version |
| --- | --- | --- |
| Message bus framework | `MassTransit` | 8.x |
| RabbitMQ transport | `MassTransit.RabbitMQ` | 8.x |
| Azure Service Bus transport | `MassTransit.Azure.ServiceBus.Core` | 8.x |
| Transactional outbox | `MassTransit.EntityFrameworkCoreIntegration` | 8.x |
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

    .Build() → IServiceCollection
        — Registers IMessageBus → MassTransitMessageBus (scoped).
        — Registers IEventPublisher → MassTransitEventPublisher (scoped).
        — Registers MessagingOptions via IOptions<MessagingOptions>.
        — Registers MassTransit IBus, IPublishEndpoint, ISendEndpointProvider (MassTransit-managed scoped).
        — Registers IHostedService for MassTransit bus lifecycle (start/stop via IBusControl).
        — Startup validation: MessagingOptions.ServiceName non-null/non-empty; transport configured.

    NOTE: Exactly one transport (.UseRabbitMq or .UseAzureServiceBus) must be called before .Build() —
          .Build() throws InvalidOperationException if no transport is configured.
          Multiple .AddConsumer<T>() calls are additive.
          All fluent methods return MessagingBusBuilder for chaining.
          No outbox, retry, or transport-specific wiring is done until .Build() is called.
```

---

## Implementation Rules

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
- Any static mutable state.

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

- Queue name: `{service-name}-{consumer-name}` in kebab-case (e.g., `order-service-order-placed-consumer`)
- `MessagingOptions.ServiceName` is used as the prefix

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
```

`SharedKernel.Messaging.Abstractions` ships **no DI extensions** — it is a pure interface library.

---

## AOT Compatibility

- `IMessageBus` and `IEventPublisher` are interfaces — AOT-safe by definition.
- `PublishContext` is a sealed class with no reflection in the hot path — AOT-safe.
- `MessagingOptions` is a plain POCO registered via the options system — AOT-safe.
- `IMessagingBuilder` is an interface — AOT-safe.
- `ConsumerBase<TMessage>` as a closed generic abstract class — AOT-safe. MassTransit consumer type scanning at startup is model-build time only (not a hot path); closed generics preserve type metadata without `[DynamicallyAccessedMembers]` at the call site.
- `MassTransitMessageBus` and `MassTransitEventPublisher` delegate to MassTransit `IBus`/`IPublishEndpoint`; MassTransit 8.x is AOT-compatible for core publish/send paths. Verify on each major upgrade.
- STJ serialization for message payloads: MassTransit 8.x supports source-generated STJ contexts. For NativeAOT builds, consuming services must supply a source-generated `JsonSerializerContext` covering all message and envelope types. Configure via `MessagingBusBuilder` when targeting NativeAOT.
- `EventEnvelope<TEvent>` (`04.Contracts`) must have an entry in the consuming service's source-generated STJ context for NativeAOT builds.
- `MassTransit.EntityFrameworkCoreIntegration` uses EF Core 8+ which is AOT-compatible with compiled models. Verify on each major upgrade.
- Transport packages (`MassTransit.RabbitMQ`, `MassTransit.Azure.ServiceBus.Core`) — verify AOT status on each major upgrade; the `MessagingBusBuilder` abstraction contains the blast radius to the composition layer.
- `DomainEventVersionHelper.GetVersion(Type)` (used by `MassTransitEventPublisher` to populate `SchemaVersion`) reads `[DomainEventVersion]` attribute metadata — attribute reading is preserved by the trimmer and is startup-time only, not a hot path.
- No `Activator.CreateInstance`, `Assembly.Load`, or dynamic reflection in hot paths within `07.Messaging` types.

---

## Test Rules

- Unit tests for each package live in the nested `.Tests/` folder inside that package's folder.
- **`IMessageBus` / `IEventPublisher` mock tests:** mock both interfaces with NSubstitute; verify application handlers call `PublishAsync` / `SendAsync` with the expected event type and arguments; do not test MassTransit internals.
- **`ConsumerBase<TMessage>` tests:** instantiate a concrete subclass via MassTransit `TestHarness`; publish a message; assert `ConsumeAsync` was called with the correct message; assert exceptions propagate without swallowing (NSubstitute throw-configured dependency).
- **Integration tests:** use MassTransit `TestHarness` (`MassTransit.Testing`) — in-memory bus, no broker required; `await harness.InactivityTask` to wait for consumer completion; verify `harness.Consumed.Select<TMessage>()` contains the expected messages.
- **Outbox integration tests:** wire `WithEntityFrameworkOutbox<TDbContext>` to SQLite (or Testcontainers PostgreSQL); publish via `IEventPublisher`; assert outbox row inserted before `SaveChangesAsync`; run outbox delivery worker; assert message delivered to consumer.
- **RabbitMQ integration tests:** use Testcontainers RabbitMQ from `16.Testing/SharedKernel.Testing`; configure `UseRabbitMq` with container connection string; publish and consume; assert end-to-end delivery.
- **No tests against live Azure Service Bus** — use `MassTransit.Testing.TestHarness` for ASB consumer logic; use a Service Bus emulator or skip in CI.
- **Retry policy tests:** configure `RetryOptions.Attempts = 3`; consumer throws on first 2 calls, succeeds on 3rd; assert `ConsumeAsync` called exactly 3 times via `TestHarness.Consumed`.
- **CloudEvents envelope tests:** publish via `IEventPublisher`; intercept the outgoing `EventEnvelope<TEvent>` via `TestHarness`; assert `SourceService`, `CorrelationId`, and `SchemaVersion` are populated correctly.
- **`MessagingBusBuilder` guard tests:** verify `IMessageBus` resolves after `.Build()`; verify `IEventPublisher` resolves; verify startup validation throws `InvalidOperationException` when `MessagingOptions.ServiceName` is null; verify `InvalidOperationException` when `.Build()` called without a transport configured.
- **Consumer endpoint convention tests:** verify queue name follows `{service-name}-{consumer-type}` kebab-case convention via `TestHarness.GetConsumerHarness<TConsumer>().QueueAddress`.
- **`RequestAsync` timeout tests:** verify `RequestAsync<TRequest, TResponse>` throws (or cancels) when no responder is registered and the cancellation token expires.
- **Standard test package set:** `xunit` 2.9.3, `xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.13.0, `coverlet.collector` 6.0.4, `FluentAssertions` 8.4.0, `NSubstitute` 5.3.0, `MassTransit.Testing` 8.x.
- **GlobalUsings.cs required** — every test project must include `global using Xunit;`.
- **SQLite for outbox unit tests** — no Testcontainers needed; SQLite covers EF Core outbox row lifecycle. Use Testcontainers only for broker-level integration tests.

---

## Changelog

> Maintained by the messaging domain agent. One line per significant change.

- [2026-06-05] Domain brain initialized — packages, interfaces, implementation rules, outbox ownership, CloudEvents compliance, AOT notes, test rules
