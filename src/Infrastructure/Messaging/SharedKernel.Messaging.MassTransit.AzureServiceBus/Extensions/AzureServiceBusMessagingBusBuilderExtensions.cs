using SharedKernel.Messaging.MassTransit.AzureServiceBus.Transport;
using SharedKernel.Messaging.MassTransit.Options;

namespace SharedKernel.Messaging.MassTransit.Extensions;

/// <summary>
/// Runs the messaging bus on Azure Service Bus.
/// </summary>
/// <remarks>
/// Declared in the builder's own namespace, so <c>AddSharedKernelMessaging(...).UseAzureServiceBus(...)</c> needs
/// no extra <c>using</c>. The transport lives in its own package so that a service on another broker does not
/// reference the Azure SDK (P-570).
/// </remarks>
public static class AzureServiceBusMessagingBusBuilderExtensions
{
    /// <summary>
    /// Configures the Azure Service Bus transport using a connection string.
    /// For local development and CI only — use managed identity in production.
    /// </summary>
    /// <param name="builder">The messaging bus builder.</param>
    /// <param name="connectionString">The Azure Service Bus connection string.</param>
    /// <returns>The builder for fluent chaining.</returns>
    public static MessagingBusBuilder UseAzureServiceBus(this MessagingBusBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return builder.UseTransport(
            new AzureServiceBusMessagingTransport(new AzureServiceBusOptions { ConnectionString = connectionString }));
    }

    /// <summary>
    /// Configures the Azure Service Bus transport from an explicit options action.
    /// Use <see cref="AzureServiceBusOptions.FullyQualifiedNamespace"/> with managed identity
    /// (<c>DefaultAzureCredential</c>) in Kubernetes workloads.
    /// </summary>
    /// <param name="builder">The messaging bus builder.</param>
    /// <param name="configure">Action to configure <see cref="AzureServiceBusOptions"/>.</param>
    /// <returns>The builder for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Both or neither of <see cref="AzureServiceBusOptions.ConnectionString"/> and
    /// <see cref="AzureServiceBusOptions.FullyQualifiedNamespace"/> are set.
    /// </exception>
    public static MessagingBusBuilder UseAzureServiceBus(
        this MessagingBusBuilder builder,
        Action<AzureServiceBusOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var opts = new AzureServiceBusOptions();
        configure(opts);

        AzureServiceBusMessagingTransport.Validate(opts);
        return builder.UseTransport(new AzureServiceBusMessagingTransport(opts));
    }
}
