using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.Npgsql.Connections;

namespace SharedKernel.Persistence.EfCore.MultiTenancy;

/// <summary>
/// Cross-tenant work for an EF Core context whose tables are protected by row-level security.
/// </summary>
/// <remarks>
/// <para>
/// Row-level security limits the application database role to the bound tenant, whatever the application
/// asks for. Work across tenants therefore runs as a separate role — one with <c>BYPASSRLS</c>, or one
/// named by a role-specific policy (<c>EnableTenantRowLevelSecurity(..., crossTenantRole: ...)</c>) —
/// connected with its own credentials (<c>SharedKernel:Persistence:{name}:RowLevelSecurity:CrossTenantConnectionString</c>, <c>{name}</c> being the connection name).
/// </para>
/// <code>
/// using (crossTenantScope.Enter("monthly billing export"))
/// {
///     await using var context = await contextFactory.CreateDbContextAsync(ct);
///     context.Database.UseCrossTenantConnection();
///     var invoices = await context.Invoices.IgnoreQueryFilters().ToListAsync(ct);
/// }
/// </code>
/// </remarks>
public static class RowLevelSecurityDatabaseFacadeExtensions
{
    /// <summary>
    /// Moves this context onto a connection of the cross-tenant database role. Call it on a new context,
    /// before its first query, inside an active <see cref="ICrossTenantScope"/>, and dispose the context
    /// when the scope ends.
    /// </summary>
    /// <param name="database">The context's database facade.</param>
    /// <exception cref="InvalidOperationException">
    /// No cross-tenant scope is active, no cross-tenant data source is configured, the context is pooled, or
    /// its connection is already open.
    /// </exception>
    public static void UseCrossTenantConnection(this DatabaseFacade database)
    {
        ArgumentNullException.ThrowIfNull(database);

        var context = database.GetService<ICurrentDbContext>().Context;
        var coreOptions = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>();

        if (coreOptions?.MaxPoolSize is not null)
        {
            throw new InvalidOperationException(
                "A pooled context cannot switch to the cross-tenant connection: the switch would outlive this "
                    + "lease. Create a non-pooled context (IDbContextFactory) for cross-tenant work.");
        }

        var services = coreOptions?.ApplicationServiceProvider
            ?? throw new InvalidOperationException(
                "The context has no application service provider, so the cross-tenant data source cannot be resolved.");

        if ((context as SharedKernelDbContext)?.CrossTenantScope.IsActive != true)
            throw new InvalidOperationException("UseCrossTenantConnection requires an active cross-tenant scope.");

        var dataSource = services.GetKeyedService<NpgsqlDataSource>(NpgsqlDataSourceKeys.CrossTenant)
            ?? throw new InvalidOperationException(
                "No cross-tenant data source is configured. Set "
                    + "'SharedKernel:Persistence:{connection name}:RowLevelSecurity:CrossTenantConnectionString' to a role "
                    + "that is exempt from the tenant policy.");

        var current = database.GetDbConnection();
        if (current.State != System.Data.ConnectionState.Closed)
            throw new InvalidOperationException("UseCrossTenantConnection must be called before the context opens its connection.");

        var connection = dataSource.CreateConnection();
        RowLevelSecurityConnections.MarkCrossTenant(connection);
        database.SetDbConnection(connection, contextOwnsConnection: true);

        services.GetService<ILoggerFactory>()?.CreateLogger(typeof(RowLevelSecurityDatabaseFacadeExtensions))
            .CrossTenantConnectionUsed(context.GetType().Name);
    }
}

/// <summary>Connections opened on the cross-tenant role by <see cref="RowLevelSecurityDatabaseFacadeExtensions.UseCrossTenantConnection"/>.</summary>
internal static class RowLevelSecurityConnections
{
    private static readonly ConditionalWeakTable<DbConnection, object> CrossTenant = new();
    private static readonly object Marker = new();

    public static void MarkCrossTenant(DbConnection connection) => CrossTenant.AddOrUpdate(connection, Marker);

    public static bool IsCrossTenant(DbConnection connection) => CrossTenant.TryGetValue(connection, out _);
}
