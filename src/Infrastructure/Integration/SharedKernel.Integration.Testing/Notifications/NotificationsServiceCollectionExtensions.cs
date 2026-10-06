using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Observability;

namespace SharedKernel.Testing.Notifications;

/// <summary>
/// DI convenience extensions registering the in-memory <c>15.Integration</c> notification test
/// doubles.
/// </summary>
public static class NotificationsServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="InMemoryNotificationSender"/> as the keyed
    /// <see cref="INotificationSender"/> singleton for <paramref name="channel"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Keyed registration, mirroring the real DI shape:</strong> production
    /// <see cref="INotificationSender"/> implementations are registered via
    /// <c>AddKeyedScoped&lt;INotificationSender, TSender&gt;(NotificationChannel.X)</c> and resolved
    /// via <c>IKeyedServiceProvider.GetRequiredKeyedService&lt;INotificationSender&gt;(channel)</c>.
    /// This double keeps that same keyed-by-channel resolution shape so application code under test
    /// needs no special-casing to resolve a fake sender instead of a real one.
    /// </para>
    /// <para>
    /// <strong>Lifetime deviation:</strong> unlike the scoped production registration, this double
    /// is registered as a singleton so tests can resolve the same recorder instance that outlives
    /// the DI scope used by the system under test, allowing assertions to run after the action
    /// under test completes — mirrors <c>AddInMemoryWebhookDispatcher()</c>'s exact precedent and
    /// rationale. Do not mistake this for the production DI shape.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="channel">The channel this sender instance serves.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddInMemoryNotificationSender(this IServiceCollection services, NotificationChannel channel)
    {
        ArgumentNullException.ThrowIfNull(services);

        var sender = new InMemoryNotificationSender(channel);
        services.AddKeyedSingleton(channel, sender);
        services.AddKeyedSingleton<INotificationSender>(channel, sender);

        return services;
    }

    /// <summary>
    /// Registers an <see cref="InMemoryNotificationDeliveryObserver"/> as the
    /// <see cref="INotificationDeliveryObserver"/> singleton.
    /// </summary>
    /// <remarks>
    /// <strong>Lifetime deviation:</strong> production <see cref="INotificationDeliveryObserver"/>
    /// registrations (<c>WithNotificationDeliveryObserver&lt;TObserver&gt;</c>) are scoped and
    /// additive (multiple registrations accumulate). This double is registered as a singleton for
    /// the same reason documented on <see cref="AddInMemoryNotificationSender"/>.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddInMemoryNotificationDeliveryObserver(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<InMemoryNotificationDeliveryObserver>();
        services.AddSingleton<INotificationDeliveryObserver>(sp => sp.GetRequiredService<InMemoryNotificationDeliveryObserver>());

        return services;
    }
}
