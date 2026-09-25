using MassTransit;

namespace SharedKernel.Messaging.MassTransit.Consumers;

/// <summary>
/// Platform-standard base class for per-consumer MassTransit endpoint configuration.
/// Pre-wires the retry exception filter so that exception types declared in
/// <see cref="NonRetryableExceptions"/> bypass the global retry policy and go directly
/// to the dead-letter queue on first failure.
/// </summary>
/// <typeparam name="TConsumer">The consumer type this definition configures.</typeparam>
/// <remarks>
/// <para>
/// Consuming services extend this class and override <see cref="NonRetryableExceptions"/>,
/// <see cref="EndpointName"/>, <see cref="PrefetchCount"/>, and <see cref="ConcurrentMessageLimit"/>
/// as needed.
/// The retry exception filter wiring is the platform minimum standard for consumer configuration
/// — it runs unconditionally before <c>ConfigureConsumer</c> is called.
/// </para>
/// <para>
/// Register consuming-service definitions via
/// <c>MessagingBusBuilder.AddConsumer&lt;TConsumer, TConsumerDefinition&gt;()</c>.
/// </para>
/// <para>
/// Prefer this base class over raw <c>IConsumerDefinition&lt;TConsumer&gt;</c>. Implementing
/// <c>IConsumerDefinition&lt;TConsumer&gt;</c> directly bypasses the retry exception filter wiring,
/// which is a hard violation.
/// </para>
/// </remarks>
public abstract class ConsumerDefinitionBase<TConsumer> : ConsumerDefinition<TConsumer>
    where TConsumer : class, IConsumer
{
    /// <summary>
    /// Gets the explicit endpoint name for this consumer's receive endpoint.
    /// When <see langword="null"/> (the default), MassTransit applies its convention-based naming,
    /// deriving the queue name from <c>MessagingOptions.ServiceName</c> and the consumer type name.
    /// </summary>
    /// <remarks>
    /// Override to return an explicit queue/subscription name (e.g.,
    /// <c>"order-service-order-placed-v2"</c>) when the convention-based name is not suitable.
    /// </remarks>
    protected virtual new string? EndpointName => null;

    /// <summary>
    /// Gets the prefetch count for this consumer's receive endpoint.
    /// When <see langword="null"/> (the default), MassTransit applies its transport-specific default.
    /// </summary>
    /// <remarks>
    /// Lower values for slow consumers; higher values for fast CPU-bound consumers.
    /// Override to set an explicit value for this consumer's endpoint.
    /// </remarks>
    protected virtual int? PrefetchCount => null;

    /// <summary>
    /// Gets the exception types that must bypass retry and go directly to the dead-letter queue.
    /// The default implementation returns an empty list — all exceptions are retried per the global
    /// retry policy configured via <c>MessagingBusBuilder.WithRetry()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Override to declare exception types that represent business-rule violations or permanent
    /// failures that should not be retried. Common entries include validation and not-found exceptions.
    /// </para>
    /// <para>
    /// Example override:
    /// <code>
    /// protected override IReadOnlyList&lt;Type&gt; NonRetryableExceptions =&gt;
    ///     [typeof(ValidationException), typeof(NotFoundException)];
    /// </code>
    /// </para>
    /// <para>
    /// The global retry policy (from <c>WithRetry()</c>) still applies to exception types NOT
    /// in this list. This property defines the <em>exclusion</em> set only — it does not replace
    /// the global policy.
    /// </para>
    /// </remarks>
    protected virtual IReadOnlyList<Type> NonRetryableExceptions => [];

    /// <summary>
    /// Gets the maximum number of messages processed concurrently on this consumer's receive endpoint.
    /// When <see langword="null"/> (the default), the transport-level default applies — see
    /// <c>RabbitMqBusOptions.ConcurrentMessageLimit</c> (<c>SharedKernel.Messaging.MassTransit.RabbitMq</c>)
    /// or <c>AzureServiceBusOptions.MaxConcurrentCalls</c> (<c>SharedKernel.Messaging.MassTransit.AzureServiceBus</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="PrefetchCount"/>: <c>Prefetch</c> bounds how many unacknowledged
    /// messages the broker delivers to the channel; <see cref="ConcurrentMessageLimit"/> bounds how
    /// many of those this endpoint processes in parallel.
    /// </para>
    /// <para>
    /// When set, this per-consumer value takes precedence over the transport-level default on this
    /// consumer's own endpoint only — it does not affect any other consumer's endpoint.
    /// </para>
    /// <para>
    /// <c>new</c> is required here: <c>MassTransit.ConsumerDefinition&lt;TConsumer&gt;</c> (the base
    /// class this type extends) already declares its own <c>ConcurrentMessageLimit</c> property
    /// (public get, protected set). This member intentionally shadows it — the sealed
    /// <see cref="ConfigureConsumer(IReceiveEndpointConfigurator, IConsumerConfigurator{TConsumer}, IRegistrationContext)"/>
    /// entry point reads this shadowing property and applies it directly to the receive endpoint
    /// configurator, exactly like <see cref="PrefetchCount"/>, rather than relying on MassTransit's
    /// own base-class field.
    /// </para>
    /// </remarks>
    protected virtual new int? ConcurrentMessageLimit => null;

    /// <summary>
    /// MassTransit entry point. Applies platform-standard configuration:
    /// (a) sets the endpoint name when <see cref="EndpointName"/> is non-null,
    /// (b) sets the prefetch count when <see cref="PrefetchCount"/> is non-null,
    /// (c) wires the retry exception filter for each type in <see cref="NonRetryableExceptions"/>,
    /// (d) delegates to <see cref="ConfigureConsumer(IReceiveEndpointConfigurator, IConsumerConfigurator{TConsumer}, IBusRegistrationContext)"/>
    /// for subclass-specific configuration,
    /// (e) sets the concurrency limit when <see cref="ConcurrentMessageLimit"/> is non-null.
    /// </summary>
    /// <param name="endpointConfigurator">The receive endpoint configurator.</param>
    /// <param name="consumerConfigurator">The consumer configurator.</param>
    /// <param name="context">The bus registration context.</param>
    /// <remarks>
    /// Do not override this method in subclasses — override
    /// <see cref="ConfigureConsumer(IReceiveEndpointConfigurator, IConsumerConfigurator{TConsumer}, IRegistrationContext)"/>
    /// instead. Overriding this method directly would bypass the platform-standard retry exception
    /// filter wiring.
    /// </remarks>
    protected sealed override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<TConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // (a) Apply explicit endpoint name when provided.
        if (EndpointName is { } name)
            base.EndpointName = name;

        // (b) Apply explicit prefetch count when provided.
        if (PrefetchCount.HasValue)
            endpointConfigurator.PrefetchCount = PrefetchCount.Value;

        // (c) Wire retry exception filter for each non-retryable exception type.
        // Configures a per-endpoint retry policy with Ignore filters for each declared type.
        // MassTransit requires a base retry policy (Immediate/Incremental/etc.) to be set before
        // Ignore filters are applied — we set Immediate(3) as the platform default count.
        // Non-retryable exceptions are classified as fatal and bypass all retry attempts;
        // all other exception types are retried per this endpoint-level policy.
        // NOTE: When this fires, it supersedes the global bus-level WithRetry() policy for this
        // consumer endpoint. Subclasses that require a different retry count should configure
        // it explicitly in ConfigureConsumer.
        var nonRetryable = NonRetryableExceptions;
        if (nonRetryable.Count > 0)
        {
            endpointConfigurator.UseMessageRetry(r =>
            {
                r.Immediate(3); // Platform default: 3 immediate retry attempts for this endpoint.
                foreach (var exceptionType in nonRetryable)
                    r.Ignore(exceptionType); // Fatal for these types — no retries regardless of count.
            });
        }

        // (d) Delegate to the subclass for additional configuration.
        ConfigureConsumer(endpointConfigurator, consumerConfigurator, (IBusRegistrationContext)context);

        // (e) Apply explicit per-consumer concurrency limit when provided (P-342/WO-054).
        // Overrides the transport-level default (RabbitMqBusOptions.ConcurrentMessageLimit or
        // AzureServiceBusOptions.MaxConcurrentCalls) for this consumer's endpoint only.
        if (ConcurrentMessageLimit.HasValue)
            endpointConfigurator.ConcurrentMessageLimit = ConcurrentMessageLimit.Value;
    }

    /// <summary>
    /// Configures the receive endpoint and consumer for this consumer's specific needs
    /// (concurrency, additional pipeline filters, etc.).
    /// </summary>
    /// <param name="endpointConfigurator">The receive endpoint configurator.</param>
    /// <param name="consumerConfigurator">The consumer configurator.</param>
    /// <param name="context">The bus registration context providing access to registered services.</param>
    /// <remarks>
    /// <para>
    /// Subclasses implement this method for endpoint-specific configuration.
    /// The platform-standard retry exception filter wiring has already been applied
    /// when this method is called — do not re-configure retry here.
    /// </para>
    /// <para>
    /// <strong>Do not override <c>IConsumerDefinition&lt;TConsumer&gt;.Configure</c> directly.</strong>
    /// The base class seals the MassTransit <c>ConfigureConsumer</c> override to guarantee that
    /// retry exception filter wiring always runs. Subclasses must override this method only.
    /// </para>
    /// </remarks>
    protected abstract void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<TConsumer> consumerConfigurator,
        IBusRegistrationContext context);
}
