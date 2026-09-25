using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Application.Pipeline.DomainEvents;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;

namespace SharedKernel.Application.Pipeline.Extensions;

/// <summary>
/// DI registration for domain-event dispatch.
/// </summary>
public static class DomainEventServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IDomainEventDispatcher"/> as the native <see cref="DomainEventDispatcher"/>
    /// (scoped). Idempotent.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, for chaining.</returns>
    /// <remarks>
    /// <c>AddSharedKernelMediatR(...)</c> calls this and registers every
    /// <see cref="IDomainEventHandler{TDomainEvent}"/> in the scanned assemblies; call it directly only
    /// in a host that dispatches domain events without a mediator.
    /// </remarks>
    public static IServiceCollection AddSharedKernelDomainEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        return services;
    }

    /// <summary>
    /// Registers one domain event handler (scoped).
    /// </summary>
    /// <typeparam name="TDomainEvent">The concrete domain event type.</typeparam>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <param name="services">The service collection to register against.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, for chaining.</returns>
    /// <remarks>
    /// For a handler outside the assemblies passed to <c>AddSharedKernelMediatR(...)</c>. Registering
    /// the same handler type twice for the same event is a no-op, so it never runs twice.
    /// </remarks>
    public static IServiceCollection AddDomainEventHandler<TDomainEvent, THandler>(this IServiceCollection services)
        where TDomainEvent : IDomainEvent
        where THandler : class, IDomainEventHandler<TDomainEvent>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDomainEventHandler<TDomainEvent>, THandler>());
        return services;
    }
}
