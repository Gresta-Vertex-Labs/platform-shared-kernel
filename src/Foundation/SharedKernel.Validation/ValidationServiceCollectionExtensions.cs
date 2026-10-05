using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SharedKernel.Validation;

/// <summary>Registers the national ID registry. Every other type in this package needs no registration.</summary>
public static class ValidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="NationalIdValidatorRegistry"/> built from the built-in
    /// validators plus every <see cref="INationalIdValidator"/> in the container. Calling it again
    /// has no effect.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddSharedKernelValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(sp => new NationalIdValidatorRegistry(sp.GetServices<INationalIdValidator>()));
        return services;
    }

    /// <summary>
    /// Adds a national ID validator for one more country. It replaces a built-in validator for the
    /// same country. Registering the same type twice has no effect.
    /// </summary>
    /// <typeparam name="TValidator">The validator type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddNationalIdValidator<TValidator>(this IServiceCollection services)
        where TValidator : class, INationalIdValidator
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<INationalIdValidator, TValidator>());
        return services;
    }
}
