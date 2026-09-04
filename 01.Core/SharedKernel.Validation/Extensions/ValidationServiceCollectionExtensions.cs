using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Validation.NationalId;

namespace SharedKernel.Validation.Extensions;

/// <summary>
/// DI extension methods for registering <c>SharedKernel.Validation</c> services.
/// </summary>
public static class ValidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="INationalIdValidatorRegistry"/> as a singleton, pre-seeded with the
    /// built-in <see cref="TckNationalIdValidator"/> ("TR") plus every <see cref="INationalIdValidator"/>
    /// registered via <see cref="AddNationalIdValidator{TValidator}"/> (in either call order —
    /// resolution is deferred until the registry singleton is first constructed).
    /// </summary>
    /// <remarks>
    /// The format validators themselves (<c>IbanValidator</c>, <c>PanValidator</c>, etc.) are
    /// static classes and require no DI registration.
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/> for chaining with <see cref="AddNationalIdValidator{TValidator}"/>.</returns>
    public static IServiceCollection AddSharedKernelValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<INationalIdValidatorRegistry>(sp =>
        {
            var registry = new NationalIdValidatorRegistry();

            foreach (INationalIdValidator validator in sp.GetServices<INationalIdValidator>())
            {
                registry.Register(validator);
            }

            return registry;
        });

        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TValidator"/> as an additional <see cref="INationalIdValidator"/>
    /// — resolved into <see cref="INationalIdValidatorRegistry"/> when that singleton is constructed.
    /// </summary>
    /// <typeparam name="TValidator">The <see cref="INationalIdValidator"/> implementation to register.</typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/> for further chaining.</returns>
    public static IServiceCollection AddNationalIdValidator<TValidator>(this IServiceCollection services)
        where TValidator : class, INationalIdValidator
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<INationalIdValidator, TValidator>();

        return services;
    }
}
