namespace SharedKernel.Messaging.Abstractions.MessageBus;

/// <summary>
/// Resolves the queue name for a given command message type.
/// </summary>
/// <remarks>
/// <para>
/// The default implementation (<c>ConventionSendEndpointResolver</c> in the MassTransit package)
/// derives the queue name from <c>MessagingOptions.ServiceName</c> as the prefix and the
/// kebab-case type name as the suffix (e.g., <c>"payment-service-process-payment"</c>).
/// </para>
/// <para>
/// Per-type overrides are registered via
/// <c>MessagingBusBuilder.WithSendEndpointRoute&lt;T&gt;(queueName)</c>.
/// </para>
/// <para>
/// <strong>Internal use only.</strong> This interface is internal to the MassTransit package
/// and injected into <c>MassTransitMessageBus</c>. Application code never calls
/// <see cref="Resolve{T}"/> directly — use <c>IMessageBus.SendAsync&lt;T&gt;</c> instead.
/// </para>
/// </remarks>
public interface ISendEndpointResolver
{
    /// <summary>
    /// Returns the queue name for message type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The command message type to resolve an endpoint for.</typeparam>
    /// <returns>
    /// The queue name string (e.g., <c>"payment-service-process-payment"</c>).
    /// </returns>
    string Resolve<T>() where T : class;
}
