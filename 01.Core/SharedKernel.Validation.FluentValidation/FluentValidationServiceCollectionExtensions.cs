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
    /// registered for a request runs in <c>ValidationBehavior</c>. Idempotent.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, for chaining.</returns>
    /// <remarks>
    /// Registers the bridge only: the validators themselves are registered as usual, for example with
    /// FluentValidation's <c>AddValidatorsFromAssembly(...)</c>. A request with no FluentValidation
    /// validator produces no errors from the bridge. Hand-written <see cref="IRequestValidator{TRequest}"/>
    /// registrations run alongside it.
    /// </remarks>
    public static IServiceCollection AddFluentValidationRequestValidators(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRequestValidator<>), typeof(FluentValidationRequestValidator<>)));
        return services;
    }
}
