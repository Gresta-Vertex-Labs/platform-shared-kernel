using Azure.Identity;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Context;
using SharedKernel.Compression;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Messaging.Abstractions.Context;
using SharedKernel.Messaging.Abstractions.Extensions;
using SharedKernel.Messaging.Abstractions.Faults;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;
using SharedKernel.Messaging.Abstractions.Idempotency;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.Abstractions.Scheduling;
using SharedKernel.Messaging.Abstractions.SchemaEvolution;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.Context;
using SharedKernel.Messaging.MassTransit.DeadLetter;
using SharedKernel.Messaging.MassTransit.EventPublisher;
using SharedKernel.Messaging.MassTransit.HeaderPropagation;
using SharedKernel.Messaging.MassTransit.MessageBus;
using SharedKernel.Messaging.MassTransit.Options;
using SharedKernel.Messaging.MassTransit.SchemaEvolution;
using SharedKernel.Messaging.MassTransit.Serialization;
using System.Linq;

// Aliased to avoid the "MassTransit.Configuration" leaf segment colliding with this file's own
// enclosing namespace tree, SharedKernel.Messaging.MassTransit.*.
using MtSystemTextJsonMessageSerializerFactory = MassTransit.Configuration.SystemTextJsonMessageSerializerFactory;

// Alias our batch options to disambiguate from the same-named MassTransit type.
using SkBatchOptions = SharedKernel.Messaging.MassTransit.Options.BatchOptions;

namespace SharedKernel.Messaging.MassTransit.Extensions;

/// <summary>
/// Fluent builder for configuring the MassTransit-backed messaging bus.
/// Returned by <see cref="ServiceCollectionExtensions.AddSharedKernelMessaging(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.Configuration.IConfiguration, System.Action{SharedKernel.Messaging.Abstractions.Options.MessagingOptions})"/>.
/// </summary>
/// <remarks>
/// Call exactly one transport method (<see cref="UseRabbitMq(string)"/> or
/// <see cref="UseAzureServiceBus(string)"/>) before calling <see cref="Build"/>.
/// Multiple <see cref="AddConsumer{TConsumer}()"/> calls are additive.
/// No outbox, retry, or transport wiring is applied until <see cref="Build"/> is called.
/// </remarks>
public sealed class MessagingBusBuilder : IMessagingBuilder
{
    // Transport kind tracking — mutually exclusive.
    private enum TransportKind { None, RabbitMq, AzureServiceBus }

    private TransportKind _transport = TransportKind.None;

    // Stored configuration delegates — applied inside AddMassTransit during Build().
    private Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? _rabbitMqBusConfigurator;
    private Action<IBusRegistrationContext, IServiceBusBusFactoryConfigurator>? _asbBusConfigurator;

    private RetryOptions? _retryOptions;
    private bool _withRetry;

    private CircuitBreakerOptions? _circuitBreakerOptions;
    private bool _withCircuitBreaker;

    private Action<IBusRegistrationConfigurator>? _outboxConfigurator;

    private readonly List<Action<IBusRegistrationConfigurator>> _consumerRegistrations = [];

    // VT-03: CLR types of all registered consumers, used by TranslatorRegistrationValidator
    // to check whether a consumer for TNew exists in this service.
    private readonly List<Type> _registeredConsumerTypes = [];

    // VT-02 / VT-03: Version translator registrations — applied as consumer registrations
    // and validated against _registeredConsumerTypes at Build() time.
    private readonly List<Action<IBusRegistrationConfigurator>> _versionTranslatorRegistrations = [];
    private readonly List<(Type OldType, Type NewType)> _versionTranslatorTypePairs = [];

    // Per-type send endpoint route overrides — populated by WithSendEndpointRoute<T>().
    private readonly Dictionary<Type, string> _sendEndpointRoutes = [];

    // Idempotency (P-134) — set by WithIdempotency().
    private bool _withIdempotency;
    private IdempotencyOptions? _idempotencyOptions;

    // Deferred delivery (P-127) — set by WithDelayedDelivery(). Backed entirely by the transport's
    // own scheduled-delivery feature, so there is no second backend to select between (P-560).
    private bool _withDelayedDelivery;

    // Dead-letter policy (P-343) — set by WithDeadLetterPolicy().
    private bool _withDeadLetterPolicy;
    private DeadLetterOptions? _deadLetterOptions;

    // Payload transform (P-346) — set by WithPayloadTransform().
    // P-561: inbound/outbound caller identity across the bus.
    private bool _withInboundRequestContext;

    private bool _withPayloadTransform;
    private PayloadTransformOptions? _payloadTransformOptions;

    /// <inheritdoc />
    public IServiceCollection Services { get; }

    // Captured inline configure action from AddSharedKernelMessaging.
    // Null when the caller used services.Configure<MessagingOptions>(...) instead.
    private readonly Action<MessagingOptions>? _configure;

    // Resolved and validated ServiceName — populated in Build() from the captured action.
    // Remains null in the deferred-validation path (no inline action supplied).
    private string? _validatedServiceName;

    // Internal ctor — created by AddSharedKernelMessaging only.
    internal MessagingBusBuilder(IServiceCollection services, Action<MessagingOptions>? configure = null)
    {
        Services = services;
        _configure = configure;
    }

    // -------------------------------------------------------------------------
    // Transport
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configures the RabbitMQ transport using a simple AMQP connection string.
    /// </summary>
    /// <param name="connectionString">
    /// AMQP connection string, e.g. <c>"rabbitmq://localhost"</c> or
    /// <c>"amqps://user:pass@rabbitmq.svc.cluster.local/vhost"</c>.
    /// </param>
    /// <returns>This builder for fluent chaining.</returns>
    public MessagingBusBuilder UseRabbitMq(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        EnsureNoTransport();

        _transport = TransportKind.RabbitMq;
        _rabbitMqBusConfigurator = (_, cfg) => cfg.Host(new Uri(connectionString));
        return this;
    }

    /// <summary>
    /// Configures the RabbitMQ transport from an explicit options action.
    /// </summary>
    /// <param name="configure">Action to configure <see cref="RabbitMqBusOptions"/>.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public MessagingBusBuilder UseRabbitMq(Action<RabbitMqBusOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EnsureNoTransport();

        _transport = TransportKind.RabbitMq;

        var opts = new RabbitMqBusOptions();
        configure(opts);

        _rabbitMqBusConfigurator = (_, cfg) => ConfigureRabbitMq(cfg, opts);

        return this;
    }

    /// <summary>
    /// Configures the Azure Service Bus transport using a connection string.
    /// For local development and CI only — use managed identity in production.
    /// </summary>
    /// <param name="connectionString">The Azure Service Bus connection string.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public MessagingBusBuilder UseAzureServiceBus(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        EnsureNoTransport();

        _transport = TransportKind.AzureServiceBus;

        var opts = new AzureServiceBusOptions { ConnectionString = connectionString };
        _asbBusConfigurator = (_, cfg) => ConfigureAzureServiceBus(cfg, opts);

        return this;
    }

    /// <summary>
    /// Configures the Azure Service Bus transport from an explicit options action.
    /// Use <see cref="AzureServiceBusOptions.FullyQualifiedNamespace"/> with managed identity
    /// (<c>DefaultAzureCredential</c>) in Kubernetes workloads.
    /// </summary>
    /// <param name="configure">Action to configure <see cref="AzureServiceBusOptions"/>.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public MessagingBusBuilder UseAzureServiceBus(Action<AzureServiceBusOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EnsureNoTransport();

        _transport = TransportKind.AzureServiceBus;

        var opts = new AzureServiceBusOptions();
        configure(opts);

        ValidateAzureServiceBusOptions(opts);
        _asbBusConfigurator = (_, cfg) => ConfigureAzureServiceBus(cfg, opts);

        return this;
    }

    // -------------------------------------------------------------------------
    // Consumers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers a MassTransit consumer by convention.
    /// The consumer must implement <see cref="IConsumer{TMessage}"/> (directly or via <c>ConsumerBase&lt;TMessage&gt;</c>).
    /// </summary>
    /// <typeparam name="TConsumer">The consumer type to register.</typeparam>
    /// <returns>This builder for fluent chaining.</returns>
    public MessagingBusBuilder AddConsumer<TConsumer>() where TConsumer : class, IConsumer
    {
        _consumerRegistrations.Add(cfg => cfg.AddConsumer<TConsumer>());
        _registeredConsumerTypes.Add(typeof(TConsumer));
        return this;
    }

    /// <summary>
    /// Registers a MassTransit consumer with an explicit consumer definition for custom endpoint name,
    /// prefetch, retry override, or dead-letter configuration.
    /// </summary>
    /// <typeparam name="TConsumer">The consumer type to register.</typeparam>
    /// <typeparam name="TConsumerDefinition">The consumer definition type.</typeparam>
    /// <returns>This builder for fluent chaining.</returns>
    public MessagingBusBuilder AddConsumer<TConsumer, TConsumerDefinition>()
        where TConsumer : class, IConsumer
        where TConsumerDefinition : class, IConsumerDefinition<TConsumer>
    {
        _consumerRegistrations.Add(cfg => cfg.AddConsumer<TConsumer, TConsumerDefinition>());
        _registeredConsumerTypes.Add(typeof(TConsumer));
        return this;
    }

    // -------------------------------------------------------------------------
    // Retry
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configures the global MassTransit retry pipeline with incremental back-off.
    /// Applies to all registered consumers. Omitting this method registers no retry policy.
    /// </summary>
    /// <param name="configure">
    /// Optional action to customise <see cref="RetryOptions"/>.
    /// When <c>null</c>, default retry options apply (3 attempts, 1 s/1 s/30 s).
    /// </param>
    /// <returns>This builder for fluent chaining.</returns>
    public MessagingBusBuilder WithRetry(Action<RetryOptions>? configure = null)
    {
        _withRetry = true;
        _retryOptions = new RetryOptions();
        configure?.Invoke(_retryOptions);
        return this;
    }

    // -------------------------------------------------------------------------
    // Outbox
    // -------------------------------------------------------------------------

    /// <summary>
    /// Wires the MassTransit EF Core transactional outbox using the consuming service's
    /// <typeparamref name="TDbContext"/>.
    /// </summary>
    /// <typeparam name="TDbContext">
    /// The consuming service's EF Core <see cref="DbContext"/> that includes MassTransit outbox tables.
    /// </typeparam>
    /// <param name="configure">
    /// Optional action to customise <see cref="OutboxOptions"/>.
    /// When <c>null</c>, default outbox options apply (100 batch, 1 s delay, 30 min dedup window).
    /// </param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// The consuming service's <typeparamref name="TDbContext"/> must include the MassTransit outbox
    /// tables. Run <c>dotnet ef migrations add AddMassTransitOutbox</c> after calling this method.
    /// </para>
    /// <para>
    /// <c>SharedKernel.Messaging.MassTransit</c> provides no migrations — the consuming service
    /// owns and runs them.
    /// </para>
    /// <para>
    /// At-least-once delivery is guaranteed; all consumers must be idempotent.
    /// </para>
    /// </remarks>
    public MessagingBusBuilder WithEntityFrameworkOutbox<TDbContext>(Action<OutboxOptions>? configure = null)
        where TDbContext : DbContext
    {
        var opts = new OutboxOptions();
        configure?.Invoke(opts);

        _outboxConfigurator = cfg =>
        {
            cfg.AddEntityFrameworkOutbox<TDbContext>(o =>
            {
                o.QueryDelay = opts.QueryDelay;
                o.DuplicateDetectionWindow = opts.DuplicateDetectionWindow;
                o.UseBusOutbox(bo =>
                {
                    bo.MessageDeliveryLimit = opts.BatchSize;
                });
            });
        };

        return this;
    }

    // -------------------------------------------------------------------------
    // Circuit Breaker (P-126)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configures the global MassTransit circuit breaker middleware.
    /// Applies to all consumers. Omitting this method registers no circuit breaker.
    /// </summary>
    /// <param name="configure">
    /// Optional action to customise <see cref="CircuitBreakerOptions"/>.
    /// When <c>null</c>, default options apply
    /// (<see cref="CircuitBreakerOptions.TripThreshold"/> = 5,
    /// <see cref="CircuitBreakerOptions.ActiveThreshold"/> = 10,
    /// <see cref="CircuitBreakerOptions.ResetInterval"/> = 60 s).
    /// </param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Ordering rule:</strong> When both <see cref="WithRetry"/> and
    /// <see cref="WithCircuitBreaker"/> are called, retry must be called first (inner pipeline)
    /// and circuit breaker second (outer pipeline). This ensures retry exhaustion happens within
    /// the current breaker state before the breaker guards against sustained failure.
    /// </para>
    /// <para>
    /// This is a global policy only — do not configure circuit breakers per-consumer via
    /// <c>IConsumerDefinition&lt;T&gt;</c>. Per-consumer overrides defeat the purpose of
    /// a shared failure-count window.
    /// </para>
    /// </remarks>
    public MessagingBusBuilder WithCircuitBreaker(Action<CircuitBreakerOptions>? configure = null)
    {
        _withCircuitBreaker = true;
        _circuitBreakerOptions = new CircuitBreakerOptions();
        configure?.Invoke(_circuitBreakerOptions);
        return this;
    }

    /// <summary>
    /// Registers a fault consumer that handles dead-lettered <typeparamref name="TMessage"/> events.
    /// </summary>
    /// <typeparam name="TMessage">The message type whose faults should be handled.</typeparam>
    /// <typeparam name="TFaultConsumer">
    /// The fault consumer implementation type. Must implement <see cref="IFaultConsumer{TMessage}"/>.
    /// </typeparam>
    /// <returns>This builder for fluent chaining.</returns>
    /// <remarks>
    /// Registers an internal <c>FaultConsumerAdapter&lt;TMessage, TFaultConsumer&gt;</c> that
    /// translates MassTransit <c>Fault&lt;TMessage&gt;</c> messages to
    /// <see cref="IFaultConsumer{TMessage}.HandleAsync"/> calls, mapping
    /// <c>Fault&lt;TMessage&gt;.Exceptions</c> to <see cref="FaultExceptionInfo"/> array entries.
    /// Never register <see cref="IFaultConsumer{TMessage}"/> implementations directly via
    /// <c>services.AddScoped</c> — the adapter wiring will be missing.
    /// </remarks>
    public MessagingBusBuilder AddFaultConsumer<TMessage, TFaultConsumer>()
        where TMessage : class
        where TFaultConsumer : class, IFaultConsumer<TMessage>
    {
        // Register TFaultConsumer itself so DI can inject it into FaultConsumerAdapter.
        Services.AddScoped<TFaultConsumer>();

        // Register the adapter as the MassTransit consumer for Fault<TMessage>.
        _consumerRegistrations.Add(cfg =>
            cfg.AddConsumer<FaultConsumerAdapter<TMessage, TFaultConsumer>>());

        return this;
    }

    // -------------------------------------------------------------------------
    // Scheduling (P-127)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Enables deferred message delivery and registers
    /// <see cref="SharedKernel.Messaging.Abstractions.Scheduling.IMessageScheduler"/> → <c>MassTransitMessageScheduler</c> as scoped.
    /// </summary>
    /// <returns>This builder for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Delivery is deferred by the <strong>transport itself</strong>, not by this process:
    /// RabbitMQ uses the delayed-message exchange (<c>UseDelayedMessageScheduler</c>), Azure Service
    /// Bus uses native scheduled enqueue (<c>UseServiceBusMessageScheduler</c>). A scheduled message
    /// is therefore held by the broker and <strong>survives a restart of the scheduling process</strong>.
    /// </para>
    /// <para>
    /// <strong>RabbitMQ prerequisite:</strong> the broker must have the
    /// <c>rabbitmq_delayed_message_exchange</c> plugin enabled. Without it the delayed exchange
    /// cannot be declared and the bus fails to start. Azure Service Bus needs no prerequisite.
    /// </para>
    /// <para>
    /// This is deferred delivery of a <em>message</em>, not job scheduling. Recurring or cron-shaped
    /// work belongs in <c>19.Scheduling</c>'s <c>IScheduledJobRegistry</c>, which owns that concern
    /// for the platform. Never use <c>Task.Delay</c> inside a consumer as a substitute — it blocks a
    /// thread-pool thread and does not survive a restart.
    /// </para>
    /// <para>
    /// Renamed from <c>WithInMemoryScheduler()</c> in P-560. The old name was inaccurate: it never
    /// wired MassTransit's in-memory scheduler on either transport, and the Quartz alternative it
    /// pointed at for durability has been removed — it required a second relational database to
    /// duplicate a guarantee both transports already provide.
    /// </para>
    /// </remarks>
    public MessagingBusBuilder WithDelayedDelivery()
    {
        _withDelayedDelivery = true;
        return this;
    }

    // -------------------------------------------------------------------------
    // Batch (P-129)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers a batch consumer with MassTransit batch endpoint configuration.
    /// </summary>
    /// <typeparam name="TConsumer">
    /// The batch consumer type. Must derive from <c>BatchConsumerBase&lt;TMessage&gt;</c>.
    /// </typeparam>
    /// <param name="configure">
    /// Optional action to customise <see cref="SharedKernel.Messaging.MassTransit.Options.BatchOptions"/>.
    /// When <c>null</c>, default options apply
    /// (<see cref="SharedKernel.Messaging.MassTransit.Options.BatchOptions.MessageLimit"/> = 10,
    /// <see cref="SharedKernel.Messaging.MassTransit.Options.BatchOptions.TimeLimit"/> = 1 s,
    /// <see cref="SharedKernel.Messaging.MassTransit.Options.BatchOptions.ConcurrencyLimit"/> = 1).
    /// </param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Use this method exclusively for batch consumers.</strong>
    /// Do not register <c>BatchConsumerBase&lt;TMessage&gt;</c> subclasses via
    /// <see cref="AddConsumer{TConsumer}()"/> — batch endpoint configuration will not be applied.
    /// </para>
    /// </remarks>
    public MessagingBusBuilder AddBatchConsumer<TConsumer>(Action<SkBatchOptions>? configure = null)
        where TConsumer : class, IConsumer
    {
        var opts = new SkBatchOptions();
        configure?.Invoke(opts);

        // Capture values so the closure doesn't capture the mutable opts reference.
        var messageLimit = opts.MessageLimit;
        var timeLimit = opts.TimeLimit;
        var concurrencyLimit = opts.ConcurrencyLimit;

        _consumerRegistrations.Add(cfg =>
            cfg.AddConsumer<TConsumer>(configurator =>
                configurator.Options<global::MassTransit.BatchOptions>(o =>
                {
                    o.MessageLimit = messageLimit;
                    o.TimeLimit = timeLimit;
                    o.ConcurrencyLimit = concurrencyLimit;
                })));
        _registeredConsumerTypes.Add(typeof(TConsumer));

        return this;
    }

    // -------------------------------------------------------------------------
    // Routing (P-131)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers a per-command-type queue name override for
    /// <c>IMessageBus.SendAsync&lt;T&gt;()</c>.
    /// </summary>
    /// <typeparam name="T">The command message type to route.</typeparam>
    /// <param name="queueName">
    /// The target queue name (e.g., <c>"payment-service-process-payment"</c>).
    /// Must be non-null and non-empty; validated at call time.
    /// </param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <strong>This is the only approved way to configure cross-service command routing.</strong>
    /// Never pass hardcoded queue URI strings to <c>ISendEndpointProvider.GetSendEndpoint()</c>
    /// directly — hardcoded addresses bypass convention-based routing and break across environments.
    /// </para>
    /// <para>
    /// The queue name must follow the <c>{target-service-name}-{command-type}</c> kebab-case
    /// convention matching the target service's <c>MessagingOptions.ServiceName</c> prefix.
    /// </para>
    /// <para>
    /// When no override is registered, <c>MassTransitMessageBus.SendAsync&lt;T&gt;()</c> falls
    /// back to <c>ConventionSendEndpointResolver</c>, which derives the queue name from
    /// the local service name and the kebab-case type name.
    /// </para>
    /// </remarks>
    public MessagingBusBuilder WithSendEndpointRoute<T>(string queueName)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        _sendEndpointRoutes[typeof(T)] = queueName;
        return this;
    }

    // -------------------------------------------------------------------------
    // Idempotency (P-134)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers <c>IdempotentConsumerBehavior&lt;TMessage&gt;</c> as a global MassTransit
    /// consume pipeline filter applied to all consumers.
    /// </summary>
    /// <returns>This builder for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// The consuming service must register a concrete <see cref="IIdempotencyStore"/>
    /// implementation before calling this method. Calling <see cref="Build"/> without
    /// a registered <see cref="IIdempotencyStore"/> throws <see cref="InvalidOperationException"/>
    /// with a diagnostic message.
    /// </para>
    /// <para>
    /// SharedKernel does not provide an <see cref="IIdempotencyStore"/> implementation —
    /// the consuming service bridges to its own persistence layer
    /// (e.g., <c>RedisIdempotencyStore</c>, <c>EfCoreIdempotencyStore</c>).
    /// </para>
    /// <para>
    /// To configure <see cref="IdempotencyOptions"/> (e.g., <c>ExpiryWindow</c>) in addition to
    /// registering the behavior, use
    /// <see cref="WithIdempotency(Action{IdempotencyOptions})"/> instead.
    /// </para>
    /// </remarks>
    public MessagingBusBuilder WithIdempotency()
    {
        _withIdempotency = true;
        return this;
    }

    /// <summary>
    /// Registers <c>IdempotentConsumerBehavior&lt;TMessage&gt;</c> as a global MassTransit
    /// consume pipeline filter and configures <see cref="IdempotencyOptions"/>.
    /// </summary>
    /// <param name="configure">
    /// Action to configure <see cref="IdempotencyOptions"/> (e.g., set
    /// <see cref="IdempotencyOptions.ExpiryWindow"/>).
    /// </param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="IdempotencyOptions"/> is available to the consuming service's
    /// <see cref="IIdempotencyStore"/> implementation via
    /// <c>IOptions&lt;IdempotencyOptions&gt;</c>.
    /// </para>
    /// <para>
    /// The consuming service must register a concrete <see cref="IIdempotencyStore"/>
    /// implementation before calling this method. Calling <see cref="Build"/> without
    /// a registered <see cref="IIdempotencyStore"/> throws <see cref="InvalidOperationException"/>
    /// with a diagnostic message.
    /// </para>
    /// </remarks>
    public MessagingBusBuilder WithIdempotency(Action<IdempotencyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _withIdempotency = true;
        _idempotencyOptions = new IdempotencyOptions();
        configure(_idempotencyOptions);
        return this;
    }

    // -------------------------------------------------------------------------
    // Header Propagation (P-135)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers <typeparamref name="T"/> as a scoped <see cref="IMessageHeaderPropagator"/> in DI.
    /// Multiple calls are additive — all registered propagators are applied in registration order
    /// at every <c>IMessageBus.PublishAsync</c> and <c>IEventPublisher.PublishAsync</c> call.
    /// </summary>
    /// <typeparam name="T">
    /// The propagator implementation type. Must implement <see cref="IMessageHeaderPropagator"/>.
    /// </typeparam>
    /// <returns>This builder for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Propagators run <em>before</em> the explicit <see cref="Action{T}"/> configure callback.
    /// When the same header key is set by a propagator and by an explicit callback,
    /// the explicit callback wins.
    /// </para>
    /// <para>
    /// Implement propagators in the consuming service's composition root — not inside SharedKernel
    /// packages. Propagators require access to service-specific ambient context
    /// (e.g., <c>IHttpContextAccessor</c>, tenant resolution) that is not available in SharedKernel.
    /// </para>
    /// </remarks>
    public MessagingBusBuilder WithHeaderPropagator<T>()
        where T : class, IMessageHeaderPropagator
    {
        Services.AddScoped<IMessageHeaderPropagator, T>();
        return this;
    }

    /// <summary>
    /// Registers the built-in <see cref="AmbientCorrelationHeaderPropagator"/> as a scoped
    /// <see cref="IMessageHeaderPropagator"/>. Zero-argument — requires no consumer-authored class,
    /// unlike <see cref="WithHeaderPropagator{T}"/>.
    /// </summary>
    /// <returns>This builder for fluent chaining.</returns>
    /// <remarks>
    /// Populates <see cref="SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext.CorrelationId"/>
    /// from the ambient <see cref="System.Diagnostics.Activity.Current"/> on every dispatch verb
    /// (<c>PublishAsync</c>, <c>SendAsync</c>, and <c>IEventPublisher.PublishAsync</c>).
    /// Distributed-trace correlation identity needs no consuming-service-supplied dependency — it
    /// is one of three named, documented exceptions to "never implement
    /// <see cref="IMessageHeaderPropagator"/> inside SharedKernel" (P-345/WO-054; the others are
    /// <see cref="TenantHeaderPropagator"/> and <see cref="RequestContextHeaderPropagator"/>).
    /// </remarks>
    public MessagingBusBuilder WithAmbientCorrelationPropagation()
    {
        Services.AddScoped<IMessageHeaderPropagator, AmbientCorrelationHeaderPropagator>();
        return this;
    }

    /// <summary>
    /// Registers the built-in <see cref="TenantHeaderPropagator"/> as a scoped
    /// <see cref="IMessageHeaderPropagator"/>, so every outgoing message carries the tenant of the ambient
    /// request context, and <see cref="IRequestContextAccessor"/> unless one is already registered.
    /// </summary>
    /// <returns>This builder for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// The tenant comes from <see cref="IRequestContextAccessor.Current"/>, which every inbound adapter sets —
    /// the consuming service writes no class at all, mirroring
    /// <see cref="WithAmbientCorrelationPropagation"/>'s zero-consumer-boilerplate shape.
    /// </para>
    /// <para>
    /// When this method is not called, <see cref="TenantHeaderPropagator"/> is never registered and
    /// <see cref="SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext.TenantId"/>
    /// is populated only via an explicit
    /// <see cref="SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext.WithTenantId"/>
    /// call or another registered <see cref="WithHeaderPropagator{T}"/> propagator.
    /// </para>
    /// </remarks>
    public MessagingBusBuilder WithTenantContext()
    {
        Services.TryAddSingleton<IRequestContextAccessor, RequestContextAccessor>();
        Services.AddScoped<IMessageHeaderPropagator, TenantHeaderPropagator>();
        return this;
    }

    /// <summary>
    /// Carries the calling identity across the bus: the publisher's tenant and actor are written
    /// onto every outgoing message, and rebuilt on the consumer so that <c>IRequestContext</c>
    /// resolves to the caller that caused the message instead of to nobody.
    /// </summary>
    /// <returns>This builder for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <strong>The problem it solves.</strong> A consumer runs with no HTTP request, so
    /// <c>IRequestContext.TenantId</c> is <see langword="null"/> and <c>06.Persistence</c> — which
    /// fails closed on a null tenant — rejects every tenant-scoped write and returns nothing for
    /// every tenant-scoped read. Without this, handling an integration event in a multi-tenant
    /// service means passing the tenant by hand through the message body and entering a
    /// cross-tenant scope to act on it.
    /// </para>
    /// <para>
    /// <strong>What it registers.</strong> Outbound, a
    /// <see cref="RequestContextHeaderPropagator"/> writing the tenant
    /// (<c>01.Core</c>'s <c>WellKnownHeaders.TenantId</c>) and the actor
    /// (<c>MessageContextHeaders</c>). Inbound, a consume filter that reads them back and a
    /// message-aware <c>IRequestContext</c> that answers from the message inside a consume and from
    /// the service's own registration everywhere else — so one handler serves both paths without
    /// branching.
    /// </para>
    /// <para>
    /// <strong>Call it after the service's own <c>IRequestContext</c> registration</strong>
    /// (typically <c>13.ServiceDefaults</c>' <c>AddSharedKernelRequestContext()</c>). The container
    /// resolves the last registration, so the reverse order would silently leave consumers with no
    /// tenant; a startup warning is logged if that happens.
    /// </para>
    /// <para>
    /// <strong>Attribution, not authorization.</strong> A permission check inside a consume always
    /// answers <see langword="false"/>, and the values are only as trustworthy as who can reach the
    /// broker. See <see cref="MessageRequestContext"/> for the full trust boundary.
    /// </para>
    /// <example>
    /// <code>
    /// builder.Services
    ///     .AddSharedKernelMessaging(o =&gt; o.ServiceName = "billing-service")
    ///     .UseRabbitMq(connectionString)
    ///     .WithInboundRequestContext()
    ///     .AddConsumer&lt;OrderPlacedConsumer&gt;()
    ///     .Build();
    /// </code>
    /// </example>
    /// </remarks>
    public MessagingBusBuilder WithInboundRequestContext()
    {
        if (_withInboundRequestContext)
            return this;

        _withInboundRequestContext = true;

        // One holder per MassTransit delivery scope. Registered concretely as well as behind the
        // interface so the consume filter can write to the very instance consumers read from.
        Services.AddScoped<InboundMessageContextAccessor>();
        Services.AddScoped<IInboundMessageContextAccessor>(
            sp => sp.GetRequiredService<InboundMessageContextAccessor>());

        Services.AddScoped(typeof(InboundRequestContextFilter<>));

        CaptureExistingRequestContextRegistration();
        Services.AddScoped<IRequestContext, MessageAwareRequestContext>();

        Services.AddScoped<IMessageHeaderPropagator, RequestContextHeaderPropagator>();
        Services.AddSingleton<IHostedService, RequestContextRegistrationAdvisoryHostedService>();

        return this;
    }

    /// <summary>
    /// Re-registers whatever <c>IRequestContext</c> the service already had under
    /// <see cref="HostRequestContextSource"/>, so the message-aware context about to shadow it can
    /// still fall back to it outside a consume.
    /// </summary>
    /// <remarks>
    /// The descriptor is turned back into a factory by hand because <c>IServiceCollection</c> holds
    /// three shapes — instance, factory, implementation type — and only the first two are already
    /// callable. The original lifetime is preserved, so a scoped HTTP-backed context is still built
    /// once per request.
    /// <para>
    /// Keyed registrations are skipped: they are resolved by key, never by
    /// <c>GetService&lt;IRequestContext&gt;()</c>, so they are not what a consumer would have got
    /// anyway — and reading <c>ImplementationType</c> off a keyed descriptor throws.
    /// </para>
    /// </remarks>
    private void CaptureExistingRequestContextRegistration()
    {
        var existing = Services.LastOrDefault(
            d => d.ServiceType == typeof(IRequestContext) && !d.IsKeyedService);

        if (existing is null)
            return;

        Func<IServiceProvider, object> factory =
            existing.ImplementationInstance is { } instance ? _ => instance
            : existing.ImplementationFactory is { } existingFactory ? existingFactory
            : sp => ActivatorUtilities.CreateInstance(sp, existing.ImplementationType!);

        Services.Add(new ServiceDescriptor(
            typeof(HostRequestContextSource),
            sp => new HostRequestContextSource((IRequestContext)factory(sp)),
            existing.Lifetime));
    }

    // -------------------------------------------------------------------------
    // Version Translation (P-137)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers <typeparamref name="TTranslator"/> as a singleton
    /// <see cref="IMessageVersionTranslator{TOld, TNew}"/> and wires a translating consumer so
    /// that messages of the legacy schema <typeparamref name="TOld"/> arriving at the transport
    /// are projected to <typeparamref name="TNew"/> before delivery to the consumer registered
    /// for <typeparamref name="TNew"/>.
    /// </summary>
    /// <typeparam name="TOld">The legacy message schema type.</typeparam>
    /// <typeparam name="TNew">The current message schema type.</typeparam>
    /// <typeparam name="TTranslator">
    /// The translator implementation type. Must implement
    /// <see cref="IMessageVersionTranslator{TOld, TNew}"/>.
    /// </typeparam>
    /// <returns>This builder for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// No consumer code change is required — the consumer for <typeparamref name="TNew"/> receives
    /// the translated payload as if it had been published directly.
    /// </para>
    /// <para>
    /// At <see cref="Build"/> time, an advisory <see cref="Microsoft.Extensions.Logging.LogLevel.Warning"/>
    /// is logged if no consumer for <typeparamref name="TNew"/> is registered in this service —
    /// this does not throw, since the consumer for <typeparamref name="TNew"/> may be registered
    /// in a separate service.
    /// </para>
    /// </remarks>
    public MessagingBusBuilder WithVersionTranslator<TOld, TNew, TTranslator>()
        where TOld : class
        where TNew : class
        where TTranslator : class, IMessageVersionTranslator<TOld, TNew>
    {
        Services.AddSingleton<IMessageVersionTranslator<TOld, TNew>, TTranslator>();

        _versionTranslatorRegistrations.Add(cfg =>
            cfg.AddConsumer<VersionTranslatingConsumer<TOld, TNew>>());

        _versionTranslatorTypePairs.Add((typeof(TOld), typeof(TNew)));

        return this;
    }

    // -------------------------------------------------------------------------
    // Dead-Letter Policy (P-343)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configures a RabbitMQ dead-letter/poison-message delivery policy — the message time-to-live
    /// applied to the automatically-derived fault/dead-letter destination.
    /// </summary>
    /// <param name="configure">
    /// Optional action to customise <see cref="DeadLetterOptions"/>.
    /// When <see langword="null"/>, default options apply
    /// (<see cref="DeadLetterOptions.QueueNameSuffix"/> = <c>"_error"</c>,
    /// <see cref="DeadLetterOptions.MessageTimeToLive"/> = <see langword="null"/>).
    /// </param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// RabbitMQ only. <see cref="DeadLetterOptions.MessageTimeToLive"/>, when set, is applied as the
    /// <c>x-message-ttl</c> argument on the receive endpoint's automatically-derived fault and
    /// dead-letter queues via <c>IRabbitMqSendTopologyConfigurator.ConfigureErrorSettings</c> /
    /// <c>.ConfigureDeadLetterSettings</c> — the same queues MassTransit routes a message to once
    /// retries are exhausted or a <c>ConsumerDefinitionBase&lt;TConsumer&gt;.NonRetryableExceptions</c>
    /// filter classifies the exception as fatal.
    /// </para>
    /// <para>
    /// <strong>Capability note:</strong> see
    /// <see cref="DeadLetterOptions.QueueNameSuffix"/> for why the suffix itself has no observable
    /// effect on the destination's name in the installed MassTransit version — only
    /// <see cref="DeadLetterOptions.MessageTimeToLive"/> is currently wired.
    /// </para>
    /// <para>
    /// When called while <see cref="UseAzureServiceBus(string)"/> is the configured transport, this
    /// is a no-op — <c>Build()</c> registers an advisory-warning
    /// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> instead of throwing, since Azure
    /// Service Bus dead-lettering is entirely transport-native.
    /// </para>
    /// <para>
    /// Optional. Omitting this method preserves MassTransit's own default RabbitMQ error-queue
    /// behavior — this domain adds a configuration surface, it does not change the unconfigured
    /// default.
    /// </para>
    /// </remarks>
    public MessagingBusBuilder WithDeadLetterPolicy(Action<DeadLetterOptions>? configure = null)
    {
        _withDeadLetterPolicy = true;
        _deadLetterOptions = new DeadLetterOptions();
        configure?.Invoke(_deadLetterOptions);
        return this;
    }

    // -------------------------------------------------------------------------
    // Payload Transform (P-346)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configures opt-in compression and/or encryption of a message's serialized payload before it
    /// reaches the transport (and the reverse on consume), built entirely on <c>01.Core</c>'s
    /// <c>SharedKernel.Compression</c>/<c>SharedKernel.Cryptography</c> primitives.
    /// </summary>
    /// <param name="configure">
    /// Optional action to customise <see cref="PayloadTransformOptions"/>.
    /// When <see langword="null"/>, both <see cref="PayloadTransformOptions.EnableCompression"/> and
    /// <see cref="PayloadTransformOptions.EnableEncryption"/> default to <see langword="false"/> —
    /// calling this method with no configuration has no observable effect on the wire format.
    /// </param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// The publish-side ordering is always compress-then-encrypt; the consume-side ordering is
    /// always decrypt-then-decompress. Neither ordering is caller-configurable.
    /// </para>
    /// <para>
    /// <see cref="Build"/> throws <see cref="InvalidOperationException"/> if
    /// <see cref="PayloadTransformOptions.EnableCompression"/> is set and no
    /// <see cref="IPayloadCompressor"/> is registered (via <c>SharedKernel.Compression</c>'s
    /// <c>AddSharedKernelCompression()</c>), or if
    /// <see cref="PayloadTransformOptions.EnableEncryption"/> is set and no
    /// <see cref="ISynchronousSymmetricEncryptionService"/> is registered (via <c>SharedKernel.Cryptography</c>'s
    /// <c>AddSharedKernelCryptography(configuration).AddSynchronousSymmetricEncryption()</c>).
    /// </para>
    /// <para>
    /// <strong>This changes the wire format</strong> of every message published on this bus once
    /// either flag is enabled — every consumer of a message type published through this bus must
    /// configure a matching <see cref="PayloadTransformOptions"/> (identical
    /// <see cref="PayloadTransformOptions.EnableCompression"/>/<see cref="PayloadTransformOptions.EnableEncryption"/>
    /// values), or deserialization fails loudly with
    /// <see cref="PayloadTransformMismatchException"/> instead of silently misinterpreting the
    /// payload.
    /// </para>
    /// <para>
    /// <b>STRUCTURAL LIMITATION — KMS/HSM-BACKED KEY PROVIDERS CANNOT BE USED WITH
    /// <see cref="PayloadTransformOptions.EnableEncryption"/>.</b> MassTransit's
    /// <c>IMessageSerializer.GetMessageBody&lt;T&gt;</c>/<c>IMessageDeserializer.Deserialize</c>
    /// pipeline stage exposes no async member anywhere in MassTransit, so this feature uses
    /// <see cref="ISynchronousSymmetricEncryptionService"/>, which needs an
    /// <see cref="ISynchronousEncryptionKeyProvider"/> holding its keys in memory (for example
    /// <see cref="StaticEncryptionKeyProvider"/>) — never a KMS/HSM-backed provider (Azure Key
    /// Vault, AWS KMS, HashiCorp Vault), which can only resolve keys asynchronously. A missing
    /// <see cref="ISynchronousEncryptionKeyProvider"/> fails when the bus is configured at startup,
    /// with a message naming the required registrations.
    /// </para>
    /// <para>
    /// Once encryption is enabled, associated data (AAD) derived from the message's own CLR type
    /// name authenticates every ciphertext; the consumer reads it back from a transport header, and
    /// a message without that header fails with <see cref="PayloadTransformMismatchException"/>.
    /// </para>
    /// </remarks>
    public MessagingBusBuilder WithPayloadTransform(Action<PayloadTransformOptions>? configure = null)
    {
        _withPayloadTransform = true;
        _payloadTransformOptions = new PayloadTransformOptions();
        configure?.Invoke(_payloadTransformOptions);
        return this;
    }

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Finalises the bus registration, wiring all configured transports, consumers, retry policies,
    /// and outbox into the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <returns>The <see cref="IServiceCollection"/> for continued registration.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no transport has been configured, or (in the inline-action path) when
    /// <c>MessagingOptions.ServiceName</c> is null or whitespace.
    /// </exception>
    /// <remarks>
    /// <para>
    /// When <c>AddSharedKernelMessaging(o => o.ServiceName = "...")</c> was used, Build() validates
    /// <c>ServiceName</c> eagerly using the captured action — no <c>BuildServiceProvider()</c> call
    /// is made.
    /// </para>
    /// <para>
    /// When <c>services.Configure&lt;MessagingOptions&gt;(config.GetSection(...))</c> was used
    /// instead, validation is deferred to startup via <c>ValidateOnStart()</c>. Build() proceeds
    /// without throwing but cannot guarantee the naming convention prefix at build time.
    /// </para>
    /// </remarks>
    public IServiceCollection Build()
    {
        if (_transport == TransportKind.None)
            throw new InvalidOperationException(
                "No transport configured. Call UseRabbitMq() or UseAzureServiceBus() before Build().");

        // ID-04 / P-134: Guard — WithIdempotency() requires IIdempotencyStore to be registered.
        if (_withIdempotency)
        {
            var storeDescriptor = Services.FirstOrDefault(
                d => d.ServiceType == typeof(IIdempotencyStore));

            if (storeDescriptor is null)
                throw new InvalidOperationException(
                    "IIdempotencyStore is not registered. " +
                    "Call services.AddScoped<IIdempotencyStore, YourImplementation>() before calling WithIdempotency().");
        }

        // PT-04 / P-346: Guard — WithPayloadTransform() requires the matching 01.Core primitive to
        // already be registered for each enabled flag.
        if (_withPayloadTransform && _payloadTransformOptions is not null)
        {
            if (_payloadTransformOptions.EnableCompression
                && Services.FirstOrDefault(d => d.ServiceType == typeof(IPayloadCompressor)) is null)
            {
                throw new InvalidOperationException(
                    "PayloadTransformOptions.EnableCompression is set but no IPayloadCompressor is registered. " +
                    "Call services.AddSharedKernelCompression(configuration) before calling WithPayloadTransform().");
            }

            // MassTransit serializers are synchronous, so encryption needs the synchronous service.
            // Its key provider is resolved at startup, where a missing one fails with the same guidance.
            if (_payloadTransformOptions.EnableEncryption
                && Services.FirstOrDefault(d => d.ServiceType == typeof(ISynchronousSymmetricEncryptionService)) is null)
            {
                throw new InvalidOperationException(
                    "PayloadTransformOptions.EnableEncryption is set but no ISynchronousSymmetricEncryptionService is registered. " +
                    MissingSynchronousEncryptionGuidance);
            }
        }

        // C-21 / C-22: Validate and resolve ServiceName without BuildServiceProvider().
        // Inline-action path: apply the captured delegate to a local instance and validate inline.
        // Deferred path (config-section binding, no inline action): skip eager validation —
        //   ValidateOnStart() registered above handles it at startup.
        if (_configure is not null)
        {
            var opts = new MessagingOptions();
            _configure(opts);

            var validator = new MessagingOptionsValidator();
            var result = validator.Validate(null, opts);

            if (result.Failed)
                throw new Microsoft.Extensions.Options.OptionsValidationException(
                    nameof(MessagingOptions),
                    typeof(MessagingOptions),
                    result.Failures ?? ["MessagingOptions validation failed."]);

            _validatedServiceName = opts.ServiceName;
        }
        // C-23: Deferred path — no inline action, validation deferred to ValidateOnStart().

        // The endpoint name formatter, resolved from the bound options rather than from anything
        // captured here.
        //
        // P-561: it used to be built from _validatedServiceName, which is set only when an inline
        // action was supplied — so a service that configured ServiceName through the
        // SharedKernel:Messaging section got an EMPTY prefix and queues named "hold-shipment"
        // instead of "orders-api-hold-shipment". Two services in one broker would then contend for
        // the same queue. Nothing failed; the wrong queues were simply declared, which is why it
        // survived until samples/ShippingApi ran against a real broker and the names were visible.
        //
        // The factory runs when the container is built, by which point configuration binding has
        // happened, so both registration paths now produce the same name.
        Services.AddSingleton<IEndpointNameFormatter>(sp => new KebabCaseEndpointNameFormatter(
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MessagingOptions>>().Value.ServiceName,
            includeNamespace: false));

        // ID-05 / P-134: Register IdempotencyOptions if WithIdempotency(Action<>) overload was used.
        if (_withIdempotency && _idempotencyOptions is not null)
        {
            var idempotencyOpts = _idempotencyOptions;
            Services.Configure<IdempotencyOptions>(o => o.ExpiryWindow = idempotencyOpts.ExpiryWindow);
        }

        // ID-03 / P-134: Register IdempotentConsumerBehavior as scoped so DI can inject IIdempotencyStore.
        // The open-generic type is registered and resolved per-message-type by MassTransit.
        if (_withIdempotency)
            Services.AddScoped(typeof(IdempotentConsumerBehavior<>));

        // RO-05: Register the route map as a singleton (immutable snapshot captured at Build time).
        // Capture a read-only copy so further builder mutations don't affect the registered map.
        var routeMap = (IReadOnlyDictionary<Type, string>)
            new System.Collections.ObjectModel.ReadOnlyDictionary<Type, string>(
                new Dictionary<Type, string>(_sendEndpointRoutes));
        Services.AddSingleton(routeMap);

        // RO-02: Register ConventionSendEndpointResolver as scoped (needs IOptions<MessagingOptions>).
        Services.AddScoped<ConventionSendEndpointResolver>();

        // Register IMessageBus and IEventPublisher as scoped.
        Services.AddScoped<Abstractions.MessageBus.IMessageBus, MassTransitMessageBus>();
        Services.AddScoped<Abstractions.EventPublisher.IEventPublisher, MassTransitEventPublisher>();

        // P-347: Register the bus-backed readiness probe as a singleton, unconditionally — no
        // opt-in builder call required. Matches MassTransit's own singleton IBus/IBusControl
        // lifetime; it is a pure read-only reflection of the bus this builder already constructs,
        // not a new capability with its own configuration surface.
        Services.AddSingleton<Abstractions.MessageBus.IMessageBusProbe, MassTransitMessageBusProbe>();

        // SC-06: Register IMessageScheduler → MassTransitMessageScheduler as scoped.
        // Uses the fully qualified abstraction type to avoid IMessageScheduler ambiguity
        // between MassTransit.IMessageScheduler and SharedKernel.Messaging.Abstractions.Scheduling.IMessageScheduler.
        if (_withDelayedDelivery)
            Services.AddScoped<
                SharedKernel.Messaging.Abstractions.Scheduling.IMessageScheduler,
                MassTransitMessageScheduler>();

        // P-137: Register an advisory startup check for WithVersionTranslator registrations
        // against consumers registered in this service. Runs once at host startup using the
        // application's configured ILogger — does not throw, logs Warning only.
        if (_versionTranslatorTypePairs.Count > 0)
        {
            var typePairs = (IReadOnlyList<(Type OldType, Type NewType)>)[.. _versionTranslatorTypePairs];
            var registeredConsumerTypes = (IReadOnlyCollection<Type>)[.. _registeredConsumerTypes];

            Services.AddSingleton<IHostedService>(sp =>
                new TranslatorRegistrationValidationHostedService(
                    typePairs,
                    registeredConsumerTypes,
                    sp.GetRequiredService<ILogger<TranslatorRegistrationValidationHostedService>>()));
        }

        // P-343: Register an advisory startup check when WithDeadLetterPolicy() was called while
        // the Azure Service Bus transport is configured — DeadLetterOptions is RabbitMQ-only.
        if (_withDeadLetterPolicy && _transport == TransportKind.AzureServiceBus)
        {
            Services.AddSingleton<IHostedService, DeadLetterPolicyAdvisoryHostedService>();
        }

        // Register MassTransit with all configuration.
        Services.AddMassTransit(cfg =>
        {
            // Apply consumer registrations.
            foreach (var registration in _consumerRegistrations)
                registration(cfg);

            // P-137: Apply version translator registrations (translating consumers).
            foreach (var registration in _versionTranslatorRegistrations)
                registration(cfg);

            // Apply outbox configuration.
            _outboxConfigurator?.Invoke(cfg);

            // SC-06: Wire the transport-native message scheduler.
            // For RabbitMQ, use the delayed message scheduler (transport-delay header).
            // For Azure Service Bus, use the ASB-native scheduler (ScheduledEnqueueTimeUtc property).
            // Both register MassTransit.IMessageScheduler in DI, which MassTransitMessageScheduler wraps.
            if (_withDelayedDelivery)
            {
                if (_transport == TransportKind.AzureServiceBus)
                    cfg.AddServiceBusMessageScheduler();
                else
                    cfg.AddDelayedMessageScheduler();
            }

            // The formatter comes from DI (registered above), so it reads the bound ServiceName.
            // SetEndpointNameFormatter is deliberately NOT called: it takes an instance, which
            // would have to be constructed here — before configuration has been bound.

            // Configure transport.
            if (_transport == TransportKind.RabbitMq)
            {
                cfg.UsingRabbitMq((ctx, busCfg) =>
                {
                    _rabbitMqBusConfigurator?.Invoke(ctx, busCfg);

                    // SC-06: Wire the RabbitMQ delayed-message exchange for deferred delivery.
                    if (_withDelayedDelivery)
                        busCfg.UseDelayedMessageScheduler();

                    // P-561: the inbound identity filter runs ahead of everything else, so a
                    // tenant-partitioned idempotency store already knows the tenant when it
                    // reserves the message id.
                    if (_withInboundRequestContext)
                        busCfg.UseConsumeFilter(typeof(InboundRequestContextFilter<>), ctx);


                    // ID-03 / P-134: Wire global idempotency consume pipeline filter.
                    // UseConsumeFilter with the open generic type applies to all message types.
                    if (_withIdempotency)
                        busCfg.UseConsumeFilter(typeof(IdempotentConsumerBehavior<>), ctx);

                    // P-343: Apply the dead-letter policy's message TTL to the automatically-derived
                    // fault/dead-letter queues.
                    if (_withDeadLetterPolicy && _deadLetterOptions is not null)
                        ConfigureDeadLetterPolicy(busCfg, _deadLetterOptions);

                    // P-346: Wire the compress/encrypt payload-transform serializer when enabled.
                    if (_withPayloadTransform && _payloadTransformOptions is not null)
                        ConfigurePayloadTransform(busCfg, ctx, _payloadTransformOptions);

                    ConfigureResilience(busCfg);
                    busCfg.ConfigureEndpoints(ctx);
                });
            }
            else if (_transport == TransportKind.AzureServiceBus)
            {
                cfg.UsingAzureServiceBus((ctx, busCfg) =>
                {
                    _asbBusConfigurator?.Invoke(ctx, busCfg);

                    // SC-06: Wire ASB native scheduled delivery (ScheduledEnqueueTimeUtc).
                    if (_withDelayedDelivery)
                        busCfg.UseServiceBusMessageScheduler();

                    // P-561: the inbound identity filter runs ahead of everything else, so a
                    // tenant-partitioned idempotency store already knows the tenant when it
                    // reserves the message id.
                    if (_withInboundRequestContext)
                        busCfg.UseConsumeFilter(typeof(InboundRequestContextFilter<>), ctx);


                    // ID-03 / P-134: Wire global idempotency consume pipeline filter.
                    if (_withIdempotency)
                        busCfg.UseConsumeFilter(typeof(IdempotentConsumerBehavior<>), ctx);

                    // P-346: Wire the compress/encrypt payload-transform serializer when enabled.
                    if (_withPayloadTransform && _payloadTransformOptions is not null)
                        ConfigurePayloadTransform(busCfg, ctx, _payloadTransformOptions);

                    ConfigureResilience(busCfg);
                    busCfg.ConfigureEndpoints(ctx);
                });
            }
        });

        return Services;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void EnsureNoTransport()
    {
        if (_transport != TransportKind.None)
            throw new InvalidOperationException(
                "A transport has already been configured. Only one transport (RabbitMQ or Azure Service Bus) is permitted per bus instance.");
    }

    private void ConfigureResilience(IBusFactoryConfigurator busCfg)
    {
        // Apply retry (inner pipeline) first.
        if (_withRetry && _retryOptions is not null)
        {
            var opts = _retryOptions;
            busCfg.UseMessageRetry(r =>
            {
                if (opts.ImmediateAttempts > 0)
                    r.Immediate(opts.ImmediateAttempts);

                r.Incremental(
                    retryLimit: opts.Attempts - 1,
                    initialInterval: opts.InitialInterval,
                    intervalIncrement: opts.IntervalIncrement);
            });
        }

        // Apply circuit breaker (outer pipeline) second — guards against sustained failure.
        if (_withCircuitBreaker && _circuitBreakerOptions is not null)
        {
            var cb = _circuitBreakerOptions;
            busCfg.UseCircuitBreaker(x =>
            {
                x.TripThreshold = cb.TripThreshold;
                x.ActiveThreshold = cb.ActiveThreshold;
                x.ResetInterval = cb.ResetInterval;
                x.TrackingPeriod = cb.TrackingPeriod;
            });
        }
    }

    // Internal (not private) so ConcurrencyLimitConfigurationTests can exercise this helper
    // in isolation via a substituted IServiceBusBusFactoryConfigurator (P-342/WO-054).
    internal static void ConfigureAzureServiceBus(
        IServiceBusBusFactoryConfigurator cfg,
        AzureServiceBusOptions opts)
    {
        if (!string.IsNullOrWhiteSpace(opts.FullyQualifiedNamespace))
        {
            // Managed identity path — construct service URI from the fully qualified namespace.
            var serviceUri = new Uri($"sb://{opts.FullyQualifiedNamespace}");
            cfg.Host(serviceUri, h =>
            {
                h.TokenCredential = new DefaultAzureCredential();
                h.TransportType = opts.TransportType;
            });
        }
        else if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
        {
            cfg.Host(opts.ConnectionString, h =>
            {
                h.TransportType = opts.TransportType;
            });
        }
        else
        {
            throw new InvalidOperationException(
                "AzureServiceBusOptions requires exactly one of ConnectionString or FullyQualifiedNamespace to be set.");
        }

        // P-342/WO-054: Apply the bus-level receive endpoint concurrency default. Previously this
        // option was read into AzureServiceBusOptions but never consulted anywhere the bus was
        // actually built — setting it had zero observable effect.
        // NOTE: IServiceBusEndpointConfigurator.MaxConcurrentCalls is obsolete
        // ("Set ConcurrentMessageLimit instead (which is exactly what setting this property does)").
        // ConcurrentMessageLimit (from the core IBusFactoryConfigurator, shared with the RabbitMQ
        // transport) is the current API — setting it here is the transport-correct equivalent of
        // the old MaxConcurrentCalls assignment.
        cfg.ConcurrentMessageLimit = opts.MaxConcurrentCalls;
    }

    // Internal (not private) so ConcurrencyLimitConfigurationTests can exercise this helper
    // in isolation via a substituted IRabbitMqBusFactoryConfigurator (P-342/WO-054).
    internal static void ConfigureRabbitMq(IRabbitMqBusFactoryConfigurator cfg, RabbitMqBusOptions opts)
    {
        cfg.Host(opts.Host, opts.VirtualHost, h =>
        {
            h.Username(opts.Username);
            h.Password(opts.Password);
            h.Heartbeat(opts.RequestedHeartbeat);
        });

        cfg.PrefetchCount = opts.Prefetch;

        // P-342/WO-054: Optional bus-level default concurrency ceiling, distinct from PrefetchCount.
        // A per-consumer override on ConsumerDefinitionBase<TConsumer>.ConcurrentMessageLimit takes
        // precedence over this default on that consumer's own endpoint.
        if (opts.ConcurrentMessageLimit.HasValue)
            cfg.ConcurrentMessageLimit = opts.ConcurrentMessageLimit.Value;
    }

    // Internal (not private) so DeadLetterPolicyConfigurationTests can exercise this helper
    // in isolation via a substituted IRabbitMqBusFactoryConfigurator (P-343/WO-054).
    internal static void ConfigureDeadLetterPolicy(IRabbitMqBusFactoryConfigurator cfg, DeadLetterOptions opts)
    {
        // Capability note: IRabbitMqSendTopologyConfigurator.ConfigureErrorSettings/
        // .ConfigureDeadLetterSettings configure the ARGUMENTS of the automatically-derived fault
        // ("_error") and dead-letter ("_skipped") queues — confirmed via reflection against
        // MassTransit.RabbitMqTransport 9.1.2 to be the same settings RabbitMqReceiveEndpointBuilder
        // uses to build the real fault transport a faulted/retry-exhausted message is routed to.
        // There is no public hook here (or anywhere else in the RabbitMQ transport's configuration
        // surface) to rename those queues — DeadLetterOptions.QueueNameSuffix is therefore accepted
        // but has no observable effect in this MassTransit version; see its own XML doc for the full
        // explanation. Only MessageTimeToLive is wired below.
        if (!opts.MessageTimeToLive.HasValue)
            return;

        var timeToLive = opts.MessageTimeToLive.Value;

        cfg.SendTopology.ConfigureErrorSettings = queue => queue.SetQueueArgument("x-message-ttl", timeToLive);
        cfg.SendTopology.ConfigureDeadLetterSettings = queue => queue.SetQueueArgument("x-message-ttl", timeToLive);
    }

    // Internal (not private) so PayloadTransformConfigurationTests can exercise this helper
    // in isolation via a substituted IBusFactoryConfigurator (P-346/WO-054).
    internal static void ConfigurePayloadTransform(
        IBusFactoryConfigurator busCfg,
        IBusRegistrationContext ctx,
        PayloadTransformOptions options)
    {
        // Resolved from the real, fully-built IServiceProvider IBusRegistrationContext wraps —
        // both are already-validated singletons per the Build()-time guard above, never a second,
        // independently constructed IServiceProvider (mirrors the ID-03 idempotency-filter pattern
        // of resolving dependencies via `ctx` inside the transport-specific configuration callback).
        IPayloadCompressor? compressor = options.EnableCompression
            ? ctx.GetRequiredService<IPayloadCompressor>()
            : null;

        ISynchronousSymmetricEncryptionService? encryptionService = options.EnableEncryption
            ? ResolveSynchronousEncryptionService(ctx)
            : null;

        // MassTransit's own default JSON (de)serializer, freshly constructed with default options —
        // matches exactly what the bus would otherwise use unconfigured, so wrapping it introduces
        // no independent behavior change beyond the compress/encrypt transform itself.
        // On the 8.5.x line this factory is parameterless; the 9.x `configure: null` argument this
        // replaced meant "apply no configuration callback", i.e. the same default options (P-560).
        var innerFactory = new MtSystemTextJsonMessageSerializerFactory();

        // ClearSerialization() is required, not optional: AddSerializer(factory, isSerializer: true)
        // alone only changes which serializer PRODUCES outgoing messages — MassTransit's own
        // already-registered default deserializer for the same content type remains active on the
        // RECEIVE side, since serializer/deserializer registration is additive by content type, not
        // overwrite-by-content-type. Without this call, an incoming compressed/encrypted message body
        // is handed to the untouched default deserializer, which fails trying to parse ciphertext as
        // JSON (confirmed empirically — the wrong deserializer instance appeared in the stack trace of
        // a MassTransit-internal SerializationException during implementation). Clearing first and
        // re-registering makes this factory the ONLY serializer/deserializer for the bus.
        var factory = new PayloadTransformSerializerFactory(innerFactory, options, compressor, encryptionService);

        busCfg.ClearSerialization();
        busCfg.AddSerializer(factory, isSerializer: true);
        busCfg.AddDeserializer(factory, isDefault: true);
    }

    private const string MissingSynchronousEncryptionGuidance =
        "Payload-transform encryption runs inside MassTransit's synchronous serializers, so it needs keys held in memory: " +
        "register an ISynchronousEncryptionKeyProvider (for example StaticEncryptionKeyProvider) and call " +
        "services.AddSharedKernelCryptography(configuration).AddSynchronousSymmetricEncryption(). " +
        "KMS/HSM-backed providers such as AzureKeyVaultEncryptionKeyProvider cannot be used here.";

    private static ISynchronousSymmetricEncryptionService ResolveSynchronousEncryptionService(IServiceProvider services)
    {
        object? service;
        try
        {
            service = services.GetService(typeof(ISynchronousSymmetricEncryptionService));
        }
        catch (InvalidOperationException ex)
        {
            // The container found the service but not its dependency, typically ISynchronousEncryptionKeyProvider.
            throw new InvalidOperationException(
                "PayloadTransformOptions.EnableEncryption is set but ISynchronousSymmetricEncryptionService could not be resolved. " +
                MissingSynchronousEncryptionGuidance,
                ex);
        }

        return service as ISynchronousSymmetricEncryptionService
            ?? throw new InvalidOperationException(
                "PayloadTransformOptions.EnableEncryption is set but no ISynchronousSymmetricEncryptionService is registered. " +
                MissingSynchronousEncryptionGuidance);
    }

    private static void ValidateAzureServiceBusOptions(AzureServiceBusOptions opts)
    {
        var hasConnectionString = !string.IsNullOrWhiteSpace(opts.ConnectionString);
        var hasNamespace = !string.IsNullOrWhiteSpace(opts.FullyQualifiedNamespace);

        if (hasConnectionString && hasNamespace)
            throw new InvalidOperationException(
                "AzureServiceBusOptions: ConnectionString and FullyQualifiedNamespace are mutually exclusive. Set exactly one.");

        if (!hasConnectionString && !hasNamespace)
            throw new InvalidOperationException(
                "AzureServiceBusOptions: Either ConnectionString or FullyQualifiedNamespace must be set.");
    }
}
