using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Application.Pipeline;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;

namespace SharedKernel.Application;

/// <summary>
/// Registers the application layer of a service: MediatR, its handlers and validators, the
/// pipeline behaviors and the domain-event bridge, in one call.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the application layer with the always-on behaviors only (tracing, logging, metrics,
    /// validation).
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="assemblies">
    /// The assemblies holding the service's handlers and FluentValidation validators; at least one.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/>, for chaining.</returns>
    /// <inheritdoc cref="AddSharedKernelApplication(IServiceCollection, Assembly[], Action{ApplicationPipelineBuilder})" path="/exception"/>
    public static IServiceCollection AddSharedKernelApplication(
        this IServiceCollection services,
        params Assembly[] assemblies)
        => services.AddSharedKernelApplication(assemblies, static _ => { });

    /// <summary>
    /// Registers the application layer, with the opt-in behaviors chosen by
    /// <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="assembly">The assembly holding the service's handlers and FluentValidation validators.</param>
    /// <param name="configure">Chooses the opt-in behaviors: <c>app => app.WithAuthorization().WithTransactions()</c>.</param>
    /// <returns>The same <see cref="IServiceCollection"/>, for chaining.</returns>
    /// <inheritdoc cref="AddSharedKernelApplication(IServiceCollection, Assembly[], Action{ApplicationPipelineBuilder})" path="/exception"/>
    public static IServiceCollection AddSharedKernelApplication(
        this IServiceCollection services,
        Assembly assembly,
        Action<ApplicationPipelineBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return services.AddSharedKernelApplication([assembly], configure);
    }

    /// <summary>
    /// Registers the application layer, with the opt-in behaviors chosen by
    /// <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="assemblies">
    /// The assemblies holding the service's handlers and FluentValidation validators; at least one.
    /// </param>
    /// <param name="configure">Chooses the opt-in behaviors: <c>app => app.WithAuthorization().WithTransactions()</c>.</param>
    /// <returns>The same <see cref="IServiceCollection"/>, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="assemblies"/> is empty or contains <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>AddSharedKernelApplication</c> was already called on <paramref name="services"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Always registers: MediatR with the handlers of <paramref name="assemblies"/>; their
    /// FluentValidation validators (scoped, public and internal); the domain-event bridge
    /// (<see cref="IDomainEventDispatcher"/> over MediatR); <see cref="ICommandScope"/>; and the
    /// tracing, logging, metrics and validation behaviors. Everything else is opted into on the
    /// builder, and lands in the canonical order whatever order the calls are made in.
    /// </para>
    /// <para>
    /// A service an opted-in behavior needs — <c>IRequestContext</c>, <c>IUnitOfWork</c>, … — is
    /// checked when the host starts, not here, so it may be registered before or after this call. A
    /// missing one fails the start (<see cref="Microsoft.Extensions.Options.OptionsValidationException"/>)
    /// with one message naming every missing service.
    /// </para>
    /// <para>
    /// Call it once: a second call throws, because it would register every behavior twice. Pass
    /// every assembly to the one call instead.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelApplication(
        this IServiceCollection services,
        Assembly[] assemblies,
        Action<ApplicationPipelineBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(configure);

        if (assemblies.Length == 0)
        {
            throw new ArgumentException(
                "AddSharedKernelApplication needs at least one assembly that holds handlers, for " +
                "example typeof(Program).Assembly.",
                nameof(assemblies));
        }

        if (Array.Exists(assemblies, assembly => assembly is null))
            throw new ArgumentException("The assemblies must not contain null.", nameof(assemblies));

        if (services.Any(descriptor => descriptor.ServiceType == typeof(PipelineRequirements)))
        {
            throw new InvalidOperationException(
                "AddSharedKernelApplication() has already been called on this service collection. A " +
                "second call would register every pipeline behavior twice. Call it once, passing every " +
                "assembly that holds handlers or validators and every With… option to that one call.");
        }

        var builder = new ApplicationPipelineBuilder(services);
        configure(builder);

        var distinct = assemblies.Distinct().ToArray();

        services.AddLogging();
        services.AddMetrics();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblies(distinct));

        foreach (var validator in AssemblyScanner.FindValidatorsInAssemblies(distinct, includeInternalTypes: true))
            services.TryAddEnumerable(ServiceDescriptor.Scoped(validator.InterfaceType, validator.ValidatorType));

        services.AddScoped<IDomainEventDispatcher, MediatRDomainEventDispatcher>();

        builder.Register();
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
    /// <see cref="IDomainEventHandler{TDomainEvent}"/> (scoped) and the internal adapter as
    /// <see cref="INotificationHandler{TNotification}"/> for
    /// <see cref="DomainEventNotification{TDomainEvent}"/> (scoped) so MediatR's
    /// <see cref="IPublisher"/> can resolve it. One call per domain event type; may be made before or
    /// after <c>AddSharedKernelApplication</c>. No assembly scanning, no
    /// <see cref="Type.MakeGenericType"/> — both type arguments are ordinary closed generics.
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
