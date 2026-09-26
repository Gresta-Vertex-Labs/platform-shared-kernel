using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Application.Validation;

namespace SharedKernel.Validation.FluentValidation;

/// <summary>
/// DI registration bridging FluentValidation into the kernel request pipeline.
/// </summary>
public static class FluentValidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FluentValidationRequestValidator{TRequest}"/> as the open-generic
    /// <see cref="IRequestValidator{TRequest}"/>, so every FluentValidation <c>IValidator&lt;T&gt;</c>
    /// registered for a request runs in the pipeline's validation behavior, and registers the validators of
    /// <paramref name="assemblies"/>. Idempotent.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="assemblies">
    /// The assemblies whose FluentValidation validators to register (scoped, public and internal) — usually the same
    /// assemblies passed to <c>AddSharedKernelApplication</c>. None registers the bridge only.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, for chaining.</returns>
    /// <remarks>
    /// A request with no FluentValidation validator produces no errors from the bridge. Hand-written
    /// <see cref="IRequestValidator{TRequest}"/> implementations, which <c>AddSharedKernelApplication</c> registers from
    /// its own assemblies, run alongside it. A validator already registered for the same type is not registered twice.
    /// </remarks>
    public static IServiceCollection AddFluentValidationRequestValidators(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRequestValidator<>), typeof(FluentValidationRequestValidator<>)));

        if (assemblies.Length > 0)
        {
            foreach (var validator in AssemblyScanner.FindValidatorsInAssemblies(assemblies.Distinct(), includeInternalTypes: true))
                services.TryAddEnumerable(ServiceDescriptor.Scoped(validator.InterfaceType, validator.ValidatorType));
        }

        return services;
    }
}
