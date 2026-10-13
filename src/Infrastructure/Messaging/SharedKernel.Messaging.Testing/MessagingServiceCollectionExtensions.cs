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
    /// Registers <see cref="InMemoryMessageBus"/> as the scoped <see cref="IMessageBus"/>.
    /// </summary>
    /// <remarks>
    /// The interface is scoped, like the production registration. The recorder behind it is one singleton, so tests
    /// can resolve the same instance that outlives the DI scope used by the system under test and assert after the
    /// action completes.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddInMemoryMessageBus(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<InMemoryMessageBus>();
        services.AddScoped<IMessageBus>(sp => sp.GetRequiredService<InMemoryMessageBus>());
        return services;
    }

    /// <summary>
    /// Registers <see cref="InMemoryEventPublisher"/> as the scoped <see cref="IEventPublisher"/>.
    /// </summary>
    /// <remarks>
    /// The interface is scoped, like the production registration; the recorder behind it is one singleton, for the
    /// reason given on <see cref="AddInMemoryMessageBus"/>.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddInMemoryEventPublisher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<InMemoryEventPublisher>();
        services.AddScoped<IEventPublisher>(sp => sp.GetRequiredService<InMemoryEventPublisher>());
        return services;
    }
}
