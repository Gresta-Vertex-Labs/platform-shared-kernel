using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.MultiTenancy;

namespace SharedKernel.Persistence;

/// <summary>
/// The one entry point of the SharedKernel PostgreSQL persistence stack.
/// </summary>
/// <remarks>
/// <code>
/// builder.AddSharedKernelPostgres&lt;OrderDbContext&gt;("orders", p =&gt; p
///     .UseMultiTenancy(rowLevelSecurity: true)
///     .MigrateOnStartup());
///
/// public sealed class OrderDbContext(DbContextOptions&lt;OrderDbContext&gt; o, PersistenceContextDependencies d)
///     : TenantedDbContext(o, d)
/// {
///     public DbSet&lt;Order&gt; Orders =&gt; Set&lt;Order&gt;();
/// }
/// </code>
/// <para>One call registers:</para>
/// <list type="bullet">
/// <item><description>the shared <c>NpgsqlDataSource</c> for <c>ConnectionStrings:{name}</c> (Aspire/Testcontainers
/// convention; further settings in <c>SharedKernel:Persistence:{name}</c>), validated at startup, TLS
/// <c>VerifyFull</c> unless the connection string says otherwise or the host is local;</description></item>
/// <item><description>EF Core on PostgreSQL: snake_case names, retry on transient failures (on by default),
/// <c>xmin</c> optimistic concurrency on every aggregate root, SQLSTATE classification (unique → 409, foreign key →
/// 400/409, ...), strongly-typed ids and <c>Money</c> mapped by convention;</description></item>
/// <item><description><c>TContext</c> (scoped), <c>IDbContextFactory&lt;TContext&gt;</c> (scoped,
/// attaches the scope's caller), <see cref="ICallerDbContextFactory{TContext}"/> (singleton, explicit caller),
/// <c>IUnitOfWork</c> (the first registered context), <c>IUnitOfWork&lt;TContext&gt;</c> and a keyed
/// <c>IUnitOfWork</c> per context type, the open-generic repositories, <c>ICrossTenantScope</c>, a fail-closed
/// anonymous <c>IRequestContext</c> when none is registered, and <c>IClock</c> when none is registered;</description></item>
/// <item><description>startup validation: the model is built and validated when the host starts, not at the first
/// query; a warning is logged when no <c>IDomainEventDispatcher</c> is registered.</description></item>
/// </list>
/// <para>
/// <strong>Several contexts</strong> call this once per context. Contexts with the same connection name share one
/// data source. Inject <c>IUnitOfWork&lt;TContext&gt;</c> (or the keyed <c>IUnitOfWork</c>) for all but the first.
/// When the contexts share an assembly, override <c>ShouldApplyConfiguration</c> so each applies only its own
/// entity configurations.
/// </para>
/// <para>
/// <strong>Read replicas:</strong> not routed by this package. List the replicas in the connection string
/// (Npgsql multi-host, <c>Target Session Attributes=prefer-standby</c>) and register a separate read context
/// with its own connection name.
/// </para>
/// </remarks>
#pragma warning disable RS0026 // The IHostApplicationBuilder and IServiceCollection overloads differ in their required receiver.
public static partial class PostgresPersistenceExtensions
{
    /// <summary>Registers <typeparamref name="TContext"/> on PostgreSQL with the platform defaults.</summary>
    /// <typeparam name="TContext">The context.</typeparam>
    /// <param name="builder">The host application builder.</param>
    /// <param name="connectionName">The connection name (<c>ConnectionStrings:{name}</c>).</param>
    /// <param name="configure">Optional settings.</param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    public static IHostApplicationBuilder AddSharedKernelPostgres<TContext>(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<EfCorePersistenceBuilder<TContext>>? configure = null)
        where TContext : SharedKernelDbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        Register(builder.Services, builder.Configuration, connectionName, configure);
        return builder;
    }

    /// <summary>Registers <typeparamref name="TContext"/> on PostgreSQL with the platform defaults.</summary>
    /// <typeparam name="TContext">The context.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration (<c>ConnectionStrings:{name}</c>, <c>SharedKernel:Persistence:{name}</c>).</param>
    /// <param name="connectionName">The connection name.</param>
    /// <param name="configure">Optional settings.</param>
    /// <returns>The same <paramref name="services"/>.</returns>
    public static IServiceCollection AddSharedKernelPostgres<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionName,
        Action<EfCorePersistenceBuilder<TContext>>? configure = null)
        where TContext : SharedKernelDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        Register(services, configuration, connectionName, configure);
        return services;
    }

    /// <summary>
    /// Declares <typeparamref name="TContext"/> multi-tenant: every <c>IHasTenant</c> entity is filtered by the
    /// caller's tenant and every write outside it is rejected (both are always on for a
    /// <see cref="TenantedDbContext"/>; this call makes the intent explicit and enables row-level security).
    /// </summary>
    /// <typeparam name="TContext">A <see cref="TenantedDbContext"/>.</typeparam>
    /// <param name="builder">The persistence builder.</param>
    /// <param name="rowLevelSecurity">
    /// Also bind the tenant into every PostgreSQL transaction so row-level-security policies enforce isolation in
    /// the database (defense in depth). Requires the policies to be created by a migration.
    /// </param>
    /// <param name="rowLevelSecurityCheck">
    /// With <paramref name="rowLevelSecurity"/>: what happens when, after the startup migrations, a tenant table of the model
    /// lacks forced row-level security or its tenant policy. Default: <see cref="RowLevelSecurityCheckMode.Fail"/>, or
    /// <see cref="RowLevelSecurityCheckMode.Warn"/> in the Development environment.
    /// </param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    public static EfCorePersistenceBuilder<TContext> UseMultiTenancy<TContext>(
        this EfCorePersistenceBuilder<TContext> builder,
        bool rowLevelSecurity = false,
        RowLevelSecurityCheckMode? rowLevelSecurityCheck = null)
        where TContext : TenantedDbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MultiTenancyRequested = true;
        if (rowLevelSecurity)
            builder.WithRowLevelSecurity(rowLevelSecurityCheck);

        return builder;
    }

    internal static void Register<TContext>(
        IServiceCollection services,
        IConfiguration? configuration,
        string connectionName,
        Action<EfCorePersistenceBuilder<TContext>>? configure)
        where TContext : SharedKernelDbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        var persistence = new EfCorePersistenceBuilder<TContext>(services, configuration, connectionName);
        configure?.Invoke(persistence);
        persistence.Register();
    }
}
#pragma warning restore RS0026

/// <summary>Options type whose startup validation checks a registered context (see <see cref="PersistenceStartupValidator{TContext}"/>).</summary>
/// <typeparam name="TContext">The registered context.</typeparam>
internal sealed class PersistenceStartupCheck<TContext>
    where TContext : SharedKernelDbContext;

/// <summary>
/// Runs when the host starts (<c>ValidateOnStart</c>): builds and validates <typeparamref name="TContext"/>'s model,
/// so a mapping error fails the deployment instead of the first request, and warns when no
/// <see cref="IDomainEventDispatcher"/> is registered.
/// </summary>
internal sealed class PersistenceStartupValidator<TContext>(IServiceProvider services, ILoggerFactory? loggerFactory = null)
    : IValidateOptions<PersistenceStartupCheck<TContext>>
    where TContext : SharedKernelDbContext
{
    public ValidateOptionsResult Validate(string? name, PersistenceStartupCheck<TContext> options)
    {
        var logger = (loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            .CreateLogger(typeof(TContext));

        if (services.GetService<IServiceProviderIsService>() is { } isService && !isService.IsService(typeof(IDomainEventDispatcher)))
            PersistenceContextLog.NoDomainEventDispatcherRegistered(logger, typeof(TContext).Name);

        try
        {
            var factory = services.GetRequiredService<ICallerDbContextFactory<TContext>>();
            using var context = factory.CreateDbContextAsync(Execution.Context.AnonymousRequestContext.Instance).GetAwaiter().GetResult();
            var entityTypeCount = context.Model.GetEntityTypes().Count();
            PersistenceContextLog.ModelValidated(logger, typeof(TContext).Name, entityTypeCount);
            return ValidateOptionsResult.Success;
        }
        catch (OptionsValidationException)
        {
            // An invalid options instance (e.g. no connection string) fails startup through its own validator,
            // with its own message; reporting it again as "the model is invalid" would only mislead.
            return ValidateOptionsResult.Skip;
        }
        catch (Exception ex)
        {
            return ValidateOptionsResult.Fail($"The EF Core model of '{typeof(TContext).Name}' is invalid: {ex.Message}");
        }
    }
}
