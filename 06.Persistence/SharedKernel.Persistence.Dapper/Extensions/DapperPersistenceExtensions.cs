using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Persistence.Dapper.TypeHandlers;

namespace SharedKernel.Persistence.Dapper.Extensions;

/// <summary>
/// DI extension methods for the SharedKernel Dapper persistence layer.
/// </summary>
public static class DapperPersistenceExtensions
{
    /// <summary>
    /// Registers platform-wide Dapper type handlers by calling
    /// <see cref="DapperTypeHandlers.Register"/> (idempotent).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <c>IDbConnectionFactory</c> is NOT registered by this extension — it is registered by
    /// <c>AddSharedKernelPostgreSQL</c> in <c>SharedKernel.Persistence.PostgreSQL</c>.
    /// Consuming services must call both extensions at startup:
    /// <code>
    /// services.AddSharedKernelPostgreSQL(connectionString);
    /// services.AddSharedKernelDapper();
    /// </code>
    /// </para>
    /// <para>
    /// Service-specific type handlers (e.g., <c>OrderIdTypeHandler</c>) must be registered
    /// separately in the consuming service's composition root via
    /// <c>Dapper.SqlMapper.AddTypeHandler(new OrderIdTypeHandler())</c>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelDapper(this IServiceCollection services)
    {
        DapperTypeHandlers.Register();
        return services;
    }
}
