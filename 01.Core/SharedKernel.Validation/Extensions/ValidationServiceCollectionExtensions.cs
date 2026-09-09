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
    /// <remarks>
    /// <see cref="INationalIdValidator"/> is a genuine, intentional multi-implementation
    /// collection — <see cref="INationalIdValidatorRegistry"/> resolves every registered instance
    /// via <c>IServiceProvider.GetServices&lt;INationalIdValidator&gt;()</c>. This method therefore
    /// registers <typeparamref name="TValidator"/> via
    /// <c>TryAddEnumerable(ServiceDescriptor.Singleton&lt;INationalIdValidator, TValidator&gt;())</c>
    /// — never a plain <c>TryAddSingleton</c>, which would collapse to a single winner and silently
    /// drop every other country's validator. <c>TryAddEnumerable</c> still prevents the identical
    /// <c>(INationalIdValidator, TValidator)</c> pair from being registered twice (e.g. calling this
    /// method twice for the same <typeparamref name="TValidator"/>), while preserving the
    /// multi-country collection semantics for every distinct <typeparamref name="TValidator"/>.
    /// </remarks>
    public static IServiceCollection AddNationalIdValidator<TValidator>(this IServiceCollection services)
        where TValidator : class, INationalIdValidator
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<INationalIdValidator, TValidator>());

        return services;
    }
}
