using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Domain;
using SharedKernel.Domain.Events;

namespace SharedKernel.Application.Extensions;

/// <summary>
/// DI registration extensions for <c>SharedKernel.Application</c>.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the MediatR-based domain-event-to-notification bridge.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, for chaining.</returns>
    /// <remarks>
    /// Registers <see cref="IDomainEventDispatcher"/> → <see cref="MediatRDomainEventDispatcher"/>
    /// (scoped). Does <b>not</b> call <c>services.AddMediatR(...)</c> — the consuming service owns
    /// MediatR registration and assembly scanning (<c>RegisterServicesFromAssembly</c>). This
    /// extension only adds the dispatcher bridge.
    /// </remarks>
    public static IServiceCollection AddSharedKernelApplication(this IServiceCollection services)
    {
        services.AddScoped<IDomainEventDispatcher, MediatRDomainEventDispatcher>();
        return services;
    }

    /// <summary>
    /// Registers a domain event handler and the internal MediatR notification adapter that
    /// forwards to it.
    /// </summary>
    /// <typeparam name="TDomainEvent">The concrete domain event type.</typeparam>
    /// <typeparam name="THandler">
    /// The concrete handler type implementing <see cref="IDomainEventHandler{TDomainEvent}"/>.
    /// </typeparam>
    /// <param name="services">The service collection to register against.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, for chaining.</returns>
    /// <remarks>
    /// Registers <typeparamref name="THandler"/> as
    /// <see cref="IDomainEventHandler{TDomainEvent}"/> (scoped) and the internal
    /// <see cref="DomainEventNotificationHandler{TDomainEvent}"/> as
    /// <see cref="INotificationHandler{TNotification}"/> for
    /// <see cref="DomainEventNotification{TDomainEvent}"/> (scoped) so MediatR's
    /// <see cref="IPublisher"/> can resolve it. One call per domain event type. No assembly
    /// scanning, no <see cref="System.Type.MakeGenericType"/> at registration time — both type
    /// arguments are supplied by the caller as ordinary closed generics.
    /// </remarks>
    public static IServiceCollection AddDomainEventHandler<TDomainEvent, THandler>(this IServiceCollection services)
        where TDomainEvent : IDomainEvent
        where THandler : class, IDomainEventHandler<TDomainEvent>
    {
        services.AddScoped<IDomainEventHandler<TDomainEvent>, THandler>();
        services.AddScoped<
            INotificationHandler<DomainEventNotification<TDomainEvent>>,
            DomainEventNotificationHandler<TDomainEvent>>();
        return services;
    }
}
