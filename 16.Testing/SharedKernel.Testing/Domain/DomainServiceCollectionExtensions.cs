using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Testing.Domain;

/// <summary>
/// DI convenience extension registering <see cref="SharedKernel.Testing"/>'s domain-adjacent
/// test doubles.
/// </summary>
public static class DomainServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FakeClock"/> as the <see cref="IClock"/> singleton.
    /// </summary>
    /// <remarks>
    /// Other domain test helpers (assertion classes, faker bases) are static or directly
    /// instantiable — consistent with the <c>Security</c>/<c>Persistence</c>/<c>Clocks</c>
    /// convention of "DI registration only when swapping in for a production registration."
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddFakeDomainServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IClock, FakeClock>();

        return services;
    }
}
