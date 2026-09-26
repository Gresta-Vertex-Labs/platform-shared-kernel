using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.MessageBus;

namespace SharedKernel.Testing.Messaging;

/// <summary>
/// DI convenience extensions registering the in-memory messaging test doubles.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="InMemoryMessageBus"/> as the <see cref="IMessageBus"/> singleton.
    /// </summary>
    /// <remarks>
    /// <strong>Lifetime deviation:</strong> production <see cref="IMessageBus"/> registrations are
    /// scoped. This double is registered as a singleton so tests can resolve the same recorder
    /// instance that outlives the DI scope used by the system under test, allowing assertions to
    /// run after the action under test completes. Do not mistake this for the production DI shape.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddInMemoryMessageBus(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<InMemoryMessageBus>();
        services.AddSingleton<IMessageBus>(sp => sp.GetRequiredService<InMemoryMessageBus>());
        return services;
    }

    /// <summary>
    /// Registers <see cref="InMemoryEventPublisher"/> as the <see cref="IEventPublisher"/> singleton.
    /// </summary>
    /// <remarks>
    /// <strong>Lifetime deviation:</strong> production <see cref="IEventPublisher"/> registrations
    /// are scoped. This double is registered as a singleton for the same reason documented on
    /// <see cref="AddInMemoryMessageBus"/>.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddInMemoryEventPublisher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<InMemoryEventPublisher>();
        services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<InMemoryEventPublisher>());
        return services;
    }
}
