using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Observability;

namespace SharedKernel.Testing.Integration;

/// <summary>
/// DI convenience extensions registering the in-memory <c>15.Integration</c> test doubles.
/// </summary>
public static class IntegrationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="InMemoryWebhookDispatcher"/> as the <see cref="IWebhookDispatcher"/> singleton.
    /// </summary>
    /// <remarks>
    /// <strong>Lifetime deviation:</strong> production <see cref="IWebhookDispatcher"/> registrations
    /// (<c>AddSharedKernelWebhooks</c>, <c>15.Integration</c>) are scoped. This double is registered
    /// as a singleton so tests can resolve the same recorder instance that outlives the DI scope used
    /// by the system under test, allowing assertions to run after the action under test completes —
    /// mirroring <c>AddInMemoryMessageBus()</c>/<c>AddInMemoryEventPublisher()</c>'s exact precedent
    /// and rationale. Do not mistake this for the production DI shape.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddInMemoryWebhookDispatcher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<InMemoryWebhookDispatcher>();
        services.AddSingleton<IWebhookDispatcher>(sp => sp.GetRequiredService<InMemoryWebhookDispatcher>());
        return services;
    }

    /// <summary>
    /// Registers <see cref="InMemoryWebhookDeliveryObserver"/> as the <see cref="IWebhookDeliveryObserver"/> singleton.
    /// </summary>
    /// <remarks>
    /// <strong>Lifetime deviation:</strong> production <see cref="IWebhookDeliveryObserver"/>
    /// registrations (<c>WithDeliveryObserver&lt;TObserver&gt;</c>, <c>15.Integration</c>) are
    /// scoped. This double is registered as a singleton for the same reason documented on
    /// <see cref="AddInMemoryWebhookDispatcher"/>.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddInMemoryWebhookDeliveryObserver(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<InMemoryWebhookDeliveryObserver>();
        services.AddSingleton<IWebhookDeliveryObserver>(sp => sp.GetRequiredService<InMemoryWebhookDeliveryObserver>());
        return services;
    }
}
