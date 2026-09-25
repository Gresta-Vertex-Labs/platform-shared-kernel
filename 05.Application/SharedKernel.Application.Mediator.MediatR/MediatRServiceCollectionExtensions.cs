using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.Extensions;
using SharedKernel.Application.Streaming;

namespace SharedKernel.Application.Mediator.MediatR;

/// <summary>
/// DI registration for the MediatR-backed kernel <see cref="ISender"/>.
/// </summary>
public static class MediatRServiceCollectionExtensions
{
    /// <summary>
    /// Registers MediatR as the transport behind the kernel <see cref="ISender"/>, and every kernel
    /// handler declared in <paramref name="assemblies"/>.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="assemblies">The assemblies declaring the service's handlers.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="assemblies"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Two different handler types handle the same request type.</exception>
    /// <remarks>
    /// A handler already registered by hand for a request type — a factory or an instance, or the same
    /// type — is kept, so a test or a composition root can supply its own.
    /// </remarks>
    /// <remarks>
    /// <para>
    /// Discovers every non-abstract, non-generic class implementing <see cref="IRequestHandler{TRequest,TResponse}"/>
    /// (which every <c>ICommandHandler</c>/<c>IQueryHandler</c> is), <see cref="IStreamQueryHandler{TQuery,TResponse}"/>
    /// or <see cref="IDomainEventHandler{TDomainEvent}"/>, public or not. Request and stream handlers
    /// are registered transient, domain-event handlers scoped. For each request type it also registers
    /// a MediatR handler that runs <c>RequestPipeline&lt;TRequest, TResponse&gt;</c> — every behavior
    /// registered by <c>AddSharedKernelApplicationBehaviors()...Build()</c>, then the handler.
    /// </para>
    /// <para>
    /// Also registers <c>RequestPipeline&lt;,&gt;</c>/<c>StreamRequestPipeline&lt;,&gt;</c> and the native
    /// <c>DomainEventDispatcher</c> as <c>IDomainEventDispatcher</c>. It registers no behavior. Safe to call
    /// more than once, with the same or other assemblies.
    /// </para>
    /// <para>
    /// Uses reflection over the scanned assemblies at registration time only; a send resolves a
    /// dispatcher registered here and makes no reflective call.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelMediatR(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        if (assemblies.Length == 0)
            throw new ArgumentException("Pass at least one assembly that declares handlers.", nameof(assemblies));

        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(global::MediatR.IMediator)))
            services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(MediatRSender).Assembly));

        services.AddSharedKernelRequestPipeline();
        services.AddSharedKernelDomainEvents();
        services.TryAddTransient<ISender, MediatRSender>();

        foreach (var type in assemblies.Distinct().SelectMany(static assembly => assembly.DefinedTypes))
        {
            if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
                continue;

            foreach (var contract in type.ImplementedInterfaces)
            {
                if (!contract.IsGenericType)
                    continue;

                var definition = contract.GetGenericTypeDefinition();
                var arguments = contract.GetGenericArguments();

                if (definition == typeof(IRequestHandler<,>))
                    RegisterRequestHandler(services, type, contract, arguments[0], arguments[1]);
                else if (definition == typeof(IStreamQueryHandler<,>))
                    RegisterStreamHandler(services, type, contract, arguments[0], arguments[1]);
                else if (definition == typeof(IDomainEventHandler<>))
                    services.TryAddEnumerable(ServiceDescriptor.Scoped(contract, type));
            }
        }

        return services;
    }

    private static void RegisterRequestHandler(
        IServiceCollection services, Type handlerType, Type contract, Type requestType, Type responseType)
    {
        if (!TryAddHandler(services, handlerType, contract, requestType))
            return;

        services.TryAddTransient(
            typeof(global::MediatR.IRequestHandler<,>).MakeGenericType(
                typeof(RequestEnvelope<,>).MakeGenericType(requestType, responseType), responseType),
            typeof(RequestEnvelopeHandler<,>).MakeGenericType(requestType, responseType));

        services.TryAdd(ServiceDescriptor.KeyedSingleton(
            typeof(RequestDispatcher<>).MakeGenericType(responseType),
            requestType,
            typeof(RequestDispatcher<,>).MakeGenericType(requestType, responseType)));
    }

    private static void RegisterStreamHandler(
        IServiceCollection services, Type handlerType, Type contract, Type requestType, Type responseType)
    {
        if (!TryAddHandler(services, handlerType, contract, requestType))
            return;

        services.TryAddTransient(
            typeof(global::MediatR.IStreamRequestHandler<,>).MakeGenericType(
                typeof(StreamEnvelope<,>).MakeGenericType(requestType, responseType), responseType),
            typeof(StreamEnvelopeHandler<,>).MakeGenericType(requestType, responseType));

        services.TryAdd(ServiceDescriptor.KeyedSingleton(
            typeof(StreamDispatcher<>).MakeGenericType(responseType),
            requestType,
            typeof(StreamDispatcher<,>).MakeGenericType(requestType, responseType)));
    }

    private static bool TryAddHandler(IServiceCollection services, Type handlerType, Type contract, Type requestType)
    {
        var existing = services.FirstOrDefault(descriptor => descriptor.ServiceType == contract && !descriptor.IsKeyedService);
        if (existing is null)
        {
            services.AddTransient(contract, handlerType);
            return true;
        }

        // The same type found again, or a factory/instance registration made by hand: keep what is there.
        if (existing.ImplementationType is null || existing.ImplementationType == handlerType)
            return true;

        throw new InvalidOperationException(
            $"'{requestType.FullName}' has more than one handler: '{existing.ImplementationType.FullName}' "
                + $"and '{handlerType.FullName}'. A request has exactly one handler.");
    }
}
