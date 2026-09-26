using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Presentation;

/// <summary>Wraps the implementation registered for a service in a decorator, in place and with the same lifetime.</summary>
internal static class ServiceDecoration
{
    /// <summary>
    /// Replaces the last non-keyed registration of <typeparamref name="TService"/> with one that builds the same
    /// implementation and passes it to <paramref name="decorate"/>. Registrations added later still win, exactly as
    /// they would have over the original.
    /// </summary>
    /// <exception cref="InvalidOperationException">No non-keyed <typeparamref name="TService"/> is registered.</exception>
    public static void Decorate<TService>(IServiceCollection services, Func<IServiceProvider, TService, TService> decorate)
        where TService : class
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var original = services[index];
            if (original.ServiceType != typeof(TService) || original.IsKeyedService)
            {
                continue;
            }

            services[index] = ServiceDescriptor.Describe(
                typeof(TService),
                provider => decorate(provider, (TService)CreateInner(provider, original)),
                original.Lifetime);
            return;
        }

        throw new InvalidOperationException($"No {typeof(TService).Name} is registered to decorate.");
    }

    private static object CreateInner(IServiceProvider provider, ServiceDescriptor descriptor) =>
        descriptor.ImplementationInstance
        ?? descriptor.ImplementationFactory?.Invoke(provider)
        ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
}
