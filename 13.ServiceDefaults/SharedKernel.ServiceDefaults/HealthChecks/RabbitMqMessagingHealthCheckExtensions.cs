using HealthChecks.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Opt-in RabbitMQ connectivity health check, wrapping <c>AspNetCore.HealthChecks.RabbitMQ</c>.
/// </summary>
public static class RabbitMqMessagingHealthCheckExtensions
{
    /// <summary>
    /// Registers a health check that opens a real AMQP connection (not just URI parsing) against
    /// <paramref name="amqpUri"/>.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="amqpUri">
    /// The AMQP connection URI. Source this from the resolved <c>RabbitMqBusOptions.Host</c>
    /// (<c>07.Messaging.MassTransit</c>) rather than duplicating a connection string at the call
    /// site.
    /// </param>
    /// <param name="name">The health check registration name. Defaults to <c>"rabbitmq"</c>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// Tagged <see cref="HealthCheckTags.Ready"/> and <see cref="HealthCheckTags.Messaging"/>.
    /// Opt-in only — never registered by <c>AddServiceDefaults()</c> or
    /// <c>AddSharedKernelHealthChecks()</c>.
    /// </remarks>
    public static IHealthChecksBuilder AddRabbitMqMessagingHealthCheck(
        this IHealthChecksBuilder builder,
        string amqpUri,
        string name = "rabbitmq")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(amqpUri);

        var connectionFactory = new ConnectionFactory { Uri = new Uri(amqpUri) };

        return builder.AddRabbitMQ(
            _ => connectionFactory.CreateConnectionAsync(),
            name: name,
            tags: [HealthCheckTags.Ready, HealthCheckTags.Messaging]);
    }
}
