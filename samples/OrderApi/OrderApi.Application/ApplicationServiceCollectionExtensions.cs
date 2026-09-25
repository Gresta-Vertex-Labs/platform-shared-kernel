using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace OrderApi.Application;

/// <summary>Registers what the application layer owns: its validators.</summary>
/// <remarks>
/// Handlers are not registered here. They are discovered by the mediator adapter the Api chooses
/// (<c>AddSharedKernelMediatR(typeof(PlaceOrderCommand).Assembly)</c>), so this project never learns which
/// mediator runs them.
/// </remarks>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Registers the order validators.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddOrderApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IValidator<PlaceOrderCommand>, PlaceOrderCommandValidator>();
        return services;
    }
}
