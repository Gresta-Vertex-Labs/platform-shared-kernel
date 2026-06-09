using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Messaging.Abstractions.Extensions;

/// <summary>
/// Builder contract returned by <c>AddSharedKernelMessaging()</c>.
/// Allows transport-specific and feature extensions to chain off the core registration.
/// </summary>
/// <remarks>
/// Consuming services should not reference <see cref="IMessagingBuilder"/> directly —
/// use the concrete <c>MessagingBusBuilder</c> from <c>SharedKernel.Messaging.MassTransit</c>
/// to access all fluent configuration methods.
/// </remarks>
public interface IMessagingBuilder
{
    /// <summary>
    /// Gets the underlying <see cref="IServiceCollection"/> for direct service registration
    /// when the fluent API is insufficient.
    /// </summary>
    IServiceCollection Services { get; }
}
