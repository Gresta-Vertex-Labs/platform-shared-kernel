using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Execution.Context;
using SharedKernel.Persistence.Abstractions.Connections;

namespace SharedKernel.MultiTenancy.Extensions;

/// <summary>
/// DI registration for <c>SharedKernel.MultiTenancy</c>.
/// </summary>
public static class MultiTenancyExtensions
{
    /// <summary>
    /// Registers <see cref="TenantResolutionOptions"/> (validated at startup — see remarks),
    /// <see cref="IRequestContextAccessor"/> (unless already registered), and the full
    /// <see cref="ITenantResolutionStrategy"/> set (<see cref="HeaderTenantResolutionStrategy"/>,
    /// <see cref="ClaimTenantResolutionStrategy"/>, <see cref="DatabaseTenantResolutionStrategy"/>),
    /// all scoped.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="TenantResolutionOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Does not register <see cref="TenantResolutionMiddleware"/> itself — that remains an
    /// explicit <c>app.UseMiddleware&lt;TenantResolutionMiddleware&gt;()</c> call by the consumer,
    /// placed after <c>UseAuthentication()</c>. Registering the services without wiring the
    /// middleware leaves the tenant resolved from nothing but the caller's credential, and no
    /// header or directory resolution — a silent failure mode by design, not a crash.
    /// </para>
    /// <para>
    /// <b>Startup-time validation:</b> <see cref="TenantResolutionOptions"/> is
    /// registered with <c>.ValidateOnStart()</c>, backed by <see cref="TenantResolutionOptionsValidator"/>
    /// — a genuine DI-aware cross-check against the real registered
    /// <see cref="ITenantResolutionStrategy"/> set. A host with an empty or DI-unmatched
    /// <see cref="TenantResolutionOptions.StrategyOrder"/> fails fast at <c>IHost.StartAsync()</c>
    /// with an <see cref="OptionsValidationException"/> — it never runs indefinitely with tenant
    /// resolution silently degraded to "never resolves a tenant."
    /// </para>
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

        optionsBuilder.ValidateOnStart();
        services.AddSingleton<IValidateOptions<TenantResolutionOptions>, TenantResolutionOptionsValidator>();

        services.TryAddSingleton<IRequestContextAccessor, RequestContextAccessor>();

        services.AddScoped<ITenantResolutionStrategy, HeaderTenantResolutionStrategy>();
        services.AddScoped<ITenantResolutionStrategy, ClaimTenantResolutionStrategy>();

        // The directory strategy needs a database (IDbConnectionFactory, from 06.Persistence); a service without one
        // omits "Database" from StrategyOrder. Without a connection factory the slot holds a placeholder, so the
        // container still builds and the other strategies work; the options validator fails startup only when the
        // configured order actually names "Database".
        services.AddScoped<ITenantResolutionStrategy>(sp =>
            sp.GetService<IDbConnectionFactory>() is { } connectionFactory
                ? new DatabaseTenantResolutionStrategy(connectionFactory)
                : new UnavailableDatabaseTenantResolutionStrategy());

        return services;
    }
}
