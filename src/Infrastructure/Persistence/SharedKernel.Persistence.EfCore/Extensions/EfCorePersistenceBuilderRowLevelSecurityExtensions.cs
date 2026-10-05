using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Persistence.EfCore;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.Npgsql.Options;

namespace SharedKernel.Persistence;

/// <summary>
/// Opt-in PostgreSQL row-level security (RLS) for <see cref="EfCorePersistenceBuilder{TContext}"/>.
/// </summary>
internal static class EfCorePersistenceBuilderRowLevelSecurityExtensions
{
    /// <summary>
    /// Binds the caller's tenant to every command of <typeparamref name="TContext"/>, transaction-locally,
    /// so the tenant policies created by <c>EnableTenantRowLevelSecurity</c> enforce isolation in the
    /// database, independently of the application's own tenant filter.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="builder">The persistence builder.</param>
    /// <param name="coverageCheck">
    /// What the startup check does when a tenant table is not protected; <see langword="null"/> = fail, or warn in the
    /// Development environment.
    /// </param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    /// <para>
    /// Also switches on <c>SharedKernel:Persistence:{name}:RowLevelSecurity:Enabled</c>, which rejects
    /// <c>Multiplexing</c>/<c>No Reset On Close</c> connection strings, makes Dapper sessions bind the
    /// tenant too, and checks at startup that the application role is not a superuser, has no
    /// <c>BYPASSRLS</c> and owns no protected table.
    /// </para>
    /// <para>
    /// <strong>What it guards against.</strong> Application bugs — a query that forgets the tenant filter, an
    /// <c>IgnoreQueryFilters()</c>, raw SQL. It does not stop SQL injection: injected SQL runs as the
    /// application role and can bind any tenant id it likes. Keep parameterized SQL.
    /// </para>
    /// <para>
    /// Works behind PgBouncer in transaction mode: nothing is bound for the session. Cross-tenant work uses
    /// a separate role; see <see cref="RowLevelSecurityDatabaseFacadeExtensions.UseCrossTenantConnection"/>.
    /// </para>
    /// </remarks>
    public static EfCorePersistenceBuilder<TContext> WithRowLevelSecurity<TContext>(
        this EfCorePersistenceBuilder<TContext> builder,
        RowLevelSecurityCheckMode? coverageCheck = null)
        where TContext : SharedKernelDbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(sp =>
            new RowLevelSecurityCoverageCheck<TContext>(
                sp, coverageCheck, sp.GetService<Microsoft.Extensions.Logging.ILogger<RowLevelSecurityCoverageCheck<TContext>>>()));

        builder.Services.TryAddSingleton<RowLevelSecurityCommandInterceptor>();
        builder.Services.TryAddSingleton<RowLevelSecurityTransactionInterceptor>();
        builder.Services.TryAddSingleton<RowLevelSecuritySaveChangesInterceptor>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPersistenceOptionsExtension, RowLevelSecurityOptionsContributor>());
        builder.Services.PostConfigure<NpgsqlPersistenceOptions>(options => options.RowLevelSecurity.Enabled = true);

        return builder;
    }

    /// <summary>
    /// Whether <see cref="WithRowLevelSecurity{TContext}"/> was called on <paramref name="services"/> — i.e. the
    /// database itself enforces tenant isolation on every statement.
    /// </summary>
    internal static bool IsRowLevelSecurityEnabled(IServiceCollection services) =>
        services.Any(descriptor => descriptor.ImplementationType == typeof(RowLevelSecurityOptionsContributor));
}
