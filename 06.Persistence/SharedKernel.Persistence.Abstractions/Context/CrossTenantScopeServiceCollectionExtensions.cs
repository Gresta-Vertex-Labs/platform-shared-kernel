using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Application.Context;

namespace SharedKernel.Persistence.Abstractions.Context;

/// <summary>Registers the default <see cref="ICrossTenantScope"/>.</summary>
public static class CrossTenantScopeServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CrossTenantScope"/> as the scoped <see cref="ICrossTenantScope"/> (unless one
    /// is already registered) and the fail-closed <see cref="AnonymousRequestContext"/> as
    /// <see cref="IRequestContext"/> when none is registered yet.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/>.</returns>
    /// <remarks>
    /// <c>AddSharedKernelPostgres</c> calls this; a service that uses only Dapper calls it itself. A real
    /// <see cref="IRequestContext"/> registered with <c>Add</c> (before or after this call) wins over the
    /// anonymous default, because the last registration is the one resolved.
    /// </remarks>
    public static IServiceCollection AddSharedKernelCrossTenantScope(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(sd => sd.ServiceType == typeof(IRequestContext)))
            services.AddSingleton<IRequestContext>(AnonymousRequestContext.Instance);

        services.TryAddScoped<ICrossTenantScope, CrossTenantScope>();
        return services;
    }
}
