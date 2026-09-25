using MassTransit;
using SharedKernel.Messaging.MassTransit.Options;
using SharedKernel.Messaging.MassTransit.RabbitMq.Transport;

namespace SharedKernel.Messaging.MassTransit.Extensions;

/// <summary>
/// Runs the messaging bus on RabbitMQ.
/// </summary>
/// <remarks>
/// Declared in the builder's own namespace, so <c>AddSharedKernelMessaging(...).UseRabbitMq(...)</c> needs no
/// extra <c>using</c>. The transport lives in its own package so that a service on another broker does not
/// reference the RabbitMQ client (P-570).
/// </remarks>
public static class RabbitMqMessagingBusBuilderExtensions
{
    /// <summary>
    /// Configures the RabbitMQ transport using a simple AMQP connection string.
    /// </summary>
    /// <param name="builder">The messaging bus builder.</param>
    /// <param name="connectionString">
    /// AMQP connection string, e.g. <c>"rabbitmq://localhost"</c> or
    /// <c>"amqps://user:pass@rabbitmq.svc.cluster.local/vhost"</c>.
    /// </param>
    /// <returns>The builder for fluent chaining.</returns>
    public static MessagingBusBuilder UseRabbitMq(this MessagingBusBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return builder.UseTransport(new RabbitMqMessagingTransport(cfg => cfg.Host(new Uri(connectionString))));
    }

    /// <summary>
    /// Configures the RabbitMQ transport from an explicit options action.
    /// </summary>
    /// <param name="builder">The messaging bus builder.</param>
    /// <param name="configure">Action to configure <see cref="RabbitMqBusOptions"/>.</param>
    /// <returns>The builder for fluent chaining.</returns>
    public static MessagingBusBuilder UseRabbitMq(this MessagingBusBuilder builder, Action<RabbitMqBusOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var opts = new RabbitMqBusOptions();
        configure(opts);

        return builder.UseTransport(
            new RabbitMqMessagingTransport(cfg => RabbitMqMessagingTransport.ConfigureRabbitMq(cfg, opts)));
    }
}
