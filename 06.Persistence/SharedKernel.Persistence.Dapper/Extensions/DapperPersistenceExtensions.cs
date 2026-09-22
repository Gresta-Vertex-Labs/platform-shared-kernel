using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Application.Context;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Dapper;
using SharedKernel.Persistence.Dapper.Options;
using SharedKernel.Persistence.Dapper.Sessions;
using SharedKernel.Persistence.Dapper.TypeHandlers;

namespace SharedKernel.Persistence;

/// <summary>
/// DI extension methods for the SharedKernel Dapper persistence layer.
/// </summary>
#pragma warning disable RS0026 // Symbol has multiple public overloads with optional parameters.
// The overloads differ in their required second parameter (none vs. IConfiguration).
public static class DapperPersistenceExtensions
{
    /// <summary>
    /// Registers <see cref="IDbSessionFactory"/> and applies <see cref="DapperConfiguration"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Type handlers and name matching, e.g. <c>b =&gt; b.AddStronglyTypedId&lt;OrderId, Guid&gt;()</c>.</param>
    /// <returns>The same <paramref name="services"/>.</returns>
    /// <remarks>
    /// <para>
    /// Needs the connection registrations of <c>AddSharedKernelNpgsql(configuration, name)</c> (or <c>AddSharedKernelPostgres</c>). So that a
    /// Dapper-only service works without EF Core, this also registers — unless already registered — an
    /// anonymous <see cref="IRequestContext"/> (no tenant; register the real one, e.g.
    /// <c>AddSharedKernelRequestContext()</c>, in any order) and the default <see cref="ICrossTenantScope"/>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelDapper(
        this IServiceCollection services,
        Action<DapperConfigurationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<DapperPersistenceOptions>();
        return AddCore(services, configure);
    }

    /// <summary>
    /// Registers <see cref="IDbSessionFactory"/> (see the primary overload) with
    /// <see cref="DapperPersistenceOptions"/> bound and validated from <see cref="DapperPersistenceOptions.SectionName"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The root configuration.</param>
    /// <param name="configure">See the primary overload.</param>
    /// <returns>The same <paramref name="services"/>.</returns>
    public static IServiceCollection AddSharedKernelDapper(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DapperConfigurationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<DapperPersistenceOptions>(configuration);
        return AddCore(services, configure);
    }

    private static IServiceCollection AddCore(IServiceCollection services, Action<DapperConfigurationBuilder>? configure)
    {
        DapperConfiguration.Apply(configure);

        services.TryAddScoped<IDbSessionFactory, DbSessionFactory>();
        services.AddSharedKernelCrossTenantScope();

        return services;
    }
}
#pragma warning restore RS0026
