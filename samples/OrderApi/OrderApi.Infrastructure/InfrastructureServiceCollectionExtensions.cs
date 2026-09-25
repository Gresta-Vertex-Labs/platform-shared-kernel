using Microsoft.Extensions.DependencyInjection;
using OrderApi.Application;
using SharedKernel.Primitives.Health;
using SharedKernel.Validation.FluentValidation;

namespace OrderApi.Infrastructure;

/// <summary>Registers the adapters behind the application's ports.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the order store, its readiness probe, and the FluentValidation bridge that lets
    /// <c>ValidationBehavior</c> run the application's FluentValidation validators through the kernel's
    /// <c>IRequestValidator&lt;T&gt;</c> port.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddOrderInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
        services.AddReadinessProbe<OrderStoreReadinessProbe>();
        services.AddFluentValidationRequestValidators();
        return services;
    }
}
