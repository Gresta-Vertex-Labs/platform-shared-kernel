using Azure.Messaging.ServiceBus;

namespace SharedKernel.Messaging.MassTransit.Options;

/// <summary>
/// Configuration options for the Azure Service Bus transport.
/// Bound from the <c>"SharedKernel:Messaging:AzureServiceBus"</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// Exactly one of <see cref="ConnectionString"/> or <see cref="FullyQualifiedNamespace"/> must be set.
/// Startup validation throws <see cref="InvalidOperationException"/> if both or neither are set.
/// </para>
/// <para>
/// <see cref="FullyQualifiedNamespace"/> with <c>DefaultAzureCredential</c> (managed identity)
/// is strongly preferred in Kubernetes workloads. <see cref="ConnectionString"/> is for local dev
/// and CI only — never commit it to source control.
/// </para>
/// </remarks>
public sealed class AzureServiceBusOptions
{
    /// <summary>The configuration section key for <see cref="AzureServiceBusOptions"/>.</summary>
    public const string SectionName = "SharedKernel:Messaging:AzureServiceBus";

    /// <summary>
    /// Gets or sets the Azure Service Bus connection string.
    /// For local development and CI only. Mutually exclusive with <see cref="FullyQualifiedNamespace"/>.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the fully qualified Service Bus namespace hostname.
    /// Example: <c>"my-namespace.servicebus.windows.net"</c>.
    /// Used with <c>DefaultAzureCredential</c> (managed identity) in Kubernetes workloads.
    /// Mutually exclusive with <see cref="ConnectionString"/>.
    /// </summary>
    public string? FullyQualifiedNamespace { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of concurrent message-processing calls per consumer.
    /// Default is <c>1</c>.
    /// </summary>
    /// <remarks>
    /// Applied to the bus-level receive endpoint default by
    /// <see cref="SharedKernel.Messaging.MassTransit.Extensions.MessagingBusBuilder"/>'s internal
    /// Azure Service Bus configuration helper (P-342/WO-054, fixing a confirmed prior defect where
    /// this option was read into <see cref="AzureServiceBusOptions"/> but never consulted anywhere
    /// the bus was actually built — setting it previously had zero observable effect).
    /// </remarks>
    public int MaxConcurrentCalls { get; set; } = 1;

    /// <summary>
    /// Gets or sets the Service Bus transport type.
    /// Default is <see cref="ServiceBusTransportType.AmqpTcp"/>.
    /// Use <see cref="ServiceBusTransportType.AmqpWebSockets"/> when AMQP port 5671 is blocked.
    /// </summary>
    public ServiceBusTransportType TransportType { get; set; } = ServiceBusTransportType.AmqpTcp;
}
