using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.DomainEvents;
using SharedKernel.Application.Streaming;
using SharedKernel.Application.Validation;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;

namespace SharedKernel.Application.Pipeline;

/// <summary>
/// Registers the application layer of a service — its handlers, validators and domain-event handlers, the request
/// pipeline and its behaviors — in one call.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the application layer with the always-on behaviors only (tracing, logging, metrics, authorization,
    /// validation) and no mediator.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="assemblies">The assemblies holding the service's handlers and validators; at least one.</param>
    /// <returns>The same <see cref="IServiceCollection"/>, for chaining.</returns>
    /// <remarks>
    /// A service that sends requests also needs a mediator, chosen on the builder — use the overload taking a
    /// configure delegate: <c>AddSharedKernelApplication(typeof(Program).Assembly, app =&gt; app.UseMediatR())</c>.
    /// </remarks>
    /// <inheritdoc cref="AddSharedKernelApplication(IServiceCollection, Assembly[], Action{ApplicationPipelineBuilder})" path="/exception"/>
    public static IServiceCollection AddSharedKernelApplication(
        this IServiceCollection services,
        params Assembly[] assemblies)
        => services.AddSharedKernelApplication(assemblies, static _ => { });

    /// <summary>
    /// Registers the application layer, with the mediator and the opt-in behaviors chosen by
    /// <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="assembly">The assembly holding the service's handlers and validators.</param>
    /// <param name="configure">
    /// Chooses the mediator and the opt-in behaviors: <c>app =&gt; app.UseMediatR().WithTransactions().WithAuditing()</c>.
    /// </param>
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
    /// Registers the application layer, with the mediator and the opt-in behaviors chosen by
    /// <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="assemblies">The assemblies holding the service's handlers and validators; at least one.</param>
    /// <param name="configure">
    /// Chooses the mediator and the opt-in behaviors: <c>app =&gt; app.UseMediatR().WithTransactions().WithAuditing()</c>.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/>, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="assemblies"/> is empty or contains <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>AddSharedKernelApplication</c> was already called on <paramref name="services"/>, or two different handler
    /// types handle the same request type.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Always registers, from <paramref name="assemblies"/> (public and internal types): every
    /// <see cref="IRequestHandler{TRequest,TResponse}"/> (which every <c>ICommandHandler</c>/<c>IQueryHandler</c> is)
    /// and <see cref="IStreamQueryHandler{TQuery,TResponse}"/> (transient), every <see cref="IRequestValidator{TRequest}"/>
    /// and <see cref="IDomainEventHandler{TDomainEvent}"/> (scoped); the native <see cref="IDomainEventDispatcher"/>;
    /// <c>ICommandScope</c>; <see cref="RequestPipeline{TRequest,TResponse}"/> and
    /// <see cref="StreamRequestPipeline{TRequest,TResponse}"/>; and the tracing, logging, metrics, authorization and
    /// validation behaviors. Everything else is opted into on the builder, and lands in the canonical order whatever
    /// order the calls are made in. A handler already registered by hand for a request type is kept.
    /// </para>
    /// <para>
    /// FluentValidation validators are bridged by <c>SharedKernel.Validation.FluentValidation</c>'s
    /// <c>AddFluentValidationRequestValidators(assemblies)</c>; this package references no validator library.
    /// </para>
    /// <para>
    /// Authorization cannot be switched off: every request carrying <c>[RequirePermission]</c>
    /// (<c>SharedKernel.Application.Authorization</c>) is checked against <c>IRequestContext</c>, on every path.
    /// When a request type of <paramref name="assemblies"/> carries the attribute, the host start demands an
    /// <c>IRequestContext</c> (naming the request types); a service that declares no permission needs none.
    /// </para>
    /// <para>
    /// A service a feature needs — <see cref="ISender"/> (the mediator), <c>IRequestContext</c>, <c>IUnitOfWork</c>, … —
    /// is checked when the host starts, not here, so it may be registered before or after this call. A missing one
    /// fails the start (<see cref="Microsoft.Extensions.Options.OptionsValidationException"/>) with one message naming
    /// every missing service. A plain <see cref="ServiceProvider"/> built without a host runs no start check.
    /// </para>
    /// <para>
    /// Call it once: a second call throws, because it would register every behavior twice. Pass every assembly and
    /// every <c>With…</c>/<c>Use…</c> option to that one call instead.
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
                "assembly that holds handlers or validators and every With…/Use… option to that one call.");
        }

        var distinct = assemblies.Distinct().ToArray();
        var builder = new ApplicationPipelineBuilder(services, distinct);
        configure(builder);

        services.AddLogging();
        services.AddMetrics();
        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        var types = ApplicationTypeScan.LoadableTypes(distinct);
        RegisterHandlers(services, types);

        builder.Register(types);
        return services;
    }

    /// <summary>
    /// Registers one domain event handler (scoped), for a handler outside the assemblies passed to
    /// <c>AddSharedKernelApplication</c>.
    /// </summary>
    /// <typeparam name="TDomainEvent">The concrete domain event type.</typeparam>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <param name="services">The service collection to register against.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, for chaining.</returns>
    /// <remarks>
    /// May be called before or after <c>AddSharedKernelApplication</c>. Registering the same handler type twice for
    /// the same event is a no-op, so it never runs twice.
    /// </remarks>
    public static IServiceCollection AddDomainEventHandler<TDomainEvent, THandler>(this IServiceCollection services)
        where TDomainEvent : IDomainEvent
        where THandler : class, IDomainEventHandler<TDomainEvent>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDomainEventHandler<TDomainEvent>, THandler>());
        return services;
    }

    private static void RegisterHandlers(IServiceCollection services, IReadOnlyList<Type> types)
    {
        foreach (var type in types)
        {
            if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
                continue;

            foreach (var contract in type.GetInterfaces())
            {
                if (!contract.IsGenericType)
                    continue;

                var definition = contract.GetGenericTypeDefinition();

                if (definition == typeof(IRequestHandler<,>) || definition == typeof(IStreamQueryHandler<,>))
                    TryAddHandler(services, type, contract);
                else if (definition == typeof(IDomainEventHandler<>) || definition == typeof(IRequestValidator<>))
                    services.TryAddEnumerable(ServiceDescriptor.Scoped(contract, type));
            }
        }
    }

    private static void TryAddHandler(IServiceCollection services, Type handlerType, Type contract)
    {
        var existing = services.FirstOrDefault(descriptor => descriptor.ServiceType == contract && !descriptor.IsKeyedService);
        if (existing is null)
        {
            services.AddTransient(contract, handlerType);
            return;
        }

        // The same type found again, or a factory/instance registration made by hand: keep what is there.
        if (existing.ImplementationType is null || existing.ImplementationType == handlerType)
            return;

        throw new InvalidOperationException(
            $"'{contract.GetGenericArguments()[0].FullName}' has more than one handler: "
                + $"'{existing.ImplementationType.FullName}' and '{handlerType.FullName}'. A request has exactly one handler.");
    }
}

/// <summary>Enumerates the loadable types of the assemblies passed to the registration call.</summary>
internal static class ApplicationTypeScan
{
    /// <summary>Returns every type of <paramref name="assemblies"/> that loads, skipping those that do not.</summary>
    internal static IReadOnlyList<Type> LoadableTypes(IEnumerable<Assembly> assemblies)
    {
        var types = new List<Type>();
        foreach (var assembly in assemblies)
        {
            try
            {
                types.AddRange(assembly.GetTypes());
            }
            catch (ReflectionTypeLoadException exception)
            {
                types.AddRange(exception.Types.OfType<Type>());
            }
        }

        return types;
    }
}
