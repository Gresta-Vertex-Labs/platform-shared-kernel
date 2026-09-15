using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;

namespace SharedKernel.Application.Extensions;

/// <summary>
/// DI registration extensions for <c>SharedKernel.Application</c>.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the MediatR-based domain-event-to-notification bridge with default serial dispatch.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, for chaining.</returns>
    /// <remarks>
    /// Registers <see cref="IDomainEventDispatcher"/> → <see cref="MediatRDomainEventDispatcher"/>
    /// (scoped) with <see cref="MediatRDomainEventDispatcherOptions.ParallelDispatch"/> set to
    /// <see langword="false"/> (serial dispatch, the safe default). Does <b>not</b> call
    /// <c>services.AddMediatR(...)</c> — the consuming service owns MediatR registration and
    /// assembly scanning (<c>RegisterServicesFromAssembly</c>). This extension only adds the
    /// dispatcher bridge.
    /// </remarks>
    public static IServiceCollection AddSharedKernelApplication(this IServiceCollection services)
        => services.AddSharedKernelApplication(null);

    /// <summary>
    /// Registers the MediatR-based domain-event-to-notification bridge with optional parallel
    /// dispatch configuration.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="configure">
    /// An optional delegate to configure <see cref="MediatRDomainEventDispatcherOptions"/>.
    /// Pass <see langword="null"/> or omit to retain the serial default (equivalent to the
    /// parameterless overload — never breaks existing call sites).
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, for chaining.</returns>
    /// <remarks>
    /// Example — opt in to parallel dispatch:
    /// <code>
    /// services.AddSharedKernelApplication(opts => opts.ParallelDispatch = true);
    /// </code>
    /// Does <b>not</b> call <c>services.AddMediatR(...)</c> — the consuming service owns MediatR
    /// registration and assembly scanning (<c>RegisterServicesFromAssembly</c>).
    /// </remarks>
    public static IServiceCollection AddSharedKernelApplication(
        this IServiceCollection services,
        Action<MediatRDomainEventDispatcherOptions>? configure)
    {
        services.AddOptions<MediatRDomainEventDispatcherOptions>();
        if (configure is not null)
            services.Configure(configure);

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
