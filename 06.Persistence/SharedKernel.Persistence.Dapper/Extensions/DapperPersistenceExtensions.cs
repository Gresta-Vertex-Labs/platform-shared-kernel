using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Persistence.Dapper.Options;
using SharedKernel.Persistence.Dapper.TypeHandlers;

namespace SharedKernel.Persistence.Dapper.Extensions;

/// <summary>
/// DI extension methods for the SharedKernel Dapper persistence layer.
/// </summary>
#pragma warning disable RS0026 // Symbol has multiple public overloads with optional parameters.
// The parameterless-registration overload and the IConfiguration-bound overload differ in their
// second required parameter's presence/type (none vs. a mandatory IConfiguration) — a caller's own
// argument list already selects the correct overload; there is no shared call shape across the two
// for a trailing optional parameter to ever disambiguate incorrectly.
public static class DapperPersistenceExtensions
{
    /// <summary>
    /// Registers this platform's Dapper type handlers plus any handlers <paramref name="configure"/>
    /// adds, once per process, under a lock (see <see cref="DapperTypeHandlers.Apply"/>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional. Registers one or more caller-supplied
    /// <see cref="Dapper.SqlMapper.ITypeHandler"/> implementations, e.g.:
    /// <code>
    /// services.AddSharedKernelDapper(b =&gt; b
    ///     .AddTypeHandler&lt;OrderId, OrderIdTypeHandler&gt;()
    ///     .AddTypeHandler&lt;OrderStatus, OrderStatusTypeHandler&gt;());
    /// </code>
    /// </param>
    /// <param name="enableSnakeCaseMapping">
    /// See <see cref="DapperTypeHandlers.Apply"/>'s own remarks — sets Dapper's process-wide
    /// <see cref="global::Dapper.DefaultTypeMap.MatchNamesWithUnderscores"/>. Defaults to
    /// <see langword="true"/>.
    /// </param>
    /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
    /// <remarks>
    /// <c>IDbConnectionFactory</c> is NOT registered by this extension — it is registered by
    /// <c>AddSharedKernelNpgsql</c>. Consuming services must call
    /// both extensions at startup:
    /// <code>
    /// services.AddSharedKernelNpgsql(builder.Configuration);
    /// services.AddSharedKernelDapper(b =&gt; b.AddTypeHandler&lt;OrderId, OrderIdTypeHandler&gt;());
    /// </code>
    /// </remarks>
    public static IServiceCollection AddSharedKernelDapper(
        this IServiceCollection services,
        Action<DapperTypeHandlerBuilder>? configure = null,
        bool enableSnakeCaseMapping = true)
    {
        ArgumentNullException.ThrowIfNull(services);

        DapperTypeHandlers.Apply(configure, enableSnakeCaseMapping);

        return services;
    }

    /// <summary>
    /// Registers this platform's Dapper type handlers (see the primary overload) plus
    /// <see cref="DapperPersistenceOptions"/>, bound and validated from
    /// <see cref="DapperPersistenceOptions.SectionName"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// The root configuration to resolve <see cref="DapperPersistenceOptions.SectionName"/> against.
    /// </param>
    /// <param name="configure">See the primary overload.</param>
    /// <param name="enableSnakeCaseMapping">See the primary overload.</param>
    /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
    public static IServiceCollection AddSharedKernelDapper(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DapperTypeHandlerBuilder>? configure = null,
        bool enableSnakeCaseMapping = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<DapperPersistenceOptions>(configuration);
        DapperTypeHandlers.Apply(configure, enableSnakeCaseMapping);

        return services;
    }
}
#pragma warning restore RS0026
