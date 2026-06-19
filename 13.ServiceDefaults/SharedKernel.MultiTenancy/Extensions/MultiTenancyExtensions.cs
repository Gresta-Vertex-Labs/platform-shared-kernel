using Microsoft.Extensions.DependencyInjection;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.MultiTenancy.Extensions;

/// <summary>
/// DI registration for <c>SharedKernel.MultiTenancy</c>.
/// </summary>
public static class MultiTenancyExtensions
{
    /// <summary>
    /// Registers <see cref="TenantResolutionOptions"/>, <see cref="AmbientTenantProvider"/> as the
    /// scoped <see cref="ITenantProvider"/>, and the full <see cref="ITenantResolutionStrategy"/>
    /// set (<see cref="HeaderTenantResolutionStrategy"/>, <see cref="ClaimTenantResolutionStrategy"/>,
    /// <see cref="DatabaseTenantResolutionStrategy"/>), all scoped.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="TenantResolutionOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// Does not register <see cref="TenantResolutionMiddleware"/> itself — that remains an
    /// explicit <c>app.UseMiddleware&lt;TenantResolutionMiddleware&gt;()</c> call by the consumer,
    /// placed after <c>UseAuthentication()</c>. Registering the services without wiring the
    /// middleware leaves <see cref="AmbientTenantProvider.TenantId"/> permanently
    /// <see cref="Guid.Empty"/> — a silent (zero-rows) failure mode by design, not a crash.
    /// </remarks>
    public static IServiceCollection AddSharedKernelMultiTenancy(
        this IServiceCollection services,
        Action<TenantResolutionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<TenantResolutionOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.AddScoped<AmbientTenantProvider>();
        services.AddScoped<ITenantProvider>(sp => sp.GetRequiredService<AmbientTenantProvider>());

        services.AddScoped<ITenantResolutionStrategy, HeaderTenantResolutionStrategy>();
        services.AddScoped<ITenantResolutionStrategy, ClaimTenantResolutionStrategy>();
        services.AddScoped<ITenantResolutionStrategy, DatabaseTenantResolutionStrategy>();

        return services;
    }
}
