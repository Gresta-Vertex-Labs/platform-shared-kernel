using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline;
using SharedKernel.Application.Streaming;

namespace SharedKernel.Application.Mediator.MediatR;

/// <summary>
/// Plugs MediatR in as the transport behind the kernel <see cref="ISender"/>.
/// </summary>
public static class MediatRServiceCollectionExtensions
{
    /// <summary>
    /// Sends the service's commands and queries through MediatR: registers the kernel <see cref="ISender"/> and, for
    /// every request and streaming-query handler of the assemblies passed to <c>AddSharedKernelApplication</c>, the
    /// MediatR handler that runs its <see cref="RequestPipeline{TRequest,TResponse}"/>.
    /// </summary>
    /// <param name="app">The application builder of <c>AddSharedKernelApplication</c>.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <c>services.AddSharedKernelApplication(typeof(Program).Assembly, app =&gt; app.UseMediatR().WithTransactions())</c>
    /// is the whole registration: the handlers, validators, behaviors and domain-event dispatch are registered by
    /// <c>AddSharedKernelApplication</c> itself, whichever mediator is used. MediatR is referenced by this package
    /// only; replacing it means another <see cref="ISender"/> implementation plugged in the same way, with no change to
    /// a command, query, handler or behavior.
    /// </para>
    /// <para>
    /// Uses reflection over the scanned assemblies at registration time only; a send resolves a dispatcher
    /// registered here and makes no reflective call. Safe to call more than once.
    /// </para>
    /// </remarks>
    public static ApplicationPipelineBuilder UseMediatR(this ApplicationPipelineBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var services = app.Services;

        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(global::MediatR.IMediator)))
            services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(MediatRSender).Assembly));

        services.TryAddTransient<ISender, MediatRSender>();

        foreach (var type in LoadableTypes(app))
        {
            if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
                continue;

            foreach (var contract in type.GetInterfaces())
            {
                if (!contract.IsGenericType)
                    continue;

                var definition = contract.GetGenericTypeDefinition();
                var arguments = contract.GetGenericArguments();

                if (definition == typeof(IRequestHandler<,>))
                    RegisterRequestRoute(services, arguments[0], arguments[1]);
                else if (definition == typeof(IStreamQueryHandler<,>))
                    RegisterStreamRoute(services, arguments[0], arguments[1]);
            }
        }

        return app;
    }

    private static void RegisterRequestRoute(IServiceCollection services, Type requestType, Type responseType)
    {
        services.TryAddTransient(
            typeof(global::MediatR.IRequestHandler<,>).MakeGenericType(
                typeof(RequestEnvelope<,>).MakeGenericType(requestType, responseType), responseType),
            typeof(RequestEnvelopeHandler<,>).MakeGenericType(requestType, responseType));

        services.TryAdd(ServiceDescriptor.KeyedSingleton(
            typeof(RequestDispatcher<>).MakeGenericType(responseType),
            requestType,
            typeof(RequestDispatcher<,>).MakeGenericType(requestType, responseType)));
    }

    private static void RegisterStreamRoute(IServiceCollection services, Type requestType, Type responseType)
    {
        services.TryAddTransient(
            typeof(global::MediatR.IStreamRequestHandler<,>).MakeGenericType(
                typeof(StreamEnvelope<,>).MakeGenericType(requestType, responseType), responseType),
            typeof(StreamEnvelopeHandler<,>).MakeGenericType(requestType, responseType));

        services.TryAdd(ServiceDescriptor.KeyedSingleton(
            typeof(StreamDispatcher<>).MakeGenericType(responseType),
            requestType,
            typeof(StreamDispatcher<,>).MakeGenericType(requestType, responseType)));
    }

    private static IEnumerable<Type> LoadableTypes(ApplicationPipelineBuilder app)
    {
        foreach (var assembly in app.Assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException exception)
            {
                types = [.. exception.Types.OfType<Type>()];
            }

            foreach (var type in types)
                yield return type;
        }
    }
}
